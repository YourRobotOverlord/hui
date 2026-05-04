using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using hui.Audio;
using hui.Configuration;
using hui.Lighting;
using hui.Modes;
using Terminal.Gui;

namespace hui.Ui;

internal sealed class HuiUi(
    AppSettingsState settingsState,
    LightingRuntimeService runtimeService,
    LightingModeCatalog modeCatalog,
    IAudioMonitorFactory audioMonitorFactory)
{
    private const int SettingsLabelWidth = 16;
    private const int SettingsControlX = 18;
    private const int SettingsControlWidth = 22;
    private const int NumericControlWidth = 7;

    private ListView _modeList = null!;
    private View _settingsFrame = null!;
    private Label _connectionLabel = null!;
    private Label _modeLabel = null!;
    private Label _brightnessLabel = null!;
    private Label _sensitivityLabel = null!;
    private Label _statusLabel = null!;
    private Label _runtimeLabel = null!;
    private IApplication _app = null!;
    private string? _renderedModeId;

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using IApplication app = Application.Create();
        app.Init();
        _app = app;

        using var window = new Window
        {
            Title = "Hue Lighting Audio Sync",
            BorderStyle = LineStyle.None
        };

        BuildLayout(window);
        RefreshModeSelection();
        RefreshStatus();

        app.AddTimeout(TimeSpan.FromMilliseconds(250), () =>
        {
            RefreshStatus();
            return !cancellationToken.IsCancellationRequested;
        });

        cancellationToken.Register(app.RequestStop);
        try
        {
            app.Run(window);
        }
        finally
        {
            runtimeService.HandleAppExitAsync().GetAwaiter().GetResult();
        }

        return Task.FromResult(0);
    }

    private void BuildLayout(Window window)
    {
        var modesFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = 28,
            Height = Dim.Fill(1),
            Title = "Modes"
        };

        _modeList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            Source = new ListWrapper<string>(new ObservableCollection<string>(modeCatalog.Modes.Select(mode => mode.DisplayName).ToList()))
        };
        _modeList.KeyDown += (_, key) =>
        {
            if (key.NoShift.KeyCode == Key.Tab.KeyCode && FocusModeSettings(key.IsShift))
            {
                key.Handled = true;
                return;
            }

            if (!key.IsShift && key.KeyCode == Key.Enter.KeyCode)
            {
                FocusModeSettings(reverse: false);
                RunInBackground(runtimeService.StartAsync);
                key.Handled = true;
            }
        };
        _modeList.ValueChanged += (_, args) =>
        {
            if (args.NewValue is null || args.NewValue < 0 || args.NewValue >= modeCatalog.Modes.Count)
            {
                return;
            }

            settingsState.Update(settings => settings.CurrentModeId = modeCatalog.Modes[args.NewValue.Value].Id);
            RefreshStatus();
        };
        modesFrame.Add(_modeList);
        window.Add(modesFrame);

        var detailsFrame = new FrameView
        {
            X = Pos.Right(modesFrame),
            Y = Pos.Top(modesFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Title = "Mode Control"
        };

        _connectionLabel = new Label { X = 1, Y = 0, Width = Dim.Fill(), Text = "Connection:" };
        _modeLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(), Text = "Mode:" };
        _brightnessLabel = new Label { X = 1, Y = 2, Width = Dim.Fill(), Text = "Brightness:" };
        _sensitivityLabel = new Label { X = 1, Y = 3, Width = Dim.Fill(), Text = "Sensitivity:" };
        _statusLabel = new Label { X = 1, Y = 5, Width = Dim.Fill(), Text = "Status:" };
        _runtimeLabel = new Label { X = 1, Y = 6, Width = Dim.Fill(), Text = "Runtime:" };

        var settingsSeparator = new Line
        {
            X = 1,
            Y = 8,
            Width = Dim.Fill(1),
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };

        var settingsTitle = new Label
        {
            X = 2,
            Y = 8,
            Text = " Settings ",
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };

        _settingsFrame = new FrameView
        {
            X = 2,
            Y = 10,
            Width = Dim.Fill(2),
            Height = Dim.Fill(),
            BorderStyle = LineStyle.None,
            TabStop = TabBehavior.TabGroup
        };

        detailsFrame.Add(_connectionLabel, _modeLabel, _brightnessLabel, _sensitivityLabel, _statusLabel, _runtimeLabel, settingsSeparator, settingsTitle, _settingsFrame);
        window.Add(detailsFrame);

        var statusBar = new StatusBar { CanFocus = false };
        statusBar.Add(
            CreateShortcut("Help", Key.F1, ShowReadmeDialog),
            CreateShortcut("Start/Stop", Key.F5, () => RunInBackground(runtimeService.ToggleAsync)),
            CreateShortcut("Next", Key.N, NextMode),
            CreateShortcut("Prev", Key.P, PreviousMode),
            CreateShortcut("Bright-", Key.B, () => AdjustBrightness(-0.05)),
            CreateShortcut("Bright+", Key.B.WithShift, () => AdjustBrightness(0.05)),
            CreateShortcut("Sense-", Key.S, () => AdjustSensitivity(-0.05)),
            CreateShortcut("Sense+", Key.S.WithShift, () => AdjustSensitivity(0.05)),
            CreateShortcut("Connect", Key.F3, ShowConnectionDialog),
            CreateShortcut("Quit", Application.GetDefaultKey(Command.Quit), _app.RequestStop));
        window.Add(statusBar);
    }

    private void RefreshStatus()
    {
        var settings = settingsState.Snapshot();
        var mode = modeCatalog.GetCurrent(settings);
        _connectionLabel.Text = BuildConnectionSummary(settings, runtimeService.IsRunning);
        _modeLabel.Text = $"Mode: {mode.DisplayName}";
        _brightnessLabel.Text = $"Brightness: {mode.GetBrightness(settings):0.00}";
        _sensitivityLabel.Text = $"Sensitivity: {mode.GetSensitivity(settings):0.00}";
        _statusLabel.Text = $"Status: {runtimeService.StatusMessage}";
        _runtimeLabel.Text = runtimeService.IsRunning
            ? $"Running in {runtimeService.ActiveAreaName} via {runtimeService.ActiveDeviceName}"
            : "Runtime: idle";
        RefreshModeSelection();
        EnsureModeSettingsPanel(settings, mode.Id);
    }

    private void RefreshModeSelection()
    {
        var currentModeId = settingsState.Snapshot().CurrentModeId;
        for (var index = 0; index < modeCatalog.Modes.Count; index++)
        {
            if (string.Equals(modeCatalog.Modes[index].Id, currentModeId, StringComparison.OrdinalIgnoreCase))
            {
                _modeList.SelectedItem = index;
                break;
            }
        }
    }

    private void NextMode()
    {
        settingsState.Update(settings => settings.CurrentModeId = modeCatalog.GetNextId(settings.CurrentModeId));
        _renderedModeId = null;
        RefreshStatus();
    }

    private void PreviousMode()
    {
        settingsState.Update(settings => settings.CurrentModeId = modeCatalog.GetPreviousId(settings.CurrentModeId));
        _renderedModeId = null;
        RefreshStatus();
    }

    private void AdjustBrightness(double delta)
    {
        settingsState.Update(settings => modeCatalog.GetCurrent(settings).AdjustBrightness(settings, delta));
        _renderedModeId = null;
        RefreshStatus();
    }

    private void AdjustSensitivity(double delta)
    {
        settingsState.Update(settings => modeCatalog.GetCurrent(settings).AdjustSensitivity(settings, delta));
        _renderedModeId = null;
        RefreshStatus();
    }

    private void ShowDevicesDialog()
    {
        var devices = audioMonitorFactory.ListRenderDevices();
        var deviceLines = devices.Count == 0
            ? "No active render devices found."
            : string.Join(Environment.NewLine, devices.Select(device => $"[{device.Index}] {device.Name}{(device.IsDefault ? " (default)" : string.Empty)}"));

        MessageBox.Query(_app, "Render Devices", deviceLines, "Ok");
    }

    private void ShowConnectionDialog()
    {
        var draft = settingsState.Snapshot().Connection.Clone();
        var devices = audioMonitorFactory.ListRenderDevices();
        var cancel = new Button { Text = "Cancel" };
        var save = new Button { Text = "Save" };
        using var dialog = new Dialog { Title = "Connection Settings", Buttons = [cancel, save] };

        var bridgeField = AddTextField(dialog, "Bridge:", 0);
        bridgeField.Text = draft.Bridge;

        var appKeyField = AddTextField(dialog, "App Key:", 1, secret: true);
        appKeyField.Text = draft.AppKey;

        var clientKeyField = AddTextField(dialog, "Client Key:", 2, secret: true);
        clientKeyField.Text = draft.ClientKey;

        var showKeysCheckBox = new CheckBox
        {
            X = 18,
            Y = 3,
            Title = "Show keys",
            Value = CheckState.UnChecked
        };
        showKeysCheckBox.ValueChanged += (_, args) =>
        {
            var showKeys = showKeysCheckBox.Value == CheckState.Checked;
            appKeyField.Secret = !showKeys;
            clientKeyField.Secret = !showKeys;
        };
        dialog.Add(showKeysCheckBox);

        var areaField = AddTextField(dialog, "Area:", 4);
        areaField.Text = draft.Area;

        AddLabel(dialog, "Device:", 5);
        var deviceLabels = new List<string> { "Default device" };
        deviceLabels.AddRange(devices.Select(device => $"[{device.Index}] {device.Name}{(device.IsDefault ? " (default)" : string.Empty)}"));
        var selectedDeviceValue = draft.DeviceIndex ?? -1;

        var deviceSelector = new OptionSelector
        {
            X = 18,
            Y = 5,
            Width = Dim.Fill(1),
            Height = deviceLabels.Count,
            Orientation = Orientation.Vertical,
            Labels = [.. deviceLabels],
            Values = [.. new[] { -1 }.Concat(devices.Select(device => device.Index))],
            Value = selectedDeviceValue
        };
        dialog.Add(deviceSelector);

        var fpsRow = 6 + deviceLabels.Count;

        AddLabel(dialog, "FPS:", fpsRow);
        var fpsEditor = new NumericUpDown<int>
        {
            X = SettingsControlX,
            Y = fpsRow,
            Width = NumericControlWidth,
            Value = draft.Fps,
            Increment = 1
        };
        ClampNumericEditor(fpsEditor, 1, 60);
        AttachDirectIntEntry(fpsEditor, "FPS", 1, 60);
        dialog.Add(fpsEditor);

        var exitBehaviorRow = fpsRow + 1;
        AddLabel(dialog, "On Exit:", exitBehaviorRow);
        var exitBehaviorSelector = new OptionSelector
        {
            X = SettingsControlX,
            Y = exitBehaviorRow,
            Width = SettingsControlWidth,
            Height = 2,
            Orientation = Orientation.Vertical,
            Labels = ["Blackout", "Set Color"],
            Values = [(int)AppExitLightingMode.Blackout, (int)AppExitLightingMode.Color],
            Value = (int)draft.ExitLightingMode
        };
        dialog.Add(exitBehaviorSelector);

        var exitColor = CreateColor(draft.ExitColorRed, draft.ExitColorGreen, draft.ExitColorBlue);
        var exitColorRow = exitBehaviorRow + 2;
        AddLabel(dialog, "Exit Color:", exitColorRow);
        var exitColorButton = new Button
        {
            X = SettingsControlX,
            Y = exitColorRow,
            Width = SettingsControlWidth,
            Text = FormatColorButtonText(exitColor)
        };
        exitColorButton.Accepting += (_, _) =>
        {
            ShowRgbColorPickerDialog("Exit Color", exitColor, newColor =>
            {
                exitColor = newColor;
                exitColorButton.Text = FormatColorButtonText(newColor);
            });
        };
        dialog.Add(exitColorButton);

        save.Accepting += (_, e) =>
        {
            settingsState.Update(settings =>
            {
                settings.Connection.Bridge = bridgeField.Text?.ToString() ?? string.Empty;
                settings.Connection.AppKey = appKeyField.Text?.ToString() ?? string.Empty;
                settings.Connection.ClientKey = clientKeyField.Text?.ToString() ?? string.Empty;
                settings.Connection.Area = areaField.Text?.ToString() ?? string.Empty;
                var deviceIndex = Convert.ToInt32(deviceSelector.Value);
                settings.Connection.DeviceIndex = deviceIndex < 0 ? null : deviceIndex;
                settings.Connection.Fps = Math.Clamp(fpsEditor.Value, 1, 60);
                settings.Connection.ExitLightingMode = (AppExitLightingMode)Convert.ToInt32(exitBehaviorSelector.Value);
                settings.Connection.ExitColorRed = exitColor.R;
                settings.Connection.ExitColorGreen = exitColor.G;
                settings.Connection.ExitColorBlue = exitColor.B;
            });

            e.Handled = true;
            dialog.RequestStop();
        };

        _app.Run(dialog);
        RefreshStatus();
    }

    private void ShowReadmeDialog()
    {
        var readmePath = FindReadmePath();
        if (readmePath is null)
        {
            MessageBox.ErrorQuery(_app, "Help", "README.md not found.", "Ok");
            return;
        }

        var close = new Button { Text = "Close", IsDefault = true };
        using var dialog = new Dialog
        {
            Title = "README",
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Buttons = [close]
        };

        var markdown = new Markdown
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = File.ReadAllText(readmePath, Encoding.UTF8),
            ShowCopyButtons = false
        };
        markdown.LinkClicked += (_, args) => args.Handled = true;
        dialog.Add(markdown);
        close.Accepting += (_, e) =>
        {
            e.Handled = true;
            dialog.RequestStop();
        };

        _app.Run(dialog);
    }

    private void RunInBackground(Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _app.Invoke(() => MessageBox.ErrorQuery(_app, "Error", exception.Message, "Ok"));
            }
        });
    }

    private static Shortcut CreateShortcut(string title, Key key, Action action)
    {
        return new Shortcut
        {
            Title = title,
            Key = key,
            Action = action,
            BindKeyToApplication = true,
            CanFocus = false
        };
    }

    private static string? FindReadmePath()
    {
        foreach (var root in EnumerateSearchRoots())
        {
            var current = root;
            while (!string.IsNullOrWhiteSpace(current))
            {
                var candidate = Path.Combine(current, "README.md");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string BuildConnectionSummary(AppSettings settings, bool isRunning)
    {
        if (string.IsNullOrWhiteSpace(settings.Connection.Bridge) ||
            string.IsNullOrWhiteSpace(settings.Connection.AppKey) ||
            string.IsNullOrWhiteSpace(settings.Connection.ClientKey) ||
            string.IsNullOrWhiteSpace(settings.Connection.Area))
        {
            return "Connection: incomplete (open F3)";
        }

        return isRunning
            ? "Connection: configured (editing applies next start)"
            : "Connection: configured";
    }

    private static TextField AddTextField(View container, string labelText, int y, bool secret = false)
    {
        var label = AddLabel(container, labelText, y);
        var field = new TextField
        {
            X = Pos.Right(label) + 1,
            Y = y,
            Width = Dim.Fill(1),
            Secret = secret
        };
        container.Add(field);
        return field;
    }

    private static Label AddLabel(View container, string text, int y)
    {
        var label = new Label
        {
            X = 0,
            Y = y,
            Width = SettingsLabelWidth,
            Text = text,
            TextAlignment = Alignment.End
        };
        container.Add(label);
        return label;
    }

    private void EnsureModeSettingsPanel(AppSettings settings, string modeId)
    {
        if (_renderedModeId == modeId)
        {
            return;
        }

        _settingsFrame.RemoveAll();
        _renderedModeId = modeId;

        switch (modeId)
        {
            case ModeIds.CycleStrobe:
                BuildCycleStrobeSettings(settings.CycleStrobe);
                break;
            case ModeIds.Sparkle:
                BuildSparkleSettings(settings.Sparkle);
                break;
            case ModeIds.WaveTravel:
                BuildWaveTravelSettings(settings.WaveTravel);
                break;
            case ModeIds.AmbientDrift:
                BuildAmbientDriftSettings(settings.AmbientDrift);
                break;
            case ModeIds.BeatPulse:
                BuildBeatPulseSettings(settings.BeatPulse);
                break;
            case ModeIds.SplitStrobe:
                BuildSplitStrobeSettings(settings.SplitStrobe);
                break;
            default:
                BuildAudioReactiveSettings(settings.AudioReactive);
                break;
        }

        WireModeSettingsTabNavigation();
    }

    private void BuildAudioReactiveSettings(AudioReactiveModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.AudioReactive.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.AudioReactive.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.AudioReactive.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.AudioReactive.CoolHue = value));
    }

    private void BuildCycleStrobeSettings(CycleStrobeModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.CycleStrobe.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.CycleStrobe.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.CycleStrobe.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.CycleStrobe.CoolHue = value));
        AddModeDoubleEditor("Cycle Sec:", 4, settings.CycleSeconds, value => UpdateModeSettings(s => s.CycleStrobe.CycleSeconds = value), 0.1, min: 0.1);
    }

    private void BuildSparkleSettings(SparkleModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.Sparkle.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.Sparkle.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.Sparkle.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.Sparkle.CoolHue = value));
        AddModeRgbColorEditor("Sparkle Color:", 4, CreateColor(settings.SparkleRed, settings.SparkleGreen, settings.SparkleBlue), color =>
            UpdateModeSettings(s =>
            {
                s.Sparkle.SparkleRed = color.R;
                s.Sparkle.SparkleGreen = color.G;
                s.Sparkle.SparkleBlue = color.B;
            }));
    }

    private void BuildWaveTravelSettings(WaveTravelModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.WaveTravel.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.WaveTravel.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.WaveTravel.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.WaveTravel.CoolHue = value));
        AddModeDoubleEditor("Wave Sec:", 4, settings.WaveSeconds, value => UpdateModeSettings(s => s.WaveTravel.WaveSeconds = value), 0.1, min: 0.1);
    }

    private void BuildAmbientDriftSettings(AmbientDriftModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.AmbientDrift.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.AmbientDrift.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.AmbientDrift.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.AmbientDrift.CoolHue = value));
        AddModeDoubleEditor("Cycle Sec:", 4, settings.CycleSeconds, value => UpdateModeSettings(s => s.AmbientDrift.CycleSeconds = value), 0.1, min: 0.1);
    }

    private void BuildBeatPulseSettings(BeatPulseModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.BeatPulse.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.BeatPulse.Sensitivity = value), min: 0.01);
        AddModeColorEditor("Warm Color:", 2, settings.WarmHue, value => UpdateModeSettings(s => s.BeatPulse.WarmHue = value));
        AddModeColorEditor("Cool Color:", 3, settings.CoolHue, value => UpdateModeSettings(s => s.BeatPulse.CoolHue = value));
    }

    private void BuildSplitStrobeSettings(SplitStrobeModeSettings settings)
    {
        AddModeDoubleEditor("Brightness:", 0, settings.Brightness, value => UpdateModeSettings(s => s.SplitStrobe.Brightness = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Sensitivity:", 1, settings.Sensitivity, value => UpdateModeSettings(s => s.SplitStrobe.Sensitivity = value), min: 0.01);
        AddModeDoubleEditor("Background:", 2, settings.BackgroundLevel, value => UpdateModeSettings(s => s.SplitStrobe.BackgroundLevel = value), min: 0.0, max: 1.0);
        AddModeDoubleEditor("Attack Sec:", 3, settings.AttackSeconds, value => UpdateModeSettings(s => s.SplitStrobe.AttackSeconds = value), 0.01, min: 0.01);
        AddModeDoubleEditor("Decay Sec:", 4, settings.DecaySeconds, value => UpdateModeSettings(s => s.SplitStrobe.DecaySeconds = value), 0.01, min: 0.01);
        AddModeRgbColorEditor("Bass Color:", 5, CreateColor(settings.BassRed, settings.BassGreen, settings.BassBlue), color =>
            UpdateModeSettings(s =>
            {
                s.SplitStrobe.BassRed = color.R;
                s.SplitStrobe.BassGreen = color.G;
                s.SplitStrobe.BassBlue = color.B;
            }));
        AddModeRgbColorEditor("Treble Color:", 6, CreateColor(settings.TrebleRed, settings.TrebleGreen, settings.TrebleBlue), color =>
            UpdateModeSettings(s =>
            {
                s.SplitStrobe.TrebleRed = color.R;
                s.SplitStrobe.TrebleGreen = color.G;
                s.SplitStrobe.TrebleBlue = color.B;
            }));
    }

    private void UpdateModeSettings(Action<AppSettings> update)
    {
        settingsState.Update(settings =>
        {
            update(settings);
            settings.Normalize();
        });
        RefreshStatus();
    }

    private void AddModeDoubleEditor(string label, int y, double value, Action<double> onChanged, double increment = 0.05, double? min = null, double? max = null)
    {
        AddLabel(_settingsFrame, label, y);
        var editor = new NumericUpDown<double>
        {
            X = SettingsControlX,
            Y = y,
            Width = NumericControlWidth,
            Value = value,
            Increment = increment,
            Format = "{0:0.##}"
        };
        ClampNumericEditor(editor, min, max);
        AttachDirectDoubleEntry(editor, label.TrimEnd(':'), min, max);
        editor.ValueChanged += (_, args) => onChanged(args.NewValue);
        _settingsFrame.Add(editor);
    }

    private void AddModeColorEditor(string label, int y, double hue, Action<double> onChanged)
    {
        AddLabel(_settingsFrame, label, y);
        var currentHue = NormalizeHue(hue);
        var button = new Button
        {
            X = SettingsControlX,
            Y = y,
            Width = SettingsControlWidth,
            Text = FormatColorButtonText(currentHue)
        };
        button.Accepting += (_, _) =>
        {
            ShowColorPickerDialog(label.TrimEnd(':'), currentHue, newHue =>
            {
                currentHue = newHue;
                onChanged(newHue);
                button.Text = FormatColorButtonText(newHue);
            });
        };
        _settingsFrame.Add(button);
    }

    private void AddModeRgbColorEditor(string label, int y, Color color, Action<Color> onChanged)
    {
        AddLabel(_settingsFrame, label, y);
        var currentColor = color;
        var button = new Button
        {
            X = SettingsControlX,
            Y = y,
            Width = SettingsControlWidth,
            Text = FormatColorButtonText(currentColor)
        };
        button.Accepting += (_, _) =>
        {
            ShowRgbColorPickerDialog(label.TrimEnd(':'), currentColor, newColor =>
            {
                currentColor = newColor;
                onChanged(newColor);
                button.Text = FormatColorButtonText(newColor);
            });
        };
        _settingsFrame.Add(button);
    }

    private void ShowColorPickerDialog(string title, double initialHue, Action<double> onChanged)
    {
        var cancel = new Button { Text = "Cancel" };
        var save = new Button { Text = "Save", IsDefault = true };
        using var dialog = new Dialog
        {
            Title = title,
            Buttons = [cancel, save]
        };

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
            SelectedColor = HueToColor(initialHue)
        };
        picker.ApplyStyleChanges();
        dialog.Add(picker);

        save.Accepting += (_, e) =>
        {
            onChanged(ColorToHue(picker.SelectedColor, initialHue));
            TryMarkMouseEventHandled(e);
            e.Handled = true;
            dialog.RequestStop();
        };

        _app.Run(dialog);
    }

    private void ShowRgbColorPickerDialog(string title, Color initialColor, Action<Color> onChanged)
    {
        var cancel = new Button { Text = "Cancel" };
        var save = new Button { Text = "Save", IsDefault = true };
        using var dialog = new Dialog
        {
            Title = title,
            Buttons = [cancel, save]
        };

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
            SelectedColor = initialColor
        };
        picker.ApplyStyleChanges();
        dialog.Add(picker);

        save.Accepting += (_, e) =>
        {
            onChanged(picker.SelectedColor);
            TryMarkMouseEventHandled(e);
            e.Handled = true;
            dialog.RequestStop();
        };

        _app.Run(dialog);
    }

    private static string FormatColorButtonText(double hue)
    {
        var color = HueToColor(hue);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2} {NormalizeHue(hue):0}°";
    }

    private static string FormatColorButtonText(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2} {ColorToHue(color, 0):0}°";
    }

    private static Color CreateColor(int red, int green, int blue) => new(red, green, blue);

    private static Color HueToColor(double hue)
    {
        var normalizedHue = NormalizeHue(hue);
        var sector = normalizedHue / 60d;
        var chroma = 1d;
        var x = chroma * (1 - Math.Abs((sector % 2) - 1));

        var (red, green, blue) = sector switch
        {
            >= 0 and < 1 => (chroma, x, 0d),
            >= 1 and < 2 => (x, chroma, 0d),
            >= 2 and < 3 => (0d, chroma, x),
            >= 3 and < 4 => (0d, x, chroma),
            >= 4 and < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new Color(
            (int)Math.Round(red * 255),
            (int)Math.Round(green * 255),
            (int)Math.Round(blue * 255));
    }

    private static double ColorToHue(Color color, double fallbackHue)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        if (delta <= double.Epsilon)
        {
            return NormalizeHue(fallbackHue);
        }

        var hue = max switch
        {
            var value when value == red => 60d * (((green - blue) / delta) % 6d),
            var value when value == green => 60d * (((blue - red) / delta) + 2d),
            _ => 60d * (((red - green) / delta) + 4d)
        };

        return NormalizeHue(hue);
    }

    private static double NormalizeHue(double hue)
    {
        var normalized = hue % 360d;
        return normalized < 0 ? normalized + 360d : normalized;
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

    private void AttachDirectDoubleEntry(NumericUpDown<double> editor, string title, double? min = null, double? max = null)
    {
        editor.KeyDown += (_, key) =>
        {
            if (key.KeyCode != Key.Enter.KeyCode || key.IsShift || key.IsCtrl || key.IsAlt)
            {
                return;
            }

            key.Handled = true;
            PromptForDouble(editor, title, min, max);
        };
    }

    private void AttachDirectIntEntry(NumericUpDown<int> editor, string title, int? min = null, int? max = null)
    {
        editor.KeyDown += (_, key) =>
        {
            if (key.KeyCode != Key.Enter.KeyCode || key.IsShift || key.IsCtrl || key.IsAlt)
            {
                return;
            }

            key.Handled = true;
            PromptForInt(editor, title, min, max);
        };
    }

    private void PromptForDouble(NumericUpDown<double> editor, string title, double? min = null, double? max = null)
    {
        while (true)
        {
            var input = ShowNumericPrompt(title, editor.Value.ToString("0.##", CultureInfo.InvariantCulture));
            if (input is null)
            {
                return;
            }

            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                if (min.HasValue)
                {
                    parsed = Math.Max(parsed, min.Value);
                }

                if (max.HasValue)
                {
                    parsed = Math.Min(parsed, max.Value);
                }

                editor.Value = parsed;
                return;
            }

            MessageBox.ErrorQuery(_app, title, "Enter a valid number.", "Ok");
        }
    }

    private void PromptForInt(NumericUpDown<int> editor, string title, int? min = null, int? max = null)
    {
        while (true)
        {
            var input = ShowNumericPrompt(title, editor.Value.ToString(CultureInfo.InvariantCulture));
            if (input is null)
            {
                return;
            }

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                if (min.HasValue)
                {
                    parsed = Math.Max(parsed, min.Value);
                }

                if (max.HasValue)
                {
                    parsed = Math.Min(parsed, max.Value);
                }

                editor.Value = parsed;
                return;
            }

            MessageBox.ErrorQuery(_app, title, "Enter a valid integer.", "Ok");
        }
    }

    private string? ShowNumericPrompt(string title, string initialValue)
    {
        var field = new TextField
        {
            Width = 12,
            Text = initialValue
        };

        using var prompt = new Prompt<TextField, string?>(field)
        {
            Title = $"Set {title}"
        };
        prompt.Initialized += (_, _) => field.SetFocus();

        _app.Run(prompt);
        return prompt.Result?.Trim();
    }

    private static void TryMarkMouseEventHandled(CommandEventArgs args)
    {
        if (args.Context?.Binding is MouseBinding { MouseEvent: { } mouseEvent })
        {
            mouseEvent.Handled = true;
        }
    }

    private bool FocusModeSettings(bool reverse)
    {
        var focusableViews = GetModeSettingsFocusableViews();
        var target = reverse ? focusableViews.LastOrDefault() : focusableViews.FirstOrDefault();
        return target?.SetFocus() ?? false;
    }

    private void WireModeSettingsTabNavigation()
    {
        var focusableViews = GetModeSettingsFocusableViews();
        if (focusableViews.Count == 0)
        {
            return;
        }

        var first = focusableViews[0];
        var last = focusableViews[^1];

        first.KeyDown += (_, key) =>
        {
            if (key.IsShift && key.NoShift.KeyCode == Key.Tab.KeyCode && _modeList.SetFocus())
            {
                key.Handled = true;
            }
        };

        last.KeyDown += (_, key) =>
        {
            if (!key.IsShift && key.KeyCode == Key.Tab.KeyCode && _modeList.SetFocus())
            {
                key.Handled = true;
            }
        };
    }

    private List<View> GetModeSettingsFocusableViews()
    {
        return _settingsFrame.SubViews
            .Where(view => view.CanFocus && view.Visible && view.Enabled)
            .ToList();
    }

}

