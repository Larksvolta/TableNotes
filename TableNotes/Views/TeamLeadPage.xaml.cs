using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableNotes.ViewModels;

namespace TableNotes.Views;

public sealed partial class TeamLeadPage : UserControl
{
    public TeamLeadPage()
    {
        InitializeComponent();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ProjectForm.Visibility = tag == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        UsersForm.Visibility = tag == "Users" ? Visibility.Visible : Visibility.Collapsed;
    }
}