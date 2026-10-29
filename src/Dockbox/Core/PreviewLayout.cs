namespace Dockbox.Core;

/// <summary>
/// Tamanhos do painel de miniaturas das janelas. Sem chamadas ao Windows, para poder testar.
/// Todas as medidas em DIPs.
/// </summary>
internal static class PreviewLayout
{
    /// <summary>Caixa da miniatura no tamanho normal (paisagem, como uma tela).</summary>
    public const double BoxWidth = 220, BoxHeight = 138;
    /// <summary>Menor fração da caixa antes de passar a esconder janelas que não cabem.</summary>
    public const double MinFactor = 0.6;
    /// <summary>Apps precisam ter pelo menos esse número de janelas para o painel aparecer.</summary>
    public const int MinWindows = 2;

    /// <summary>
    /// Quantas miniaturas cabem e em que escala. <paramref name="cardLength"/> é o comprimento de um cartão
    /// no eixo do Dock no tamanho normal (com margens), <paramref name="fixedLength"/> a parte do cartão que
    /// não encolhe (título, margens) e <paramref name="available"/> o espaço da tela nesse eixo.
    /// </summary>
    public static (double Factor, int Count) Arrange(int windows, double cardLength, double fixedLength, double available)
    {
        if (windows <= 0 || cardLength <= 0) return (1, 0);
        if (windows * cardLength <= available) return (1, windows);

        double shrinkable = Math.Max(1, cardLength - fixedLength);
        double perCard = available / windows;
        double factor = Math.Clamp((perCard - fixedLength) / shrinkable, MinFactor, 1);
        double small = fixedLength + shrinkable * factor;
        int count = Math.Clamp((int)Math.Floor(available / small + 1e-9), 1, windows);
        return (factor, count);
    }

    /// <summary>
    /// Retângulo da miniatura dentro da caixa <paramref name="boxW"/>×<paramref name="boxH"/>, mantendo a
    /// proporção da janela e centralizado. Sem tamanho válido da janela, ocupa a caixa toda.
    /// </summary>
    public static (double X, double Y, double W, double H) Fit(double sourceW, double sourceH, double boxW, double boxH)
    {
        if (!(sourceW > 0) || !(sourceH > 0) || !double.IsFinite(sourceW) || !double.IsFinite(sourceH))
            return (0, 0, boxW, boxH);
        double s = Math.Min(boxW / sourceW, boxH / sourceH);
        double w = sourceW * s, h = sourceH * s;
        return ((boxW - w) / 2, (boxH - h) / 2, w, h);
    }
}
