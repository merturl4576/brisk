namespace Brisk.Tests;

/// A test run by hand (BRISK_MANUAL=1): it measures the machine it runs on,
/// so it has no place in an ordinary run or in CI.
public sealed class ManualFactAttribute : FactAttribute
{
    public ManualFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BRISK_MANUAL") != "1")
            Skip = "manual: set BRISK_MANUAL=1 to run";
    }
}
