# Who holds the antivirus job, and the file brisk could not read — Design

**Status:** approved in chat 2026-09-09 ("devam et o işlere de").
**Branch:** `feat/honest-gauge`, lands with 0.10.0.

Three leftovers from the two outside tests and the 2026-09-07 live run,
done as one wave.

## 1. A third-party antivirus holds the job

**Why.** The first outside tester's laptop wore a red McAfee shield in its
tray, and brisk's launch died with "cancelled by the user" for a reason
nobody could name. A third-party antivirus is the commonest thing on a
consumer laptop that both slows every file open and blocks an unsigned
program without a word. brisk had no sentence for it.

**Probe.** `IDefenderProbe.Registered()` returns every row of Security
Center's antivirus table (`root\SecurityCenter2`, `AntiVirusProduct`),
readable without elevation, empty on Server or on any failure.
`AntivirusState.Decode` reads productState's on/off bit (0x1000) and
out-of-date bit (0x10); `AntivirusState.IsDefender` knows Microsoft's row by
its `windowsdefender://` path first and its name second. Verified live on
the maintainer's machine: one row, Windows Defender, productState 397568.

**Rule.** `third-party-av`, Advise, fires when any non-Defender product is
listed. Notice + Hygiene: a product someone chose is a fact, not a fault,
and the gauge does not move. The severity is the sentence's weight: on and
current is Info; definitions out of date is Warning; a product that reports
itself off — the expired-trial shape, listed as the machine's antivirus and
scanning nothing — is Warning. The worst state decides the variant, and
every product is named. Every variant ends "brisk changes nothing here; it
reports". Routed to Sağlık by default; not a PrivacyDisclosureRule, so the
privacy page's bands are untouched.

## 2. The Local State brisk could not read

**Why.** `HardwareAccelerationRule` swallowed an unparsable Chrome or Edge
`Local State` (`catch {}`) and reported nothing — the same silence as a
browser with acceleration on. Noted on the 2026-09-07 live run.

**What changes.** A file that exists and does not parse is a fact of its
own. Nothing readable off: a free Info notice under its own title, "A
browser's settings file could not be read", naming the browser, no button.
One browser off and the other unreadable: the fixable problem as before,
its evidence naming both, and the fix writing only the file it could read.
A missing file is still silence — that browser is not installed.

## 3. get.ps1 in English

The one-liner's script was the only file in the repo written in Turkish,
comments and messages alike, while the README promises "readable lines" to
an English reader and the announcement goes to an English forum. Rewritten
in English, same 63 lines of behaviour, verified again with the stubbed
network-free harness in three modes.

## Tests

- `ThirdPartyAntivirusRuleTests` (7): silent with nothing listed and with
  Defender alone; on → free Info notice naming the product and Defender;
  off → Warning naming the expired trial; outdated → Warning; two products
  take the worse state and name both; registered once.
- `AntivirusStateTests`: five productState values including the live one;
  Defender recognised by path, then name.
- `HardwareAccelerationRuleTests` (+2): the unreadable-only notice; the
  mixed case names both and fixes only what it read.
- `LocTests`: eight new keys in both languages; the antivirus copy names the
  product and Defender and promises to change nothing, en and tr.
- The registry count moves to 33.
