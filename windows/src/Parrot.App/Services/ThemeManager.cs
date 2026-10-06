// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Views/Theme.swift.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Parrot.Core.Storage;

namespace Parrot.App.Services;

/// Swaps the Light/Dark color dictionary. "System" follows Windows' app mode.
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(ThemePreference preference)
    {
        IsDark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemUsesDarkMode(),
        };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative),
        };
        var existing = dictionaries.FirstOrDefault(d => d.Source != null &&
            (d.Source.OriginalString.EndsWith("Light.xaml") || d.Source.OriginalString.EndsWith("Dark.xaml")));
        if (existing != null) dictionaries[dictionaries.IndexOf(existing)] = colors; else dictionaries.Insert(0, colors);
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    public static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// Dark title bar on Windows 10 20H1+ / 11 (DWMWA_USE_IMMERSIVE_DARK_MODE = 20).
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var value = IsDark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)); } catch (Exception) { }
    }
}
