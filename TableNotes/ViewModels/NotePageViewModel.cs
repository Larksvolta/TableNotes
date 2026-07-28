using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableNotes.Models;
using TableNotes.Services;

namespace TableNotes.ViewModels;

public partial class NotePageViewModel : ObservableObject
{
    private readonly ExcelService _excelService;

    public string PageName { get; }

    public bool ShowTreeView => PageName == "Checklist";

    public ObservableCollection<TableNote> Notes { get; } = new();
    public ObservableCollection<TableRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedNote))]
    [NotifyPropertyChangedFor(nameof(SelectedNoteModifiedAt))]
    private TableNote? _selectedNote;

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasSelectedNote => SelectedNote is not null;
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public string SelectedNoteModifiedAt => SelectedNote?.ModifiedAt.ToString("g") ?? string.Empty;

    public NotePageViewModel(string pageName)
    {
        PageName = pageName;
        _excelService = new ExcelService(pageName);
    }

    partial void OnSelectedNoteChanged(TableNote? value)
    {
        if (value is not null)
        {
            EditTitle = value.Title;
            _ = LoadRowsAsync(value);
        }
    }

    public async Task LoadNotesAsync()
    {
        var notes = await _excelService.LoadIndexAsync();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<TableNote>();
        foreach (var n in notes)
        {
            if (seen.Add(n.FileName))
                deduped.Add(n);
        }

        Notes.Clear();
        foreach (var n in deduped.OrderByDescending(x => x.ModifiedAt))
            Notes.Add(n);

        await EnsurePlaceholdersAsync();

        if (Notes.Count > 0 && SelectedNote is null)
            SelectedNote = Notes[0];
    }

    private static TableRow CreateLoremIpsumRow()
    {
        var text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        return new TableRow { Col1 = text, Col2 = text, Col3 = text, Col4 = text, Col5 = text, Col6 = text, Col7 = text };
    }

    private async Task EnsurePlaceholdersAsync()
    {
        if (PageName != "Checklist")
            return;

        var changed = false;

        foreach (var (title, fileName) in new[] { ("Dialogue", "Dialogue.xlsx"), ("UI", "UI.xlsx") })
        {
            var path = Path.Combine(_excelService.DataDirectory, fileName);
            if (File.Exists(path))
                continue;

            var rows = new ObservableCollection<TableRow> { CreateLoremIpsumRow() };
            var existing = Notes.FirstOrDefault(n => n.FileName == fileName);

            if (existing is not null)
            {
                await _excelService.SaveRowsAsync(existing, rows);
            }
            else
            {
                var note = new TableNote { Title = title, FileName = fileName };
                await _excelService.SaveRowsAsync(note, rows);
                Notes.Add(note);
                changed = true;
            }
        }

        if (changed)
            await _excelService.SaveIndexAsync(Notes.ToList());
    }

    private async Task LoadRowsAsync(TableNote note)
    {
        Rows.Clear();
        var rows = await _excelService.LoadRowsAsync(note);
        foreach (var r in rows)
            Rows.Add(r);
    }

    private string GetUniqueFileName(string title)
    {
        var existing = Notes.Select(n => n.FileName).ToHashSet();
        return ExcelService.GenerateFileName(title, existing);
    }

    [RelayCommand]
    private void NewNote()
    {
        var note = new TableNote
        {
            Title = "New Note",
            ModifiedAt = DateTime.Now,
            FileName = GetUniqueFileName("New Note")
        };
        Notes.Insert(0, note);
        SelectedNote = note;
        Rows.Clear();
        Rows.Add(new TableRow());
        StatusMessage = "New note created";
    }

    [RelayCommand]
    private async Task SaveNote()
    {
        if (SelectedNote is null)
        {
            var note = new TableNote
            {
                Title = EditTitle,
                ModifiedAt = DateTime.Now,
                FileName = GetUniqueFileName(EditTitle)
            };
            Notes.Insert(0, note);
            SelectedNote = note;
        }
        else
        {
            var oldTitle = SelectedNote.Title;
            SelectedNote.Title = EditTitle;
            SelectedNote.ModifiedAt = DateTime.Now;

            if (SelectedNote.Title != oldTitle)
            {
                var newFile = GetUniqueFileName(SelectedNote.Title);
                _excelService.RenameFile(SelectedNote, newFile);
            }
        }

        await _excelService.SaveRowsAsync(SelectedNote!, Rows);
        await _excelService.SaveIndexAsync(Notes.ToList());
        StatusMessage = "Note saved";
    }

    [RelayCommand]
    private async Task DeleteNote()
    {
        if (SelectedNote is null) return;

        var toRemove = SelectedNote;
        var idx = Notes.IndexOf(toRemove);

        SelectedNote = null;
        EditTitle = string.Empty;
        Rows.Clear();
        Notes.Remove(toRemove);
        _excelService.DeleteFile(toRemove);
        await _excelService.SaveIndexAsync(Notes.ToList());

        if (Notes.Count > 0)
        {
            var nextIdx = Math.Min(idx, Notes.Count - 1);
            SelectedNote = Notes[nextIdx];
        }

        StatusMessage = "Note deleted";
    }

    [RelayCommand]
    private void AddRow()
    {
        Rows.Add(new TableRow());
    }

    private int _selectedRowIndex = -1;

    public int SelectedRowIndex
    {
        get => _selectedRowIndex;
        set
        {
            if (_selectedRowIndex != value)
            {
                _selectedRowIndex = value;
                OnPropertyChanged();
            }
        }
    }
}
