namespace hui.Configuration;

internal sealed class AppSettings
{
    public ConnectionSettings Connection { get; set; } = new();
    public string CurrentModeId { get; set; } = Modes.ModeIds.AudioReactive;
    public AudioReactiveModeSettings AudioReactive { get; set; } = new();
    public CycleStrobeModeSettings CycleStrobe { get; set; } = new();
    public SparkleModeSettings Sparkle { get; set; } = new();
    public WaveTravelModeSettings WaveTravel { get; set; } = new();
    public AmbientDriftModeSettings AmbientDrift { get; set; } = new() { CycleSeconds = 14 };
    public BeatPulseModeSettings BeatPulse { get; set; } = new();
    public SplitStrobeModeSettings SplitStrobe { get; set; } = new();

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Connection = Connection.Clone(),
            CurrentModeId = CurrentModeId,
            AudioReactive = AudioReactive.Clone(),
            CycleStrobe = CycleStrobe.Clone(),
            Sparkle = Sparkle.Clone(),
            WaveTravel = WaveTravel.Clone(),
            AmbientDrift = AmbientDrift.Clone(),
            BeatPulse = BeatPulse.Clone(),
            SplitStrobe = SplitStrobe.Clone()
        }.Normalize();
    }

    public AppSettings Normalize()
    {
        Connection = Connection.Normalize();
        CurrentModeId = string.IsNullOrWhiteSpace(CurrentModeId) ? Modes.ModeIds.AudioReactive : CurrentModeId.Trim();
        AudioReactive = AudioReactive.Normalize();
        CycleStrobe = CycleStrobe.Normalize();
        Sparkle = Sparkle.Normalize();
        WaveTravel = WaveTravel.Normalize();
        AmbientDrift = AmbientDrift.Normalize();
        BeatPulse = BeatPulse.Normalize();
        SplitStrobe = SplitStrobe.Normalize();
        return this;
    }
}

internal sealed class ConnectionSettings
{
    public string Bridge { get; set; } = string.Empty;
    public string AppKey { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public int? DeviceIndex { get; set; }
    public int Fps { get; set; } = 30;
    public AppExitLightingMode ExitLightingMode { get; set; } = AppExitLightingMode.Blackout;
    public int ExitColorRed { get; set; } = 255;
    public int ExitColorGreen { get; set; } = 255;
    public int ExitColorBlue { get; set; } = 255;

    public ConnectionSettings Clone()
    {
        return new ConnectionSettings
        {
            Bridge = Bridge,
            AppKey = AppKey,
            ClientKey = ClientKey,
            Area = Area,
            DeviceIndex = DeviceIndex,
            Fps = Fps,
            ExitLightingMode = ExitLightingMode,
            ExitColorRed = ExitColorRed,
            ExitColorGreen = ExitColorGreen,
            ExitColorBlue = ExitColorBlue
        }.Normalize();
    }

    public ConnectionSettings Normalize()
    {
        Bridge = (Bridge ?? string.Empty).Trim();
        AppKey = (AppKey ?? string.Empty).Trim();
        ClientKey = (ClientKey ?? string.Empty).Trim();
        Area = (Area ?? string.Empty).Trim();
        Fps = Math.Clamp(Fps, 1, 60);
        DeviceIndex = DeviceIndex is < 0 ? null : DeviceIndex;
        ExitLightingMode = Enum.IsDefined(ExitLightingMode) ? ExitLightingMode : AppExitLightingMode.Blackout;
        ExitColorRed = Math.Clamp(ExitColorRed, 0, 255);
        ExitColorGreen = Math.Clamp(ExitColorGreen, 0, 255);
        ExitColorBlue = Math.Clamp(ExitColorBlue, 0, 255);
        return this;
    }
}

internal enum AppExitLightingMode
{
    Blackout = 0,
    Color = 1
}

internal abstract class ModeSettingsBase
{
    public double Brightness { get; set; } = 1.0;
    public double Sensitivity { get; set; } = 1.75;
    public double WarmHue { get; set; } = 18.0;
    public double CoolHue { get; set; } = 220.0;

    protected void NormalizeCommon()
    {
        Brightness = Math.Clamp(Brightness, 0.0, 1.0);
        Sensitivity = Math.Max(Sensitivity, 0.01);
        WarmHue = ClampHue(WarmHue);
        CoolHue = ClampHue(CoolHue);
    }

    protected static double ClampHue(double value) => Math.Clamp(value, 0.0, 360.0);
}

internal sealed class AudioReactiveModeSettings : ModeSettingsBase
{
    public AudioReactiveModeSettings Clone()
    {
        return new AudioReactiveModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue
        }.Normalize();
    }

    public AudioReactiveModeSettings Normalize()
    {
        NormalizeCommon();
        return this;
    }
}

internal sealed class CycleStrobeModeSettings : ModeSettingsBase
{
    public double CycleSeconds { get; set; } = 6.0;

    public CycleStrobeModeSettings Clone()
    {
        return new CycleStrobeModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue,
            CycleSeconds = CycleSeconds
        }.Normalize();
    }

    public CycleStrobeModeSettings Normalize()
    {
        NormalizeCommon();
        CycleSeconds = Math.Max(CycleSeconds, 0.1);
        return this;
    }
}

internal sealed class SparkleModeSettings : ModeSettingsBase
{
    public int SparkleRed { get; set; } = 255;
    public int SparkleGreen { get; set; } = 255;
    public int SparkleBlue { get; set; } = 255;

    public SparkleModeSettings Clone()
    {
        return new SparkleModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue,
            SparkleRed = SparkleRed,
            SparkleGreen = SparkleGreen,
            SparkleBlue = SparkleBlue
        }.Normalize();
    }

    public SparkleModeSettings Normalize()
    {
        NormalizeCommon();
        SparkleRed = Math.Clamp(SparkleRed, 0, 255);
        SparkleGreen = Math.Clamp(SparkleGreen, 0, 255);
        SparkleBlue = Math.Clamp(SparkleBlue, 0, 255);
        return this;
    }
}

internal sealed class WaveTravelModeSettings : ModeSettingsBase
{
    public double WaveSeconds { get; set; } = 1.6;

    public WaveTravelModeSettings Clone()
    {
        return new WaveTravelModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue,
            WaveSeconds = WaveSeconds
        }.Normalize();
    }

    public WaveTravelModeSettings Normalize()
    {
        NormalizeCommon();
        WaveSeconds = Math.Max(WaveSeconds, 0.1);
        return this;
    }
}

internal sealed class AmbientDriftModeSettings : ModeSettingsBase
{
    public double CycleSeconds { get; set; } = 14.0;

    public AmbientDriftModeSettings Clone()
    {
        return new AmbientDriftModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue,
            CycleSeconds = CycleSeconds
        }.Normalize();
    }

    public AmbientDriftModeSettings Normalize()
    {
        NormalizeCommon();
        CycleSeconds = Math.Max(CycleSeconds, 0.1);
        return this;
    }
}

internal sealed class BeatPulseModeSettings : ModeSettingsBase
{
    public BeatPulseModeSettings Clone()
    {
        return new BeatPulseModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue
        }.Normalize();
    }

    public BeatPulseModeSettings Normalize()
    {
        NormalizeCommon();
        return this;
    }
}

internal sealed class SplitStrobeModeSettings : ModeSettingsBase
{
    public double BackgroundLevel { get; set; } = 0.08;
    public double AttackSeconds { get; set; } = 0.08;
    public double DecaySeconds { get; set; } = 0.35;
    public int BassRed { get; set; } = 255;
    public int BassGreen { get; set; } = 96;
    public int BassBlue { get; set; } = 96;
    public int TrebleRed { get; set; } = 64;
    public int TrebleGreen { get; set; } = 160;
    public int TrebleBlue { get; set; } = 255;

    public SplitStrobeModeSettings Clone()
    {
        return new SplitStrobeModeSettings
        {
            Brightness = Brightness,
            Sensitivity = Sensitivity,
            WarmHue = WarmHue,
            CoolHue = CoolHue,
            BackgroundLevel = BackgroundLevel,
            AttackSeconds = AttackSeconds,
            DecaySeconds = DecaySeconds,
            BassRed = BassRed,
            BassGreen = BassGreen,
            BassBlue = BassBlue,
            TrebleRed = TrebleRed,
            TrebleGreen = TrebleGreen,
            TrebleBlue = TrebleBlue
        }.Normalize();
    }

    public SplitStrobeModeSettings Normalize()
    {
        NormalizeCommon();
        BackgroundLevel = Math.Clamp(BackgroundLevel, 0.0, 1.0);
        AttackSeconds = Math.Max(AttackSeconds, 0.01);
        DecaySeconds = Math.Max(DecaySeconds, 0.01);
        BassRed = Math.Clamp(BassRed, 0, 255);
        BassGreen = Math.Clamp(BassGreen, 0, 255);
        BassBlue = Math.Clamp(BassBlue, 0, 255);
        TrebleRed = Math.Clamp(TrebleRed, 0, 255);
        TrebleGreen = Math.Clamp(TrebleGreen, 0, 255);
        TrebleBlue = Math.Clamp(TrebleBlue, 0, 255);
        BackgroundLevel = Math.Min(BackgroundLevel, Brightness);
        return this;
    }
}

