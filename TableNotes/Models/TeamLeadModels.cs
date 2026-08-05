namespace TableNotes.Models;

public class TeamLeadProject
{
    public string Name { get; set; } = string.Empty;
    public string Lead { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class TeamLeadUser
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}