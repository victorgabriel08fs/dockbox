using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Tests;

public sealed class AppBarTests
{
    private static readonly RECT Screen = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

    [Theory]
    [InlineData(DockEdge.Bottom, 0, 960, 1920, 1040)]
    [InlineData(DockEdge.Top, 0, 0, 1920, 80)]
    [InlineData(DockEdge.Left, 0, 0, 80, 1040)]
    [InlineData(DockEdge.Right, 1840, 0, 1920, 1040)]
    public void Strip_EncostaNaBordaComAEspessuraPedida(DockEdge edge, int left, int top, int right, int bottom)
    {
        var r = AppBar.Strip(Screen, edge, 80);
        Assert.Equal((left, top, right, bottom), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void Strip_RespeitaAreaJaAjustadaPeloWindows()
    {
        // Barra de tarefas à esquerda com 60 px: o Windows devolve a faixa começando em 60.
        var adjusted = new RECT { Left = 60, Top = 0, Right = 1920, Bottom = 1040 };
        var r = AppBar.Strip(adjusted, DockEdge.Bottom, 76);
        Assert.Equal((60, 964, 1920, 1040), (r.Left, r.Top, r.Right, r.Bottom));
    }
}
