using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;

namespace BriskEngine.Diagnostics;

public static class DiagnosticRuleRegistry
{
    public static IReadOnlyList<IDiagnosticRule> All { get; } = new IDiagnosticRule[]
    {
        new PowerPlanRule(), new BrowserGpuRule(), new HardwareAccelerationRule(),
        new StartupBloatRule(), new RamPressureRule(), new ThermalsRule(),
        new DiskBreakdownRule(), new DiskForecastRule(), new OrphanedDataRule(),
        new StaleDevCachesRule(), new VisualEffectsRule(), new StorageSenseRule(),
    };
}
