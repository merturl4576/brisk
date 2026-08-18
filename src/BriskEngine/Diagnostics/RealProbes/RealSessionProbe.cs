using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BriskEngine.Diagnostics.RealProbes;

/// Compares the process token's owner against the owner of the session the
/// process is running in.
///
/// The session is the right question, not the console: over-the-shoulder
/// elevation starts the elevated process in the SAME session as the person who
/// asked for it, with a different token. So this asks Windows who is signed in
/// to *this* session (WTSQuerySessionInformation on the process's own session
/// id) rather than WTSGetActiveConsoleSessionId, which names the physical
/// console and would answer wrongly over RDP.
///
/// SIDs are compared where both can be resolved, because two accounts can
/// share a name across domains. When a name cannot be translated to a SID —
/// an unreachable domain controller, a deleted account — the comparison falls
/// back to the account names. When even the session cannot be queried, the
/// interactive user is reported as unknown and nothing is claimed: a check
/// that guesses is worse than no check.
public sealed class RealSessionProbe : ISessionProbe
{
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    public SessionIdentity Current()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var processUser = identity.Name;
        var interactive = InteractiveUser();
        if (interactive is null) return new SessionIdentity(processUser, null);

        // Prefer SIDs: names are not unique across domains, and this decides
        // whether brisk tells the user their profile is not the one being read.
        var processSid = identity.User?.Value;
        var interactiveSid = TranslateToSid(interactive);
        if (processSid is not null && interactiveSid is not null)
            return new SessionIdentity(processUser,
                string.Equals(processSid, interactiveSid, StringComparison.Ordinal)
                    ? processUser        // same account: report no difference at all
                    : interactive);

        return new SessionIdentity(processUser, interactive);
    }

    private static string? TranslateToSid(string accountName)
    {
        try
        {
            return ((SecurityIdentifier)new NTAccount(accountName)
                .Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (IdentityNotMappedException) { return null; }
        catch (SystemException) { return null; }   // domain unreachable, RPC failure
    }

    private static string? InteractiveUser()
    {
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var session))
            return null;
        var user = QuerySession(session, WtsUserName);
        if (string.IsNullOrEmpty(user)) return null;
        var domain = QuerySession(session, WtsDomainName);
        return string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
    }

    private static string? QuerySession(uint session, int infoClass)
    {
        var buffer = IntPtr.Zero;
        try
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass,
                    out buffer, out _))
                return null;
            return Marshal.PtrToStringUni(buffer);
        }
        catch (DllNotFoundException) { return null; }   // no Terminal Services
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
