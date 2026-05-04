using hui.Configuration;
using hui.Modes;

namespace hui.Lighting;

internal sealed class LightingRuntimeService(
    LightingSessionRunner sessionRunner,
    AppSettingsState settingsState,
    LightingModeCatalog modeCatalog)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runCancellationSource;
    private Task? _runTask;
    private SessionEndBehavior _endBehavior = SessionEndBehavior.BlackoutFrame;

    public bool IsRunning { get; private set; }
    public string StatusMessage { get; private set; } = "Stopped";
    public string ActiveAreaName { get; private set; } = string.Empty;
    public string ActiveDeviceName { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var snapshot = settingsState.Snapshot();
            modeCatalog.GetCurrent(snapshot).Reset();

            _runCancellationSource = new CancellationTokenSource();
            _endBehavior = SessionEndBehavior.BlackoutFrame;
            IsRunning = true;
            StatusMessage = $"Starting {modeCatalog.GetCurrent(snapshot).DisplayName}...";
            _runTask = Task.Run(() => RunCoreAsync(_runCancellationSource.Token));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await StopAsync(SessionEndBehavior.BlackoutFrame).ConfigureAwait(false);
    }

    public async Task HandleAppExitAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        await StopAsync(CreateAppExitBehavior(settingsState.Snapshot().Connection)).ConfigureAwait(false);
    }

    private async Task StopAsync(SessionEndBehavior endBehavior)
    {
        Task? runTask;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                StatusMessage = "Stopped";
                return;
            }

            StatusMessage = "Stopping...";
            _endBehavior = endBehavior;
            _runCancellationSource?.Cancel();
            runTask = _runTask;
        }
        finally
        {
            _gate.Release();
        }

        if (runTask is not null)
        {
            await runTask.ConfigureAwait(false);
        }
    }

    public Task ToggleAsync() => IsRunning ? StopAsync() : StartAsync();

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await sessionRunner.RunAsync(
                    () => settingsState.Snapshot(),
                    cancellationToken,
                    (area, device) =>
                    {
                        ActiveAreaName = $"{area.Name} [{area.Id}]";
                        ActiveDeviceName = device;
                        StatusMessage = $"Running {modeCatalog.GetCurrent(settingsState.Snapshot()).DisplayName}";
                    },
                    () => _endBehavior)
                .ConfigureAwait(false);

            StatusMessage = "Stopped";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Error: {exception.Message}";
        }
        finally
        {
            IsRunning = false;
            _runCancellationSource?.Dispose();
            _runCancellationSource = null;
            _runTask = null;
            _endBehavior = SessionEndBehavior.BlackoutFrame;
        }
    }

    private static SessionEndBehavior CreateAppExitBehavior(ConnectionSettings settings)
    {
        return settings.ExitLightingMode switch
        {
            AppExitLightingMode.Color => SessionEndBehavior.SolidColor(new Hue.RgbColor(
                settings.ExitColorRed / 255d,
                settings.ExitColorGreen / 255d,
                settings.ExitColorBlue / 255d)),
            _ => SessionEndBehavior.BlackoutFrame
        };
    }
}

