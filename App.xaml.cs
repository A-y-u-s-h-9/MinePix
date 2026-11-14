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
        // log any unhandled exceptions to the settings folder — silent, local-only, keeps the island alive
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "crash.log"),
                    $"{DateTime.Now:o} {args.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
            args.Handled = true;
        };

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
