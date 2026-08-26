using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// Three of the four report-only disclosures: what Windows has RECORDED
/// about this machine, as a number, read out of the registry. Nothing here
/// can be fixed and nothing here is a switch — brisk counts what is already
/// written and says how it read. The fourth, delivery-optimization, reads a
/// counter through a cmdlet rather than a registry key and answers a zero
/// differently, so it has its own file rather than a fourth entry in the
/// theories below.
///
/// Two rules govern every assertion in this file.
///
/// NUMBERS, NEVER CONTENTS. A count may be reported; the thing counted may
/// not. UsbHistory_NeverSurfacesADeviceName and RunHistory_NeverSurfacesA
/// ProgramRecord_EncodedOrDecoded plant recognisable names in the two rules
/// that read names at all, and read every string a finding can put in front
/// of a user back out again.
///
/// AN UNREADABLE READ REPORTS UNREADABLE, NEVER ZERO. "no USB device
/// recorded" and "I could not read the USB record" are different claims, and
/// only the second is one brisk can make from an empty read: the probe
/// returns an empty list for a key that is not there and for a key with
/// nothing in it alike, so brisk cannot tell those apart and does not pick
/// one. AnEmptyRegistry_ReportsUnreadable_AndClaimsNoNumber is that rule.
public class PrivacyDisclosureRuleTests
{
    private static readonly string[] Ids =
    {
        "usb-history", "run-history", "recall-status",
    };

    public static TheoryData<string> AllDisclosures()
    {
        var data = new TheoryData<string>();
        foreach (var id in Ids) data.Add(id);
        return data;
    }

    private static PrivacyDisclosureRule Rule(string id) => id switch
    {
        "usb-history" => new UsbHistoryRule(),
        "run-history" => new RunHistoryRule(),
        "recall-status" => new RecallStatusRule(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(id), id, "not one of this wave's report-only disclosures"),
    };

    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    /// EVERY state a rule can report, one plant each, ending with the empty
    /// registry. The two resx theories walk this rather than one readable
    /// reading and one empty one: a rule that names a different key per state
    /// — and all three do — would otherwise have some of its keys checked
    /// against the resx files and the rest checked against nothing. That is
    /// how rule.recall-status.*.on and rule.usb-history.evidence.no-date sat
    /// unguarded for a round.
    private static IReadOnlyList<Action<FakeRegistry>> EveryStateItCanReport(string id) =>
        id switch
        {
            "usb-history" => new Action<FakeRegistry>[]
            {
                reg => PlantSomethingReadable(reg, id),
                // counted, with no date brisk could read
                reg => PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa"),
                _ => { },
            },
            "run-history" => new Action<FakeRegistry>[]
            {
                reg => PlantSomethingReadable(reg, id),
                _ => { },
            },
            "recall-status" => new Action<FakeRegistry>[]
            {
                reg => PlantSomethingReadable(reg, id),
                reg => reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, 0),
                _ => { },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no plants for this id"),
        };

    /// The reading each rule needs before it has anything to report. Kept in
    /// one place so the shape theories below run over a registry that answers
    /// rather than over the empty one, which has its own theory.
    private static void PlantSomethingReadable(FakeRegistry reg, string id)
    {
        switch (id)
        {
            case "usb-history":
                PlantUsbDevice(reg, "Ven_Kingston&Prod_DataTraveler", "0123456789ABCD",
                    new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc));
                break;
            case "run-history":
                reg.SetInt(RunHistoryRule.CountKeyPaths[0], "Fgrnz.rkr", 1);
                break;
            case "recall-status":
                reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "no plant for this id");
        }
    }

    /// One USB storage device the way Windows records one: the model under
    /// the enum root, the instance under the model, and the dates in the
    /// device property store below the instance, each as a Windows FILETIME.
    ///
    /// Each date is planted only when a caller asks for one, and the two are
    /// asked for separately, because a record carrying one and not the other
    /// is a state a real property store reaches — and it is the state
    /// ReadDevices has to render without inventing the missing half.
    private static void PlantUsbDevice(FakeRegistry reg, string model, string instance,
        DateTime? installedUtc = null, DateTime? lastArrivalUtc = null)
    {
        Sub(reg, UsbHistoryRule.KeyPath, model);
        Sub(reg, $@"{UsbHistoryRule.KeyPath}\{model}", instance);
        var instanceKey = $@"{UsbHistoryRule.KeyPath}\{model}\{instance}";
        if (installedUtc is not null)
            reg.SetBytes(
                $@"{instanceKey}\{UsbHistoryRule.InstallDateSubPath}",
                UsbHistoryRule.InstallDateValueName,
                BitConverter.GetBytes(installedUtc.Value.ToFileTimeUtc()));
        if (lastArrivalUtc is not null)
            reg.SetBytes(
                $@"{instanceKey}\{UsbHistoryRule.LastArrivalSubPath}",
                UsbHistoryRule.InstallDateValueName,
                BitConverter.GetBytes(lastArrivalUtc.Value.ToFileTimeUtc()));
    }

    private static void Sub(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var children))
            reg.SubKeys[parent] = children = new List<string>();
        if (!children.Contains(child)) children.Add(child);
    }

    /// Every string a finding can put in front of a user: the engine's own
    /// prose, and every argument a GUI substitutes into a localized template.
    /// An argument is as visible as the sentence it lands in, so a name
    /// smuggled through EvidenceArgs would reach the screen exactly as if it
    /// had been written into the evidence.
    private static IEnumerable<string> EverythingAReaderWouldSee(DiagnosticFinding f)
    {
        yield return f.Title;
        yield return f.Evidence;
        foreach (var arg in f.EvidenceArgs ?? Array.Empty<string>()) yield return arg;
        if (f.Headline is not { } h) yield break;
        yield return h.Value;
        yield return h.Caption;
        foreach (var arg in h.ValueArgs) yield return arg;
        foreach (var arg in h.CaptionArgs) yield return arg;
    }

    /// The number a finding leads with, or a phrase saying there was none.
    /// Read through this rather than off Headline directly: a rule that
    /// reported nothing would otherwise fail these with a null reference and
    /// no word about what it did instead.
    private static string Counted(DiagnosticFinding finding) =>
        finding.Headline?.Value ?? "no count at all";

    // ---- the shape the three share -------------------------------------

    /// Report-only, and the consent level says so: Advise is the level
    /// FixRunner refuses to apply a fix for at all, which is what makes
    /// "brisk shows the number and nothing else" a property of the build
    /// rather than of the copy.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void EachDisclosure_IsAdviseAndCannotBeFixed(string id)
    {
        var (ctx, reg) = Context();
        PlantSomethingReadable(reg, id);
        var rule = Rule(id);
        var finding = rule.Detect(ctx);

        Assert.True(rule.Category == RuleCategory.Advise,
            $"{id} ships as {rule.Category}; a report-only disclosure is Advise");
        Assert.NotNull(finding);
        Assert.False(finding!.CanFix, $"{id} reports a record brisk offers no fix for");
        Assert.Null(finding.FixDescription);
        Assert.Equal(rule.Category, finding.Category);
    }

    /// Privacy is a second axis: brisk shows it and never grades it. The
    /// switches assert the same thing in TelemetrySwitchRuleTests; this is
    /// the report-only half of the wave's one health-score rule.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void TheFinding_IsANotice_AndCostsTheHealthScoreNothing(string id)
    {
        var (ctx, reg) = Context();
        PlantSomethingReadable(reg, id);
        var finding = Rule(id).Detect(ctx)!;

        Assert.True(finding.Kind == FindingKind.Notice,
            $"{id} ships as {finding.Kind}; every finding in this wave is a Notice");
        Assert.True(HealthScore.Compute(new[] { finding }) == 100,
            $"{id} moved the health score to {HealthScore.Compute(new[] { finding })}");
    }

    /// The impact scale measures expected PERFORMANCE impact, and a record
    /// Windows keeps costs none. One rather than zero because the field is
    /// documented 1..5 and a surface reusing the finding row renders a meter
    /// over whatever number it is given.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void TheFinding_ClaimsNoPerformanceImpact(string id)
    {
        var (ctx, reg) = Context();
        PlantSomethingReadable(reg, id);
        var finding = Rule(id).Detect(ctx)!;

        Assert.True(finding.ImpactStars == 1,
            $"{id} claims {finding.ImpactStars} stars of performance impact; " +
            "a record brisk only counts has none, and 1 is the floor of the " +
            "documented 1..5 scale");
        Assert.True(finding.Severity == Severity.Info,
            $"{id} ships as {finding.Severity}");
    }

    /// A rule brisk never runs is a rule that never fires.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void EachDisclosure_IsRegisteredExactlyOnce(string id)
    {
        Assert.True(DiagnosticRuleRegistry.All.Count(r => r.Id == id) == 1,
            $"'{id}' appears {DiagnosticRuleRegistry.All.Count(r => r.Id == id)} times " +
            "in DiagnosticRuleRegistry.All");
    }

    /// What makes these three unlike the six switches: they lead with a
    /// number, so they carry a Headline, and a Headline is what a surface
    /// ranking measured numbers picks a finding up by.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void TheReadableFinding_LeadsWithItsOwnHeadlineKeys(string id)
    {
        var (ctx, reg) = Context();
        PlantSomethingReadable(reg, id);
        var finding = Rule(id).Detect(ctx)!;

        Assert.Equal(id, finding.RuleId);
        Assert.True(finding.Headline is not null,
            $"{id} read its record and led with no headline");
        Assert.StartsWith($"rule.{id}.headline.value", finding.Headline!.ValueKey,
            StringComparison.Ordinal);
        Assert.StartsWith($"rule.{id}.headline.caption", finding.Headline.CaptionKey,
            StringComparison.Ordinal);
        Assert.StartsWith($"rule.{id}.title", finding.TitleKey, StringComparison.Ordinal);
        Assert.StartsWith($"rule.{id}.evidence", finding.EvidenceKey!,
            StringComparison.Ordinal);
    }

    // ---- unreadable, never zero ----------------------------------------

    /// The rule this task exists to hold. A registry that answers nothing is
    /// not a machine with nothing on it, and the finding may not read as one:
    /// it states no number at all, and it carries no headline — a headline is
    /// what a finding leads with, and a read that came back with nothing
    /// gives it nothing to lead with.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void AnEmptyRegistry_ReportsUnreadable_AndClaimsNoNumber(string id)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx);

        Assert.NotNull(finding);
        Assert.True(finding!.Headline is null,
            $"{id} read nothing and still led with the headline " +
            $"\"{finding.Headline?.Value}\", which is a reading it did not get");
        var digits = string.Concat(EverythingAReaderWouldSee(finding).SelectMany(
            s => s.Where(char.IsDigit)));
        Assert.True(digits.Length == 0,
            $"{id} read nothing and still put the digits \"{digits}\" in front of " +
            "the user; an unreadable record is reported as unreadable, never as a count");
    }

    /// The same rule stated the way the brief states it, on the one id whose
    /// zero would be the most believable lie.
    [Fact]
    public void UsbHistory_WhenTheKeyCannotBeRead_DoesNotClaimZeroDevices()
    {
        var finding = new UsbHistoryRule().Detect(TestContext.Empty());
        Assert.True(finding is null || !finding.Evidence.Contains("0 ", StringComparison.Ordinal),
            "an unreadable USB record must not be reported as zero devices");
    }

    // ---- numbers, never contents ---------------------------------------

    /// The wave's second red line, per rule, with names planted that a leak
    /// would be unmistakable about. run-history's names are ROT13 as Windows
    /// stores them, and BOTH forms are checked: the encoded name because it
    /// is what the registry holds, and the plain one because decoding it is
    /// the one way this rule could produce the contents the spec forbids.
    /// brisk does not decode them — the count is the whole finding.
    [Fact]
    public void UsbHistory_NeverSurfacesADeviceName()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_SanDisk&Prod_Cruzer&Rev_1.00", "4C530001120716117362",
            new DateTime(2019, 7, 16, 0, 0, 0, DateTimeKind.Utc));
        var finding = new UsbHistoryRule().Detect(ctx)!;

        foreach (var fragment in new[]
                 { "SanDisk", "Cruzer", "Ven_", "4C530001120716117362" })
            Assert.DoesNotContain(fragment,
                string.Join(" | ", EverythingAReaderWouldSee(finding)),
                StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunHistory_NeverSurfacesAProgramRecord_EncodedOrDecoded()
    {
        var (ctx, reg) = Context();
        // ROT13 of "Steam.exe", which is how UserAssist stores such a name.
        reg.SetInt(RunHistoryRule.CountKeyPaths[0], "Fgrnz.rkr", 4);
        reg.SetInt(RunHistoryRule.CountKeyPaths[1], "Puebzr.yax", 9);
        var finding = new RunHistoryRule().Detect(ctx)!;

        foreach (var fragment in new[]
                 { "Fgrnz", "Steam", "Puebzr", "Chrome", ".rkr" })
            Assert.DoesNotContain(fragment,
                string.Join(" | ", EverythingAReaderWouldSee(finding)),
                StringComparison.OrdinalIgnoreCase);
    }

    // ---- usb-history ---------------------------------------------------

    /// Instances, not models. One model with three instances is three
    /// devices; a rule that counted the models would say one, and a rule
    /// that counted both would say four.
    [Fact]
    public void UsbHistory_CountsInstances_NotTheModelsAboveThem()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa");
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "bbb");
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "ccc");
        PlantUsbDevice(reg, "Ven_B&Prod_Disk", "ddd");
        var finding = new UsbHistoryRule().Detect(ctx)!;

        Assert.True(Counted(finding) == "4",
            $"four instances under two models were counted as {Counted(finding)}");
    }

    /// How far back, and the word is EARLIEST: the record's age is the age of
    /// its oldest entry, not of whichever one the enumeration happened to
    /// reach first.
    [Fact]
    public void UsbHistory_ReportsTheEarliestInstallDateItCouldRead()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "newer",
            new DateTime(2024, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "older",
            new DateTime(2017, 5, 9, 8, 30, 0, DateTimeKind.Utc));
        var finding = new UsbHistoryRule().Detect(ctx)!;

        Assert.True(finding.Evidence.Contains("2017-05-09", StringComparison.Ordinal),
            $"the oldest planted install date was 2017-05-09 and the evidence says: {finding.Evidence}");
        Assert.DoesNotContain("2024-11-02", finding.Evidence, StringComparison.Ordinal);
    }

    /// The brief's split case, and the one an "if in doubt, guess" rule would
    /// get wrong: the count is readable and the date is not, so the count is
    /// reported and the date is admitted as unread. No date is invented, and
    /// the count is not thrown away with it.
    [Fact]
    public void UsbHistory_WithNoReadableDate_ReportsTheCountAndSaysTheDateWentUnread()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa");
        PlantUsbDevice(reg, "Ven_B&Prod_Disk", "bbb");
        var finding = new UsbHistoryRule().Detect(ctx)!;

        Assert.True(Counted(finding) == "2",
            $"two devices with no readable date were counted as {Counted(finding)}");
        Assert.True(finding.EvidenceKey == "rule.usb-history.evidence.no-date",
            $"the evidence key is {finding.EvidenceKey}, which is the sentence that " +
            "carries a date brisk never read");
        Assert.DoesNotContain("-", string.Join(" ", finding.EvidenceArgs!));
    }

    /// A stamp brisk cannot make sense of is not a date. Too few bytes to be
    /// a FILETIME, and a FILETIME of zero, are both left unread rather than
    /// turned into the beginning of the Windows epoch.
    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void UsbHistory_AStampItCannotReadIsNotADate(byte[] stamp)
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa");
        reg.SetBytes(
            $@"{UsbHistoryRule.KeyPath}\Ven_A&Prod_Stick\aaa\{UsbHistoryRule.InstallDateSubPath}",
            UsbHistoryRule.InstallDateValueName, stamp);
        var finding = new UsbHistoryRule().Detect(ctx)!;

        Assert.True(finding.EvidenceKey == "rule.usb-history.evidence.no-date",
            $"a {stamp.Length}-byte stamp was read as a date: {finding.Evidence}");
        Assert.DoesNotContain("1601", finding.Evidence, StringComparison.Ordinal);
    }

    /// The device property store carries its own ACL, and on a real machine
    /// a read of it can be refused outright. The count sits ABOVE that read,
    /// so a refusal costs the date and must not cost the count — and it must
    /// not reach EngineHost's catch-all, which would drop the whole finding.
    [Fact]
    public void UsbHistory_ADatePropertyItIsRefused_CostsTheDateAndNotTheCount()
    {
        var reg = new FakeRegistry();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa");
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "bbb");
        var ctx = TestContext.Empty() with { Registry = new RefusesPropertyReads(reg) };
        var finding = new UsbHistoryRule().Detect(ctx)!;

        Assert.True(Counted(finding) == "2",
            $"a refused property read left the count reading {Counted(finding)}");
        Assert.Equal("rule.usb-history.evidence.no-date", finding.EvidenceKey);
    }

    // ---- usb-history: the records themselves ---------------------------

    /// WHAT DETECT THROWS AWAY, read for the one surface allowed to see it.
    /// Detect counts instances and reports a number; this returns the model
    /// name and the two dates behind each of those instances, for the
    /// Gizlilik page — the owner's own screen, and nowhere else. That is the
    /// spec's red line 2 as amended on 2026-08-26.
    ///
    /// ONE RECORD PER INSTANCE, which is what the count counts: two sticks of
    /// one model are two records carrying that model's name twice, exactly as
    /// Detect says two. The model is the MODEL-level subkey name; the
    /// instance id below it is read to build the key path and goes nowhere,
    /// the same way Detect treats it.
    [Fact]
    public void ReadDevices_ReturnsOneRecordPerInstance_CarryingItsModelAndBothDates()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_Kingston&Prod_DataTraveler", "0123456789ABCD",
            new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            new DateTime(2026, 8, 20, 9, 30, 0, DateTimeKind.Utc));
        PlantUsbDevice(reg, "Ven_Kingston&Prod_DataTraveler", "SECONDSTICK");

        var devices = UsbHistoryRule.ReadDevices(ctx);

        Assert.Equal(
            new[] { "Ven_Kingston&Prod_DataTraveler", "Ven_Kingston&Prod_DataTraveler" },
            devices.Select(d => d.Model));
        var dated = devices.Single(d => d.FirstSeen is not null);
        Assert.Equal(new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc), dated.FirstSeen);
        Assert.Equal(new DateTime(2026, 8, 20, 9, 30, 0, DateTimeKind.Utc), dated.LastSeen);
        // The second instance carries no property store at all, and neither
        // date is borrowed from the first one's.
        var undated = devices.Single(d => d.FirstSeen is null);
        Assert.Null(undated.LastSeen);
    }

    /// The two dates are read SEPARATELY, from two properties. A read that
    /// took one stamp and filled both fields with it would pass the test
    /// above and print a last-seen date brisk never read.
    [Fact]
    public void ReadDevices_ADeviceWithOnlyAnInstallDate_ClaimsNoLastArrival()
    {
        var (ctx, reg) = Context();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa",
            installedUtc: new DateTime(2017, 5, 9, 8, 30, 0, DateTimeKind.Utc));

        var device = Assert.Single(UsbHistoryRule.ReadDevices(ctx));

        Assert.Equal(new DateTime(2017, 5, 9, 8, 30, 0, DateTimeKind.Utc), device.FirstSeen);
        Assert.True(device.LastSeen is null,
            $"nothing was written at {UsbHistoryRule.LastArrivalSubPath} and brisk " +
            $"read a last-arrival date of {device.LastSeen} out of it");
    }

    /// The refusal Detect survives by losing the date and keeping the count,
    /// on the read that has a record to lose instead. A refused property key
    /// costs that instance its two DATES; it never costs the record, and it
    /// never costs the list — the model name is what the page exists to show,
    /// and it is read a level above the ACL that refuses.
    [Fact]
    public void ReadDevices_ADatePropertyItIsRefused_CostsTheDatesAndNotTheRecord()
    {
        var reg = new FakeRegistry();
        PlantUsbDevice(reg, "Ven_A&Prod_Stick", "aaa",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        PlantUsbDevice(reg, "Ven_B&Prod_Disk", "bbb");
        var ctx = TestContext.Empty() with { Registry = new RefusesPropertyReads(reg) };

        var devices = UsbHistoryRule.ReadDevices(ctx);

        Assert.Equal(new[] { "Ven_A&Prod_Stick", "Ven_B&Prod_Disk" },
            devices.Select(d => d.Model));
        Assert.All(devices, device =>
        {
            Assert.Null(device.FirstSeen);
            Assert.Null(device.LastSeen);
        });
    }

    /// A key the process is not allowed to open throws rather than answering
    /// empty — Registry.OpenSubKey raises SecurityException for that — and no
    /// fake in this suite does it, so the one that must is written here.
    private sealed class RefusesPropertyReads : IRegistryProbe
    {
        private readonly FakeRegistry _inner;
        public RefusesPropertyReads(FakeRegistry inner) { _inner = inner; }

        private static void Refuse(string keyPath)
        {
            if (keyPath.Contains(@"\Properties\", StringComparison.OrdinalIgnoreCase))
                throw new System.Security.SecurityException($"denied: {keyPath}");
        }

        public byte[]? GetBytes(string k, string v) { Refuse(k); return _inner.GetBytes(k, v); }
        public IReadOnlyList<string> GetSubKeyNames(string k) { Refuse(k); return _inner.GetSubKeyNames(k); }
        public IReadOnlyList<string> GetValueNames(string k) { Refuse(k); return _inner.GetValueNames(k); }
        public string? GetString(string k, string v) { Refuse(k); return _inner.GetString(k, v); }
        public int? GetInt(string k, string v) { Refuse(k); return _inner.GetInt(k, v); }
        public void SetString(string k, string v, string value) => _inner.SetString(k, v, value);
        public void SetBytes(string k, string v, byte[] value) => _inner.SetBytes(k, v, value);
        public void SetInt(string k, string v, int value) => _inner.SetInt(k, v, value);
        public void DeleteValue(string k, string v) => _inner.DeleteValue(k, v);
    }

    // ---- run-history ---------------------------------------------------

    /// Windows keeps this record under two keys and the count is of both. A
    /// rule that read one of them would under-report by however much sits in
    /// the other, and would do it silently.
    [Fact]
    public void RunHistory_CountsTheEntriesUnderBothKeys()
    {
        var (ctx, reg) = Context();
        reg.SetInt(RunHistoryRule.CountKeyPaths[0], "Nnn", 1);
        reg.SetInt(RunHistoryRule.CountKeyPaths[0], "Ooo", 1);
        reg.SetInt(RunHistoryRule.CountKeyPaths[1], "Ppp", 1);
        var finding = new RunHistoryRule().Detect(ctx)!;

        Assert.True(Counted(finding) == "3",
            "two entries under the first key and one under the second were " +
            $"counted as {Counted(finding)}");
    }

    /// One key readable and the other empty is still a readable record: the
    /// count is what brisk could count, and the finding says so with a number
    /// rather than falling back to the unreadable sentence.
    [Fact]
    public void RunHistory_OneKeyWithEntries_IsStillACount()
    {
        var (ctx, reg) = Context();
        reg.SetInt(RunHistoryRule.CountKeyPaths[1], "Nnn", 1);
        var finding = new RunHistoryRule().Detect(ctx)!;

        Assert.True(Counted(finding) == "1",
            $"one entry under the second key alone was counted as {Counted(finding)}");
    }

    // ---- recall-status -------------------------------------------------

    /// The policy set to switch it off. This is the one state brisk reports
    /// as off, and it reports it about the POLICY it read.
    [Fact]
    public void RecallStatus_ThePolicySetToSwitchItOff_ReadsAsOffByPolicy()
    {
        var (ctx, reg) = Context();
        reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, 1);
        var finding = new RecallStatusRule().Detect(ctx)!;

        Assert.Equal("rule.recall-status.title.off", finding.TitleKey);
        Assert.Equal("rule.recall-status.evidence.off", finding.EvidenceKey);
        Assert.Equal("rule.recall-status.headline.value.off", finding.Headline!.ValueKey);
    }

    [Fact]
    public void RecallStatus_ThePolicySetToLeaveItOn_IsNotReportedAsOff()
    {
        var (ctx, reg) = Context();
        reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, 0);
        var finding = new RecallStatusRule().Detect(ctx)!;

        Assert.Equal("rule.recall-status.title.on", finding.TitleKey);
        Assert.Equal("rule.recall-status.evidence.on", finding.EvidenceKey);
        Assert.Equal("rule.recall-status.headline.value.on", finding.Headline!.ValueKey);
    }

    /// The state the whole rule is shaped around. Recall's policy surface is
    /// new and differs between builds, so on most machines there is nothing
    /// at that value — and "brisk could not establish this" is a real answer
    /// that must never be rounded down to "it is off". What this theory
    /// plants, which is a sample of that arm and not the whole of it: nothing
    /// written; a value of a type brisk cannot read as a number, which the
    /// real probe returns null for exactly as the fake does; and a number
    /// brisk has no reading for.
    [Theory]
    [InlineData(null)]
    [InlineData("switched-off")]
    [InlineData(7)]
    public void RecallStatus_WhatItCannotRead_IsNeverReportedAsOff(object? planted)
    {
        var (ctx, reg) = Context();
        if (planted is int number)
            reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, number);
        else if (planted is string word)
            reg.SetString(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, word);
        var finding = new RecallStatusRule().Detect(ctx)!;

        Assert.True(finding.TitleKey == "rule.recall-status.title.unread",
            $"the policy read as {planted?.ToString() ?? "absent"} and brisk " +
            $"titled the finding {finding.TitleKey}");
        Assert.Equal("rule.recall-status.evidence.unread", finding.EvidenceKey);
        Assert.True(finding.Headline is null,
            "brisk could not establish the state and still led with a headline");
    }

    // ---- the surfaces, and both languages -------------------------------

    /// The paths, as literals, asserted one at a time. A tuple or array
    /// comparison over strings this long elides their middles and prints two
    /// lines that read identically, which is the one thing a path test must
    /// not do.
    [Fact]
    public void TheRegistrySurfaces_AreTheOnesTheSpecNames()
    {
        foreach (var (what, expected, actual) in new[]
                 {
                     ("the USB storage enum root",
                         @"HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR",
                         UsbHistoryRule.KeyPath),
                     ("the install-date property below an instance",
                         @"Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0064",
                         UsbHistoryRule.InstallDateSubPath),
                     ("the last-arrival property below an instance",
                         @"Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0066",
                         UsbHistoryRule.LastArrivalSubPath),
                     ("the first UserAssist count key",
                         @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer" +
                         @"\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count",
                         RunHistoryRule.CountKeyPaths[0]),
                     ("the second UserAssist count key",
                         @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer" +
                         @"\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count",
                         RunHistoryRule.CountKeyPaths[1]),
                     ("the Recall policy key",
                         @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
                         RecallStatusRule.KeyPath),
                     ("the Recall policy value",
                         "DisableAIDataAnalysis", RecallStatusRule.ValueName),
                 })
            Assert.True(expected == actual, $"{what} is \"{actual}\", not \"{expected}\"");
    }

    /// One claim, two sources: the engine ships English prose the CLI prints
    /// verbatim, and a resx key the GUI renders instead. Over every state the
    /// rule can report, including the unreadable one — a machine that lands
    /// there is a machine whose only sentence is that one, and it is the
    /// sentence this task exists to get right.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void TheEnglishResx_SaysWhatTheEngineSays(string id)
    {
        var en = Resx("Strings.resx");
        foreach (var plant in EveryStateItCanReport(id))
        {
            var (ctx, reg) = Context();
            plant(reg);
            var finding = Rule(id).Detect(ctx)!;

            Assert.True(en.TryGetValue(finding.TitleKey, out var title),
                $"{finding.TitleKey} is missing from Strings.resx");
            Assert.Equal(finding.Title, title);

            Assert.True(en.TryGetValue(finding.EvidenceKey!, out var evidence),
                $"{finding.EvidenceKey} is missing from Strings.resx");
            Assert.Equal(finding.Evidence, string.Format(CultureInfo.InvariantCulture,
                evidence!, (finding.EvidenceArgs ?? Array.Empty<string>()).ToArray<object>()));
        }
    }

    /// Every key these three rules name has to exist in BOTH files. LocTests
    /// holds the two key sets equal to each other; nothing there knows which
    /// keys a rule actually asks for, so a rule naming a key that is in
    /// neither file would leave both sets equal and both readers looking at a
    /// raw key string.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void EveryKeyTheRuleNames_IsInBothLanguages(string id)
    {
        var files = new[] { "Strings.resx", "Strings.tr.resx" }
            .ToDictionary(f => f, Resx);

        foreach (var plant in EveryStateItCanReport(id))
        {
            var (ctx, reg) = Context();
            plant(reg);
            var finding = Rule(id).Detect(ctx)!;

            var keys = new List<string> { finding.TitleKey, finding.EvidenceKey!, $"rule.{id}.advice" };
            if (finding.Headline is { } h)
            {
                keys.Add(h.ValueKey);
                keys.Add(h.CaptionKey);
            }
            foreach (var (file, strings) in files)
            foreach (var key in keys)
                Assert.True(strings.ContainsKey(key), $"{key} is missing from {file}");
        }
    }

    /// The red line the spec makes a test rather than a comment: brisk says
    /// nothing about what anybody receives. Read off disk over every string
    /// these three rules ship, in both languages, because a claim only one
    /// language makes is still a claim brisk made.
    [Theory]
    [MemberData(nameof(AllDisclosures))]
    public void NoDisclosureCopy_ClaimsAnythingAboutWhoReceivesWhat(string id)
    {
        string[] forbidden =
        {
            "Microsoft", "sends", "sent", "sees", "receives", "collect",
            "gönderi", "gidiyor", "görüyor", "topluyor",
        };

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        foreach (var (key, text) in Resx(file))
        {
            if (!key.StartsWith($"rule.{id}.", StringComparison.Ordinal)) continue;
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{key} in {file} says \"{word}\" — brisk reads this machine and " +
                    "makes no claim about what leaves it or who receives it");
        }
    }

    /// internal rather than private: DeliveryOptimizationRuleTests reads the
    /// same two files for the same reason, and one reader means one place to
    /// fix when the resx files move.
    internal static Dictionary<string, string> Resx(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return XDocument
                    .Load(Path.Combine(dir.FullName, "src", "Brisk", "Localization",
                        fileName)).Root!
                    .Elements("data")
                    .ToDictionary(e => (string)e.Attribute("name")!,
                        e => (string)e.Element("value")!);
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
