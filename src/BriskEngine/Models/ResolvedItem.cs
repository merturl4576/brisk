using System;

namespace BriskEngine.Models;

/// Locked (additive, round 11): the scan-time delete-lock probe found this
/// item — or something inside it — held by a running process or denied by
/// ACLs, so a recycle attempt would predictably fail right now. The clean
/// still ATTEMPTS locked items (locks can clear between scan and clean);
/// only the PROMISE excludes them.
public sealed record ResolvedItem(string TargetId, string Path, long Bytes,
    DateTime? LastWriteUtc, bool Locked = false);
