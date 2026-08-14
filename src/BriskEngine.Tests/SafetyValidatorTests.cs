using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SafetyValidatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-test-").FullName;
    private readonly SafetyValidator _validator = new();

    private CleanupTarget TargetOver(string template, bool contentsOnly = false) => new(
        Id: "test-target", DisplayName: "Test", Level: CleanupLevel.Safe,
        PathTemplates: new List<string> { template }, Category: "Test",
        DeletesContentsNotDirectory: contentsOnly);

    [Fact]
    public void PathInsideTemplate_Allowed()
    {
        var inside = Path.Combine(_root, "cache", "a.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
        File.WriteAllText(inside, "x");
        var result = _validator.Authorize(inside, TargetOver(Path.Combine(_root, "cache")));
        Assert.True(result.Allowed, result.Reason);
    }

    [Fact]
    public void PathOutsideTemplate_Denied()
    {
        var outside = Path.Combine(_root, "elsewhere", "a.tmp");
        var result = _validator.Authorize(outside, TargetOver(Path.Combine(_root, "cache")));
        Assert.False(result.Allowed);
    }

    [Fact]
    public void JunctionEscapingTemplate_Denied()
    {
        var template = Path.Combine(_root, "cache");
        var outside = Path.Combine(_root, "victim");
        Directory.CreateDirectory(template);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "doc.txt"), "x");
        var junction = Path.Combine(template, "jump");
        // Junctions need no admin rights; mklink is a cmd builtin.
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);

        var result = _validator.Authorize(Path.Combine(junction, "doc.txt"), TargetOver(template));
        Assert.False(result.Allowed);
        Assert.Contains("allowlist", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtectedFolder_DeniedEvenWhenTemplateCoversIt()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var testFile = Path.Combine(documents, "brisk-test-novel.docx");
        try
        {
            File.WriteAllText(testFile, "test");
            var result = _validator.Authorize(testFile, TargetOver(documents));
            Assert.False(result.Allowed);
            Assert.Contains("protected", result.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(testFile); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void UserProfileRootItself_Denied()
    {
        var profileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = _validator.Authorize(profileRoot, TargetOver(profileRoot));
        Assert.False(result.Allowed);
        Assert.Contains("protected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContentsOnlyTarget_TemplateItselfDenied_ChildAllowed()
    {
        var template = Path.Combine(_root, "cache2");
        Directory.CreateDirectory(template);
        File.WriteAllText(Path.Combine(template, "a.tmp"), "x");
        var target = TargetOver(template, contentsOnly: true);
        Assert.False(_validator.Authorize(template, target).Allowed);
        Assert.True(_validator.Authorize(Path.Combine(template, "a.tmp"), target).Allowed);
    }

    [Fact]
    public void NonexistentPathInsideTemplate_Denied()
    {
        var template = Path.Combine(_root, "cache3");
        Directory.CreateDirectory(template);
        var nonexistent = Path.Combine(template, "does", "not", "exist.tmp");
        var result = _validator.Authorize(nonexistent, TargetOver(template));
        Assert.False(result.Allowed);
        Assert.Contains("verified", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VeryLongPath_IsStillAuthorized()
    {
        var template = Path.Combine(_root, "longpaths");
        Directory.CreateDirectory(template);

        // Build a chain of nested directories with long names to exceed 1100 characters total
        var currentPath = template;
        var pathLength = currentPath.Length;
        var namePart = new string('a', 80); // Long directory name
        while (pathLength < 1100)
        {
            currentPath = Path.Combine(currentPath, namePart);
            pathLength = currentPath.Length;
        }

        // Create the full directory chain
        Directory.CreateDirectory(currentPath);
        var filePath = Path.Combine(currentPath, "file.tmp");
        File.WriteAllText(filePath, "x");

        var result = _validator.Authorize(filePath, TargetOver(template));
        Assert.True(result.Allowed, result.Reason);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
