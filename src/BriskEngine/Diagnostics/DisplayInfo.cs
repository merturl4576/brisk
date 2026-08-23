namespace BriskEngine.Diagnostics;

/// One attached display, and the refresh rates its driver reports for the
/// resolution it is running at right now. MaxHz is computed by the probe
/// because "which modes count" is a Win32 enumeration detail; the rule above
/// only decides whether the gap is worth reporting.
public sealed record DisplayInfo(
    string DeviceName,     // @"\\.\DISPLAY1" — the name ChangeDisplaySettingsEx wants
    string FriendlyName,   // what the finding shows the user
    int CurrentHz,
    int MaxHz);
