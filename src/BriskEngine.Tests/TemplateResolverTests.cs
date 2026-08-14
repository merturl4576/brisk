using System;
using System.IO;
using System.Linq;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public sealed class TemplateResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-tr-").FullName;

    [Fact]
    public void NoWildcard_ExistingPath_ReturnsItself()
    {
        var result = TemplateResolver.Resolve(_root);
        Assert.Equal(new[] { _root }, result);
    }

    [Fact]
    public void NoWildcard_MissingPath_ReturnsEmpty()
    {
        Assert.Empty(TemplateResolver.Resolve(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void MidSegmentWildcard_EnumeratesDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "p1.default", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "p2.default", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "p3.other")); // no cache2 inside
        var result = TemplateResolver.Resolve(Path.Combine(_root, "*", "cache2"));
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.EndsWith("cache2", p));
    }

    [Fact]
    public void FinalSegmentWildcard_MatchesFiles()
    {
        File.WriteAllText(Path.Combine(_root, "thumbcache_32.db"), "x");
        File.WriteAllText(Path.Combine(_root, "thumbcache_96.db"), "x");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
        var result = TemplateResolver.Resolve(Path.Combine(_root, "thumbcache_*.db"));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DriveRootAdjacentWildcard_EnumeratesTheActualRoot()
    {
        // Regression test for drive-relative path bug: C:\*\foo where parent="C:"
        // Old code: Directory.Exists("C:") is true (current dir on C:), enumerates wrong dir
        // New code: normalizes "C:" to "C:\" before enumeration
        // This test sets up a temp dir that would have matched the buggy code,
        // but should NOT match the fixed code when querying the actual root.

        var origDir = Directory.GetCurrentDirectory();
        var tempBase = Directory.CreateTempSubdirectory("brisk-drive-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tempBase, "a", "brisk-definitely-not-a-real-dir-xyz"));

            // Change to temp base; now "current directory on C:" == tempBase (if on C:)
            Directory.SetCurrentDirectory(tempBase);

            // Query pattern that matches our temp structure, but targets the actual root
            var driveRoot = Path.GetPathRoot(_root) ?? "C:\\";
            var pattern = Path.Combine(driveRoot, "*", "brisk-definitely-not-a-real-dir-xyz");

            // Fixed code: returns empty (pattern doesn't match actual root)
            // Buggy code: would return entries from the current directory
            var result = TemplateResolver.Resolve(pattern);
            Assert.Empty(result);
        }
        finally
        {
            Directory.SetCurrentDirectory(origDir);
            try { Directory.Delete(tempBase, recursive: true); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
