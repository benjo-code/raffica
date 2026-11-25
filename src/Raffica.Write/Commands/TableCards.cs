using Raffica.Hosting;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>
/// What the Table properties card needs of the table: the width of each column (the widths the page layout draws now where it can say, else the model's), the width of the table, whether the first row is a header
/// row, the height of the first selected row (twips, zero is automatic; exact or at least) and the rows of the selection (indexes of <c>Table.Rows</c>) the row height applies to.
/// </summary>
internal sealed record TablePropertiesFacts(IReadOnlyList<int> ColumnTwips, int TableWidthTwips, bool HeaderRow, int RowHeightTwips, bool RowHeightExact, int FirstRow, int LastRow);

/// <summary>What the Table properties card knows: the facts of the table and whether lengths are in inches (the region rule of <c>PagePresets.UsesInches</c>).</summary>
internal sealed record TablePropertiesContext(TablePropertiesFacts Facts, bool Inches);

/// <summary>
/// The three cards of the table commands, which the window owns: Insert table (any size up to 63 by 500), Borders (preset or sides, style, width, colour) and Table properties (widths, row height, header row). A command asks
/// its card and runs the answer (null: the user cancelled or changed nothing). The window uses <see cref="DialogTableCards"/>; a test puts its own in.
/// </summary>
internal interface ITableCards
{
    /// <summary>Asks the size of a new table; null when cancelled.</summary>
    Task<TableSize?> AskInsertAsync();

    /// <summary>Asks the borders to write on the selected cells; null when cancelled.</summary>
    Task<BorderSpec?> AskBordersAsync();

    /// <summary>Asks the properties of the table, only the parts the user changed; null when cancelled or when nothing was changed.</summary>
    Task<TableProperties?> AskPropertiesAsync(TablePropertiesContext context);
}

/// <summary>The real cards, shown one at a time through the window's <see cref="DialogQueue"/>.</summary>
internal sealed class DialogTableCards(DialogQueue dialogs) : ITableCards
{
    /// <inheritdoc/>
    public Task<TableSize?> AskInsertAsync() => dialogs.ShowAsync<TableSize?>(done => TableInsertCard.Create(done));

    /// <inheritdoc/>
    public Task<BorderSpec?> AskBordersAsync() => dialogs.ShowAsync<BorderSpec?>(done => TableBordersCard.Create(done));

    /// <inheritdoc/>
    public Task<TableProperties?> AskPropertiesAsync(TablePropertiesContext context) =>
        dialogs.ShowAsync<TableProperties?>(done => TablePropertiesCard.Create(context, done));
}
