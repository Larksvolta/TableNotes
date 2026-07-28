using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableNotes.Models;
using TableNotes.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using WinUI.TableView;

namespace TableNotes.Views;

public sealed partial class NotePage : UserControl
{
    private TableView? _tableView;
    private ColumnDef[] _columns = [];
    private TreeViewNode? _rootNode;
    private bool _sidebarExpanded = true;
    private readonly Dictionary<Guid, TreeViewNode> _noteNodeMap = new();

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
            SetupSidebar(vm);
            SetupTableView(vm);
        }
    }

    private void SetupSidebar(NotePageViewModel vm)
    {
        NotesList.Visibility = vm.ShowTreeView ? Visibility.Collapsed : Visibility.Visible;
        NotesTree.Visibility = vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;

        if (vm.ShowTreeView)
        {
            RebuildTree(vm);
            vm.Notes.CollectionChanged += OnNotesCollectionChanged;
            NotesTree.ItemInvoked += OnTreeItemInvoked;
        }
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

        RootLayout.ColumnDefinitions[0].Width = _sidebarExpanded ? new GridLength(260) : new GridLength(0);

        var vm = GetVm();
        if (vm is not null)
        {
            NotesList.Visibility = _sidebarExpanded && !vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
            NotesTree.Visibility = _sidebarExpanded && vm.ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
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
        };

        var wrapStyle = new Style(typeof(TextBlock));
        wrapStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));

        foreach (var col in _columns)
        {
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
            };

            tc.Width = col.Header switch
            {
                "String ID" => new GridLength(100),
                "Steps to Reproduce" => new GridLength(150),
                _ => new GridLength(1, GridUnitType.Star),
            };

            _tableView.Columns.Add(tc);
        }

        _tableView.CellContextFlyout = new MenuFlyout();
        _tableView.CellContextFlyoutOpening += OnCellContextFlyoutOpening;

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
        var currentValue = GetPropertyValue(row, prop);

        flyout.Items.Clear();

        var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
        copyItem.Click += (_, _) => CopyToClipboard(currentValue);

        var editItem = new MenuFlyoutItem { Text = "Edit", Icon = new SymbolIcon(Symbol.Edit) };
        editItem.Click += (_, _) => _ = ShowEditDialog(row, prop, currentValue);

        var bugItem = new MenuFlyoutItem { Text = "Bug", Icon = new SymbolIcon(Symbol.ReportHacked) };
        bugItem.Click += (_, _) => _ = ShowBugDialog(row, prop);

        flyout.Items.Add(copyItem);
        flyout.Items.Add(editItem);
        flyout.Items.Add(bugItem);
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

    private async Task ShowEditDialog(TableRow row, string propName, string currentValue)
    {
        var editBox = new TextBox
        {
            Text = currentValue,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 200,
        };

        var dialog = new ContentDialog
        {
            Title = "Text Edit",
            Content = editBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            SetPropertyValue(row, propName, editBox.Text);
    }

    private async Task ShowBugDialog(TableRow row, string propName)
    {
        var titleBox = new TextBox { Header = "Title", PlaceholderText = "Bug title" };
        var descBox = new TextBox
        {
            Header = "Description",
            PlaceholderText = "Bug description",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 100,
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(titleBox);
        stack.Children.Add(descBox);

        var dialog = new ContentDialog
        {
            Title = "Dev Bug",
            Content = stack,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var bugText = $"BUG: {titleBox.Text} - {descBox.Text}";
            SetPropertyValue(row, propName, bugText);
        }
    }
}
