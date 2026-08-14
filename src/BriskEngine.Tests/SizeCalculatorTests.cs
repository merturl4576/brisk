using System;
using System.Diagnostics;
using System.IO;
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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
