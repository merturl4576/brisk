using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Principal;

namespace BriskEngine.Diagnostics.RealProbes;

/// Two WMI classes under root\Microsoft\Windows\Defender, the same scope
/// Get-MpComputerStatus and Get-MpPreference read. Verified 2026-09-05 on the
/// maintainer's machine: elevated, ExclusionPath came back with five entries
/// and the registry agreed; RealTimeProtectionEnabled and IsTamperProtected
/// both true; AMRunningMode "Normal".
///
/// THE EXCLUSION QUERY RUNS ONLY WHEN THE PROCESS IS ELEVATED, and what a
/// non-administrator gets instead is the reason. Verified 2026-09-06 in the
/// same session, unelevated: ExclusionPath, ExclusionExtension and
/// ExclusionProcess each came back holding ONE string, the same one in all
/// three — "N/A: Must be an administrator to view exclusions". Not an error,
/// not an empty list. A probe that queried anyway would count three entries
/// that do not exist and hand that sentence to the Privacy page as an
/// excluded path. Null is what brisk reports instead, and the rule says it
/// could not read the list.
public sealed class RealDefenderProbe : IDefenderProbe
{
    private const string Scope = @"root\Microsoft\Windows\Defender";

    public DefenderStatus? Read()
    {
        try
        {
            bool? realTime = null, tamper = null;
            string? mode = null;
            DateTime? quick = null, full = null;
            int? signatureAge = null;
            var answered = false;
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(Scope),
                       new ObjectQuery("SELECT RealTimeProtectionEnabled, IsTamperProtected, " +
                                       "AMRunningMode, QuickScanEndTime, FullScanEndTime, " +
                                       "AntivirusSignatureAge FROM MSFT_MpComputerStatus")))
            using (var rows = searcher.Get())
            {
                foreach (ManagementBaseObject row in rows)
                {
                    using (row)
                    {
                        answered = true;
                        realTime = row["RealTimeProtectionEnabled"] as bool?;
                        tamper = row["IsTamperProtected"] as bool?;
                        mode = row["AMRunningMode"] as string;
                        quick = WmiDate(row["QuickScanEndTime"]);
                        full = WmiDate(row["FullScanEndTime"]);
                        signatureAge = row["AntivirusSignatureAge"] switch
                        {
                            uint u => (int)Math.Min(u, int.MaxValue),
                            int i => i,
                            _ => null,
                        };
                    }
                    break;
                }
            }
            if (!answered) return null;

            int? count = null;
            var paths = new List<string>();
            if (IsElevated())
            {
                count = 0;
                using var prefs = new ManagementObjectSearcher(new ManagementScope(Scope),
                    new ObjectQuery("SELECT ExclusionPath, ExclusionExtension, ExclusionProcess " +
                                    "FROM MSFT_MpPreference"));
                using var rows = prefs.Get();
                foreach (ManagementBaseObject row in rows)
                {
                    using (row)
                    {
                        foreach (var name in new[] { "ExclusionPath", "ExclusionExtension", "ExclusionProcess" })
                        {
                            if (row[name] is not string[] items) continue;
                            count += items.Length;
                            if (name == "ExclusionPath") paths.AddRange(items);
                        }
                    }
                    break;
                }
            }
            return new DefenderStatus(realTime, tamper, mode, count, paths, quick, full, signatureAge);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// WMI dates are DMTF strings; an unset one is null or empty. A string
    /// the converter refuses is no date, not a date of zero.
    private static DateTime? WmiDate(object? value)
    {
        if (value is not string s || s.Length == 0) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s).ToUniversalTime(); }
        catch (Exception) { return null; }
    }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
}
