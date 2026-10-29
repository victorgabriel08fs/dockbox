using System.Windows;

namespace Dockbox;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Só uma instância do Dock por vez.
        _singleInstance = new Mutex(true, @"Local\Dockbox.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
