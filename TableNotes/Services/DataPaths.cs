namespace TableNotes.Services;

internal static class DataPaths
{
    public static string BasePath => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
        "Data");
}
