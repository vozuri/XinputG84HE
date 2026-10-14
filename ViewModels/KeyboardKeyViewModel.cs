using CommunityToolkit.Mvvm.ComponentModel;
using XinputG84HE.Models;

namespace XinputG84HE.ViewModels;

public partial class KeyboardKeyViewModel : ObservableObject
{
    public KeyboardKey Key { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActuationText))]
    private double percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActuationText))]
    private bool isMapped;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActuationText))]
    private string mappedInputs = "";

    public string ActuationText => $"{KnownKeyNames.GetName(Key.RawKeyId)} · {Percent:0}%"
        + (IsMapped ? $" · {MappedInputs}" : "");

    public void UpdateMappings(Profile profile)
    {
        IsMapped = profile.Bindings.Any(pair => pair.Value?.RawKeyId == Key.RawKeyId);
        MappedInputs = string.Join(", ", profile.Bindings
            .Where(pair => pair.Value?.RawKeyId == Key.RawKeyId)
            .Select(pair => ControllerInputInfo.ShortLabel(pair.Key)));
    }

    public KeyboardKeyViewModel(KeyboardKey key) => Key = key;
}
