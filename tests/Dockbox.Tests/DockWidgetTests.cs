using System.Windows.Media;
using Dockbox.Widgets;

namespace Dockbox.Tests;

public class DockWidgetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Arc_VazioQuandoZero(double f) => Assert.True(DockWidget.Arc(32, 32, 20, f).IsEmpty());

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Arc_CirculoCompletoQuandoUm(double f) => Assert.IsType<EllipseGeometry>(DockWidget.Arc(32, 32, 20, f));

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Arc_ParcialFicaDentroDoCirculo(double f)
    {
        var g = DockWidget.Arc(32, 32, 20, f);
        var b = g.Bounds;
        Assert.InRange(b.Left, 11.9, 52.1);
        Assert.InRange(b.Right, 11.9, 52.1);
        Assert.InRange(b.Top, 11.9, 52.1);
        Assert.InRange(b.Bottom, 11.9, 52.1);
    }

    [Fact]
    public void WidgetFactory_CriaTodosOsTiposConhecidos()
    {
        // Precisa de thread STA por causa dos elementos WPF.
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                foreach (var kind in Dockbox.Core.SettingsStore.AllWidgetKinds)
                {
                    var w = WidgetFactory.Create(kind);
                    Assert.NotNull(w);
                    Assert.Equal(kind, w!.Kind);
                    Assert.False(string.IsNullOrWhiteSpace(WidgetFactory.DisplayName(kind)));
                    Assert.NotEqual(kind, WidgetFactory.DisplayName(kind));
                    w.Dispose();
                }
                Assert.Null(WidgetFactory.Create("desconhecido"));
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }
}
