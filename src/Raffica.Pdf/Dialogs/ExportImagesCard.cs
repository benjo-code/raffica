using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pdf;
using Raffica.Pdf.Organizing;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The card of "Export pages as images": the pages (<see cref="PageRange"/> syntax, <c>all</c> to start with, checked as the user types, with the
/// reason), the resolution (72, 150 or 300 dpi, or a custom value from 1 to 1200), "Include marks and form fields" (on), the folder picker and
/// Export/Cancel. It shows the largest page in pixels at the chosen resolution and, when a page is over the limit of Core, names it with its number
/// as the reason Export stays disabled; the same for a folder that does not exist. Every state is a sentence, never colour alone.
/// </summary>
internal static class ExportImagesCard
{
    private static readonly int[] Presets = [72, 150, 300];

    /// <param name="fileName">The name of the open file.</param>
    /// <param name="sizes">The page sizes of the open file (the card is about as many pages).</param>
    /// <param name="pickFolder">Asks for the folder; null: the user canceled.</param>
    /// <param name="done">The choice, or null when the user canceled.</param>
    public static Dialog Create(string fileName, IReadOnlyList<PdfPageSize> sizes, Func<Task<string?>> pickFolder, Action<ImageExportChoice?> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(pickFolder);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizes.Count, 1);

        var header = new TextBlock { Name = "PART_File", Text = PdfStrings.Fill(PdfStrings.ExtractHeaderFormat, fileName, sizes.Count), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var rangeLabel = new TextBlock { Text = PdfStrings.ExportRangeLabel, TextWrapping = TextWrapping.Wrap };
        var range = new TextBox { Name = "PART_Range", MinWidth = 280, Text = PdfStrings.PageKeywordAll };
        AutomationProperties.SetName(range, PdfStrings.ExportRangeLabel);
        var rangeHint = Hint("PART_RangeHint");

        var presets = Presets.Select(dpi => new RadioButton
        {
            Name = "PART_Dpi" + dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GroupName = "ExportDpi",
            Content = PdfStrings.Fill(PdfStrings.ExportDpiFormat, dpi),
            Tag = dpi,
        }).ToArray();
        var custom = new RadioButton { Name = "PART_DpiCustom", GroupName = "ExportDpi", Content = PdfStrings.ExportDpiCustom };
        var customField = new TextBox { Name = "PART_DpiField", Width = 100, Text = "200" };
        AutomationProperties.SetName(customField, PdfStrings.ExportDpiCustomLabel);
        var dpiHint = Hint("PART_DpiHint");
        presets[1].IsChecked = true;
        var resolution = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = PdfStrings.ExportDpiLabel, FontWeight = FontWeight.SemiBold } } };
        foreach (var preset in presets)
        {
            resolution.Children.Add(preset);
        }

        resolution.Children.Add(custom);
        resolution.Children.Add(new StackPanel { Margin = new Thickness(24, 0, 0, 0), Spacing = 4, Children = { new TextBlock { Text = PdfStrings.ExportDpiCustomLabel, TextWrapping = TextWrapping.Wrap }, customField, dpiHint } });

        var marks = new CheckBox { Name = "PART_Marks", Content = PdfStrings.ExportMarks, IsChecked = true };
        var largest = new TextBlock { Name = "PART_Largest", TextWrapping = TextWrapping.Wrap };
        var sizeHint = Hint("PART_SizeHint");

        var folderText = new TextBlock { Name = "PART_Folder", Text = PdfStrings.SplitFolderNone, TextWrapping = TextWrapping.Wrap };
        var choose = new Button { Name = "PART_ChooseFolder", Content = PdfStrings.SplitFolderChoose };
        choose.Classes.Add("boxed");
        var folderHint = Hint("PART_FolderHint");
        var folderRow = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = PdfStrings.SplitFolderLabel, FontWeight = FontWeight.SemiBold }, folderText, choose, folderHint } };

        var export = new Button { Name = "PART_Export", Content = PdfStrings.ExportConfirm };
        export.Classes.Add("primary");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");
        var dialog = new Dialog
        {
            Title = PdfStrings.ExportTitle,
            Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { header, rangeLabel, range, rangeHint, resolution, marks, largest, sizeHint, folderRow } } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, cancel } },
        };

        string? folder = null;
        string DpiText() => custom.IsChecked == true ? customField.Text ?? string.Empty : ((int)presets.First(static p => p.IsChecked == true).Tag!).ToString(System.Globalization.CultureInfo.InvariantCulture);

        ImageExportView Update()
        {
            // The custom field only speaks while it is the chosen resolution.
            var view = ImageExportPlan.Evaluate(range.Text, DpiText(), sizes);
            rangeHint.Text = view.RangeProblem;
            rangeHint.IsVisible = view.RangeProblem is not null;
            dpiHint.Text = custom.IsChecked == true ? view.DpiProblem : null;
            dpiHint.IsVisible = custom.IsChecked == true && view.DpiProblem is not null;

            largest.IsVisible = view.LargestPage >= 0;
            if (view.LargestPage >= 0)
            {
                largest.Text = PdfStrings.Fill(
                    PdfStrings.ExportLargestFormat,
                    view.LargestWidth.ToString("N0", PdfStrings.Culture),
                    view.LargestHeight.ToString("N0", PdfStrings.Culture),
                    (view.LargestWidth * view.LargestHeight / 1_000_000.0).ToString("0.#", PdfStrings.Culture));
            }

            sizeHint.IsVisible = view.OverPage >= 0;
            if (view.OverPage >= 0)
            {
                var pixels = view.OverPixels == long.MaxValue ? double.PositiveInfinity : view.OverPixels / 1_000_000.0;
                var limit = (Raffica.Core.Pages.PageImageExporter.MaxPixels / 1_000_000.0).ToString("0.###", PdfStrings.Culture);
                sizeHint.Text = double.IsInfinity(pixels)
                    ? PdfStrings.Fill(PdfStrings.ExportTooLargeHugeFormat, view.OverPage + 1, string.Empty, limit)
                    : PdfStrings.Fill(PdfStrings.ExportTooLargeFormat, view.OverPage + 1, pixels.ToString("0.#", PdfStrings.Culture), limit);
            }

            export.IsEnabled = view.IsOk && folder is not null && folderHint.IsVisible == false;
            return view;
        }

        void Confirm()
        {
            if (Update() is { IsOk: true } view && export.IsEnabled && folder is not null)
            {
                done(new ImageExportChoice(range.Text!.Trim(), view.Dpi, marks.IsChecked == true, folder));
            }
        }

        range.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Update()));
        customField.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Update()));
        foreach (var radio in presets.Append(custom))
        {
            radio.IsCheckedChanged += (_, _) => Update();
        }

        // Working in the custom field chooses it (the field is always enabled so that focusing it can).
        customField.GotFocus += (_, _) => custom.IsChecked = true;

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

                    // A folder that is not there is a reason on the card; Core refuses it again at the write.
                    var exists = Directory.Exists(picked);
                    folderHint.Text = exists ? null : PdfStrings.ExportFolderMissing;
                    folderHint.IsVisible = !exists;
                    Update();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A picker that fails leaves the folder as it was; Export stays disabled without one.
            }
            finally
            {
                picking = false;
            }
        };

        export.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => done(null);
        dialog.CancelRequested += (_, _) => done(null);
        void ConfirmOnEnter(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Confirm();
            }
        }

        range.KeyDown += ConfirmOnEnter;
        customField.KeyDown += ConfirmOnEnter;
        dialog.AttachedToVisualTree += (_, _) => range.SelectAll();
        Update();
        return dialog;
    }

    private static TextBlock Hint(string name)
    {
        var hint = new TextBlock { Name = name, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));
        return hint;
    }
}
