using hui.Audio;
using hui.Cli;
using hui.Hue;

namespace hui.Mapping;

internal static class AudioReactiveMapper
{
    public static ChannelColor[] Map(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        var colors = options.Mapper switch
        {
            MapperKind.CycleStrobe => MapCycleStrobe(channels, frame, elapsedSeconds, options),
            MapperKind.Sparkle => MapSparkle(channels, frame, elapsedSeconds, options),
            MapperKind.WaveTravel => MapWaveTravel(channels, frame, elapsedSeconds, options),
            MapperKind.AmbientDrift => MapAmbientDrift(channels, frame, elapsedSeconds, options),
            MapperKind.BeatPulse => MapBeatPulse(channels, frame, elapsedSeconds, options),
            _ => MapAudioReactive(channels, frame, options)
        };

        return colors;
    }

    private static ChannelColor[] MapAudioReactive(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);

        var spectralHue = LerpAngle(options.WarmHue, options.CoolHue, frame.TrebleRatio);
        var brightnessBase = Math.Clamp(
            ((frame.OverallLevel * options.Sensitivity) + (frame.PeakLevel * 0.35) - 0.02) * options.Brightness,
            0,
            options.Brightness);
        var saturationBase = Math.Clamp(0.45 + (frame.BassRatio * 0.3) + (frame.PeakLevel * 0.2), 0.3, 1.0);

        var result = new ChannelColor[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var pan = channels.Count == 1
                ? 0.5
                : Math.Clamp(0.5 + ((channel.Position.X / maxAbsX) * 0.5), 0, 1);
            var height = Math.Clamp(0.5 + ((channel.Position.Y / maxAbsY) * 0.5), 0, 1);

            var stereoLevel = Lerp(frame.LeftLevel, frame.RightLevel, pan);
            var zoneEmphasis = (frame.BassRatio * (1 - height)) + (frame.TrebleRatio * height) + (frame.MidRatio * 0.35);
            var value = Math.Clamp(
                brightnessBase * (0.55 + (stereoLevel * 0.45)) * (0.75 + (zoneEmphasis * 0.25)),
                0,
                options.Brightness);

            var hue = LerpAngle(spectralHue - 35, spectralHue + 35, pan);
            var saturation = Math.Clamp(saturationBase + (Math.Abs(pan - 0.5) * 0.2), 0, 1);

            result[index] = new ChannelColor((byte)channel.ChannelId, HsvToRgb(hue, saturation, value));
        }

        return result;
    }

    private static ChannelColor[] MapBeatPulse(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);
        var threshold = GetBeatPulseThreshold(options.Sensitivity);
        var beatStrength = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var spectralHue = LerpAngle(options.WarmHue, options.CoolHue, frame.TrebleRatio);

        options.BeatState.Trim(elapsedSeconds);
        if (beatStrength > 0.04 && options.BeatState.CanLaunch(elapsedSeconds))
        {
            options.BeatState.Launch(elapsedSeconds, beatStrength, spectralHue);
        }

        var baseWash = Math.Clamp(
            ((frame.OverallLevel * options.Sensitivity * 0.1) + (frame.PeakLevel * 0.03)) * options.Brightness,
            0,
            options.Brightness * 0.14);

        var result = new ChannelColor[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var pan = channels.Count == 1
                ? 0.5
                : Math.Clamp(0.5 + ((channel.Position.X / maxAbsX) * 0.5), 0, 1);
            var height = Math.Clamp(0.5 + ((channel.Position.Y / maxAbsY) * 0.5), 0, 1);

            var baseHue = LerpAngle(spectralHue - 22, spectralHue + 22, pan);
            var baseColor = HsvToRgb(baseHue, 0.52, baseWash * (0.92 + (height * 0.12)));
            var strongestPulse = 0d;
            var pulseHue = spectralHue;

            foreach (var pulse in options.BeatState.Pulses)
            {
                var age = elapsedSeconds - pulse.LaunchTime;
                if (age < 0)
                {
                    continue;
                }

                var envelope = Math.Exp(-age / 0.34) * pulse.Strength;
                if (envelope > strongestPulse)
                {
                    strongestPulse = envelope;
                    pulseHue = pulse.Hue;
                }
            }

            if (strongestPulse > 0.001)
            {
                var channelHue = LerpAngle(pulseHue - 14, pulseHue + 14, pan);
                var pulseValue = Math.Clamp(strongestPulse * options.Brightness * (0.94 + (height * 0.08)), 0, options.Brightness);
                var pulseColor = HsvToRgb(channelHue, 1.0, pulseValue);
                baseColor = Blend(baseColor, pulseColor, Math.Clamp(strongestPulse, 0, 1));
            }

            result[index] = new ChannelColor((byte)channel.ChannelId, baseColor);
        }

        return result;
    }

    private static ChannelColor[] MapAmbientDrift(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);

        var speedFactor = 1 + Math.Clamp((frame.OverallLevel * options.Sensitivity * 0.55) + (frame.PeakLevel * 0.18), 0, 1.25);
        var driftTime = elapsedSeconds * speedFactor;
        var cycleLength = Math.Max(options.CycleSeconds, 0.1);
        var phase = (Math.Sin((driftTime / cycleLength) * Math.PI * 2) + 1) * 0.5;
        var baseHue = Lerp(options.WarmHue, options.CoolHue, phase);
        var baseBrightness = Math.Clamp(
            (options.Brightness * 0.18) +
            (frame.OverallLevel * options.Sensitivity * options.Brightness * 0.22) +
            (frame.PeakLevel * options.Brightness * 0.08),
            0,
            options.Brightness * 0.55);

        var result = new ChannelColor[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var pan = channels.Count == 1
                ? 0.5
                : Math.Clamp(0.5 + ((channel.Position.X / maxAbsX) * 0.5), 0, 1);
            var height = Math.Clamp(0.5 + ((channel.Position.Y / maxAbsY) * 0.5), 0, 1);

            var hue = Lerp(baseHue - 18, baseHue + 18, pan);
            var saturation = Math.Clamp(0.42 + (frame.TrebleRatio * 0.18) + (Math.Abs(pan - 0.5) * 0.08), 0.28, 0.75);
            var value = Math.Clamp(baseBrightness * (0.9 + (height * 0.15)), 0, options.Brightness);

            result[index] = new ChannelColor((byte)channel.ChannelId, HsvToRgb(hue, saturation, value));
        }

        return result;
    }

    private static ChannelColor[] MapWaveTravel(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var threshold = GetWaveThreshold(options.Sensitivity);
        var launchStrength = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var spectralHue = LerpAngle(options.WarmHue, options.CoolHue, frame.TrebleRatio);

        options.WaveState.Trim(elapsedSeconds, options.WaveSeconds);
        if (launchStrength > 0.05 && options.WaveState.CanLaunch(elapsedSeconds))
        {
            options.WaveState.Launch(elapsedSeconds, launchStrength, spectralHue);
        }

        var baseWash = Math.Clamp(
            ((frame.OverallLevel * options.Sensitivity * 0.2) + (frame.PeakLevel * 0.06)) * options.Brightness,
            0,
            options.Brightness * 0.25);

        var result = new ChannelColor[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var x = channels.Count == 1
                ? 0.5
                : Math.Clamp(0.5 + ((channel.Position.X / maxAbsX) * 0.5), 0, 1);

            var color = HsvToRgb(spectralHue, 0.55, baseWash);
            var peakWave = 0d;
            double peakHue = spectralHue;

            foreach (var wave in options.WaveState.Waves)
            {
                var phase = (elapsedSeconds - wave.LaunchTime) / Math.Max(options.WaveSeconds, 0.1);
                if (phase < 0 || phase > 1)
                {
                    continue;
                }

                var center = wave.Direction > 0 ? phase : 1 - phase;
                var distance = Math.Abs(x - center);
                var influence = Math.Exp(-Math.Pow(distance / 0.16, 2)) * wave.Strength;
                if (influence > peakWave)
                {
                    peakWave = influence;
                    peakHue = wave.Hue;
                }
            }

            if (peakWave > 0.001)
            {
                var waveValue = Math.Clamp(peakWave * options.Brightness, 0, options.Brightness);
                var waveColor = HsvToRgb(peakHue, 1.0, waveValue);
                color = Blend(color, waveColor, Math.Clamp(peakWave, 0, 1));
            }

            result[index] = new ChannelColor((byte)channel.ChannelId, color);
        }

        return result;
    }

    private static ChannelColor[] MapCycleStrobe(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var cycleLength = Math.Max(options.CycleSeconds, 0.1);
        var cyclePhase = (Math.Sin((elapsedSeconds / cycleLength) * Math.PI * 2) + 1) * 0.5;
        var hue = Lerp(options.WarmHue, options.CoolHue, cyclePhase);
        var threshold = GetCycleStrobeThreshold(options.Sensitivity);
        var strobeLevel = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var value = Math.Clamp(
            Math.Pow(strobeLevel, 0.9) * options.Brightness,
            0,
            options.Brightness);

        if (value <= 0.001)
        {
            return channels.Select(channel => new ChannelColor((byte)channel.ChannelId, RgbColor.Black)).ToArray();
        }

        var color = HsvToRgb(hue, 1.0, value);
        return channels.Select(channel => new ChannelColor((byte)channel.ChannelId, color)).ToArray();
    }

    private static ChannelColor[] MapSparkle(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AudioReactiveOptions options)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);
        var spectralHue = LerpAngle(options.WarmHue, options.CoolHue, frame.TrebleRatio);
        var baseWash = Math.Clamp(
            ((frame.OverallLevel * options.Sensitivity * 0.28) + (frame.PeakLevel * 0.08)) * options.Brightness,
            0,
            options.Brightness * 0.35);

        var transientThreshold = GetSparkleThreshold(options.Sensitivity);
        var sparkleGate = NormalizeAboveThreshold(frame.TransientLevel, transientThreshold);
        var sparkleWindow = (int)Math.Floor(elapsedSeconds * 14);

        var result = new ChannelColor[channels.Count];
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var pan = channels.Count == 1
                ? 0.5
                : Math.Clamp(0.5 + ((channel.Position.X / maxAbsX) * 0.5), 0, 1);
            var height = Math.Clamp(0.5 + ((channel.Position.Y / maxAbsY) * 0.5), 0, 1);

            var baseHue = LerpAngle(spectralHue - 28, spectralHue + 28, pan);
            var baseColor = HsvToRgb(baseHue, 0.7, baseWash * (0.8 + (height * 0.2)));

            var sparkleChance = 0.08 + (sparkleGate * 0.72);
            var sparkleSeed = HashToUnit(channel.ChannelId, sparkleWindow);
            var sparkleAmount = sparkleSeed < sparkleChance
                ? Math.Clamp((1 - (sparkleSeed / Math.Max(sparkleChance, 0.0001))) * sparkleGate, 0, 1)
                : 0;

            var finalColor = sparkleAmount > 0
                ? Blend(baseColor, new RgbColor(1, 1, 1), sparkleAmount)
                : baseColor;

            result[index] = new ChannelColor((byte)channel.ChannelId, finalColor);
        }

        return result;
    }

    private static double Lerp(double start, double end, double amount)
    {
        return start + ((end - start) * amount);
    }

    private static double GetCycleStrobeThreshold(double sensitivity)
    {
        return Math.Clamp(0.5 / Math.Max(sensitivity, 0.01), 0.05, 0.95);
    }

    private static double GetSparkleThreshold(double sensitivity)
    {
        return Math.Clamp(0.58 / Math.Max(sensitivity, 0.01), 0.08, 0.97);
    }

    private static double GetWaveThreshold(double sensitivity)
    {
        return Math.Clamp(0.54 / Math.Max(sensitivity, 0.01), 0.06, 0.96);
    }

    private static double GetBeatPulseThreshold(double sensitivity)
    {
        return Math.Clamp(0.52 / Math.Max(sensitivity, 0.01), 0.06, 0.96);
    }

    private static double NormalizeAboveThreshold(double value, double threshold)
    {
        if (value <= threshold)
        {
            return 0;
        }

        return Math.Clamp((value - threshold) / Math.Max(1 - threshold, 0.0001), 0, 1);
    }

    private static double HashToUnit(int channelId, int sparkleWindow)
    {
        var hash = HashCode.Combine(channelId, sparkleWindow, 0x5f3759df);
        return (uint)hash / (double)uint.MaxValue;
    }

    private static double LerpAngle(double start, double end, double amount)
    {
        var delta = ((end - start + 540) % 360) - 180;
        return (start + (delta * amount) + 360) % 360;
    }

    private static RgbColor HsvToRgb(double hue, double saturation, double value)
    {
        if (value <= 0)
        {
            return RgbColor.Black;
        }

        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var secondary = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var match = value - chroma;

        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };

        return new RgbColor(red + match, green + match, blue + match);
    }

    private static RgbColor Blend(RgbColor left, RgbColor right, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new RgbColor(
            Lerp(left.R, right.R, amount),
            Lerp(left.G, right.G, amount),
            Lerp(left.B, right.B, amount));
    }

}

internal sealed record AudioReactiveOptions(
    MapperKind Mapper,
    double CycleSeconds,
    double WaveSeconds,
    double Sensitivity,
    double Brightness,
    double WarmHue,
    double CoolHue)
{
    public WaveTravelState WaveState { get; } = new();
    public BeatPulseState BeatState { get; } = new();
}

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

