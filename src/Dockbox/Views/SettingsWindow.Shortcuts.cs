using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dockbox.Core;

namespace Dockbox.Views;

/// <summary>Editores dos atalhos globais: mostrar/ocultar o Dock, busca e itens 1 a 9 (issue #31).</summary>
public partial class SettingsWindow
{
    private void BuildShortcutEditors()
    {
        ShortcutEditors.Children.Clear();
        ShortcutEditors.Children.Add(ShortcutEditor(Loc.T("S_ShortcutToggle"), Loc.T("S_ShortcutToggleHint"),
            () => A.ToggleShortcut, v => A.ToggleShortcut = v));
        ShortcutEditors.Children.Add(ShortcutEditor(Loc.T("S_ShortcutSearch"), Loc.T("S_ShortcutSearchHint"),
            () => A.SearchShortcut, v => A.SearchShortcut = v));
    }

    /// <summary>
    /// Uma linha com o nome, uma caixa que grava o atalho apertado e "Desligar". Na caixa: aperte a combinação;
    /// Backspace ou Delete desliga; Esc desiste.
    /// </summary>
    private FrameworkElement ShortcutEditor(string title, string hint, Func<string> get, Action<string> set)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("UiSubFg"), Margin = new Thickness(0, 0, 0, 6) });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new TextBox
        {
            Style = (Style)FindResource("UiTextBox"),
            Width = 220,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Text = Hotkey.Display(get()),
            Cursor = Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(box, Loc.T("S_ShortcutRecordHint"));
        var note = new TextBlock { Style = (Style)FindResource("UiSub"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Text = hint };

        void Show() => box.Text = Hotkey.Display(get());
        void Save(string value)
        {
            set(value);
            Commit();
            Show();
            note.Text = hint;
            UpdateShortcutHint();
        }

        box.GotKeyboardFocus += (_, _) =>
        {
            box.Text = Loc.T("S_ShortcutPress");
            note.Text = Loc.T("S_ShortcutRecordHint");
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            Show();
            note.Text = hint;
        };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab) return; // deixa o Tab andar entre os controles
            e.Handled = true;
            switch (key)
            {
                case Key.Escape:
                    Keyboard.ClearFocus();
                    return;
                case Key.Back:
                case Key.Delete:
                    Save("");
                    Keyboard.ClearFocus();
                    return;
                case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                    or Key.LWin or Key.RWin:
                    box.Text = Partial(Keyboard.Modifiers) + "…";
                    return;
            }
            uint mods = ModsOf(Keyboard.Modifiers);
            string? text = Hotkey.Format(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (text == null)
            {
                note.Text = Loc.T("S_ShortcutInvalid");
                box.Text = Loc.T("S_ShortcutPress");
                return;
            }
            Save(text);
            Keyboard.ClearFocus();
        };

        var off = new Button { Style = (Style)FindResource("UiButton"), Content = Loc.T("S_ShortcutOff"), Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(off, $"{Loc.T("S_ShortcutOff")}: {title}");
        off.Click += (_, _) => Save("");

        row.Children.Add(box);
        row.Children.Add(off);
        panel.Children.Add(row);
        panel.Children.Add(note);
        return panel;
    }

    private static uint ModsOf(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Control) ? Hotkey.ModControl : 0) |
        (m.HasFlag(ModifierKeys.Alt) ? Hotkey.ModAlt : 0) |
        (m.HasFlag(ModifierKeys.Shift) ? Hotkey.ModShift : 0) |
        (m.HasFlag(ModifierKeys.Windows) ? Hotkey.ModWin : 0);

    private static string Partial(ModifierKeys m)
    {
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        return string.Join("+", parts) + (parts.Count > 0 ? "+" : "");
    }

    private void BuildItemShortcutChoices()
    {
        ItemShortcutChoices.Children.Clear();
        var style = (Style)FindResource("UiSegment");
        foreach (var choice in Hotkey.ItemModifierChoices)
        {
            string label = choice.Length == 0 ? Loc.T("Common_Off") : $"{choice}+1…9";
            var rb = new RadioButton
            {
                Style = style,
                GroupName = "itemShortcut",
                Content = label,
                IsChecked = A.ItemShortcutModifiers == choice,
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, $"{Loc.T("S_ItemShortcuts")}: {label}");
            rb.Checked += (_, _) =>
            {
                if (_loading || A.ItemShortcutModifiers == choice) return;
                A.ItemShortcutModifiers = choice;
                Commit();
            };
            ItemShortcutChoices.Children.Add(rb);
        }
    }
}
