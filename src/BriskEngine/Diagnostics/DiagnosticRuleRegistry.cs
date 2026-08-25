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
        // The two that cost the user something. They are Confirm rather than
        // Auto, which is what keeps them out of `brisk fix --all` — being in
        // this list means brisk detects them and `brisk scan` prints them,
        // not that anything flips them without being asked.
        new LocationRule(), new ActivityHistoryRule(),
        // The report-only disclosures: no switch, no fix, just what Windows
        // has already written down, counted. They are Advise, which is the
        // consent level FixRunner refuses to apply a fix for at all — and
        // unlike the six above they carry a Headline whenever their read
        // succeeds, so RevelationPicker takes them today: on a machine where
        // one of these counts sorts first, it is the number the overview band
        // and the report card lead with.
        new UsbHistoryRule(), new RunHistoryRule(), new RecallStatusRule(),
        // The fourth report-only disclosure, and the only one that reads a
        // counter through a cmdlet rather than a registry key. Unlike the
        // three above it, it reports NOTHING when its reading is zero — a
        // machine that uploaded no bytes has nothing to disclose — and it
        // still reports the unreadable case, which is the distinction the
        // rule exists to keep.
        new DeliveryOptimizationRule(),
    };
}
