using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Apps pedindo atenção (o ícone pula até você abrir a janela) e contadores vermelhos nos ícones.
/// O Windows avisa quando uma janela pisca (FlashWindowEx) a quem se registra com RegisterShellHookWindow.
/// Os contadores vêm do título das janelas, como "(3) WhatsApp" (ver <see cref="Badges"/>).
/// </summary>
public partial class DockWindow
{
    private static readonly int ShellHookMessage = RegisterWindowMessage("SHELLHOOK");
    private readonly AttentionTracker _attention = new();
    private HashSet<string> _attentionKeys = new();
    private Dictionary<string, int> _badgeCounts = new();
    private static readonly Brush BadgeRed = ColorUtil.Brush(Color.FromRgb(0xFF, 0x3B, 0x30));

    private void RegisterShellHook(IntPtr hwnd)
    {
        try { RegisterShellHookWindow(hwnd); }
        catch (Exception ex) { ErrorLog.Write("Avisos do shell", ex); }
    }

    private static void UnregisterShellHook(IntPtr hwnd)
    {
        try { DeregisterShellHookWindow(hwnd); } catch { }
    }

    /// <summary>Mensagem do shell: janela piscou, foi ativada ou fechou.</summary>
    private void OnShellHook(IntPtr wParam, IntPtr lParam)
    {
        int code = (int)wParam.ToInt64();
        if (code == HSHELL_FLASH)
        {
            // Quem pisca às vezes é uma caixa de diálogo do app; conta a janela principal dele.
            IntPtr hwnd = lParam;
            IntPtr root = GetAncestor(hwnd, GA_ROOTOWNER);
            if (root != IntPtr.Zero) hwnd = root;
            if (_attention.Flash(hwnd, GetForegroundWindow())) Dispatcher.BeginInvoke(() => RefreshAttention(null));
        }
        else if (code is HSHELL_WINDOWACTIVATED or HSHELL_RUDEAPPACTIVATED or HSHELL_WINDOWDESTROYED && _attention.Count > 0)
            Dispatcher.BeginInvoke(() => RefreshAttention(null));
    }

    /// <summary>Recalcula quais apps pedem atenção e faz os ícones deles pularem.</summary>
    private void RefreshAttention(List<RunningApps.AppWindow>? windows)
    {
        if (_attention.Count == 0 && _attentionKeys.Count == 0) return;
        try { windows ??= RunningApps.Windows(); }
        catch { return; }
        var keysOf = new Dictionary<IntPtr, string[]>();
        foreach (var w in windows) keysOf[w.Handle] = w.Keys.ToArray();
        _attentionKeys = _attention.Update(keysOf, GetForegroundWindow());

        bool changed = false;
        foreach (var t in _tiles)
        {
            bool want = t.ExeKey != null && _attentionKeys.Contains(t.ExeKey);
            if (want != t.Attention) { t.Attention = want; changed = true; }
        }
        BounceAttention();
        if (changed) UpdateAutomationNames();
    }

    /// <summary>Pula de novo os ícones que pedem atenção (chamado a cada segundo: o pulo dura 1,4 s).</summary>
    private void BounceAttention()
    {
        if (!A.BounceOnAttention || ReduceMotion) return;
        foreach (var t in _tiles)
            if (t.Attention && t.BounceStart < 0) Bounce(t);
    }

    /// <summary>Lê os contadores dos títulos das janelas e atualiza os ícones. True se algum mudou.</summary>
    private bool UpdateBadges(List<RunningApps.AppWindow> windows)
    {
        var counts = new Dictionary<string, int>();
        if (A.ShowBadges)
        {
            foreach (var w in windows)
            {
                if (Badges.ParseCount(RunningApps.Title(w.Handle)) is not int n) continue;
                foreach (var key in w.Keys)
                    if (!counts.TryGetValue(key, out int old) || n > old) counts[key] = n;
            }
        }
        _badgeCounts = counts;
        return ApplyBadges();
    }

    /// <summary>Passa os contadores guardados para os ícones (também depois de recriá-los).</summary>
    private bool ApplyBadges()
    {
        bool changed = false;
        foreach (var t in _tiles)
        {
            if (t.Badge == null) continue;
            string? text = A.ShowBadges && t.ExeKey != null && _badgeCounts.TryGetValue(t.ExeKey, out int n) ? Badges.Format(n) : null;
            if (text != t.BadgeText) { t.BadgeText = text; changed = true; }
            t.Attention = t.ExeKey != null && _attentionKeys.Contains(t.ExeKey);
        }
        return changed;
    }

    private static Border CreateBadge()
    {
        var badge = new Border
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Panel.SetZIndex(badge, 10);
        return badge;
    }

    /// <summary>Badge no canto do ícone (em cima à direita; com o Dock em cima, embaixo à direita).</summary>
    private void PlaceBadge(Tile t, double iconSize)
    {
        var badge = t.Badge!;
        bool show = t.BadgeText != null && t != _dragTile;
        badge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        double h = Math.Round(Math.Max(15, iconSize * 0.34));
        var text = (TextBlock)badge.Child;
        text.Text = t.BadgeText;
        text.FontSize = h * 0.62;
        bool hc = SystemParameters.HighContrast;
        badge.Background = hc ? SystemColors.HighlightBrush : BadgeRed;
        text.Foreground = hc ? SystemColors.HighlightTextBrush : Brushes.White;
        badge.Height = h;
        badge.MinWidth = h;
        badge.CornerRadius = new CornerRadius(h / 2);
        badge.Padding = new Thickness(h * 0.28, 0, h * 0.28, 0);
        badge.Measure(new Size(double.PositiveInfinity, h));
        double w = badge.DesiredSize.Width;

        double left = Canvas.GetLeft(t.Visual), top = Canvas.GetTop(t.Visual);
        if (double.IsNaN(left) || double.IsNaN(top)) return;
        Canvas.SetLeft(badge, Math.Round(left + iconSize - w * 0.8));
        Canvas.SetTop(badge, Math.Round(A.Edge == DockEdge.Top ? top + iconSize - h * 0.8 : top - h * 0.2));
    }
}
