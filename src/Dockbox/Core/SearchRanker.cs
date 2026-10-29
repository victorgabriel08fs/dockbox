using System.Globalization;
using System.Text;

namespace Dockbox.Core;

public enum SearchKind { Dock, App, File }

/// <summary>Algo que a busca pode abrir.</summary>
public sealed record SearchItem(string Name, string Path, SearchKind Kind, string? Arguments = null);

/// <summary>Ordena os resultados da busca (estilo Spotlight), sem acentos e sem diferença de maiúsculas.</summary>
public static class SearchRanker
{
    public const int MaxResults = 8;

    /// <summary>Pontos de quanto <paramref name="name"/> combina com a busca; 0 = não combina.</summary>
    public static int Score(string query, string name)
    {
        string q = Normalize(query), n = Normalize(name);
        if (q.Length == 0 || n.Length == 0) return 0;
        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 800;

        var words = n.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 600;
        string initials = string.Concat(words.Select(w => w[0]));
        if (q.Length >= 2 && initials.StartsWith(q, StringComparison.Ordinal)) return 500;
        if (n.Contains(q, StringComparison.Ordinal)) return 400;
        if (q.Length >= 3 && IsSubsequence(q, n)) return 100;
        return 0;
    }

    /// <summary>Os melhores resultados: mais pontos, depois Dock, apps e arquivos, depois nomes mais curtos.</summary>
    public static List<SearchItem> Rank(string query, IEnumerable<SearchItem> items, int max = MaxResults)
    {
        return items
            .Select(i => (Item: i, Score: Score(query, i.Name)))
            .Where(x => x.Score > 0)
            .GroupBy(x => (Normalize(x.Item.Name), x.Item.Path.ToLowerInvariant()))
            .Select(g => g.OrderBy(x => x.Item.Kind).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Kind)
            .ThenBy(x => x.Item.Name.Length)
            .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>Minúsculas e sem acentos ("Configurações" → "configuracoes").</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var d = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsSubsequence(string q, string n)
    {
        int i = 0;
        foreach (char c in n)
            if (i < q.Length && c == q[i]) i++;
        return i == q.Length;
    }
}
