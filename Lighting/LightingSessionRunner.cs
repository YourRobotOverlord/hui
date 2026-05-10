using System.Diagnostics;
using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;
using hui.Modes;

namespace hui.Lighting;

internal sealed class LightingSessionRunner(
    HueBridgeClient bridgeClient,
    IAudioMonitorFactory audioMonitorFactory,
    LightingModeCatalog modeCatalog)
{
    public async Task RunAsync(
        Func<AppSettings> settingsProvider,
        CancellationToken cancellationToken,
        Action<EntertainmentArea, string>? onConnected = null,
        Func<SessionEndBehavior>? endBehaviorProvider = null,
        Action<ILightingMode>? onModeChanged = null)
    {
        var initial = settingsProvider().Normalize();
        Validate(initial);

        var area = await bridgeClient.GetEntertainmentAreaAsync(
                initial.Connection.Bridge,
                initial.Connection.AppKey,
                initial.Connection.Area,
                cancellationToken)
            .ConfigureAwait(false);

        using var monitor = audioMonitorFactory.Create(initial.Connection.DeviceIndex);
        using var streamer = new HueEntertainmentStreamer(
            initial.Connection.Bridge,
            initial.Connection.AppKey,
            initial.Connection.ClientKey,
            area.Id);

        onConnected?.Invoke(area, monitor.DeviceName);

        await bridgeClient.SetStreamingActionAsync(initial.Connection.Bridge, initial.Connection.AppKey, area.Id, "start", cancellationToken)
            .ConfigureAwait(false);

        ILightingMode? activeMode = null;

        try
        {
            streamer.Connect();
            monitor.Start();

            var stopwatch = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = settingsProvider();
                var mode = modeCatalog.GetCurrent(settings);

                if (!ReferenceEquals(mode, activeMode))
                {
                    activeMode?.Reset();
                    mode.Reset();
                    activeMode = mode;
                    onModeChanged?.Invoke(mode);
                }

                var frame = monitor.GetFrame();
                var colors = mode.Render(area.Channels, frame, stopwatch.Elapsed.TotalSeconds, settings);
                streamer.SendFrame(colors);
                await Task.Delay(TimeSpan.FromSeconds(1d / settings.Connection.Fps), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            activeMode?.Reset();

            try
            {
                SendEndFrame(streamer, area.Channels, endBehaviorProvider?.Invoke() ?? CreateExitBehavior(initial.Connection));
            }
            catch
            {
            }

            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await bridgeClient.SetStreamingActionAsync(initial.Connection.Bridge, initial.Connection.AppKey, area.Id, "stop", stopTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async Task ApplyExitBehaviorAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = settings.Normalize();
        Validate(normalized);

        var area = await bridgeClient.GetEntertainmentAreaAsync(
                normalized.Connection.Bridge,
                normalized.Connection.AppKey,
                normalized.Connection.Area,
                cancellationToken)
            .ConfigureAwait(false);

        using var streamer = new HueEntertainmentStreamer(
            normalized.Connection.Bridge,
            normalized.Connection.AppKey,
            normalized.Connection.ClientKey,
            area.Id);

        await bridgeClient.SetStreamingActionAsync(normalized.Connection.Bridge, normalized.Connection.AppKey, area.Id, "start", cancellationToken)
            .ConfigureAwait(false);

        try
        {
            streamer.Connect();
            SendEndFrame(streamer, area.Channels, CreateExitBehavior(normalized.Connection));
        }
        finally
        {
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await bridgeClient.SetStreamingActionAsync(normalized.Connection.Bridge, normalized.Connection.AppKey, area.Id, "stop", stopTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static void Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Connection.Bridge) ||
            string.IsNullOrWhiteSpace(settings.Connection.AppKey) ||
            string.IsNullOrWhiteSpace(settings.Connection.ClientKey) ||
            string.IsNullOrWhiteSpace(settings.Connection.Area))
        {
            throw new InvalidOperationException("Bridge, app key, client key, and area must all be configured before starting the lighting session.");
        }
    }

    private static SessionEndBehavior CreateExitBehavior(ConnectionSettings settings)
    {
        return settings.ExitLightingMode switch
        {
            AppExitLightingMode.Color => SessionEndBehavior.SolidColor(new RgbColor(
                settings.ExitColorRed / 255d,
                settings.ExitColorGreen / 255d,
                settings.ExitColorBlue / 255d)),
            _ => SessionEndBehavior.BlackoutFrame
        };
    }

    private static void SendEndFrame(HueEntertainmentStreamer streamer, IReadOnlyList<EntertainmentChannel> channels, SessionEndBehavior endBehavior)
    {
        if (endBehavior.IsBlackout)
        {
            streamer.SendBlackout(channels);
            return;
        }

        var result = new ChannelColor[channels.Count];
        for (var i = 0; i < channels.Count; i++)
        {
            result[i] = new ChannelColor((byte)channels[i].ChannelId, endBehavior.Color);
        }

        streamer.SendFrame(result);
    }
}

internal sealed record SessionEndBehavior(bool IsBlackout, RgbColor Color)
{
    public static SessionEndBehavior BlackoutFrame { get; } = new(true, RgbColor.Black);

    public static SessionEndBehavior SolidColor(RgbColor color) => new(false, color);
}

