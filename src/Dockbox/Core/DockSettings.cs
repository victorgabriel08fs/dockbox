using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dockbox.Core;

public enum DockEdge { Bottom, Top, Left, Right }
public enum ThemeMode { Auto, Light, Dark, Custom }

public sealed class DockEntry
{
    public string Name { get; set; } = "";
    /// <summary>Caminho de um .exe, atalho .lnk, pasta ou arquivo. Aceita %VARIAVEIS%.</summary>
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    /// <summary>O usuário já confirmou que este item (script, protocolo...) pode abrir. Zera ao mudar caminho ou argumentos.</summary>
    public bool Trusted { get; set; }
}

public sealed class WidgetConfig
{
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Appearance
{
    public double IconSize { get; set; } = 54;
    public bool Magnify { get; set; } = true;
    public double Magnification { get; set; } = 1.8;
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    /// <summary>Opacidade do fundo, 0 a 1.</summary>
    public double Opacity { get; set; } = 0.72;
    /// <summary>Cor do fundo no tema personalizado (#RRGGBB).</summary>
    public string CustomColor { get; set; } = "#2A2D3A";
    /// <summary>Cor de destaque: indicador de app aberto, anéis dos widgets (#RRGGBB).</summary>
    public string AccentColor { get; set; } = "#0A84FF";
    public double CornerRadius { get; set; } = 18;
    public bool ShowLabels { get; set; } = true;
    public bool ShowRunning { get; set; } = true;
    public bool BounceOnLaunch { get; set; } = true;
    public bool ShowSeparator { get; set; } = true;
    /// <summary>Reserva a faixa do Dock na tela para as janelas maximizadas pararem antes dele.</summary>
    public bool ReserveSpace { get; set; } = true;
    /// <summary>Mostra apps abertos que não estão fixados, depois dos fixados.</summary>
    public bool ShowRunningUnpinned { get; set; } = true;
    /// <summary>Esconde o Dock fora da tela até o mouse encostar na borda.</summary>
    public bool AutoHide { get; set; }
    /// <summary>Segundos com o mouse na borda até o Dock aparecer.</summary>
    public double AutoHideDelay { get; set; } = 0.3;
}

public sealed class DockSettings
{
    public List<DockEntry> Items { get; set; } = new();
    public List<WidgetConfig> Widgets { get; set; } = new();
    public Appearance Appearance { get; set; } = new();
    /// <summary>Cidade do widget de clima; vazio = detectar pela internet.</summary>
    public string WeatherCity { get; set; } = "";
    public bool Clock24h { get; set; } = true;
    /// <summary>Com a cidade vazia, descobrir a localização pelo IP (ipwho.is). Desligado por padrão.</summary>
    public bool WeatherAutoLocate { get; set; }
}

/// <summary>Lê e grava %APPDATA%\Dockbox\settings.json e avisa quem estiver ouvindo.</summary>
public static class SettingsStore
{
    public static string FolderPath { get; private set; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockbox");

    public static string FilePath => System.IO.Path.Combine(FolderPath, "settings.json");

    /// <summary>Usa outra pasta (para testes; não toca no %APPDATA% real).</summary>
    public static void UseFolder(string folder) => FolderPath = folder;
    private static string LegacyPinnedPath => System.IO.Path.Combine(FolderPath, "pinned.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static DockSettings Current { get; private set; } = new();

    /// <summary>Aviso para mostrar ao usuário depois de carregar (ex.: arquivo corrompido).</summary>
    public static string? LoadWarning { get; private set; }

    /// <summary>Disparado (na thread da UI) sempre que as configurações mudam.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        DockSettings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch
        {
            // Arquivo inválido: guarda uma cópia com data (nunca sobrescrita) antes de recomeçar.
            string copy = FilePath + $".corrompido-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(FilePath, copy, false);
                LoadWarning = $"O arquivo de configurações estava com erro e o Dockbox voltou aos padrões.\n\nUma cópia do arquivo antigo foi guardada em:\n{copy}";
            }
            catch
            {
                LoadWarning = "O arquivo de configurações estava com erro e o Dockbox voltou aos padrões.";
            }
        }

        if (s == null)
        {
            s = new DockSettings { Items = LoadLegacyPinned() ?? DefaultItems() };
        }

        Normalize(s);
        Current = s;
        Save();
    }

    /// <summary>Motivo da última falha ao salvar (com o caminho do arquivo), ou null se salvou.</summary>
    public static string? LastSaveError { get; private set; }

    /// <summary>Disparado quando salvar falha depois das novas tentativas. Recebe a mensagem para o usuário.</summary>
    public static event Action<string>? SaveFailed;

    /// <summary>Disparado quando volta a salvar depois de uma falha.</summary>
    public static event Action? SaveRecovered;

    /// <summary>Quantas vezes tenta gravar antes de desistir (o arquivo pode estar travado por um instante).</summary>
    private const int SaveAttempts = 3;

    /// <summary>
    /// Grava o settings.json. Se falhar (somente leitura, disco cheio, OneDrive ou antivírus travando),
    /// tenta de novo e, se continuar falhando, avisa por <see cref="SaveFailed"/>. As mudanças
    /// continuam em memória, então dá para tentar salvar de novo.
    /// </summary>
    public static bool Save()
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= SaveAttempts; attempt++)
        {
            try
            {
                WriteFile();
                bool recovered = LastSaveError != null;
                LastSaveError = null;
                if (recovered) SaveRecovered?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < SaveAttempts) Thread.Sleep(120 * attempt);
            }
        }

        bool firstFailure = LastSaveError == null;
        LastSaveError = $"Não foi possível salvar as configurações: {Describe(last!)}.\n\nArquivo: {FilePath}";
        Log(last!);
        if (firstFailure) SaveFailed?.Invoke(LastSaveError);
        return false;
    }

    private static void WriteFile()
    {
        Directory.CreateDirectory(FolderPath);
        // Escrita atômica: grava num .tmp e troca, assim uma queda no meio não corrompe o arquivo.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
        try
        {
            if (!File.Exists(FilePath)) File.Move(tmp, FilePath);
            else
            {
                try { File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true); }
                catch (IOException) { File.Copy(tmp, FilePath, true); }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Explica a falha em palavras simples.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "o arquivo está como somente leitura ou falta permissão na pasta",
        IOException io when (io.HResult & 0xFFFF) == 0x70 => "o disco está cheio",
        IOException io when (io.HResult & 0xFFFF) is 0x20 or 0x21 => "o arquivo está em uso por outro programa (OneDrive, antivírus ou backup)",
        DirectoryNotFoundException => "a pasta de configurações não existe e não pôde ser criada",
        _ => ex.Message.TrimEnd('.'),
    };

    private static void Log(Exception ex)
    {
        try
        {
            File.AppendAllText(System.IO.Path.Combine(FolderPath, "erros.log"), $"[{DateTime.Now:s}] Falha ao salvar: {ex}\n\n");
        }
        catch { }
    }

    /// <summary>Salva e avisa o Dock para se redesenhar.</summary>
    public static void Commit()
    {
        Normalize(Current);
        Save();
        Changed?.Invoke();
    }

    public static readonly string[] AllWidgetKinds =
        { "clock", "calendar", "weather", "system", "battery", "media", "trash" };

    private static void Normalize(DockSettings s)
    {
        s.Items ??= new();
        s.Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Path));
        s.Widgets ??= new();
        s.Widgets.RemoveAll(w => w == null || !AllWidgetKinds.Contains(w.Kind));
        s.Widgets = s.Widgets.GroupBy(w => w.Kind).Select(g => g.First()).ToList();
        foreach (var kind in AllWidgetKinds)
        {
            if (!s.Widgets.Any(w => w.Kind == kind))
            {
                // Padrão: relógio, calendário, clima e lixeira ligados
                bool on = kind is "clock" or "calendar" or "weather" or "trash";
                s.Widgets.Add(new WidgetConfig { Kind = kind, Enabled = on });
            }
        }

        var a = s.Appearance ??= new Appearance();
        if (!Enum.IsDefined(a.Edge)) a.Edge = DockEdge.Bottom;
        if (!Enum.IsDefined(a.Theme)) a.Theme = ThemeMode.Auto;
        a.IconSize = Math.Clamp(a.IconSize, 32, 96);
        a.Magnification = Math.Clamp(a.Magnification, 1.0, 2.6);
        a.Opacity = Math.Clamp(a.Opacity, 0.0, 1.0);
        a.CornerRadius = Math.Clamp(a.CornerRadius, 0, 40);
        a.AutoHideDelay = double.IsFinite(a.AutoHideDelay) ? Math.Clamp(a.AutoHideDelay, 0, 2) : 0.3;
        a.CustomColor = ColorUtil.IsValid(a.CustomColor) ? a.CustomColor : "#2A2D3A";
        a.AccentColor = ColorUtil.IsValid(a.AccentColor) ? a.AccentColor : "#0A84FF";
        s.WeatherCity ??= "";
    }

    private static List<DockEntry>? LoadLegacyPinned()
    {
        try
        {
            if (!File.Exists(LegacyPinnedPath)) return null;
            return JsonSerializer.Deserialize<List<DockEntry>>(File.ReadAllText(LegacyPinnedPath), JsonOptions);
        }
        catch { return null; }
    }

    private static List<DockEntry> DefaultItems()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.SystemDirectory;
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new List<DockEntry>
        {
            new() { Name = "Explorador de Arquivos", Path = System.IO.Path.Combine(win, "explorer.exe") },
            new() { Name = "Microsoft Edge", Path = System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe") },
            new() { Name = "Google Chrome", Path = System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe") },
            new() { Name = "Google Chrome", Path = System.IO.Path.Combine(local, @"Google\Chrome\Application\chrome.exe") },
            new() { Name = "Bloco de Notas", Path = System.IO.Path.Combine(sys, "notepad.exe") },
            new() { Name = "Calculadora", Path = System.IO.Path.Combine(sys, "calc.exe") },
            new() { Name = "Paint", Path = System.IO.Path.Combine(sys, "mspaint.exe") },
            TerminalEntry(sys, local),
        };

        return candidates
            .Where(a => a.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(a.Path))
            .GroupBy(a => a.Name).Select(g => g.First()).ToList();
    }

    /// <summary>"Terminal": Windows Terminal quando instalado; senão PowerShell; por último o cmd.</summary>
    public static DockEntry TerminalEntry(string systemDir, string localAppData)
    {
        // O alias wt.exe em WindowsApps só existe com o Windows Terminal instalado.
        string wtAlias = System.IO.Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(wtAlias))
            return new DockEntry { Name = "Terminal", Path = @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" };
        string ps = System.IO.Path.Combine(systemDir, @"WindowsPowerShell\v1.0\powershell.exe");
        if (File.Exists(ps)) return new DockEntry { Name = "Terminal", Path = ps };
        return new DockEntry { Name = "Terminal", Path = System.IO.Path.Combine(systemDir, "cmd.exe") };
    }
}
