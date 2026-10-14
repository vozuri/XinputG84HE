using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XinputG84HE.ViewModels;

public partial class PresetTileViewModel : ObservableObject
{
    public bool IsAutosave { get; }
    [ObservableProperty] private string name;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isLocked;
    [ObservableProperty] private bool isEditingName;
    [ObservableProperty] private string editBuffer = "";

    private readonly Action<PresetTileViewModel> _onSelect;
    private readonly Action<PresetTileViewModel> _onToggleLock;
    private readonly Action<PresetTileViewModel> _onDelete;
    private readonly Action<PresetTileViewModel, string> _onRename;

    public PresetTileViewModel(
        string name,
        bool isLocked,
        Action<PresetTileViewModel> onSelect,
        Action<PresetTileViewModel> onToggleLock,
        Action<PresetTileViewModel> onDelete,
        Action<PresetTileViewModel, string> onRename,
        bool isAutosave = false)
    {
        this.name = name;
        IsAutosave = isAutosave;
        this.isLocked = !isAutosave && isLocked;
        _onSelect = onSelect;
        _onToggleLock = onToggleLock;
        _onDelete = onDelete;
        _onRename = onRename;
    }

    [RelayCommand]
    private void Select() => _onSelect(this);

    [RelayCommand]
    private void ToggleLock() { if (!IsAutosave) _onToggleLock(this); }

    [RelayCommand]
    private void Delete() { if (!IsAutosave) _onDelete(this); }

    [RelayCommand]
    private void BeginRename()
    {
        if (IsLocked || IsAutosave)
        {
            return;
        }
        EditBuffer = Name;
        IsEditingName = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsEditingName || IsAutosave)
        {
            return;
        }
        IsEditingName = false;

        string trimmed = EditBuffer.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != Name)
        {
            _onRename(this, trimmed);
        }
    }

    [RelayCommand]
    private void CancelRename() => IsEditingName = false;
}
