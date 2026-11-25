using System.Globalization;
using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Insert table for any size the grid picker does not offer: the number of columns (1 to 63, the layout's cap) and of rows (1 to 500). The default is three by three. A wrong entry shows a hint and keeps the card
/// open; Enter confirms, Esc and Cancel change nothing.
/// </summary>
internal static class TableInsertCard
{
    /// <summary>The size the card starts with.</summary>
    internal const int DefaultSize = 3;

    /// <param name="done">The size chosen, or null when the user cancelled.</param>
    public static Dialog Create(Action<TableSize?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var finish = CardParts.Once(done);
        var columns = CardParts.Text("PART_Columns", WriteStrings.TiColumns, DefaultSize.ToString(CultureInfo.InvariantCulture), 96);
        var rows = CardParts.Text("PART_Rows", WriteStrings.TiRows, DefaultSize.ToString(CultureInfo.InvariantCulture), 96);
        var hint = CardParts.Hint();

        var body = new StackPanel { Spacing = 10, MinWidth = 300 };
        body.Children.Add(CardParts.Field(WriteStrings.TiColumns, columns));
        body.Children.Add(CardParts.Field(WriteStrings.TiRows, rows));
        body.Children.Add(hint);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.TiOk, ShellStrings.Cancel);
        var dialog = new Dialog { Title = WriteStrings.TiTitle, Content = body, Footer = CardFooter.Build(ok, null, cancel) };

        void Confirm()
        {
            if (!TryCount(columns.Text, TableRules.MaxColumns, out var c))
            {
                Show(WriteStrings.TiColumnsInvalid, columns);
                return;
            }

            if (!TryCount(rows.Text, TableRules.MaxRows, out var r))
            {
                Show(WriteStrings.TiRowsInvalid, rows);
                return;
            }

            finish(new TableSize(c, r));
        }

        void Show(string text, TextBox field)
        {
            hint.Text = text;
            hint.IsVisible = true;
            field.Focus();
            field.SelectAll();
        }

        CardParts.ConfirmOnEnter(Confirm, [columns, rows]);
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        return dialog;
    }

    // A whole number from 1 to the limit, digits only (a sign, a decimal mark or a unit is a wrong entry).
    internal static bool TryCount(string? text, int limit, out int count)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        count = 0;
        return trimmed.Length is > 0 and <= 6 && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 1 && count <= limit;
    }
}
