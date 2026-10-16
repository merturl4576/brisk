using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Actions;
using Xunit;

namespace Brisk.Tests;

public class ActionStringsTests
{
    private static string LocalizationDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Localization");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    private static System.Collections.Generic.Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(LocalizationDir(), file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.tr.resx")]
    public void EveryEngineActionKey_IsTranslated(string file)
    {
        var strings = Load(file);
        foreach (var key in ActionCatalog.AllKeys())
            Assert.True(strings.ContainsKey(key), $"{file} has no '{key}'");
    }

    [Fact]
    public void TurkishTitlesAndCostLines_FollowTheCopyRules()
    {
        var tr = Load("Strings.tr.resx");
        foreach (var key in ActionCatalog.AllKeys().Where(k => k.EndsWith(".title") || k.Contains(".cost")))
            Assert.True(tr[key].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6,
                $"'{key}' is longer than six words: {tr[key]}");
    }

    [Fact]
    public void EnglishResxMatchesTheEngineEnglish()
    {
        var en = Load("Strings.resx");
        foreach (var row in ActionCatalog.Rows)
            Assert.Equal(row.Title, en[row.TitleKey]);
        foreach (var (key, english) in ActionStrings.Ui)
            Assert.Equal(english, en[key]);
    }
}
