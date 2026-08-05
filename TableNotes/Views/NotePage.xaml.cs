using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TableNotes.Models;
using TableNotes.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Storage;
using WinUI.TableView;

namespace TableNotes.Views;

public sealed partial class NotePage : UserControl
{
    private TableView? _tableView;
    private ColumnDef[] _columns = [];
    private TreeViewNode? _rootNode;
    private bool _sidebarExpanded = true;
    private readonly Dictionary<Guid, TreeViewNode> _noteNodeMap = new();
    private readonly Dictionary<TableRow, HashSet<string>> _modifiedCells = new();
    private readonly Dictionary<TableRow, HashSet<string>> _passedCells = new();
    private readonly Dictionary<TableRow, HashSet<string>> _yellowCells = new();
    private static readonly Dictionary<string, string> _savedMarkings = new();
    private static readonly string _markingsPath = Path.Combine(
        Services.DataPaths.BasePath, "cell_markings.json");
    private readonly Dictionary<TableRow, HashSet<string>> _diffCells = new();
    private bool _diffVisualsPending;
    private bool _rowsCleared;
    private readonly Dictionary<(string NoteFileName, string RowNumber), (Guid NoteId, int RowIndex, string ColumnProp, TableNote ChangelogNote)> _changelogRevertMap = new();
    private readonly Dictionary<(Guid NoteId, int RowIndex, string ColumnProp), (TableRow ChangelogRow, TableNote ChangelogNote)> _changelogReverseMap = new();
    private TableRow? _selectedBugRow;
    private ComboBox? _bugFormType;
    private Segmented? _bugFormStatusSelector;
    private TextBox? _bugFormSummary;
    private TextBox? _bugFormDesc;
    private TextBox? _bugFormSteps;
    private Segmented? _bugFormFrench;
    private Segmented? _bugFormItalian;
    private Segmented? _bugFormGerman;
    private Segmented? _bugFormSpanish;
    private TextBox? _bugFormFrenchObserved;
    private TextBox? _bugFormFrenchExpected;
    private TextBox? _bugFormItalianObserved;
    private TextBox? _bugFormItalianExpected;
    private TextBox? _bugFormGermanObserved;
    private TextBox? _bugFormGermanExpected;
    private TextBox? _bugFormSpanishObserved;
    private TextBox? _bugFormSpanishExpected;
    private TextBox? _bugFormGlobalObserved;
    private TextBox? _bugFormGlobalExpected;
    private CheckBox? _bugFormNeedsLangSpecific;
    private TextBlock? _bugFormId;
    private TextBlock? _bugFormUsername;
    private TextBlock? _bugFormDate;
    private readonly Dictionary<string, ItemsControl> _bugLangScreenshotPreviews = new();
    private readonly Dictionary<string, List<string>> _bugLangScreenshots = new();
    private readonly Dictionary<string, StackPanel> _bugLangScreenshotZones = new();
    private readonly Dictionary<string, Border> _bugLangScreenshotOverlays = new();
    private Grid? _bugLangScreenshotGrid;
    private static readonly string[] _langKeys = { "FR", "IT", "DE", "ES" };
    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff"
    };
    private static readonly string _bugScreenshotsDir = Path.Combine(
        Path.GetFullPath(Path.Combine(Services.DataPaths.BasePath, "..")),
        "Screenshots");
    private readonly Services.ScreenshotStore _screenshotStore =
        new(Path.Combine(Services.DataPaths.BasePath, "BugTracker"));

    private string _bugSortColumn = "Col1";
    private bool _bugSortAscending = true;
    private string _bugFilterText = string.Empty;
    private string _bugQuickSearchText = string.Empty;
    private HashSet<string> _bugStatusFilterSet = new();
    private HashSet<string> _summaryFilterSet = new();
    private TextBlock? _numSortArrow;
    private TextBlock? _summarySortArrow;
    private TextBlock? _bugStatusSortArrow;
    private StackPanel? _bugItemsPanel;
    private Grid? _bugHeaderGrid;

    static NotePage()
    {
        try
        {
            if (File.Exists(_markingsPath))
            {
                var json = File.ReadAllText(_markingsPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data is not null)
                    foreach (var kvp in data)
                        _savedMarkings[kvp.Key] = kvp.Value;
            }
        }
        catch { }
    }

    private static void SaveMarkings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_markingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_markingsPath, JsonSerializer.Serialize(_savedMarkings));
        }
        catch { }
    }

    private sealed class TreeNoteItem
    {
        public string DisplayText { get; set; } = string.Empty;
        public required TableNote Note { get; init; }
        public override string ToString() => DisplayText;
    }

    public NotePage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private record ColumnDef(string Header, string Prop);

    private static ColumnDef[] GetColumnDefs(string pageName) => pageName switch
    {
        "Checklist" =>
        [
            new("String ID", "Col1"),
            new("Source", "Col2"),
            new("Steps to Reproduce", "Col3"),
            new("French", "Col4"),
            new("Italian", "Col5"),
            new("German", "Col6"),
            new("Spanish", "Col7"),
        ],
        "Changelog" =>
        [
            new("#", "Col1"),
            new("Tester", "Col2"),
            new("Date", "Col3"),
            new("Type", "Col4"),
            new("Description", "Col5"),
            new("String ID", "Col6"),
            new("Source", "Col7"),
            new("Actual Result", "Col8"),
            new("Expected Result", "Col9"),
            new("Status", "Col10"),
        ],
        "BugTracker" =>
        [
            new("#", "Col1"),
            new("Username", "Col2"),
            new("Date", "Col3"),
            new("Type", "Col4"),
            new("Summary", "Col5"),
            new("Description", "Col6"),
            new("Steps to Reproduce", "Col7"),
            new("French", "Col8"),
            new("Italian", "Col9"),
            new("German", "Col10"),
            new("Spanish", "Col11"),
            new("Bug Status", "Col12"),
        ],
        "TextFiles" =>
        [
            new("String ID", "Col1"),
            new("Source", "Col2"),
            new("French", "Col3"),
            new("Italian", "Col4"),
            new("German", "Col5"),
            new("Spanish", "Col6"),
        ],
        _ =>
        [
            new("Column 1", "Col1"),
            new("Column 2", "Col2"),
            new("Column 3", "Col3"),
            new("Column 4", "Col4"),
            new("Column 5", "Col5"),
        ]
    };

    private NotePageViewModel? GetVm() => DataContext as NotePageViewModel;

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is NotePageViewModel vm)
        {
            if (vm.IsChangelog || vm.IsTextFiles || vm.IsBugTracker)
            {
                NewNoteBtn.Visibility = Visibility.Collapsed;
                DeleteNoteBtn.Visibility = Visibility.Collapsed;
            }

            SetupSidebar(vm);
            SetupTableView(vm);
            RegisterMessenger(vm);
        }
    }

    private void RegisterMessenger(NotePageViewModel vm)
    {
        WeakReferenceMessenger.Default.Unregister<EditSavedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<RevertCellMessage>(this);
        WeakReferenceMessenger.Default.Unregister<SetChangelogPendingMessage>(this);

        if (vm.PageName == "Checklist")
        {
            vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                    _rowsCleared = true;
                else if (_rowsCleared && args.Action == NotifyCollectionChangedAction.Add)
                {
                    _rowsCleared = false;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, RestoreCellMarkings);
                }
            };

            WeakReferenceMessenger.Default.Register<RevertCellMessage>(this, async (r, m) =>
            {
                var checklistVm = GetVm();
                if (checklistVm?.PageName != "Checklist") return;

                var checklistNote = checklistVm.Notes.FirstOrDefault(n => n.Id == m.NoteId);
                if (checklistNote is null) return;

                var noteRows = await checklistVm.ExcelService.LoadRowsAsync(checklistNote);
                if (m.RowIndex < 0 || m.RowIndex >= noteRows.Count) return;
                var checklistRow = noteRows[m.RowIndex];

                if (m.Reapply)
                {
                    if (checklistNote == checklistVm.SelectedNote)
                        MarkCellRed(checklistRow, m.ColumnProp);

                    var fileName = Path.GetFileName(checklistNote.FileName);
                    var textFilesVm = MainWindow.Instance?.ViewModel.TextFilesVm;
                    if (textFilesVm is not null)
                    {
                        await UpdateTextFilesCell(textFilesVm, fileName, checklistRow.Col1, m.ColumnProp, m.ExpectedResult);
                    }
                }
                else
                {
                    if (checklistNote == checklistVm.SelectedNote)
                    {
                        if (m.RowIndex >= 0 && m.RowIndex < checklistVm.Rows.Count)
                        {
                            var displayedRow = checklistVm.Rows[m.RowIndex];
                            RemoveRed(displayedRow, m.ColumnProp);
                            RemoveGreen(displayedRow, m.ColumnProp);
                            RemoveYellow(displayedRow, m.ColumnProp);
                            FindAndColorCell(displayedRow, m.ColumnProp, null);
                        }
                    }
                }
            });
        }
        else if (vm.PageName is "TextFiles")
        {
            vm.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                    _rowsCleared = true;
                else if (_rowsCleared && args.Action == NotifyCollectionChangedAction.Add)
                {
                    _rowsCleared = false;
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, RestoreCellMarkings);
                }
            };
        }
        else if (vm.PageName == "Changelog")
        {
            vm.Rows.CollectionChanged += (_, _) => ComputeDiff();

            WeakReferenceMessenger.Default.Register<EditSavedMessage>(this, async (r, m) =>
            {
                var changelogVm = GetVm();
                if (changelogVm is null) return;
                await changelogVm.AddChangelogEntry(m.Type, m.Description, m.StringId, m.Source, m.ActualResult, m.ExpectedResult, m.ColumnProp);

                var noteTitle = NotePageViewModel.GetChangelogNoteTitle(m.ColumnProp);
                var note = changelogVm.Notes.FirstOrDefault(n => n.Title == noteTitle);
                if (note is null) return;

                var key = (m.ChecklistNoteId, m.RowIndex, m.ColumnProp);
                if (changelogVm.SelectedNote == note)
                {
                    if (changelogVm.Rows.Count > 0)
                    {
                        var newRow = changelogVm.Rows[^1];
                        _changelogRevertMap[(note.FileName, newRow.Col1)] = (m.ChecklistNoteId, m.RowIndex, m.ColumnProp, note);
                        _changelogReverseMap[key] = (newRow, note);
                    }
                    ComputeDiff();
                }
                else
                {
                    var rows = await changelogVm.ExcelService.LoadRowsAsync(note);
                    if (rows.Count > 0)
                    {
                        var newRow = rows[^1];
                        _changelogRevertMap[(note.FileName, newRow.Col1)] = (m.ChecklistNoteId, m.RowIndex, m.ColumnProp, note);
                        _changelogReverseMap[key] = (newRow, note);
                    }
                }
            });

            WeakReferenceMessenger.Default.Register<SetChangelogPendingMessage>(this, async (r, m) =>
            {
                var changelogVm = GetVm();
                if (changelogVm is null) return;
                var key = (m.NoteId, m.RowIndex, m.ColumnProp);
                if (!_changelogReverseMap.TryGetValue(key, out var entry)) return;

                var (changelogRow, changelogNote) = entry;
                changelogRow.Col10 = "Pending";

                var rows = await changelogVm.ExcelService.LoadRowsAsync(changelogNote);
                var found = rows.FirstOrDefault(row => row.Col1 == changelogRow.Col1);
                if (found is not null)
                    found.Col10 = "Pending";

                await changelogVm.ExcelService.SaveRowsAsync(changelogNote, rows);

                if (changelogVm.SelectedNote == changelogNote)
                {
                    changelogVm.Rows.Clear();
                    foreach (var row2 in rows)
                        changelogVm.Rows.Add(row2);
                }
            });
        }
    }

    private void OnBugTrackerCellEditEnded(object? sender, TableViewCellEditEndedEventArgs e)
    {
        if (e.EditAction != TableViewEditAction.Commit || e.DataItem is not TableRow row)
            return;

        var header = e.Column.Header as string;

        if (header is "French" or "Italian" or "German" or "Spanish")
        {
            var prop = e.Column.Tag as string ?? "";
            if (row.GetType().GetProperty(prop)?.GetValue(row) as string == "Affected")
                MarkCellRed(row, prop);
            else
            {
                RemoveRed(row, prop);
                RemoveYellow(row, prop);
                MarkGreen(row, prop);
            }
        }

        RebuildBugTrackerRows();
    }

    private void OnChangelogCellEditEnded(object? sender, TableViewCellEditEndedEventArgs e)
    {
        if (e.EditAction != TableViewEditAction.Commit || e.DataItem is not TableRow row)
            return;

        var header = e.Column.Header as string;
        var changelogVm = GetVm();

        if (header == "Status" && changelogVm?.SelectedNote is not null)
        {
            var revertKey = (changelogVm.SelectedNote.FileName, row.Col1);
            if (_changelogRevertMap.TryGetValue(revertKey, out var revertInfo))
            {
                if (row.Col10 == "Pending")
                    WeakReferenceMessenger.Default.Send(new RevertCellMessage(revertInfo.NoteId, revertInfo.RowIndex, revertInfo.ColumnProp));
                else if (row.Col10 == "Changed")
                    WeakReferenceMessenger.Default.Send(new RevertCellMessage(revertInfo.NoteId, revertInfo.RowIndex, revertInfo.ColumnProp, Reapply: true, ExpectedResult: row.Col9));
            }
        }
        else if (header == "String ID")
        {
            var newStringId = row.Col6;
            if (string.IsNullOrEmpty(newStringId)) return;

            var checklistVm = MainWindow.Instance?.ViewModel.ChecklistVm;
            if (checklistVm is null) return;

            var foundRow = checklistVm.Rows.FirstOrDefault(r => r.Col1 == newStringId);
            if (foundRow is null) return;

            if (GetVm() is { } vm && vm.SelectedNote is not null)
            {
                var revertKey = (vm.SelectedNote.FileName, row.Col1);
                if (_changelogRevertMap.TryGetValue(revertKey, out var revInfo))
                {
                    var prop = typeof(TableRow).GetProperty(revInfo.ColumnProp);
                    var val = prop?.GetValue(foundRow) as string ?? string.Empty;
                    row.Col7 = foundRow.Col2;
                    row.Col8 = val;
                    row.Col9 = val;
                }
                else
                {
                    row.Col7 = foundRow.Col2;
                }

                vm.SaveNoteCommand.Execute(null);
            }
        }

        ComputeDiff();
    }

    private void SetupSidebar(NotePageViewModel vm)
    {
        if (vm.IsBugTracker)
        {
            NotesList.Visibility = Visibility.Collapsed;
            NotesTree.Visibility = Visibility.Collapsed;
            BugTrackerSidebar.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            SaveNoteBtn.Visibility = Visibility.Collapsed;
            AddRowBtn.Visibility = Visibility.Collapsed;
            SetSidebarWidth(_sidebarExpanded);
            PopulateBugTrackerSidebar(vm);
            vm.Rows.CollectionChanged += (_, _) => PopulateBugTrackerSidebar(vm);
            return;
        }

        NotesList.Visibility = vm.ShowTreeView ? Visibility.Collapsed : Visibility.Visible;
        NotesTree.Visibility = vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
        SaveNoteBtn.Visibility = Visibility.Visible;
        AddRowBtn.Visibility = Visibility.Visible;
        SetSidebarWidth(_sidebarExpanded);

        if (vm.ShowTreeView)
        {
            RebuildTree(vm);
            vm.Notes.CollectionChanged += OnNotesCollectionChanged;
            NotesTree.ItemInvoked += OnTreeItemInvoked;
        }
    }

    private void PopulateBugTrackerSidebar(NotePageViewModel vm)
    {
        BugTrackerSidebar.Children.Clear();
        BugTrackerSidebar.RowDefinitions.Clear();
        BugTrackerSidebar.ColumnDefinitions.Clear();

        BugTrackerSidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BugTrackerSidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BugTrackerSidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BugTrackerSidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        BugTrackerSidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var filterPanel = BuildBugStatusSegmented();
        Grid.SetRow(filterPanel, 0);
        BugTrackerSidebar.Children.Add(filterPanel);

        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        var quickSearch = new TextBox
        {
            PlaceholderText = "Quick Search",
            Text = _bugQuickSearchText,
            Width = 902,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        quickSearch.TextChanged += (_, _) =>
        {
            _bugQuickSearchText = quickSearch.Text ?? "";
            RebuildBugTrackerRows();
        };
        searchRow.Children.Add(quickSearch);
        Grid.SetRow(searchRow, 1);
        BugTrackerSidebar.Children.Add(searchRow);

        _bugHeaderGrid = BuildSortableHeader(_bugSortColumn, _bugSortAscending);
        Grid.SetRow(_bugHeaderGrid, 2);
        BugTrackerSidebar.Children.Add(_bugHeaderGrid);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _bugItemsPanel = new StackPanel();
        scroll.Content = _bugItemsPanel;
        Grid.SetRow(scroll, 3);
        BugTrackerSidebar.Children.Add(scroll);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var saveBtn = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        saveBtn.SetBinding(Button.CommandProperty, new Binding { Path = new PropertyPath("SaveNoteCommand") });
        footer.Children.Add(saveBtn);
        var addRowBtn = new Button { Content = "Add Row" };
        addRowBtn.SetBinding(Button.CommandProperty, new Binding { Path = new PropertyPath("AddRowCommand") });
        footer.Children.Add(addRowBtn);
        Grid.SetRow(footer, 4);
        BugTrackerSidebar.Children.Add(footer);

        RebuildBugTrackerRows();
    }

    private void ToggleSort(string column)
    {
        if (_bugSortColumn == column)
            _bugSortAscending = !_bugSortAscending;
        else
        {
            _bugSortColumn = column;
            _bugSortAscending = true;
        }
        UpdateSortIndicators();
        RebuildBugTrackerRows();
    }

    private void UpdateSortIndicators()
    {
        SetArrow(_numSortArrow, _bugSortColumn == "Col1");
        SetArrow(_summarySortArrow, _bugSortColumn == "Col5");
        SetArrow(_bugStatusSortArrow, _bugSortColumn == "Col12");
    }

    private void SetArrow(TextBlock? arrow, bool isActive)
    {
        if (arrow is null) return;
        if (isActive)
        {
            arrow.Text = _bugSortAscending ? "\u2191" : "\u2193";
            arrow.Visibility = Visibility.Visible;
        }
        else
        {
            arrow.Text = "";
            arrow.Visibility = Visibility.Collapsed;
        }
    }

    private void RebuildBugTrackerRows()
    {
        var vm = GetVm();
        if (vm?.PageName != "BugTracker" || _bugItemsPanel is null) return;

        _bugItemsPanel.Children.Clear();

        var filtered = vm.Rows.AsEnumerable();

        if (!string.IsNullOrEmpty(_bugQuickSearchText))
        {
            var terms = _bugQuickSearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtered = filtered.Where(row =>
            {
                var haystack = string.Join(" ", new[]
                {
                    row.Col1, row.Col2, row.Col3, row.Col4, row.Col5,
                    row.Col6, row.Col7, row.Col8, row.Col9, row.Col10,
                    row.Col11, row.Col12, row.Col13, row.Col14, row.Col15,
                    row.Col16, row.Col17, row.Col18, row.Col19, row.Col20,
                    row.Col21, row.Col22, row.Col23,
                });
                return terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }

        if (_summaryFilterSet.Count > 0)
        {
            filtered = filtered.Where(row =>
                !string.IsNullOrEmpty(row.Col5) && _summaryFilterSet.Contains(row.Col5));
        }

        if (_bugStatusFilterSet.Count > 0)
        {
            filtered = filtered.Where(row =>
                !string.IsNullOrEmpty(row.Col12) && _bugStatusFilterSet.Contains(row.Col12));
        }

        if (!string.IsNullOrEmpty(_bugSortColumn))
        {
            filtered = _bugSortColumn switch
            {
                "Col1" => _bugSortAscending
                    ? filtered.OrderBy(row => int.TryParse(row.Col1, out var n) ? n : 0)
                    : filtered.OrderByDescending(row => int.TryParse(row.Col1, out var n) ? n : 0),
                "Col5" => _bugSortAscending
                    ? filtered.OrderBy(row => row.Col5 ?? "")
                    : filtered.OrderByDescending(row => row.Col5 ?? ""),
                "Col12" => _bugSortAscending
                    ? filtered.OrderBy(row => row.Col12 ?? "")
                    : filtered.OrderByDescending(row => row.Col12 ?? ""),
                _ => filtered
            };
        }

        var list = filtered.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var row = list[i];

            var rowGrid = new Grid { Height = 40, Margin = new Thickness(1, 0, 1, 0) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var selBorder = new Border();
            if (ReferenceEquals(row, _selectedBugRow))
                selBorder.Background = GetBugStatusColor(row.Col12);
            Grid.SetColumnSpan(selBorder, 3);
            rowGrid.Children.Add(selBorder);

            rowGrid.Children.Add(new TextBlock { Text = row.Col1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            var st = new TextBlock { Text = row.Col5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(st, 1);
            rowGrid.Children.Add(st);

            var sp = BuildStatusPanel(row);
            Grid.SetColumn(sp, 2);
            rowGrid.Children.Add(sp);

            var langSep = new Border
            {
                Width = 1,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 8, 0),
                Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)),
            };
            Grid.SetColumn(langSep, 3);
            rowGrid.Children.Add(langSep);

            var langPanel = BuildLanguagePanel(row);
            Grid.SetColumn(langPanel, 4);
            rowGrid.Children.Add(langPanel);

            var missingObs = new TextBlock { Text = GetMissingLanguageInitials(row, observed: true), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(missingObs, 5);
            rowGrid.Children.Add(missingObs);

            var missingExp = new TextBlock { Text = GetMissingLanguageInitials(row, observed: false), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(missingExp, 6);
            rowGrid.Children.Add(missingExp);

            var missingShot = new TextBlock { Text = GetMissingScreenshotInitials(row), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(missingShot, 7);
            rowGrid.Children.Add(missingShot);

            rowGrid.PointerPressed += (_, _) => OnBugSelected(row);
            _bugItemsPanel.Children.Add(rowGrid);
        }
    }

    private Grid BuildSortableHeader(string sortedColumn, bool sortAscending)
    {
        var header = new Grid
        {
            Height = 44,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(42) },
                new ColumnDefinition { Width = new GridLength(260) },
                new ColumnDefinition { Width = new GridLength(100) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(140) },
                new ColumnDefinition { Width = new GridLength(140) },
                new ColumnDefinition { Width = new GridLength(120) },
            },
            Margin = new Thickness(0, 0, 0, 6)
        };

        var bold = Microsoft.UI.Text.FontWeights.SemiBold;

        static Button MakeFilterBtn(string tag)
        {
            var btn = new Button
            {
                Content = "\u2026",
                FontSize = 16,
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                Tag = tag,
            };
            btn.Tapped += (_, e) => e.Handled = true;
            return btn;
        }

        var numHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        numHeader.Children.Add(new TextBlock { Text = "#", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center });
        _numSortArrow = new TextBlock { Text = sortedColumn == "Col1" ? (sortAscending ? "\u2191" : "\u2193") : "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        numHeader.Children.Add(_numSortArrow);
        numHeader.Tapped += (_, _) => ToggleSort("Col1");
        header.Children.Add(numHeader);

        var summaryHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        summaryHeader.Children.Add(new TextBlock { Text = "Summary", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center });
        _summarySortArrow = new TextBlock { Text = sortedColumn == "Col5" ? (sortAscending ? "\u2191" : "\u2193") : "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        summaryHeader.Children.Add(_summarySortArrow);
        var summaryFilterBtn = MakeFilterBtn("Summary");
        summaryFilterBtn.Click += (_, _) => BuildFilterPopup(summaryFilterBtn);
        summaryHeader.Children.Add(summaryFilterBtn);
        summaryHeader.Tapped += (_, _) => ToggleSort("Col5");
        Grid.SetColumn(summaryHeader, 1);
        header.Children.Add(summaryHeader);

        var statusHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        statusHeader.Children.Add(new TextBlock { Text = "Bug Status", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center });
        _bugStatusSortArrow = new TextBlock { Text = sortedColumn == "Col12" ? (sortAscending ? "\u2191" : "\u2193") : "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        statusHeader.Children.Add(_bugStatusSortArrow);
        var statusFilterBtn = MakeFilterBtn("Status");
        statusFilterBtn.Click += (_, _) => BuildFilterPopup(statusFilterBtn);
        statusHeader.Children.Add(statusFilterBtn);
        statusHeader.Tapped += (_, _) => ToggleSort("Col12");
        Grid.SetColumn(statusHeader, 2);
        header.Children.Add(statusHeader);

        var langSep = new Border
        {
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 8, 0),
            Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)),
        };
        Grid.SetColumn(langSep, 3);
        header.Children.Add(langSep);

        var langHeader = new TextBlock { Text = "Language Status", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        Grid.SetColumn(langHeader, 4);
        header.Children.Add(langHeader);

        var missingObsHeader = new TextBlock { Text = "Missing Observed Result", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        Grid.SetColumn(missingObsHeader, 5);
        header.Children.Add(missingObsHeader);

        var missingExpHeader = new TextBlock { Text = "Missing Expected Result", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        Grid.SetColumn(missingExpHeader, 6);
        header.Children.Add(missingExpHeader);

        var missingShotHeader = new TextBlock { Text = "Missing Screenshot", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        Grid.SetColumn(missingShotHeader, 7);
        header.Children.Add(missingShotHeader);

        return header;
    }

    private void BuildFilterPopup(FrameworkElement target)
    {
        var vm = GetVm();
        if (vm?.PageName != "BugTracker") return;

        var isSummary = target.Tag as string == "Summary";
        var prop = isSummary ? "Col5" : "Col12";
        var filterSet = isSummary ? _summaryFilterSet : _bugStatusFilterSet;

        var values = vm.Rows.Select(r => r.GetType().GetProperty(prop)?.GetValue(r) as string)
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().OrderBy(s => s).ToList();
        if (values.Count == 0)
            values = new List<string> { "Ready to vet", "Approved", "Reporting", "Uploaded", "Not a bug", "Duplicate" };

        var popup = new StackPanel { Spacing = 4, Padding = new Thickness(12), Width = 250 };

        Button MakeSortItem(string text, bool ascending)
        {
            var b = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
            };
            b.Click += (_, _) =>
            {
                _bugSortColumn = prop;
                _bugSortAscending = ascending;
                UpdateSortIndicators();
                RebuildBugTrackerRows();
            };
            return b;
        }

        popup.Children.Add(MakeSortItem("Sort Ascending", true));
        popup.Children.Add(MakeSortItem("Sort Descending", false));

        popup.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });

        var searchBox = new TextBox { PlaceholderText = "Search", Text = _bugFilterText };

        var checkList = new StackPanel { Spacing = 2 };
        var scroll = new ScrollViewer { MaxHeight = 220, Content = checkList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void RefreshList()
        {
            checkList.Children.Clear();
            var q = searchBox.Text?.Trim() ?? "";
            foreach (var v in values.Where(v => string.IsNullOrEmpty(q) || v.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                var cb = new CheckBox { Content = v, IsChecked = filterSet.Contains(v) };
                var s = v;
                cb.Checked += (_, _) => { filterSet.Add(s); RebuildBugTrackerRows(); };
                cb.Unchecked += (_, _) => { filterSet.Remove(s); RebuildBugTrackerRows(); };
                checkList.Children.Add(cb);
            }
        }

        searchBox.TextChanged += (_, _) => RefreshList();
        popup.Children.Add(searchBox);
        popup.Children.Add(scroll);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var selectAllBtn = new Button { Content = "Select All" };
        selectAllBtn.Click += (_, _) =>
        {
            foreach (var v in values) filterSet.Add(v);
            RefreshList();
            RebuildBugTrackerRows();
        };
        var clearBtn = new Button { Content = "Clear" };
        clearBtn.Click += (_, _) =>
        {
            filterSet.Clear();
            RefreshList();
            RebuildBugTrackerRows();
        };
        btnRow.Children.Add(selectAllBtn);
        btnRow.Children.Add(clearBtn);
        popup.Children.Add(btnRow);

        RefreshList();

        var flyout = new Flyout { Content = popup };
        flyout.ShowAt(target);
    }

    private StackPanel BuildBugStatusSelector()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };

        var label = new TextBlock { Text = "Bug Status", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(label);

        var statuses = new[] { "Ready to vet", "Approved", "Reporting", "Uploaded", "Not a bug", "Duplicate" };
        _bugFormStatusSelector = new Segmented { HorizontalAlignment = HorizontalAlignment.Stretch };

        foreach (var status in statuses)
        {
            var item = new SegmentedItem { Content = status };
            var s = status;
            item.Tapped += (_, _) =>
            {
                if (_selectedBugRow is null) return;
                _selectedBugRow.Col12 = s;
                _bugFormStatusSelector.SelectedIndex = Array.IndexOf(statuses, s);
                RebuildBugTrackerRows();
            };
            _bugFormStatusSelector.Items.Add(item);
        }

        panel.Children.Add(_bugFormStatusSelector);
        return panel;
    }

    private static Brush DefaultDropBorderBrush() =>
        Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0));

    private static Brush DefaultDropBackground() =>
        Application.Current.Resources["ControlFillColorSecondaryBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0, 0, 0));

    private StackPanel BuildScreenshotsSection()
    {
        try
        {
            if (!Directory.Exists(_bugScreenshotsDir))
                Directory.CreateDirectory(_bugScreenshotsDir);
        }
        catch { }

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        var label = new TextBlock { Text = "Screenshots", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        panel.Children.Add(label);

        var langGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12, Margin = new Thickness(0, 4, 0, 0), MinHeight = 160, AllowDrop = true };
        langGrid.ColumnDefinitions.Add(new ColumnDefinition());
        langGrid.ColumnDefinitions.Add(new ColumnDefinition());
        langGrid.RowDefinitions.Add(new RowDefinition());
        langGrid.RowDefinitions.Add(new RowDefinition());
        _bugLangScreenshotGrid = langGrid;
        langGrid.DragOver += OnLangGridDragOver;
        langGrid.DragLeave += OnLangGridDragLeave;
        langGrid.Drop += OnLangGridDrop;
        panel.Children.Add(langGrid);

        LoadScreenshotsForSelectedBug();
        UpdateLangScreenshotVisibility();
        return panel;
    }

    private string? GetLangScreenshotZoneAt(Windows.Foundation.Point p)
    {
        if (_bugLangScreenshotGrid is null) return null;
        foreach (var kvp in _bugLangScreenshotZones)
        {
            var zone = kvp.Value;
            var pt = zone.TransformToVisual(_bugLangScreenshotGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            var rect = new Windows.Foundation.Rect(pt.X, pt.Y, zone.ActualWidth, zone.ActualHeight);
            if (rect.Contains(p))
                return kvp.Key;
        }
        return null;
    }

    private void OnLangGridDragOver(object sender, DragEventArgs e)
    {
        var lang = GetLangScreenshotZoneAt(e.GetPosition(_bugLangScreenshotGrid!));
        if (_selectedBugRow is null || lang is null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            if (e.DragUIOverride is not null)
                e.DragUIOverride.Caption = _selectedBugRow is null ? "Select a bug first" : "";
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
            e.DragUIOverride.Caption = "Add to screenshots";
        foreach (var kvp in _bugLangScreenshotOverlays)
            kvp.Value.Visibility = kvp.Key == lang ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLangGridDragLeave(object sender, DragEventArgs e)
    {
        foreach (var overlay in _bugLangScreenshotOverlays.Values)
            overlay.Visibility = Visibility.Collapsed;
    }

    private async void OnLangGridDrop(object sender, DragEventArgs e)
    {
        var lang = GetLangScreenshotZoneAt(e.GetPosition(_bugLangScreenshotGrid!));
        foreach (var overlay in _bugLangScreenshotOverlays.Values)
            overlay.Visibility = Visibility.Collapsed;
        if (lang is not null)
            await HandleLangScreenshotDrop(lang, e);
    }

    private StackPanel BuildLangScreenshotArea(string langKey, string label)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var container = new Grid { MinHeight = 120 };

        var preview = new ItemsControl();
        try
        {
            preview.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<ItemsWrapGrid Orientation='Horizontal'/></ItemsPanelTemplate>");
        }
        catch { }
        container.Children.Add(preview);

        var accent = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD7));
        var highlight = Application.Current.Resources["AccentFillColorSecondaryBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0x00, 0x78, 0xD7));

        var overlay = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Background = highlight,
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = $"Drop {label} screenshots here",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
            },
        };
        container.Children.Add(overlay);

        var dropBorder = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = DefaultDropBorderBrush(),
            Background = DefaultDropBackground(),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = container,
        };
        panel.Children.Add(dropBorder);

        _bugLangScreenshotPreviews[langKey] = preview;
        _bugLangScreenshotOverlays[langKey] = overlay;
        return panel;
    }

    private async Task HandleLangScreenshotDrop(string langKey, DragEventArgs e)
    {
        var vm = GetVm();
        if (vm?.SelectedNote is null || _selectedBugRow is null) return;

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is not StorageFile file) continue;
                if (!_imageExtensions.Contains(Path.GetExtension(file.Name))) continue;

                var baseName = Path.GetFileNameWithoutExtension(file.Name);
                var sanitized = string.Concat(baseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                if (string.IsNullOrEmpty(sanitized)) sanitized = "screenshot";
                var name = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{sanitized}{Path.GetExtension(file.Name)}";
                var target = Path.Combine(_bugScreenshotsDir, name);
                File.Copy(file.Path, target, overwrite: false);
                _screenshotStore.AddScreenshot(vm.SelectedNote.FileName, _selectedBugRow.Col1, langKey, name);
                _bugLangScreenshots[langKey].Add(target);
            }
            RefreshLangScreenshotPreviews(langKey);
            RebuildBugTrackerRows();
        }
        catch { }
    }

    private void LoadScreenshotsForSelectedBug()
    {
        foreach (var k in _langKeys)
            _bugLangScreenshots[k] = new List<string>();

        var vm = GetVm();
        if (vm?.SelectedNote is not null && _selectedBugRow is not null)
        {
            foreach (var k in _langKeys)
            {
                foreach (var fileName in _screenshotStore.GetScreenshots(vm.SelectedNote.FileName, _selectedBugRow.Col1, k))
                    _bugLangScreenshots[k].Add(Path.Combine(_bugScreenshotsDir, fileName));
            }
        }
        foreach (var k in _langKeys)
            RefreshLangScreenshotPreviews(k);
    }

    private void RefreshLangScreenshotPreviews(string langKey)
    {
        if (!_bugLangScreenshotPreviews.TryGetValue(langKey, out var preview)) return;
        preview.Items.Clear();
        foreach (var path in _bugLangScreenshots[langKey])
        {
            try
            {
                preview.Items.Add(new Image
                {
                    Source = new BitmapImage(new Uri("file:///" + path.Replace('\\', '/'))),
                    Width = 100,
                    Height = 75,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 0, 6, 6),
                });
            }
            catch { }
        }
    }

    private StackPanel BuildBugStatusSegmented()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 12) };

        var label = new TextBlock { Text = "Filter Bugs", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(label);

        var segments = new Segmented { HorizontalAlignment = HorizontalAlignment.Left };

        var allItem = new SegmentedItem { Content = "All" };
        allItem.Tapped += (_, _) => { _bugStatusFilterSet.Clear(); RebuildBugTrackerRows(); };
        segments.Items.Add(allItem);

        var openItem = new SegmentedItem { Content = "Open" };
        openItem.Tapped += (_, _) =>
        {
            _bugStatusFilterSet.Clear();
            _bugStatusFilterSet.Add("Ready to vet");
            _bugStatusFilterSet.Add("Approved");
            _bugStatusFilterSet.Add("Reporting");
            _bugStatusFilterSet.Add("Uploaded");
            RebuildBugTrackerRows();
        };
        segments.Items.Add(openItem);

        var closedItem = new SegmentedItem { Content = "Closed" };
        closedItem.Tapped += (_, _) =>
        {
            _bugStatusFilterSet.Clear();
            _bugStatusFilterSet.Add("Not a bug");
            _bugStatusFilterSet.Add("Duplicate");
            RebuildBugTrackerRows();
        };
        segments.Items.Add(closedItem);

        panel.Children.Add(segments);
        return panel;
    }

    private void OnBugSelected(TableRow row)
    {
        var vm = GetVm();
        if (vm is null) return;
        _selectedBugRow = row;
        PopulateBugTrackerSidebar(vm);
        PopulateBugForm(row);
    }

    private Segmented BuildLangSegmented()
    {
        var seg = new Segmented();
        seg.Items.Add(new SegmentedItem { Content = "Not Affected" });
        seg.Items.Add(new SegmentedItem { Content = "Affected" });
        seg.SelectionChanged += (_, _) => ColorLangSegmented(seg);
        return seg;
    }

    private StackPanel BuildLangResultPanel(string langName, out Segmented segmented, out TextBox observed, out TextBox expected)
    {
        segmented = BuildLangSegmented();
        observed = new TextBox { Header = "Observed Result", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 60 };
        expected = new TextBox { Header = "Expected Result", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 60 };
        var seg = segmented;
        var obs = observed;
        var exp = expected;
        SetLangResultEnabled(seg, obs, exp);
        seg.SelectionChanged += (_, _) =>
        {
            SetLangResultEnabled(seg, obs, exp);
            UpdateLangScreenshotVisibility();
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = langName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(segmented);
        panel.Children.Add(observed);
        panel.Children.Add(expected);
        return panel;
    }

    private static void SetLangResultEnabled(Segmented seg, TextBox observed, TextBox expected)
    {
        var enabled = seg.SelectedIndex == 1;
        observed.IsEnabled = enabled;
        expected.IsEnabled = enabled;
    }

    private void UpdateLangResultVisibility()
    {
        var show = _bugFormNeedsLangSpecific?.IsChecked == true;
        var boxes = new[]
        {
            _bugFormFrenchObserved, _bugFormFrenchExpected,
            _bugFormItalianObserved, _bugFormItalianExpected,
            _bugFormGermanObserved, _bugFormGermanExpected,
            _bugFormSpanishObserved, _bugFormSpanishExpected,
        };
        foreach (var b in boxes)
        {
            if (b is not null)
                b.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateLangScreenshotVisibility()
    {
        if (_bugLangScreenshotGrid is null) return;

        var affected = new Dictionary<string, bool>
        {
            ["FR"] = _bugFormFrench?.SelectedIndex == 1,
            ["IT"] = _bugFormItalian?.SelectedIndex == 1,
            ["DE"] = _bugFormGerman?.SelectedIndex == 1,
            ["ES"] = _bugFormSpanish?.SelectedIndex == 1,
        };
        var labels = new Dictionary<string, string>
        {
            ["FR"] = "French", ["IT"] = "Italian", ["DE"] = "German", ["ES"] = "Spanish",
        };

        _bugLangScreenshotGrid.Children.Clear();
        _bugLangScreenshotPreviews.Clear();
        _bugLangScreenshotZones.Clear();
        _bugLangScreenshotOverlays.Clear();
        int idx = 0;
        foreach (var k in _langKeys)
        {
            if (!affected[k]) continue;
            var zone = BuildLangScreenshotArea(k, labels[k]);
            _bugLangScreenshotZones[k] = zone;
            Grid.SetColumn(zone, idx % 2);
            Grid.SetRow(zone, idx / 2);
            _bugLangScreenshotGrid.Children.Add(zone);
            idx++;
        }

        foreach (var k in _langKeys)
        {
            if (affected[k])
                RefreshLangScreenshotPreviews(k);
        }
    }

    private static void ColorLangSegmented(Segmented seg)
    {
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCD, 0xD2));
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));
        for (int i = 0; i < seg.Items.Count; i++)
        {
            if (seg.Items[i] is SegmentedItem item)
                item.Background = i == seg.SelectedIndex
                    ? (i == 0 ? green : red)
                    : null;
        }
    }

    private static void SetLangSegmented(Segmented seg, string? value)
    {
        seg.SelectedIndex = value == "Affected" ? 1 : value == "Not Affected" ? 0 : -1;
        ColorLangSegmented(seg);
    }

    private string? GetAffectedLanguageInitials()
    {
        var initials = new List<string>();
        if (_bugFormFrench?.SelectedIndex == 1) initials.Add("FR");
        if (_bugFormItalian?.SelectedIndex == 1) initials.Add("IT");
        if (_bugFormGerman?.SelectedIndex == 1) initials.Add("DE");
        if (_bugFormSpanish?.SelectedIndex == 1) initials.Add("ES");
        return initials.Count == 0 ? null : "[" + string.Join(", ", initials) + "]";
    }

    private static string BuildBugSummaryWithLanguages(string summary, string? languageInitials)
    {
        var cleaned = Regex.Replace(summary, @"\s*\[(?:FR|IT|DE|ES)(?:\s*,\s*(?:FR|IT|DE|ES))*\]", "").Trim();
        return string.IsNullOrEmpty(languageInitials) ? cleaned : $"{languageInitials} {cleaned}".Trim();
    }

    private static string GetMissingLanguageInitials(TableRow row, bool observed)
    {
        if (row.Col23 != "Yes")
            return string.Empty;
        var initials = new List<string>();
        var fr = observed ? row.Col13 : row.Col14;
        var it = observed ? row.Col15 : row.Col16;
        var de = observed ? row.Col17 : row.Col18;
        var es = observed ? row.Col19 : row.Col20;
        if (row.Col8 == "Affected" && string.IsNullOrWhiteSpace(fr)) initials.Add("FR");
        if (row.Col9 == "Affected" && string.IsNullOrWhiteSpace(it)) initials.Add("IT");
        if (row.Col10 == "Affected" && string.IsNullOrWhiteSpace(de)) initials.Add("DE");
        if (row.Col11 == "Affected" && string.IsNullOrWhiteSpace(es)) initials.Add("ES");
        return string.Join(", ", initials);
    }

    private string GetMissingScreenshotInitials(TableRow row)
    {
        var vm = GetVm();
        if (vm?.SelectedNote is null)
            return string.Empty;
        var with = _screenshotStore.GetLanguagesWithScreenshots(vm.SelectedNote.FileName, row.Col1);
        var initials = new List<string>();
        if (row.Col8 == "Affected" && !with.Contains("FR")) initials.Add("FR");
        if (row.Col9 == "Affected" && !with.Contains("IT")) initials.Add("IT");
        if (row.Col10 == "Affected" && !with.Contains("DE")) initials.Add("DE");
        if (row.Col11 == "Affected" && !with.Contains("ES")) initials.Add("ES");
        return string.Join(", ", initials);
    }

    private void PopulateBugForm(TableRow? row)
    {
        if (row is null)
        {
            _bugFormId!.Text = "";
            _bugFormUsername!.Text = "";
            _bugFormDate!.Text = "";
            _bugFormType!.SelectedIndex = -1;
            _bugFormSummary!.Text = "";
            _bugFormDesc!.Text = "";
            _bugFormSteps!.Text = "";
            SetLangSegmented(_bugFormFrench!, null);
            SetLangSegmented(_bugFormItalian!, null);
            SetLangSegmented(_bugFormGerman!, null);
            SetLangSegmented(_bugFormSpanish!, null);
            _bugFormFrenchObserved!.Text = "";
            _bugFormFrenchExpected!.Text = "";
            _bugFormItalianObserved!.Text = "";
            _bugFormItalianExpected!.Text = "";
            _bugFormGermanObserved!.Text = "";
            _bugFormGermanExpected!.Text = "";
            _bugFormSpanishObserved!.Text = "";
            _bugFormSpanishExpected!.Text = "";
            _bugFormGlobalObserved!.Text = "";
            _bugFormGlobalExpected!.Text = "";
            _bugFormNeedsLangSpecific!.IsChecked = false;
            UpdateLangResultVisibility();
            UpdateLangScreenshotVisibility();
            LoadScreenshotsForSelectedBug();
            return;
        }

        _bugFormId!.Text = row.Col1;
        _bugFormUsername!.Text = row.Col2;
        _bugFormDate!.Text = row.Col3;
        _bugFormType!.SelectedItem = row.Col4;
        _bugFormSummary!.Text = row.Col5;
        _bugFormDesc!.Text = row.Col6;
        _bugFormSteps!.Text = row.Col7;
        _bugFormStatusSelector!.SelectedIndex = Array.IndexOf(new[] { "Ready to vet", "Approved", "Reporting", "Uploaded", "Not a bug", "Duplicate" }, row.Col12);
        SetLangSegmented(_bugFormFrench!, row.Col8);
        SetLangSegmented(_bugFormItalian!, row.Col9);
        SetLangSegmented(_bugFormGerman!, row.Col10);
        SetLangSegmented(_bugFormSpanish!, row.Col11);
        _bugFormFrenchObserved!.Text = row.Col13;
        _bugFormFrenchExpected!.Text = row.Col14;
        _bugFormItalianObserved!.Text = row.Col15;
        _bugFormItalianExpected!.Text = row.Col16;
        _bugFormGermanObserved!.Text = row.Col17;
        _bugFormGermanExpected!.Text = row.Col18;
        _bugFormSpanishObserved!.Text = row.Col19;
        _bugFormSpanishExpected!.Text = row.Col20;
        _bugFormGlobalObserved!.Text = row.Col21;
        _bugFormGlobalExpected!.Text = row.Col22;
        _bugFormNeedsLangSpecific!.IsChecked = row.Col23 == "Yes";
        UpdateLangResultVisibility();
        UpdateLangScreenshotVisibility();
        LoadScreenshotsForSelectedBug();
    }

    private async Task SaveBugForm()
    {
        var vm = GetVm();
        if (vm is null || _selectedBugRow is null) return;

        var row = _selectedBugRow;
        row.Col4 = _bugFormType?.SelectedItem as string ?? "";
        var bugSummary = BuildBugSummaryWithLanguages(_bugFormSummary?.Text ?? "", GetAffectedLanguageInitials());
        row.Col5 = bugSummary;
        _bugFormSummary!.Text = bugSummary;
        row.Col6 = _bugFormDesc?.Text ?? "";
        row.Col7 = _bugFormSteps?.Text ?? "";
        row.Col8 = _bugFormFrench?.SelectedIndex switch { 0 => "Not Affected", 1 => "Affected", _ => "" } ?? "";
        row.Col9 = _bugFormItalian?.SelectedIndex switch { 0 => "Not Affected", 1 => "Affected", _ => "" } ?? "";
        row.Col10 = _bugFormGerman?.SelectedIndex switch { 0 => "Not Affected", 1 => "Affected", _ => "" } ?? "";
        row.Col11 = _bugFormSpanish?.SelectedIndex switch { 0 => "Not Affected", 1 => "Affected", _ => "" } ?? "";
        row.Col13 = _bugFormFrenchObserved?.Text ?? "";
        row.Col14 = _bugFormFrenchExpected?.Text ?? "";
        row.Col15 = _bugFormItalianObserved?.Text ?? "";
        row.Col16 = _bugFormItalianExpected?.Text ?? "";
        row.Col17 = _bugFormGermanObserved?.Text ?? "";
        row.Col18 = _bugFormGermanExpected?.Text ?? "";
        row.Col19 = _bugFormSpanishObserved?.Text ?? "";
        row.Col20 = _bugFormSpanishExpected?.Text ?? "";
        row.Col21 = _bugFormGlobalObserved?.Text ?? "";
        row.Col22 = _bugFormGlobalExpected?.Text ?? "";
        row.Col23 = _bugFormNeedsLangSpecific?.IsChecked == true ? "Yes" : "";

        var note = vm.SelectedNote;
        if (note is not null)
        {
            await vm.ExcelService.SaveRowsAsync(note, vm.Rows);
            note.ModifiedAt = DateTime.Now;
        }

        PopulateBugTrackerSidebar(vm);
    }

    private void BuildBugForm()
    {
        TableViewContainer.Children.Clear();

        var typeOptions = new[] { "Grammar", "Spelling", "Missing Translation", "Inconsistent Translation", "Glossary", "Incorrect Translation", "Shortening", "Compliance" };

        var scroller = new ScrollViewer();
        var form = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 12, 12) };

        var infoGrid = new Grid { ColumnSpacing = 16 };
        for (int i = 0; i < 6; i++)
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _bugFormId = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _bugFormUsername = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _bugFormDate = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        var idLabel = new TextBlock { Text = "#", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(idLabel, 0);
        infoGrid.Children.Add(idLabel);
        Grid.SetColumn(_bugFormId, 1);
        infoGrid.Children.Add(_bugFormId);
        var unLabel = new TextBlock { Text = "Username", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(unLabel, 2);
        infoGrid.Children.Add(unLabel);
        Grid.SetColumn(_bugFormUsername, 3);
        infoGrid.Children.Add(_bugFormUsername);
        var dtLabel = new TextBlock { Text = "Date", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dtLabel, 4);
        infoGrid.Children.Add(dtLabel);
        Grid.SetColumn(_bugFormDate, 5);
        infoGrid.Children.Add(_bugFormDate);
        form.Children.Add(infoGrid);

        _bugFormType = new ComboBox
        {
            Header = "Type",
            ItemsSource = typeOptions,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        form.Children.Add(_bugFormType);

        _bugFormSummary = new TextBox { Header = "Summary", TextWrapping = TextWrapping.Wrap, Height = 60, AcceptsReturn = true };
        form.Children.Add(_bugFormSummary);

        _bugFormDesc = new TextBox { Header = "Description", TextWrapping = TextWrapping.Wrap, Height = 100, AcceptsReturn = true };
        form.Children.Add(_bugFormDesc);

        _bugFormSteps = new TextBox { Header = "Steps to Reproduce", TextWrapping = TextWrapping.Wrap, Height = 100, AcceptsReturn = true };
        form.Children.Add(_bugFormSteps);

        var resultRow = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        resultRow.ColumnDefinitions.Add(new ColumnDefinition());
        resultRow.ColumnDefinitions.Add(new ColumnDefinition());
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _bugFormGlobalObserved = new TextBox { Header = "Observed Result", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 60 };
        _bugFormGlobalExpected = new TextBox { Header = "Expected Result", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 60 };
        _bugFormNeedsLangSpecific = new CheckBox
        {
            Content = "Needs language specific results",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        _bugFormNeedsLangSpecific.Checked += (_, _) => UpdateLangResultVisibility();
        _bugFormNeedsLangSpecific.Unchecked += (_, _) => UpdateLangResultVisibility();
        Grid.SetColumn(_bugFormGlobalObserved, 0);
        Grid.SetColumn(_bugFormGlobalExpected, 1);
        Grid.SetColumn(_bugFormNeedsLangSpecific, 2);
        resultRow.Children.Add(_bugFormGlobalObserved);
        resultRow.Children.Add(_bugFormGlobalExpected);
        resultRow.Children.Add(_bugFormNeedsLangSpecific);
        form.Children.Add(resultRow);

        form.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });
        form.Children.Add(new TextBlock { Text = "Languages", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var langRow1 = new Grid { ColumnSpacing = 16 };
        langRow1.ColumnDefinitions.Add(new ColumnDefinition());
        langRow1.ColumnDefinitions.Add(new ColumnDefinition());
        var frenchPanel = BuildLangResultPanel("French", out _bugFormFrench, out _bugFormFrenchObserved, out _bugFormFrenchExpected);
        Grid.SetColumn(frenchPanel, 0);
        langRow1.Children.Add(frenchPanel);
        var italianPanel = BuildLangResultPanel("Italian", out _bugFormItalian, out _bugFormItalianObserved, out _bugFormItalianExpected);
        Grid.SetColumn(italianPanel, 1);
        langRow1.Children.Add(italianPanel);
        form.Children.Add(langRow1);

        var langRow2 = new Grid { ColumnSpacing = 16 };
        langRow2.ColumnDefinitions.Add(new ColumnDefinition());
        langRow2.ColumnDefinitions.Add(new ColumnDefinition());
        var germanPanel = BuildLangResultPanel("German", out _bugFormGerman, out _bugFormGermanObserved, out _bugFormGermanExpected);
        Grid.SetColumn(germanPanel, 0);
        langRow2.Children.Add(germanPanel);
        var spanishPanel = BuildLangResultPanel("Spanish", out _bugFormSpanish, out _bugFormSpanishObserved, out _bugFormSpanishExpected);
        Grid.SetColumn(spanishPanel, 1);
        langRow2.Children.Add(spanishPanel);
        form.Children.Add(langRow2);

        form.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });
        form.Children.Add(BuildBugStatusSelector());

        form.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });
        form.Children.Add(BuildScreenshotsSection());

        var saveBtn = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        saveBtn.Click += async (_, _) => await SaveBugForm();
        form.Children.Add(saveBtn);

        scroller.Content = form;
        TableViewContainer.Children.Add(scroller);
    }

    private void AlignSidebarWithTable(FrameworkElement target)
    {
        var offset = TitleBox.ActualHeight;
        if (offset > 0)
        {
            target.Margin = new Thickness(0, offset, 0, 0);
            return;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var retry = TitleBox.ActualHeight;
            target.Margin = new Thickness(0, retry, 0, 0);
        });
    }

    private static SolidColorBrush GetBugStatusColor(string? status)
    {
        var c = status switch
        {
            "Ready to vet" => Windows.UI.Color.FromArgb(0x33, 0x00, 0x78, 0xD7),
            "Approved" => Windows.UI.Color.FromArgb(0x33, 0xC8, 0xE6, 0xC9),
            "Reporting" => Windows.UI.Color.FromArgb(0x33, 0xFF, 0x8C, 0x00),
            "Uploaded" => Windows.UI.Color.FromArgb(0x33, 0xFF, 0xD7, 0x00),
            "Not a bug" => Windows.UI.Color.FromArgb(0x33, 0x80, 0x80, 0x80),
            "Duplicate" => Windows.UI.Color.FromArgb(0x33, 0xCC, 0x00, 0x00),
            _ => Windows.UI.Color.FromArgb(0x33, 0x00, 0x78, 0xD7),
        };
        return new SolidColorBrush(c);
    }

    private static FrameworkElement BuildStatusPanel(TableRow row)
    {
        var status = row.Col12 ?? "";
        var tb = new TextBlock
        {
            Text = status,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return tb;
    }

    private static FrameworkElement BuildLanguagePanel(TableRow row)
    {
        var langVals = new[] { row.Col8, row.Col9, row.Col10, row.Col11 };
        var langLabels = new[] { "FR", "IT", "DE", "ES" };
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCD, 0xD2));
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));

        var segmented = new Segmented { IsEnabled = false };
        for (int i = 0; i < 4; i++)
        {
            var item = new SegmentedItem
            {
                Content = new TextBlock { Text = langLabels[i], FontSize = 11 },
                Padding = new Thickness(8, 2, 8, 2),
            };
            var bg = langVals[i] == "Affected" ? red : langVals[i] == "Not Affected" ? green : null;
            if (bg is not null)
                item.Background = bg;
            segmented.Items.Add(item);
        }
        return segmented;
    }

    private void OnNotesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var vm = GetVm();
        if (vm is not null)
            RebuildTree(vm);
    }

    private void RebuildTree(NotePageViewModel vm)
    {
        _noteNodeMap.Clear();
        NotesTree.RootNodes.Clear();

        _rootNode = new TreeViewNode
        {
            Content = "Necronomicon",
            IsExpanded = true,
        };

        foreach (var note in vm.Notes.OrderBy(n => n.Title))
        {
            var item = new TreeNoteItem { DisplayText = note.Title, Note = note };
            var node = new TreeViewNode { Content = item };
            _rootNode.Children.Add(node);
            _noteNodeMap[note.Id] = node;

            note.PropertyChanged += OnNotePropertyChanged;
        }

        NotesTree.RootNodes.Add(_rootNode);
    }

    private void OnNotePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TableNote note || e.PropertyName != nameof(TableNote.Title))
            return;

        if (_noteNodeMap.TryGetValue(note.Id, out var node))
        {
            var item = (TreeNoteItem)node.Content;
            item.DisplayText = note.Title;
            node.Content = item;
        }
    }

    private void OnTreeItemInvoked(object? sender, TreeViewItemInvokedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null) return;

        if (e.InvokedItem is TreeViewNode node)
        {
            if (node.Content is TreeNoteItem item && vm.SelectedNote != item.Note)
                vm.SelectedNote = item.Note;
        }
        else if (e.InvokedItem is TreeNoteItem item)
        {
            if (vm.SelectedNote != item.Note)
                vm.SelectedNote = item.Note;
        }
    }

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;

        var vm = GetVm();
        SetSidebarWidth(_sidebarExpanded);

        if (vm is not null)
        {
            if (vm.IsBugTracker)
            {
                BugTrackerSidebar.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                NotesList.Visibility = _sidebarExpanded && !vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
                NotesTree.Visibility = _sidebarExpanded && vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        ToggleBtn.Content = new SymbolIcon(_sidebarExpanded ? Symbol.OpenPane : Symbol.ClosePane);
    }

    private void SetSidebarWidth(bool expanded)
    {
        RootLayout.ColumnDefinitions[0].Width = expanded
            ? GridLength.Auto
            : new GridLength(0);
    }

    private void SetupTableView(NotePageViewModel vm)
    {
        TableViewContainer.Children.Clear();

        _columns = GetColumnDefs(vm.PageName);

        _tableView = new TableView
        {
            AutoGenerateColumns = false,
            ShowExportOptions = false,
            HeaderRowHeight = 32,
        };

        var wrapStyle = new Style(typeof(TextBlock));
        wrapStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));

        if (vm.PageName == "Changelog")
        {
            var typeOptions = new[] { "Grammar", "Spelling", "Missing Translation", "Inconsistent Translation", "Glossary", "Incorrect Translation", "Shortening", "Compliance" };
            var statusOptions = new[] { "Pending", "Changed" };

            foreach (var col in _columns)
            {
                var isReadOnly = col.Header switch
                {
                    "#" or "Tester" or "Date" or "Source" or "Actual Result" => true,
                    _ => false,
                };

                if (col.Header == "Type")
                {
                    var tc = new TableViewComboBoxColumn
                    {
                        Header = col.Header,
                        Tag = col.Prop,
                        Binding = new Binding { Path = new PropertyPath(col.Prop), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                        ItemsSource = typeOptions,
                        IsReadOnly = false,
                    };
                    tc.Width = new GridLength(1, GridUnitType.Star);
                    _tableView.Columns.Add(tc);
                }
                else if (col.Header == "Status")
                {
                    var tc = new TableViewComboBoxColumn
                    {
                        Header = col.Header,
                        Tag = col.Prop,
                        Binding = new Binding { Path = new PropertyPath(col.Prop), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                        ItemsSource = statusOptions,
                        IsReadOnly = false,
                    };
                    tc.Width = new GridLength(1, GridUnitType.Star);
                    _tableView.Columns.Add(tc);
                }
                else
                {
                    var tc = new TableViewTextColumn
                    {
                        Header = col.Header,
                        Tag = col.Prop,
                        Binding = new Binding { Path = new PropertyPath(col.Prop), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                        ElementStyle = wrapStyle,
                        IsReadOnly = isReadOnly,
                    };
                    tc.Width = new GridLength(1, GridUnitType.Star);
                    _tableView.Columns.Add(tc);
                }
            }
        }
        else if (vm.PageName == "BugTracker")
        {
            BuildBugForm();
            return;
        }
        else
        {
            foreach (var col in _columns)
            {
                var isReadOnly = vm.PageName == "TextFiles" || col.Header switch
                {
                    "String ID" or "Source" or "French" or "Italian" or "German" or "Spanish" => true,
                    _ => false,
                };

                var tc = new TableViewTextColumn
                {
                    Header = col.Header,
                    Tag = col.Prop,
                    Binding = new Binding
                    {
                        Path = new PropertyPath(col.Prop),
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    },
                    ElementStyle = wrapStyle,
                    IsReadOnly = isReadOnly,
                };

                tc.Width = col.Header switch
                {
                    "String ID" => new GridLength(100),
                    "Steps to Reproduce" => new GridLength(150),
                    _ => new GridLength(1, GridUnitType.Star),
                };

                if (col.Header == "Steps to Reproduce")
                {
                    var editStyle = new Style(typeof(TextBox));
                    editStyle.Setters.Add(new Setter(TextBox.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
                    editStyle.Setters.Add(new Setter(TextBox.VerticalAlignmentProperty, VerticalAlignment.Stretch));
                    editStyle.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
                    tc.EditingElementStyle = editStyle;
                }

                _tableView.Columns.Add(tc);
            }
        }

        _tableView.CellContextFlyout = new MenuFlyout();
        _tableView.CellContextFlyoutOpening += OnCellContextFlyoutOpening;
        _tableView.CellDoubleTapped += OnCellDoubleTapped;
        _tableView.PreparingCellForEdit += OnPreparingCellForEdit;
        _tableView.ContainerContentChanging += OnContainerContentChanging;
        _tableView.LayoutUpdated += OnTableViewLayoutUpdated;

        if (vm.PageName == "Changelog")
        _tableView.CellEditEnded += OnChangelogCellEditEnded;
    else if (vm.PageName == "BugTracker")
        _tableView.CellEditEnded += OnBugTrackerCellEditEnded;

        _tableView.SetBinding(TableView.ItemsSourceProperty, new Binding
        {
            Path = new PropertyPath("Rows"),
        });

        Grid.SetRow(_tableView, 0);
        Grid.SetColumn(_tableView, 0);
        TableViewContainer.Children.Add(_tableView);
    }

    private void OnCellContextFlyoutOpening(object? sender, TableViewCellContextFlyoutEventArgs e)
    {
        if (e.Item is not TableRow row || e.Flyout is not MenuFlyout flyout)
            return;

        var colIndex = e.Slot.Column;
        if (colIndex < 0 || colIndex >= _columns.Length)
            return;

        var prop = _columns[colIndex].Prop;
        var header = _columns[colIndex].Header;
        var currentValue = GetPropertyValue(row, prop);

        flyout.Items.Clear();

        var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
        copyItem.Click += (_, _) => CopyToClipboard(currentValue);
        flyout.Items.Add(copyItem);

        var vm = GetVm();
        if (vm?.PageName is "Changelog" or "TextFiles" or "BugTracker")
            return;

        if (header is not ("String ID" or "Source" or "Steps to Reproduce"))
        {
            var editItem = new MenuFlyoutItem { Text = "Edit", Icon = new SymbolIcon(Symbol.Edit) };
            editItem.Click += (_, _) => _ = ShowEditDialog(row, prop, currentValue, header);

            var bugItem = new MenuFlyoutItem { Text = "Bug", Icon = new SymbolIcon(Symbol.ReportHacked) };
            bugItem.Click += (_, _) => _ = ShowBugDialog(row, prop, header);

            flyout.Items.Add(editItem);
            flyout.Items.Add(bugItem);
        }
    }

    private void OnCellDoubleTapped(object? sender, TableViewCellDoubleTappedEventArgs e)
    {
        var vm = GetVm();
        if (vm is null || vm.IsTextFiles || vm.IsBugTracker) return;

        var colIndex = e.Slot.Column;
        if (colIndex < 0 || colIndex >= _columns.Length)
            return;

        var header = _columns[colIndex].Header;
        if (e.Cell is null || header is not ("French" or "Italian" or "German" or "Spanish"))
            return;

        if (e.Item is not TableRow row)
            return;

        var prop = _columns[colIndex].Prop;

        if (IsCellRed(row, prop) || IsCellYellow(row, prop))
        {
            _ = ShowTranslationStatusDialog(e.Cell, row, prop);
            return;
        }

        if (IsCellGreen(row, prop))
        {
            RemoveGreen(row, prop);
            e.Cell.Background = null;
            return;
        }

        MarkGreen(row, prop);
        e.Cell.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));
    }

    private void OnPreparingCellForEdit(object? sender, TableViewPreparingCellForEditEventArgs e)
    {
        if (e.Column is not TableViewTextColumn col || e.EditingElement is not TextBox textBox)
            return;

        var colIndex = _tableView?.Columns.IndexOf(col) ?? -1;
        if (colIndex < 0 || colIndex >= _columns.Length || _columns[colIndex].Header != "Steps to Reproduce")
            return;

        textBox.KeyDown -= OnEditingTextBoxKeyDown;
        textBox.KeyDown += OnEditingTextBoxKeyDown;
    }

    private void OnEditingTextBoxKeyDown(object? sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || sender is not TextBox textBox)
            return;

        var menuState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        if ((menuState & Windows.UI.Core.CoreVirtualKeyStates.Down) != Windows.UI.Core.CoreVirtualKeyStates.Down)
            return;

        var idx = textBox.SelectionStart;
        textBox.Text = textBox.Text.Insert(idx, "\r\n");
        textBox.SelectionStart = idx + 2;
        e.Handled = true;
    }

    private bool IsCellRed(TableRow row, string propName)
    {
        return _modifiedCells.TryGetValue(row, out var props) && props.Contains(propName);
    }

    private bool IsCellGreen(TableRow row, string propName)
    {
        return _passedCells.TryGetValue(row, out var props) && props.Contains(propName);
    }

    private bool IsCellYellow(TableRow row, string propName)
    {
        return _yellowCells.TryGetValue(row, out var props) && props.Contains(propName);
    }

    private void PersistMark(string fileName, int rowIndex, string prop, string value)
    {
        var pageName = GetVm()?.PageName ?? "";
        var key = $"{pageName}|{fileName}|{rowIndex}|{prop}";
        _savedMarkings[key] = value;
        SaveMarkings();
    }

    private void UnpersistMark(string fileName, int rowIndex, string prop)
    {
        var pageName = GetVm()?.PageName ?? "";
        var key = $"{pageName}|{fileName}|{rowIndex}|{prop}";
        _savedMarkings.Remove(key);
        SaveMarkings();
    }

    private void RestoreCellMarkings()
    {
        var vm = GetVm();
        if (vm?.SelectedNote is null) return;
        var fileName = vm.SelectedNote.FileName;

        _modifiedCells.Clear();
        _passedCells.Clear();
        _yellowCells.Clear();

        var prefix = $"{vm.PageName}|{fileName}|";
        foreach (var kvp in _savedMarkings)
        {
            if (!kvp.Key.StartsWith(prefix)) continue;
            var rest = kvp.Key[prefix.Length..];
            var parts = rest.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var ri)) continue;
            var prop = parts[1];
            if (ri < 0 || ri >= vm.Rows.Count) continue;
            var row = vm.Rows[ri];

            if (kvp.Value == "red")
            {
                if (!_modifiedCells.TryGetValue(row, out var props))
                {
                    props = new HashSet<string>();
                    _modifiedCells[row] = props;
                }
                props.Add(prop);
            }
            else if (kvp.Value == "green")
            {
                if (!_passedCells.TryGetValue(row, out var props))
                {
                    props = new HashSet<string>();
                    _passedCells[row] = props;
                }
                props.Add(prop);
            }
            else if (kvp.Value == "yellow")
            {
                if (!_yellowCells.TryGetValue(row, out var props))
                {
                    props = new HashSet<string>();
                    _yellowCells[row] = props;
                }
                props.Add(prop);
            }
        }
    }

    private void MarkGreen(TableRow row, string propName)
    {
        if (!_passedCells.TryGetValue(row, out var props))
        {
            props = new HashSet<string>();
            _passedCells[row] = props;
        }
        props.Add(propName);

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) PersistMark(vm.SelectedNote.FileName, idx, propName, "green");
        }
    }

    private void RemoveGreen(TableRow row, string propName)
    {
        if (_passedCells.TryGetValue(row, out var props))
        {
            props.Remove(propName);
            if (props.Count == 0) _passedCells.Remove(row);
        }

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) UnpersistMark(vm.SelectedNote.FileName, idx, propName);
        }
    }

    private void RemoveRed(TableRow row, string propName)
    {
        if (_modifiedCells.TryGetValue(row, out var props))
        {
            props.Remove(propName);
            if (props.Count == 0) _modifiedCells.Remove(row);
        }

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) UnpersistMark(vm.SelectedNote.FileName, idx, propName);
        }
    }

    private void RemoveYellow(TableRow row, string propName)
    {
        if (_yellowCells.TryGetValue(row, out var props))
        {
            props.Remove(propName);
            if (props.Count == 0) _yellowCells.Remove(row);
        }

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) UnpersistMark(vm.SelectedNote.FileName, idx, propName);
        }
    }

    private async Task ShowTranslationStatusDialog(TableViewCell cell, TableRow row, string propName)
    {
        var dialog = new ContentDialog
        {
            Title = "Translation Status",
            Content = new TextBlock
            {
                Text = "A bug/text edit has been reported for this translation.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Set as Pass",
            SecondaryButtonText = "Set as Pending",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            RemoveRed(row, propName);
            RemoveYellow(row, propName);
            MarkGreen(row, propName);
            cell.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));
        }
        else if (result == ContentDialogResult.Secondary)
        {
            RemoveRed(row, propName);
            RemoveGreen(row, propName);
            RemoveYellow(row, propName);
            cell.Background = null;

            var vm = GetVm();
            if (vm?.SelectedNote is not null)
            {
                var rowIndex = vm.Rows.IndexOf(row);
                if (rowIndex >= 0)
                {
                    WeakReferenceMessenger.Default.Send(new SetChangelogPendingMessage(vm.SelectedNote.Id, rowIndex, propName));
                }
            }
        }
    }

    public void MarkCellYellowFromExternal(TableRow row, string propName)
    {
        MarkCellYellow(row, propName);
    }

    private void MarkCellYellow(TableRow row, string propName)
    {
        RemoveGreen(row, propName);
        RemoveRed(row, propName);

        if (!_yellowCells.TryGetValue(row, out var props))
        {
            props = new HashSet<string>();
            _yellowCells[row] = props;
        }
        props.Add(propName);

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) PersistMark(vm.SelectedNote.FileName, idx, propName, "yellow");
        }

        var yellow = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xF9, 0xC4));
        FindAndColorCell(row, propName, yellow);
    }

    private void MarkCellRed(TableRow row, string propName)
    {
        RemoveGreen(row, propName);
        RemoveYellow(row, propName);

        if (!_modifiedCells.TryGetValue(row, out var props))
        {
            props = new HashSet<string>();
            _modifiedCells[row] = props;
        }
        props.Add(propName);

        var vm = GetVm();
        if (vm?.SelectedNote is not null)
        {
            var idx = vm.Rows.IndexOf(row);
            if (idx >= 0) PersistMark(vm.SelectedNote.FileName, idx, propName, "red");
        }

        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCD, 0xD2));
        FindAndColorCell(row, propName, red);
    }

    private static string ChecklistPropToTextFilesProp(string checklistProp) => checklistProp switch
    {
        "Col2" => "Col2",
        "Col4" => "Col3",
        "Col5" => "Col4",
        "Col6" => "Col5",
        "Col7" => "Col6",
        _ => checklistProp,
    };

    private async Task UpdateTextFilesCell(NotePageViewModel textFilesVm, string fileName, string stringId, string columnProp, string expectedResult)
    {
        if (string.IsNullOrEmpty(stringId)) return;

        var textFilesNote = textFilesVm.Notes.FirstOrDefault(n => n.FileName == fileName);
        if (textFilesNote is null) return;

        var rows = await textFilesVm.ExcelService.LoadRowsAsync(textFilesNote);
        var targetRow = rows.FirstOrDefault(r => r.Col1 == stringId);
        if (targetRow is null) return;

        var tfPropName = ChecklistPropToTextFilesProp(columnProp);
        var dstProp = typeof(TableRow).GetProperty(tfPropName);
        if (dstProp is null) return;

        dstProp.SetValue(targetRow, expectedResult);

        await textFilesVm.ExcelService.SaveRowsAsync(textFilesNote, rows);

        if (textFilesVm.SelectedNote == textFilesNote)
        {
            var newRows = await textFilesVm.ExcelService.LoadRowsAsync(textFilesNote);
            textFilesVm.Rows.Clear();
            foreach (var r in newRows)
                textFilesVm.Rows.Add(r);

            var newTarget = textFilesVm.Rows.FirstOrDefault(r => r.Col1 == stringId);
            if (newTarget is not null)
            {
                var tfPage = MainWindow.Instance?.TextFilesPage;
                if (tfPage is not null)
                    tfPage.MarkCellYellowFromExternal(newTarget, tfPropName);
            }
        }
    }

    private void FindAndColorCell(TableRow row, string propName, Brush brush)
    {
        if (_tableView is null) return;
        var colIndex = Array.FindIndex(_columns, c => c.Prop == propName);
        if (colIndex < 0 || colIndex >= _tableView.Columns.Count) return;
        var col = _tableView.Columns[colIndex];
        var vm = GetVm();
        var rowIndex = vm?.Rows.IndexOf(row) ?? -1;
        if (rowIndex < 0) return;

        ApplyCellBackground(_tableView, rowIndex, col, brush);
    }

    private static void ApplyCellBackground(DependencyObject parent, int targetRowIndex, TableViewColumn targetCol, Brush? brush)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TableViewCell cell)
            {
                if (cell.Slot.Row == targetRowIndex && cell.Column == targetCol)
                {
                    cell.Background = brush;
                    return;
                }
            }
            ApplyCellBackground(child, targetRowIndex, targetCol, brush);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not TableRow row)
            return;

        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCD, 0xD2));
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));
        var yellow = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xF9, 0xC4));

        _modifiedCells.TryGetValue(row, out var redProps);
        _passedCells.TryGetValue(row, out var greenProps);
        _yellowCells.TryGetValue(row, out var yellowProps);

        var vm = GetVm();
        bool isChangelog = vm?.PageName == "Changelog";

        var maxCols = _tableView?.Columns.Count ?? 0;
        for (int colIndex = 0; colIndex < maxCols && colIndex < _columns.Length; colIndex++)
        {
            var prop = _columns[colIndex].Prop;
            var header = _columns[colIndex].Header;
            Brush? brush = null;

            if (redProps?.Contains(prop) == true)
                brush = red;
            else if (greenProps?.Contains(prop) == true)
                brush = green;
            else if (yellowProps?.Contains(prop) == true)
                brush = yellow;

            ApplyCellBackground(args.ItemContainer, args.ItemIndex, _tableView!.Columns[colIndex], brush);

            if (isChangelog && row.Col8 != row.Col9 && (prop == "Col8" || prop == "Col9"))
            {
                var col = _tableView!.Columns[colIndex];
                var cell = FindTableViewCell(args.ItemContainer, args.ItemIndex, col)
                        ?? FindTableViewCell(_tableView, args.ItemIndex, col);
                if (cell is not null)
                    ApplyDiffToCell(cell, row, header == "Actual Result");
            }
        }
    }

    private void ComputeDiff()
    {
        _diffCells.Clear();

        var vm = GetVm();
        if (vm?.PageName != "Changelog") return;

        foreach (var row in vm.Rows)
        {
            if (row.Col8 != row.Col9)
            {
                _diffCells[row] = new HashSet<string> { "Col8", "Col9" };
            }
        }

        _diffVisualsPending = true;
        UpdateDiffVisuals();
    }

    private void OnTableViewLayoutUpdated(object? sender, object e)
    {
        if (!_diffVisualsPending) return;
        _diffVisualsPending = false;
        UpdateDiffVisuals();
    }

    private static TableViewCell? FindTableViewCell(DependencyObject parent, int rowIndex, TableViewColumn col)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TableViewCell cell && cell.Slot.Row == rowIndex && cell.Column == col)
                return cell;
            var found = FindTableViewCell(child, rowIndex, col);
            if (found is not null) return found;
        }
        return null;
    }

    private static void BuildDiffInlines(TextBlock tb, string oldText, string newText, bool isActualResult)
    {
        tb.Inlines.Clear();

        var segments = ComputeWordDiff(oldText, newText);
        var black = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x80, 0x00));

        bool first = true;
        foreach (var seg in segments)
        {
            bool show = seg.Op == DiffOp.Same
                || (seg.Op == DiffOp.Delete && isActualResult)
                || (seg.Op == DiffOp.Insert && !isActualResult);

            if (!show) continue;

            if (!first)
                tb.Inlines.Add(new Run { Text = " " });

            var brush = seg.Op == DiffOp.Same ? black
                : seg.Op == DiffOp.Delete ? red
                : green;

            tb.Inlines.Add(new Run { Text = seg.Text, Foreground = brush });
            first = false;
        }
    }



    private enum DiffOp { Same, Delete, Insert }

    private sealed record DiffSegment(DiffOp Op, string Text);

    private static List<DiffSegment> ComputeWordDiff(string oldText, string newText)
    {
        var oldWords = Tokenize(oldText);
        var newWords = Tokenize(newText);

        int m = oldWords.Length, n = newWords.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                if (oldWords[i - 1] == newWords[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var reversed = new List<DiffSegment>();
        int x = m, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && oldWords[x - 1] == newWords[y - 1])
            {
                reversed.Add(new DiffSegment(DiffOp.Same, oldWords[x - 1]));
                x--; y--;
            }
            else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
            {
                reversed.Add(new DiffSegment(DiffOp.Insert, newWords[y - 1]));
                y--;
            }
            else
            {
                reversed.Add(new DiffSegment(DiffOp.Delete, oldWords[x - 1]));
                x--;
            }
        }

        reversed.Reverse();
        return reversed;
    }

    private static string[] Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ApplyDiffToCell(TableViewCell cell, TableRow row, bool isActualResult)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var oldText = row.Col8;
        var newText = row.Col9;
        BuildDiffInlines(tb, oldText, newText, isActualResult);
        cell.Content = tb;
    }

    private void UpdateDiffVisuals()
    {
        var vm = GetVm();
        if (vm?.PageName != "Changelog" || _tableView is null) return;

        for (int i = 0; i < vm.Rows.Count; i++)
        {
            var row = vm.Rows[i];
            if (!_diffCells.ContainsKey(row)) continue;

            for (int ci = 0; ci < _columns.Length; ci++)
            {
                if (_columns[ci].Prop is not "Col8" and not "Col9") continue;
                var cell = FindTableViewCell(_tableView, i, _tableView.Columns[ci]);
                if (cell is null) continue;
                ApplyDiffToCell(cell, row, _columns[ci].Header == "Actual Result");
            }
        }
    }

    private static string GetPropertyValue(TableRow row, string propName)
    {
        var prop = typeof(TableRow).GetProperty(propName);
        return prop?.GetValue(row) as string ?? string.Empty;
    }

    private static void SetPropertyValue(TableRow row, string propName, string value)
    {
        var prop = typeof(TableRow).GetProperty(propName);
        prop?.SetValue(row, value);
    }

    private static void CopyToClipboard(string text)
    {
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(text);
        Clipboard.SetContent(pkg);
    }

    private async Task ShowEditDialog(TableRow row, string propName, string currentValue, string header)
    {
        var isTranslation = header is "French" or "Italian" or "German" or "Spanish";

        var typeCombo = new ComboBox
        {
            Header = "Type",
            PlaceholderText = "Select a type",
            ItemsSource = new[] { "Grammar", "Spelling", "Missing Translation", "Inconsistent Translation", "Glossary", "Incorrect Translation", "Shortening", "Compliance" },
            SelectedIndex = -1,
            MinWidth = 300,
        };

        var descBox = new TextBox
        {
            Header = "Description",
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
        };
        descBox.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var menuState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
                if ((menuState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down)
                {
                    var idx = descBox.SelectionStart;
                    descBox.Text = descBox.Text.Insert(idx, "\r\n");
                    descBox.SelectionStart = idx + 2;
                    e.Handled = true;
                }
            }
        };

        var nextEditNum = (MainWindow.Instance?.ViewModel.ChangelogVm.Rows.Count ?? 0) + 1;

        var stringIdBox = new TextBox
        {
            Header = "String ID",
            Text = row.Col1,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
        };

        var sourceBox = new TextBox
        {
            Header = "Source",
            Text = row.Col2,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
        };

        var actualBox = new TextBox
        {
            Header = "Actual Result",
            Text = currentValue,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
        };

        var expectedBox = new TextBox
        {
            Header = "Expected Result",
            Text = currentValue,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(typeCombo);
        stack.Children.Add(descBox);
        stack.Children.Add(stringIdBox);
        stack.Children.Add(sourceBox);
        stack.Children.Add(actualBox);
        stack.Children.Add(expectedBox);

        var dialog = new ContentDialog
        {
            Title = $"Text Edit - {nextEditNum}",
            Content = stack,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (isTranslation)
            {
                MarkCellYellow(row, propName);
            }
            else
            {
                SetPropertyValue(row, propName, expectedBox.Text);
                MarkCellYellow(row, propName);
            }

            var checklistVm = GetVm();
            if (checklistVm?.PageName == "Checklist")
            {
                var rowIndex = checklistVm.Rows.IndexOf(row);
                WeakReferenceMessenger.Default.Send(new EditSavedMessage(
                    Type: typeCombo.SelectedItem as string ?? "",
                    Description: descBox.Text,
                    StringId: row.Col1,
                    Source: row.Col2,
                    ActualResult: currentValue,
                    ExpectedResult: expectedBox.Text,
                    ChecklistNoteId: checklistVm.SelectedNote?.Id ?? Guid.Empty,
                    RowIndex: rowIndex >= 0 ? rowIndex : 0,
                    ColumnProp: propName
                ));
            }
        }
    }

    private async Task ShowBugDialog(TableRow row, string propName, string header)
    {
        var typeOptions = new[] { "Grammar", "Spelling", "Missing Translation", "Inconsistent Translation", "Glossary", "Incorrect Translation", "Shortening", "Compliance" };

        var typeCombo = new ComboBox
        {
            Header = "Type",
            PlaceholderText = "Select a type",
            ItemsSource = typeOptions,
            SelectedIndex = -1,
            MinWidth = 300,
        };

        var summaryBox = new TextBox { Header = "Summary", TextWrapping = TextWrapping.Wrap, Height = 60 };

        var descBox = new TextBox
        {
            Header = "Description",
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
            AcceptsReturn = true,
        };

        var stepsBox = new TextBox { Header = "Steps to Reproduce", TextWrapping = TextWrapping.Wrap, Height = 80, AcceptsReturn = true };

        var stack = new StackPanel { Spacing = 8, MaxHeight = 500 };
        stack.Children.Add(typeCombo);
        stack.Children.Add(summaryBox);
        stack.Children.Add(descBox);
        stack.Children.Add(stepsBox);

        var scroller = new ScrollViewer { Content = stack, MaxHeight = 500 };

        var dialog = new ContentDialog
        {
            Title = "Report Bug",
            Content = scroller,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var bugVm = MainWindow.Instance?.ViewModel.BugTrackerVm;
            if (bugVm is null) return;

            var bugNote = bugVm.Notes.FirstOrDefault();
            if (bugNote is null) return;

            var rows = await bugVm.ExcelService.LoadRowsAsync(bugNote);
            var summary = summaryBox.Text;
            if (header is "French" or "Italian" or "German" or "Spanish")
            {
                var initial = header switch { "French" => "FR", "Italian" => "IT", "German" => "DE", _ => "ES" };
                summary = BuildBugSummaryWithLanguages(summary, $"[{initial}]");
            }
            var newRow = new TableRow
            {
                Col1 = (rows.Count + 1).ToString(),
                Col2 = Environment.UserName,
                Col3 = DateTime.Now.ToString("yyyy/MM/dd"),
                Col4 = typeCombo.SelectedItem as string ?? "",
                Col5 = summary,
                Col6 = descBox.Text,
                Col7 = stepsBox.Text,
            };
            switch (header)
            {
                case "French": newRow.Col8 = "Affected"; break;
                case "Italian": newRow.Col9 = "Affected"; break;
                case "German": newRow.Col10 = "Affected"; break;
                case "Spanish": newRow.Col11 = "Affected"; break;
            }
            rows.Add(newRow);
            await bugVm.ExcelService.SaveRowsAsync(bugNote, rows);

            if (bugVm.SelectedNote == bugNote)
            {
                bugVm.Rows.Clear();
                foreach (var r in rows)
                    bugVm.Rows.Add(r);
            }

            var isTranslation = header is "French" or "Italian" or "German" or "Spanish";
            if (isTranslation)
            {
                MarkCellYellow(row, propName);
            }
            else
            {
                SetPropertyValue(row, propName, $"BUG: {summaryBox.Text}");
                MarkCellYellow(row, propName);
            }
        }
    }
}
