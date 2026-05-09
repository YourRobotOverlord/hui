using hui.Audio;
using hui.Cli;
using hui.Configuration;
using hui.Hue;
using hui.Lighting;
using hui.Modes;
using hui.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace hui;

internal static class App
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, _) => cancellationSource.Cancel();

        try
        {
            return await RunCoreAsync(args, cancellationSource.Token).ConfigureAwait(false);
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            CommandLine.PrintUsage();
            return 1;
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCoreAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = CommandLine.Parse(args);
        using var host = BuildHost();
        var bridgeClient = host.Services.GetRequiredService<HueBridgeClient>();

        switch (command)
        {
            case HelpCommand:
                CommandLine.PrintUsage();
                return 0;

            case UiCommand:
                Button.DefaultShadow = ShadowStyles.None;
                return await host.Services.GetRequiredService<HuiUi>().RunAsync(cancellationToken).ConfigureAwait(false);

            case PairCommand pair:
                return await RunPairAsync(
                        bridgeClient,
                        host.Services.GetRequiredService<AppSettingsState>(),
                        pair,
                        cancellationToken)
                    .ConfigureAwait(false);

            case ListAreasCommand listAreas:
                return await RunListAreasAsync(bridgeClient, listAreas, cancellationToken).ConfigureAwait(false);

            case ListDevicesCommand:
                return RunListDevices(host.Services.GetRequiredService<IAudioMonitorFactory>());

            case RunCommand run:
                return await RunStreamingAsync(
                        host.Services.GetRequiredService<LightingSessionRunner>(),
                        host.Services.GetRequiredService<AppSettingsState>(),
                        run,
                        cancellationToken)
                    .ConfigureAwait(false);

            default:
                throw new CommandLineException("Unsupported command.");
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        var settingsPath = AppSettingsPathResolver.GetSettingsPath();

        builder.Configuration.AddJsonFile("appsettings.json", optional: true);
        builder.Configuration.AddJsonFile(settingsPath, optional: true);

        builder.Services.AddSingleton<HueBridgeClient>();
        builder.Services.AddSingleton<IAudioMonitorFactory, LoopbackAudioMonitorFactory>();
        builder.Services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        builder.Services.AddSingleton<AppSettingsState>();
        builder.Services.AddSingleton<ILightingMode, AudioReactiveMode>();
        builder.Services.AddSingleton<ILightingMode, CycleStrobeMode>();
        builder.Services.AddSingleton<ILightingMode, SparkleMode>();
        builder.Services.AddSingleton<ILightingMode, WaveTravelMode>();
        builder.Services.AddSingleton<ILightingMode, AmbientDriftMode>();
        builder.Services.AddSingleton<ILightingMode, BeatPulseMode>();
        builder.Services.AddSingleton<ILightingMode, SplitStrobeMode>();
        builder.Services.AddSingleton<LightingModeCatalog>();
        builder.Services.AddSingleton<LightingSessionRunner>();
        builder.Services.AddSingleton<LightingRuntimeService>();
        builder.Services.AddSingleton<HuiUi>();

        return builder.Build();
    }

    private static async Task<int> RunPairAsync(
        HueBridgeClient bridgeClient,
        AppSettingsState settingsState,
        PairCommand command,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Press Hue bridge link button, then wait few seconds.");
        var result = await bridgeClient.PairAsync(command.Bridge, command.AppName, command.InstanceName, cancellationToken).ConfigureAwait(false);
        SavePairing(settingsState, command.Bridge, result);

        Console.WriteLine();
        Console.WriteLine($"Bridge:     {command.Bridge}");
        Console.WriteLine($"App key:    {result.AppKey}");
        Console.WriteLine($"Client key: {result.ClientKey}");
        Console.WriteLine($"Saved to:   {AppSettingsPathResolver.GetSettingsPath()}");
        return 0;
    }

    private static void SavePairing(AppSettingsState settingsState, string bridge, PairingResult result)
    {
        settingsState.Update(settings =>
        {
            settings.Connection.Bridge = bridge;
            settings.Connection.AppKey = result.AppKey;
            settings.Connection.ClientKey = result.ClientKey;
        });
    }

    private static async Task<int> RunListAreasAsync(HueBridgeClient bridgeClient, ListAreasCommand command, CancellationToken cancellationToken)
    {
        var areas = await bridgeClient.GetEntertainmentAreasAsync(command.Bridge, command.AppKey, cancellationToken).ConfigureAwait(false);
        if (areas.Count == 0)
        {
            Console.WriteLine("No entertainment areas found.");
            return 0;
        }

        foreach (var area in areas.OrderBy(area => area.Name, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{area.Name} [{area.Id}] status={area.Status} channels={area.Channels.Count}");
            foreach (var channel in area.Channels.OrderBy(channel => channel.ChannelId))
            {
                Console.WriteLine(
                    $"  ch {channel.ChannelId,2}  x={channel.Position.X,6:F2}  y={channel.Position.Y,6:F2}  z={channel.Position.Z,6:F2}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int RunListDevices(IAudioMonitorFactory audioMonitorFactory)
    {
        var devices = audioMonitorFactory.ListRenderDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No active render devices found.");
            return 0;
        }

        foreach (var device in devices)
        {
            var marker = device.IsDefault ? " (default)" : string.Empty;
            Console.WriteLine($"[{device.Index}] {device.Name}{marker}");
            Console.WriteLine($"     {device.Id}");
        }

        return 0;
    }

    private static async Task<int> RunStreamingAsync(LightingSessionRunner sessionRunner, AppSettingsState settingsState, RunCommand command, CancellationToken cancellationToken)
    {
        var settings = CreateRunSettings(command, settingsState.Snapshot());
        Console.WriteLine($"Mode:   {settings.CurrentModeId}");
        Console.WriteLine($"FPS:    {settings.Connection.Fps}");
        Console.WriteLine("Streaming. Press Ctrl+C to stop.");

        await sessionRunner.RunAsync(
                () => settings.Clone(),
                cancellationToken,
                (area, device) =>
                {
                    Console.WriteLine($"Area:   {area.Name} [{area.Id}]");
                    Console.WriteLine($"Device: {device}");
                })
            .ConfigureAwait(false);

        return 0;
    }

    private static AppSettings CreateRunSettings(RunCommand command, AppSettings persisted)
    {
        var settings = persisted;

        if (!string.IsNullOrWhiteSpace(command.Bridge)) settings.Connection.Bridge = command.Bridge;
        if (!string.IsNullOrWhiteSpace(command.AppKey)) settings.Connection.AppKey = command.AppKey;
        if (!string.IsNullOrWhiteSpace(command.ClientKey)) settings.Connection.ClientKey = command.ClientKey;
        if (!string.IsNullOrWhiteSpace(command.Area)) settings.Connection.Area = command.Area;
        if (command.DeviceIndex.HasValue) settings.Connection.DeviceIndex = command.DeviceIndex;

        if (string.IsNullOrWhiteSpace(settings.Connection.Bridge))
            throw new CommandLineException("Missing required option --bridge.");
        if (string.IsNullOrWhiteSpace(settings.Connection.AppKey))
            throw new CommandLineException("Missing required option --app-key.");
        if (string.IsNullOrWhiteSpace(settings.Connection.ClientKey))
            throw new CommandLineException("Missing required option --client-key.");
        if (string.IsNullOrWhiteSpace(settings.Connection.Area))
            throw new CommandLineException("Missing required option --area.");

        settings.Connection.Fps = command.Fps;
        settings.CurrentModeId = command.Mapper switch
        {
            MapperKind.CycleStrobe => ModeIds.CycleStrobe,
            MapperKind.Sparkle => ModeIds.Sparkle,
            MapperKind.WaveTravel => ModeIds.WaveTravel,
            MapperKind.AmbientDrift => ModeIds.AmbientDrift,
            MapperKind.BeatPulse => ModeIds.BeatPulse,
            _ => ModeIds.AudioReactive
        };

        Apply(settings.AudioReactive);
        Apply(settings.CycleStrobe);
        Apply(settings.Sparkle);
        Apply(settings.WaveTravel);
        Apply(settings.AmbientDrift);
        Apply(settings.BeatPulse);

        return settings.Normalize();

        void Apply(ModeSettingsBase s)
        {
            s.Brightness = command.Brightness;
            s.Sensitivity = command.Sensitivity;
        }
    }
}


