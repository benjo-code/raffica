using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pages;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The cards of "Extract pages": the range card (the pages in <see cref="Raffica.Core.Pdf.PageRange"/> syntax, preset to the current page, checked
/// as the user types, with the name the new file will be suggested under) and, when the report of Core lists something, the confirm card with
/// the loss list that is shown before anything is written. The file name itself is chosen with the save dialog, which confirms an overwrite.
/// </summary>
internal static class ExtractCard
{
    // The losses scroll beyond this height; the buttons stay in view.
    private const double LossesMaxHeight = 260;

    /// <param name="fileName">The name of the open file.</param>
    /// <param name="pageCount">The pages of the open file.</param>
    /// <param name="currentPage">The 1-based page the field starts with.</param>
    /// <param name="stem">The name of the open file without its extension (the suggested file name starts with it).</param>
    /// <param name="done">The range text, or null when the user canceled.</param>
    public static Dialog Create(string fileName, int pageCount, int currentPage, string stem, Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(stem);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);

        var header = new TextBlock
        {
            Name = "PART_File",
            Text = PdfStrings.Fill(PdfStrings.ExtractHeaderFormat, fileName, pageCount),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var label = new TextBlock { Text = PdfStrings.ExtractRangeLabel, TextWrapping = TextWrapping.Wrap };
        var field = new TextBox { Name = "PART_Range", MinWidth = 280, Text = Math.Clamp(currentPage, 1, pageCount).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        AutomationProperties.SetName(field, PdfStrings.ExtractRangeLabel);
        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));
        var nameLabel = new TextBlock { Text = PdfStrings.ExtractNameLabel, TextWrapping = TextWrapping.Wrap };
        var name = new TextBlock { Name = "PART_Name", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };

        var ok = Button("PART_Extract", PdfStrings.ExtractConfirm, primary: true);
        var cancel = Button("PART_Cancel", ShellStrings.Cancel, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.ExtractTitle,
            Content = new StackPanel { Spacing = 12, Children = { header, label, field, hint, nameLabel, name } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } },
        };

        void Validate()
        {
            var problem = InsertFileCard.Problem(field.Text, pageCount);
            hint.Text = problem;
            hint.IsVisible = problem is not null;
            ok.IsEnabled = problem is null;
            name.Text = problem is null ? SuggestedName(stem, field.Text!) : string.Empty;
        }

        void Confirm()
        {
            if (InsertFileCard.Problem(field.Text, pageCount) is null)
            {
                done(field.Text!.Trim());
            }
        }

        field.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Validate()));
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => done(null);
        dialog.CancelRequested += (_, _) => done(null);
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        };
        dialog.AttachedToVisualTree += (_, _) => field.SelectAll();
        Validate();
        return dialog;
    }

    /// <summary>
    /// The card that says what the new file loses, before the save dialog and the write.
    /// </summary>
    /// <param name="range">The range text of the extract.</param>
    /// <param name="losses">One sentence per loss (<see cref="Organizing.PageLossWording.DescribeParts"/>), at least one.</param>
    /// <param name="done">Whether to go on.</param>
    public static Dialog CreateConfirm(string range, IReadOnlyList<string> losses, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(losses);
        ArgumentNullException.ThrowIfNull(done);
        var go = Button("PART_Go", PdfStrings.ExtractGo, primary: true);
        var back = Button("PART_Back", PdfStrings.ApplyBack, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.ExtractConfirmTitle,
            Content = LossBody(PdfStrings.Fill(PdfStrings.ExtractConfirmIntroFormat, range), losses),
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { go, back } },
        };
        go.Click += (_, _) => done(true);
        back.Click += (_, _) => done(false);
        dialog.CancelRequested += (_, _) => done(false);
        return dialog;
    }

    /// <summary>The file name the extract is suggested under: <c>&lt;stem&gt;-pages-&lt;range&gt;.pdf</c>, made one safe name by <see cref="OutputNames"/>.</summary>
    internal static string SuggestedName(string stem, string range)
    {
        var compact = string.Concat(range.Where(static c => !char.IsWhiteSpace(c)));
        return OutputNames.Sanitize(PdfStrings.Fill(PdfStrings.ExtractSuggestedNameFormat, stem, compact)) + ".pdf";
    }

    // The introduction, the heading and the bulleted sentences (text, never colour alone).
    internal static StackPanel LossBody(string intro, IReadOnlyList<string> losses)
    {
        var list = new StackPanel { Name = "PART_Losses", Spacing = 6 };
        foreach (var loss in losses)
        {
            var line = new TextBlock { Name = "PART_Loss", Text = "• " + loss, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(line, loss);
            list.Children.Add(line);
        }

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Name = "PART_Intro", Text = intro, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = PdfStrings.OutputLossesHeading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new ScrollViewer
                {
                    MaxHeight = LossesMaxHeight,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = list,
                },
            },
        };
    }

    private static Button Button(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
