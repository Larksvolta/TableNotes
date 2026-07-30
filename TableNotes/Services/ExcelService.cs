using System.Collections.ObjectModel;
using System.Text.Json;
using ClosedXML.Excel;
using TableNotes.Models;

namespace TableNotes.Services;

public class ExcelService
{
    private readonly string _dataDir;
    private readonly string _indexPath;
    private readonly string _pageName;

    public string PageName => _pageName;
    public string DataDirectory => _dataDir;

    public ExcelService(string pageName = "", string? dataDir = null)
    {
        _pageName = pageName;
        if (dataDir is not null)
        {
            _dataDir = dataDir;
        }
        else
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TableNotes");
            _dataDir = string.IsNullOrEmpty(pageName) ? baseDir : Path.Combine(baseDir, pageName);
        }
        Directory.CreateDirectory(_dataDir);
        _indexPath = Path.Combine(_dataDir, "notes.json");
    }

    public async Task<List<TableNote>> LoadIndexAsync()
    {
        if (!File.Exists(_indexPath))
            return new List<TableNote>();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<List<TableNote>>(json) ?? new List<TableNote>();
    }

    public async Task SaveIndexAsync(List<TableNote> notes)
    {
        var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_indexPath, json);
    }

    public static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(title.Where(c => !invalid.Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    public void RenameFile(TableNote note, string newFileName)
    {
        var oldPath = Path.Combine(_dataDir, note.FileName);
        var newPath = Path.Combine(_dataDir, newFileName);
        if (File.Exists(oldPath) && oldPath != newPath)
        {
            File.Move(oldPath, newPath, overwrite: true);
        }
        note.FileName = newFileName;
    }

    public static string GenerateFileName(string title, HashSet<string> existingNames)
    {
        var baseName = SanitizeFileName(title) + ".xlsx";
        if (!existingNames.Contains(baseName))
            return baseName;

        for (int i = 2; i < 1000; i++)
        {
            var name = $"{SanitizeFileName(title)} ({i}).xlsx";
            if (!existingNames.Contains(name))
                return name;
        }
        return $"{SanitizeFileName(title)} ({Guid.NewGuid():N}).xlsx";
    }

    public async Task<ObservableCollection<TableRow>> LoadRowsAsync(TableNote note)
    {
        var path = Path.Combine(_dataDir, note.FileName);
        var rows = new ObservableCollection<TableRow>();

        if (!File.Exists(path))
            return rows;

        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(path);
            var ws = workbook.Worksheet(1);
            var columnCount = ws.ColumnsUsed().Count();
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var r = new TableRow
                {
                    Col1 = row.Cell(1).GetString(),
                    Col2 = row.Cell(2).GetString(),
                    Col3 = row.Cell(3).GetString(),
                    Col4 = row.Cell(4).GetString(),
                    Col5 = row.Cell(5).GetString(),
                    Col6 = columnCount >= 6 ? row.Cell(6).GetString() : string.Empty,
                    Col7 = columnCount >= 7 ? row.Cell(7).GetString() : string.Empty,
                    Col8 = columnCount >= 8 ? row.Cell(8).GetString() : string.Empty,
                    Col9 = columnCount >= 9 ? row.Cell(9).GetString() : string.Empty,
                    Col10 = columnCount >= 10 ? row.Cell(10).GetString() : string.Empty,
                    Col11 = columnCount >= 11 ? row.Cell(11).GetString() : string.Empty,
                };
                rows.Add(r);
            }
        });

        return rows;
    }

    private static string[] GetHeaders(string pageName) => pageName switch
    {
        "Checklist" => ["String ID", "Source", "Steps to Reproduce", "French", "Italian", "German", "Spanish"],
        "BugTracker" => ["#", "Username", "Date", "Type", "Summary", "Description", "Steps to reproduce", "French", "Italian", "German", "Spanish"],
        "Changelog" => ["#", "Tester", "Date", "Type", "Description", "String ID", "Source", "Actual Result", "Expected Result", "Status"],
        "TextFiles" => ["String ID", "Source", "French", "Italian", "German", "Spanish"],
        _ => ["Column 1", "Column 2", "Column 3", "Column 4", "Column 5"]
    };

    public async Task SaveRowsAsync(TableNote note, ObservableCollection<TableRow> rows)
    {
        var path = Path.Combine(_dataDir, note.FileName);
        var headers = GetHeaders(_pageName);

        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Data");

            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            for (int i = 0; i < rows.Count; i++)
            {
                ws.Cell(i + 2, 1).Value = rows[i].Col1;
                ws.Cell(i + 2, 2).Value = rows[i].Col2;
                ws.Cell(i + 2, 3).Value = rows[i].Col3;
                ws.Cell(i + 2, 4).Value = rows[i].Col4;
                ws.Cell(i + 2, 5).Value = rows[i].Col5;
                ws.Cell(i + 2, 6).Value = rows[i].Col6;
                ws.Cell(i + 2, 7).Value = rows[i].Col7;
                if (headers.Length >= 8) ws.Cell(i + 2, 8).Value = rows[i].Col8;
                if (headers.Length >= 9) ws.Cell(i + 2, 9).Value = rows[i].Col9;
                if (headers.Length >= 10) ws.Cell(i + 2, 10).Value = rows[i].Col10;
                if (headers.Length >= 11) ws.Cell(i + 2, 11).Value = rows[i].Col11;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(path);
        });
    }

    public void DeleteFile(TableNote note)
    {
        var path = Path.Combine(_dataDir, note.FileName);
        if (File.Exists(path))
            File.Delete(path);
    }
}
