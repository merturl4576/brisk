using System;

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
    /// determined.
    string? InteractiveUser,
    /// The verdict, carried rather than re-derived. The probe decides it by
    /// SID where it can, and a SID comparison must not be second-guessed by a
    /// string: two accounts in different forests can share one DOMAIN\\user
    /// spelling, which is exactly the case SIDs exist to tell apart. Null
    /// InteractiveUser is never a mismatch — an unknown answer must not
    /// become a confident claim about whose files these are.
    bool DiffersFromInteractiveUser)
{
    /// Nothing could be established about the session. Says nothing.
    public static SessionIdentity Unknown(string processUser) =>
        new(processUser, null, false);

    /// The name-only comparison, used ONLY when SIDs could not be resolved on
    /// both sides. It compares the leaf account name, because WTSDomainName
    /// can come back empty: a bare "alice" against "PC\\alice" is the same
    /// person, and declaring a mismatch there would be a false accusation on
    /// the strength of a missing string.
    ///
    /// That trade is deliberate and one-directional. Comparing leaves can miss
    /// a real difference (PC1\\alice versus CORP\\alice), which produces
    /// silence; the alternative produces a false claim. Silence is the only
    /// safe way to be wrong here.
    public static bool NamesDiffer(string processUser, string? interactiveUser)
    {
        if (interactiveUser is null) return false;
        if (string.Equals(processUser, interactiveUser, StringComparison.OrdinalIgnoreCase))
            return false;
        return !string.Equals(Leaf(processUser), Leaf(interactiveUser),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Leaf(string account)
    {
        var slash = account.LastIndexOf('\\');
        return slash < 0 ? account : account[(slash + 1)..];
    }
}

public interface ISessionProbe
{
    SessionIdentity Current();
}
