namespace hui.Audio;

internal sealed class LoopbackAudioMonitorFactory : IAudioMonitorFactory
{
    public IReadOnlyList<AudioDeviceInfo> ListRenderDevices() => LoopbackAudioMonitor.ListRenderDevices();

    public IAudioMonitor Create(int? deviceIndex) => LoopbackAudioMonitor.Create(deviceIndex);
}

