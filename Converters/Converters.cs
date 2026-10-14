using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace XinputG84HE.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert)
        {
            b = !b;
        }
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}


public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "On";
    public string FalseText { get; set; } = "Off";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueBrush { get; set; }
    public Brush? FalseBrush { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PercentFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{d:0}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PressedInputsToBrushConverter : IValueConverter
{
    public Brush? PressedBrush { get; set; }
    public Brush? IdleBrush { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Collections.Generic.IReadOnlyCollection<XinputG84HE.Models.ControllerInput> pressed
            && parameter is XinputG84HE.Models.ControllerInput input)
        {
            return pressed.Contains(input) ? PressedBrush : IdleBrush;
        }
        return IdleBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Background for analog hotspots (stick directions, triggers): low-opacity red if unbound, low-opacity green if bound.</summary>
public sealed class LabelToBoundBrushConverter : IValueConverter
{
    public Brush? UnboundBrush { get; set; }
    public Brush? BoundBrush { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) == "Unbound" ? UnboundBrush : BoundBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hides the bound-key label entirely when the input is unbound, so unbound hotspots show only their glyph and color.</summary>
public sealed class LabelToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) == "Unbound" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Background for digital hotspots: red if unbound, bright green if bound and currently pressed, low-opacity green if bound and idle.</summary>
public sealed class BoundPressedMultiConverter : IMultiValueConverter
{
    public Brush? UnboundBrush { get; set; }
    public Brush? BoundIdleBrush { get; set; }
    public Brush? PressedBrush { get; set; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string label)
        {
            return UnboundBrush ?? Brushes.Transparent;
        }

        if (label == "Unbound")
        {
            return UnboundBrush ?? Brushes.Transparent;
        }

        if (values[1] is System.Collections.Generic.IReadOnlyCollection<XinputG84HE.Models.ControllerInput> pressed
            && parameter is XinputG84HE.Models.ControllerInput input
            && pressed.Contains(input))
        {
            return PressedBrush ?? Brushes.LimeGreen;
        }

        return BoundIdleBrush ?? Brushes.Green;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a bound int count is zero (e.g. an empty-state placeholder).</summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is int count && count == 0) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Reuse frozen brushes for the live keyboard: idle slate to signature green.</summary>
public sealed class KeyActuationBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush[] Fills = Enumerable.Range(0, 101).Select(percent =>
    {
        double depth = percent / 100.0;
        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(23 * (1 - depth)),
            (byte)Math.Round(29 + (200 - 29) * depth),
            (byte)Math.Round(40 + (150 - 40) * depth)));
        brush.Freeze();
        return brush;
    }).ToArray();

    private static readonly Brush[] Labels = Fills.Select(fill =>
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        Color c = fill.Color;
        double luminance = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
    }).ToArray();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int index = value is double percent && double.IsFinite(percent) ? (int)Math.Round(Math.Clamp(percent, 0, 100)) : 0;
        return parameter as string == "Label" ? Labels[index] : Fills[index];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
