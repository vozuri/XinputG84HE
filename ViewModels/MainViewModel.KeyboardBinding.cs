using CommunityToolkit.Mvvm.Input;
using XinputG84HE.Models;
using XinputG84HE.Services;

namespace XinputG84HE.ViewModels;

public partial class MainViewModel
{
    private KeyboardKeyViewModel? _selectedKeyboardKey;
    private ControllerInput? _pendingControllerInput;
    private int _bindingGeneration;

    [RelayCommand]
    private void CancelBind()
    {
        ++_bindingGeneration;
        _bindingCapture.CancelCapture();
        ClearBindingSelection();
        BindingStatusText = "Cancelled - no changes made.";
    }

    private void ClearBindingSelection()
    {
        if (_selectedKeyboardKey is not null) _selectedKeyboardKey.IsSelected = false;
        _selectedKeyboardKey = null;
        _pendingControllerInput = null;
        IsBinding = false;
    }

    [RelayCommand]
    private void SelectKeyboardKey(KeyboardKeyViewModel key)
    {
        if (_profile.Locked || IsCapturingHotkey) return;

        if (_pendingControllerInput.HasValue)
        {
            _bindingCapture.CompleteWithBinding(VirtualKeyboardBinding.Create(key.Key.RawKeyId, _profile));
            return;
        }

        bool deselect = ReferenceEquals(_selectedKeyboardKey, key);
        CancelBind();
        if (deselect) return;
        _selectedKeyboardKey = key;
        key.IsSelected = true;
        IsBinding = true;
        BindingStatusText = $"{KnownKeyNames.GetName(key.Key.RawKeyId)} selected - click a controller input. Right-click to cancel.";
    }

    [RelayCommand]
    private async Task StartBindAsync(ControllerInput input)
    {
        if (_profile.Locked || IsCapturingHotkey) return;

        if (_selectedKeyboardKey is not null)
        {
            var binding = VirtualKeyboardBinding.Create(_selectedKeyboardKey.Key.RawKeyId, _profile);
            ClearBindingSelection();
            ApplyBinding(input, binding);
            return;
        }
        if (IsBinding) return;

        var owner = _profile;
        int generation = ++_bindingGeneration;
        _pendingControllerInput = input;
        IsBinding = true;
        BindingStatusText = $"Press a key or click one below for {ControllerInputInfo.ShortLabel(input)}. Esc unbinds; right-click cancels.";
        try
        {
            CaptureResult result = await _bindingCapture.CaptureNextKeyAsync(TimeSpan.FromSeconds(6));
            // A preset switch or lock invalidates this assignment even if a key
            // completed the capture just before the UI processed that action.
            if (generation != _bindingGeneration || !ReferenceEquals(owner, _profile) || owner.Locked) return;
            if (result.Outcome == CaptureOutcome.Success) ApplyBinding(input, result.Binding);
            else if (result.Outcome == CaptureOutcome.Unbind) ApplyBinding(input, null);
            else BindingStatusText = "Cancelled - no changes made.";
        }
        finally
        {
            if (generation == _bindingGeneration) ClearBindingSelection();
        }
    }

    [RelayCommand]
    private void Unbind(ControllerInput input)
    {
        if (_profile.Locked) return;
        CancelBind();
        ApplyBinding(input, null);
    }

    private void ApplyBinding(ControllerInput input, KeyBinding? binding)
    {
        _profile.Bindings[input] = binding;
        _profileStore.Save(_profile);
        RefreshLabel(input);
        RefreshKeyboardMappings();
        BindingStatusText = binding is null
            ? $"{ControllerInputInfo.ShortLabel(input)} unbound."
            : $"{ControllerInputInfo.ShortLabel(input)} bound to {binding.DisplayName}.";
        if (IsEmulationRunning) RefreshBlockedKeys();
    }

    private void RefreshKeyboardMappings()
    {
        foreach (var key in KeyboardKeys) key.UpdateMappings(_profile);
    }
}
