using Dockbox.Core;

namespace Dockbox.Tests;

[Collection(SettingsCollection.Name)]
public sealed class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-log-" + Guid.NewGuid().ToString("N"));

    public ErrorLogTests()
    {
        Directory.CreateDirectory(_dir);
        SettingsStore.UseFolder(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Registro_TemDataVersaoEExcecao()
    {
        ErrorLog.Write("Teste", new InvalidOperationException("boom"));
        string text = File.ReadAllText(ErrorLog.FilePath);
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[v[\d.?]+\] Teste: System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void PassouDe1MB_RotacionaParaErros1()
    {
        File.WriteAllText(ErrorLog.FilePath, new string('x', (int)ErrorLog.MaxBytes + 10));
        ErrorLog.Write("depois da rotação");

        Assert.True(File.Exists(ErrorLog.PreviousPath));
        Assert.True(new FileInfo(ErrorLog.PreviousPath).Length > ErrorLog.MaxBytes);
        string current = File.ReadAllText(ErrorLog.FilePath);
        Assert.Contains("depois da rotação", current);
        Assert.True(current.Length < 1000);
    }

    [Fact]
    public void SegundaRotacao_SubstituiOArquivoAntigo()
    {
        File.WriteAllText(ErrorLog.PreviousPath, "muito antigo");
        File.WriteAllText(ErrorLog.FilePath, new string('y', (int)ErrorLog.MaxBytes));
        ErrorLog.Write("novo");
        Assert.DoesNotContain("muito antigo", File.ReadAllText(ErrorLog.PreviousPath));
    }
}
