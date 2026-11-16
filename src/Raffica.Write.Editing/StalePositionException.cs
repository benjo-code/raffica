namespace Raffica.Write.Editing;

/// <summary>
/// The text editor's own refusal of a position that is not in the document (a stale paragraph, a story the document has not): thrown before the editor has changed anything, so
/// model, history and layout are as they were. It is the one failure of an operation that is only a refusal; every other exception, argument exceptions of the framework included,
/// may have been thrown after a change and must fault the editing session.
/// </summary>
public sealed class StalePositionException(string message) : ArgumentException(message);
