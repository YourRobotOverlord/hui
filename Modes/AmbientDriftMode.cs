using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class AmbientDriftMode : LightingModeBase<AmbientDriftModeSettings>
{
    public override string Id => ModeIds.AmbientDrift;
    public override string DisplayName => "Ambient Drift";
    public override string Description => "Slow hue drift with gentle audio-reactive motion.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderAmbientDrift(channels, frame, elapsedSeconds, settings.AmbientDrift);

    protected override AmbientDriftModeSettings GetSettings(AppSettings settings) => settings.AmbientDrift;
}

