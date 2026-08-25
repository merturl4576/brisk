using System;
using System.Collections.Generic;
using System.Globalization;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// How many USB storage devices Windows has a record of, and how far back
/// that record goes. Windows enumerates them two levels deep — a subkey per
/// device MODEL, and under each model a subkey per INSTANCE, which is what it
/// records one attached device as. The instances are what is counted, because
/// counting the models would report one where somebody has attached three
/// sticks of the same model.
///
/// Those subkey names identify the device: the model above, and below it the
/// instance id Windows gave that one. They are read to build the next key
/// path and they go nowhere else — no name reaches the title, the evidence,
/// an evidence argument or the headline. That is the spec's second red line:
/// a count may be shown, the thing counted may not.
///
/// The date is a separate read and it fails on its own, MEASURED rather than
/// guessed at. Windows keeps an install date in the device property store
/// below each instance, as a FILETIME — and on the machine this was written
/// on, opening that property key unelevated raises SecurityException
/// ("İstenen kayıt defteri erişimine izin verilmiyor") while the instance
/// keys above it enumerate fine. An exception escaping here would reach
/// EngineHost's catch-all and drop the whole finding, count included, so the
/// read is guarded: a refusal costs the date and not the count. brisk then
/// reports how many it counted and says, in as many words, that it could not
/// read when. It never picks a date to fill the gap — which means the
/// no-date sentence is the ordinary result of an unelevated scan here, not
/// an exotic one.
public sealed class UsbHistoryRule : PrivacyDisclosureRule
{
    public const string KeyPath = @"HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR";

    /// Below an instance key: the device property store, the property set
    /// Windows keeps device timestamps in, and the install-date property in
    /// it. Its data is the value the key holds by default, which is what an
    /// empty value name asks IRegistryProbe for.
    public const string InstallDateSubPath =
        @"Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0064";

    public const string InstallDateValueName = "";

    public override string Id => "usb-history";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var (count, earliest) = Read(ctx);
        if (count == 0) return Unread();

        var counted = count.ToString(CultureInfo.InvariantCulture);
        var headline = new Headline(
            counted, "USB storage devices recorded on this machine",
            $"rule.{Id}.headline.value", new[] { counted },
            $"rule.{Id}.headline.caption", Array.Empty<string>());

        if (earliest is not { } oldest)
            return Disclosure(
                $"rule.{Id}.title", Title,
                $"rule.{Id}.evidence.no-date",
                "Windows keeps a record of the USB storage devices that have " +
                $"been attached to this machine. brisk counted {counted} of them " +
                "and could not read a date from any of those records, so it does " +
                "not say how far back the record goes. brisk counts the records " +
                "and never reads a device name.",
                new[] { counted }, headline);

        var date = oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Disclosure(
            $"rule.{Id}.title", Title,
            $"rule.{Id}.evidence",
            "Windows keeps a record of the USB storage devices that have been " +
            $"attached to this machine. brisk counted {counted} of them, and the " +
            $"oldest date it could read among them is {date}. brisk counts the " +
            "records and never reads a device name.",
            new[] { counted, date }, headline);
    }

    private const string Title =
        "Windows keeps a record of the USB storage devices attached to this machine";

    /// Nothing counted. The reads above cannot tell a key that is not there
    /// from a key with nothing in it, and a key they were refused was already
    /// folded into the same empty answer — so brisk names no reason for the
    /// silence and reports no number for it. No headline either: the headline
    /// would be the count brisk does not have.
    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        "The number of recorded USB storage devices could not be established",
        $"rule.{Id}.evidence.unread",
        "brisk looked where Windows keeps its record of USB storage devices " +
        "and found nothing there to count. A record with nothing in it and a " +
        "record brisk could not read look the same from here, so brisk does " +
        "not report a count of none.");

    private static (int Count, DateTime? Earliest) Read(DiagnosticContext ctx)
    {
        var count = 0;
        DateTime? earliest = null;
        foreach (var model in SubKeys(ctx, KeyPath))
        foreach (var instance in SubKeys(ctx, $@"{KeyPath}\{model}"))
        {
            count++;
            if (InstallDate(ctx, $@"{KeyPath}\{model}\{instance}") is { } when
                && (earliest is null || when < earliest))
                earliest = when;
        }
        return (count, earliest);
    }

    /// A key the process may not open throws rather than answering empty.
    /// EngineHost would catch that and drop the whole finding; catching it
    /// here costs only the branch that could not be walked. KNOWN AND NOT
    /// CLAIMED AWAY: a refused model key therefore lowers the count without
    /// the copy saying it did. The alternative is losing the count and the
    /// date together, and a count that is short is still a count of records
    /// that exist.
    private static IReadOnlyList<string> SubKeys(DiagnosticContext ctx, string keyPath)
    {
        try { return ctx.Registry.GetSubKeyNames(keyPath); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// The install date of one instance, or nothing. Nothing is what a read
    /// that cannot be turned into a date returns: a refused key, a value that
    /// is not there or is not bytes, too few bytes to hold a FILETIME, and a
    /// FILETIME that will not convert — including zero, which converts
    /// happily into the first instant of the FILETIME epoch and would report
    /// a machine as having had a USB stick attached in 1601.
    private static DateTime? InstallDate(DiagnosticContext ctx, string instanceKeyPath)
    {
        byte[]? stamp;
        try
        {
            stamp = ctx.Registry.GetBytes(
                $@"{instanceKeyPath}\{InstallDateSubPath}", InstallDateValueName);
        }
        catch (Exception) { return null; }

        if (stamp is null || stamp.Length < sizeof(long)) return null;
        var filetime = BitConverter.ToInt64(stamp, 0);
        if (filetime <= 0) return null;
        try { return DateTime.FromFileTimeUtc(filetime); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
