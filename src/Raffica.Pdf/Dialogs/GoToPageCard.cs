using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The question for a page number (Ctrl+G): a field with the current page, OK (primary) and Cancel. Enter confirms, Esc cancels. Only a
/// whole number from 1 to the page count is accepted; anything else shows a hint on the card and keeps it open. All numbers are the ones
/// the user sees, counted from 1.
/// </summary>
internal static class GoToPageCard
{
    /// <param name="pageCount">The number of pages of the document (at least 1).</param>
    /// <param name="current">The page shown now (counted from 1); the field starts with it, or with 1 when it is not a page.</param>
    /// <param name="done">The page chosen (counted from 1), or null when the user canceled.</param>
    public static Dialog Create(int pageCount, int current, Action<int?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);

        var label = new TextBlock { Text = PdfStrings.Fill(PdfStrings.GoToPageLabelFormat, pageCount), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var field = new TextBox
        {
            Name = "PART_Page",
            MinWidth = 160,
            Text = (current >= 1 && current <= pageCount ? current : 1).ToString(CultureInfo.InvariantCulture),
        };
        AutomationProperties.SetName(field, PdfStrings.GoToPageTitle);
        var hint = new TextBlock
        {
            Name = "PART_Hint",
            Text = PdfStrings.Fill(PdfStrings.GoToPageInvalidFormat, pageCount),
            IsVisible = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));

        var ok = CreateButton("PART_Ok", ShellStrings.Ok, primary: true);
        var cancel = CreateButton("PART_Cancel", ShellStrings.Cancel, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.GoToPageTitle,
            Content = new StackPanel { Spacing = 12, Children = { label, field, hint } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } },
        };

        void Confirm()
        {
            if (TryParse(field.Text, pageCount) is { } page)
            {
                done(page);
            }
            else
            {
                hint.IsVisible = true;
                field.Focus();
                field.SelectAll();
            }
        }

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
        return dialog;
    }

    /// <summary>The page the text names (counted from 1), or null when it is not a whole number from 1 to <paramref name="pageCount"/>.</summary>
    internal static int? TryParse(string? text, int pageCount) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1 && page <= pageCount ? page : null;

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
