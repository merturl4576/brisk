using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;

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

    /// Everything below can reach the LSA — WindowsIdentity.Name resolves a SID
    /// to a name, NTAccount.Translate does the reverse — and on a domain-joined
    /// machine with an unreachable domain controller either can block for
    /// seconds. This runs inside AppState's constructor, on the dispatcher,
    /// before any window paints, so the whole resolution is bounded. Expiry is
    /// treated as "nothing is known", which already means brisk says nothing:
    /// the safe direction costs nothing here.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    public SessionIdentity Current()
    {
        var resolve = Task.Run(Resolve);
        return resolve.Wait(Bound)
            ? resolve.Result
            // Environment.UserName reads the process token, not the directory,
            // so it cannot be the thing that is hanging. It is only ever used
            // inside a message this verdict guarantees is never shown.
            : SessionIdentity.Unknown(Environment.UserName);
    }

    private static SessionIdentity Resolve()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var processUser = identity.Name;
            var interactive = InteractiveUser();
            if (interactive is null) return SessionIdentity.Unknown(processUser);

            // SIDs decide wherever both sides resolve, and their verdict is
            // CARRIED on the record rather than re-derived from the names: two
            // accounts in different forests can share one DOMAIN\user
            // spelling, and telling those apart is the entire reason to compare
            // SIDs in the first place.
            var processSid = identity.User?.Value;
            var interactiveSid = TranslateToSid(interactive);
            if (processSid is not null && interactiveSid is not null)
            {
                var differs = !string.Equals(processSid, interactiveSid,
                    StringComparison.Ordinal);
                return new SessionIdentity(processUser,
                    differs ? interactive : processUser, differs);
            }

            // Neither SID resolved: fall back to names, leaf-first, so an empty
            // WTSDomainName cannot manufacture a mismatch out of a bare
            // "alice" against "PC\alice".
            return new SessionIdentity(processUser, interactive,
                SessionIdentity.NamesDiffer(processUser, interactive));
        }
        catch (SystemException)
        {
            return SessionIdentity.Unknown(Environment.UserName);
        }
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
