using System;
using System.IO;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

/// The pixel side gets a smoke test, not a pixel test: the PNG exists, is a
/// PNG, and is card-sized. Everything about the card's CONTENT is pinned on
/// the model in ReportCardModelTests.
public class ReportCardRenderTests
{
    [Fact]
    public void Render_WritesAValidPng()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        var model = ReportCardModel.Build(
            TestData.Snapshot(null, new SensorStatus(true, true, null)),
            Array.Empty<UndoableFix>(), loc);
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("brisk-card-").FullName, "card.png");

        ReportCardRenderer.RenderOnStaThread(model, path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 10_000, $"suspiciously small: {bytes.Length} bytes");
        // The eight-byte PNG signature.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            bytes[..8]);
    }
}
