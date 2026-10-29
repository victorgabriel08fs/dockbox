using System.IO;

namespace Dockbox.Core;

/// <summary>Como a pilha de uma pasta aparece: automático (leque com poucos itens, senão grade), leque, grade ou lista.</summary>
public enum StackView { Auto, Fan, Grid, List }

/// <summary>Ordem dos itens na pilha.</summary>
public enum StackSort { Name, DateAdded, DateModified, Kind }

/// <summary>Um item dentro da pasta.</summary>
internal sealed record StackItem(string Path, string Name, bool IsFolder, DateTime Added, DateTime Modified, string Kind);

/// <summary>Conteúdo das pilhas de pastas: listar, ordenar e escolher o jeito de mostrar. Testável sem a interface.</summary>
internal static class StackContent
{
    /// <summary>No leque cabem poucos itens; com mais, o automático usa a grade.</summary>
    public const int FanMax = 10;
    /// <summary>Limite de itens mostrados de uma vez; o resto fica para o Explorador.</summary>
    public const int MaxItems = 400;

    /// <summary>
    /// Itens da pasta, sem ocultos, de sistema e atalhos internos (desktop.ini, thumbs.db). Pasta que não existe
    /// ou sem permissão devolve lista vazia.
    /// </summary>
    public static List<StackItem> List(string folder)
    {
        var result = new List<StackItem>();
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return result;
            foreach (var info in dir.EnumerateFileSystemInfos())
            {
                try
                {
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (info.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || info.Name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
                    bool isFolder = info is DirectoryInfo;
                    string name = isFolder || info.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || info.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                        ? System.IO.Path.GetFileNameWithoutExtension(info.Name) is { Length: > 0 } n && !isFolder ? n : info.Name
                        : info.Name;
                    result.Add(new StackItem(info.FullName, name, isFolder, info.CreationTime, info.LastWriteTime,
                        isFolder ? "" : info.Extension.ToLowerInvariant()));
                }
                catch { /* item sumiu ou sem permissão: pula */ }
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Ordena: nome em ordem alfabética; datas da mais nova para a mais velha; tipo agrupa pastas primeiro e
    /// depois por extensão. Empates ficam em ordem de nome.
    /// </summary>
    public static List<StackItem> Sort(IEnumerable<StackItem> items, StackSort sort)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            StackSort.DateAdded => items.OrderByDescending(i => i.Added).ThenBy(i => i.Name, byName).ToList(),
            StackSort.DateModified => items.OrderByDescending(i => i.Modified).ThenBy(i => i.Name, byName).ToList(),
            StackSort.Kind => items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Name, byName).ToList(),
            _ => items.OrderBy(i => i.Name, byName).ToList(),
        };
    }

    /// <summary>
    /// Jeito de mostrar de fato: o leque só existe com o Dock embaixo e com até <see cref="FanMax"/> itens;
    /// fora disso vira grade. Dentro de uma subpasta, o leque também vira grade (para poder voltar).
    /// </summary>
    public static StackView Resolve(StackView view, int count, DockEdge edge, bool inSubfolder)
    {
        bool fanFits = edge == DockEdge.Bottom && count <= FanMax && !inSubfolder;
        return view switch
        {
            StackView.Auto => fanFits && count > 0 ? StackView.Fan : StackView.Grid,
            StackView.Fan => fanFits ? StackView.Fan : StackView.Grid,
            _ => view,
        };
    }

    /// <summary>Item mais recente da pasta (pelo dia em que entrou nela), para o ícone no Dock.</summary>
    public static StackItem? Latest(IEnumerable<StackItem> items) =>
        items.OrderByDescending(i => i.Added).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();

    /// <summary>
    /// Ao soltar arquivos na pasta: mover (mesmo disco) ou copiar (outro disco), como no Explorador.
    /// Ctrl força copiar e Shift força mover.
    /// </summary>
    public static bool ShouldMove(string source, string targetFolder, bool ctrl, bool shift)
    {
        if (ctrl) return false;
        if (shift) return true;
        string a = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(source)) ?? "";
        string b = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(targetFolder)) ?? "";
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True se <paramref name="path"/> é a própria pasta ou está dentro dela (não dá para mover uma pasta para dentro de si).</summary>
    public static bool IsInside(string path, string folder)
    {
        string p = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
        string f = System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/');
        return p.Equals(f, StringComparison.OrdinalIgnoreCase)
            || f.StartsWith(p + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
