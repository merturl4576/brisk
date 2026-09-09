# The disk under everything — Design

**Status:** approved in chat 2026-09-09 ("sıradakinden devam et").
**Branch:** `feat/honest-gauge`, lands with 0.10.0 beside the honest gauge.

## Why

The second outside test (the maintainer's father's PC, 2026-09-09) was a
machine its owner calls very slow. After the honest gauge, brisk charges its
boot; it still had nothing to say about *why*. Every rule brisk owned was a
setting or a timing. The disk under all of them — the likeliest reason a PC
of that age crawls — was not a thing brisk looked at, so the one sentence the
owner needed ("this is the disk, and an SSD is the fix") was never said.

## What changes

1. **A probe that asks the disk.** `IDiskInfoProbe.MediumOf(driveRoot)`
   answers `Rotational`, `Solid` or `Unknown`. The real probe asks the kernel
   twice, no WMI, no elevation: which physical disk holds the volume
   (`IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`), then whether that disk pays a
   seek penalty (`IOCTL_STORAGE_QUERY_PROPERTY`,
   `StorageDeviceSeekPenaltyProperty`). That is the reading Task Manager
   turns into its HDD/SSD label. A volume over more than one disk, a bridge
   that will not say, a letter with nothing behind it: `Unknown`.
   Verified live, non-elevated, on build 26200: NVMe under C: → one extent,
   disk 0, no seek penalty.

2. **A rule that names it.** `system-disk-hdd`, Advise, fires only on
   `Rotational` for the drive Windows actually runs from
   (`Environment.SystemDirectory`'s root, not a fixed C:). `Unknown` is
   silent: brisk never grades on a guess.

3. **A notice with a price.** Kind Notice — brisk will not swap a drive, so
   no button. ImpactClass Measured — the drive reported the reading, so it
   charges: Warning × 4 stars = 12 points. Not Critical: the boot rule
   already charges the symptom (up to 25), and this one names the cause;
   Critical on both would take 50 for one part. The two are never joined —
   nothing says how many boot seconds the disk is worth, because nothing
   measured that.

4. **Copy that sells nothing brisk does.** The evidence names the SSD as
   the one upgrade, points at Task Manager > Performance where the same
   reading shows, and says in the same breath that nothing brisk cleans or
   switches changes it. Tests refuse "faster", "speed up", "hızlandır" in
   both languages.

5. **Filed beside the boot.** `FindingSections` routes it to Performans, next
   to the boot finding it explains, rather than the Sağlık default.

## What does not change

- No headline: there is no number to lead with, and no surface invents one.
- `RevelationPicker` is untouched; a finding without a headline never leads.
- The Hygiene/Measured line holds: memory below its rating stays a free
  notice, because nothing measured says the owner feels it. A seek penalty
  is felt on every open, and the drive itself said so.

## Tests

- `SystemDiskHddRuleTests` (6): fires on Rotational as a Measured Warning
  notice costing 12; silent on Solid and Unknown; asks about the system
  root; evidence names SSD and Task Manager, carries the letter, no headline;
  registered once.
- `RealDiskInfoProbeTests` (3): live smoke — the system disk answers one of
  the three without throwing; a letter with no volume and a non-root path are
  Unknown.
- `LocTests`: the three keys exist in both languages; the copy names the SSD,
  points at Task Manager, and promises nothing, en and tr.
- `FindingSectionsTests` (1): the id is a Performance finding.
