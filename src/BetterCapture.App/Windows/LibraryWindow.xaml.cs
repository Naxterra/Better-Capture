using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using BetterCapture.App.Library;
using BetterCapture.App.Services;
using Windows.Graphics;
using Windows.System;

namespace BetterCapture.App.Windows;

internal sealed partial class LibraryWindow : Window
{
    private readonly Action<string> _openEditor;
    private readonly ObservableCollection<LibraryItemViewModel> _visibleItems = [];
    private IReadOnlyList<LibraryItemViewModel> _allItems = [];
    private CancellationTokenSource? _refreshCancellation;
    private string _root;

    internal LibraryWindow(string root, Action<string> openEditor)
    {
        _root = root;
        _openEditor = openEditor;
        InitializeComponent();
        WindowAppearanceService.ApplyDarkTitleBar(this);
        Title = Localizer.Get("LibraryWindowTitle");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1120, 760));
        CenterWindow();
        LibraryGrid.ItemsSource = _visibleItems;
        LibraryRootText.Text = _root;
        Closed += OnClosed;
        _ = RefreshAsync();
    }

    internal async Task SetRootAndRefreshAsync(string root)
    {
        _root = root;
        LibraryRootText.Text = root;
        await RefreshAsync();
    }

    internal async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            _allItems = await LibraryScanner.ScanAsync(_root, cancellationToken);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void CenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is null)
        {
            return;
        }

        var workArea = area.WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - 1120) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - 760) / 2)));
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.SourceApplication.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                System.IO.Path.GetFileName(item.Path).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        _visibleItems.Clear();
        foreach (var item in matches)
        {
            _visibleItems.Add(item);
        }

        EmptyState.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryStatusText.Text = Localizer.Format("LibraryItemCount", _visibleItems.Count);
    }

    private async void LibraryGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is LibraryItemViewModel item)
        {
            await item.EnsureThumbnailAsync();
        }
    }

    private void LibraryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = LibraryGrid.SelectedItem as LibraryItemViewModel;
        EditSelectedButton.IsEnabled = item?.CanEdit == true;
        OpenSelectedButton.IsEnabled = item is not null;
    }

    private async void LibraryGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is LibraryItemViewModel item)
        {
            await OpenItemAsync(item);
        }
    }

    private void EditSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is LibraryItemViewModel { CanEdit: true } item)
        {
            _openEditor(item.Path);
        }
    }

    private async void OpenSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is LibraryItemViewModel item)
        {
            await OpenItemAsync(item);
        }
    }

    private async Task OpenItemAsync(LibraryItemViewModel item)
    {
        if (item.CanEdit)
        {
            _openEditor(item.Path);
            return;
        }

        var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
        await Launcher.LaunchFileAsync(file);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }
}
