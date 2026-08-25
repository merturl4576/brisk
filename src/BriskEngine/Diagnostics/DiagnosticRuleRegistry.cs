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
        // The telemetry switches. Their ids are what routes them to the
        // Privacy page, and the list that routes on them lives in the Brisk
        // project, which this assembly cannot see — PrivacyRedLineTests reads
        // the two against each other from the side that can.
        new AdvertisingIdRule(), new DiagnosticLevelRule(),
        new TailoredExperiencesRule(), new SpeechTypingRule(),
    };
}
