using System.Text.Json;
using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class BlurSettingTests
{
    [Fact]
    public void Desfoque_VemLigado() => Assert.True(new Appearance().Blur);

    [Fact]
    public void ConfiguracaoAntiga_SemOCampo_FicaComDesfoque()
    {
        var s = JsonSerializer.Deserialize<DockSettings>("""{ "Appearance": { "Opacity": 0.5 } }""", SettingsStore.JsonOptions)!;
        Assert.True(s.Appearance.Blur);
        Assert.Equal(0.5, s.Appearance.Opacity);
    }

    [Fact]
    public void DesfoqueDesligado_ContinuaDesligadoAoSalvar()
    {
        var s = new DockSettings();
        s.Appearance.Blur = false;
        var again = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(s, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        Assert.False(again.Appearance.Blur);
    }
}
