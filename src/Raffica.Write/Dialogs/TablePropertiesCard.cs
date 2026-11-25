using Avalonia.Controls;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Table properties: the width of the table, the width of each column, the height of the selected rows (automatic, at least or exactly) and whether the first row repeats at the top of each page. The values shown are
/// what the page draws now; only the parts the user changed are handed back, as one <see cref="TableProperties"/> that the window writes as one history step: when a column was changed all column widths are written
/// (their sum is then the width of the table, so a changed table width next to it is ignored), when only the table width was changed the columns keep their proportions. Lengths are typed in centimetres or inches by the
/// region rule of the page presets. A wrong entry shows a hint and keeps the card open; nothing changed closes the card with no answer; Enter confirms, Esc and Cancel change nothing.
/// </summary>
internal static class TablePropertiesCard
{
    private const int Automatic = 0;
    private const int AtLeast = 1;
    private const int Exact = 2;

    /// <param name="context">The facts of the table and whether lengths are in inches.</param>
    /// <param name="done">What was changed, or null when the user cancelled or changed nothing.</param>
    public static Dialog Create(TablePropertiesContext context, Action<TableProperties?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);
        var facts = context.Facts;
        var finish = CardParts.Once(done);
        var unit = context.Inches ? WriteStrings.PsUnitInch : WriteStrings.PsUnitCm;

        var tableWidth = new PageSetupCard.Length("PART_TableWidth", WriteStrings.TpTableWidthFormat, unit, context.Inches);
        tableWidth.Set(facts.TableWidthTwips);
        var columns = new List<PageSetupCard.Length>();
        for (var i = 0; i < facts.ColumnTwips.Count; i++)
        {
            var column = new PageSetupCard.Length("PART_Column" + (i + 1).ToString(WriteStrings.Culture), WriteStrings.Fill(WriteStrings.TpColumnWidthFormat, i + 1, "{0}"), unit, context.Inches);
            column.Set(facts.ColumnTwips[i]);
            columns.Add(column);
        }

        var rule = CardParts.Select("PART_RowRule", WriteStrings.TpRowRule, [WriteStrings.TpRuleAuto, WriteStrings.TpRuleAtLeast, WriteStrings.TpRuleExact]);
        var initialRule = facts.RowHeightTwips <= 0 ? Automatic : facts.RowHeightExact ? Exact : AtLeast;
        rule.SelectedIndex = initialRule;
        var rowHeight = new PageSetupCard.Length("PART_RowHeight", WriteStrings.TpRowHeightFormat, unit, context.Inches);
        rowHeight.Set(facts.RowHeightTwips);
        rowHeight.Box.IsEnabled = initialRule != Automatic;
        rule.SelectionChanged += (_, _) => rowHeight.Box.IsEnabled = rule.SelectedIndex != Automatic;
        var header = new CheckBox { Name = "PART_HeaderRow", Content = WriteStrings.TpHeaderRow, IsChecked = facts.HeaderRow };
        var hint = CardParts.Hint();

        var body = new StackPanel { Spacing = 10, MinWidth = 380 };
        body.Children.Add(tableWidth.Panel);
        body.Children.Add(new TextBlock { Text = WriteStrings.TpColumns, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        foreach (var column in columns)
        {
            body.Children.Add(column.Panel);
        }

        body.Children.Add(CardParts.Field(WriteStrings.TpRowRule, rule));
        body.Children.Add(rowHeight.Panel);
        body.Children.Add(new TextBlock { Text = WriteStrings.TpRowsNote, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "secondary" } });
        body.Children.Add(header);
        body.Children.Add(hint);

        var (ok, cancel) = CardParts.Buttons(WriteStrings.TpOk, ShellStrings.Cancel);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = 560, Content = body };
        var dialog = new Dialog { Title = WriteStrings.TpTitle, Content = scroll, Footer = CardFooter.Build(ok, null, cancel) };

        void Confirm()
        {
            var (properties, problem) = Read();
            if (problem is { } wrong)
            {
                hint.Text = wrong.Text;
                hint.IsVisible = true;
                wrong.Focus.Focus();
                wrong.Focus.SelectAll();
                return;
            }

            finish(properties);
        }

        (TableProperties? Properties, (string Text, TextBox Focus)? Problem) Read()
        {
            var widths = new int[columns.Count];
            var columnsChanged = false;
            for (var i = 0; i < columns.Count; i++)
            {
                if (!columns[i].TryRead(out widths[i]))
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PsInvalidValueFormat, columns[i].Label), columns[i].Box));
                }

                if (widths[i] < TableRules.MinColumnTwips || widths[i] > TableRules.MaxTwips)
                {
                    return (null, (WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, columns[i].Label), columns[i].Box));
                }

                columnsChanged |= widths[i] != facts.ColumnTwips[i];
            }

            if (!tableWidth.TryRead(out var total))
            {
                return (null, (WriteStrings.Fill(WriteStrings.PsInvalidValueFormat, tableWidth.Label), tableWidth.Box));
            }

            var widthChanged = total != facts.TableWidthTwips;
            if (columnsChanged ? widths.Sum(w => (long)w) > TableRules.MaxTwips : widthChanged && (total < columns.Count * TableRules.MinColumnTwips || total > TableRules.MaxTwips))
            {
                return (null, (WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, columnsChanged ? columns[0].Label : tableWidth.Label), columnsChanged ? columns[0].Box : tableWidth.Box));
            }

            TableRowHeight? height = null;
            var chosen = rule.SelectedIndex;
            var twips = 0;
            if (chosen != Automatic && (!rowHeight.TryRead(out twips) || twips <= 0 || twips > TableRules.MaxTwips))
            {
                return (null, (WriteStrings.Fill(WriteStrings.PsOutOfRangeFormat, rowHeight.Label), rowHeight.Box));
            }

            if (chosen != initialRule || (chosen != Automatic && twips != facts.RowHeightTwips))
            {
                height = new TableRowHeight(facts.FirstRow, facts.LastRow, chosen == Automatic ? 0 : twips, chosen == Exact);
            }

            bool? headerRow = (header.IsChecked == true) != facts.HeaderRow ? header.IsChecked == true : null;
            var properties = new TableProperties(columnsChanged ? null : widthChanged ? total : null, columnsChanged ? widths : null, height, headerRow);
            return (properties.IsEmpty ? null : properties, null);
        }

        CardParts.ConfirmOnEnter(Confirm, [tableWidth.Box, rowHeight.Box, .. columns.Select(c => c.Box)]);
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        return dialog;
    }
}
