using Dockbox.Core;
using System.Windows;
using Dockbox.Views;
using Forms = System.Windows.Forms;

namespace Dockbox.Services;

/// <summary>
/// Ícone do Dockbox na área de notificação, com atalhos para Gerenciar Dock e Sair.
/// Útil quando o Dock está escondido ou fora da tela.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon()
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Dockbox",
            ContextMenuStrip = new Forms.ContextMenuStrip(),
            Visible = true,
        };
        BuildMenu();
        Loc.Changed += BuildMenu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenSettings();
        };
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(Loc.T("Menu_ManageDock"), null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Menu_Quit"), null, (_, _) => Quit());
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Dockbox.ico"));
        using var stream = info.Stream;
        // Pede o tamanho certo para a bandeja (16 px em 100%, 24 px em 150%...).
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    private static void OpenSettings() =>
        Application.Current.Dispatcher.BeginInvoke(SettingsWindow.ShowSingle);

    private static void Quit() =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (Application.Current.MainWindow is Window w) w.Close();
            else Application.Current.Shutdown();
        });

    public void Dispose()
    {
        Loc.Changed -= BuildMenu;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
