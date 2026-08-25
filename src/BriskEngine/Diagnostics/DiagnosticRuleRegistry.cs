using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

public static class DiagnosticRuleRegistry
{
    public static IReadOnlyList<IDiagnosticRule> All { get; } = new IDiagnosticRule[]
    {
        new PowerPlanRule(), new BrowserGpuRule(), new HardwareAccelerationRule(),
        new StartupBloatRule(), new RamPressureRule(), new ThermalsRule(),
        new DiskBreakdownRule(), new DiskForecastRule(), new OrphanedDataRule(),
        new StaleDevCachesRule(), new VisualEffectsRule(), new StorageSenseRule(),
        new DisplayRefreshRule(), new SearchWebResultsRule(),
        new BootDegradationRule(), new MemorySpeedRule(),
        // The telemetry switches. Their ids are what a later task of this
        // wave will route the privacy page on; the list to be routed against
        // lives in the Brisk project, which this assembly cannot see —
        // PrivacyRedLineTests reads the two against each other from the side
        // that can. Today those ids only keep these findings OFF the two
        // pages that exist.
        new AdvertisingIdRule(), new DiagnosticLevelRule(),
        new TailoredExperiencesRule(), new SpeechTypingRule(),
    };
}
