using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// Which apps used the microphone and the camera in the last 30 days, by
/// Windows' own LastUsedTimeStart/Stop stamps. Numbers on the finding, names
/// on the snapshot list the Privacy page reads, and nowhere else.
public class CapabilityUseRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    private static CapabilityUseRule Rule() => new(() => Now);

    /// The root Value is what a real machine always carries; planting it is
    /// how a test says "the record exists" without planting an app.
    private static void Store(FakeRegistry reg, string key)
    {
        reg.SetString(key, "Value", "Allow");
        if (!reg.SubKeys.ContainsKey(key)) reg.SubKeys[key] = new List<string>();
    }

    private static void Sub(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var kids)) reg.SubKeys[parent] = kids = new List<string>();
        if (!kids.Contains(child)) kids.Add(child);
    }

    private static void Packaged(FakeRegistry reg, string key, string family, DateTime? lastUsedUtc)
    {
        Store(reg, key);
        Sub(reg, key, family);
        if (lastUsedUtc is { } t)
            reg.SetLong($@"{key}\{family}", CapabilityUseRule.LastUsedStopValueName, t.ToFileTimeUtc());
    }

    private static void Desktop(FakeRegistry reg, string key, string exePath, DateTime? lastUsedUtc)
    {
        Store(reg, key);
        Sub(reg, key, CapabilityUseRule.NonPackaged);
        var hashed = exePath.Replace('\\', '#');
        Sub(reg, $@"{key}\{CapabilityUseRule.NonPackaged}", hashed);
        if (lastUsedUtc is { } t)
            reg.SetLong($@"{key}\{CapabilityUseRule.NonPackaged}\{hashed}",
                CapabilityUseRule.LastUsedStopValueName, t.ToFileTimeUtc());
    }

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

    [Fact]
    public void CountsAppsInsideTheWindow_AndLeadsWithTheLargerCount()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Program Files\Google\Chrome\Application\chrome.exe", Now.AddDays(-25));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Users\U\AppData\Local\WisprFlow\app-1.6.447\Wispr Flow.exe", Now.AddDays(-25));
        Packaged(reg, CapabilityUseRule.WebcamKey, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", Now.AddDays(-4));

        var finding = Rule().Detect(ctx)!;

        Assert.Equal(CapabilityUseRule.RuleId, finding.RuleId);
        Assert.Equal(new[] { "2", "1" }, finding.EvidenceArgs);
        Assert.Equal("2", finding.Headline!.Value);
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.headline.caption.mic", finding.Headline.CaptionKey);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public void TheWindowIsThirtyDays(int daysAgo, bool counted)
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-daysAgo));
        Store(reg, CapabilityUseRule.WebcamKey);

        var finding = Rule().Detect(ctx);
        Assert.Equal(counted, finding is not null);
    }

    [Fact]
    public void OneAppInstalledInManyVersions_CountsOnce_WithItsNewestDate()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9241\Discord.exe", Now.AddDays(-20));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9244\Discord.exe", Now.AddDays(-3));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9200\Discord.exe", Now.AddDays(-29));
        Store(reg, CapabilityUseRule.WebcamKey);

        var records = CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!;
        var one = Assert.Single(records);
        Assert.Equal("Discord.exe", one.AppName);
        Assert.Equal(Now.AddDays(-3), one.LastUsedUtc);
        Assert.Equal(@"C:\U\Discord\app-1.0.9244\Discord.exe", one.Path);
    }

    [Fact]
    public void APackagedApp_IsNamedByItsFamilyName_WithoutThePublisherHash()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.WebcamKey, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", Now.AddDays(-1));
        var one = Assert.Single(CapabilityUseRule.ReadRecent(ctx, Capability.Camera, Now)!);
        Assert.Equal("5319275A.WhatsAppDesktop", one.AppName);
        Assert.Null(one.Path);
    }

    /// BOTH stamps zero or absent is what "never used" looks like. Zero on
    /// Stop alone is not — see the theory below it.
    [Fact]
    public void AZeroOrAbsentStamp_IsNeverUsed()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Never.Used_x", null);
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Zero.Stamp_x", null);
        reg.SetLong($@"{CapabilityUseRule.MicrophoneKey}\Zero.Stamp_x",
            CapabilityUseRule.LastUsedStopValueName, 0);
        reg.SetLong($@"{CapabilityUseRule.MicrophoneKey}\Zero.Stamp_x",
            CapabilityUseRule.LastUsedStartValueName, 0);
        Store(reg, CapabilityUseRule.WebcamKey);

        Assert.Empty(CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!);
        Assert.Null(Rule().Detect(ctx));
    }

    /// A SESSION THAT WAS NEVER CLOSED. Windows writes LastUsedTimeStart when
    /// an app acquires the device and LastUsedTimeStop when it releases it,
    /// so Stop stays 0 while the device is still held — what Settings shows
    /// as "currently in use" — and after an app crashed without releasing
    /// one. Reading Stop alone drops exactly the entry this rule exists to
    /// surface, so the use is counted at its START, and the window is applied
    /// to that same measured stamp rather than to the moment the test runs.
    ///
    /// brisk does not call it "in use now": nothing here measured that, and
    /// an open session and a crash remnant read identically from here.
    [Theory]
    [InlineData(2, true)]
    [InlineData(40, false)]
    public void ASessionLeftOpen_IsCountedAtItsStart_AndStillObeysTheWindow(
        int daysAgo, bool counted)
    {
        var (ctx, reg) = Context();
        var key = $@"{CapabilityUseRule.MicrophoneKey}\Held.App_x";
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Held.App_x", null);
        reg.SetLong(key, CapabilityUseRule.LastUsedStopValueName, 0);
        reg.SetLong(key, CapabilityUseRule.LastUsedStartValueName,
            Now.AddDays(-daysAgo).ToFileTimeUtc());
        Store(reg, CapabilityUseRule.WebcamKey);

        var records = CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!;
        if (!counted)
        {
            Assert.Empty(records);
            return;
        }
        var one = Assert.Single(records);
        Assert.Equal("Held.App", one.AppName);
        Assert.Equal(Now.AddDays(-daysAgo), one.LastUsedUtc);
    }

    /// A number in the value that is not a FILETIME brisk can convert is
    /// skipped rather than guessed at, on either stamp — and it is skipped
    /// quietly, because an ArgumentOutOfRangeException escaping the walk
    /// would cost the whole capability rather than one entry.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStampThatWillNotConvert_IsSkipped_NotGuessedAt(bool onStop)
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Bad.Stamp_x", null);
        reg.SetLong($@"{CapabilityUseRule.MicrophoneKey}\Bad.Stamp_x",
            onStop ? CapabilityUseRule.LastUsedStopValueName
                   : CapabilityUseRule.LastUsedStartValueName,
            long.MaxValue);
        Store(reg, CapabilityUseRule.WebcamKey);

        Assert.Empty(CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!);
    }

    [Fact]
    public void BothRecordsUnreadable_ReportsUnreadable_WithNoDigits()
    {
        var (ctx, _) = Context();
        var finding = Rule().Detect(ctx)!;

        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Null(finding.Headline);
        var digits = string.Concat(EverythingAReaderWouldSee(finding).SelectMany(s => s.Where(char.IsDigit)));
        Assert.True(digits.Length == 0, $"unreadable finding carries digits: {digits}");
    }

    [Fact]
    public void OneRecordUnreadable_ReportsTheOther_AndSaysWhichWasNotRead()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
        // no webcam key at all

        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.evidence.cam-unread", finding.EvidenceKey);
        Assert.Equal(new[] { "1" }, finding.EvidenceArgs);
        Assert.Equal("1", finding.Headline!.Value);
    }

    /// ONE HALF UNREADABLE AND THE OTHER COUNTING NOTHING. The only reading
    /// that arrived is a zero, so nothing here USED anything — and a finding
    /// titled "Apps have used the microphone or camera recently" over that,
    /// pointing at a fold with nothing in it, is a claim brisk did not
    /// measure. It takes the unreadable title and no headline, which is also
    /// what puts it in the page's "what brisk could not read" band; the
    /// evidence stays the half-unread sentence, which is the honest one — a
    /// measured 0 on the half brisk read and "could not be read" on the half
    /// it did not.
    ///
    /// The readable half holds an app whose last use is OUTSIDE the window,
    /// so the registry has a name in it that the count legitimately does not
    /// reach — which is what makes the name assertion say something.
    [Theory]
    [InlineData("mic-unread-zero", "mic-unread")]
    [InlineData("cam-unread-zero", "cam-unread")]
    public void OneRecordUnreadable_AndTheOtherCountingNothing_IsAnUnreadableFinding(
        string state, string evidence)
    {
        var finding = Detect(state);

        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.evidence.{evidence}",
            finding.EvidenceKey);
        Assert.Equal(new[] { "0" }, finding.EvidenceArgs);
        Assert.Null(finding.Headline);
        Assert.DoesNotContain("ZZZ", string.Join(" | ", EverythingAReaderWouldSee(finding))
            + " | " + JsonSerializer.Serialize(finding), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARegistryThatThrows_CostsThatRecordAndNotTheScan()
    {
        var reg = new FakeRegistry();
        var ctx = TestContext.Empty() with { Registry = new ThrowsOnWebcam(reg) };
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));

        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.evidence.cam-unread", finding.EvidenceKey);
    }

    [Fact]
    public void NoNameOrPath_ReachesAnyFindingString_OrTheJson()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Users\ZZZ-User\Desktop\ZZZ-Recorder.exe", Now.AddDays(-1));
        Packaged(reg, CapabilityUseRule.WebcamKey, "ZZZ.PackagedApp_abc123", Now.AddDays(-1));

        var finding = Rule().Detect(ctx)!;
        var everything = string.Join(" | ", EverythingAReaderWouldSee(finding))
            + " | " + JsonSerializer.Serialize(finding);
        foreach (var fragment in new[] { "ZZZ", "Recorder", "PackagedApp", "abc123", "Desktop" })
            Assert.DoesNotContain(fragment, everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadAllRecent_CarriesBothCapabilities_NewestFirst()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Old.App_x", Now.AddDays(-10));
        Packaged(reg, CapabilityUseRule.WebcamKey, "New.App_x", Now.AddDays(-1));

        var all = CapabilityUseRule.ReadAllRecent(ctx, Now);
        Assert.Equal(new[] { "New.App", "Old.App" }, all.Select(r => r.AppName));
        Assert.Equal(Capability.Camera, all[0].Capability);
    }

    [Fact]
    public void TheFinding_IsAdvise_Notice_Info_AndRegisteredOnce()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
        Store(reg, CapabilityUseRule.WebcamKey);
        var rule = Rule();
        var finding = rule.Detect(ctx)!;

        Assert.Equal(RuleCategory.Advise, rule.Category);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.False(finding.CanFix);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == CapabilityUseRule.RuleId));
    }

    // ---- one claim, two languages ---------------------------------------

    /// EVERY state this rule can report, one plant each. The two resx
    /// theories below walk this rather than one reading: the rule names a
    /// different evidence key per state, so a single plant would check one
    /// of its four sentences against the resx and the rest against nothing.
    /// That is the same reason PrivacyDisclosureRuleTests keeps
    /// EveryStateItCanReport beside its three rules.
    public static TheoryData<string> EveryStateItCanReport()
    {
        var data = new TheoryData<string>();
        foreach (var state in new[]
                 {
                     "both", "mic-unread", "cam-unread",
                     "mic-unread-zero", "cam-unread-zero", "unread",
                 })
            data.Add(state);
        return data;
    }

    /// The plant for each of those states. The two `-zero` states put an app
    /// OUTSIDE the window in the half brisk can read, so the readable count
    /// is an honest 0 over a record that does hold a name.
    private static DiagnosticFinding Detect(string state)
    {
        var (ctx, reg) = Context();
        switch (state)
        {
            case "both":
                Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
                Packaged(reg, CapabilityUseRule.WebcamKey, "B.App_x", Now.AddDays(-2));
                break;
            case "mic-unread":
                Packaged(reg, CapabilityUseRule.WebcamKey, "B.App_x", Now.AddDays(-2));
                break;
            case "cam-unread":
                Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
                break;
            case "mic-unread-zero":
                Packaged(reg, CapabilityUseRule.WebcamKey, "ZZZ.LongAgo_x", Now.AddDays(-40));
                break;
            case "cam-unread-zero":
                Packaged(reg, CapabilityUseRule.MicrophoneKey, "ZZZ.LongAgo_x", Now.AddDays(-40));
                break;
            case "unread":
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(state), state, "no plant for this state");
        }
        return Rule().Detect(ctx)!;
    }

    /// The engine ships English prose the CLI prints verbatim and a resx key
    /// the GUI renders instead, and the two have to say the same thing.
    [Theory]
    [MemberData(nameof(EveryStateItCanReport))]
    public void TheEnglishResx_SaysWhatTheEngineSays(string state)
    {
        var en = PrivacyDisclosureRuleTests.Resx("Strings.resx");
        var finding = Detect(state);

        Assert.True(en.TryGetValue(finding.TitleKey, out var title),
            $"{finding.TitleKey} is missing from Strings.resx");
        Assert.Equal(finding.Title, title);

        Assert.True(en.TryGetValue(finding.EvidenceKey!, out var evidence),
            $"{finding.EvidenceKey} is missing from Strings.resx");
        Assert.Equal(finding.Evidence, string.Format(CultureInfo.InvariantCulture,
            evidence!, (finding.EvidenceArgs ?? Array.Empty<string>()).ToArray<object>()));
    }

    /// LocTests holds the two key sets equal to each other; nothing there
    /// knows which keys a rule actually asks for, so a rule naming a key that
    /// is in neither file leaves both sets equal and both readers looking at
    /// a raw key string. The headline caption is the sharpest of them: it is
    /// rendered off the English fallback when the key is missing, so nothing
    /// else in the suite would notice.
    [Theory]
    [MemberData(nameof(EveryStateItCanReport))]
    public void EveryKeyTheRuleNames_IsInBothLanguages(string state)
    {
        var finding = Detect(state);
        var keys = new List<string>
        {
            finding.TitleKey, finding.EvidenceKey!,
            $"rule.{CapabilityUseRule.RuleId}.advice",
        };
        if (finding.Headline is { } h) { keys.Add(h.ValueKey); keys.Add(h.CaptionKey); }

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var strings = PrivacyDisclosureRuleTests.Resx(file);
            foreach (var key in keys)
                Assert.True(strings.ContainsKey(key), $"{key} is missing from {file}");
        }
    }

    /// The wave's first red line, read off disk in both languages. This rule
    /// reads when an app last opened the microphone or the camera and nothing
    /// about what it did with either, so the temptation to say more here is
    /// the largest in the topic.
    [Fact]
    public void NoCopy_ClaimsAnythingAboutWhoReceivesWhat()
    {
        string[] forbidden =
        {
            "Microsoft", "sends", "sent", "sees", "receives", "collect",
            "gönderi", "gidiyor", "görüyor", "topluyor",
        };

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        foreach (var (key, text) in PrivacyDisclosureRuleTests.Resx(file))
        {
            if (!key.StartsWith($"rule.{CapabilityUseRule.RuleId}.", StringComparison.Ordinal))
                continue;
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{key} in {file} says \"{word}\" — brisk reads this machine and " +
                    "makes no claim about what leaves it or who receives it");
        }
    }

    /// A registry that refuses one capability's root, the way a locked key
    /// refuses OpenSubKey.
    private sealed class ThrowsOnWebcam : IRegistryProbe
    {
        private readonly FakeRegistry _inner;
        public ThrowsOnWebcam(FakeRegistry inner) => _inner = inner;
        private static void Guard(string k)
        {
            if (k.StartsWith(CapabilityUseRule.WebcamKey, StringComparison.OrdinalIgnoreCase))
                throw new System.Security.SecurityException("refused");
        }
        public string? GetString(string k, string v) { Guard(k); return _inner.GetString(k, v); }
        public void SetString(string k, string v, string value) => _inner.SetString(k, v, value);
        public void DeleteValue(string k, string v) => _inner.DeleteValue(k, v);
        public byte[]? GetBytes(string k, string v) { Guard(k); return _inner.GetBytes(k, v); }
        public void SetBytes(string k, string v, byte[] value) => _inner.SetBytes(k, v, value);
        public int? GetInt(string k, string v) { Guard(k); return _inner.GetInt(k, v); }
        public void SetInt(string k, string v, int value) => _inner.SetInt(k, v, value);
        public long? GetLong(string k, string v) { Guard(k); return _inner.GetLong(k, v); }
        public IReadOnlyList<string> GetValueNames(string k) { Guard(k); return _inner.GetValueNames(k); }
        public IReadOnlyList<string> GetSubKeyNames(string k) { Guard(k); return _inner.GetSubKeyNames(k); }
    }
}
