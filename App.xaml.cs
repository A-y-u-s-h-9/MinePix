using System.Windows;

namespace MinePix;

/// <summary>
/// Entry point. Enforces a single instance, then shows the island.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // one MinePix at a time — a second launch just exits
        _singleInstanceMutex = new Mutex(true, "MinePix-SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        window.Show();
    }
}
