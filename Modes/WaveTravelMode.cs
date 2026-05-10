using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;

namespace hui.Modes;

internal sealed class WaveTravelMode : LightingModeBase<WaveTravelModeSettings>
{
    private WaveTravelState _state = new();

    public override string Id => ModeIds.WaveTravel;
    public override string DisplayName => "Wave Travel";
    public override string Description => "Travelling waves launched by detected transients.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderWaveTravel(channels, frame, elapsedSeconds, settings.WaveTravel, _state);

    public override void Reset() => _state = new WaveTravelState();

    protected override WaveTravelModeSettings GetSettings(AppSettings settings) => settings.WaveTravel;
}

