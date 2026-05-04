using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;
using Terminal.Gui;

namespace hui.Modes;

internal sealed class BeatPulseMode : LightingModeBase<BeatPulseModeSettings>
{
    private BeatPulseState _state = new();

    public override string Id => ModeIds.BeatPulse;
    public override string DisplayName => "Beat Pulse";
    public override string Description => "Full-area beat pulses with fast attack and smooth decay.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderBeatPulse(channels, frame, elapsedSeconds, settings.BeatPulse, _state);

    public override Dialog CreateSettingsDialog(AppSettingsState state)
    {
        var draft = state.Snapshot().BeatPulse.Clone();
        var save = new Button { Text = "Save" };
        var cancel = new Button { Text = "Cancel" };
        var dialog = new Dialog { Title = $"{DisplayName} Settings", Buttons = [cancel, save] };

        var brightness = AddDoubleEditor(dialog, "Brightness:", 0, draft.Brightness, 0.05, min: 0.0, max: 1.0);
        var sensitivity = AddDoubleEditor(dialog, "Sensitivity:", 1, draft.Sensitivity, 0.05, min: 0.01);
        var warmHue = AddDoubleEditor(dialog, "Warm Hue:", 2, draft.WarmHue, 1, min: 0.0, max: 360.0);
        var coolHue = AddDoubleEditor(dialog, "Cool Hue:", 3, draft.CoolHue, 1, min: 0.0, max: 360.0);

        save.Accepting += (_, _) =>
        {
            draft.Brightness = brightness.Value;
            draft.Sensitivity = sensitivity.Value;
            draft.WarmHue = warmHue.Value;
            draft.CoolHue = coolHue.Value;
            state.Update(settings => settings.BeatPulse = draft.Normalize());
        };

        return dialog;
    }

    public override void Reset() => _state = new BeatPulseState();

    protected override BeatPulseModeSettings GetSettings(AppSettings settings) => settings.BeatPulse;
}

