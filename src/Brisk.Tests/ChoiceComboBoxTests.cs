using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Brisk.Localization;
using Brisk.Tests.Snapshots;
using Brisk.ViewModels;
using Xunit;
// WinForms is on in this project, so these are ambiguous bare.
using ComboBox = System.Windows.Controls.ComboBox;
using DataTemplate = System.Windows.DataTemplate;
using TextBlock = System.Windows.Controls.TextBlock;
using Binding = System.Windows.Data.Binding;
using Border = System.Windows.Controls.Border;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace Brisk.Tests;

/// The settings dropdowns, on a real ComboBox rather than on the view model
/// that feeds one. SecondaryViewModelTests can say the view model did its part
/// and be right while the window is still wrong: the live build held an
/// English list and drew "Koyu" in the closed Theme box. Only a real ComboBox
/// can be asked what it does with what the view model announces.
public sealed class ChoiceComboBoxTests
{
    private static Loc English()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static ComboBox Built(IReadOnlyList<ChoiceOption> items)
    {
        var template = new DataTemplate(typeof(ChoiceOption));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        template.VisualTree = text;

        return new ComboBox
        {
            ItemsSource = items,
            SelectedValuePath = "Value",
            ItemTemplate = template,
        };
    }

    private static IReadOnlyList<ChoiceOption> Options(Loc loc) => new[]
    {
        new ChoiceOption("system", "settings.value.system", loc),
        new ChoiceOption("light", "settings.value.light", loc),
        new ChoiceOption("dark", "settings.value.dark", loc),
    };

    /// The fix, at the seam it had to work at, asserted on the TEXT THE BOX
    /// ACTUALLY DRAWS rather than on the option's own property. Label is read
    /// live, so asserting it after a language change proves nothing at all —
    /// it would read the new language whether or not anything was announced,
    /// and WPF would still be showing the old string. So the box is laid out
    /// and the TextBlock inside its closed selection box is read directly.
    [Fact]
    public void TheClosedBoxRedrawsItsLabel_WithoutTheListMoving()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var loc = English();
            var box = Built(Options(loc));
            box.SelectedValue = "dark";
            LayOut(box);
            var selected = box.SelectedItem;
            Assert.Equal("Dark", DrawnLabel(box));

            loc.SetLanguage("tr");
            foreach (var option in (IReadOnlyList<ChoiceOption>)box.ItemsSource)
                option.Relabel();
            box.UpdateLayout();

            Assert.Same(selected, box.SelectedItem);      // nothing moved...
            Assert.Equal("Koyu", DrawnLabel(box));        // ...and it redrew
        });
    }

    private static void LayOut(ComboBox box)
    {
        var host = new Border { Child = box, Width = 240, Height = 32 };
        host.Measure(new Size(240, 32));
        host.Arrange(new Rect(0, 0, 240, 32));
        host.UpdateLayout();
    }

    /// The one TextBlock in the CLOSED box: the popup is never opened here, so
    /// the item containers for the list itself are not realized.
    private static string DrawnLabel(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text && !string.IsNullOrEmpty(text.Text))
                return text.Text;
            var found = DrawnLabel(child);
            if (found.Length > 0) return found;
        }
        return "";
    }

    /// The route NOT taken, pinned so nobody takes it later. Rebuilding the
    /// lists in the new language is the obvious fix and it breaks the box: the
    /// old selection is in neither list, so the ComboBox drops the selection
    /// entirely. SettingsPage binds SelectedValue TwoWay, so that null goes
    /// back over the stored setting.
    [Fact]
    public void ReplacingTheListWithDifferentlyLabelledOptions_LosesTheSelection()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var english = English();
            var box = Built(Options(english));
            box.SelectedValue = "dark";
            Assert.NotNull(box.SelectedItem);

            var turkish = new Loc();
            turkish.SetLanguage("tr");
            box.ItemsSource = Options(turkish);

            Assert.Null(box.SelectedItem);
        });
    }
}
