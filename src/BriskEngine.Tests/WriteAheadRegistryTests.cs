using System;
using System.IO;
using BriskEngine.Actions;
using Xunit;

namespace BriskEngine.Tests;

public sealed class WriteAheadRegistryTests : IDisposable
{
    private const string K = @"HKCU\Software\BriskTest";
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-wal-").FullName;
    private string Wal => Path.Combine(_root, "wal.jsonl");

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void RollBack_RestoresEveryTouchedValue()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        reg.SetString(K, "C", "x");
        var wal = new WriteAheadRegistry(reg, Wal);

        wal.SetInt(K, "A", 0);
        wal.SetInt(K, "B", 5);
        wal.SetString(K, "C", "y");
        Assert.Equal(0, wal.RollBack());

        Assert.Equal(1, reg.GetInt(K, "A"));
        Assert.Null(reg.GetInt(K, "B"));
        Assert.Equal("x", reg.GetString(K, "C"));
        Assert.False(File.Exists(Wal));
    }

    [Fact]
    public void OnlyTheFirstWriteOfAValueIsRecorded()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        var wal = new WriteAheadRegistry(reg, Wal);

        wal.SetInt(K, "A", 0);
        wal.SetInt(K, "A", 7);
        Assert.Single(wal.Priors);
        wal.RollBack();

        Assert.Equal(1, reg.GetInt(K, "A"));
    }

    [Fact]
    public void AWriteThatThrows_ChangesNothing_AndRollBackDoesNotThrow()
    {
        var reg = new FakeRegistry();
        reg.DenyWriteKeys.Add(K);
        var wal = new WriteAheadRegistry(reg, Wal);

        Assert.Throws<UnauthorizedAccessException>(() => wal.SetInt(K, "A", 1));
        Assert.Equal(0, wal.RollBack());
        Assert.Null(reg.GetInt(K, "A"));
    }

    [Fact]
    public void RecoverPending_RestoresWhatACrashedRunHalfWrote()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        var crashed = new WriteAheadRegistry(reg, Wal);
        crashed.SetInt(K, "A", 0);
        crashed.SetInt(K, "B", 9);
        // no Commit, no RollBack: the process died here

        Assert.Equal(2, WriteAheadRegistry.RecoverPending(reg, Wal));
        Assert.Equal(1, reg.GetInt(K, "A"));
        Assert.Null(reg.GetInt(K, "B"));
        Assert.False(File.Exists(Wal));
        Assert.Equal(0, WriteAheadRegistry.RecoverPending(reg, Wal));
    }

    [Fact]
    public void Commit_KeepsTheWrites_AndDropsTheRecord()
    {
        var reg = new FakeRegistry();
        var wal = new WriteAheadRegistry(reg, Wal);
        wal.SetInt(K, "A", 3);
        wal.Commit();

        Assert.Equal(3, reg.GetInt(K, "A"));
        Assert.Empty(wal.Priors);
        Assert.False(File.Exists(Wal));
    }

    [Fact]
    public void ABinaryValueRoundTrips()
    {
        var reg = new FakeRegistry();
        reg.SetBytes(K, "Bin", new byte[] { 2, 0, 0 });
        var wal = new WriteAheadRegistry(reg, Wal);
        wal.SetBytes(K, "Bin", new byte[] { 3, 0, 0 });
        wal.RollBack();
        Assert.Equal(new byte[] { 2, 0, 0 }, reg.GetBytes(K, "Bin"));
    }
}
