using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pdf;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>Where the inserted pages go.</summary>
internal enum InsertPosition
{
    /// <summary>After the last selected page.</summary>
    AfterSelection,

    /// <summary>After the last page of the plan.</summary>
    AtEnd,

    /// <summary>Before the first page of the plan.</summary>
    AtStart,
}

/// <summary>What the insert card asks for: the pages of the file (<see cref="PageRange"/> text) and the position.</summary>
internal readonly record struct InsertChoice(string Range, InsertPosition Position);

/// <summary>
/// The card of "Insert file": the pages to take from the chosen file (a range field, default <c>all</c>, checked as the user types, with the
/// reason shown), the position as radio buttons, and Insert (primary) and Cancel. Insert stays disabled while the range is not a selection of
/// the pages of the file. Enter confirms a valid range, Esc cancels.
/// </summary>
internal static class InsertFileCard
{
    /// <param name="fileName">The name of the file (no path).</param>
    /// <param name="pageCount">The pages of the file (at least 1).</param>
    /// <param name="selectionPosition">The text of the "after the selected pages" choice, or null when no page is selected (the choice is not offered).</param>
    /// <param name="done">The choice, or null when the user canceled.</param>
    public static Dialog Create(string fileName, int pageCount, string? selectionPosition, Action<InsertChoice?> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);

        var header = new TextBlock
        {
            Name = "PART_File",
            Text = PdfStrings.Fill(PdfStrings.InsertFileHeaderFormat, fileName, pageCount),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var label = new TextBlock { Text = PdfStrings.InsertFileRangeLabel, TextWrapping = TextWrapping.Wrap };
        var field = new TextBox { Name = "PART_Range", MinWidth = 280, Text = PdfStrings.PageKeywordAll };
        AutomationProperties.SetName(field, PdfStrings.InsertFileRangeLabel);
        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Danger"));

        var positionLabel = new TextBlock { Text = PdfStrings.InsertPositionLabel };
        var group = new StackPanel { Spacing = 4 };
        var after = selectionPosition is null ? null : Radio("PART_AfterSelection", selectionPosition);
        var atEnd = Radio("PART_AtEnd", PdfStrings.InsertAtEnd);
        var atStart = Radio("PART_AtStart", PdfStrings.InsertAtStart);
        if (after is not null)
        {
            group.Children.Add(after);
        }

        group.Children.Add(atEnd);
        group.Children.Add(atStart);
        (after ?? atEnd).IsChecked = true;

        var ok = CreateButton("PART_Insert", PdfStrings.InsertConfirm, primary: true);
        var cancel = CreateButton("PART_Cancel", ShellStrings.Cancel, primary: false);
        var dialog = new Dialog
        {
            Title = PdfStrings.InsertFileTitle,
            Content = new StackPanel { Spacing = 12, Children = { header, label, field, hint, positionLabel, group } },
            Footer = CardFooter.Build(ok, null, cancel),
        };

        void Validate()
        {
            var problem = Problem(field.Text, pageCount);
            hint.Text = problem;
            hint.IsVisible = problem is not null;
            ok.IsEnabled = problem is null;
        }

        void Confirm()
        {
            if (Problem(field.Text, pageCount) is not null)
            {
                return;
            }

            var position = atStart.IsChecked == true ? InsertPosition.AtStart : atEnd.IsChecked == true ? InsertPosition.AtEnd : InsertPosition.AfterSelection;
            done(new InsertChoice(field.Text!.Trim(), position));
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

    /// <summary>Why the text is no selection of the pages of the file (in the UI language), or null when it is one.</summary>
    internal static string? Problem(string? text, int pageCount)
    {
        if (text is null)
        {
            return PdfStrings.InsertRangeInvalid;
        }

        try
        {
            PageRange.Validate(text);
            PageRange.Parse(text, pageCount);
            return null;
        }
        catch (FormatException)
        {
            return PdfStrings.InsertRangeInvalid;
        }
        catch (ArgumentOutOfRangeException)
        {
            return PdfStrings.Fill(PdfStrings.InsertRangeOutsideFormat, pageCount);
        }
    }

    private static RadioButton Radio(string name, string text) => new() { Name = name, GroupName = "InsertPosition", Content = text };

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
