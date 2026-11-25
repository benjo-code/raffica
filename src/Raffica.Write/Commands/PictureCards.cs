using Raffica.Hosting;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>
/// What the Picture properties card needs of the selected picture: its size in twips, its description, the id of its wrap (<see cref="PicturePayloads.Wraps"/>), whether it is turned (its size is then not offered), whether
/// a floating wrap is possible (a picture in a table cell stays in line with the text) and whether lengths are in inches (the region rule of <c>PagePresets.UsesInches</c>). The description is read when the card opens, never earlier.
/// </summary>
internal sealed record PicturePropertiesContext(int WidthTwips, int HeightTwips, string AltText, string WrapId, bool Rotated, bool CanFloat, bool Inches);

/// <summary>
/// The two cards of the picture commands, which the window owns: Insert picture (the optional description, asked after the file was chosen and read) and Picture properties (size, description, wrap). A command asks its
/// card and runs the answer (null: the user cancelled or changed nothing). The window uses <see cref="DialogPictureCards"/>; a test puts its own in.
/// </summary>
internal interface IPictureCards
{
    /// <summary>Asks the description of a picture about to be inserted (an empty text is allowed); null when cancelled.</summary>
    Task<string?> AskInsertAsync();

    /// <summary>Asks the properties of the picture, only the parts the user changed; null when cancelled or when nothing was changed.</summary>
    Task<PictureProperties?> AskPropertiesAsync(PicturePropertiesContext context);
}

/// <summary>The real cards, shown one at a time through the window's <see cref="DialogQueue"/>.</summary>
internal sealed class DialogPictureCards(DialogQueue dialogs) : IPictureCards
{
    /// <inheritdoc/>
    public Task<string?> AskInsertAsync() => dialogs.ShowAsync<string?>(done => PictureInsertCard.Create(done));

    /// <inheritdoc/>
    public Task<PictureProperties?> AskPropertiesAsync(PicturePropertiesContext context) =>
        dialogs.ShowAsync<PictureProperties?>(done => PicturePropertiesCard.Create(context, done));
}
