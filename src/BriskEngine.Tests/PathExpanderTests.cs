using System;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public class PathExpanderTests
{
    [Fact]
    public void Expand_LocalAppData()
    {
        var result = PathExpander.Expand(@"%LOCALAPPDATA%\Temp");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp",
            result);
    }

    [Fact]
    public void Expand_TildeIsUserProfile()
    {
        var result = PathExpander.Expand(@"~\.cargo");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.cargo",
            result);
    }

    [Fact]
    public void Expand_UndefinedVariable_ReturnsNull()
    {
        Assert.Null(PathExpander.Expand(@"%BRISK_DOES_NOT_EXIST_XYZ%\x"));
    }

    [Fact]
    public void Expand_PlainAbsolutePath_Unchanged()
    {
        Assert.Equal(@"C:\Windows\Temp", PathExpander.Expand(@"C:\Windows\Temp"));
    }
}
