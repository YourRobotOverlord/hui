using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class AudioReactiveMode : LightingModeBase<AudioReactiveModeSettings>
{
    public override string Id => ModeIds.AudioReactive;
    public override string DisplayName => "Audio Reactive";
    public override string Description => "Spectrum-driven hue wash with stereo and zone emphasis.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderAudioReactive(channels, frame, settings.AudioReactive);

    protected override AudioReactiveModeSettings GetSettings(AppSettings settings) => settings.AudioReactive;
}

