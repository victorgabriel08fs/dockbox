namespace Dockbox.Core;

/// <summary>
/// Guarda os últimos itens removidos do Dock para poder desfazer (no aviso do Dock ou com
/// Ctrl+Z no Gerenciar Dock). Só em memória: fechar o Dockbox esquece o histórico.
/// </summary>
public static class RemovalHistory
{
    private const int Limit = 10;
    private static readonly List<(DockEntry Entry, int Index)> Stack = new();

    /// <summary>Disparado depois de registrar uma remoção (o Dock mostra o aviso "Desfazer").</summary>
    public static event Action<DockEntry>? Recorded;

    public static bool CanUndo => Stack.Count > 0;
    public static DockEntry? Last => Stack.Count > 0 ? Stack[^1].Entry : null;

    /// <summary>Remove o item da lista e lembra a posição. Devolve false se ele não estava lá.</summary>
    public static bool Remove(List<DockEntry> items, DockEntry entry)
    {
        int index = items.IndexOf(entry);
        if (index < 0) return false;
        items.RemoveAt(index);
        Stack.Add((entry, index));
        if (Stack.Count > Limit) Stack.RemoveAt(0);
        Recorded?.Invoke(entry);
        return true;
    }

    /// <summary>Devolve o último item removido à mesma posição. Quem chama salva (Commit).</summary>
    public static DockEntry? Undo(List<DockEntry> items)
    {
        if (Stack.Count == 0) return null;
        var (entry, index) = Stack[^1];
        Stack.RemoveAt(Stack.Count - 1);
        if (!items.Contains(entry)) items.Insert(Math.Clamp(index, 0, items.Count), entry);
        return entry;
    }

    public static void Clear() => Stack.Clear();
}
