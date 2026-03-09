using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ListBox = System.Windows.Controls.ListBox;

namespace RadioV2.Helpers;

public class InfiniteScrollBehavior : Behavior<ListBox>
{
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(nameof(LoadMoreCommand), typeof(ICommand), typeof(InfiniteScrollBehavior));

    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    private ScrollViewer? _scrollViewer;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(AssociatedObject);
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null) return;

        // Auto-fill: content doesn't yet fill the viewport — load more without requiring scroll.
        if (_scrollViewer.ScrollableHeight == 0)
        {
            if (LoadMoreCommand?.CanExecute(null) == true)
                LoadMoreCommand.Execute(null);
            return;
        }

        // User-triggered: scrolled past the near-bottom threshold.
        // VerticalOffset > 0 guard prevents firing at the top when ScrollableHeight is small.
        if (_scrollViewer.VerticalOffset > 0 &&
            _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 200)
        {
            if (LoadMoreCommand?.CanExecute(null) == true)
                LoadMoreCommand.Execute(null);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}

/// <summary>
/// Infinite scroll behavior for a plain ScrollViewer (e.g. the groups WrapPanel on DiscoverPage).
/// </summary>
public class ScrollViewerInfiniteScrollBehavior : Behavior<ScrollViewer>
{
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(nameof(LoadMoreCommand), typeof(ICommand), typeof(ScrollViewerInfiniteScrollBehavior));

    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ScrollChanged += OnScrollChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.ScrollChanged -= OnScrollChanged;
        base.OnDetaching();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var sv = AssociatedObject;

        // VerticalOffset > 0 guard prevents firing at the top when ScrollableHeight is small.
        if (sv.ScrollableHeight > 0 &&
            sv.VerticalOffset > 0 &&
            sv.VerticalOffset >= sv.ScrollableHeight - 200)
        {
            if (LoadMoreCommand?.CanExecute(null) == true)
                LoadMoreCommand.Execute(null);
        }
    }
}
