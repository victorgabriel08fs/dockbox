namespace Dockbox.Core;

/// <summary>
/// Contadores nos ícones. O Windows não deixa outro programa ler o contador e o progresso que os apps
/// mandam para a barra de tarefas; o que dá para ler é o título das janelas, onde muitos apps e sites
/// põem o número de não lidas no começo: "(3) WhatsApp", "(12) Discord", "(1+) Algo".
/// Sem chamadas ao Windows, para poder testar.
/// </summary>
internal static class Badges
{
    /// <summary>Número no começo do título no formato "(N)" ou "(N+)", ou null.</summary>
    public static int? ParseCount(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var s = title.AsSpan().TrimStart();
        if (s.Length < 3 || s[0] != '(') return null;
        int close = s.IndexOf(')');
        if (close < 2) return null;
        var inner = s[1..close];
        if (inner.Length > 0 && inner[^1] == '+') inner = inner[..^1];
        if (inner.Length is 0 or > 6) return null;
        foreach (char c in inner) if (!char.IsAsciiDigit(c)) return null;
        // Depois do ")" vem o nome: "(3) WhatsApp". "(2)" sozinho ou "(2).txt" não contam.
        var rest = s[(close + 1)..];
        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0])) return null;
        int n = int.Parse(inner);
        return n > 0 ? n : null;
    }

    /// <summary>Maior contador entre os títulos das janelas de um app, ou null.</summary>
    public static int? CountFor(IEnumerable<string?> titles)
    {
        int? best = null;
        foreach (var t in titles)
            if (ParseCount(t) is int n && (best == null || n > best)) best = n;
        return best;
    }

    /// <summary>Texto do badge: até 99, depois "99+".</summary>
    public static string Format(int count) => count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Janelas que piscaram pedindo atenção (FlashWindowEx) e ainda não foram vistas.
/// Uma janela sai quando ela, ou outra janela do mesmo app, vai para o primeiro plano, ou quando fecha.
/// </summary>
internal sealed class AttentionTracker
{
    private readonly HashSet<IntPtr> _windows = new();

    public int Count => _windows.Count;
    public bool Contains(IntPtr hwnd) => _windows.Contains(hwnd);

    /// <summary>A janela piscou. Ignorada se já está em primeiro plano (o usuário já está nela).</summary>
    public bool Flash(IntPtr hwnd, IntPtr foreground)
    {
        if (hwnd == IntPtr.Zero || hwnd == foreground) return false;
        return _windows.Add(hwnd);
    }

    public void Remove(IntPtr hwnd) => _windows.Remove(hwnd);

    /// <summary>
    /// Atualiza com as janelas abertas agora (<paramref name="keysOf"/>: janela → chaves do app, como o executável
    /// e o AppUserModelID) e a janela em primeiro plano: tira as que fecharam e as do app que está em primeiro
    /// plano. Devolve as chaves dos apps que pedem atenção.
    /// </summary>
    public HashSet<string> Update(IReadOnlyDictionary<IntPtr, string[]> keysOf, IntPtr foreground)
    {
        var fgKeys = keysOf.TryGetValue(foreground, out var f) ? f : Array.Empty<string>();
        _windows.RemoveWhere(h => !keysOf.TryGetValue(h, out var k) || k.Any(fgKeys.Contains));
        var keys = new HashSet<string>();
        foreach (var h in _windows) keys.UnionWith(keysOf[h]);
        return keys;
    }
}
