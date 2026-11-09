using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Ocr;
using Raffica.Pdf.Ocr;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The card of "Recognize text": the languages (a tick each for the installed ones, a warning above three, a refusal above eight), the pages (all, or a range
/// checked as the user types), what happens to pages that already have text, the resolution (automatic or 150 to 400 dpi), the one sentence that recognition
/// is not error-free and, for a signed file that allows no changes, that the recognized text counts as a change. "Recognize" (primary) hands the chosen
/// <see cref="OcrOptions"/> to <c>done</c>; it stays disabled while no language is chosen or the range is no selection of the pages. Cancel and Esc give null.
/// The card starts nothing itself and reads no folder: the caller gives the installed languages.
/// </summary>
internal static class RecognizeCard
{
    /// <summary>The resolutions offered besides automatic (0).</summary>
    internal static readonly int[] Resolutions = [150, 200, 300, 400];

    /// <param name="fileName">The name of the open file (no path).</param>
    /// <param name="pageCount">The pages of the document (at least 1).</param>
    /// <param name="languages">The installed and the chosen languages; the card changes the choice.</param>
    /// <param name="signedWithoutChangeRights">Whether the file carries a signature that allows no changes.</param>
    /// <param name="done">The options, or null when the user canceled.</param>
    /// <param name="currentPage">The 0-based page that is shown when the card opens (the "current page" option keeps that page, whatever the view does later), or null for none: the option is off.</param>
    public static Dialog Create(string fileName, int pageCount, LanguageList languages, bool signedWithoutChangeRights, Action<OcrOptions?> done, int? currentPage = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);

        var finished = false;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(RedactCardParts.Name(fileName));

        // Languages.
        body.Children.Add(Heading(PdfStrings.OcrLanguagesHeading));
        var languageHint = new TextBlock { Name = "PART_LanguageHint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        languageHint.Bind(TextBlock.ForegroundProperty, languageHint.GetResourceObservable("Raffica.Brush.Danger"));
        var slow = new TextBlock { Name = "PART_SlowWarning", Text = PdfStrings.OcrSlowWarning, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var boxes = new StackPanel { Name = "PART_Languages", Spacing = 2 };
        var ok = new Button { Name = "PART_Recognize", Content = PdfStrings.OcrRecognizeConfirm };
        ok.Classes.Add("primary");
        var changing = false;

        if (languages.Installed.Count == 0)
        {
            boxes.Children.Add(new TextBlock { Name = "PART_NoLanguages", Text = PdfStrings.OcrNoLanguages, TextWrapping = TextWrapping.Wrap });
        }

        // The pages and the rest are made below; the languages only refresh the two things that depend on them.
        Action refresh = () => { };
        foreach (var language in languages.Installed)
        {
            var text = language.Origin == TessdataOrigin.Imported ? PdfStrings.Fill(PdfStrings.OcrLanguageImportedFormat, language.Name) : language.Name;
            var box = new CheckBox { Name = "PART_Lang_" + language.Name, Content = text, IsChecked = languages.IsSelected(language.Name) };
            AutomationProperties.SetName(box, language.Name);
            box.IsCheckedChanged += (_, _) =>
            {
                if (changing)
                {
                    return;
                }

                var result = languages.Select(language.Name, box.IsChecked == true);
                languageHint.IsVisible = result != LanguageChoice.Done;
                if (result != LanguageChoice.Done)
                {
                    languageHint.Text = PdfStrings.OcrLanguageRefusal(result);
                    changing = true;
                    box.IsChecked = false;
                    changing = false;
                }

                refresh();
            };
            boxes.Children.Add(box);
        }

        body.Children.Add(RedactCardParts.Scroll(boxes));
        body.Children.Add(languageHint);
        body.Children.Add(slow);

        // Pages.
        body.Children.Add(Heading(PdfStrings.OcrPagesHeading));
        var all = Radio("PART_PagesAll", "OcrPages", PdfStrings.OcrPagesAll);
        var hasCurrent = currentPage is { } shown && shown >= 0 && shown < pageCount;
        var current = Radio("PART_PagesCurrent", "OcrPages", hasCurrent ? PdfStrings.Fill(PdfStrings.OcrPagesCurrentFormat, currentPage!.Value + 1) : PdfStrings.Fill(PdfStrings.OcrPagesCurrentFormat, "-"));
        current.IsEnabled = hasCurrent;
        var some = Radio("PART_PagesRange", "OcrPages", PdfStrings.OcrPagesRange);
        all.IsChecked = true;
        var range = new TextBox { Name = "PART_Range", MinWidth = 240, Text = "1", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(range, PdfStrings.OcrPagesRangeLabel);
        var rangeHint = new TextBlock { Name = "PART_RangeHint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        rangeHint.Bind(TextBlock.ForegroundProperty, rangeHint.GetResourceObservable("Raffica.Brush.Danger"));
        body.Children.Add(all);
        body.Children.Add(current);
        body.Children.Add(some);
        body.Children.Add(range);
        body.Children.Add(rangeHint);

        // Pages that already have text.
        body.Children.Add(Heading(PdfStrings.OcrModeHeading));
        var skip = Radio("PART_ModeSkip", "OcrMode", PdfStrings.OcrModeSkip);
        var redo = Radio("PART_ModeRedo", "OcrMode", PdfStrings.OcrModeRedo);
        var force = Radio("PART_ModeForce", "OcrMode", PdfStrings.OcrModeForce);
        skip.IsChecked = true;
        body.Children.Add(skip);
        body.Children.Add(redo);
        body.Children.Add(force);

        // Resolution.
        body.Children.Add(Heading(PdfStrings.OcrResolutionHeading));
        var dpi = new SelectBox
        {
            Name = "PART_Dpi",
            ItemsSource = new[] { PdfStrings.OcrResolutionAuto }.Concat(Resolutions.Select(r => PdfStrings.Fill(PdfStrings.OcrResolutionFormat, r.ToString(CultureInfo.CurrentCulture)))).ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        AutomationProperties.SetName(dpi, PdfStrings.OcrResolutionHeading);
        body.Children.Add(dpi);

        body.Children.Add(new TextBlock { Name = "PART_Accuracy", Text = PdfStrings.OcrAccuracyNote, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        if (signedWithoutChangeRights)
        {
            body.Children.Add(new TextBlock { Name = "PART_SignedChange", Text = PdfStrings.OcrSignedChange, TextWrapping = TextWrapping.Wrap });
        }

        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        var dialog = new Dialog
        {
            Title = PdfStrings.OcrRecognizeTitle,
            Content = new ScrollViewer { MaxHeight = 460, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = body },
            Footer = CardFooter.Build(ok, null, cancel),
        };

        string? RangeProblem() => some.IsChecked == true ? InsertFileCard.Problem(range.Text, pageCount) : null;
        bool Valid() => languages.Selected.Count > 0 && RangeProblem() is null;
        refresh = () =>
        {
            slow.IsVisible = languages.IsSlow;
            ok.IsEnabled = Valid();
        };

        void Validate()
        {
            range.IsEnabled = some.IsChecked == true;
            var problem = RangeProblem();
            rangeHint.Text = problem;
            rangeHint.IsVisible = problem is not null;
            refresh();
        }

        void Confirm()
        {
            if (finished || !Valid())
            {
                return;
            }

            finished = true;
            IReadOnlySet<int>? pages = current.IsChecked == true && hasCurrent ? new HashSet<int> { currentPage!.Value } : some.IsChecked == true ? new HashSet<int>(Raffica.Core.Pdf.PageRange.Parse(range.Text!, pageCount)) : null;
            var mode = redo.IsChecked == true ? OcrMode.Redo : force.IsChecked == true ? OcrMode.Force : OcrMode.Skip;
            var chosen = dpi.SelectedIndex > 0 && dpi.SelectedIndex <= Resolutions.Length ? Resolutions[dpi.SelectedIndex - 1] : 0;
            done(new OcrOptions(languages.Selection!, mode, chosen, pages));
        }

        void Cancel()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(null);
        }

        all.IsCheckedChanged += (_, _) => Validate();
        some.IsCheckedChanged += (_, _) => Validate();
        current.IsCheckedChanged += (_, _) => Validate();
        range.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Validate()));
        range.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        };
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => Cancel();
        dialog.CancelRequested += (_, _) => Cancel();
        Validate();
        return dialog;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    private static RadioButton Radio(string name, string group, string text) => new() { Name = name, GroupName = group, Content = text };
}
