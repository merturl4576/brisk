# Faz 3 — Disclosure and the Telemetry Triple — Design

**Status:** approved 2026-08-25. Implements PLAN.md §6.3 and the Faz 3
checklist in one wave, on the maintainer's decision.

**Version:** lands as 0.6.0. No git tag — the visible release stays at v0.1.0
until the announcement, which follows this phase.

## Why

Everything brisk does today is about the machine's speed and hygiene. The niche
where the stars actually are is privacy and debloat, and brisk has never
entered it. PLAN.md §6.3 is the entry: not "Windows is watching you", which
everyone has heard and nobody is surprised by, but **the number on your own
machine** — the records Windows keeps and most people do not know exist.

The differentiator is not the disclosure. Every debloat tool shows settings.
**No tool checks that what it turned off stayed off.** Windows feature updates
re-enable some of it; some policies are silently ignored on Home. The tool
says "disabled", the user believes they are protected. brisk reads back, and
looks again weeks later.

## Scope

**In:** the disclosure probes, the telemetry triple (show / reversibly turn off
/ read back whether it held), the Delivery Optimization disclosure, a new
Privacy page, and entry into the existing revelation lottery.

**Out, with reasons:**

- **Per-app network usage (SRUM).** Its data lives in an ESE database, not the
  registry — a new dependency or a parser of its own, alone worth more than the
  other eight probes combined. Deferred to Faz 6. Delivery Optimization takes
  its place in this wave and is the stronger claim anyway.
- **Any speed test.** Contacting an outside server contradicts "nothing leaves
  your machine". Never, not as an option (§6.3 decision, 2026-08-23).
- **GPU assignment visibility.** Faz 6.

## The red lines

These are not guidance. Each becomes a test (see Guards).

1. **brisk never says "Microsoft can no longer see this."** The only sentence
   available is *"this setting currently reads as off; I last confirmed it on
   this date"*. brisk reads a machine; it has no visibility into what Microsoft
   receives.
2. **Numbers, never contents — on every surface built to be shared.** "47 USB
   devices" yes; device names never on the report card, in a headline, or in
   any string a screenshot carries. On the Gizlilik page itself the USB record
   may be shown in full to its owner — model and dates are the user's own
   data, rendered where only the user looks, behind a fold that opens on
   request. The program list stays banned everywhere: "1,284 program records"
   yes; the list never, on any surface. *(Amended 2026-08-26 on the
   maintainer's call at the first live look; the original read: "Numbers,
   never contents. '47 USB devices' yes; device names never. '1,284 program
   records' yes; the program list never. This already governs the report card
   and now governs the Privacy page.")*
3. **Policies that do not apply on this edition are said so.** A Home machine
   where a Group Policy value is written but ignored must read as ignored, not
   as protection.
4. **What could not be read goes in "okuyamadıklarım".** An unreadable probe is
   never a silent zero.

## Health score

Every finding in this wave is `FindingKind.Notice`, **including the ones brisk
can fix.** This deliberately breaks the v0.4 heuristic that a fixable finding
is a `Problem`, and executes PLAN.md's own line: *"ifşa bulguları sağlık
puanını düşürmemeli"*.

The reasoning, recorded because the exception will look wrong to a later
reader: the health score grades the machine's **performance and hygiene**. An
advertising ID that is switched on does not make the machine slower. Privacy is
a second axis — brisk shows it and can act on it, but does not grade it. A user
whose machine is fast and clean should read 100 whether or not they have chosen
to leave telemetry on; that choice is theirs and brisk is not scoring it.

## Engine — probes

All local reads. No network call of any kind exists in this wave.

**Corrected 2026-08-25, after Tasks 2-4 shipped.** This section originally named
four new probes. Three of them were never needed and one of the three would
have been actively wrong. The table below is what the first three rows became;
the fourth is unbuilt and says so. Each row's note quotes the clause it
replaces, because a spec that quietly rewrites itself teaches the next reader
nothing — and this note is itself a correction, since an earlier draft of this
paragraph claimed the whole table was built and that whole rows were quoted.

| probe | source | notes |
|---|---|---|
| *(none — the existing `IRegistryProbe`)* | registry: advertising ID, diagnostic data level, tailored experiences, speech/typing personalisation, location, activity history, Recall | The spec said `RealPrivacyProbe`. No new probe was needed: `IRegistryProbe` already exposes `GetInt`/`GetString`/`GetSubKeyNames`/`GetValueNames`/`GetBytes`, which is the whole surface. |
| *(none — the existing `IRegistryProbe`)* | `USBSTOR` instance count; earliest install date from each instance's `Properties\{83da6326-…}\0064` FILETIME | The spec said `RealUsbHistoryProbe`, reading the date from `SetupAPI.dev.log`. **That log is never read.** The date comes from the device property store — and on an unelevated machine that key is refused, so the count ships without a date and the finding says so. |
| *(none — the existing `IRegistryProbe`)* | `UserAssist` value-name count under both `Count` keys | The spec said "ROT13-decoded". **Nothing is decoded, ever.** The count is the number of value names; decoding would produce exactly the contents red line 2 forbids, and code that decodes is code a reviewer must then prove never leaks. |
| `IDeliveryOptimizationProbe` | Delivery Optimization performance counters | bytes uploaded to peers this month. The one genuinely new probe, and **still to be built** — Task 5. The spec first called it `RealDeliveryOptimizationProbe`; the interface is what the context takes and the `Real…` class is what implements it, so both names are right about different things. |

The one new probe follows the existing `RealProbes` shape and gets a fake for
tests; the three rows that read "(none)" need neither, because `IRegistryProbe`
already has both. What binds all four is the rule above them: a read that
throws or finds nothing reports "unreadable", never zero — the difference
between "no USB devices recorded" and "I could not read the USB record" is the
difference between a claim and a lie.

## Engine — rules

New rules, all `Notice`, routed to the new page by rule id — **not** by a new
`RuleCategory` member. `RuleCategory` is a consent level (Auto / Confirm /
Advise) and `FindingSections` says so in as many words; topic routing already
happens by rule id, and privacy rules span all three consent levels: the four
consequence-free ones are `Auto`, the two that cost the user something are
`Confirm`, and the report-only ones are `Advise`. So the page gets a
`FindingSections.IsPrivacy` set beside `IsPerformance`, and consent keeps
meaning consent.

**Report only, no fix** — brisk shows the number and nothing else:

- `usb-history` — how many devices, how far back
- `run-history` — how many program records
- `delivery-optimization` — bytes uploaded to strangers this month
- `recall-status` — present and on, present and off, or not on this build.
  Deliberately no fix in this wave: the surface is new, varies between builds,
  and a fix brisk cannot verify is exactly what this project refuses to ship.
  The page links to Windows' own setting instead.

**Show and fix, no visible consequence** — these four are what the single
button turns off:

- `advertising-id`, `diagnostic-level`, `tailored-experiences`, `speech-typing`

**Show and fix, but the user loses something** — their own switch, with the
loss named beside it:

- `location` (Find my device stops working)
- `activity-history` (Timeline ends)

Every fixable rule implements `Fix`/`Undo` against the existing `FixJournal`,
so every change brisk makes here is undoable by the same machinery as every
other fix — and is what the read-back below re-reads.

## The read-back — "tuttu mu"

**No scheduler, no background service, no new moving part.** brisk already
journals every fix it applies. Each scan re-reads the settings brisk has turned
off and compares against the journal:

- **still off** → a quiet line: *"{0} gün önce kapattın, hâlâ kapalı"*
- **back on** → *"Bunu {0} tarihinde kapatmıştın; şu an yeniden açık."* This is
  the sentence no competitor can print.
- **written but ignored** (a policy on an edition that does not honour it) →
  *"Ayar kapalı yazıyor ama bu Windows sürümü onu dikkate almıyor."*
- **written, and brisk cannot tell** — a policy with no second value to read it
  against → the date, and the admission both policy rules already carry word
  for word: *"bu Windows sürümünün o ilkeye uyup uymadığını bu okuma
  söyleyemez"*

The third case is the wave's best story and its hardest honesty test: it is
brisk reporting that its own fix did not take.

**Corrected 2026-08-25, after Task 6 shipped.** Three things above were wrong
when written, and the shipped code is what the list now describes.

- The back-on line said *"…; 3 Eylül'de geri açılmış."* **brisk has no such
  date.** The journal records brisk's own writes, so brisk knows when it
  applied the fix and what the setting reads now, and nothing about the moment
  in between. The shipped sentence names the fix date and the present reading
  and stops.
- There were **three states and there are four.** Three assumed every switch
  can be sorted into them, and they cannot. Only `diagnostic-level` has a
  second value brisk reads and never writes, so only it can separate an
  edition that ignored the policy from one that honoured it.
  `activity-history` has no such value — Task 3 declined to invent a path it
  could not vouch for, precisely so this state would not rest on a read that
  means nothing — so a three-state read-back would have had to report it as
  *still off* on exactly the Home machine where the policy is ignored and
  Timeline is still running. The fourth state says brisk does not know
  instead. The four consumer settings are not policies at all and never reach
  it: their value **is** the setting, so re-reading it is the whole answer.
- The back-on line was called **a finding**, and it is not one. The read-back
  produces `ReadBackResult` rows; `DiagnosticFinding`s come from the rules.
  The two coincide on this case — the state is decided by the same live read
  that decides whether the rule reports a finding — which is why the guard
  below still has something to be red about.

## UI

A sixth nav tile, **Gizlilik**, holding three blocks:

1. **Disclosure** — the numbers, largest first.
2. **"Windows'un kendi gizlilik anahtarları"** — the switches, in the two-tier
   model the maintainer chose: one button turns off everything with **no
   visible consequence** (advertising ID, diagnostic level, tailored
   experiences, speech/typing); the two settings that **cost the user
   something** (location, activity history) sit on their own switches with the
   loss named beside them — *"'Cihazımı bul' çalışmaz"*, *"Timeline biter"*.
   All of it reversible.
3. **Read-back** — what brisk turned off, when, and whether it held.

**Corrected 2026-08-26, after Task 9 shipped.** Item 2's heading was
**"Windows'a ne gönderiliyor"** — *"What gets sent to Windows"* — and it now
reads **"Windows'un kendi gizlilik anahtarları"** / *"Windows' own privacy
switches"*. Task 7 shipped the original, flagged that it and red line 1 pull
against each other, and left the call to Task 9, which took it.

The principle is **"no transmission claim without a record of one."** Red line
1 forbids the assurance — *"Microsoft can no longer see this"* — and the reason
it gives is that brisk reads a machine and has no visibility into what
Microsoft receives. That is a statement about EVIDENCE, so it cuts both ways: a
denial needs the same record an assertion does, and a switch is not a record of
what moved. The old heading asserted a transmission brisk had no record of.

Two further facts settled it. It was the only string in the whole topic, in
either language, asserting a transmission **brisk holds no record of**, and the
only one naming **Windows as the recipient** of one — while the sentence
directly beneath it says *"what leaves this machine is not something those
reads can tell you"*, so the mitigation worked by contradicting the heading it
mitigated. And it was **wrong about the switches it headed**: an advertising ID
is a number apps on this machine read, tailored experiences is Windows using
data it already holds, and the location consent governs what apps here may ask
for. Three of the six are not about anything being sent to Windows, so the
heading did not merely outrun its evidence, it mislabelled half of its own
block.

**What the principle does NOT say**, because the first draft of this block said
it and brisk's own copy refutes it: it does not say brisk never names a
transmission. `rule.delivery-optimization.*` names one in six English strings
and their six Turkish twins — *"Windows uploaded data from this machine to
other machines this month"* — and is right to. brisk read Windows' own running
counter of those bytes, and that copy names *"other machines"* as the
recipient, never Windows and never a company. **A claim standing on a counter
brisk read is a claim brisk earned.** So the upload vocabulary is absent from
the ban by decision, not by oversight, and
`PrivacyRedLineTests.TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned`
makes that decision checkable in both directions: put "upload" on the list and
the ban goes red on six shipped strings that are right; take the transmission
out of this rule's copy and the control goes red instead.

**The warrant does not cover all six evenly**, and this block first claimed it
for "six English strings" without saying so — the same absence one level down.
`Reported()` builds `title`, `evidence` and `headline.caption`, and runs only
when the probe returned a positive count: those three are written with the
counter in brisk's hands. `Unread()` builds `title.unread` and
`evidence.unread`, and is reached exactly when the counter could **not** be
read. Those two are permitted on a different ground: they name the transmission
only to say brisk could not measure it, and `evidence.unread` refuses the count
in as many words — *"a machine that uploaded nothing and a machine brisk could
not ask are different things, so brisk does not report a count of none."*
Naming a quantity you failed to obtain asserts no transmission, so the
principle is satisfied there rather than excepted. `advice` rides both rows,
because `HealthViewModel` keys it on the rule id; it names the upload as what
the counter *measures*, which is true either way.

**Item 2 also said** *"Recall appears here as state only, with a link to
Windows' own setting"*, and that clause is struck above because the page does
not put it here. `PrivacyViewModel.Band` sends a finding carrying a Headline to
the disclosure band and a headless one to "what brisk could not read";
`recall-status` carries one whenever its policy reads either way, so it lands
in **block 1** with the numbers, and in the unreadable band when brisk cannot
read the policy. The link to Windows' own setting rides on that row wherever it
lands, and the reason brisk shows the state without offering to change it is
unchanged and still given under Rules.

`privacy.switches.note` lost its opening sentence with it — *"These are the
switches Windows keeps for it"* pointed at the claim that is gone. The limit
that sentence exists for is untouched and still sits directly under the
heading.

Disclosure findings enter the existing `RevelationPicker` lottery, so the
Overview headline can lead with *"47 cihaz"* on a machine where that is the
most striking number. The shock should not require navigating to find it.

## Report card

Disclosure findings appear on the card under the existing rule: counts, never
contents. The card's "okuyamadıklarım" section gains any probe that could not
read its source.

**Corrected 2026-08-26, after Task 8 shipped.** Two things this section did not
say. The second was a live defect rather than a gap in the prose.

- **The card leads with five numbers and counts the rest** on a line of its
  own. "Appear on the card" was written while exactly five shipped rules
  carried a headline and all five fitted; this wave brought that count to nine.
- **A sixth was being drawn past the frame's edge and never seen** on any card
  whose other sections were full: measured at 758px against the 715px the body
  column gets. The card is a fixed 1600x900 with nothing in it that scrolls,
  and that column had no bound at all — the comment that claimed one was
  describing a coincidence, not a mechanism. Task 8 put the bound in the model:
  five finding rows, then a counted line, and the fix list gives up one row for
  every line the sections above it take, because an unread sentence and a fix
  row are the same height (28.61px, measured).

  **That trade has a floor, so it is not itself a bound**, and this paragraph
  said it without the qualification the source file was made to carry. The fix
  list is never taken below one row, so on a card also carrying the overflow
  line the floor is reached at nine unread lines and the column grows again
  past that. Unreachable on the shipped rules — the section's ceiling is the
  sensor line plus one per report-only disclosure — and `TheTrade_HasHeadroom
  ForEveryUnreadLineTheShippedRulesCanProduce` derives that ceiling from the
  registry and fails when it stops fitting.

  **Corrected 2026-08-26, in Task 10, which took the margin.** One term of the
  trade was not even while this paragraph first said it was: the overflow line
  measured 34.61px and was charged one 28.61px row, because it wore the finding
  rows' 12px bottom margin rather than the 6px used by the small rows it is
  styled like. `FindingsMore` in `Views/ReportCard.xaml` wears `0,0,0,6` now —
  one attribute, no other geometry — so all three traded rows are 28.61px and
  the six pixels went back to the column the worst card is measured in. Those
  figures are re-measured on the real control by
  `TheRowHeightsTheBudgetTrades_AreTheOnesFixBudgetsDocClaims`, which holds the
  gap between the traded rows at zero, because a measured number written down
  with nothing checking it is how this card's clipping stayed invisible for a
  wave.

The lottery placement, which this section did not name either: `usb-history`
is third on `RevelationPicker.Priority`, after `boot-degradation` and
`display-refresh`. brisk leads with a measurement the user can act on today,
and those two are; the USB count is the strongest number brisk owns that the
user can do nothing about, so it leads the moment nothing actionable outranks
it. The other three disclosures stay off the list, which is the tail rank.

## Guards

The red lines above are tests, not comments. **Named here as of Task 9**,
because a guard nobody can find is a guard the next reader writes a second
copy of:

- no string this topic ships — rule copy, read-back sentence or page heading,
  in either language — makes **a transmission claim brisk holds no record of**.
  Not *"Microsoft can no longer see this"*, and not the assertion either, since
  a denial needs the same record an assertion does:
  `PrivacyRedLineTests.NoPrivacyCopy_ClaimsAnythingAboutWhatAnybodyElseSees`,
  over every `rule.<privacy id>.*`, `readback.*` and `privacy.*` key in both
  resx files, with `ThePrivacyCopyGuard_ReachesAllThreeFamilies_AndCanFire` as
  its control in both directions. The one claim that DOES have a record —
  delivery-optimization's, off Windows' own upload counter — is deliberately
  allowed, and `TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned` holds
  that exception open so it stays a decision rather than a gap
- no finding produced by this wave lowers the health score:
  `PrivacyRedLineTests.EveryPrivacyRule_ReportsANoticeAndCostsTheScoreNothing`,
  over `DiagnosticRuleRegistry.All` intersected with `PrivacyRuleIds.All`, so
  an eleventh privacy rule is covered the day it is registered
- the report card carries counts and never a device or program name:
  `ReportCardModelTests.TheCard_CarriesCounts_AndNeverADeviceOrAProgramName`
  and `PrivacyBan_EvidenceNamesAndPathsNeverReachTheCard`, over a card the real
  rules built from a registry with a real device name in it — and since the
  amendment, over a snapshot that CARRIES those names in `UsbDevices` rather
  than merely having had them in reach
- the amended half, both directions in one fixture:
  `ReportCardModelTests.TheDeviceName_RendersOnThePrivacyPage_AndOnNothingThe
  CardCarries` hands ONE such snapshot to `PrivacyViewModel` and to
  `ReportCardModel`, and asserts the model name renders in `UsbDeviceRows` and
  reaches neither the card's text nor any headline. One fixture and not two,
  because a rule split across two files can drift into disagreeing about which
  surface is which
- a probe that fails produces "unreadable", never zero:
  `PrivacyRedLineTests.NoPrivacyRule_TurnsAnUnreadableProbeIntoANumber`, over
  the same ten rules against a machine that answers nothing
- a fix that does not hold reads back as reverted and never as held, watched
  red by planting a re-enabled setting:
  `ReadBackTests.AFixThatIsNotThereAnyMore_ReadsAsReverted`, with
  `DiagnosticLevel_PolicyWritten_ButTheMachineRecordsAHigherLevel_ReadsAsIgnored`
  holding red line 3 beside it

Plus the house standard: every fixable rule's `Fix`/`Undo` round-trips, and the
new page gets a snapshot render.

## Risks named up front

- **This is the largest wave the project has run.** It runs as two task groups
  on one branch: disclosure first (read-only, no risk), actions second.
- **Recall detection** is new surface and varies between builds. It may land in
  "okuyamadıklarım", and that is an acceptable outcome rather than a failure.
- **Delivery Optimization counters** need a cmdlet or COM call. If unavailable,
  the honest gap is the answer.
- **Diagnostic-data policy on Home** is the classic case where a tool lies. It
  is also exactly what the read-back exists for.
