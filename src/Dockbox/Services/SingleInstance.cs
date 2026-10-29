using System.Diagnostics;

namespace Dockbox.Services;

/// <summary>
/// Uma cópia do Dockbox por sessão do Windows. Abrir de novo (por exemplo, uma versão nova) pergunta
/// se é para trocar: a cópia aberta recebe um pedido para fechar e, se não responder (versão antiga
/// sem esse pedido, travada), é encerrada.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\Dockbox.SingleInstance";
    private const string QuitEventName = @"Local\Dockbox.Quit";

    /// <summary>Cria a trava. <paramref name="isFirst"/> é false quando outra cópia já está aberta.</summary>
    public static Mutex Create(out bool isFirst) => new(true, MutexName, out isFirst);

    /// <summary>Espera a trava ficar livre (a outra cópia fechou). True se agora ela é desta cópia.</summary>
    public static bool WaitFor(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { return true; } // a outra cópia foi encerrada sem soltar a trava
    }

    /// <summary>Na cópia aberta: chama <paramref name="onQuit"/> quando outra cópia pedir para ela fechar.</summary>
    public static void ListenForQuit(Action onQuit)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var thread = new Thread(() =>
        {
            while (signal.WaitOne()) onQuit();
        })
        { IsBackground = true, Name = "Dockbox.Quit" };
        thread.Start();
    }

    /// <summary>Pede para a cópia aberta fechar. False se ela não escuta esse pedido (versão antiga).</summary>
    public static bool AskRunningToQuit()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var signal)) return false;
        using (signal) return signal.Set();
    }

    /// <summary>Nomes de processo que podem ser outra cópia do Dockbox (o exe pode ter sido renomeado).</summary>
    internal static IEnumerable<string> CandidateNames(string currentName) =>
        new[] { currentName, "Dockbox" }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Encerra as outras cópias nesta sessão. False se alguma não pôde ser encerrada (ex.: aberta como administrador).</summary>
    public static bool KillOthers()
    {
        using var me = Process.GetCurrentProcess();
        bool ok = true;
        foreach (var name in CandidateNames(me.ProcessName))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.Id == me.Id || p.SessionId != me.SessionId) continue;
                    try
                    {
                        p.Kill();
                        if (!p.WaitForExit(3000)) ok = false;
                    }
                    catch { ok = false; }
                }
            }
        }
        return ok;
    }
}
