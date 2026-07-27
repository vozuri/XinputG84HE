using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XinputG84HE.Models;
using XinputG84HE.Services;

namespace XinputG84HE.ViewModels;

/// <summary>One row in the live keypress overlay: a currently-active raw key and its actuation depth.</summary>
public sealed record ActiveKeyRow(string Label, double Percent);

public partial class MainViewModel : ObservableObject
{
    private const string HotkeyToggle = "ToggleEmulation";
    private const string HotkeyNext = "NextPreset";
    private const string HotkeyPrev = "PreviousPreset";

    // Keys must drop below this actuation fraction before a pending stop
    // actually takes effect - prevents the ViGEm "ghost input" bug where
    // disconnecting the virtual controller while a key is still held can
    // leave a stuck analog/button value behind, sometimes until reboot.
    private const double ReleaseThreshold = 0.02;

    // How long a released key's actuation bar takes to animate down to 0%
    // in the live actuation overlay before the row disappears.
    private const double KeyRowFadeSeconds = 1.5;

    private readonly HidDeviceService _hid;
    private readonly GamepadOutputService _gamepad;
    private readonly KeyboardHookService _hook;
    private readonly BindingCaptureService _bindingCapture;
    private readonly ProfileStore _profileStore;
    private readonly Dispatcher _dispatcher;

    private Profile _profile;
    private bool _pendingStop;
    private bool _suppressTuningSave;
    private DateTime _lastLiveViewTick = DateTime.UtcNow;
    private readonly Dictionary<ushort, (double displayPercent, string label, bool fading)> _keyRowTracking = new();

    [ObservableProperty] private bool isKeyboardConnected;
    [ObservableProperty] private bool isEmulationRunning;
    [ObservableProperty] private bool isPendingStop;
    [ObservableProperty] private bool isBinding;
    [ObservableProperty] private string bindingStatusText = "";
    [ObservableProperty] private string statusLog = "Ready.";
    [ObservableProperty] private string currentPresetName = "Default";
    [ObservableProperty] private bool isCurrentPresetLocked;
    [ObservableProperty] private double deadzonePercent = 5;
    [ObservableProperty] private double digitalThresholdPercent = 50;
    [ObservableProperty] private bool blockMappedKeys = true;
    [ObservableProperty] private bool useRadialDeadzone = false;
    [ObservableProperty] private bool antiGhostInputEnabled = false;
    [ObservableProperty] private double antiGhostInputSeconds = 2.5;
    [ObservableProperty] private int emulationReportRate = 500;
    [ObservableProperty] private bool isCapturingHotkey;

    public int[] AvailableReportRates { get; } = new[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
    [ObservableProperty] private bool isAnalogHelpVisible;
    [ObservableProperty] private bool isCurveEditorVisible;
    [ObservableProperty] private int curveEditorJoystick; // 0 = Left, 1 = Right
    [ObservableProperty] private string curveEditorTitle = "Response Curve Editor";
    public ObservableCollection<CurvePointViewModel> CurvePoints { get; } = new();
    [ObservableProperty] private bool isHotkeysExpanded;
    [ObservableProperty] private string toggleHotkeyText = "Unbound";
    [ObservableProperty] private string nextPresetHotkeyText = "Unbound";
    [ObservableProperty] private string prevPresetHotkeyText = "Unbound";

    // ---- Live input view ----
    [ObservableProperty] private double leftStickDotX;
    [ObservableProperty] private double leftStickDotY;
    [ObservableProperty] private double rightStickDotX;
    [ObservableProperty] private double rightStickDotY;
    [ObservableProperty] private double leftTriggerFill;
    [ObservableProperty] private double rightTriggerFill;
    [ObservableProperty] private IReadOnlyCollection<ControllerInput> pressedInputs = Array.Empty<ControllerInput>();
    [ObservableProperty] private IReadOnlyList<ActiveKeyRow> activeKeyRows = Array.Empty<ActiveKeyRow>();

    private readonly DispatcherTimer _liveViewTimer;

    public ObservableCollection<PresetTileViewModel> PresetTiles { get; } = new();

    // One label property per controller input, kept simple and directly bindable from XAML.
    [ObservableProperty] private string aLabel = "Unbound";
    [ObservableProperty] private string bLabel = "Unbound";
    [ObservableProperty] private string xLabel = "Unbound";
    [ObservableProperty] private string yLabel = "Unbound";
    [ObservableProperty] private string leftBumperLabel = "Unbound";
    [ObservableProperty] private string rightBumperLabel = "Unbound";
    [ObservableProperty] private string leftTriggerLabel = "Unbound";
    [ObservableProperty] private string rightTriggerLabel = "Unbound";
    [ObservableProperty] private string backLabel = "Unbound";
    [ObservableProperty] private string startLabel = "Unbound";
    [ObservableProperty] private string guideLabel = "Unbound";
    [ObservableProperty] private string dpadUpLabel = "Unbound";
    [ObservableProperty] private string dpadDownLabel = "Unbound";
    [ObservableProperty] private string dpadLeftLabel = "Unbound";
    [ObservableProperty] private string dpadRightLabel = "Unbound";
    [ObservableProperty] private string leftStickClickLabel = "Unbound";
    [ObservableProperty] private string rightStickClickLabel = "Unbound";
    [ObservableProperty] private string leftStickUpLabel = "Unbound";
    [ObservableProperty] private string leftStickDownLabel = "Unbound";
    [ObservableProperty] private string leftStickLeftLabel = "Unbound";
    [ObservableProperty] private string leftStickRightLabel = "Unbound";
    [ObservableProperty] private string rightStickUpLabel = "Unbound";
    [ObservableProperty] private string rightStickDownLabel = "Unbound";
    [ObservableProperty] private string rightStickLeftLabel = "Unbound";
    [ObservableProperty] private string rightStickRightLabel = "Unbound";

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        _hid = new HidDeviceService();
        _gamepad = new GamepadOutputService(_hid);
        _hook = new KeyboardHookService();
        _bindingCapture = new BindingCaptureService(_hook, _hid);
        _profileStore = new ProfileStore();

        _hid.ConnectionChanged += connected => _dispatcher.Invoke(() => IsKeyboardConnected = connected);
        _hid.Log += msg => _dispatcher.Invoke(() => StatusLog = msg);
        _gamepad.Log += msg => _dispatcher.Invoke(() => StatusLog = msg);
        _hook.HotkeyPressed += name => _dispatcher.Invoke(() => OnHotkeyPressed(name));

        _profile = _profileStore.LoadLastOrDefault();
        ApplyProfileToUiState();

        LoadHotkeyOrDefault(HotkeyToggle, 0, 0, d => ToggleHotkeyText = d);
        LoadHotkeyOrDefault(HotkeyNext, 0, 0, d => NextPresetHotkeyText = d);
        LoadHotkeyOrDefault(HotkeyPrev, 0, 0, d => PrevPresetHotkeyText = d);

        (bool ghostEnabled, double ghostSeconds) = _profileStore.LoadAntiGhostSettings();
        int reportRate = _profileStore.LoadReportRate();
        
        _suppressTuningSave = true;
        AntiGhostInputEnabled = ghostEnabled;
        AntiGhostInputSeconds = ghostSeconds;
        EmulationReportRate = reportRate;
        _suppressTuningSave = false;
        
        _hid.AntiGhostEnabled = ghostEnabled;
        _hid.AntiGhostSeconds = ghostSeconds;
        _gamepad.SetReportRate(reportRate);

        RefreshPresetTiles();
        RefreshAllLabels();

        _hook.Start();
        _hid.Start();

        // Belt-and-braces: the device supervisor already sends this on every
        // connect, but fire it once more right at startup too in case the
        // keyboard was already connected before this ran.
        _hid.EnableSimulationMode();

        _liveViewTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _liveViewTimer.Tick += (_, _) => UpdateLiveView();
        _liveViewTimer.Start();
    }

    private void LoadHotkeyOrDefault(string name, int defaultModifiers, int defaultVk, Action<string> setText)
    {
        (int modifiers, int vk) combo = _profileStore.LoadHotkey(name) ?? (defaultModifiers, defaultVk);
        if (combo.vk == 0)
        {
            _hook.SetHotkey(name, 0, 0);
            setText("Unbound");
        }
        else
        {
            _hook.SetHotkey(name, combo.modifiers, combo.vk);
            setText(KeyboardHookService.DescribeCombo(combo.modifiers, combo.vk));
        }
    }

    private void OnHotkeyPressed(string name)
    {
        switch (name)
        {
            case HotkeyToggle: ToggleEmulation(); break;
            case HotkeyNext: CyclePreset(1); break;
            case HotkeyPrev: CyclePreset(-1); break;
        }
    }

    // ---- Commands ----

    [RelayCommand]
    private void ToggleEmulation()
    {
        if (IsEmulationRunning)
        {
            if (_pendingStop)
            {
                // Pressed again while waiting for keys to release - cancel the stop, stay on.
                _pendingStop = false;
                IsPendingStop = false;
                return;
            }

            if (AnyMappedKeyCurrentlyActuated())
            {
                _pendingStop = true;
                IsPendingStop = true;
                return;
            }

            ActuallyStopEmulation();
        }
        else
        {
            _pendingStop = false;
            IsPendingStop = false;
            _hid.EnableSimulationMode();
            bool ok = _gamepad.Start(_profile);
            if (ok)
            {
                RefreshBlockedKeys();
                _hook.BlockingEnabled = BlockMappedKeys;
                IsEmulationRunning = true;
            }
        }
    }

    private void ActuallyStopEmulation()
    {
        _gamepad.Stop();
        _hook.BlockingEnabled = false;
        IsEmulationRunning = false;
        _pendingStop = false;
        IsPendingStop = false;
    }

    private bool AnyMappedKeyCurrentlyActuated()
    {
        foreach (KeyBinding? binding in _profile.Bindings.Values)
        {
            if (binding is not null && _hid.GetNormalized(binding.RawKeyId) > ReleaseThreshold)
            {
                return true;
            }
        }
        return false;
    }

    private void CyclePreset(int direction)
    {
        if (PresetTiles.Count == 0)
        {
            return;
        }

        int currentIndex = 0;
        for (int i = 0; i < PresetTiles.Count; i++)
        {
            if (PresetTiles[i].Name == _profile.Name)
            {
                currentIndex = i;
                break;
            }
        }

        int count = PresetTiles.Count;
        int nextIndex = ((currentIndex + direction) % count + count) % count;
        LoadPreset(PresetTiles[nextIndex].Name);
    }

    public event Action? OnCurveEditorRedrawNeeded;

    [RelayCommand]
    private void ToggleAnalogHelp() => IsAnalogHelpVisible = !IsAnalogHelpVisible;

    [RelayCommand]
    private void OpenCurveEditor(string joystickStr)
    {
        int joystick = int.TryParse(joystickStr, out int j) ? j : 0;
        SelectJoystick(joystickStr);
        IsCurveEditorVisible = true;
    }

    [RelayCommand]
    private void SelectJoystick(string joystickStr)
    {
        int joystick = int.TryParse(joystickStr, out int j) ? j : 0;
        CurveEditorJoystick = joystick;
        CurveEditorTitle = joystick == 0 ? "Left Stick Response Curve" : "Right Stick Response Curve";
        
        _suppressTuningSave = true;
        CurvePoints.Clear();
        
        double[] curve = joystick == 0 ? _profile.LeftStickCurve : _profile.RightStickCurve;
        for (int i = 0; i < 11; i++)
        {
            var point = new CurvePointViewModel(i * 10, curve[i]);
            point.YChanged += (_, _) => OnCurvePointChanged();
            CurvePoints.Add(point);
        }
        _suppressTuningSave = false;

        OnCurveEditorRedrawNeeded?.Invoke();
    }

    [RelayCommand]
    private void ApplyLinearCurve() => SetCurveValues(new double[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 });

    [RelayCommand]
    private void ApplySCurve() => SetCurveValues(new double[] { 0, 3, 8, 18, 32, 50, 68, 82, 92, 97, 100 });

    [RelayCommand]
    private void ApplyAggressiveCurve() => SetCurveValues(new double[] { 0, 25, 45, 60, 72, 82, 90, 95, 98, 99, 100 });

    [RelayCommand]
    private void ApplySmoothCurve() => SetCurveValues(new double[] { 0, 1, 3, 8, 15, 28, 45, 65, 82, 94, 100 });

    [RelayCommand]
    private void ResetCurve() => ApplyLinearCurve();

    private void SetCurveValues(double[] values)
    {
        if (values == null || values.Length != 11 || CurvePoints.Count != 11) return;
        _suppressTuningSave = true;
        for (int i = 0; i < 11; i++)
        {
            CurvePoints[i].Y = values[i];
        }
        _suppressTuningSave = false;
        OnCurvePointChanged();
    }

    [RelayCommand]
    private void CloseCurveEditor() => IsCurveEditorVisible = false;

    private void OnCurvePointChanged()
    {
        if (_suppressTuningSave) return;

        double[] curve = CurveEditorJoystick == 0 ? _profile.LeftStickCurve : _profile.RightStickCurve;
        for (int i = 0; i < 11; i++)
        {
            if (i < CurvePoints.Count)
            {
                curve[i] = CurvePoints[i].Y;
            }
        }

        _profileStore.Save(_profile);
        OnCurveEditorRedrawNeeded?.Invoke();
    }

    [RelayCommand]
    private void SetToggleHotkey() => BeginCaptureHotkey(HotkeyToggle, d => ToggleHotkeyText = d);

    [RelayCommand]
    private void SetNextPresetHotkey() => BeginCaptureHotkey(HotkeyNext, d => NextPresetHotkeyText = d);

    [RelayCommand]
    private void SetPrevPresetHotkey() => BeginCaptureHotkey(HotkeyPrev, d => PrevPresetHotkeyText = d);

    private void BeginCaptureHotkey(string name, Action<string> setText)
    {
        IsCapturingHotkey = true;
        setText("Press a key combo...");

        _hook.CaptureNextHotkey((modifiers, vk) =>
        {
            _dispatcher.Invoke(() =>
            {
                _hook.SetHotkey(name, modifiers, vk);
                _profileStore.SaveHotkey(name, modifiers, vk);
                setText(KeyboardHookService.DescribeCombo(modifiers, vk));
                IsCapturingHotkey = false;
            });
        });
    }

    [RelayCommand]
    private void InitializeAnalogMode() => _hid.EnableSimulationMode();

    [RelayCommand]
    private void SavePreset() => _profileStore.Save(_profile);

    [RelayCommand]
    private void CancelBind() => _bindingCapture.CancelCapture();

    [RelayCommand]
    private async Task StartBindAsync(ControllerInput input)
    {
        if (IsBinding)
        {
            return;
        }

        if (_profile.Locked)
        {
            BindingStatusText = "This preset is locked - unlock it first to change bindings.";
            return;
        }

        IsBinding = true;
        BindingStatusText = $"Press a key for {ControllerInputInfo.ShortLabel(input)}... (Esc to unbind, right-click to cancel)";

        CaptureResult result = await _bindingCapture.CaptureNextKeyAsync(TimeSpan.FromSeconds(6));

        switch (result.Outcome)
        {
            case CaptureOutcome.Success:
                _profile.Bindings[input] = result.Binding;
                _profileStore.Save(_profile);
                RefreshLabel(input);
                BindingStatusText = $"{ControllerInputInfo.ShortLabel(input)} bound to {result.Binding!.DisplayName}.";
                if (IsEmulationRunning)
                {
                    RefreshBlockedKeys();
                }
                break;

            case CaptureOutcome.Unbind:
                _profile.Bindings[input] = null;
                _profileStore.Save(_profile);
                RefreshLabel(input);
                BindingStatusText = $"{ControllerInputInfo.ShortLabel(input)} unbound.";
                if (IsEmulationRunning)
                {
                    RefreshBlockedKeys();
                }
                break;

            case CaptureOutcome.Cancelled:
                BindingStatusText = "Cancelled - no changes made.";
                break;
        }

        IsBinding = false;
    }

    [RelayCommand]
    private void Unbind(ControllerInput input)
    {
        if (_profile.Locked)
        {
            return;
        }

        _profile.Bindings[input] = null;
        _profileStore.Save(_profile);
        RefreshLabel(input);
        if (IsEmulationRunning)
        {
            RefreshBlockedKeys();
        }
    }

    [RelayCommand]
    private void NewPreset()
    {
        string name = GenerateUniqueName("New Preset");

        var fresh = new Profile
        {
            Name = name,
            Deadzone = _profile.Deadzone,
            DigitalThreshold = _profile.DigitalThreshold,
            BlockMappedKeys = _profile.BlockMappedKeys,
            UseRadialDeadzone = _profile.UseRadialDeadzone,
        };
        _profileStore.Save(fresh);
        RefreshPresetTiles();
        LoadPreset(name);
    }

    partial void OnDeadzonePercentChanged(double value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        if (_profile.Locked)
        {
            _suppressTuningSave = true;
            DeadzonePercent = _profile.Deadzone * 100.0;
            _suppressTuningSave = false;
            return;
        }
        _profile.Deadzone = Math.Clamp(value / 100.0, 0.0, 0.5);
        _profileStore.Save(_profile);
    }

    partial void OnDigitalThresholdPercentChanged(double value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        if (_profile.Locked)
        {
            _suppressTuningSave = true;
            DigitalThresholdPercent = _profile.DigitalThreshold * 100.0;
            _suppressTuningSave = false;
            return;
        }
        _profile.DigitalThreshold = Math.Clamp(value / 100.0, 0.05, 0.95);
        _profileStore.Save(_profile);
    }

    partial void OnBlockMappedKeysChanged(bool value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        if (_profile.Locked)
        {
            _suppressTuningSave = true;
            BlockMappedKeys = _profile.BlockMappedKeys;
            _suppressTuningSave = false;
            return;
        }
        _profile.BlockMappedKeys = value;
        _profileStore.Save(_profile);
        if (IsEmulationRunning)
        {
            _hook.BlockingEnabled = value;
        }
    }

    partial void OnUseRadialDeadzoneChanged(bool value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        if (_profile.Locked)
        {
            _suppressTuningSave = true;
            UseRadialDeadzone = _profile.UseRadialDeadzone;
            _suppressTuningSave = false;
            return;
        }
        _profile.UseRadialDeadzone = value;
        _profileStore.Save(_profile);
    }

    partial void OnAntiGhostInputEnabledChanged(bool value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        _hid.AntiGhostEnabled = value;
        _profileStore.SaveAntiGhostSettings(value, AntiGhostInputSeconds);
    }

    partial void OnAntiGhostInputSecondsChanged(double value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        _hid.AntiGhostSeconds = value;
        _profileStore.SaveAntiGhostSettings(AntiGhostInputEnabled, value);
    }

    partial void OnEmulationReportRateChanged(int value)
    {
        if (_suppressTuningSave)
        {
            return;
        }
        _gamepad.SetReportRate(value);
        _profileStore.SaveReportRate(value);
    }

    // ---- Preset tile callbacks ----

    private void OnTileSelect(PresetTileViewModel tile) => LoadPreset(tile.Name);

    private void OnTileToggleLock(PresetTileViewModel tile)
    {
        Profile? target = tile.Name == _profile.Name ? _profile : _profileStore.Load(tile.Name);
        if (target is null)
        {
            return;
        }

        target.Locked = !target.Locked;
        _profileStore.Save(target);
        tile.IsLocked = target.Locked;

        if (target.Name == _profile.Name)
        {
            IsCurrentPresetLocked = _profile.Locked;
        }
    }

    private void OnTileDelete(PresetTileViewModel tile)
    {
        if (PresetTiles.Count <= 1)
        {
            return;
        }
        if (tile.IsLocked)
        {
            return;
        }

        bool wasCurrent = tile.Name == _profile.Name;
        _profileStore.Delete(tile.Name);
        RefreshPresetTiles();

        if (wasCurrent)
        {
            string? next = PresetTiles.FirstOrDefault()?.Name;
            if (next is not null)
            {
                LoadPreset(next);
            }
        }
    }

    private void OnTileRename(PresetTileViewModel tile, string newName)
    {
        if (PresetTiles.Any(t => t != tile && string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        bool wasCurrent = tile.Name == _profile.Name;
        _profileStore.Rename(tile.Name, newName);
        tile.Name = newName;

        if (wasCurrent)
        {
            _profile.Name = newName;
            CurrentPresetName = newName;
            _profileStore.RememberLastActive(newName);
        }
    }

    // ---- Helpers ----

    private string GenerateUniqueName(string baseName)
    {
        string name = baseName;
        int n = 2;
        while (PresetTiles.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {n++}";
        }
        return name;
    }

    private void LoadPreset(string name)
    {
        Profile? loaded = _profileStore.Load(name);
        if (loaded is null)
        {
            return;
        }

        _profile = loaded;
        _profileStore.RememberLastActive(name);
        ApplyProfileToUiState();
        RefreshAllLabels();
        UpdateTileSelection();

        if (IsEmulationRunning)
        {
            _gamepad.SetProfile(_profile);
            RefreshBlockedKeys();
        }
    }

    private void ApplyProfileToUiState()
    {
        _suppressTuningSave = true;
        CurrentPresetName = _profile.Name;
        IsCurrentPresetLocked = _profile.Locked;
        DeadzonePercent = _profile.Deadzone * 100.0;
        DigitalThresholdPercent = _profile.DigitalThreshold * 100.0;
        BlockMappedKeys = _profile.BlockMappedKeys;
        UseRadialDeadzone = _profile.UseRadialDeadzone;
        _suppressTuningSave = false;
    }

    private void RefreshPresetTiles()
    {
        PresetTiles.Clear();

        List<string> names = _profileStore.ListPresetNames();
        if (!names.Contains(_profile.Name))
        {
            names.Add(_profile.Name);
        }

        foreach (string name in names)
        {
            bool locked = name == _profile.Name ? _profile.Locked : (_profileStore.Load(name)?.Locked ?? false);
            var tile = new PresetTileViewModel(name, locked, OnTileSelect, OnTileToggleLock, OnTileDelete, OnTileRename)
            {
                IsSelected = name == _profile.Name,
            };
            PresetTiles.Add(tile);
        }
    }

    private void UpdateTileSelection()
    {
        foreach (PresetTileViewModel tile in PresetTiles)
        {
            tile.IsSelected = tile.Name == _profile.Name;
            tile.IsLocked = tile.Name == _profile.Name ? _profile.Locked : tile.IsLocked;
        }
    }

    private void RefreshBlockedKeys()
    {
        _hook.BlockedVirtualKeys.Clear();
        foreach (KeyBinding? binding in _profile.Bindings.Values)
        {
            if (binding is not null)
            {
                _hook.BlockedVirtualKeys.Add(binding.VirtualKey);
            }
        }
    }

    private void RefreshAllLabels()
    {
        foreach (ControllerInput input in Enum.GetValues<ControllerInput>())
        {
            RefreshLabel(input);
        }
    }

    private void RefreshLabel(ControllerInput input)
    {
        string label = _profile.GetBinding(input)?.DisplayName ?? "Unbound";

        switch (input)
        {
            case ControllerInput.A: ALabel = label; break;
            case ControllerInput.B: BLabel = label; break;
            case ControllerInput.X: XLabel = label; break;
            case ControllerInput.Y: YLabel = label; break;
            case ControllerInput.LeftBumper: LeftBumperLabel = label; break;
            case ControllerInput.RightBumper: RightBumperLabel = label; break;
            case ControllerInput.LeftTrigger: LeftTriggerLabel = label; break;
            case ControllerInput.RightTrigger: RightTriggerLabel = label; break;
            case ControllerInput.Back: BackLabel = label; break;
            case ControllerInput.Start: StartLabel = label; break;
            case ControllerInput.Guide: GuideLabel = label; break;
            case ControllerInput.DpadUp: DpadUpLabel = label; break;
            case ControllerInput.DpadDown: DpadDownLabel = label; break;
            case ControllerInput.DpadLeft: DpadLeftLabel = label; break;
            case ControllerInput.DpadRight: DpadRightLabel = label; break;
            case ControllerInput.LeftStickClick: LeftStickClickLabel = label; break;
            case ControllerInput.RightStickClick: RightStickClickLabel = label; break;
            case ControllerInput.LeftStickUp: LeftStickUpLabel = label; break;
            case ControllerInput.LeftStickDown: LeftStickDownLabel = label; break;
            case ControllerInput.LeftStickLeft: LeftStickLeftLabel = label; break;
            case ControllerInput.LeftStickRight: LeftStickRightLabel = label; break;
            case ControllerInput.RightStickUp: RightStickUpLabel = label; break;
            case ControllerInput.RightStickDown: RightStickDownLabel = label; break;
            case ControllerInput.RightStickLeft: RightStickLeftLabel = label; break;
            case ControllerInput.RightStickRight: RightStickRightLabel = label; break;
        }
    }

    // ---- Live input view ----
    // Dot/trigger math mirrors GamepadOutputService's deadzone handling so the
    // preview matches what actually gets sent to the virtual controller.
    // Stick center coordinates here must match the circle positions in MainWindow.xaml.

    private const double LeftStickCenterX = 200, LeftStickCenterY = 250;
    private const double RightStickCenterX = 580, RightStickCenterY = 400;
    private const double StickDotTravel = 30;
    private const double StickDotHalfSize = 8;

    private void UpdateLiveView()
    {
        // Continuously re-derive from source of truth rather than relying only
        // on one-off assignments at toggle/checkbox-change time - guarantees
        // the checkbox can never get out of sync with actual hook behavior.
        _hook.BlockingEnabled = IsEmulationRunning && _profile.BlockMappedKeys && !_pendingStop;

        double leftX, leftY;
        double rightX, rightY;

        if (_profile.UseRadialDeadzone)
        {
            leftX = RawSignedAxis(ControllerInput.LeftStickRight, ControllerInput.LeftStickLeft);
            leftY = RawSignedAxis(ControllerInput.LeftStickUp, ControllerInput.LeftStickDown);
            ApplyCircularMapping(ref leftX, ref leftY);
            ApplyRadialDeadzone(ref leftX, ref leftY, _profile.Deadzone);

            rightX = RawSignedAxis(ControllerInput.RightStickRight, ControllerInput.RightStickLeft);
            rightY = RawSignedAxis(ControllerInput.RightStickUp, ControllerInput.RightStickDown);
            ApplyCircularMapping(ref rightX, ref rightY);
            ApplyRadialDeadzone(ref rightX, ref rightY, _profile.Deadzone);
        }
        else
        {
            leftX = SignedAxis(ControllerInput.LeftStickRight, ControllerInput.LeftStickLeft);
            leftY = SignedAxis(ControllerInput.LeftStickUp, ControllerInput.LeftStickDown);
            rightX = SignedAxis(ControllerInput.RightStickRight, ControllerInput.RightStickLeft);
            rightY = SignedAxis(ControllerInput.RightStickUp, ControllerInput.RightStickDown);
        }

        LeftStickDotX = LeftStickCenterX - StickDotHalfSize + (leftX * StickDotTravel);
        LeftStickDotY = LeftStickCenterY - StickDotHalfSize - (leftY * StickDotTravel);
        RightStickDotX = RightStickCenterX - StickDotHalfSize + (rightX * StickDotTravel);
        RightStickDotY = RightStickCenterY - StickDotHalfSize - (rightY * StickDotTravel);

        LeftTriggerFill = DeadzonedValue(ControllerInput.LeftTrigger);
        RightTriggerFill = DeadzonedValue(ControllerInput.RightTrigger);

        var pressed = new List<ControllerInput>();
        foreach (ControllerInput input in Enum.GetValues<ControllerInput>())
        {
            if (ControllerInputInfo.IsAnalog(input))
            {
                continue;
            }
            ushort? keyId = _profile.GetRawKeyId(input);
            if (keyId.HasValue && _hid.GetNormalized(keyId.Value) >= _profile.DigitalThreshold)
            {
                pressed.Add(input);
            }
        }
        PressedInputs = pressed;

        UpdateActiveKeyRows();

        if (_pendingStop && !AnyMappedKeyCurrentlyActuated())
        {
            ActuallyStopEmulation();
        }
    }

    /// <summary>
    /// Live keypress overlay: every raw key currently reporting nonzero
    /// actuation, labeled with its bound control if any, else its real name.
    /// Normal presses/releases show the real live value with no animation -
    /// the physical release itself is already the "fade". Only a key that
    /// gets nullified by Anti Ghost Input animates down to 0% over ~1.5s, as
    /// a visible cue that it was actively suppressed rather than released.
    /// </summary>
    private void UpdateActiveKeyRows()
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastLiveViewTick).TotalSeconds, 0, 0.25);
        _lastLiveViewTick = now;
        double decayPerSecond = 100.0 / KeyRowFadeSeconds;

        Dictionary<ushort, ushort> rawActive = _hid.GetRawActiveKeys();
        HashSet<ushort> suppressed = _hid.GetSuppressedKeyIds();

        foreach ((ushort keyId, ushort raw) in rawActive)
        {
            double percent = Math.Clamp(raw / (double)HidDeviceService.MaxRawTravel * 100.0, 0, 100);
            string label = FindBindingLabel(keyId) ?? KnownKeyNames.GetName(keyId);

            if (suppressed.Contains(keyId))
            {
                // Ghost-nullified: keep fading from wherever it already was,
                // don't jump back up to the (still-stuck) raw percent.
                if (_keyRowTracking.TryGetValue(keyId, out (double displayPercent, string label, bool fading) existing) && existing.fading)
                {
                    _keyRowTracking[keyId] = (existing.displayPercent, label, true);
                }
                else
                {
                    _keyRowTracking[keyId] = (percent, label, true);
                }
            }
            else
            {
                // Real, live actuation - show it directly, no animation.
                _keyRowTracking[keyId] = (percent, label, false);
            }
        }

        var toRemove = new List<ushort>();
        foreach (ushort keyId in _keyRowTracking.Keys)
        {
            (double displayPercent, string label, bool fading) tracked = _keyRowTracking[keyId];

            if (rawActive.ContainsKey(keyId))
            {
                if (tracked.fading)
                {
                    // Still stuck and still fading - keep decaying toward 0 even though the raw report hasn't changed.
                    double decayed = Math.Max(0, tracked.displayPercent - (decayPerSecond * dt));
                    _keyRowTracking[keyId] = (decayed, tracked.label, true);
                }
                continue;
            }

            if (!tracked.fading)
            {
                toRemove.Add(keyId); // genuine release - disappear immediately, no synthetic animation
                continue;
            }

            // Remove at <= 1% rather than waiting for a literal 0 - keeps the
            // fade-out from ever rendering a "0%" row for a frame before it
            // disappears (the {0:0}% format rounds anything under 0.5 down to
            // "0%", so the cutoff needs headroom above that).
            double newPercent = Math.Max(0, tracked.displayPercent - (decayPerSecond * dt));
            if (newPercent <= 1.0)
            {
                toRemove.Add(keyId);
            }
            else
            {
                _keyRowTracking[keyId] = (newPercent, tracked.label, true);
            }
        }
        foreach (ushort keyId in toRemove)
        {
            _keyRowTracking.Remove(keyId);
        }

        // Simple, final safety net: whatever the tracked state is, never
        // actually display a row that would round to "0%" - hide it instead
        // of showing a dead 0% bar. It stays in _keyRowTracking internally
        // (so decay/removal timing above is unaffected), it just doesn't
        // reach the UI for the frame(s) where it'd read as 0.
        ActiveKeyRows = _keyRowTracking.Values
            .Where(v => Math.Round(v.displayPercent, MidpointRounding.AwayFromZero) >= 1)
            .Select(v => new ActiveKeyRow(v.label, v.displayPercent))
            .OrderByDescending(r => r.Percent)
            .ToList();
    }

    private string? FindBindingLabel(ushort keyId)
    {
        foreach (KeyBinding? binding in _profile.Bindings.Values)
        {
            if (binding is not null && binding.RawKeyId == keyId)
            {
                return binding.DisplayName;
            }
        }
        return null;
    }

    private double SignedAxis(ControllerInput positive, ControllerInput negative) =>
        Math.Clamp(DeadzonedValue(positive) - DeadzonedValue(negative), -1.0, 1.0);

    private double DeadzonedValue(ControllerInput input)
    {
        ushort? keyId = _profile.GetRawKeyId(input);
        if (!keyId.HasValue)
        {
            return 0.0;
        }
        double normalized = _hid.GetNormalized(keyId.Value);
        if (normalized <= _profile.Deadzone)
        {
            return 0.0;
        }
        return (normalized - _profile.Deadzone) / (1.0 - _profile.Deadzone);
    }

    private double RawSignedAxis(ControllerInput positive, ControllerInput negative)
    {
        double pos = RawValue(positive);
        double neg = RawValue(negative);
        return Math.Clamp(pos - neg, -1.0, 1.0);
    }

    private double RawValue(ControllerInput input)
    {
        ushort? keyId = _profile.GetRawKeyId(input);
        return keyId.HasValue ? _hid.GetNormalized(keyId.Value) : 0.0;
    }

    private static void ApplyRadialDeadzone(ref double x, ref double y, double deadzone)
    {
        if (deadzone <= 0.0) return;
        
        double length = Math.Sqrt(x * x + y * y);
        if (length <= deadzone)
        {
            x = 0.0;
            y = 0.0;
            return;
        }

        double rescaledLength = (length - deadzone) / (1.0 - deadzone);
        rescaledLength = Math.Min(rescaledLength, 1.0);

        x = (x / length) * rescaledLength;
        y = (y / length) * rescaledLength;
    }

    private static void ApplyCircularMapping(ref double x, ref double y)
    {
        double mappedX = x * Math.Sqrt(1.0 - (y * y) / 2.0);
        double mappedY = y * Math.Sqrt(1.0 - (x * x) / 2.0);
        x = mappedX;
        y = mappedY;
    }

    public void Shutdown()
    {
        _liveViewTimer.Stop();
        _gamepad.Stop();
        _hook.Stop();
    }
}
