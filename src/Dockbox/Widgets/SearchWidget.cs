using System.Windows;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Views;

namespace Dockbox.Widgets;

/// <summary>Lupa que abre a busca de apps e arquivos (a mesma do atalho global).</summary>
public sealed class SearchWidget : DockWidget
{
    public override string Kind => "search";
    public override string DisplayName => Loc.T("W_Search_Name");
    public override string Description => Loc.T("W_Search_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(5);

    public override void Build()
    {
        var tile = Tile(Vertical("#5E5CE6", "#3634A3"), Solid("#30FFFFFF"));
        tile.Child = Glyph("\uE721", 26, Brushes.White);
        Visual.Children.Add(tile);
        UpdateLabel();
    }

    public override Task UpdateAsync()
    {
        UpdateLabel();
        return Task.CompletedTask;
    }

    private void UpdateLabel()
    {
        string sc = SettingsStore.Current.Appearance.SearchShortcut;
        Label = string.IsNullOrEmpty(sc) ? Loc.T("W_Search_Name") : Loc.F("W_Search_Label", Hotkey.Display(sc));
    }

    public override void OnClick(FrameworkElement anchor) => SearchWindow.Toggle();
}
