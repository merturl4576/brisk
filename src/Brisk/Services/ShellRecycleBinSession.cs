using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Brisk.Services;

/// Late-bound Shell.Application COM — no interop assembly, no NuGet. COM is
/// deliberately untestable here; the interface is the test seam and Task 18
/// verifies this class against the real bin.
public sealed class ShellRecycleBinSession : IRecycleBinSession
{
    private const int RecycleBinFolder = 10; // ssfBITBUCKET

    public bool Restore(IReadOnlyList<string> originalPaths) =>
        ForEachMatch(originalPaths, item =>
        {
            foreach (var verbObj in item.Verbs())
            {
                dynamic verb = verbObj;
                string name = ((string)verb.Name).Replace("&", "");
                // EN "Restore", TR "Geri Yükle" — match either shell language.
                if (name.StartsWith("Restore", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Geri", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
            return false;
        });

    public bool Purge(IReadOnlyList<string> originalPaths) =>
        ForEachMatch(originalPaths, item =>
        {
            string physical = (string)item.Path;
            if (Directory.Exists(physical)) Directory.Delete(physical, recursive: true);
            else if (File.Exists(physical)) File.Delete(physical);
            return true;
        });

    public void OpenRecycleBinUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder")
            { UseShellExecute = true });
        }
        catch (Exception) { /* UI nicety only */ }
    }

    private static bool ForEachMatch(IReadOnlyList<string> originalPaths,
        Func<dynamic, bool> action)
    {
        try
        {
            var wanted = new HashSet<string>(originalPaths, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return true;
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic bin = shell.Namespace(RecycleBinFolder);
            var matched = 0;
            foreach (var itemObj in bin.Items())
            {
                dynamic item = itemObj;
                string? original =
                    item.ExtendedProperty("System.Recycle.DeducedOriginalPath") as string;
                if (original is null || !wanted.Contains(original)) continue;
                if (!action(item)) return false;
                matched++;
            }
            return matched == wanted.Count;
        }
        catch (Exception)
        {
            return false; // COM surprises degrade to "open the bin yourself"
        }
    }
}
