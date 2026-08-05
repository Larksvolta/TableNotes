using System.Text.Json;

namespace TableNotes.Services;

public class ScreenshotStore
{
    private readonly string _filePath;
    private Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>? _cache;

    public ScreenshotStore(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "screenshots.json");
    }

    private Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> Load()
    {
        if (_cache is not null) return _cache;

        _cache = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
        if (File.Exists(_filePath))
        {
            try
            {
                _cache = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>>(
                    File.ReadAllText(_filePath)) ?? new();
            }
            catch { }
        }
        return _cache;
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(
                Load(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public List<string> GetScreenshots(string noteFileName, string bugId, string language)
    {
        if (Load().TryGetValue(noteFileName, out var byBug)
            && byBug.TryGetValue(bugId, out var byLang)
            && byLang.TryGetValue(language, out var files))
            return new List<string>(files);
        return new List<string>();
    }

    public List<string> GetLanguagesWithScreenshots(string noteFileName, string bugId)
    {
        if (Load().TryGetValue(noteFileName, out var byBug)
            && byBug.TryGetValue(bugId, out var byLang))
            return byLang.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
        return new List<string>();
    }

    public void AddScreenshot(string noteFileName, string bugId, string language, string fileName)
    {
        var map = Load();
        if (!map.TryGetValue(noteFileName, out var byBug))
        {
            byBug = new Dictionary<string, Dictionary<string, List<string>>>();
            map[noteFileName] = byBug;
        }
        if (!byBug.TryGetValue(bugId, out var byLang))
        {
            byLang = new Dictionary<string, List<string>>();
            byBug[bugId] = byLang;
        }
        if (!byLang.TryGetValue(language, out var files))
        {
            files = new List<string>();
            byLang[language] = files;
        }
        if (!files.Contains(fileName))
            files.Add(fileName);
        Save();
    }
}
