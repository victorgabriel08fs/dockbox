using System.Windows.Markup;
using Dockbox.Core;

namespace Dockbox.Views;

/// <summary>Texto traduzido no XAML: <c>Text="{l:T Settings_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
