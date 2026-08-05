using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableNotes.ViewModels;
using TableNotes.Views;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace TableNotes;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    private InputNonClientPointerSource? _nonClientInput;
    private readonly TabView _tabView = new() { IsAddTabButtonVisible = false };
    private readonly NotePage[] _pages = new NotePage[4];
    private TeamLeadPage? _teamLeadPage;

    public static MainWindow? Instance { get; private set; }
    public NotePage? TextFilesPage => _pages.FirstOrDefault(p => p.DataContext is NotePageViewModel vm && vm.PageName == "TextFiles");

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        _nonClientInput = InputNonClientPointerSource.GetForWindowId(windowId);

        var icon = new TextBlock
        {
            Text = "\U0001F4CB",
            FontSize = 16,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _tabView.TabStripHeader = icon;

        var spacer = new Grid { Width = 188 };
        _tabView.TabStripFooter = spacer;

        _tabView.Loaded += (_, _) => UpdateDragRegions();
        _tabView.SizeChanged += (_, _) => UpdateDragRegions();

        var pageNames = new[] { "Checklist", "Bug Tracker", "Changelog", "Text Files" };
        var viewModels = new[] { ViewModel.ChecklistVm, ViewModel.BugTrackerVm, ViewModel.ChangelogVm, ViewModel.TextFilesVm };

        for (int i = 0; i < 4; i++)
        {
            var page = new NotePage { DataContext = viewModels[i] };
            var item = new TabViewItem { Header = pageNames[i], IsClosable = false, Tag = page };
            _tabView.TabItems.Add(item);
            _pages[i] = page;
        }

        _teamLeadPage = new TeamLeadPage { DataContext = ViewModel.TeamLeadVm };
        var teamLeadItem = new TabViewItem { Header = "Team Lead", IsClosable = false, Tag = _teamLeadPage };
        _tabView.TabItems.Add(teamLeadItem);
        _teamLeadPage.Visibility = Visibility.Collapsed;
        Grid.SetRow(_teamLeadPage, 2);
        RootGrid.Children.Add(_teamLeadPage);

        _tabView.SelectionChanged += OnTabSelectionChanged;

        var menuBar = new MenuBar();

        var fileMenu = new MenuBarItem { Title = "File" };
        fileMenu.Items.Add(new MenuFlyoutItem { Text = "New Note" });
        fileMenu.Items.Add(new MenuFlyoutSeparator());
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => this.Close();
        fileMenu.Items.Add(exitItem);
        menuBar.Items.Add(fileMenu);

        var editMenu = new MenuBarItem { Title = "Edit" };
        var addRowItem = new MenuFlyoutItem { Text = "Add Row" };
        addRowItem.Click += (_, _) => GetCurrentVm()?.AddRowCommand.Execute(null);
        editMenu.Items.Add(addRowItem);
        var deleteNoteItem = new MenuFlyoutItem { Text = "Delete Note" };
        deleteNoteItem.Click += (_, _) => GetCurrentVm()?.DeleteNoteCommand.Execute(null);
        editMenu.Items.Add(deleteNoteItem);
        menuBar.Items.Add(editMenu);

        var viewMenu = new MenuBarItem { Title = "View" };
        viewMenu.Items.Add(new MenuFlyoutItem { Text = "Zoom In" });
        viewMenu.Items.Add(new MenuFlyoutItem { Text = "Zoom Out" });
        menuBar.Items.Add(viewMenu);

        var accountButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE77B", FontSize = 16 },
            Margin = new Thickness(0, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var accountMenu = new MenuFlyout();
        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Click += async (_, _) => await ShowSettingsDialog();
        accountMenu.Items.Add(settingsItem);
        accountButton.Flyout = accountMenu;

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(menuBar, 0);
        Grid.SetColumn(accountButton, 1);
        topRow.Children.Add(menuBar);
        topRow.Children.Add(accountButton);

        Grid.SetRow(_tabView, 0);
        Grid.SetRow(topRow, 1);

        RootGrid.Children.Add(_tabView);
        RootGrid.Children.Add(topRow);

        _pages[0].Visibility = Visibility.Visible;
        Grid.SetRow(_pages[0], 2);
        RootGrid.Children.Add(_pages[0]);

        for (int i = 1; i < 4; i++)
        {
            _pages[i].Visibility = Visibility.Collapsed;
            Grid.SetRow(_pages[i], 2);
            RootGrid.Children.Add(_pages[i]);
        }

        _ = ViewModel.LoadAllCommand.ExecuteAsync(null);
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedTab = (_tabView.SelectedItem as TabViewItem)?.Tag as FrameworkElement;
        foreach (var page in _pages)
            page.Visibility = ReferenceEquals(page, selectedTab) ? Visibility.Visible : Visibility.Collapsed;
        if (_teamLeadPage is not null)
            _teamLeadPage.Visibility = ReferenceEquals(_teamLeadPage, selectedTab) ? Visibility.Visible : Visibility.Collapsed;
    }

    private NotePageViewModel? GetCurrentVm()
    {
        var page = (_tabView.SelectedItem as TabViewItem)?.Tag as NotePage;
        return page?.DataContext as NotePageViewModel;
    }

    private async Task ShowSettingsDialog()
    {
        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "App settings will be available here.", TextWrapping = TextWrapping.Wrap },
                },
            },
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void UpdateDragRegions()
    {
        if (_nonClientInput is null) return;
        var w = _tabView.ActualWidth;
        if (w <= 0) return;

        var topHeight = 40;
        var captionButtonsWidth = 188;
        var captionRects = new List<RectInt32>();

        var tabPositions = new List<(double left, double right)>();
        for (int i = 0; i < _tabView.TabItems.Count; i++)
        {
            if (_tabView.ContainerFromIndex(i) is TabViewItem item)
            {
                var transform = item.TransformToVisual(null);
                var pos = transform.TransformPoint(new Point(0, 0));
                tabPositions.Add((pos.X, pos.X + item.ActualWidth));
            }
        }

        if (tabPositions.Count >= 2)
        {
            captionRects.Add(new RectInt32(0, 0, (int)tabPositions[0].left, topHeight));

            for (int i = 0; i < tabPositions.Count - 1; i++)
            {
                var gapStart = (int)tabPositions[i].right;
                var gapEnd = (int)tabPositions[i + 1].left;
                var gapWidth = gapEnd - gapStart;
                if (gapWidth > 0)
                    captionRects.Add(new RectInt32(gapStart, 0, gapWidth, topHeight));
            }

            var rightStart = (int)tabPositions[^1].right;
            var rightWidth = (int)w - rightStart - captionButtonsWidth;
            if (rightWidth > 0)
                captionRects.Add(new RectInt32(rightStart, 0, rightWidth, topHeight));
        }
        else
        {
            captionRects.Add(new RectInt32(0, 0, 60, topHeight));
            var rw = (int)w - captionButtonsWidth;
            if (rw > 60)
                captionRects.Add(new RectInt32(rw, 0, captionButtonsWidth, topHeight));
        }

        _nonClientInput.SetRegionRects(NonClientRegionKind.Caption, captionRects.ToArray());
    }
}
