using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Ocr;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The cards of the language files: the first lists the installed languages and says, before any file picker opens, that a language file is read by code
/// that does not check it (add files only from a source you trust) and that the chosen file is copied, never moved. "Choose file…" goes on to the picker,
/// "Close" and Esc stop. The second asks whether an earlier added file of the same language is replaced; "Keep the old one" is the default, never "Replace".
/// <see cref="ImportAsync"/> copies off the UI thread.
/// </summary>
internal static class LanguageFilesCard
{
    /// <param name="installed">The installed languages.</param>
    /// <param name="done">True for "Choose file…", false for Close and Esc.</param>
    public static Dialog Create(IReadOnlyList<TessdataLanguage> installed, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        void Finish(bool choose)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(choose);
        }

        var warning = new TextBlock { Name = "PART_Trust", Text = PdfStrings.OcrFilesTrustWarning, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        var copy = new TextBlock { Name = "PART_CopyNote", Text = PdfStrings.OcrFilesCopyNote, TextWrapping = TextWrapping.Wrap };
        var list = new StackPanel { Name = "PART_Installed", Spacing = 2 };
        if (installed.Count == 0)
        {
            list.Children.Add(new TextBlock { Name = "PART_None", Text = PdfStrings.OcrFilesNone, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var language in installed)
        {
            var template = language.Origin == TessdataOrigin.Imported ? PdfStrings.OcrFilesImportedFormat : PdfStrings.OcrFilesShippedFormat;
            list.Children.Add(new TextBlock { Name = "PART_Language", Text = PdfStrings.Fill(template, language.Name), TextWrapping = TextWrapping.Wrap });
        }

        var choose = new Button { Name = "PART_Choose", Content = PdfStrings.OcrFilesChoose };
        choose.Classes.Add("primary");
        var close = new Button { Name = "PART_CloseCard", Content = ShellStrings.Close };
        close.Classes.Add("boxed");
        choose.Click += (_, _) => Finish(true);
        close.Click += (_, _) => Finish(false);

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                warning,
                copy,
                new TextBlock { Text = PdfStrings.OcrFilesInstalledHeading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                RedactCardParts.Scroll(list),
            },
        };
        var dialog = new Dialog
        {
            Title = PdfStrings.OcrFilesTitle,
            Content = body,
            Footer = CardFooter.Build(choose, null, close),
        };
        dialog.CancelRequested += (_, _) => Finish(false);
        return dialog;
    }

    /// <param name="language">The language of the file that was added before.</param>
    /// <param name="done">True to replace it, false to keep it (also Esc).</param>
    public static Dialog CreateReplace(string language, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        void Finish(bool replace)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(replace);
        }

        var keep = new Button { Name = "PART_Keep", Content = PdfStrings.OcrReplaceKeep };
        keep.Classes.Add("primary");
        var replace = new Button { Name = "PART_Replace", Content = PdfStrings.OcrReplaceConfirm };
        replace.Classes.Add("boxed");
        keep.Click += (_, _) => Finish(false);
        replace.Click += (_, _) => Finish(true);
        var dialog = new Dialog
        {
            Title = PdfStrings.OcrReplaceTitle,
            Content = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.OcrReplaceMessageFormat, language), TextWrapping = TextWrapping.Wrap },
            Footer = CardFooter.Build(keep, [replace], null),
        };
        dialog.CancelRequested += (_, _) => Finish(false);
        return dialog;
    }

    /// <summary>
    /// Copies the file on a pool thread (a language file is up to 200 MB, so the window stays alive meanwhile). A failure that Core did not turn into a
    /// result is a <see cref="TessdataImportError.Failed"/>: nothing was added.
    /// </summary>
    /// <param name="import">The copy (<see cref="TessdataStore.Import"/>; a slow one in tests).</param>
    /// <param name="path">The file the user chose.</param>
    /// <param name="replaceImported">Whether an earlier added file of the language may be replaced.</param>
    public static Task<TessdataImportResult> ImportAsync(Func<string, bool, TessdataImportResult> import, string path, bool replaceImported)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Task.Run(() =>
        {
            try
            {
                return import(path, replaceImported);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new TessdataImportResult(TessdataImportError.Failed);
            }
        });
    }
}
