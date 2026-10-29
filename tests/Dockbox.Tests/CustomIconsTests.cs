using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Tests;

[Collection(SettingsCollection.Name)] // usa a pasta do SettingsStore
public sealed class CustomIconsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-icons-" + Guid.NewGuid().ToString("N"));

    public CustomIconsTests()
    {
        Directory.CreateDirectory(_dir);
        SettingsStore.UseFolder(_dir);
        SettingsStore.Load();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Imagens do WPF numa thread STA, como no app.</summary>
    private static void Sta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private string MakePng(int w, int h, string name)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        string path = Path.Combine(_dir, name);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(path)) enc.Save(f);
        return path;
    }

    private static (int W, int H) SizeOf(string path)
    {
        using var s = File.OpenRead(path);
        var frame = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    [Theory]
    [InlineData(1024, 512, 256, 128)]
    [InlineData(300, 900, 85, 256)]
    [InlineData(64, 64, 64, 64)]       // pequena: não aumenta
    [InlineData(256, 256, 256, 256)]
    [InlineData(0, 10, 1, 1)]
    public void TamanhoFinal(int w, int h, int ew, int eh) => Assert.Equal((ew, eh), CustomIcons.FitSize(w, h));

    [Fact]
    public void ImagemGrande_ReduzidaECopiadaParaAPastaDeIcones() => Sta(() =>
    {
        string src = MakePng(1024, 512, "grande.png");
        string name = CustomIcons.ImportImage(src);
        string saved = CustomIcons.FullPath(name)!;
        Assert.StartsWith(CustomIcons.Folder, saved);
        Assert.Equal((256, 128), SizeOf(saved));
    });

    [Fact]
    public void IconeSobreviveAApagarOOriginal() => Sta(() =>
    {
        string src = MakePng(128, 128, "logo.png");
        string name = CustomIcons.ImportImage(src);
        File.Delete(src);
        Assert.NotNull(CustomIcons.Load(name));
        // E sobrevive a reiniciar: o nome fica salvo no item.
        SettingsStore.Current.Items.Add(new DockEntry { Name = "App", Path = @"C:\app.exe", Icon = name });
        SettingsStore.Save();
        SettingsStore.Load();
        Assert.Equal(name, SettingsStore.Current.Items.Last().Icon);
        Assert.NotNull(CustomIcons.Load(SettingsStore.Current.Items.Last().Icon));
    });

    [Fact]
    public void IconeDeUmaDll() => Sta(() =>
    {
        string shell32 = Path.Combine(Environment.SystemDirectory, "shell32.dll");
        if (!File.Exists(shell32)) return; // só no Windows
        string name = CustomIcons.ImportFromModule(shell32, 3);
        var (w, h) = SizeOf(CustomIcons.FullPath(name)!);
        Assert.InRange(w, 16, CustomIcons.MaxSize);
        Assert.Equal(w, h);
    });

    [Fact]
    public void Restaurar_ApagaACopiaSoSeNinguemMaisUsa() => Sta(() =>
    {
        string name = CustomIcons.ImportImage(MakePng(32, 32, "a.png"));
        var a = new DockEntry { Name = "A", Path = @"C:\a.exe", Icon = name };
        var b = new DockEntry { Name = "B", Path = @"C:\b.exe", Icon = name };
        SettingsStore.Current.Items.Add(a);
        SettingsStore.Current.Items.Add(b);

        CustomIcons.Restore(a);
        Assert.Null(a.Icon);
        Assert.True(File.Exists(CustomIcons.FullPath(name)));   // B ainda usa

        CustomIcons.Restore(b);
        Assert.False(File.Exists(CustomIcons.FullPath(name)));
    });

    [Fact]
    public void SemIconeOuArquivoSumido_Null()
    {
        Assert.Null(CustomIcons.Load(null));
        Assert.Null(CustomIcons.Load("nao-existe.png"));
        Assert.Null(CustomIcons.FullPath(""));
    }

    [Fact]
    public void NomeGuardado_NaoSaiDaPastaDeIcones() =>
        Assert.Equal(Path.Combine(CustomIcons.Folder, "x.png"), CustomIcons.FullPath(@"..\..\Windows\x.png"));

    [Theory]
    [InlineData("foto.PNG", true, false)]
    [InlineData("icone.ico", true, false)]
    [InlineData("app.exe", false, true)]
    [InlineData("shell32.dll", false, true)]
    [InlineData("doc.txt", false, false)]
    public void TiposAceitos(string file, bool image, bool module)
    {
        Assert.Equal(image, CustomIcons.IsImage(file));
        Assert.Equal(module, CustomIcons.IsModule(file));
    }
}
