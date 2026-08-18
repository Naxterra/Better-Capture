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
    private bool _updatingPeriodFilters;
    private string _root;

    internal LibraryWindow(string root, Action<string> openEditor)
    {
        _root = root;
        _openEditor = openEditor;
        InitializeComponent();
        ApplyLocalization();
        WindowAppearanceService.ApplyDarkTitleBar(this);
        Title = Localizer.Get("LibraryWindowTitle");
        WindowAppearanceService.ApplyAppIcon(this);
        AppWindow.Resize(new SizeInt32(1120, 760));
        CenterWindow();
        LibraryGrid.ItemsSource = _visibleItems;
        LibraryRootText.Text = _root;
        Closed += OnClosed;
        _ = RefreshAsync();
    }

    private void ApplyLocalization()
    {
        LibraryHeadingText.Text = Localizer.Get("LibraryWindowHeading/Text");
        SearchBox.PlaceholderText = Localizer.Get("LibrarySearchBox/PlaceholderText");
        EditSelectedButton.Content = Localizer.Get("LibraryEdit/Content");
        OpenSelectedButton.Content = Localizer.Get("LibraryOpen/Content");
        RefreshButton.Content = Localizer.Get("LibraryRefresh/Content");
        YearLabelText.Text = Localizer.Get("LibraryYearLabel/Text");
        MonthLabelText.Text = Localizer.Get("LibraryMonthLabel/Text");
        EmptyTitleText.Text = Localizer.Get("LibraryEmpty/Text");
        EmptyHintText.Text = Localizer.Get("LibraryEmptyHint/Text");
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
            UpdatePeriodFilters();
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
        IReadOnlyList<LibraryItemViewModel> matches =
            YearFilter.SelectedItem is int year &&
            MonthFilter.SelectedItem is LibraryMonthOption month
                ? _allItems.Where(item => item.CaptureYear == year && item.CaptureMonth == month.Number).ToArray()
                : [];
        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(item =>
                item.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.SourceApplication.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                System.IO.Path.GetFileName(item.Path).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
        }

        _visibleItems.Clear();
        foreach (var item in matches)
        {
            _visibleItems.Add(item);
        }

        EmptyState.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryStatusText.Text = Localizer.Format("LibraryItemCount", _visibleItems.Count);
    }

    private void UpdatePeriodFilters()
    {
        var previousYear = YearFilter.SelectedItem is int year ? year : (int?)null;
        var previousMonth = (MonthFilter.SelectedItem as LibraryMonthOption)?.Number;
        var years = _allItems
            .Select(item => item.CaptureYear)
            .Distinct()
            .OrderDescending()
            .ToArray();

        _updatingPeriodFilters = true;
        YearFilter.ItemsSource = years;
        YearFilter.SelectedItem = previousYear is not null && years.Contains(previousYear.Value)
            ? previousYear.Value
            : years.FirstOrDefault();
        PopulateMonthFilter(previousMonth);
        _updatingPeriodFilters = false;
    }

    private void PopulateMonthFilter(int? preferredMonth)
    {
        if (YearFilter.SelectedItem is not int year)
        {
            MonthFilter.ItemsSource = Array.Empty<LibraryMonthOption>();
            MonthFilter.SelectedItem = null;
            return;
        }

        var months = _allItems
            .Where(item => item.CaptureYear == year)
            .Select(item => item.CaptureMonth)
            .Distinct()
            .OrderDescending()
            .Select(month => new LibraryMonthOption(
                month,
                $"{Localizer.CurrentCulture.DateTimeFormat.GetMonthName(month)} ({month:00})"))
            .ToArray();
        MonthFilter.ItemsSource = months;
        MonthFilter.SelectedItem = preferredMonth is not null
            ? months.FirstOrDefault(month => month.Number == preferredMonth.Value) ?? months.FirstOrDefault()
            : months.FirstOrDefault();
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

    private void YearFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPeriodFilters)
        {
            return;
        }

        _updatingPeriodFilters = true;
        PopulateMonthFilter(preferredMonth: null);
        _updatingPeriodFilters = false;
        ApplyFilter();
    }

    private void MonthFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingPeriodFilters)
        {
            ApplyFilter();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }

    private sealed record LibraryMonthOption(int Number, string Display);
}
