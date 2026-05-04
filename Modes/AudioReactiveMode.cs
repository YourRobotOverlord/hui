using hui.Audio;
using hui.Configuration;
using hui.Hue;
using hui.Mapping;
using Terminal.Gui;

namespace hui.Modes;

internal sealed class AudioReactiveMode : LightingModeBase<AudioReactiveModeSettings>
{
    public override string Id => ModeIds.AudioReactive;
    public override string DisplayName => "Audio Reactive";
    public override string Description => "Spectrum-driven hue wash with stereo and zone emphasis.";

    public override ChannelColor[] Render(IReadOnlyList<EntertainmentChannel> channels, AudioFrame frame, double elapsedSeconds, AppSettings settings)
        => LightingModeRenderer.RenderAudioReactive(channels, frame, settings.AudioReactive);

    public override Dialog CreateSettingsDialog(AppSettingsState state)
    {
        var draft = state.Snapshot().AudioReactive.Clone();
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
            state.Update(settings => settings.AudioReactive = draft.Normalize());
        };

        return dialog;
    }

    protected override AudioReactiveModeSettings GetSettings(AppSettings settings) => settings.AudioReactive;
}

