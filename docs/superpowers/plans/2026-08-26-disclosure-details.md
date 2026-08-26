# Disclosure Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Act on the maintainer's first live look at 0.6.0: page-level buttons on
Performans, usb-history out of every shareable lead, the Delivery Optimization
counter split into LAN/Internet, and the USB device records shown to their owner
on the Gizlilik page — never on the card.

**Architecture:** Four independent tasks on one branch. The USB details ride the
scan in a NEW snapshot channel (`ScanSnapshot.UsbDevices`) that the report-card
pipeline never touches — names stay out of `DiagnosticFinding` entirely, so no
card bug can ever leak them. The DO split widens the existing probe answer from
one number to two; the probe already parses both fields and sums them.

**Tech Stack:** C#/.NET 8, WPF, xUnit. TR/EN resx pairs must keep identical key
sets (pinned by `ResxFiles_ExposeTheSameKeySet`).

**Spec:** `docs/superpowers/specs/2026-08-25-faz3-disclosure-design.md` — Task 4
amends its red line 2 (exact wording in Task 4, maintainer approved the feature
2026-08-26; the amendment text itself needs his one-word OK before the T4 commit
that relies on it).

## Global Constraints

- Red line 1 (unchanged): no transmission claim without a record of one. The DO
  counter IS such a record; the carve-out is by rule id
  (`TheOneTransmissionClaim…IsNotBanned`) and any new `rule.delivery-optimization.*`
  string must keep it satisfied in both languages.
- Red line 2 (amended by Task 4): counts on every shareable surface — card,
  headline, anything a screenshot carries. Device records may render on the
  Gizlilik page only. The program list stays banned everywhere (maintainer kept
  run-history count-only).
- Every commit builds green alone: `dotnet test -c Release`, 0 warnings.
  Baseline 1281 (600 + 681) at branch base `2a3c8c2`.
- TDD red-first; for UI claims assert the text the control/VM actually exposes.
- No claim outrunning its evidence in any comment, assertion message, or commit
  body. Mark genuinely unverified registry layouts UNVERIFIED the way
  `UsbHistoryRule.InstallDateSubPath` does.
- brisk runs elevated; never launch `brisk-app.exe` from the session.

---

### Task 1: Performans page gets the Sağlık button band

**Files:**
- Modify: `src/Brisk/Views/PerfPage.xaml` (insert after the hero `Border`, before the scroll content)
- Test: `src/Brisk.Tests/PanelSourceTests.cs`

**Interfaces:**
- Consumes: `HealthViewModel.ScanCommand`, `.FixAllCommand`, `.CreateRestorePointFirst`, `.IsBusy`, `.State.IsScanning` — all already on the VM both pages share.
- Produces: nothing later tasks use.

- [ ] **Step 1: Write the failing source-guard test** (mirror the existing PanelSourceTests read-the-XAML pattern in that file):

```csharp
    /// The maintainer's first live look at 0.6.0: Performans had no way to
    /// start a scan or run the safe fixes while Sağlık, the same view model
    /// behind a different filter, offered both. The two findings pages now
    /// carry the same band, from the same bindings.
    [Fact]
    public void PerfPage_CarriesTheSameActionBand_AsHealthPage()
    {
        var xaml = File.ReadAllText(PagePath("PerfPage.xaml"));
        Assert.Contains("{Binding ScanCommand}", xaml);
        Assert.Contains("{Binding FixAllCommand}", xaml);
        Assert.Contains("{Binding CreateRestorePointFirst}", xaml);
    }
```

(`PagePath` — use whatever helper PanelSourceTests already uses to reach
`src/Brisk/Views`; if it has none, mirror how it opens the other XAML files it
reads.)

- [ ] **Step 2: Run it, watch it fail** — `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~PerfPage_CarriesTheSameActionBand"`. Expected: FAIL, ScanCommand absent.

- [ ] **Step 3: Insert the band into PerfPage.xaml** — copy of HealthPage.xaml's band (same keys, no new resx), placed as the second `DockPanel.Dock="Top"` element, directly after the hero `Border`:

```xml
        <!-- The same action band Sağlık carries, from the same view model:
             the maintainer's first live look found this page could neither
             start a scan nor run the safe fixes. No HorizontalAlignment —
             the content column works by MaxWidth + default Stretch. -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,14"
                    MaxWidth="{StaticResource ContentMaxWidth}">
            <Button Margin="0,0,8,0" Command="{Binding ScanCommand}"
                    Content="{Binding [flyout.scan], Source={x:Static loc:Loc.Instance}}">
                <Button.Style>
                    <Style TargetType="Button" BasedOn="{StaticResource GhostButton}">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding State.IsScanning}" Value="True">
                                <Setter Property="IsEnabled" Value="False" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Button.Style>
            </Button>
            <Button Margin="0,0,16,0" Command="{Binding FixAllCommand}"
                    Content="{Binding [health.fixall], Source={x:Static loc:Loc.Instance}}">
                <Button.Style>
                    <Style TargetType="Button" BasedOn="{StaticResource PrimaryButton}">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding State.IsScanning}" Value="True">
                                <Setter Property="IsEnabled" Value="False" />
                            </DataTrigger>
                            <DataTrigger Binding="{Binding IsBusy}" Value="True">
                                <Setter Property="IsEnabled" Value="False" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Button.Style>
            </Button>
            <CheckBox Style="{StaticResource QuietCheck}" VerticalAlignment="Center"
                      IsChecked="{Binding CreateRestorePointFirst}"
                      Content="{Binding [health.restorepoint], Source={x:Static loc:Loc.Instance}}" />
        </StackPanel>
```

`health.fixall` is deliberately reused: FixAllService acts on the whole snapshot
from any surface, so the label may not differ per page.

- [ ] **Step 4: Full suite green** — `dotnet test -c Release`, 0 warnings.
- [ ] **Step 5: Commit** (house-style narrative body).

### Task 2: usb-history stops leading anything shareable

**Files:**
- Modify: `src/BriskEngine/Diagnostics/RevelationPicker.cs`
- Test: `src/BriskEngine.Tests/RevelationPickerTests.cs`, plus whichever ReportCardModelTests/OverviewViewModelTests fixtures assert a usb-led pick (update their expectations, keeping their claims).

**Interfaces:**
- Consumes: `RevelationPicker.Pick(IEnumerable<DiagnosticFinding>)` — both `OverviewViewModel.cs:536` and `ReportCardModel.cs:99` call it; excluding here removes usb from BOTH the Overview lead and the card's picked rows. `PrivacyViewModel`'s banding reads `finding.Headline` directly, NOT Pick — the page row keeps its number. Do NOT touch `UsbHistoryRule`'s Headline.
- Produces: `RevelationPicker.NeverLeads` (internal static readonly `string[]`), for tests to read.

- [ ] **Step 1: Failing test:**

```csharp
    /// The controller ranked usb-history third; the maintainer's machine
    /// then showed what that buys — a count of 1 leading surfaces built to
    /// be read and shared. His call on the first live data (2026-08-26):
    /// the record count lives on the Gizlilik page and leads nothing.
    [Fact]
    public void UsbHistory_IsNeverPicked_HoweverStrongItsNumber()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            TestData.Finding("usb-history", Severity.Warning, stars: 5,
                headline: new Headline("47", "USB storage devices recorded",
                    "rule.usb-history.headline.value", new[] { "47" },
                    "rule.usb-history.headline.caption", Array.Empty<string>())),
            TestData.Finding("disk-breakdown", Severity.Info, stars: 1,
                headline: new Headline("58.1 GB", "of disk in large folders",
                    "rule.disk-breakdown.headline.value", new[] { "58.1 GB" },
                    "rule.disk-breakdown.headline.caption", Array.Empty<string>())),
        });
        Assert.DoesNotContain(picked, f => f.RuleId == "usb-history");
        Assert.Contains(picked, f => f.RuleId == "disk-breakdown");
    }
```

(Adapt the `TestData.Finding` headline plumbing to however RevelationPickerTests
already builds headline-bearing findings — reuse its own helper.)

- [ ] **Step 2: Watch it fail** (usb is picked today, and first).
- [ ] **Step 3: Implement** — in `RevelationPicker`: remove `"usb-history"` from `Priority`; add above `Pick`:

```csharp
    /// Rules whose number never leads any surface Pick feeds — today the
    /// Overview hero and the report card. The Gizlilik page does not ask
    /// Pick; it reads Headline itself, which is where these still render.
    ///
    /// usb-history was THIRD in Priority above, on the ruling that the
    /// strongest number brisk owns should lead the moment nothing
    /// actionable outranks it. The maintainer's machine then showed the
    /// other side of that ruling — a count of 1 over a 58.1 GB disk
    /// finding — and he overturned it on the first live data (2026-08-26).
    internal static readonly string[] NeverLeads = { "usb-history" };
```

and in `Pick`, before the ordering: `.Where(f => Array.IndexOf(NeverLeads, f.RuleId) < 0)`.
Rewrite the now-false "third, because…" comment block in `Priority` to point at
`NeverLeads` (its history moves there); do not leave both stories standing.

- [ ] **Step 4: Full suite** — expect fallout in picker/card/overview tests that fixed usb into expectations; update THEIR fixtures to keep asserting their own claims (ranking order, card row shape) without usb, never by weakening an assertion.
- [ ] **Step 5: Commit.**

### Task 3: the DO counter says where the bytes went

**Files:**
- Modify: `src/BriskEngine/Diagnostics/IDeliveryOptimizationProbe.cs`, `src/BriskEngine/Diagnostics/RealProbes/RealDeliveryOptimizationProbe.cs`, `src/BriskEngine/Diagnostics/Rules/Privacy/DeliveryOptimizationRule.cs`, `src/Brisk/Localization/Strings.resx` + `Strings.tr.resx` (`rule.delivery-optimization.evidence` value edit — check arg count against the rule)
- Test: `src/BriskEngine.Tests/Rules/DeliveryOptimizationRuleTests.cs` (fakes live here/near — update all `IDeliveryOptimizationProbe` doubles), `src/Brisk.Tests/PrivacyRedLineTests.cs` only re-run (the carve-out guard must stay green over the new copy).

**Interfaces:**
- Consumes: `ParseUploadedBytes` already requires BOTH `UploadLanBytes` and `UploadInternetBytes` and sums them.
- Produces:

```csharp
/// One month's answer, in Windows' own two halves. Both fields were always
/// required to report at all (a half-recognised shape is an unread counter);
/// this record stops throwing the halves away after requiring them.
public sealed record PeerUpload(long LanBytes, long InternetBytes)
{
    public long Total => LanBytes + InternetBytes;
}
```

on the interface: `PeerUpload? UploadedToPeers();` REPLACING `long? BytesUploadedToPeers()` (no compatibility shim — one reader, the rule).

- [x] **Step 1: Failing engine tests** (probe parse + rule copy):

```csharp
    [Fact]
    public void Parse_CarriesBothHalves_NotJustTheirSum()
    {
        var result = RealDeliveryOptimizationProbe.ParseUploaded(
            "{\"UploadLanBytes\":317000384,\"UploadInternetBytes\":1024,\"MonthStartDate\":\"/Date(1754006400000)/\"}");
        Assert.Equal(317000384, result!.LanBytes);
        Assert.Equal(1024, result.InternetBytes);
    }

    [Fact]
    public void Evidence_NamesBothDestinations_WithTheMeasuredSplit()
    {
        // fake probe answering new PeerUpload(317000384, 0) — the real
        // machine's actual reading on 2026-08-26, all of it local
        var finding = DetectWith(new PeerUpload(317_000_384, 0));
        Assert.Contains("302.3 MB", finding.EvidenceArgs[1]);   // match Fmt.Bytes' real rendering — adjust literal to it
        Assert.Contains("0 B", finding.EvidenceArgs[2]);
    }
```

(Rename `ParseUploadedBytes` → `ParseUploaded` returning `PeerUpload?`; keep its
both-or-nothing and below-zero refusals — move them onto the record fields:
either half below zero ⇒ null.)

- [x] **Step 2: Watch them fail.**
- [x] **Step 3: Implement.** Rule's `Reported` takes the record; headline stays `Fmt.Bytes(u.Total)`; evidence sentence becomes (EN — TR mirrors claim-for-claim):

> "…for the current calendar month that counter reads {0}: {1} of it to machines
> on this local network, {2} to machines on the internet. brisk reads the
> counter and nothing past it: which machines those were is not something that
> read can tell you."

Both resx `rule.delivery-optimization.evidence` values updated to three args.
The last clause is deliberate: the split does NOT name machines, and the copy
must keep saying so.

- [x] **Step 4: Full suite** — `PrivacyRedLineTests` in particular: the copy ban
must still pass via the DO carve-out (verb+recipient needles per language — if
a needle no longer matches, fix the needle to the new sentence, never widen the
carve-out beyond rule id).
- [x] **Step 5: Commit.**

### Task 4: the USB records reach their owner — and only their owner

**Files:**
- Modify: `docs/superpowers/specs/2026-08-25-faz3-disclosure-design.md` (red line 2 — exact edit below, ONLY after maintainer OK), `src/BriskEngine/Diagnostics/Rules/Privacy/UsbHistoryRule.cs` (expose `ReadDevices`), `src/Brisk/Services/IEngineHost.cs` + `EngineHost.cs` (snapshot channel), `src/Brisk.Tests/Fakes.cs` / `TestData` (default `UsbDevices: Array.Empty<UsbDeviceRecord>()` in the central snapshot builder), `src/Brisk/ViewModels/PrivacyViewModel.cs`, `src/Brisk/Views/PrivacyPage.xaml`, both resx.
- Test: `src/BriskEngine.Tests/Rules/TelemetrySwitchRuleTests.cs` sibling file for the rule read, `src/Brisk.Tests/PrivacyViewModelTests.cs`, `src/Brisk.Tests/ReportCardModelTests.cs` (the existing plant test gains the page-shows/card-still-refuses split), `src/Brisk.Tests/PrivacyRedLineTests.cs`.

**Interfaces:**
- Produces (engine): `public sealed record UsbDeviceRecord(string Model, DateTime? FirstSeen, DateTime? LastSeen);` and on `UsbHistoryRule`: `public static IReadOnlyList<UsbDeviceRecord> ReadDevices(DiagnosticContext ctx)` — same enumeration `Detect` walks, one record per INSTANCE, model from the model-level subkey name; `FirstSeen` from the existing `InstallDateSubPath` (0064); `LastSeen` from the SAME property-store path with `0066` (DEVPKEY_Device_LastArrivalDate) — **UNVERIFIED on real hardware exactly like 0064; copy that comment's honesty, read guarded, null when refused.** Every per-instance read individually guarded: a refusal costs that field, never the list, never the scan.
- Produces (app): `ScanSnapshot` gains `IReadOnlyList<UsbDeviceRecord> UsbDevices` — REQUIRED, no default (house rule: no default that adds no claim; an empty list claims "the record holds no devices brisk could read"). `EngineHost.ScanCoreAsync` fills it in the same pass, inside a catch that costs the list and not the scan (the lesson `d083da1` just paid for). The card model NEVER receives it — names cannot leak through a pipeline that never carries them.
- Produces (VM): `PrivacyViewModel.UsbDeviceRows` (`ObservableCollection<string>`), formatted via new keys `privacy.usb.device` = EN `"{0} — first recorded {1} · last seen {2}"` / TR `"{0} — ilk kayıt {1} · son görülme {2}"`, absent dates rendered as the repo's dash `"—"`; and `privacy.usb.devices.title` = EN `"What that record holds"` / TR `"O kaydın tuttukları"`. Fold in `PrivacyPage.xaml` under the usb row's card, closed by default, Expander style matching the page.

- [ ] **Step 0: Spec amendment — REQUIRES MAINTAINER OK on this exact text.** Red line 2 becomes:

> 2. **Numbers, never contents — on every surface built to be shared.** "47 USB
>    devices" yes; device names never on the report card, in a headline, or in
>    any string a screenshot carries. On the Gizlilik page itself the USB record
>    may be shown in full to its owner — model and dates are the user's own
>    data, rendered where only the user looks, behind a fold that opens on
>    request. The program list stays banned everywhere: "1,284 program records"
>    yes; the list never, on any surface. *(Amended 2026-08-26 on the
>    maintainer's call at the first live look; the original read: "Numbers,
>    never contents. '47 USB devices' yes; device names never. '1,284 program
>    records' yes; the program list never. This already governs the report card
>    and now governs the Privacy page.")*

- [ ] **Step 1: Failing tests, red-first, in this order:** (a) `ReadDevices` returns one record per instance with the planted model name and both dates from a fake registry; a per-instance refusal costs the field/record, not the list; (b) `PrivacyViewModel` renders the planted name in `UsbDeviceRows`; (c) the EXISTING card plant test extended: the same snapshot's card output still contains no device name (`ReportCardModelTests`) — this is the amended red line as a test, page-yes-card-never in one fixture.
- [ ] **Step 2: Watch each fail for its own reason.**
- [ ] **Step 3: Implement engine read → snapshot channel → VM rows → XAML fold, in that order, committing when green.**
- [ ] **Step 4: resx parity + full suite + `PrivacyRedLineTests` in full.**
- [ ] **Step 5: Commit(s); the spec edit rides the commit that makes the page render names, never an earlier one.**

## Self-review notes

- Spec coverage: T1 has no spec clause (pure UI parity, maintainer-ordered); T2
  touches no red line (counts stay counts, surfaces shrink); T3 widens copy
  under red line 1's carve-out, already test-held; T4 is the red-line 2
  amendment and carries it.
- Type check: `PeerUpload` produced in T3 and consumed only there;
  `UsbDeviceRecord` produced and consumed in T4; T1/T2 share nothing.
- Order: tasks are independent; T4 is last because it alone waits on the
  maintainer's OK for the spec sentence.

## Execution log

### T3 — the DO counter says where the bytes went (2026-08-26)

**Commits** (branch `feat/disclosure-details`, on top of `bed1fb2`):

- `316f782` the counter's two halves survive the read that always required them
- `c2fd397` the DO counter's sentence says which side of the router the bytes stopped at

**Reds watched:**

1. `TheParser_CarriesBothHalves_NotJustTheirSum` —
   `error CS0117: 'RealDeliveryOptimizationProbe' bir 'ParseUploaded' tanımı
   içermiyor` (does not contain a definition for `ParseUploaded`). The right
   reason: the test demands the renamed parse and nothing had written it.
2. `TheEvidence_NamesBothDestinations_WithTheMeasuredSplit` —
   `Assert.Equal() Failure: Collections differ ↓ (pos 1) Expected: ["302 MB",
   "302 MB", "0 B"] Actual: ["302 MB"]`. The evidence still carried the total
   alone. That failure is also where `Fmt.Bytes(317000384) == "302 MB"` was
   read off the real formatter rather than guessed.

**Counts:** baseline 1279 (597 + 682) → 1283 (601 + 682) after `316f782` →
**1284 (602 + 682)** after `c2fd397`. 0 warnings, `dotnet test -c Release`.
`PrivacyRedLineTests` run alone: **42/42 green**, and the carve-out theory
`TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned` green in both
languages.

**Deviations from the plan, and why:**

- The plan's `"302.3 MB"` literal was a guess and is wrong: `Fmt.Bytes`
  formats its megabyte branch `"F0"`, so 302.3125 MB renders `"302 MB"`. The
  `"0 B"` guess was right. Both literals are now pinned to the real
  rendering, and the test says which branch produces them.
- Two commits rather than one — the type change and the copy change are each
  green alone, and the first has its own red.
- `Detect` gained a third range check nobody asked for and it is not
  redundant: the probe hands back two halves AND their sum, and neither range
  follows from the other. A half below zero hides inside a positive total
  ((-2, +3) summed to a plausible 1 and passed the OLD parser, which checked
  the sum), and two halves that are each `long.MaxValue` wrap their sum to
  -2. Both are pinned as theory rows, in `AnUploadFigureBelowZero_IsNotACount`
  and `OutputItCannotRead_IsNotZero`.
- The parse's below-zero refusal moved onto the halves as the plan said, and
  is therefore STRICTER than what it replaced — the comment on it says so and
  names the pair it now catches.

**Found, not touched:**

- The carve-out needed no change at all. `TheOneTransmissionClaimBrisk
  HasARecordOf_IsNotBanned` demands ONE `rule.delivery-optimization.*` string
  carrying both a verb and a recipient; `rule.delivery-optimization.title`
  satisfies it on its own, and the new evidence still carries "uploaded" +
  "to other machines" / "yükle" + "başka makinelere" as well. No needle was
  widened or moved.
- Nothing outside the rule and its tests pins the old one-arg evidence
  string. `LocalizedText.Resolve` passes `EvidenceArgs` through by array, the
  CLI prints the engine's own English, and the Gizlilik page and report card
  read the same two paths. The only other places naming the rule id assert
  routing, ranking or the UNREAD sentence, none of which moved.
- `RevelationPickerTests` and `OverviewViewModelTests` carry `"302 MB"` as a
  planted headline value for this rule. Still correct, still the total, left
  alone.
