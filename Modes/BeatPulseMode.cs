using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class BeatPulseMode : LightingModeBase<BeatPulseModeSettings>
{
    private BeatPulseState _state = new();

    public override string Id => ModeIds.BeatPulse;
    public override string DisplayName => "Beat Pulse";
    public override string Description => "Full-area beat pulses with fast attack and smooth decay.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderBeatPulse(channels, frame, elapsedSeconds, settings.BeatPulse, _state);

    public override void Reset() => _state = new BeatPulseState();

    protected override BeatPulseModeSettings GetSettings(AppSettings settings) => settings.BeatPulse;
}

