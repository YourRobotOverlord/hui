using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class CycleStrobeMode : LightingModeBase<CycleStrobeModeSettings>
{
    public override string Id => ModeIds.CycleStrobe;
    public override string DisplayName => "Cycle Strobe";
    public override string Description => "Cycles between two hues and strobes on transients.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderCycleStrobe(channels, frame, elapsedSeconds, settings.CycleStrobe);

    protected override CycleStrobeModeSettings GetSettings(AppSettings settings) => settings.CycleStrobe;
}

