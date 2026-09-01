using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

/// The live workbench (2026-09-01) printed "SHFileOperation failed (120)"
/// fourteen times for a cache the shell is not allowed to touch. Nobody
/// should have to look 0x78 up to learn what happened to their machine.
public class WindowsRecyclerTests
{
    [Theory]
    [InlineData(0x78, "access denied at the source")]                 // DE_ACCESSDENIEDSRC — the live DO-cache failure
    [InlineData(0x7C, "the path is invalid or the item is in use")]   // DE_INVALIDFILES — the live thumbcache failure
    [InlineData(0x74, "the source is a root directory")]
    [InlineData(0x75, "the operation was cancelled")]
    [InlineData(0x79, "the path is too deep")]
    [InlineData(0x81, "the name is too long")]
    [InlineData(0x86, "a sharing violation")]
    [InlineData(0x402, "an unknown shell error")]
    [InlineData(999999, "an unknown shell error")]
    public void Describe_turns_shell_codes_into_words(int code, string expected)
        => Assert.Equal(expected, WindowsRecycler.Describe(code));
}
