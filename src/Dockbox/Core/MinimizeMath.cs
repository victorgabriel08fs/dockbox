namespace Dockbox.Core;

/// <summary>Efeito ao minimizar uma janela para o Dock.</summary>
public enum MinimizeEffect { None, Scale, Genie }

/// <summary>
/// Contas da animação ao minimizar: o retângulo da janela em cada instante, do tamanho original até o ícone.
/// Sem chamadas ao Windows, para poder testar. Retângulos em pixels físicos: (X, Y, W, H).
/// </summary>
internal static class MinimizeMath
{
    /// <summary>Duração em segundos de cada efeito.</summary>
    public static double Duration(MinimizeEffect effect) => effect switch
    {
        MinimizeEffect.Genie => 0.42,
        MinimizeEffect.Scale => 0.26,
        _ => 0,
    };

    /// <summary>
    /// Retângulo no instante <paramref name="t"/> (0 = janela, 1 = ícone). <paramref name="horizontalDock"/>:
    /// Dock embaixo ou em cima. No Gênio, a janela primeiro se estreita no sentido do Dock e depois desce
    /// até o ícone, como se fosse sugada; na Escala, tudo encolhe junto.
    /// </summary>
    public static (double X, double Y, double W, double H) At(MinimizeEffect effect, bool horizontalDock,
        (double X, double Y, double W, double H) from, (double X, double Y, double W, double H) to, double t)
    {
        t = Math.Clamp(double.IsFinite(t) ? t : 1, 0, 1);
        if (effect == MinimizeEffect.Genie)
        {
            // Eixo ao longo do Dock: rápido no começo. Eixo em direção ao Dock: começa depois e acelera.
            double along = EaseOut(Math.Clamp(t / 0.6, 0, 1));
            double toward = EaseIn(Math.Clamp((t - 0.2) / 0.8, 0, 1));
            return horizontalDock
                ? (Lerp(from.X, to.X, along), Lerp(from.Y, to.Y, toward), Lerp(from.W, to.W, along), Lerp(from.H, to.H, toward))
                : (Lerp(from.X, to.X, toward), Lerp(from.Y, to.Y, along), Lerp(from.W, to.W, toward), Lerp(from.H, to.H, along));
        }
        double k = EaseInOut(t);
        return (Lerp(from.X, to.X, k), Lerp(from.Y, to.Y, k), Lerp(from.W, to.W, k), Lerp(from.H, to.H, k));
    }

    /// <summary>Transparência no instante <paramref name="t"/>: some um pouco no fim, ao entrar no ícone.</summary>
    public static double Opacity(double t) => t < 0.75 ? 1 : 1 - (Math.Clamp(t, 0, 1) - 0.75) / 0.25 * 0.6;

    /// <summary>
    /// Retângulo da janela antes de minimizar, a partir do posicionamento salvo pelo Windows (GetWindowPlacement).
    /// <paramref name="normal"/> vem em coordenadas da área de trabalho: soma o deslocamento entre a área de
    /// trabalho e o monitor (barra de tarefas à esquerda ou em cima). Janela maximizada ocupa a área de trabalho.
    /// </summary>
    public static (double X, double Y, double W, double H) RestoredRect(
        (int Left, int Top, int Right, int Bottom) normal, bool maximized,
        (int Left, int Top, int Right, int Bottom) monitor, (int Left, int Top, int Right, int Bottom) work)
    {
        if (maximized) return (work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        int dx = work.Left - monitor.Left, dy = work.Top - monitor.Top;
        return (normal.Left + dx, normal.Top + dy, normal.Right - normal.Left, normal.Bottom - normal.Top);
    }

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;
    private static double EaseIn(double t) => t * t * t;
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
}
