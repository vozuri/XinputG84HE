using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace XinputG84HE.ViewModels;

public partial class CurvePointViewModel : ObservableObject
{
    public int X { get; }
    
    [ObservableProperty]
    private double y;

    public CurvePointViewModel(int x, double y)
    {
        X = x;
        this.y = y;
    }

    partial void OnYChanged(double value)
    {
        YChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? YChanged;
}
