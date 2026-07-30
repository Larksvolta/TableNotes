using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TableNotes.Models;
using TableNotes.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Documents;
using System.Text.Json;
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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TableNotes", "cell_markings.json");
    private readonly Dictionary<TableRow, HashSet<string>> _diffCells = new();
    private bool _diffVisualsPending;
    private bool _rowsCleared;
    private readonly Dictionary<(string NoteFileName, string RowNumber), (Guid NoteId, int RowIndex, string ColumnProp, TableNote ChangelogNote)> _changelogRevertMap = new();
    private readonly Dictionary<(Guid NoteId, int RowIndex, string ColumnProp), (TableRow ChangelogRow, TableNote ChangelogNote)> _changelogReverseMap = new();
    private int _selectedBugIndex = -1;
    private ComboBox? _bugFormType;
    private TextBox? _bugFormSummary;
    private TextBox? _bugFormDesc;
    private TextBox? _bugFormSteps;
    private ComboBox? _bugFormFrench;
    private ComboBox? _bugFormItalian;
    private ComboBox? _bugFormGerman;
    private ComboBox? _bugFormSpanish;
    private TextBlock? _bugFormId;
    private TextBlock? _bugFormUsername;
    private TextBlock? _bugFormDate;

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
            PopulateBugTrackerSidebar(vm);
            vm.Rows.CollectionChanged += (_, _) => PopulateBugTrackerSidebar(vm);
            return;
        }

        NotesList.Visibility = vm.ShowTreeView ? Visibility.Collapsed : Visibility.Visible;
        NotesTree.Visibility = vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;

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

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerGrid = new Grid { Height = 32 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        var bold = Microsoft.UI.Text.FontWeights.SemiBold;
        var numHdr = new TextBlock { Text = "#", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        headerGrid.Children.Add(numHdr);
        var summaryHdr = new TextBlock { Text = "Summary", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        Grid.SetColumn(summaryHdr, 1);
        headerGrid.Children.Add(summaryHdr);
        var statusHdr = new TextBlock { Text = "Status", FontWeight = bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
        Grid.SetColumn(statusHdr, 2);
        headerGrid.Children.Add(statusHdr);
        headerGrid.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });
        root.Children.Add(headerGrid);

        var selBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x00, 0x78, 0xD7));
        var itemsPanel = new StackPanel();

        for (int i = 0; i < vm.Rows.Count; i++)
        {
            var row = vm.Rows[i];
            var idx = i;
            var isSelected = i == _selectedBugIndex;

            var rowGrid = new Grid { Height = 40 };
            if (isSelected) rowGrid.Background = selBrush;
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            rowGrid.Children.Add(new TextBlock { Text = row.Col1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            var st = new TextBlock { Text = row.Col5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(st, 1);
            rowGrid.Children.Add(st);

            var sp = BuildStatusPanel(row);
            Grid.SetColumn(sp, 2);
            rowGrid.Children.Add(sp);

            rowGrid.PointerPressed += (_, _) => OnBugSelected(idx);
            itemsPanel.Children.Add(rowGrid);
        }

        var scroller = new ScrollViewer { Content = itemsPanel };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        AlignSidebarWithTable(root);
        BugTrackerSidebar.Children.Add(root);
    }

    private void OnBugSelected(int index)
    {
        var vm = GetVm();
        if (vm is null) return;
        _selectedBugIndex = index;
        PopulateBugTrackerSidebar(vm);
        PopulateBugForm(index < vm.Rows.Count ? vm.Rows[index] : null);
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
            _bugFormFrench!.SelectedIndex = -1;
            _bugFormItalian!.SelectedIndex = -1;
            _bugFormGerman!.SelectedIndex = -1;
            _bugFormSpanish!.SelectedIndex = -1;
            return;
        }

        _bugFormId!.Text = row.Col1;
        _bugFormUsername!.Text = row.Col2;
        _bugFormDate!.Text = row.Col3;
        _bugFormType!.SelectedItem = row.Col4;
        _bugFormSummary!.Text = row.Col5;
        _bugFormDesc!.Text = row.Col6;
        _bugFormSteps!.Text = row.Col7;
        _bugFormFrench!.SelectedItem = row.Col8;
        _bugFormItalian!.SelectedItem = row.Col9;
        _bugFormGerman!.SelectedItem = row.Col10;
        _bugFormSpanish!.SelectedItem = row.Col11;
    }

    private async Task SaveBugForm()
    {
        var vm = GetVm();
        if (vm is null || _selectedBugIndex < 0 || _selectedBugIndex >= vm.Rows.Count) return;

        var row = vm.Rows[_selectedBugIndex];
        row.Col4 = _bugFormType?.SelectedItem as string ?? "";
        row.Col5 = _bugFormSummary?.Text ?? "";
        row.Col6 = _bugFormDesc?.Text ?? "";
        row.Col7 = _bugFormSteps?.Text ?? "";
        row.Col8 = _bugFormFrench?.SelectedItem as string ?? "";
        row.Col9 = _bugFormItalian?.SelectedItem as string ?? "";
        row.Col10 = _bugFormGerman?.SelectedItem as string ?? "";
        row.Col11 = _bugFormSpanish?.SelectedItem as string ?? "";

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
        var langOptions = new[] { "Not Affected", "Affected" };

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

        form.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = Application.Current.Resources["ControlStrokeColorDefaultBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)) });
        form.Children.Add(new TextBlock { Text = "Languages", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var langRow1 = new Grid { ColumnSpacing = 16 };
        langRow1.ColumnDefinitions.Add(new ColumnDefinition());
        langRow1.ColumnDefinitions.Add(new ColumnDefinition());
        _bugFormFrench = new ComboBox { Header = "French", ItemsSource = langOptions, MinWidth = 200 };
        _bugFormItalian = new ComboBox { Header = "Italian", ItemsSource = langOptions, MinWidth = 200 };
        Grid.SetColumn(_bugFormFrench, 0);
        langRow1.Children.Add(_bugFormFrench);
        Grid.SetColumn(_bugFormItalian, 1);
        langRow1.Children.Add(_bugFormItalian);
        form.Children.Add(langRow1);

        var langRow2 = new Grid { ColumnSpacing = 16 };
        langRow2.ColumnDefinitions.Add(new ColumnDefinition());
        langRow2.ColumnDefinitions.Add(new ColumnDefinition());
        _bugFormGerman = new ComboBox { Header = "German", ItemsSource = langOptions, MinWidth = 200 };
        _bugFormSpanish = new ComboBox { Header = "Spanish", ItemsSource = langOptions, MinWidth = 200 };
        Grid.SetColumn(_bugFormGerman, 0);
        langRow2.Children.Add(_bugFormGerman);
        Grid.SetColumn(_bugFormSpanish, 1);
        langRow2.Children.Add(_bugFormSpanish);
        form.Children.Add(langRow2);

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

    private static FrameworkElement BuildStatusPanel(TableRow row)
    {
        var langVals = new[] { row.Col8, row.Col9, row.Col10, row.Col11 };
        var langLabels = new[] { "FR", "IT", "DE", "ES" };
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCD, 0xD2));
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9));

        var segmented = new Segmented
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        for (int i = 0; i < 4; i++)
        {
            var item = new SegmentedItem
            {
                Content = langLabels[i],
            };
            var bg = langVals[i] == "Affected" ? red : langVals[i] == "Not Affected" ? green : null;
            if (bg is not null)
                item.Background = bg;
            segmented.Items.Add(item);
        }

        segmented.Loaded += (s, e) =>
        {
            for (int i = 0; i < 4; i++)
            {
                if (segmented.ContainerFromIndex(i) is SegmentedItem container)
                {
                    container.CornerRadius = i switch
                    {
                        0 => new CornerRadius(4, 0, 0, 4),
                        3 => new CornerRadius(0, 4, 4, 0),
                        _ => new CornerRadius(0),
                    };
                }
            }
        };

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

        RootLayout.ColumnDefinitions[0].Width = _sidebarExpanded ? new GridLength(500) : new GridLength(0);

        var vm = GetVm();
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

    private void SetupTableView(NotePageViewModel vm)
    {
        TableViewContainer.Children.Clear();

        _columns = GetColumnDefs(vm.PageName);

        _tableView = new TableView
        {
            AutoGenerateColumns = false,
            ShowExportOptions = false,
            HeaderRowHeight = 32,
            RowHeight = 40,
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
            var newRow = new TableRow
            {
                Col1 = (rows.Count + 1).ToString(),
                Col2 = Environment.UserName,
                Col3 = DateTime.Now.ToString("yyyy/MM/dd"),
                Col4 = typeCombo.SelectedItem as string ?? "",
                Col5 = summaryBox.Text,
                Col6 = descBox.Text,
                Col7 = stepsBox.Text,
            };
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
