using System.IO;

namespace Dockbox.Core;

/// <summary>Normaliza caminhos para comparar itens do Dock (evitar duplicados).</summary>
public static class ItemIdentity
{
    /// <summary>
    /// Expande %VARIAVEIS%, tira aspas e barra final, resolve "..\" e ignora maiúsculas.
    /// Protocolos (https:, shell:...) só são comparados sem diferenciar maiúsculas.
    /// </summary>
    public static string Normalize(string? path)
    {
        string p = Environment.ExpandEnvironmentVariables(path ?? "").Trim().Trim('"');
        bool isUri = p.Length > 2 && p.IndexOf(':') > 1;
        if (!isUri && p.Length > 0)
        {
            try { p = Path.GetFullPath(p); }
            catch { /* caminho inválido: compara como está */ }
            if (p.Length > 3) p = p.TrimEnd('\\', '/');
        }
        return p.ToLowerInvariant();
    }

    /// <summary>Chave de comparação: caminho normalizado mais argumentos (o mesmo exe com argumentos diferentes é outro item).</summary>
    public static string Key(string? path, string? arguments = null)
    {
        string args = (arguments ?? "").Trim();
        return args.Length == 0 ? Normalize(path) : Normalize(path) + "|" + args.ToLowerInvariant();
    }
}
