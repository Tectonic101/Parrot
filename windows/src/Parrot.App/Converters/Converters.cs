// Parrot for Windows. Derived from Parrot (GPL-3.0), KindStyle.swift (adaptive kind colors).
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Parrot.App.Services;

namespace Parrot.App.Converters;

/// true → Visible. ConverterParameter="Invert" flips it. Non-bool: non-null/non-empty → Visible.
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "Invert") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
}

/// Profile kind hex → brush; the preset hexes map to the same light/dark pairs as the Mac.
public sealed class KindBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (uint Light, uint Dark)> Adaptive = new()
    {
        ["4F6FB0"] = (0x4F6FB0, 0x8AA0D0), // blue — suggestions/answers
        ["2F7E96"] = (0x2F7E96, 0x57AEC6), // teal — questions/next steps
        ["E8943A"] = (0xE8943A, 0xE8A85C), // orange — blockers/objections
        ["3F9168"] = (0x3F9168, 0x5BBE8C), // green — actions/commitments
        ["5F6470"] = (0x5F6470, 0xA0A4AD), // gray — notes/feedback
        ["C0563B"] = (0xC0563B, 0xD9805F), // terracotta — unanswered question
        ["7A5FB0"] = (0x7A5FB0, 0xA58FD0), // purple — opportunity
        ["C29218"] = (0xC29218, 0xD8AE4A), // gold — "Ask this next"
        ["888888"] = (0x888888, 0xB0B0B0),
    };

    public static Brush BrushFor(string? hex)
    {
        var key = (hex ?? "").TrimStart('#').ToUpperInvariant();
        uint rgb;
        if (Adaptive.TryGetValue(key, out var pair)) rgb = ThemeManager.IsDark ? pair.Dark : pair.Light;
        else if (key.Length != 6 || !uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) rgb = 0x888888;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => BrushFor(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
