using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class SparkleMode : LightingModeBase<SparkleModeSettings>
{
    public override string Id => ModeIds.Sparkle;
    public override string DisplayName => "Sparkle";
    public override string Description => "Dim wash plus transient-triggered sparkles.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderSparkle(channels, frame, elapsedSeconds, settings.Sparkle);

    protected override SparkleModeSettings GetSettings(AppSettings settings) => settings.Sparkle;
}

