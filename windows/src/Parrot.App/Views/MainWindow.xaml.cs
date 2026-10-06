// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Parrot.App.Services;
using Parrot.App.ViewModels;

namespace Parrot.App.Views;

public partial class MainWindow : Window
{
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing || DataContext is not MainViewModel vm || !vm.IsRecording)
        {
            base.OnClosing(e);
            return;
        }
        var answer = MessageBox.Show("A call is being recorded. Stop and save it, then quit?", "Parrot",
                                     MessageBoxButton.OKCancel, MessageBoxImage.Question);
        e.Cancel = true;
        if (answer != MessageBoxResult.OK) return;
        _closing = true;
        await vm.ShutdownAsync();
        Close();
    }
}

/// ProgressBar: a negative value means "unknown" → indeterminate.
public sealed class NegativeToTrueConverter : IValueConverter
{
    public static readonly NegativeToTrueConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d && d < 0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
