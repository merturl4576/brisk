using System;

namespace BriskEngine.Models;

public sealed record ResolvedItem(string TargetId, string Path, long Bytes, DateTime? LastWriteUtc);
