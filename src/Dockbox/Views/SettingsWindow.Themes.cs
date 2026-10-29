using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Core;
using Dockbox.Services;
using Microsoft.Win32;

namespace Dockbox.Views;

/// <summary>Temas prontos, importar e exportar tema, e backup completo (issue #29).</summary>
public partial class SettingsWindow
{
    /// <summary>Um botão por tema pronto, com uma bolinha da cor de fundo.</summary>
    private void BuildBuiltInThemes()
    {
        BuiltInThemes.Children.Clear();
        foreach (var (key, theme) in ThemeFiles.BuiltIn)
        {
            var dot = new Ellipse
            {
                Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), StrokeThickness = 1,
                Stroke = (Brush)FindResource("UiCardBorder"),
                Fill = SwatchFor(theme),
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = Loc.T(key), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Style = (Style)FindResource("UiButton"),
                Content = content,
                Margin = new Thickness(0, 0, 8, 8),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{Loc.T("S_Themes")}: {Loc.T(key)}");
            var t = theme;
            button.Click += (_, _) =>
            {
                // Tema pronto muda só cores, fundo e cantos; tamanho e ampliação ficam como o usuário deixou.
                t.ApplyTo(A, includeSize: false);
                Commit();
                ApplyTitleBarTheme();
                LoadAll();
            };
            BuiltInThemes.Children.Add(button);
        }
    }

    private static Brush SwatchFor(ThemeData t)
    {
        string? hex = t.Theme switch
        {
            ThemeMode.Light => "#ECECEC",
            ThemeMode.Dark => "#2C2C2E",
            ThemeMode.Custom => t.CustomColor,
            _ => null,
        };
        if (hex == null) return Brushes.Transparent;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Transparent; }
    }

    private void ShowFileError(string message) =>
        MessageBox.Show(this, message, "Dockbox", MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool WriteFile(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception ex)
        {
            ShowFileError(Loc.F("ThemeFile_CantWrite", SettingsStore.Describe(ex)));
            return false;
        }
    }

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("Theme_Filter"),
            DefaultExt = ThemeFiles.ThemeExtension,
            FileName = Loc.T("Theme_DefaultFileName") + ThemeFiles.ThemeExtension,
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        WriteFile(dlg.FileName, ThemeFiles.ExportTheme(A, name));
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Theme_Filter") };
        if (dlg.ShowDialog(this) != true) return;
        ThemeData theme;
        try { theme = ThemeFiles.ParseTheme(ThemeFiles.ReadFile(dlg.FileName)); }
        catch (ThemeFileException ex)
        {
            ShowFileError(ex.Message);
            return;
        }

        // Prévia: aplica no Dock e pergunta se fica. "Não" volta exatamente ao que era.
        var before = ThemeData.From(A, "");
        theme.ApplyTo(A);
        Commit();
        ApplyTitleBarTheme();
        LoadAll();
        string name = theme.Name.Length > 0 ? theme.Name : Loc.T("Theme_Untitled");
        var keep = MessageBox.Show(this, Loc.F("Theme_Keep", name), "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (keep == MessageBoxResult.Yes) return;
        before.ApplyTo(A);
        Commit();
        ApplyTitleBarTheme();
        LoadAll();
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("Backup_Filter"),
            DefaultExt = ThemeFiles.BackupExtension,
            FileName = Loc.F("Backup_DefaultFileName", DateTime.Now.ToString("yyyy-MM-dd")) + ThemeFiles.BackupExtension,
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (_commitDebounce.IsEnabled) { _commitDebounce.Stop(); SettingsStore.Commit(); }
        if (WriteFile(dlg.FileName, ThemeFiles.ExportBackup(S, CustomIcons.Folder)))
            MessageBox.Show(this, Loc.F("Backup_Exported", dlg.FileName), "Dockbox", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Backup_Filter") };
        if (dlg.ShowDialog(this) != true) return;
        ThemeFiles.Backup backup;
        try { backup = ThemeFiles.ParseBackup(ThemeFiles.ReadFile(dlg.FileName)); }
        catch (ThemeFileException ex)
        {
            ShowFileError(ex.Message);
            return;
        }

        var r = MessageBox.Show(this, Loc.F("Backup_Confirm", backup.Settings.Items.Count, backup.Settings.Widgets.Count),
            "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        // Ícones primeiro: se não der para gravá-los, nada muda.
        try { ThemeFiles.WriteIcons(backup, CustomIcons.Folder); }
        catch (Exception ex)
        {
            ShowFileError(Loc.F("ThemeFile_CantWrite", SettingsStore.Describe(ex)));
            return;
        }
        _commitDebounce.Stop();
        bool languageChanges = backup.Settings.Language != S.Language;
        ThemeFiles.ApplyBackup(S, backup);
        Commit();
        // Com idioma novo, a janela reabre sozinha com os textos novos (e isso já mostra que deu certo).
        if (languageChanges) return;
        ApplyTitleBarTheme();
        LoadAll();
        MessageBox.Show(this, Loc.T("Backup_Done"), "Dockbox", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
