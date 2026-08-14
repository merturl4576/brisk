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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
