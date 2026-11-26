using Avalonia.Automation;
using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// The Caption card: the label (Figure, Table, Equation and the labels the file already uses, or a new one), the text after the number, the place (above or below) and the chapter number. The place follows the label until the user
/// chooses it (a table is captioned above, everything else below). The chapter number is offered only with numbered headings and says so when it is not. A label or text that breaks the rule shows its reason in the hint and keeps the
/// card open; Enter in a text box confirms, Esc cancels. The preview shows the caption the answer will make (with the word of the document); it holds no text of the document.
/// </summary>
internal static class CaptionCard
{
    // The sample number of the preview: the first caption of the first chapter.
    private const string SampleChapterNumber = "2" + CaptionBuilder.ChapterSeparator + "1";

    /// <param name="context">The labels to offer, the label to start with, whether a chapter number is possible and the word of a label.</param>
    /// <param name="done">What the user chose, or null when the card was closed.</param>
    public static Dialog Create(CaptionContext context, Action<CaptionRequest?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        var label = CardParts.Select("PART_Label", WriteStrings.CaptionLabelLabel, context.Labels);
        label.SelectedIndex = Math.Max(0, IndexOf(context.Labels, context.DefaultLabel));
        var newLabel = CardParts.Text("PART_NewLabel", WriteStrings.CaptionNewLabelLabel, null, minWidth: 200);
        var text = CardParts.Text("PART_Text", WriteStrings.CaptionTextLabel, null, minWidth: 320);
        var place = CardParts.Select("PART_Place", WriteStrings.CaptionPlaceLabel, [WriteStrings.CaptionPlaceAbove, WriteStrings.CaptionPlaceBelow]);

        var chapter = new CheckBox { Name = "PART_Chapter", Content = WriteStrings.CaptionChapter, IsChecked = false, IsEnabled = context.CanChapterNumber };
        AutomationProperties.SetName(chapter, WriteStrings.CaptionChapter);
        var chapterHint = new TextBlock { Name = "PART_ChapterHint", Text = WriteStrings.CaptionChapterUnavailable, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = !context.CanChapterNumber };
        chapterHint.Classes.Add("secondary");

        var preview = new TextBlock { Name = "PART_Preview", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var hint = CardParts.Hint();

        var (ok, cancel) = CardParts.Buttons(WriteStrings.CaptionOk, ShellStrings.Cancel);
        var body = new StackPanel { Spacing = 10, MinWidth = 360 };
        body.Children.Add(CardParts.Field(WriteStrings.CaptionLabelLabel, label));
        body.Children.Add(CardParts.Field(WriteStrings.CaptionNewLabelLabel, newLabel));
        body.Children.Add(CardParts.Field(WriteStrings.CaptionTextLabel, text));
        body.Children.Add(CardParts.Field(WriteStrings.CaptionPlaceLabel, place));
        body.Children.Add(chapter);
        body.Children.Add(chapterHint);
        body.Children.Add(CardParts.Field(WriteStrings.CaptionPreviewLabel, preview));
        body.Children.Add(hint);
        var dialog = new Dialog { Title = WriteStrings.CaptionTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        var finish = CardParts.Once(done);
        var placeTouched = false;
        var settingPlace = false;

        // The label the answer carries: a new label when one is typed, else the chosen one.
        string CurrentLabel()
        {
            var typed = newLabel.Text?.Trim();
            return string.IsNullOrEmpty(typed) ? (label.SelectedItem as string ?? string.Empty) : typed;
        }

        void SetPlace(CaptionPlace value)
        {
            settingPlace = true;
            place.SelectedIndex = value == CaptionPlace.Above ? 0 : 1;
            settingPlace = false;
        }

        // The place follows the label until the user has chosen one: Table above, everything else below.
        void FollowLabel()
        {
            if (!placeTouched)
            {
                SetPlace(string.Equals(CurrentLabel(), CaptionLabels.Table, StringComparison.OrdinalIgnoreCase) ? CaptionPlace.Above : CaptionPlace.Below);
            }
        }

        void Refresh()
        {
            var current = CurrentLabel();
            var word = CaptionLabels.Problem(current) is null ? context.WordFor(current) : current;
            var number = chapter.IsChecked == true && context.CanChapterNumber ? SampleChapterNumber : "1";
            preview.Text = word + " " + number + text.Text;
        }

        label.SelectionChanged += (_, _) =>
        {
            hint.IsVisible = false;
            FollowLabel();
            Refresh();
        };
        newLabel.TextChanged += (_, _) =>
        {
            hint.IsVisible = false;
            FollowLabel();
            Refresh();
        };
        text.TextChanged += (_, _) =>
        {
            hint.IsVisible = false;
            Refresh();
        };
        place.SelectionChanged += (_, _) => placeTouched |= !settingPlace;
        chapter.IsCheckedChanged += (_, _) => Refresh();
        FollowLabel();
        Refresh();

        void Confirm()
        {
            var chosen = CurrentLabel();
            if (CaptionLabels.Problem(chosen) is not null)
            {
                hint.Text = WriteStrings.CaptionProblemLabel;
                hint.IsVisible = true;
                newLabel.Focus();
                newLabel.SelectAll();
                return;
            }

            var line = text.Text ?? string.Empty;
            if (!CaptionBuilder.IsLine(line))
            {
                hint.Text = WriteStrings.CaptionProblemText;
                hint.IsVisible = true;
                text.Focus();
                text.SelectAll();
                return;
            }

            finish(new CaptionRequest(chosen, line, place.SelectedIndex == 0 ? CaptionPlace.Above : CaptionPlace.Below, chapter.IsChecked == true && context.CanChapterNumber));
        }

        CardParts.ConfirmOnEnter(Confirm, [newLabel, text]);
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        dialog.AttachedToVisualTree += (_, _) => text.Focus();
        return dialog;
    }

    private static int IndexOf(IReadOnlyList<string> labels, string wanted)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
