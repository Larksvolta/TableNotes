using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TableNotes.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public NotePageViewModel ChecklistVm { get; }
    public NotePageViewModel BugTrackerVm { get; }
    public NotePageViewModel ChangelogVm { get; }
    public NotePageViewModel TextFilesVm { get; }
    public TeamLeadViewModel TeamLeadVm { get; }

    public MainViewModel()
    {
        ChecklistVm = new NotePageViewModel("Checklist");
        BugTrackerVm = new NotePageViewModel("BugTracker");
        ChangelogVm = new NotePageViewModel("Changelog");
        TextFilesVm = new NotePageViewModel("TextFiles");
        TeamLeadVm = new TeamLeadViewModel();
    }

    [RelayCommand]
    private async Task LoadAll()
    {
        await ChecklistVm.LoadNotesAsync();
        await BugTrackerVm.LoadNotesAsync();
        await ChangelogVm.LoadNotesAsync();
        await TextFilesVm.LoadNotesAsync();
        TeamLeadVm.Load();
    }
}
