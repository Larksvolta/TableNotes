using Microsoft.UI.Xaml;

namespace TableNotes;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.Activate();
    }
}
