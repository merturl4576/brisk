using System;
using System.Windows;
using System.Windows.Automation;
using Brisk.Localization;
using Brisk.Tests.Snapshots;
using Xunit;
// WinForms is on in this project, so a bare Button is ambiguous.
using Button = System.Windows.Controls.Button;

namespace Brisk.Tests;

/// brisk draws its own caption buttons now, and Windows named those buttons
/// before we took them over. A drawn button announces whatever we tell it to
/// announce — which, left unsaid, is a private-use-area codepoint from an
/// icon font. So the names are asserted here rather than assumed, on the real
/// MainWindow rather than on a lookalike.
///
/// The middle button is the interesting one: it is two buttons wearing one
/// control, Maximize on a normal window and Restore on a maximized one, and a
/// name frozen at "Maximize" would be wrong exactly half the time — for the
/// users least able to check the name against the picture.
public sealed class CaptionButtonTests
{
    /// The window is built but never SHOWN, and that is deliberate rather
    /// than lazy. Physically maximizing a window needs a real HWND, and
    /// Windows maximizes onto the nearest monitor — so a faithful "click
    /// maximize" test would throw a full-screen brisk across the developer's
    /// desktop mid-run. What is driven instead is the other trigger the app
    /// already wires to the very same method: Loc's Item[] change. The
    /// language is re-set to the one already in force, so nothing about the
    /// singleton actually moves and no parallel test can see a flicker of
    /// Turkish — but UpdateMaximizeButton runs, reads the live WindowState,
    /// and has to move all three properties together.
    [Fact]
    public void MaximizeButton_ReadsTheWindowState_AndMovesGlyphNameAndTooltipTogether()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var window = SnapshotTests.CockpitWindow();
            var button = (Button)window.FindName("MaximizeButton")!;

            AssertNamed(button, "Maximize");
            var normalGlyph = button.Content;

            window.WindowState = WindowState.Maximized;
            Republish();
            AssertNamed(button, "Restore");
            Assert.NotEqual(normalGlyph, button.Content);

            window.WindowState = WindowState.Normal;
            Republish();
            AssertNamed(button, "Maximize");
            Assert.Equal(normalGlyph, button.Content);
        });
    }

    /// The two buttons whose name never changes take it from a binding, so
    /// what this catches is a mistyped key: Loc answers a miss with the key
    /// itself, which would put "chrome.close" in the tooltip and in a screen
    /// reader's mouth without failing anything else in the suite.
    [Theory]
    [InlineData("MinimizeButton", "Minimize")]
    [InlineData("CloseButton", "Close")]
    public void TheOtherCaptionButtons_AreNamedToo(string element, string expected)
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var window = SnapshotTests.CockpitWindow();
            AssertNamed((Button)window.FindName(element)!, expected);
        });
    }

    /// Name and tooltip are one string, seen twice. A sighted mouse user and
    /// a screen reader user are being told about the same button, so this
    /// asserts they are told the same thing — the failure mode of two keys is
    /// that they drift, and it drifts silently.
    private static void AssertNamed(Button button, string expected)
    {
        var name = AutomationProperties.GetName(button);
        Assert.Equal(expected, name);
        Assert.Equal(name, button.ToolTip);
    }

    /// The failure this pair of fixes exists for, made deterministic.
    ///
    /// A MainWindow subscribes the process-lifetime Loc.Instance singleton
    /// and never unsubscribes, so it stays reachable long after the test that
    /// built it returned. Republishing the language from a thread that is not
    /// the window's own then walks that subscriber list synchronously and
    /// reads WindowState, which VerifyAccess()es — and the throw lands in
    /// whichever test happened to call SetLanguage, not in the one that
    /// leaked the window. LocKeyConverterTests has always had exactly this
    /// shape, and was green only because of the order tests happened to run
    /// in.
    ///
    /// What is asserted is the absence of a throw, and that is the whole
    /// claim: SetLanguage invokes handlers on the caller's thread, so a
    /// window that fails to marshal takes this test down with it.
    [Fact]
    public void RepublishingTheLanguage_OffTheWindowsThread_DoesNotThrowIntoIt()
    {
        SnapshotRenderer.OnUiThread(() => _ = SnapshotTests.CockpitWindow());

        // Deliberately NOT inside OnUiThread: this is the test runner's own
        // thread, which is the foreign one.
        Republish();
    }

    /// Re-announce the language already in force. SetLanguage raises Item[]
    /// unconditionally, which is the notification every localized binding in
    /// the window listens to — and the one MainWindow hooks so the maximize
    /// button's name survives a language switch.
    private static void Republish() => Loc.Instance.SetLanguage("en");
}
