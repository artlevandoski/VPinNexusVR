using System.Windows;

namespace VRPLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Optional: --settings <path> to use a settings file other than the one beside the exe.
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var i = Array.FindIndex(e.Args, a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < e.Args.Length)
            settingsPath = Path.GetFullPath(e.Args[i + 1]);

        var window = new MainWindow(settingsPath);
        MainWindow = window;
        window.Show();
    }
}
