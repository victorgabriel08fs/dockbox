using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Tests;

/// <summary>Arquivo que lembra o estado original da barra de tarefas (para restaurar depois de um travamento).</summary>
[Collection(SettingsCollection.Name)] // usa a pasta do SettingsStore, que é global
public sealed class TaskbarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-taskbar-" + Guid.NewGuid().ToString("N"));

    public TaskbarTests() => SettingsStore.UseFolder(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void SemArquivo_NaoHaNadaParaRestaurar() => Assert.Null(Taskbar.ReadMarker());

    [Theory]
    [InlineData(ABS_ALWAYSONTOP)]
    [InlineData(ABS_AUTOHIDE | ABS_ALWAYSONTOP)]
    [InlineData(0)]
    public void Marcador_GuardaELeOEstado(int state)
    {
        Taskbar.WriteMarker(state);
        Assert.True(File.Exists(Taskbar.MarkerPath));
        Assert.StartsWith(_dir, Taskbar.MarkerPath);
        Assert.Equal(state, Taskbar.ReadMarker());
        Taskbar.DeleteMarker();
        Assert.Null(Taskbar.ReadMarker());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("99")]
    public void MarcadorEstragado_RestauraABarraNormal(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Taskbar.MarkerPath, content);
        Assert.Equal(ABS_ALWAYSONTOP, Taskbar.ReadMarker());
    }

    [Theory]
    [InlineData("Windows.UI.Core.CoreWindow", true)]   // Iniciar e pesquisa
    [InlineData("XamlExplorerHostIslandWindow", true)] // painéis do Windows 11
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Notepad", false)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("", false)]
    public void JanelasDoShell_NaoEscondemABarra(string cls, bool popup) =>
        Assert.Equal(popup, Taskbar.IsShellPopup(cls));

    [Fact]
    public void Opcao_VemDesligada() => Assert.False(new Appearance().HideTaskbar);
}
