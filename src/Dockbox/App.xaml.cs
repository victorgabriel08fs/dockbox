using System.Windows;
using System.Windows.Media;
using Dockbox.Core;

namespace Dockbox;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Reaberto de outra pasta (ao ligar o início automático): espera a instância antiga fechar.
        int afterIdx = Array.IndexOf(e.Args, "--after-pid");
        if (afterIdx >= 0 && afterIdx + 1 < e.Args.Length && int.TryParse(e.Args[afterIdx + 1], out int oldPid))
        {
            try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(8000); }
            catch { /* já fechou */ }
        }

        // Só uma instância do Dock por vez.
        _singleInstance = new Mutex(true, @"Local\Dockbox.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        SettingsStore.SaveFailed += message => Dispatcher.BeginInvoke(() => ShowSaveError(message));
        SettingsStore.Load();
        Services.Autostart.RepairIfMoved();
        DispatcherUnhandledException += (_, args) =>
        {
            // Não deixa um erro inesperado fechar o Dock; registra para diagnóstico.
            ErrorLog.Write("Erro inesperado", args.Exception);
            args.Handled = true;
        };
        base.OnStartup(e);

        if (SettingsStore.LoadWarning is string warning)
            Dispatcher.BeginInvoke(() => MessageBox.Show(warning, "Dockbox", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    /// <summary>Avisa que as configurações não foram salvas e oferece tentar de novo.</summary>
    private static void ShowSaveError(string message)
    {
        var answer = MessageBox.Show(
            message + "\n\nSuas mudanças continuam valendo enquanto o Dockbox estiver aberto, mas se perdem ao fechar.\n\n" +
            "Corrija o problema e clique em Sim para tentar salvar de novo.",
            "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        if (!SettingsStore.Save())
            MessageBox.Show("Ainda não foi possível salvar. Dá para tentar de novo pelo Gerenciar Dock.",
                "Dockbox", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Última tentativa de não perder mudanças que falharam ao salvar.
        if (SettingsStore.LastSaveError != null) SettingsStore.Save();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Atualiza as cores dos menus e da janela de configurações.</summary>
    public static void ApplyUiTheme(DockTheme theme)
    {
        var r = Current.Resources;
        bool light = theme.IsLight;
        void Set(string key, Color c) => r[key] = ColorUtil.Brush(c);

        Set("UiWindowBg", light ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x20, 0x20, 0x24));
        Set("UiSidebarBg", light ? Color.FromRgb(0xEA, 0xEA, 0xEE) : Color.FromRgb(0x18, 0x18, 0x1B));
        Set("UiCardBg", light ? Colors.White : Color.FromRgb(0x2A, 0x2A, 0x2F));
        Set("UiCardBorder", light ? Color.FromRgb(0xDD, 0xDD, 0xE2) : Color.FromRgb(0x3A, 0x3A, 0x40));
        Set("UiFg", light ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF2, 0xF2, 0xF5));
        Set("UiSubFg", light ? Color.FromRgb(0x6E, 0x6E, 0x76) : Color.FromRgb(0xA0, 0xA0, 0xA8));
        Set("UiAccent", theme.Accent);
        Set("UiAccentFg", ColorUtil.Luminance(theme.Accent) > 0.7 ? Colors.Black : Colors.White);
        Set("UiHover", light ? Color.FromArgb(0x12, 0, 0, 0) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        Set("UiPressed", light ? Color.FromArgb(0x20, 0, 0, 0) : Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        Set("UiInputBg", light ? Color.FromRgb(0xF0, 0xF0, 0xF3) : Color.FromRgb(0x1C, 0x1C, 0x20));
        Set("UiMenuBg", light ? Color.FromArgb(0xF5, 0xF8, 0xF8, 0xFA) : Color.FromArgb(0xF2, 0x2C, 0x2C, 0x31));
        Set("UiMenuBorder", light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    }
}
