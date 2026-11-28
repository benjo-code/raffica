using Avalonia.Automation;
using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// The Table of contents card: the kind (a table of contents of the headings, or a list of figures or tables of one caption label), the first and last level of a table of contents (1 to 9), whether page numbers are shown, whether they
/// stand at the right edge and the leader that leads to them. The levels apply to a table of contents only, the right edge and the leader to the page numbers only. Insert answers with the options; Remove (shown when the caret is in a
/// table of contents) answers Remove; a first level below the last one shows its reason in the hint and keeps the card open. Esc cancels. The card holds no text of the document.
/// </summary>
internal static class TocCard
{
    private static readonly string[] s_levels = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

    /// <param name="context">The caption labels a list can be made of, and whether the caret is in a table of contents.</param>
    /// <param name="done">What the user chose, or null when the card was closed.</param>
    public static Dialog Create(TocContext context, Action<TocRequest?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        var kind = CardParts.Select("PART_Kind", WriteStrings.TocKindLabel, [WriteStrings.TocKindContents, WriteStrings.TocKindList]);
        kind.SelectedIndex = 0;
        var label = CardParts.Select("PART_Label", WriteStrings.TocCaptionLabel, context.Labels);
        label.SelectedIndex = 0;
        var first = CardParts.Select("PART_First", WriteStrings.TocFirstLevel, s_levels);
        first.SelectedIndex = 0;
        var last = CardParts.Select("PART_Last", WriteStrings.TocLastLevel, s_levels);
        last.SelectedIndex = 2;
        var pages = new CheckBox { Name = "PART_Pages", Content = WriteStrings.TocPages, IsChecked = true };
        AutomationProperties.SetName(pages, WriteStrings.TocPages);
        var right = new CheckBox { Name = "PART_Right", Content = WriteStrings.TocRight, IsChecked = true };
        AutomationProperties.SetName(right, WriteStrings.TocRight);
        var leader = CardParts.Select("PART_Leader", WriteStrings.TocLeaderLabel, [WriteStrings.TocLeaderDots, WriteStrings.TocLeaderHyphens, WriteStrings.TocLeaderLine, WriteStrings.TocLeaderNone]);
        leader.SelectedIndex = 0;
        var hint = CardParts.Hint();

        var (ok, cancel) = CardParts.Buttons(WriteStrings.TocOk, ShellStrings.Cancel);
        Button? remove = null;
        if (context.CanRemove)
        {
            remove = new Button { Name = "PART_Remove", Content = WriteStrings.TocRemoveButton };
            remove.Classes.Add("boxed");
        }

        var labelField = CardParts.Field(WriteStrings.TocCaptionLabel, label);
        var firstField = CardParts.Field(WriteStrings.TocFirstLevel, first);
        var lastField = CardParts.Field(WriteStrings.TocLastLevel, last);
        var leaderField = CardParts.Field(WriteStrings.TocLeaderLabel, leader);
        var body = new StackPanel { Spacing = 10, MinWidth = 340 };
        body.Children.Add(CardParts.Field(WriteStrings.TocKindLabel, kind));
        body.Children.Add(labelField);
        body.Children.Add(firstField);
        body.Children.Add(lastField);
        body.Children.Add(pages);
        body.Children.Add(right);
        body.Children.Add(leaderField);
        body.Children.Add(hint);
        var dialog = new Dialog { Title = WriteStrings.TocTitle, Content = body, Footer = CardFooter.Build(ok, remove is null ? null : [remove], cancel) };

        var finish = CardParts.Once(done);

        bool IsList() => kind.SelectedIndex == 1 && context.Labels.Count > 0;

        // What does not apply to the chosen kind is hidden, what does not apply without page numbers is dimmed: the answer still carries the values, the editor ignores the ones it does not use.
        void Refresh()
        {
            var list = IsList();
            labelField.IsVisible = list;
            firstField.IsVisible = !list;
            lastField.IsVisible = !list;
            right.IsEnabled = pages.IsChecked == true;
            leader.IsEnabled = pages.IsChecked == true && right.IsChecked == true;
            leaderField.IsEnabled = leader.IsEnabled;
        }

        kind.SelectionChanged += (_, _) =>
        {
            hint.IsVisible = false;
            Refresh();
        };
        first.SelectionChanged += (_, _) => hint.IsVisible = false;
        last.SelectionChanged += (_, _) => hint.IsVisible = false;
        pages.IsCheckedChanged += (_, _) => Refresh();
        right.IsCheckedChanged += (_, _) => Refresh();
        Refresh();

        void Confirm()
        {
            var firstLevel = first.SelectedIndex + 1;
            var lastLevel = last.SelectedIndex + 1;
            var list = IsList();
            if (!list && firstLevel > lastLevel)
            {
                hint.Text = WriteStrings.TocProblemLevels;
                hint.IsVisible = true;
                first.Focus();
                return;
            }

            var tocLeader = (TocLeader)Math.Clamp(leader.SelectedIndex, 0, 3);
            finish(new TocRequest(TocAction.Insert, new TocOptions(firstLevel, lastLevel, pages.IsChecked == true, right.IsChecked == true, tocLeader, list ? label.SelectedItem as string : null)));
        }

        ok.Click += (_, _) => Confirm();
        if (remove is not null)
        {
            remove.Click += (_, _) => finish(new TocRequest(TocAction.Remove, null));
        }

        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        dialog.AttachedToVisualTree += (_, _) => kind.Focus();
        return dialog;
    }
}
