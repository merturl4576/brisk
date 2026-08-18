using System;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

/// The payloads below are real records, captured verbatim from
/// Microsoft-Windows-Diagnostics-Performance/Operational on Windows 11 build
/// 26200. That matters: the ID 100 payload carries 44 Data elements, and it is
/// only at that size that an index-based read looks plausible and is wrong.
/// Because they are pasted in, every one of these tests runs on any machine,
/// unelevated, with no event log present at all.
public class EventLogProbeTests
{
    /// BootTime 51237, MainPathBootTime 24437, BootStartTime 2026-08-17T20:40:16.7269640Z.
    /// BootTime sits at index 5, with SystemBootInstance (index 3) and
    /// UserBootInstance (index 4) immediately before it — both boot counters,
    /// both 392, either of which an off-by-one would read as a 392 ms boot.
    private const string RealBootXml =
        @"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Diagnostics-Performance' Guid='{cfc18ec0-96b1-4eba-961b-622caee05b0a}'/><EventID>100</EventID><Version>2</Version><Level>3</Level><Task>4002</Task><Opcode>34</Opcode><Keywords>0x8000000000010000</Keywords><TimeCreated SystemTime='2026-08-18T09:33:20.2798602Z'/><EventRecordID>1492</EventRecordID><Correlation ActivityID='{9c836a43-2e88-0009-e53a-849c882edd01}'/><Execution ProcessID='4932' ThreadID='7208'/><Channel>Microsoft-Windows-Diagnostics-Performance/Operational</Channel><Computer>DESKTOP-92JC71A</Computer><Security UserID='S-1-5-19'/></System><EventData><Data Name='BootTsVersion'>2</Data><Data Name='BootStartTime'>2026-08-17T20:40:16.7269640Z</Data><Data Name='BootEndTime'>2026-08-18T09:33:12.5438360Z</Data><Data Name='SystemBootInstance'>392</Data><Data Name='UserBootInstance'>392</Data><Data Name='BootTime'>51237</Data><Data Name='MainPathBootTime'>24437</Data><Data Name='BootKernelInitTime'>79</Data><Data Name='BootDriverInitTime'>608</Data><Data Name='BootDevicesInitTime'>957</Data><Data Name='BootPrefetchInitTime'>0</Data><Data Name='BootPrefetchBytes'>0</Data><Data Name='BootAutoChkTime'>0</Data><Data Name='BootSmssInitTime'>10962</Data><Data Name='BootCriticalServicesInitTime'>423</Data><Data Name='BootUserProfileProcessingTime'>5400</Data><Data Name='BootMachineProfileProcessingTime'>152</Data><Data Name='BootExplorerInitTime'>4731</Data><Data Name='BootNumStartupApps'>16</Data><Data Name='BootPostBootTime'>26800</Data><Data Name='BootIsRebootAfterInstall'>false</Data><Data Name='BootRootCauseStepImprovementBits'>0</Data><Data Name='BootRootCauseGradualImprovementBits'>0</Data><Data Name='BootRootCauseStepDegradationBits'>0</Data><Data Name='BootRootCauseGradualDegradationBits'>0</Data><Data Name='BootIsDegradation'>false</Data><Data Name='BootIsStepDegradation'>false</Data><Data Name='BootIsGradualDegradation'>false</Data><Data Name='BootImprovementDelta'>0</Data><Data Name='BootDegradationDelta'>0</Data><Data Name='BootIsRootCauseIdentified'>false</Data><Data Name='OSLoaderDuration'>1308</Data><Data Name='BootPNPInitStartTimeMS'>79</Data><Data Name='BootPNPInitDuration'>1396</Data><Data Name='OtherKernelInitDuration'>225</Data><Data Name='SystemPNPInitStartTimeMS'>1605</Data><Data Name='SystemPNPInitDuration'>572</Data><Data Name='SessionInitStartTimeMS'>2193</Data><Data Name='Session0InitDuration'>6727</Data><Data Name='Session1InitDuration'>260</Data><Data Name='SessionInitOtherDuration'>3974</Data><Data Name='WinLogonStartTimeMS'>13156</Data><Data Name='OtherLogonInitActivityDuration'>997</Data><Data Name='UserLogonWaitDuration'>729</Data></EventData></Event>";

    /// BootTime 111814, BootStartTime 2026-08-16T22:28:43.1933117Z — the boot
    /// the two offender payloads below belong to.
    private const string RealBootWithOffendersXml =
        @"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Diagnostics-Performance' Guid='{cfc18ec0-96b1-4eba-961b-622caee05b0a}'/><EventID>100</EventID><Version>2</Version><Level>1</Level><Task>4002</Task><Opcode>34</Opcode><Keywords>0x8000000000010000</Keywords><TimeCreated SystemTime='2026-08-17T09:10:44.6031254Z'/><EventRecordID>1488</EventRecordID><Correlation ActivityID='{9841997e-2dce-0000-cd23-4298ce2ddd01}'/><Execution ProcessID='5020' ThreadID='7412'/><Channel>Microsoft-Windows-Diagnostics-Performance/Operational</Channel><Computer>DESKTOP-92JC71A</Computer><Security UserID='S-1-5-19'/></System><EventData><Data Name='BootTsVersion'>2</Data><Data Name='BootStartTime'>2026-08-16T22:28:43.1933117Z</Data><Data Name='BootEndTime'>2026-08-17T09:10:34.1219917Z</Data><Data Name='SystemBootInstance'>391</Data><Data Name='UserBootInstance'>391</Data><Data Name='BootTime'>111814</Data><Data Name='MainPathBootTime'>25314</Data><Data Name='BootKernelInitTime'>80</Data><Data Name='BootDriverInitTime'>605</Data><Data Name='BootDevicesInitTime'>951</Data><Data Name='BootPrefetchInitTime'>0</Data><Data Name='BootPrefetchBytes'>0</Data><Data Name='BootAutoChkTime'>0</Data><Data Name='BootSmssInitTime'>11224</Data><Data Name='BootCriticalServicesInitTime'>482</Data><Data Name='BootUserProfileProcessingTime'>5336</Data><Data Name='BootMachineProfileProcessingTime'>167</Data><Data Name='BootExplorerInitTime'>5215</Data><Data Name='BootNumStartupApps'>14</Data><Data Name='BootPostBootTime'>86500</Data><Data Name='BootIsRebootAfterInstall'>false</Data><Data Name='BootRootCauseStepImprovementBits'>0</Data><Data Name='BootRootCauseGradualImprovementBits'>0</Data><Data Name='BootRootCauseStepDegradationBits'>0</Data><Data Name='BootRootCauseGradualDegradationBits'>0</Data><Data Name='BootIsDegradation'>false</Data><Data Name='BootIsStepDegradation'>false</Data><Data Name='BootIsGradualDegradation'>false</Data><Data Name='BootImprovementDelta'>0</Data><Data Name='BootDegradationDelta'>0</Data><Data Name='BootIsRootCauseIdentified'>false</Data><Data Name='OSLoaderDuration'>1296</Data><Data Name='BootPNPInitStartTimeMS'>80</Data><Data Name='BootPNPInitDuration'>1573</Data><Data Name='OtherKernelInitDuration'>229</Data><Data Name='SystemPNPInitStartTimeMS'>1787</Data><Data Name='SystemPNPInitDuration'>569</Data><Data Name='SessionInitStartTimeMS'>2371</Data><Data Name='Session0InitDuration'>6889</Data><Data Name='Session1InitDuration'>270</Data><Data Name='SessionInitOtherDuration'>4064</Data><Data Name='WinLogonStartTimeMS'>13596</Data><Data Name='OtherLogonInitActivityDuration'>998</Data><Data Name='UserLogonWaitDuration'>739</Data></EventData></Event>";

    /// TiWorker.exe, DegradationTime 1998.
    private const string RealOffenderXml =
        @"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Diagnostics-Performance' Guid='{cfc18ec0-96b1-4eba-961b-622caee05b0a}'/><EventID>101</EventID><Version>1</Version><Level>3</Level><Task>4002</Task><Opcode>33</Opcode><Keywords>0x8000000000010000</Keywords><TimeCreated SystemTime='2026-08-17T09:10:44.6031447Z'/><EventRecordID>1490</EventRecordID><Correlation ActivityID='{9841997e-2dce-0000-cd23-4298ce2ddd01}'/><Execution ProcessID='5020' ThreadID='7412'/><Channel>Microsoft-Windows-Diagnostics-Performance/Operational</Channel><Computer>DESKTOP-92JC71A</Computer><Security UserID='S-1-5-19'/></System><EventData><Data Name='StartTime'>2026-08-16T22:28:43.1933117Z</Data><Data Name='NameLength'>13</Data><Data Name='Name'>TiWorker.exe</Data><Data Name='FriendlyNameLength'>33</Data><Data Name='FriendlyName'>Windows Modules Installer Worker</Data><Data Name='VersionLength'>39</Data><Data Name='Version'>10.0.26100.9156 (WinBuild.160101.0800)</Data><Data Name='TotalTime'>5498</Data><Data Name='DegradationTime'>1998</Data><Data Name='PathLength'>125</Data><Data Name='Path'>C:\Windows\WinSxS\amd64_microsoft-windows-servicingstack_31bf3856ad364e35_10.0.26100.9156_none_a546383f7734e5e5\TiWorker.exe</Data><Data Name='ProductNameLength'>37</Data><Data Name='ProductName'>Microsoft® Windows® Operating System</Data><Data Name='CompanyNameLength'>22</Data><Data Name='CompanyName'>Microsoft Corporation</Data></EventData></Event>";

    /// brisk-app.exe, DegradationTime 26081, and a genuinely empty FriendlyName.
    private const string RealOffenderWithoutFriendlyNameXml =
        @"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Diagnostics-Performance' Guid='{cfc18ec0-96b1-4eba-961b-622caee05b0a}'/><EventID>101</EventID><Version>1</Version><Level>3</Level><Task>4002</Task><Opcode>33</Opcode><Keywords>0x8000000000010000</Keywords><TimeCreated SystemTime='2026-08-17T09:10:44.6031355Z'/><EventRecordID>1489</EventRecordID><Correlation ActivityID='{9841997e-2dce-0000-cd23-4298ce2ddd01}'/><Execution ProcessID='5020' ThreadID='7412'/><Channel>Microsoft-Windows-Diagnostics-Performance/Operational</Channel><Computer>DESKTOP-92JC71A</Computer><Security UserID='S-1-5-19'/></System><EventData><Data Name='StartTime'>2026-08-16T22:28:43.1933117Z</Data><Data Name='NameLength'>14</Data><Data Name='Name'>brisk-app.exe</Data><Data Name='FriendlyNameLength'>0</Data><Data Name='FriendlyName'></Data><Data Name='VersionLength'>0</Data><Data Name='Version'></Data><Data Name='TotalTime'>31081</Data><Data Name='DegradationTime'>26081</Data><Data Name='PathLength'>77</Data><Data Name='Path'>C:\Users\MERT\Desktop\brisk\src\Brisk\bin\Debug\net8.0-windows\brisk-app.exe</Data><Data Name='ProductNameLength'>0</Data><Data Name='ProductName'></Data><Data Name='CompanyNameLength'>0</Data><Data Name='CompanyName'></Data></EventData></Event>";

    private static readonly DateTime RealBootStart =
        new DateTime(2026, 8, 17, 20, 40, 16, DateTimeKind.Utc).AddTicks(7269640);

    private static readonly DateTime RealBootWithOffendersStart =
        new DateTime(2026, 8, 16, 22, 28, 43, DateTimeKind.Utc).AddTicks(1933117);

    // ---- reading by field name, not by index -------------------------------

    [Fact]
    public void RealBootPayload_IsReadByFieldName()
    {
        var boot = BootEventParser.ReadBoot(RealBootXml);

        Assert.NotNull(boot);
        Assert.Equal(51237, boot!.BootMs);
        Assert.Equal(24437, boot.MainPathMs);
        Assert.Equal(RealBootStart, boot.Started);
    }

    /// The exact mistake the by-name rule exists to prevent: SystemBootInstance
    /// (392, a count of boots) sits beside BootTime in the payload and would
    /// read as a gloriously fast boot.
    [Fact]
    public void RealBootPayload_NeverReportsTheBootCounterAsMilliseconds()
    {
        var boot = BootEventParser.ReadBoot(RealBootXml);

        Assert.NotNull(boot);
        Assert.NotEqual(392, boot!.BootMs);
        Assert.NotEqual(392, boot.MainPathMs);
    }

    /// Microsoft never promised the order of these elements. Reversing all 44
    /// changes every index and must change nothing at all.
    [Fact]
    public void ReorderedBootPayload_ReadsExactlyTheSame()
    {
        var asLogged = BootEventParser.ReadBoot(RealBootXml);
        var reordered = BootEventParser.ReadBoot(WithFieldsReversed(RealBootXml));

        Assert.NotNull(reordered);
        Assert.Equal(asLogged, reordered);
    }

    [Fact]
    public void RealOffenderPayload_IsReadByFieldName()
    {
        var parsed = BootEventParser.ReadOffender(RealOffenderXml);

        Assert.NotNull(parsed);
        Assert.Equal(RealBootWithOffendersStart, parsed!.BootStarted);
        Assert.Equal("TiWorker.exe", parsed.Offender.Name);
        Assert.Equal("Windows Modules Installer Worker", parsed.Offender.FriendlyName);
        Assert.Equal(1998, parsed.Offender.DegradationMs);
        Assert.EndsWith(@"\TiWorker.exe", parsed.Offender.Path);
    }

    [Fact]
    public void ReorderedOffenderPayload_ReadsExactlyTheSame()
    {
        var asLogged = BootEventParser.ReadOffender(RealOffenderXml);
        var reordered = BootEventParser.ReadOffender(WithFieldsReversed(RealOffenderXml));

        Assert.NotNull(reordered);
        Assert.Equal(asLogged, reordered);
    }

    /// Real programs ship without a friendly name, so an empty one must not be
    /// mistaken for a record worth dropping.
    [Fact]
    public void OffenderWithoutAFriendlyName_IsStillReported()
    {
        var parsed = BootEventParser.ReadOffender(RealOffenderWithoutFriendlyNameXml);

        Assert.NotNull(parsed);
        Assert.Equal("brisk-app.exe", parsed!.Offender.Name);
        Assert.Equal("", parsed.Offender.FriendlyName);
        Assert.Equal(26081, parsed.Offender.DegradationMs);
    }

    // ---- absent is absent, never zero --------------------------------------

    /// A zero here would let a consumer compute BootMs - MainPathMs and blame
    /// the user's own programs for 100% of a boot they had nothing to do with.
    [Fact]
    public void BootWithoutMainPathTime_ReportsNullRatherThanZero()
    {
        var boot = BootEventParser.ReadBoot(Without(RealBootXml, "MainPathBootTime"));

        Assert.NotNull(boot);
        Assert.Equal(51237, boot!.BootMs);
        Assert.Null(boot.MainPathMs);
    }

    [Fact]
    public void BootWithoutABootTime_IsSkipped() =>
        Assert.Null(BootEventParser.ReadBoot(Without(RealBootXml, "BootTime")));

    /// No start time means it can be neither placed in time nor matched to its
    /// offenders, so there is nothing honest left to report.
    [Fact]
    public void BootWithoutAStartTime_IsSkipped() =>
        Assert.Null(BootEventParser.ReadBoot(Without(RealBootXml, "BootStartTime")));

    [Fact]
    public void OffenderWithoutADegradationTime_IsSkipped() =>
        Assert.Null(BootEventParser.ReadOffender(Without(RealOffenderXml, "DegradationTime")));

    [Fact]
    public void OffenderWithoutAName_IsSkipped() =>
        Assert.Null(BootEventParser.ReadOffender(Without(RealOffenderXml, "Name")));

    [Fact]
    public void OffenderWithoutAStartTime_IsSkipped() =>
        Assert.Null(BootEventParser.ReadOffender(Without(RealOffenderXml, "StartTime")));

    // ---- correlation -------------------------------------------------------

    /// End to end on one real cluster: the ID 100 record and the two ID 101
    /// records Windows wrote for the same boot. They agree on
    /// BootStartTime / StartTime exactly, which is why that is the key — the
    /// records' own timestamps differ by a few hundred ticks and grouping on
    /// those would have split this one boot into three.
    [Fact]
    public void RealCluster_ArrivesAsOneBootCarryingItsOwnOffenders()
    {
        var boot = BootEventParser.ReadBoot(RealBootWithOffendersXml);
        var tiWorker = BootEventParser.ReadOffender(RealOffenderXml);
        var briskApp = BootEventParser.ReadOffender(RealOffenderWithoutFriendlyNameXml);
        Assert.NotNull(boot);
        Assert.NotNull(tiWorker);
        Assert.NotNull(briskApp);

        var assembled = BootEventParser.Assemble(
            new[] { boot! }, new[] { tiWorker!, briskApp! });

        var only = Assert.Single(assembled);
        Assert.Equal(111814, only.BootMs);
        Assert.Equal(RealBootWithOffendersStart, only.When);
        Assert.Equal(new[] { "brisk-app.exe", "TiWorker.exe" },
            only.Offenders.Select(o => o.Name));   // worst first: 26081 then 1998
    }

    [Fact]
    public void Assemble_AttachesEachOffenderToItsOwnBoot()
    {
        var older = new ParsedBoot(Utc(1), 40000, 20000);
        var newer = new ParsedBoot(Utc(2), 50000, 25000);

        var assembled = BootEventParser.Assemble(
            new[] { newer, older },
            new[]
            {
                new ParsedOffender(Utc(1), Offender("old.exe", 300)),
                new ParsedOffender(Utc(2), Offender("new.exe", 400)),
            });

        Assert.Equal(new[] { "new.exe" }, assembled[0].Offenders.Select(o => o.Name));
        Assert.Equal(new[] { "old.exe" }, assembled[1].Offenders.Select(o => o.Name));
    }

    [Fact]
    public void Assemble_OrdersOffendersWorstFirst()
    {
        var assembled = BootEventParser.Assemble(
            new[] { new ParsedBoot(Utc(1), 50000, 25000) },
            new[]
            {
                new ParsedOffender(Utc(1), Offender("small.exe", 82)),
                new ParsedOffender(Utc(1), Offender("huge.exe", 36541)),
                new ParsedOffender(Utc(1), Offender("middling.exe", 4893)),
            });

        Assert.Equal(new[] { "huge.exe", "middling.exe", "small.exe" },
            assembled[0].Offenders.Select(o => o.Name));
    }

    /// Common in real data: three of the ten most recent boots on the verified
    /// machine had no ID 101 records at all.
    [Fact]
    public void Assemble_LeavesABootWindowsBlamedNobodyForWithAnEmptyList()
    {
        var assembled = BootEventParser.Assemble(
            new[] { new ParsedBoot(Utc(1), 50000, 25000) },
            Array.Empty<ParsedOffender>());

        Assert.Empty(Assert.Single(assembled).Offenders);
    }

    /// A wrong attribution is worse than a missing one, so an offender from a
    /// boot outside the window is dropped rather than attached to a neighbour.
    [Fact]
    public void Assemble_DropsAnOffenderWhoseBootIsNotInTheWindow()
    {
        var assembled = BootEventParser.Assemble(
            new[] { new ParsedBoot(Utc(2), 50000, 25000) },
            new[] { new ParsedOffender(Utc(1), Offender("elsewhere.exe", 900)) });

        Assert.Empty(Assert.Single(assembled).Offenders);
    }

    [Fact]
    public void Assemble_KeepsTheBootOrderItWasGiven()
    {
        var assembled = BootEventParser.Assemble(
            new[]
            {
                new ParsedBoot(Utc(3), 30000, null),
                new ParsedBoot(Utc(2), 20000, null),
                new ParsedBoot(Utc(1), 10000, null),
            },
            Array.Empty<ParsedOffender>());

        Assert.Equal(new[] { Utc(3), Utc(2), Utc(1) }, assembled.Select(b => b.When));
    }

    // ---- the offender walk's bound -----------------------------------------

    /// The bound must be the earliest boot in the set, not the last one read.
    /// Boots normally arrive newest-first so the two agree, but a clock
    /// correction between boots lets a newer boot carry an earlier start — and
    /// taking the last would then stop the walk early and silently drop the
    /// offenders of every boot behind it.
    [Fact]
    public void OldestStart_TakesTheEarliestStart_NotTheLastBootRead()
    {
        var clockWentBackwards = new[]
        {
            new ParsedBoot(Utc(3), 30000, null),
            new ParsedBoot(Utc(1), 10000, null),   // earliest, and not last
            new ParsedBoot(Utc(2), 20000, null),
        };

        Assert.Equal(Utc(1), BootEventParser.OldestStart(clockWentBackwards));
    }

    [Fact]
    public void OldestStart_OnBootsInTheUsualOrder_IsTheLastOne()
    {
        var newestFirst = new[]
        {
            new ParsedBoot(Utc(3), 30000, null),
            new ParsedBoot(Utc(2), 20000, null),
            new ParsedBoot(Utc(1), 10000, null),
        };

        Assert.Equal(Utc(1), BootEventParser.OldestStart(newestFirst));
    }

    // ---- context wiring ----------------------------------------------------

    [Fact]
    public void FakeEventLog_ReturnsWhatItWasGiven()
    {
        var log = new FakeEventLog();
        log.Boots.Add(new BootRecord(new DateTime(2026, 8, 18), 51237, 24437,
            new[] { new BootOffender("Spotify.exe", "Spotify", @"C:\x\Spotify.exe", 37141) }));

        Assert.Equal(51237, log.RecentBoots(5)[0].BootMs);
        Assert.Equal(37141, log.RecentBoots(5)[0].Offenders[0].DegradationMs);
    }

    [Fact]
    public void EmptyContext_HasNoBootHistory() =>
        Assert.Empty(TestContext.Empty().EventLog.RecentBoots(5));

    // ---- helpers -----------------------------------------------------------

    private static readonly XNamespace EventNs =
        "http://schemas.microsoft.com/win/2004/08/events/event";

    private static DateTime Utc(int day) =>
        new DateTime(2026, 8, day, 4, 0, 0, DateTimeKind.Utc);

    private static BootOffender Offender(string name, int degradationMs) =>
        new(name, "", @"C:\" + name, degradationMs);

    /// Reverses every Data element, so each one lands at a different index.
    private static string WithFieldsReversed(string xml)
    {
        var doc = XDocument.Parse(xml);
        var data = doc.Root!.Element(EventNs + "EventData")!;
        var reversed = data.Elements().Reverse().ToList();
        data.RemoveNodes();
        data.Add(reversed);
        return doc.ToString();
    }

    private static string Without(string xml, string fieldName)
    {
        var doc = XDocument.Parse(xml);
        doc.Root!.Element(EventNs + "EventData")!
            .Elements(EventNs + "Data")
            .Single(d => (string?)d.Attribute("Name") == fieldName)
            .Remove();
        return doc.ToString();
    }
}
