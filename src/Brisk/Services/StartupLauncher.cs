using BriskEngine.Diagnostics;

namespace Brisk.Services;

/// Registers brisk itself under HKCU Run. Default is OFF: a tool that
/// criticizes startup bloat earns trust by staying out of startup unless asked.
public sealed class StartupLauncher
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "brisk";

    private readonly IRegistryProbe _registry;
    private readonly string _exePath;

    public StartupLauncher(IRegistryProbe registry, string exePath)
    {
        _registry = registry;
        _exePath = exePath;
    }

    public bool IsOn() => _registry.GetString(RunKey, ValueName) is not null;

    public void Apply(bool on)
    {
        if (on) _registry.SetString(RunKey, ValueName, $"\"{_exePath}\" --tray");
        else _registry.DeleteValue(RunKey, ValueName);
    }
}
