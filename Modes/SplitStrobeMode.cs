using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;
using Terminal.Gui;

namespace hui.Modes;

internal sealed class SplitStrobeMode : LightingModeBase<SplitStrobeModeSettings>
{
    private SplitStrobeState _state = new();

    public override string Id => ModeIds.SplitStrobe;
    public override string DisplayName => "Split Strobe";
    public override string Description => "Random bass/treble light split with independent color strobes and attack/decay envelopes.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderSplitStrobe(channels, frame, elapsedSeconds, settings.SplitStrobe, _state);

    public override Dialog CreateSettingsDialog(AppSettingsState state)
    {
        var draft = state.Snapshot().SplitStrobe.Clone();
        var save = new Button { Text = "Save" };
        var cancel = new Button { Text = "Cancel" };
        var dialog = new Dialog { Title = $"{DisplayName} Settings", Buttons = [cancel, save] };

        var brightness = AddDoubleEditor(dialog, "Brightness:", 0, draft.Brightness, 0.05, min: 0.0, max: 1.0);
        var sensitivity = AddDoubleEditor(dialog, "Sensitivity:", 1, draft.Sensitivity, 0.05, min: 0.01);
        var background = AddDoubleEditor(dialog, "Background:", 2, draft.BackgroundLevel, 0.05, min: 0.0, max: 1.0);
        var attack = AddDoubleEditor(dialog, "Attack Sec:", 3, draft.AttackSeconds, 0.01, min: 0.01);
        var decay = AddDoubleEditor(dialog, "Decay Sec:", 4, draft.DecaySeconds, 0.01, min: 0.01);
        var bassColorButton = CreateColorButton(dialog, "Bass Color:", 5, CreateColor(draft.BassRed, draft.BassGreen, draft.BassBlue), color =>
        {
            draft.BassRed = color.R;
            draft.BassGreen = color.G;
            draft.BassBlue = color.B;
        });
        var trebleColorButton = CreateColorButton(dialog, "Treble Color:", 6, CreateColor(draft.TrebleRed, draft.TrebleGreen, draft.TrebleBlue), color =>
        {
            draft.TrebleRed = color.R;
            draft.TrebleGreen = color.G;
            draft.TrebleBlue = color.B;
        });

        save.Accepting += (_, _) =>
        {
            draft.Brightness = brightness.Value;
            draft.Sensitivity = sensitivity.Value;
            draft.BackgroundLevel = background.Value;
            draft.AttackSeconds = attack.Value;
            draft.DecaySeconds = decay.Value;
            state.Update(settings => settings.SplitStrobe = draft.Normalize());
        };

        dialog.Add(bassColorButton, trebleColorButton);
        return dialog;
    }

    public override void Reset() => _state = new SplitStrobeState();

    protected override SplitStrobeModeSettings GetSettings(AppSettings settings) => settings.SplitStrobe;

    private static Button CreateColorButton(View container, string labelText, int y, Color initialColor, Action<Color> onChanged)
    {
        var label = AddLabel(container, labelText, y);
        var currentColor = initialColor;
        var button = new Button
        {
            X = Pos.Right(label) + 1,
            Y = y,
            Width = 22,
            Text = FormatColorButtonText(currentColor)
        };
        button.Accepting += (_, e) =>
        {
            var app = button.App ?? throw new InvalidOperationException("Color dialog requires an active Terminal.Gui application.");
            using var dialog = new Dialog
            {
                Title = labelText.TrimEnd(':'),
                Buttons = [new Button { Text = "Cancel" }, new Button { Text = "Save", IsDefault = true }]
            };
            var cancel = dialog.Buttons[0];
            var save = dialog.Buttons[1];
            var picker = new ColorPicker
            {
                X = 0,
                Y = 0,
                Width = 36,
                Style = new ColorPickerStyle
                {
                    ColorModel = ColorModel.HSV,
                    ShowTextFields = true,
                    ShowColorName = false
                },
                SelectedColor = currentColor
            };
            picker.ApplyStyleChanges();
            dialog.Add(picker);
            save.Accepting += (_, saveArgs) =>
            {
                currentColor = picker.SelectedColor;
                onChanged(currentColor);
                button.Text = FormatColorButtonText(currentColor);
                saveArgs.Handled = true;
                dialog.RequestStop();
            };
            cancel.Accepting += (_, cancelArgs) =>
            {
                cancelArgs.Handled = true;
                dialog.RequestStop();
            };
            ((Button)save).SuperView?.SetFocus();
            app.Run(dialog);
            e.Handled = true;
        };
        return button;
    }

    private static string FormatColorButtonText(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color CreateColor(int red, int green, int blue) => new(red, green, blue);
}
