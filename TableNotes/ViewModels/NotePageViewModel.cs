using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ClosedXML.Excel;
using TableNotes.Models;
using TableNotes.Services;

namespace TableNotes.ViewModels;

public record EditSavedMessage(
    string Type,
    string Description,
    string StringId,
    string Source,
    string ActualResult,
    string ExpectedResult,
    Guid ChecklistNoteId,
    int RowIndex,
    string ColumnProp
);

public record RevertCellMessage(Guid NoteId, int RowIndex, string ColumnProp, bool Reapply = false, string ExpectedResult = "");
public record SetChangelogPendingMessage(Guid NoteId, int RowIndex, string ColumnProp);

public partial class NotePageViewModel : ObservableObject
{
    private readonly ExcelService _excelService;
    public ExcelService ExcelService => _excelService;

    public string PageName { get; }

    public bool ShowTreeView => PageName == "Checklist";
    public bool IsChangelog => PageName == "Changelog";
    public bool IsTextFiles => PageName == "TextFiles";
    public bool IsBugTracker => PageName == "BugTracker";

    public static string GetChangelogNoteTitle(string columnProp) => columnProp switch
    {
        "Col2" => "Source",
        "Col4" => "French",
        "Col5" => "Italian",
        "Col6" => "German",
        "Col7" => "Spanish",
        _ => "Other",
    };

    public static readonly string[] ChangelogNoteTitles = ["Source", "French", "Italian", "German", "Spanish"];

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

    private static string MasterTextsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TableNotes", "MasterTexts");

    private async Task<List<TableRow>> LoadRowsFromMasterAsync(string fileName)
    {
        var masterPath = Path.Combine(MasterTextsDirectory, fileName);
        var rows = new List<TableRow>();

        if (!File.Exists(masterPath))
            return rows;

        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(masterPath);
            var ws = workbook.Worksheet(1);
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                rows.Add(new TableRow
                {
                    Col1 = row.Cell(1).GetString(),
                    Col2 = row.Cell(2).GetString(),
                    Col3 = string.Empty,
                    Col4 = row.Cell(3).GetString(),
                    Col5 = row.Cell(4).GetString(),
                    Col6 = row.Cell(5).GetString(),
                    Col7 = row.Cell(6).GetString(),
                });
            }
        });

        return rows;
    }

    private async Task EnsurePlaceholdersAsync()
    {
        if (PageName == "Changelog")
        {
            Notes.Clear();
            foreach (var title in ChangelogNoteTitles)
            {
                var fileName = $"{title}.xlsx";
                var note = new TableNote { Title = title, FileName = fileName };
                Notes.Add(note);
            }
            SelectedNote = Notes[0];
            await _excelService.SaveIndexAsync(Notes.ToList());
            return;
        }

        if (PageName == "TextFiles")
        {
            var masterDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TableNotes", "MasterTexts");

            if (!Directory.Exists(masterDir))
                return;

            var changed = false;
            foreach (var file in Directory.GetFiles(masterDir, "*.xlsx"))
            {
                var fileName = Path.GetFileName(file);
                var note = Notes.FirstOrDefault(n => n.FileName == fileName);
                var targetPath = Path.Combine(_excelService.DataDirectory, fileName);

                if (!File.Exists(targetPath))
                {
                    File.Copy(file, targetPath, overwrite: true);
                }

                if (note is null)
                {
                    note = new TableNote { Title = fileName, FileName = fileName };
                    Notes.Add(note);
                    changed = true;
                }
            }

            if (changed)
                await _excelService.SaveIndexAsync(Notes.ToList());

            if (Notes.Count > 0)
                SelectedNote = Notes[0];
            return;
        }

        if (PageName == "BugTracker")
        {
            if (Notes.Count == 0)
            {
                var note = new TableNote { Title = "Bug Tracker", FileName = "BugTracker.xlsx" };
                Notes.Add(note);
                await _excelService.SaveIndexAsync(Notes.ToList());
            }
            SelectedNote = Notes[0];
            return;
        }

        if (PageName != "Checklist")
            return;

        var changedChecklist = false;

        foreach (var (title, fileName) in new[] { ("Dialogue", "Dialogue.xlsx"), ("UI", "UI.xlsx") })
        {
            var path = Path.Combine(_excelService.DataDirectory, fileName);
            var note = Notes.FirstOrDefault(n => n.FileName == fileName);

            if (!File.Exists(path))
            {
                var rows = await LoadRowsFromMasterAsync(fileName);
                if (rows.Count == 0)
                    continue;

                var obsRows = new ObservableCollection<TableRow>(rows);

                if (note is not null)
                {
                    await _excelService.SaveRowsAsync(note, obsRows);
                }
                else
                {
                    note = new TableNote { Title = title, FileName = fileName };
                    await _excelService.SaveRowsAsync(note, obsRows);
                    Notes.Add(note);
                    changedChecklist = true;
                }
            }
            else if (note is null)
            {
                note = new TableNote { Title = title, FileName = fileName };
                Notes.Add(note);
                changedChecklist = true;
            }
        }

        if (changedChecklist)
            await _excelService.SaveIndexAsync(Notes.ToList());
    }

    private async Task LoadRowsAsync(TableNote note)
    {
        Rows.Clear();
        var rows = await _excelService.LoadRowsAsync(note);
        foreach (var r in rows)
            Rows.Add(r);
    }

    public async Task AddChangelogEntry(string type, string description, string stringId, string source, string actualResult, string expectedResult, string columnProp)
    {
        var noteTitle = GetChangelogNoteTitle(columnProp);
        var note = Notes.FirstOrDefault(n => n.Title == noteTitle);
        if (note is null)
        {
            note = new TableNote { Title = noteTitle, FileName = $"{noteTitle}.xlsx" };
            Notes.Insert(0, note);
            await _excelService.SaveIndexAsync(Notes.ToList());
        }

        var rows = await _excelService.LoadRowsAsync(note);
        var nextId = (rows.Count + 1).ToString();
        var row = new TableRow
        {
            Col1 = nextId,
            Col2 = Environment.UserName,
            Col3 = DateTime.Now.ToString("yyyy/MM/dd"),
            Col4 = type,
            Col5 = description,
            Col6 = stringId,
            Col7 = source,
            Col8 = actualResult,
            Col9 = expectedResult,
            Col10 = "Pending",
        };
        rows.Add(row);
        await _excelService.SaveRowsAsync(note, rows);

        if (SelectedNote == note)
        {
            Rows.Clear();
            foreach (var r in rows)
                Rows.Add(r);
        }
    }

    private string GetUniqueFileName(string title)
    {
        var existing = Notes.Select(n => n.FileName).ToHashSet();
        return ExcelService.GenerateFileName(title, existing);
    }

    [RelayCommand]
    private void NewNote()
    {
        if (PageName is "Changelog" or "TextFiles" or "BugTracker") return;
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
        if (PageName is "Changelog" or "TextFiles" or "BugTracker") return;
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
        var row = new TableRow();
        if (PageName == "BugTracker")
        {
            row.Col1 = (Rows.Count + 1).ToString();
            row.Col2 = Environment.UserName;
            row.Col3 = DateTime.Now.ToString("yyyy/MM/dd");
        }
        Rows.Add(row);
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
