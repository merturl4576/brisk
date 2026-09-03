using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BriskEngine.Cleaning;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealPowercfgProbe : IPowercfgProbe
{
    public sealed record Scheme(Guid Id, string Name, bool IsActive);

    private readonly IProcessRunner _runner;
    public RealPowercfgProbe(IProcessRunner runner) => _runner = runner;

    // Locale-proof: matches the GUID and the parenthesised name, never English labels.
    private static readonly Regex SchemeLine = new(
        @"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((?<name>[^)]+)\)\s*(?<active>\*)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<Scheme> ParseSchemes(string powercfgOutput) =>
        SchemeLine.Matches(powercfgOutput)
            .Select(m => new Scheme(
                Guid.Parse(m.Groups["guid"].Value),
                m.Groups["name"].Value.Trim(),
                m.Groups["active"].Success))
            .ToList();

    public (Guid Id, string Name) GetActiveScheme()
    {
        var (_, stdout) = _runner.Run("powercfg", "/getactivescheme");
        var scheme = ParseSchemes(stdout).FirstOrDefault()
            ?? throw new InvalidOperationException("Could not parse powercfg output");
        return (scheme.Id, scheme.Name);
    }

    public IReadOnlyList<(Guid Id, string Name)> ListSchemes()
    {
        var (_, stdout) = _runner.Run("powercfg", "/list");
        return ParseSchemes(stdout).Select(s => (s.Id, s.Name)).ToList();
    }

    public void SetActive(Guid id)
    {
        var (code, _) = _runner.Run("powercfg", $"/setactive {id}");
        if (code != 0) throw new InvalidOperationException($"powercfg /setactive failed ({code})");
    }

    /// GetSystemPowerStatus: BatteryFlag 128 means "no system battery", 255
    /// means unknown. Only an explicit 128 counts as a desktop; a failed call
    /// or an unknown flag is reported as "has battery" so the rule stays quiet.
    public bool HasBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return true;
        const byte NoSystemBattery = 128;
        return (status.BatteryFlag & NoSystemBattery) == 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
