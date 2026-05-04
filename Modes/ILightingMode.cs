using hui.Configuration;
using hui.Hue;
using hui.Audio;
using Terminal.Gui;

namespace hui.Modes;

internal interface ILightingMode
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings);
    Dialog CreateSettingsDialog(AppSettingsState state);
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
    public abstract Dialog CreateSettingsDialog(AppSettingsState state);

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

    protected static Label AddLabel(View container, string text, int y)
    {
        var label = new Label
        {
            X = 0,
            Y = y,
            Text = text,
            Width = 16,
            TextAlignment = Alignment.End
        };

        container.Add(label);
        return label;
    }

    protected static NumericUpDown<double> AddDoubleEditor(View container, string text, int y, double value, double increment = 0.1d, double? min = null, double? max = null)
    {
        var label = AddLabel(container, text, y);
        var editor = new NumericUpDown<double>
        {
            X = Pos.Right(label) + 1,
            Y = y,
            Width = 7,
            Value = value,
            Increment = increment,
            Format = "{0:0.##}"
        };
        ClampNumericEditor(editor, min, max);
        container.Add(editor);
        return editor;
    }

    protected static NumericUpDown<int> AddIntEditor(View container, string text, int y, int value, int increment = 1, int? min = null, int? max = null)
    {
        var label = AddLabel(container, text, y);
        var editor = new NumericUpDown<int>
        {
            X = Pos.Right(label) + 1,
            Y = y,
            Width = 7,
            Value = value,
            Increment = increment
        };
        ClampNumericEditor(editor, min, max);
        container.Add(editor);
        return editor;
    }

    protected static CheckBox AddCheckBox(View container, string text, int y, bool value)
    {
        var box = new CheckBox
        {
            X = 0,
            Y = y,
            Title = text,
            Value = value ? CheckState.Checked : CheckState.UnChecked
        };

        container.Add(box);
        return box;
    }

    private static void ClampNumericEditor(NumericUpDown<double> editor, double? min = null, double? max = null)
    {
        editor.ValueChanging += (_, args) =>
        {
            var newValue = args.NewValue;
            if (min.HasValue)
            {
                newValue = Math.Max(newValue, min.Value);
            }

            if (max.HasValue)
            {
                newValue = Math.Min(newValue, max.Value);
            }

            args.NewValue = newValue;
        };
    }

    private static void ClampNumericEditor(NumericUpDown<int> editor, int? min = null, int? max = null)
    {
        editor.ValueChanging += (_, args) =>
        {
            var newValue = args.NewValue;
            if (min.HasValue)
            {
                newValue = Math.Max(newValue, min.Value);
            }

            if (max.HasValue)
            {
                newValue = Math.Min(newValue, max.Value);
            }

            args.NewValue = newValue;
        };
    }
}

