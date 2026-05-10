using hui.Configuration;
using hui.Hue;
using hui.Audio;

namespace hui.Modes;

internal interface ILightingMode
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings);
    double GetBrightness(AppSettings settings);
    double GetSensitivity(AppSettings settings);
    void AdjustBrightness(AppSettings settings, double delta);
    void AdjustSensitivity(AppSettings settings, double delta);
    void Reset();
}

internal abstract class LightingModeBase<TSettings> : ILightingMode where TSettings : ModeSettingsBase
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    public abstract ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings);

    public virtual void Reset()
    {
    }

    public double GetBrightness(AppSettings settings) => GetSettings(settings).Brightness;

    public double GetSensitivity(AppSettings settings) => GetSettings(settings).Sensitivity;

    public void AdjustBrightness(AppSettings settings, double delta)
    {
        var modeSettings = GetSettings(settings);
        modeSettings.Brightness = Math.Clamp(modeSettings.Brightness + delta, 0.0, 1.0);
    }

    public void AdjustSensitivity(AppSettings settings, double delta)
    {
        var modeSettings = GetSettings(settings);
        modeSettings.Sensitivity = Math.Max(modeSettings.Sensitivity + delta, 0.01);
    }

    protected abstract TSettings GetSettings(AppSettings settings);
}

