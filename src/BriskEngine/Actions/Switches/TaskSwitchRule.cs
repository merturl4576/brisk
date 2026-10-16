using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Switches;

public interface IScheduledTaskProbe
{
    /// null: no such task on this machine.
    bool? IsEnabled(string taskPath);
    void SetEnabled(string taskPath, bool enabled);
}

/// schtasks /XML is read instead of the table output because the table's
/// status words are localized; the XML element names are not.
public sealed class RealScheduledTaskProbe : IScheduledTaskProbe
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly IProcessRunner _runner;

    public RealScheduledTaskProbe(IProcessRunner runner) { _runner = runner; }

    public bool? IsEnabled(string taskPath)
    {
        var (exit, xml) = _runner.Run("schtasks.exe", $"/Query /TN \"{taskPath}\" /XML");
        return exit == 0 ? ParseEnabled(xml) : null;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        var (exit, _) = _runner.Run("schtasks.exe",
            $"/Change /TN \"{taskPath}\" /{(enabled ? "Enable" : "Disable")}");
        if (exit != 0)
            throw new InvalidOperationException($"schtasks exited {exit} for '{taskPath}'");
    }

    internal static bool? ParseEnabled(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml.Trim());
            var settings = doc.Root?.Element(Ns + "Settings");
            if (settings is null) return null;
            var enabled = settings.Element(Ns + "Enabled");
            return enabled is null || !string.Equals(enabled.Value.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException) { return null; }
    }
}

/// The scheduled tasks that collect telemetry. A task this build does not
/// have (Compatibility Appraiser is gone on 26200) is skipped silently.
public sealed class TaskSwitchRule : IReadBackable
{
    public static IReadOnlyList<string> TelemetryTasks { get; } = new[]
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    };

    private readonly IScheduledTaskProbe _probe;

    public TaskSwitchRule(IScheduledTaskProbe probe) { _probe = probe; }

    public string Id => "telemetry-tasks";
    public RuleCategory Category => RuleCategory.Auto;

    private List<string> Enabled() =>
        TelemetryTasks.Where(t => _probe.IsEnabled(t) == true).ToList();

    public bool IsOn(DiagnosticContext ctx) => Enabled().Count > 0;

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var enabled = Enabled();
        if (enabled.Count == 0) return null;
        return new DiagnosticFinding(Id, $"rule.{Id}.title",
            "Telemetry scheduled tasks are enabled",
            $"{enabled.Count} scheduled tasks that collect usage data are enabled.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Disable the telemetry scheduled tasks (undoable)",
            Kind: FindingKind.Notice);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var enabled = Enabled();
        foreach (var t in enabled) _probe.SetEnabled(t, false);
        return JsonSerializer.Serialize(enabled);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        foreach (var t in JsonSerializer.Deserialize<List<string>>(priorStateJson) ?? new())
            _probe.SetEnabled(t, true);
    }
}
