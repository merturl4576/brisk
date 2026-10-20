using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

public sealed class RealProcessRunnerTests
{
    [Fact]
    public void AChildsErrorStreamIsCaptured_NotLeftOnBrisksConsole() =>
        Assert.True(RealProcessRunner.StartInfo("schtasks.exe", "/Query").RedirectStandardError);

    [Fact]
    public void AChildThatWritesMuchToStandardError_StillReturnsItsOutput()
    {
        // ~400 KB on standard error: more than a pipe holds, so an undrained
        // redirect would block the child and this call forever.
        var (exit, stdout) = new RealProcessRunner().Run("cmd.exe",
            "/c \"for /L %i in (1,1,8000) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx 1>&2\" & echo done");
        Assert.Equal(0, exit);
        Assert.Contains("done", stdout);
    }
}
