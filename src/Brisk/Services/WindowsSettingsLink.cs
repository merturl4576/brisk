using System;
using System.Diagnostics;

namespace Brisk.Services;

/// Opens a page of Windows' OWN Settings app. The only thing on the Privacy
/// page that hands the user somewhere else instead of changing something
/// here, and the whole of what brisk OFFERS TO DO about Recall — it reports
/// what the policy reads as, and past that it points.
///
/// UseShellExecute is what makes an "ms-settings:" URI mean anything at all:
/// it is a protocol Windows registers, not a file, and Process.Start without
/// the shell would look for an executable by that name and fail. The same
/// flag ShellRecycleBinSession uses to open the bin.
///
/// The result is REPORTED rather than swallowed, which is the one difference
/// from that method's `/* UI nicety only */`. Opening the bin is a shortcut
/// to something the user can reach from Explorer; this link is the only
/// action the Recall row has, so a click that started nothing has to say so
/// or it is a control that did nothing and kept quiet.
public static class WindowsSettingsLink
{
    public static bool Open(string uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri)
            {
                UseShellExecute = true,
            });
            // Null is not a failure here and a non-null process is not a
            // success: the shell hands the URI to whatever is registered for
            // it and may return no process object at all. What is being
            // reported is that the shell accepted it without throwing.
            return true;
        }
        catch (Exception)
        {
            // A machine with no handler registered for the protocol, or a
            // policy that blocks the Settings app. Either way brisk did not
            // reach it, and the page says so rather than looking like it did.
            return false;
        }
    }
}
