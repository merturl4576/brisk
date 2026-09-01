using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SizeCalculatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-sz-").FullName;

    [Fact]
    public void SizesNestedFiles()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "sub", "b.bin"), new byte[50]);
        Assert.Equal(150, SizeCalculator.SizeOf(_root));
    }

    [Fact]
    public void MissingPath_IsZero()
    {
        Assert.Equal(0, SizeCalculator.SizeOf(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void DoesNotTraverseJunctions()
    {
        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        File.WriteAllBytes(Path.Combine(big, "big.bin"), new byte[1000]);
        var scanned = Path.Combine(_root, "scanned");
        Directory.CreateDirectory(scanned);
        File.WriteAllBytes(Path.Combine(scanned, "own.bin"), new byte[10]);
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{Path.Combine(scanned, "jump")}\" \"{big}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(10, SizeCalculator.SizeOf(scanned));
    }

    [Fact]
    public void RootThatIsAJunction_IsNotTraversed()
    {
        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        File.WriteAllBytes(Path.Combine(big, "big.bin"), new byte[1000]);
        var link = Path.Combine(_root, "link");
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{big}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, SizeCalculator.SizeOf(link));
    }

    /// The same walk SizeOf does, asked a second question on the way past:
    /// how big, and which files were the big ones. The floor is what keeps
    /// the answer worth reading — a folder holds thousands of files and ten
    /// of them are the story.
    [Fact]
    public void StatsOf_SumsTheSameBytes_AndNamesTheLargestAboveTheFloor()
    {
        File.WriteAllBytes(Path.Combine(_root, "small.bin"), new byte[2]);
        File.WriteAllBytes(Path.Combine(_root, "mid.bin"), new byte[5]);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "sub", "big.bin"), new byte[9]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "third.bin"), new byte[4]);

        var stats = SizeCalculator.StatsOf(_root, minFileBytes: 3, take: 2);

        Assert.Equal(20, stats.Bytes);
        Assert.Equal(SizeCalculator.SizeOf(_root), stats.Bytes);
        Assert.Equal(new[] { 9L, 5L }, stats.Largest.Select(f => f.Bytes).ToArray());
        Assert.Equal(Path.Combine(_root, "sub", "big.bin"), stats.Largest[0].Path);
        Assert.Equal(Path.Combine(_root, "mid.bin"), stats.Largest[1].Path);
        Assert.All(stats.Largest, f => Assert.Equal(DateTimeKind.Utc, f.WriteUtc.Kind));
    }

    /// A floor nothing clears is not an error: the size still answers.
    [Fact]
    public void StatsOf_NothingClearsTheFloor_StillSizesTheFolder()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[100]);

        var stats = SizeCalculator.StatsOf(_root, minFileBytes: 1000, take: 10);

        Assert.Equal(100, stats.Bytes);
        Assert.Empty(stats.Largest);
    }

    /// Junctions are not traversed here either — a folder that names files
    /// behind a junction would name files it does not hold.
    [Fact]
    public void StatsOf_DoesNotTraverseJunctions()
    {
        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        File.WriteAllBytes(Path.Combine(big, "big.bin"), new byte[1000]);
        var scanned = Path.Combine(_root, "scanned");
        Directory.CreateDirectory(scanned);
        File.WriteAllBytes(Path.Combine(scanned, "own.bin"), new byte[10]);
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{Path.Combine(scanned, "jump")}\" \"{big}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();

        var stats = SizeCalculator.StatsOf(scanned, minFileBytes: 1, take: 10);

        Assert.Equal(10, stats.Bytes);
        Assert.Equal(new[] { Path.Combine(scanned, "own.bin") },
            stats.Largest.Select(f => f.Path).ToArray());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
