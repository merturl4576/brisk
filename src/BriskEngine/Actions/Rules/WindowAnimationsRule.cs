using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IUiEffectsProbe
{
    bool MinimizeAnimation();
    bool ClientAreaAnimation();
    void Set(bool minimize, bool clientArea);
}

public sealed class RealUiEffectsProbe : IUiEffectsProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo { public uint cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref AnimationInfo pvParam, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref bool pvParam, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, System.IntPtr pvParam, uint winIni);

    private const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public bool MinimizeAnimation()
    {
        var info = new AnimationInfo { cbSize = (uint)Marshal.SizeOf<AnimationInfo>() };
        return SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0) && info.iMinAnimate != 0;
    }

    public bool ClientAreaAnimation()
    {
        var on = false;
        return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) && on;
    }

    public void Set(bool minimize, bool clientArea)
    {
        var info = new AnimationInfo
        {
            cbSize = (uint)Marshal.SizeOf<AnimationInfo>(),
            iMinAnimate = minimize ? 1 : 0,
        };
        if (!SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, (System.IntPtr)(clientArea ? 1 : 0),
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}

/// Minimize/maximize and in-window animations, through SystemParametersInfo
/// so the change is felt at once.
public sealed class WindowAnimationsRule : IReadBackable
{
    private sealed record Prior(bool Minimize, bool ClientArea);
    private readonly IUiEffectsProbe _ui;

    public WindowAnimationsRule(IUiEffectsProbe ui) { _ui = ui; }

    public string Id => "window-animations";
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx) => _ui.MinimizeAnimation() || _ui.ClientAreaAnimation();

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Window animations are on",
            "Windows animates windows when they open, minimize and maximize.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn window animations off (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(_ui.MinimizeAnimation(), _ui.ClientAreaAnimation());
        _ui.Set(false, false);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        _ui.Set(prior.Minimize, prior.ClientArea);
    }
}
