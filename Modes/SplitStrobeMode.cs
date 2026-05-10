using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class SplitStrobeMode : LightingModeBase<SplitStrobeModeSettings>
{
    private SplitStrobeState _state = new();

    public override string Id => ModeIds.SplitStrobe;
    public override string DisplayName => "Split Strobe";
    public override string Description => "Random bass/treble light split with independent color strobes and attack/decay envelopes.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderSplitStrobe(channels, frame, elapsedSeconds, settings.SplitStrobe, _state);

    public override void Reset() => _state = new SplitStrobeState();

    protected override SplitStrobeModeSettings GetSettings(AppSettings settings) => settings.SplitStrobe;
}

