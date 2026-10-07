// Parrot for Windows. Derived from Parrot (GPL-3.0).
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Parrot.App.ViewModels;

namespace Parrot.App.Views;

public partial class LiveView : UserControl
{
    public LiveView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LiveViewModel old) old.Lines.CollectionChanged -= OnLinesChanged;
            if (e.NewValue is LiveViewModel vm)
            {
                vm.Lines.CollectionChanged += OnLinesChanged;
                ScrollToEnd(force: true);
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is LiveViewModel vm) vm.Lines.CollectionChanged -= OnLinesChanged;
        };
        Loaded += (_, _) =>
        {
            if (DataContext is LiveViewModel vm)
            {
                vm.Lines.CollectionChanged -= OnLinesChanged;
                vm.Lines.CollectionChanged += OnLinesChanged;
            }
            ScrollToEnd(force: true);
        };
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd(force: false);
    }

    /// Follows new lines only when the user is already at the bottom (reading back stays put).
    private void ScrollToEnd(bool force)
    {
        if (TranscriptList.Items.Count == 0) return;
        var viewer = FindScrollViewer(TranscriptList);
        var atBottom = viewer == null || viewer.VerticalOffset >= viewer.ScrollableHeight - 40;
        if (force || atBottom) TranscriptList.ScrollIntoView(TranscriptList.Items[^1]);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }
}
