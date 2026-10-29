using System.Globalization;

namespace Dockbox.Core;

/// <summary>Contas dos widgets de volume e rede, sem chamadas ao Windows (para os testes).</summary>
public static class WidgetMath
{
    /// <summary>Quanto o volume muda por "dente" da rodinha do mouse.</summary>
    public const double VolumeStep = 0.02;

    /// <summary>Volume depois de rolar <paramref name="wheelDelta"/> (120 = um dente para cima). Arredonda ao passo.</summary>
    public static double StepVolume(double current, int wheelDelta)
    {
        if (!double.IsFinite(current)) current = 0;
        double notches = wheelDelta / 120.0;
        double next = current + notches * VolumeStep;
        next = Math.Round(next / VolumeStep) * VolumeStep;
        return Math.Clamp(Math.Round(next, 4), 0, 1);
    }

    /// <summary>Glifo do alto-falante (Segoe Fluent) conforme o volume: mudo, 0, baixo, médio, alto.</summary>
    public static string VolumeGlyph(double level, bool muted) =>
        muted ? "" : level <= 0.001 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";

    /// <summary>Bytes por segundo entre duas leituras do contador. Contador que voltou (reiniciou) dá 0.</summary>
    public static double Rate(long before, long now, double seconds)
    {
        if (seconds <= 0 || now < before) return 0;
        return (now - before) / seconds;
    }

    /// <summary>Velocidade curta para caber no ícone: "0 B/s", "820 KB/s", "1,2 MB/s", "12 MB/s".</summary>
    public static string FormatSpeed(double bytesPerSecond, CultureInfo culture)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 1) return "0 B/s";
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int u = 0;
        double v = bytesPerSecond;
        while (v >= 1000 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        string number = v < 10 && u > 0 ? v.ToString("0.0", culture) : Math.Round(v).ToString("0", culture);
        return $"{number} {units[u]}";
    }
}
