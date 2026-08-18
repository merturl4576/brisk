namespace BriskEngine.Diagnostics;

/// Who brisk is running as, versus who is sitting at the machine.
///
/// On an administrator account UAC keeps the same SID, these two are equal and
/// nothing here matters. On a STANDARD account they are not: Windows asks for
/// another account's credentials ("over-the-shoulder" elevation) and the
/// elevated process belongs to that administrator. Everything brisk reads and
/// writes per-user then follows the token, not the person — HKCU (search
/// suggestions, visual effects, storage sense, browser GPU, and the whole Run
/// list the startup page shows), %LOCALAPPDATA% (every cleaner target: user
/// temp, all five browser caches, thumbnails, the dev caches), the fix journal,
/// the action log, and the Recycle Bin the safe clean purges.
///
/// brisk does not refuse to run that way — it is a legitimate setup — but it
/// must never present another account's profile as "your temp files".
public sealed record SessionIdentity(
    /// The account brisk's process token belongs to, e.g. "PC\\Admin".
    string ProcessUser,
    /// The account signed in to this session, or null when it could not be
    /// determined. Null is never reported as a mismatch: an unknown answer
    /// must not become a confident claim.
    string? InteractiveUser)
{
    public bool DiffersFromInteractiveUser =>
        InteractiveUser is not null &&
        !string.Equals(ProcessUser, InteractiveUser, System.StringComparison.OrdinalIgnoreCase);
}

public interface ISessionProbe
{
    SessionIdentity Current();
}
