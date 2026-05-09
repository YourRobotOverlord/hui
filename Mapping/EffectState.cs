namespace hui.Mapping;

internal sealed class WaveTravelState
{
    private bool _launchFromLeft = true;
    private double _lastLaunchTime = double.NegativeInfinity;

    public List<WavePulse> Waves { get; } = [];

    public bool CanLaunch(double elapsedSeconds)
    {
        return elapsedSeconds - _lastLaunchTime >= 0.18;
    }

    public void Launch(double elapsedSeconds, double strength, double hue)
    {
        _lastLaunchTime = elapsedSeconds;
        Waves.Add(new WavePulse(elapsedSeconds, _launchFromLeft ? 1 : -1, Math.Clamp(strength, 0.25, 1.0), hue));
        _launchFromLeft = !_launchFromLeft;
    }

    public void Trim(double elapsedSeconds, double waveSeconds)
    {
        var lifetime = Math.Max(waveSeconds, 0.1);
        Waves.RemoveAll(wave => elapsedSeconds - wave.LaunchTime > lifetime);
    }
}

internal sealed record WavePulse(double LaunchTime, int Direction, double Strength, double Hue);

internal sealed class BeatPulseState
{
    private double _lastLaunchTime = double.NegativeInfinity;

    public List<BeatPulse> Pulses { get; } = [];

    public bool CanLaunch(double elapsedSeconds)
    {
        return elapsedSeconds - _lastLaunchTime >= 0.16;
    }

    public void Launch(double elapsedSeconds, double strength, double hue)
    {
        _lastLaunchTime = elapsedSeconds;
        Pulses.Add(new BeatPulse(elapsedSeconds, Math.Clamp(strength, 0.3, 1.0), hue));
    }

    public void Trim(double elapsedSeconds)
    {
        Pulses.RemoveAll(pulse => elapsedSeconds - pulse.LaunchTime > 1.15);
    }
}

internal sealed record BeatPulse(double LaunchTime, double Strength, double Hue);
