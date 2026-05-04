using hui.Audio;
using hui.Configuration;
using hui.Hue;

namespace hui.Mapping;

internal static class LightingModeRenderer
{
    public static ChannelColor[] RenderAudioReactive(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        AudioReactiveModeSettings settings)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);

        var spectralHue = LerpAngle(settings.WarmHue, settings.CoolHue, frame.TrebleRatio);
        var brightnessBase = Math.Clamp(
            ((frame.OverallLevel * settings.Sensitivity) + (frame.PeakLevel * 0.35) - 0.02) * settings.Brightness,
            0,
            settings.Brightness);
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
                settings.Brightness);

            var hue = LerpAngle(spectralHue - 35, spectralHue + 35, pan);
            var saturation = Math.Clamp(saturationBase + (Math.Abs(pan - 0.5) * 0.2), 0, 1);

            result[index] = new ChannelColor((byte)channel.ChannelId, HsvToRgb(hue, saturation, value));
        }

        return result;
    }

    public static ChannelColor[] RenderCycleStrobe(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        CycleStrobeModeSettings settings)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var cycleLength = Math.Max(settings.CycleSeconds, 0.1);
        var cyclePhase = (Math.Sin((elapsedSeconds / cycleLength) * Math.PI * 2) + 1) * 0.5;
        var hue = Lerp(settings.WarmHue, settings.CoolHue, cyclePhase);
        var threshold = Math.Clamp(0.5 / Math.Max(settings.Sensitivity, 0.01), 0.05, 0.95);
        var strobeLevel = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var value = Math.Clamp(Math.Pow(strobeLevel, 0.9) * settings.Brightness, 0, settings.Brightness);

        if (value <= 0.001)
        {
            return channels.Select(channel => new ChannelColor((byte)channel.ChannelId, RgbColor.Black)).ToArray();
        }

        var color = HsvToRgb(hue, 1.0, value);
        return channels.Select(channel => new ChannelColor((byte)channel.ChannelId, color)).ToArray();
    }

    public static ChannelColor[] RenderSparkle(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        SparkleModeSettings settings)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);
        var spectralHue = LerpAngle(settings.WarmHue, settings.CoolHue, frame.TrebleRatio);
        var baseWash = Math.Clamp(
            ((frame.OverallLevel * settings.Sensitivity * 0.28) + (frame.PeakLevel * 0.08)) * settings.Brightness,
            0,
            settings.Brightness * 0.35);

        var transientThreshold = Math.Clamp(0.58 / Math.Max(settings.Sensitivity, 0.01), 0.08, 0.97);
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

            result[index] = new ChannelColor(
                (byte)channel.ChannelId,
                sparkleAmount > 0
                    ? Blend(baseColor, GetSparkleColor(settings), sparkleAmount)
                    : baseColor);
        }

        return result;
    }

    public static ChannelColor[] RenderWaveTravel(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        WaveTravelModeSettings settings,
        WaveTravelState state)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var threshold = Math.Clamp(0.54 / Math.Max(settings.Sensitivity, 0.01), 0.06, 0.96);
        var launchStrength = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var spectralHue = LerpAngle(settings.WarmHue, settings.CoolHue, frame.TrebleRatio);

        state.Trim(elapsedSeconds, settings.WaveSeconds);
        if (launchStrength > 0.05 && state.CanLaunch(elapsedSeconds))
        {
            state.Launch(elapsedSeconds, launchStrength, spectralHue);
        }

        var baseWash = Math.Clamp(
            ((frame.OverallLevel * settings.Sensitivity * 0.2) + (frame.PeakLevel * 0.06)) * settings.Brightness,
            0,
            settings.Brightness * 0.25);

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

            foreach (var wave in state.Waves)
            {
                var phase = (elapsedSeconds - wave.LaunchTime) / Math.Max(settings.WaveSeconds, 0.1);
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
                var waveValue = Math.Clamp(peakWave * settings.Brightness, 0, settings.Brightness);
                var waveColor = HsvToRgb(peakHue, 1.0, waveValue);
                color = Blend(color, waveColor, Math.Clamp(peakWave, 0, 1));
            }

            result[index] = new ChannelColor((byte)channel.ChannelId, color);
        }

        return result;
    }

    public static ChannelColor[] RenderAmbientDrift(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        AmbientDriftModeSettings settings)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);
        var speedFactor = 1 + Math.Clamp((frame.OverallLevel * settings.Sensitivity * 0.55) + (frame.PeakLevel * 0.18), 0, 1.25);
        var driftTime = elapsedSeconds * speedFactor;
        var cycleLength = Math.Max(settings.CycleSeconds, 0.1);
        var phase = (Math.Sin((driftTime / cycleLength) * Math.PI * 2) + 1) * 0.5;
        var baseHue = Lerp(settings.WarmHue, settings.CoolHue, phase);
        var baseBrightness = Math.Clamp(
            (settings.Brightness * 0.18) +
            (frame.OverallLevel * settings.Sensitivity * settings.Brightness * 0.22) +
            (frame.PeakLevel * settings.Brightness * 0.08),
            0,
            settings.Brightness * 0.55);

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
            var value = Math.Clamp(baseBrightness * (0.9 + (height * 0.15)), 0, settings.Brightness);

            result[index] = new ChannelColor((byte)channel.ChannelId, HsvToRgb(hue, saturation, value));
        }

        return result;
    }

    public static ChannelColor[] RenderBeatPulse(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        BeatPulseModeSettings settings,
        BeatPulseState state)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        var maxAbsX = Math.Max(channels.Max(channel => Math.Abs(channel.Position.X)), 0.001d);
        var maxAbsY = Math.Max(channels.Max(channel => Math.Abs(channel.Position.Y)), 0.001d);
        var threshold = Math.Clamp(0.52 / Math.Max(settings.Sensitivity, 0.01), 0.06, 0.96);
        var beatStrength = NormalizeAboveThreshold(frame.TransientLevel, threshold);
        var spectralHue = LerpAngle(settings.WarmHue, settings.CoolHue, frame.TrebleRatio);

        state.Trim(elapsedSeconds);
        if (beatStrength > 0.04 && state.CanLaunch(elapsedSeconds))
        {
            state.Launch(elapsedSeconds, beatStrength, spectralHue);
        }

        var baseWash = Math.Clamp(
            ((frame.OverallLevel * settings.Sensitivity * 0.1) + (frame.PeakLevel * 0.03)) * settings.Brightness,
            0,
            settings.Brightness * 0.14);

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

            foreach (var pulse in state.Pulses)
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
                var pulseValue = Math.Clamp(strongestPulse * settings.Brightness * (0.94 + (height * 0.08)), 0, settings.Brightness);
                var pulseColor = HsvToRgb(channelHue, 1.0, pulseValue);
                baseColor = Blend(baseColor, pulseColor, Math.Clamp(strongestPulse, 0, 1));
            }

            result[index] = new ChannelColor((byte)channel.ChannelId, baseColor);
        }

        return result;
    }

    public static ChannelColor[] RenderSplitStrobe(
        IReadOnlyList<EntertainmentChannel> channels,
        AudioFrame frame,
        double elapsedSeconds,
        SplitStrobeModeSettings settings,
        SplitStrobeState state)
    {
        if (channels.Count == 0)
        {
            return [];
        }

        state.EnsureAssignments(channels);
        state.UpdateLevels(frame, elapsedSeconds, settings);

        var bassColor = ScaleColor(GetSplitStrobeBassColor(settings), state.BassLevel);
        var trebleColor = ScaleColor(GetSplitStrobeTrebleColor(settings), state.TrebleLevel);

        return channels.Select(channel =>
                new ChannelColor(
                    (byte)channel.ChannelId,
                    state.IsBassChannel(channel.ChannelId) ? bassColor : trebleColor))
            .ToArray();
    }

    private static double Lerp(double start, double end, double amount) => start + ((end - start) * amount);

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

    private static RgbColor ScaleColor(RgbColor color, double level)
    {
        level = Math.Clamp(level, 0, 1);
        return new RgbColor(color.R * level, color.G * level, color.B * level);
    }

    private static RgbColor GetSparkleColor(SparkleModeSettings settings)
    {
        return new RgbColor(
            settings.SparkleRed / 255d,
            settings.SparkleGreen / 255d,
            settings.SparkleBlue / 255d);
    }

    private static RgbColor GetSplitStrobeBassColor(SplitStrobeModeSettings settings)
    {
        return new RgbColor(
            settings.BassRed / 255d,
            settings.BassGreen / 255d,
            settings.BassBlue / 255d);
    }

    private static RgbColor GetSplitStrobeTrebleColor(SplitStrobeModeSettings settings)
    {
        return new RgbColor(
            settings.TrebleRed / 255d,
            settings.TrebleGreen / 255d,
            settings.TrebleBlue / 255d);
    }
}

