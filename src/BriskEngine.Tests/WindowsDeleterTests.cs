using System.Diagnostics;
using System.IO;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

public sealed class WindowsDeleterTests
{
    [Fact]
    public void Delete_RootThatIsAJunction_RemovesTheLinkAndNeverTheTarget()
    {
        var root = Directory.CreateTempSubdirectory("brisk-junction-").FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var link = Path.Combine(root, "link");
        using (var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
               { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
            mk.WaitForExit();
        Assert.True(Directory.Exists(link));

        new WindowsDeleter().Delete(link);

        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        Assert.False(Directory.Exists(link));
        Directory.Delete(root, true);
    }
}
