using System;
using System.Collections.Generic;
using System.Globalization;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// ONE instance of one USB storage device, as Windows recorded it: the name
/// of the model subkey it sits under, and the two dates the device property
/// store keeps below it.
///
/// Either date is null when brisk did not read one — a refused key, a value
/// that is not there, a stamp that will not convert. Null means "brisk read
/// no date here" and never "there is no date": the two are the same from
/// where this read stands, and picking one of them is what the rest of this
/// file refuses to do with a count.
///
/// THIS IS THE ONLY SHAPE A DEVICE NAME TRAVELS IN, and it travels one way:
/// EngineHost puts it in ScanSnapshot.UsbDevices and the Gizlilik page
/// renders it. It is not on DiagnosticFinding, so no report card, headline
/// or picked row can carry a name however the rest of the pipeline is wired.
/// That is the spec's red line 2 as amended on 2026-08-26 — shown in full to
/// its owner, on the owner's own screen, and on no surface built to be
/// shared — held by construction rather than by remembering.
public sealed record UsbDeviceRecord(string Model, DateTime? FirstSeen, DateTime? LastSeen);

/// How many USB storage devices Windows has a record of, and how far back
/// that record goes. Windows enumerates them two levels deep — a subkey per
/// device MODEL, and under each model a subkey per INSTANCE, which is what it
/// records one attached device as. The instances are what is counted, because
/// counting the models would report one where somebody has attached three
/// sticks of the same model.
///
/// Those subkey names identify the device: the model above, and below it the
/// instance id Windows gave that one. Detect reads them to build the next key
/// path and they go nowhere else FROM HERE — no name reaches the title, the
/// evidence, an evidence argument or the headline, which is what a finding
/// carries onto every surface built to be shared.
///
/// ReadDevices below is the one door the model name leaves by, and it does
/// not open onto a finding: the spec's red line 2, amended on the
/// maintainer's call at his first live look (2026-08-26), lets the record be
/// shown in full to its owner on the Gizlilik page and nowhere else. The
/// instance id is not part of that — it is a serial number, it identifies
/// the stick rather than describing it, and nothing asked for it.
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
    /// it. The data is read as the value that key holds by default, which is
    /// what an empty value name asks IRegistryProbe for.
    ///
    /// UNVERIFIED against real hardware, unlike the enum root and the two
    /// levels above it, which were enumerated on this machine. This path and
    /// that value name come from the task brief, and the machine this was
    /// written on refuses the property key unelevated (see the class header),
    /// so only the fake has ever exercised the layout. If the date never
    /// appears on a machine that CAN open the key, this constant and
    /// InstallDateValueName are the first two things to doubt.
    public const string InstallDateSubPath =
        @"Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0064";

    public const string InstallDateValueName = "";

    /// The other half of the same property store: when Windows last saw this
    /// device attached. Same property set, property 0066 —
    /// DEVPKEY_Device_LastArrivalDate — read as the same default value under
    /// the same InstallDateValueName, because "the value the key holds by
    /// default" is one empty name whichever property it belongs to.
    ///
    /// UNVERIFIED against real hardware, exactly like InstallDateSubPath
    /// above and for exactly the same reason: this path came from the task
    /// brief, the machine this was written on refuses the property key
    /// unelevated, and only the fake has ever exercised the layout. If a
    /// device shows a first date and never a last one on a machine that CAN
    /// open the key, this constant is the first thing to doubt.
    public const string LastArrivalSubPath =
        @"Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0066";

    /// Every instance Detect counts, carrying what Detect throws away: the
    /// model name and both dates, one record per INSTANCE, in the order the
    /// two-level walk reaches them. The same walk and the same guards — a
    /// refused model key costs that branch, a refused property read costs
    /// that field — so a refusal costs a record's DATE, never the record,
    /// never the list, and never the scan.
    ///
    /// It walks the registry a second time in the same scan rather than
    /// sharing Detect's pass, and that is deliberate. Detect's answer is a
    /// count and one earliest date; a list of records cannot be recovered
    /// from it, and folding this into Detect would change the shape of a
    /// method whose behaviour a dozen tests pin. The cost is one extra
    /// enumeration of a key that holds tens of entries.
    ///
    /// The one caller is EngineHost, which puts the answer in
    /// ScanSnapshot.UsbDevices for the Gizlilik page. Nothing here reaches a
    /// DiagnosticFinding.
    public static IReadOnlyList<UsbDeviceRecord> ReadDevices(DiagnosticContext ctx)
    {
        var devices = new List<UsbDeviceRecord>();
        foreach (var model in SubKeys(ctx, KeyPath))
        foreach (var instance in SubKeys(ctx, $@"{KeyPath}\{model}"))
        {
            var instanceKeyPath = $@"{KeyPath}\{model}\{instance}";
            devices.Add(new UsbDeviceRecord(model,
                Stamp(ctx, instanceKeyPath, InstallDateSubPath),
                Stamp(ctx, instanceKeyPath, LastArrivalSubPath)));
        }
        return devices;
    }

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
            if (Stamp(ctx, $@"{KeyPath}\{model}\{instance}", InstallDateSubPath) is { } when
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

    /// ONE dated property of one instance, or nothing. Nothing is what a read
    /// that cannot be turned into a date returns: a refused key, a value that
    /// is not there or is not bytes, too few bytes to hold a FILETIME, and a
    /// FILETIME that will not convert — including zero, which converts
    /// happily into the first instant of the FILETIME epoch and would report
    /// a machine as having had a USB stick attached in 1601.
    ///
    /// The sub-path is a parameter because two properties in one store are
    /// read this way and both are FILETIMEs under the same empty value name.
    /// It was InstallDate, reading InstallDateSubPath alone; ReadDevices
    /// needs the last-arrival property read to exactly this standard, and a
    /// second copy of these five refusals is a second chance to leave one
    /// out.
    private static DateTime? Stamp(DiagnosticContext ctx, string instanceKeyPath,
        string subPath)
    {
        byte[]? stamp;
        try
        {
            stamp = ctx.Registry.GetBytes(
                $@"{instanceKeyPath}\{subPath}", InstallDateValueName);
        }
        catch (Exception) { return null; }

        if (stamp is null || stamp.Length < sizeof(long)) return null;
        var filetime = BitConverter.ToInt64(stamp, 0);
        if (filetime <= 0) return null;
        try { return DateTime.FromFileTimeUtc(filetime); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
