namespace Dockbox.Core;

public enum ShortcutAction { Focus, ToggleDock, Search, OpenItem }

/// <summary>Um atalho global a registrar no Windows.</summary>
public sealed record PlannedShortcut(int Id, ShortcutAction Action, int Item, string Text, uint Mods, uint Vk);

/// <summary>
/// Quais atalhos globais registrar a partir das configurações. Atalho repetido entre duas funções fica só na
/// primeira (na ordem: foco, mostrar/ocultar, busca, itens) e o repetido vai para <see cref="Duplicates"/>.
/// </summary>
public sealed class ShortcutPlan
{
    public const int FocusId = 0xD0C, ToggleId = 0xD0D, SearchId = 0xD0E, FirstItemId = 0xD11;

    public IReadOnlyList<PlannedShortcut> Shortcuts { get; }
    public IReadOnlyList<string> Duplicates { get; }

    /// <summary>Texto que muda quando qualquer atalho muda (para não registrar de novo à toa).</summary>
    public string Signature => string.Join("|", Shortcuts.Select(s => $"{s.Id}:{s.Mods}:{s.Vk}"));

    private ShortcutPlan(List<PlannedShortcut> shortcuts, List<string> duplicates)
    {
        Shortcuts = shortcuts;
        Duplicates = duplicates;
    }

    public static ShortcutPlan Build(Appearance a)
    {
        var list = new List<PlannedShortcut>();
        var dups = new List<string>();
        void Add(int id, ShortcutAction action, int item, string? text)
        {
            if (Hotkey.Parse(text) is not (uint mods, uint vk)) return;
            if (list.Any(s => s.Mods == mods && s.Vk == vk))
            {
                dups.Add(text!);
                return;
            }
            list.Add(new PlannedShortcut(id, action, item, text!, mods, vk));
        }

        Add(FocusId, ShortcutAction.Focus, 0, a.KeyboardShortcut);
        Add(ToggleId, ShortcutAction.ToggleDock, 0, a.ToggleShortcut);
        Add(SearchId, ShortcutAction.Search, 0, a.SearchShortcut);
        for (int n = 1; n <= 9; n++)
            Add(FirstItemId + n - 1, ShortcutAction.OpenItem, n, Hotkey.ForItem(a.ItemShortcutModifiers, n));
        return new ShortcutPlan(list, dups);
    }
}
