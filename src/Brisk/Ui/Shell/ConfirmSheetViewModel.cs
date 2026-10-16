using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Shell;

/// The one confirmation: user files that cannot come back are named, with
/// their sizes, before anything runs. Continue, skip them, or look first.
public sealed class ConfirmSheetViewModel
{
    public ConfirmSheetViewModel(IReadOnlyList<ActionItem> items, ActionGroupKind? group, ActionSession session,
        Loc loc, Action close, Action seeList)
    {
        Title = loc["gui.sheet.title"];
        Lines = items.Select(i => Line(i, session, loc)).ToList();
        GoText = loc["gui.sheet.go"];
        SkipText = loc["gui.sheet.skip"];
        ListText = loc["gui.sheet.list"];
        Go = new RelayCommand(() => { close(); _ = session.RunAsync(group); });
        Skip = new RelayCommand(() => { close(); _ = session.RunAsync(group, skipUnrecoverable: true); });
        SeeList = new RelayCommand(() => { close(); seeList(); });
    }

    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
    public string GoText { get; }
    public string SkipText { get; }
    public string ListText { get; }
    public RelayCommand Go { get; }
    public RelayCommand Skip { get; }
    public RelayCommand SeeList { get; }

    private static string Line(ActionItem item, ActionSession session, Loc loc)
    {
        var size = Sizes.Format(session.SelectedBytes(item), loc.Culture);
        return item.Id switch
        {
            "space.downloads" => loc.F("gui.sheet.downloads", item.Files.Count(f => session.IsFileTicked(f.Path)), size),
            "space.recycle-bin" => loc.F("gui.sheet.recycle-bin", size),
            "space.windows-old" => loc.F("gui.sheet.windows-old", size),
            _ => $"{(item.TitleKey is null ? item.Title : loc.Title(item.TitleKey, item.Title))} · {size}",
        };
    }
}
