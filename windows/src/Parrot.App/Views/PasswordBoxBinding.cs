// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Windows;
using System.Windows.Controls;

namespace Parrot.App.Views;

/// Lets a PasswordBox two-way bind its text (PasswordBox.Password isn't a dependency property).
public static class PasswordBoxBinding
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(PasswordBoxBinding));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!(bool)box.GetValue(HookedProperty))
        {
            box.SetValue(HookedProperty, true);
            box.PasswordChanged += (_, _) => SetText(box, box.Password);
        }
        var value = e.NewValue as string ?? "";
        if (box.Password != value) box.Password = value;
    }
}
