namespace Dockbox.Core;

/// <summary>
/// Temporizador do widget (Pomodoro). Recebe a hora de fora em cada chamada, para os testes não esperarem de verdade.
/// </summary>
public sealed class FocusTimer
{
    public static readonly int[] Presets = { 5, 15, 25, 50 };
    public const int DefaultMinutes = 25;

    private TimeSpan _elapsedBeforeStart;
    private DateTime? _startedAt;

    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(DefaultMinutes);
    public bool Running => _startedAt != null;

    public TimeSpan Elapsed(DateTime now)
    {
        var e = _elapsedBeforeStart + (_startedAt is DateTime s ? now - s : TimeSpan.Zero);
        if (e < TimeSpan.Zero) e = TimeSpan.Zero; // relógio do PC voltou
        return e > Duration ? Duration : e;
    }

    public TimeSpan Remaining(DateTime now) => Duration - Elapsed(now);

    /// <summary>De 0 (acabou de começar) a 1 (terminou).</summary>
    public double Fraction(DateTime now) => Duration.TotalSeconds <= 0 ? 1 : Elapsed(now).TotalSeconds / Duration.TotalSeconds;

    public bool IsDone(DateTime now) => Elapsed(now) >= Duration;

    /// <summary>Nunca iniciado ou zerado.</summary>
    public bool IsIdle => !Running && _elapsedBeforeStart == TimeSpan.Zero;

    public void Start(DateTime now)
    {
        if (Running) return;
        if (IsDone(now)) _elapsedBeforeStart = TimeSpan.Zero;
        _startedAt = now;
    }

    public void Pause(DateTime now)
    {
        if (_startedAt == null) return;
        _elapsedBeforeStart = Elapsed(now);
        _startedAt = null;
    }

    public void Reset()
    {
        _startedAt = null;
        _elapsedBeforeStart = TimeSpan.Zero;
    }

    /// <summary>Troca a duração e zera.</summary>
    public void SetMinutes(int minutes)
    {
        Duration = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180));
        Reset();
    }

    /// <summary>"25:00", "4:59", "1:05:00".</summary>
    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        // Arredonda para cima: com 0,4 s restantes ainda mostra 0:01, e 0:00 só quando terminou.
        long total = (long)Math.Ceiling(t.TotalSeconds - 1e-9);
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
}
