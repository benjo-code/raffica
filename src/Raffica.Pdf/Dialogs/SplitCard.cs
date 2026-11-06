using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pages;
using Raffica.Pdf.Organizing;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>How the split card cuts the document.</summary>
internal enum SplitModeChoice
{
    /// <summary>Every n pages.</summary>
    Every,

    /// <summary>One file per range.</summary>
    Ranges,

    /// <summary>One file per top-level bookmark.</summary>
    Bookmarks,
}

/// <summary>What the split card asks for: the mode with its texts and the folder.</summary>
/// <param name="Mode">How to cut.</param>
/// <param name="Every">The text of the page count of "every n pages".</param>
/// <param name="Ranges">The text of the ranges.</param>
/// <param name="Folder">The folder the files go to.</param>
internal readonly record struct SplitChoice(SplitModeChoice Mode, string Every, string Ranges, string Folder);

/// <summary>
/// The cards of "Split document": the mode (every n pages, ranges, top-level bookmarks, the last one disabled with its reason when the file has no
/// bookmarks), the folder picker, a live preview of the files (names and pages, the first ten and the count of the rest) and Split/Cancel. Split stays
/// disabled until the mode is valid and a folder is chosen. The confirm card shows the loss list of the parts before anything is written.
/// </summary>
internal static class SplitCard
{
    private const int PreviewShown = 10;

    /// <param name="fileName">The name of the open file.</param>
    /// <param name="pageCount">The pages of the open file.</param>
    /// <param name="hasBookmarks">Whether the file has a top-level bookmark with a page.</param>
    /// <param name="plan">Works out the parts for a mode and its texts (<see cref="SplitFlow.PlanFor"/>).</param>
    /// <param name="pickFolder">Asks for the folder; null: the user canceled.</param>
    /// <param name="done">The choice, or null when the user canceled.</param>
    public static Dialog Create(string fileName, int pageCount, bool hasBookmarks, Func<SplitModeChoice, string, string, SplitPlan> plan, Func<Task<string?>> pickFolder, Action<SplitChoice?> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pickFolder);
        ArgumentNullException.ThrowIfNull(done);

        var header = new TextBlock { Name = "PART_File", Text = PdfStrings.Fill(PdfStrings.SplitHeaderFormat, fileName, pageCount), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var every = Radio("PART_ModeEvery", PdfStrings.SplitModeEvery);
        var everyField = new TextBox { Name = "PART_Every", Width = 100, Text = "1" };
        AutomationProperties.SetName(everyField, PdfStrings.SplitEveryLabel);
        var ranges = Radio("PART_ModeRanges", PdfStrings.SplitModeRanges);
        var rangesField = new TextBox { Name = "PART_Ranges", MinWidth = 280, Text = "all" };
        AutomationProperties.SetName(rangesField, PdfStrings.SplitRangesLabel);
        var bookmarks = Radio("PART_ModeBookmarks", PdfStrings.SplitModeBookmarks);
        var reason = new TextBlock { Name = "PART_BookmarksReason", Text = PdfStrings.SplitNoBookmarksReason, IsVisible = !hasBookmarks, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 0, 0) };
        bookmarks.IsEnabled = hasBookmarks;
        if (!hasBookmarks)
        {
            AutomationProperties.SetHelpText(bookmarks, PdfStrings.SplitNoBookmarksReason);
        }

        every.IsChecked = true;
        var modes = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = PdfStrings.SplitModeLabel, FontWeight = FontWeight.SemiBold },
                every,
                new StackPanel { Margin = new Thickness(24, 0, 0, 0), Children = { new TextBlock { Text = PdfStrings.SplitEveryLabel }, everyField } },
                ranges,
                new StackPanel { Margin = new Thickness(24, 0, 0, 0), Children = { new TextBlock { Text = PdfStrings.SplitRangesLabel, TextWrapping = TextWrapping.Wrap }, rangesField } },
                bookmarks,
                reason,
            },
        };

        var folderText = new TextBlock { Name = "PART_Folder", Text = PdfStrings.SplitFolderNone, TextWrapping = TextWrapping.Wrap };
        var choose = Button("PART_ChooseFolder", PdfStrings.SplitFolderChoose, primary: false);
        var folderRow = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = PdfStrings.SplitFolderLabel, FontWeight = FontWeight.SemiBold }, folderText, choose } };

        var problem = new TextBlock { Name = "PART_PreviewProblem", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable("Raffica.Brush.Danger"));
        var preview = new StackPanel { Name = "PART_Preview", Spacing = 2 };
        var more = new TextBlock { Name = "PART_PreviewMore", IsVisible = false };
        var previewBox = new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = PdfStrings.SplitPreviewHeading, FontWeight = FontWeight.SemiBold }, problem, new ScrollViewer { MaxHeight = 180, Content = preview }, more },
        };

        var split = Button("PART_Split", PdfStrings.SplitConfirm, primary: true);
        var cancel = Button("PART_Cancel", ShellStrings.Cancel, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.SplitTitle,
            Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { header, modes, folderRow, previewBox } } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { split, cancel } },
        };

        string? folder = null;
        SplitModeChoice Mode() => bookmarks.IsChecked == true ? SplitModeChoice.Bookmarks : ranges.IsChecked == true ? SplitModeChoice.Ranges : SplitModeChoice.Every;

        void Update()
        {
            var result = plan(Mode(), everyField.Text ?? string.Empty, rangesField.Text ?? string.Empty);
            problem.Text = result.Problem;
            problem.IsVisible = result.Problem is not null;
            preview.Children.Clear();
            more.IsVisible = false;
            if (result.Parts is { } parts)
            {
                var names = OutputNames.Unique(parts.Select(static part => part.Stem), ".pdf", static _ => false);
                for (var i = 0; i < Math.Min(PreviewShown, parts.Count); i++)
                {
                    preview.Children.Add(new TextBlock { Name = "PART_PreviewItem", Text = PdfStrings.Fill(PdfStrings.SplitPreviewItemFormat, names[i], PagesOf(parts[i])), TextWrapping = TextWrapping.Wrap });
                }

                if (parts.Count > PreviewShown)
                {
                    more.Text = PdfStrings.Fill(PdfStrings.SplitPreviewMoreFormat, parts.Count - PreviewShown);
                    more.IsVisible = true;
                }
            }

            split.IsEnabled = result.Parts is not null && folder is not null;
        }

        foreach (var radio in new[] { every, ranges, bookmarks })
        {
            radio.IsCheckedChanged += (_, _) => Update();
        }

        everyField.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Update()));
        rangesField.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Update()));

        // Working in a field chooses its mode.
        everyField.GotFocus += (_, _) => every.IsChecked = true;
        rangesField.GotFocus += (_, _) => ranges.IsChecked = true;

        var picking = false;
        choose.Click += async (_, _) =>
        {
            if (picking)
            {
                return;
            }

            picking = true;
            try
            {
                var picked = await pickFolder();
                if (!string.IsNullOrEmpty(picked))
                {
                    folder = picked;
                    folderText.Text = picked;
                    Update();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A picker that fails leaves the folder as it was; Split stays disabled without one.
            }
            finally
            {
                picking = false;
            }
        };

        split.Click += (_, _) =>
        {
            if (split.IsEnabled && folder is not null)
            {
                done(new SplitChoice(Mode(), everyField.Text ?? string.Empty, rangesField.Text ?? string.Empty, folder));
            }
        };
        cancel.Click += (_, _) => done(null);
        dialog.CancelRequested += (_, _) => done(null);
        Update();
        return dialog;
    }

    /// <summary>The card that says what the new files lose, before anything is written.</summary>
    /// <param name="partCount">How many files will be written.</param>
    /// <param name="losses">One sentence per loss (<see cref="PageLossWording.DescribeParts"/>), at least one.</param>
    /// <param name="done">Whether to go on.</param>
    public static Dialog CreateConfirm(int partCount, IReadOnlyList<string> losses, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(losses);
        ArgumentNullException.ThrowIfNull(done);
        var go = Button("PART_Go", PdfStrings.SplitConfirm, primary: true);
        var back = Button("PART_Back", PdfStrings.ApplyBack, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.SplitTitle,
            Content = ExtractCard.LossBody(PdfStrings.Fill(PdfStrings.SplitConfirmIntroFormat, partCount), losses),
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { go, back } },
        };
        go.Click += (_, _) => done(true);
        back.Click += (_, _) => done(false);
        dialog.CancelRequested += (_, _) => done(false);
        return dialog;
    }

    // "3", "1-5", or the list when the pages are not one run.
    private static string PagesOf(SplitPart part)
    {
        var pages = part.Pages;
        if (pages.Count == 1)
        {
            return (pages[0] + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        return pages[^1] - pages[0] + 1 == pages.Count
            ? $"{pages[0] + 1}-{pages[^1] + 1}"
            : PageLossWording.PageList(pages);
    }

    private static RadioButton Radio(string name, string text) => new() { Name = name, GroupName = "SplitMode", Content = text };

    private static Button Button(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
