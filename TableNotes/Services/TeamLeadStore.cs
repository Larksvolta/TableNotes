using System.Text.Json;
using TableNotes.Models;

namespace TableNotes.Services;

internal static class TeamLeadStore
{
    private static string Dir => Path.Combine(DataPaths.BasePath, "TeamLead");
    private static string ProjectFile => Path.Combine(Dir, "project.json");
    private static string UsersFile => Path.Combine(Dir, "users.json");

    public static TeamLeadProject LoadProject()
    {
        try
        {
            if (File.Exists(ProjectFile))
                return JsonSerializer.Deserialize<TeamLeadProject>(File.ReadAllText(ProjectFile)) ?? new TeamLeadProject();
        }
        catch { }
        return new TeamLeadProject();
    }

    public static List<TeamLeadUser> LoadUsers()
    {
        try
        {
            if (File.Exists(UsersFile))
                return JsonSerializer.Deserialize<List<TeamLeadUser>>(File.ReadAllText(UsersFile)) ?? new List<TeamLeadUser>();
        }
        catch { }
        return new List<TeamLeadUser>();
    }

    public static void SaveProject(TeamLeadProject project)
    {
        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            File.WriteAllText(ProjectFile, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void SaveUsers(List<TeamLeadUser> users)
    {
        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            File.WriteAllText(UsersFile, JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}