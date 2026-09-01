using System.Collections.Generic;

namespace BriskEngine.Models;

/// ONE file, as the size walk passed it: where it is, how big, and when it
/// was last written. Nothing else — brisk names these files and touches
/// none of them, so there is no handle here to act with.
///
/// Path is the full path, because that is what the walk holds and what a
/// caller needs to shorten it against something. Shortening it is the
/// CALLER's job and the reason it matters: %USERPROFILE% carries the user's
/// name, and the surfaces a finding reaches are built to be shared.
public sealed record LargeFile(string Path, long Bytes, System.DateTime WriteUtc);

/// What one walk of one folder learned: the total, and the biggest files in
/// it. Two answers from one traversal — the size alone was what brisk
/// reported until it turned out that nobody feels a folder total and
/// everybody feels a named 23.5 GB file.
///
/// Largest is already sorted, biggest first, and already cut to the caller's
/// `take`. Empty when nothing in the folder cleared the caller's floor,
/// which is an ordinary answer and not a failure: Bytes still stands.
public sealed record DirectoryStats(long Bytes, IReadOnlyList<LargeFile> Largest);
