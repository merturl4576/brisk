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

    /// profile-git removes ~\.git and nothing that only shares its name or
    /// prefix. "~" is a temp home here: the real ~\.git is a live repository.
    [Fact]
    public void Profile_git_is_authorized_only_as_itself()
    {
        var registered = BriskEngine.Cleaning.CleanupTargetRegistry.All.Single(t => t.Id == "profile-git");
        Assert.Equal(new[] { @"~\.git" }, registered.PathTemplates);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".git"),
            BriskEngine.Paths.PathExpander.Expand(registered.PathTemplates[0]));

        var home = Path.Combine(_root, "home");
        var git = Directory.CreateDirectory(Path.Combine(home, ".git")).FullName;
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main");
        var gitconfig = Path.Combine(home, ".gitconfig");
        File.WriteAllText(gitconfig, "[user]");
        var nested = Directory.CreateDirectory(Path.Combine(home, "Documents", "x", ".git")).FullName;
        var target = registered with { PathTemplates = new[] { git } };

        Assert.True(_validator.Authorize(git, target).Allowed);
        Assert.False(_validator.Authorize(gitconfig, target).Allowed);
        Assert.False(_validator.Authorize(nested, target).Allowed);
    }

    private static CleanupTarget Registered(string id) =>
        BriskEngine.Cleaning.CleanupTargetRegistry.All.Single(t => t.Id == id);

    /// A Node project in `projects` whose package.json was last written 200 days ago.
    private static string StaleNodeProject(string projects)
    {
        var modules = Directory.CreateDirectory(Path.Combine(projects, "app", "node_modules")).FullName;
        File.WriteAllText(Path.Combine(modules, "index.js"), "x");
        var manifest = Path.Combine(projects, "app", "package.json");
        File.WriteAllText(manifest, "{}");
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddDays(-200));
        return modules;
    }

    /// Review focus 1: the scan's verdict is not trusted. The rule is asked
    /// again on the real path at delete time — a project touched since the
    /// scan keeps its node_modules.
    [Fact]
    public void Validator_rechecks_rule_at_delete_time()
    {
        var projects = Path.Combine(_root, "projects");
        var modules = StaleNodeProject(projects);
        var validator = new SafetyValidator(() => new[] { projects }, () => DateTime.UtcNow);
        var target = Registered("stale-project-deps");
        Assert.True(validator.Authorize(modules, target).Allowed);

        File.WriteAllText(Path.Combine(projects, "app", "package.json"), "{ \"touched\": true }");

        var result = validator.Authorize(modules, target);
        Assert.False(result.Allowed);
        Assert.Contains("rule", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_denies_dynamic_path_outside_roots()
    {
        var projects = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;
        var modules = StaleNodeProject(Path.Combine(_root, "elsewhere"));
        var validator = new SafetyValidator(() => new[] { projects }, () => DateTime.UtcNow);
        var target = Registered("stale-project-deps");

        var result = validator.Authorize(modules, target);
        Assert.False(result.Allowed);
        Assert.Contains("outside", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(validator.Authorize(projects, target).Allowed);   // a root is never an item

        // The real roots are the user's own folders: a temp path is outside them.
        Assert.False(new SafetyValidator().Authorize(modules, target).Allowed);
    }

    [Fact]
    public void Validator_denies_dynamic_path_in_a_system_folder()
    {
        var system32 = Environment.SystemDirectory;
        var validator = new SafetyValidator(() => new[] { system32 }, () => DateTime.UtcNow);

        var result = validator.Authorize(Path.Combine(system32, "drivers"), Registered("stale-project-deps"));

        Assert.False(result.Allowed);
        Assert.Contains("protected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// An archive is deleted only while its extracted copy still sits beside
    /// it: a copy removed after the scan keeps the archive.
    [Fact]
    public void Validator_keeps_an_archive_whose_extracted_copy_is_gone()
    {
        var downloads = Path.Combine(_root, "downloads");
        var archive = DynamicTargetsTests.Sparse(Path.Combine(downloads, "kali.7z"), 2L << 30);
        DynamicTargetsTests.Sparse(Path.Combine(downloads, "kali", "kali.vdi"), 3L << 30);
        var validator = new SafetyValidator(() => new[] { downloads }, () => DateTime.UtcNow);
        var target = Registered("archive-beside-extracted");
        Assert.True(validator.Authorize(archive, target).Allowed);

        Directory.Delete(Path.Combine(downloads, "kali"), recursive: true);

        Assert.False(validator.Authorize(archive, target).Allowed);
    }

    /// Desktop and Documents stay shut to every path template; a dynamic
    /// target may reach into them, and into nothing else that is protected.
    /// Strings only: nothing on the real Desktop is created or touched.
    [Fact]
    public void Dynamic_rule_opens_only_Desktop_and_Documents()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        bool InOneDrive(string p) => !string.IsNullOrEmpty(oneDrive)
            && p.StartsWith(RealPath.Resolve(oneDrive) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        {
            var inside = Path.Combine(RealPath.Resolve(Environment.GetFolderPath(folder)), "proj", "node_modules");
            Assert.True(ProtectedPaths.IsProtected(inside));
            Assert.Equal(InOneDrive(inside), ProtectedPaths.IsProtected(inside, exceptUserFolders: true));
        }
        var pictures = RealPath.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Assert.True(ProtectedPaths.IsProtected(Path.Combine(pictures, "x"), exceptUserFolders: true));
        Assert.True(ProtectedPaths.IsProtected(RealPath.Resolve(Environment.SystemDirectory), exceptUserFolders: true));
        Assert.True(ProtectedPaths.IsProtected(
            RealPath.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), exceptUserFolders: true));
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
