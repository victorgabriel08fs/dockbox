namespace Dockbox.Core;

/// <summary>Últimos textos copiados, mais novo primeiro. Só na memória: nada vai para o disco.</summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 10;
    /// <summary>Textos maiores que isso não entram (provavelmente um arquivo inteiro colado).</summary>
    public const int MaxLength = 100_000;

    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;

    /// <summary>Guarda o texto. Repetido sobe para o topo. False se foi ignorado (vazio ou grande demais).</summary>
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;
        _items.Remove(text);
        _items.Insert(0, text);
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        return true;
    }

    public void Clear() => _items.Clear();

    /// <summary>Uma linha curta para a lista: espaços e quebras viram um espaço só, cortado com "…".</summary>
    public static string Preview(string text, int max = 60)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string line = string.Join(' ', parts);
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }
}
