using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// The Cross-reference card: the type of the target (heading, caption, footnote, endnote, bookmark), the list of the targets of that type, and what the reference shows of the chosen target (the choices the type offers, see
/// <see cref="CrossReferences.Offered"/>). The list is read when a type is chosen, never before the card opens. Insert is available once a target is selected; Enter in the list inserts, Esc and Cancel answer null. The list shows the
/// lines of the document the targets are (headings, captions, the beginning of a note, bookmark names): the card is the one place where the text of the document is shown, and only for the user to choose from.
/// </summary>
internal static class CrossReferenceCard
{
    private static readonly CrossRefTargetKind[] s_types = [CrossRefTargetKind.Heading, CrossRefTargetKind.Caption, CrossRefTargetKind.Footnote, CrossRefTargetKind.Endnote, CrossRefTargetKind.Bookmark];

    /// <param name="context">Where the targets of a type come from and the type to start with.</param>
    /// <param name="done">What the user chose, or null when the card was closed.</param>
    public static Dialog Create(CrossRefContext context, Action<CrossRefRequest?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        var type = CardParts.Select("PART_Type", WriteStrings.CrossRefTypeLabel, s_types.Select(TypeName));
        type.SelectedIndex = Math.Max(0, Array.IndexOf(s_types, context.DefaultType));
        var list = new ListBox { Name = "PART_List", MinHeight = 140, MaxHeight = 240, MinWidth = 340 };
        AutomationProperties.SetName(list, WriteStrings.CrossRefTargetLabel);
        var empty = new TextBlock { Name = "PART_Empty", Text = WriteStrings.CrossRefNone, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        empty.Classes.Add("secondary");
        var kind = CardParts.Select("PART_Kind", WriteStrings.CrossRefKindLabel, []);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.CrossRefOk, ShellStrings.Cancel);
        var body = new StackPanel { Spacing = 10, MinWidth = 360 };
        body.Children.Add(CardParts.Field(WriteStrings.CrossRefTypeLabel, type));
        body.Children.Add(CardParts.Field(WriteStrings.CrossRefTargetLabel, list));
        body.Children.Add(empty);
        body.Children.Add(CardParts.Field(WriteStrings.CrossRefKindLabel, kind));
        var dialog = new Dialog { Title = WriteStrings.CrossRefTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        var finish = CardParts.Once(done);
        IReadOnlyList<CrossRefTarget> targets = [];
        IReadOnlyList<CrossRefKind> kinds = [];

        void Refresh()
        {
            ok.IsEnabled = list.SelectedIndex >= 0 && kind.SelectedIndex >= 0;
        }

        // The targets and the choices of the chosen type; the choice the user had stays when the new type offers it.
        void Load()
        {
            var chosen = s_types[Math.Max(0, type.SelectedIndex)];
            var previous = kind.SelectedIndex >= 0 && kind.SelectedIndex < kinds.Count ? kinds[kind.SelectedIndex] : (CrossRefKind?)null;
            targets = context.TargetsOf(chosen);
            kinds = CrossReferences.Offered(chosen);
            list.ItemsSource = targets.Select(t => t.Display).ToArray();
            list.SelectedIndex = targets.Count > 0 ? 0 : -1;
            empty.IsVisible = targets.Count == 0;
            kind.ItemsSource = kinds.Select(KindName).ToArray();
            var again = previous is { } p ? kinds.ToList().IndexOf(p) : -1;
            kind.SelectedIndex = again >= 0 ? again : 0;
            Refresh();
        }

        void Confirm()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= targets.Count || kind.SelectedIndex < 0 || kind.SelectedIndex >= kinds.Count)
            {
                return;
            }

            finish(new CrossRefRequest(targets[list.SelectedIndex], kinds[kind.SelectedIndex]));
        }

        type.SelectionChanged += (_, _) => Load();
        list.SelectionChanged += (_, _) => Refresh();
        kind.SelectionChanged += (_, _) => Refresh();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        };
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        dialog.AttachedToVisualTree += (_, _) => list.Focus();
        Load();
        return dialog;
    }

    /// <summary>The name of a type of target in the interface language.</summary>
    internal static string TypeName(CrossRefTargetKind kind) => kind switch
    {
        CrossRefTargetKind.Heading => WriteStrings.CrossRefTypeHeading,
        CrossRefTargetKind.Caption => WriteStrings.CrossRefTypeCaption,
        CrossRefTargetKind.Footnote => WriteStrings.CrossRefTypeFootnote,
        CrossRefTargetKind.Endnote => WriteStrings.CrossRefTypeEndnote,
        _ => WriteStrings.CrossRefTypeBookmark,
    };

    /// <summary>The name of what a reference shows, in the interface language.</summary>
    internal static string KindName(CrossRefKind kind) => kind switch
    {
        CrossRefKind.LabelAndNumber => WriteStrings.CrossRefKindLabelAndNumber,
        CrossRefKind.NumberOnly => WriteStrings.CrossRefKindNumber,
        CrossRefKind.Text => WriteStrings.CrossRefKindText,
        _ => WriteStrings.CrossRefKindPage,
    };
}
