namespace hui.Audio;

internal interface IAudioMonitor : IDisposable
{
    string DeviceName { get; }
    void Start();
    AudioFrame GetFrame();
}

internal interface IAudioMonitorFactory
{
    IReadOnlyList<AudioDeviceInfo> ListRenderDevices();
    IAudioMonitor Create(int? deviceIndex);
}

