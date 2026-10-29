using System.Text.RegularExpressions;

namespace Dockbox.Core;

public enum LaunchRisk
{
    /// <summary>App, atalho, pasta, arquivo comum ou link da web: abre direto.</summary>
    Safe,
    /// <summary>Script, protocolo desconhecido ou interpretador com argumentos: pergunta uma vez.</summary>
    Confirm,
    /// <summary>Protocolo usado em ataques conhecidos: nunca executa.</summary>
    Blocked,
}

/// <summary>
/// Decide se um item do Dock pode abrir sem perguntar. Não acessa disco: quem chama resolve
/// atalhos (.lnk) e lê arquivos .url antes, e passa o resultado.
/// </summary>
public static class LaunchPolicy
{
    private static readonly Regex Scheme = new(@"^(?<s>[a-zA-Z][a-zA-Z0-9+.\-]+):", RegexOptions.Compiled);

    /// <summary>Protocolos que abrem sem perguntar.</summary>
    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "shell", "ms-settings",
    };

    /// <summary>Protocolos explorados em ataques (Follina, search-ms, Office URI...). Nunca abrem.</summary>
    private static readonly HashSet<string> BlockedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ms-msdt", "search-ms", "search", "ms-officecmd", "ms-cxh", "ms-cxh-full", "ms-appinstaller",
        "ms-word", "ms-excel", "ms-powerpoint", "ms-access", "ms-visio", "ms-project", "ms-publisher",
        "ms-spd", "ms-infopath", "javascript", "vbscript", "jscript", "its", "ms-its", "mk", "hcp",
    };

    /// <summary>Extensões que executam código sem ser um programa instalado.</summary>
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".scr", ".com", ".pif", ".reg", ".cpl", ".msc", ".msi", ".msp", ".jar", ".application", ".inf",
        ".chm", ".settingcontent-ms", ".library-ms", ".searchconnector-ms",
    };

    /// <summary>Programas que rodam o que vier nos argumentos.</summary>
    private static readonly HashSet<string> Interpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "msiexec.exe", "bash.exe", "wsl.exe", "certutil.exe",
        "bitsadmin.exe", "msbuild.exe", "installutil.exe", "forfiles.exe", "conhost.exe",
    };

    /// <summary>
    /// Classifica um item.
    /// </summary>
    /// <param name="path">Caminho já com %VARIAVEIS% expandidas.</param>
    /// <param name="arguments">Argumentos do item, se houver.</param>
    /// <param name="linkTarget">Destino do atalho, quando <paramref name="path"/> é um .lnk.</param>
    /// <param name="urlFileTarget">URL de dentro do arquivo, quando <paramref name="path"/> é um .url.</param>
    public static LaunchRisk Classify(string path, string? arguments = null, string? linkTarget = null, string? urlFileTarget = null)
    {
        path = (path ?? "").Trim().Trim('"');
        if (path.Length == 0) return LaunchRisk.Confirm;

        if (SchemeOf(path) is string scheme)
        {
            if (BlockedSchemes.Contains(scheme)) return LaunchRisk.Blocked;
            return SafeSchemes.Contains(scheme) ? LaunchRisk.Safe : LaunchRisk.Confirm;
        }

        string ext = Extension(path);
        if (ext == ".url")
        {
            // Só links da web abrem direto; um .url apontando para arquivo ou protocolo estranho pergunta.
            if (string.IsNullOrWhiteSpace(urlFileTarget) || SchemeOf(urlFileTarget.Trim()) == null) return LaunchRisk.Confirm;
            return Classify(urlFileTarget);
        }

        if (ext == ".lnk" && !string.IsNullOrWhiteSpace(linkTarget))
            return Classify(linkTarget, arguments);

        if (ScriptExtensions.Contains(ext)) return LaunchRisk.Confirm;

        if (!string.IsNullOrWhiteSpace(arguments) && Interpreters.Contains(FileName(path)))
            return LaunchRisk.Confirm;

        return LaunchRisk.Safe;
    }

    /// <summary>Lê a linha URL= de um arquivo .url (formato INI).</summary>
    public static string? ParseUrlFile(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line[4..].Trim();
        }
        return null;
    }

    private static string? SchemeOf(string path)
    {
        var m = Scheme.Match(path);
        // "C:\..." tem uma letra antes dos dois-pontos: é unidade, não protocolo (a regex exige 2+).
        return m.Success ? m.Groups["s"].Value : null;
    }

    private static string FileName(string path)
    {
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 ? path[(i + 1)..] : path;
    }

    private static string Extension(string path)
    {
        string name = FileName(path);
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name[dot..] : "";
    }
}
