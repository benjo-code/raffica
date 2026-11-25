using Avalonia.Platform.Storage;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The picture commands of the window (the Picture group of the Insert tab): what the ribbon, the command search and the macOS menu run, the facts that enable them and the two cards. Nothing exists before the first
/// question: the facts source is made when a rule first asks, it reads nothing for a tab without an editing session, and the list of the wrap picker is read when it opens. Insert picture asks the system file picker
/// (one file; a test puts its own in <see cref="PictureFilePicker"/>), checks the size of the file before it reads it (50 MiB, <see cref="PictureRules.MaxBytes"/>), reads it on a pool thread, then asks the card for the
/// optional description and inserts at the caret as one history step; Cancel on the picker or the card changes nothing. An error reads the file in a note that names neither the file nor its contents.
/// </summary>
internal sealed partial class WriteWindow
{
    private PictureFactsSource? _pictures;
    private PicturePickers _picturePickers = null!;
    private IPictureCards? _pictureCards;
    private bool _pictureFlowRunning;

    /// <summary>Whether no Insert picture flow runs (tests).</summary>
    internal bool PictureFlowIdle => !_pictureFlowRunning;

    /// <summary>The cards Insert picture and Picture properties open: the dialogs of the window (made on first use), or the cards a test put in.</summary>
    internal IPictureCards PictureCards
    {
        get => _pictureCards ??= new DialogPictureCards(Dialogs);
        set => _pictureCards = value;
    }

    /// <summary>Replaces the system file dialog of Insert picture (tests): returns the chosen path, null when the user cancelled. Null: the system dialog.</summary>
    internal Func<Task<string?>>? PictureFilePicker { get; set; }

    /// <summary>The picker of Text wrapping (tests read how often its list was asked).</summary>
    internal PicturePickers PicturePickerSet => _picturePickers;

    /// <summary>Whether the facts source of the picture commands exists (tests; false before the first question of a picture command).</summary>
    internal bool PictureFactsExist => _pictures is not null;

    /// <summary>How many times the editor was asked for the facts of the picture commands (tests; 0 before the first ask).</summary>
    internal int PictureFactReads => _pictures?.Reads ?? 0;

    // The facts of the rules; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private PictureCommandFacts ReadPictureFacts(string id) => (_pictures ??= new PictureFactsSource()).Read(Workspace.Active, id);

    // The picker of Text wrapping: made with the ribbon (a small record, no read), the list is asked when it opens.
    private void CreatePicturePickers() => _picturePickers = new PicturePickers((id, value) => RunCommand(id, value));

    // The ribbon actions of the picture commands: Insert picture and Picture properties open their flow, Text wrapping runs through RunCommand(id, payload) with the wrap its picker chose (the command search opens the picker).
    private void AddPictureActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsPicture(c.Id)))
        {
            var id = command.Id;
            if (command.TakesPayload)
            {
                _payloadActions[id] = payload => RunPictureFromUi(id, payload);
            }

            switch (id)
            {
                case WriteCommandIds.PictureInsert:
                    _actions[id] = () => _ = GuardAsync(InsertPictureAsync);
                    break;
                case WriteCommandIds.PictureProperties:
                    _actions[id] = () => _ = GuardAsync(PicturePropertiesAsync);
                    break;
                case WriteCommandIds.PictureWrap:
                    break;
                default:
                    _actions[id] = () => _ = RunPictureFromUi(id, null);
                    break;
            }
        }
    }

    // The ribbon, the command search and the menu run a picture command on the active view; the view takes the focus back (a ribbon button that kept it would swallow the typing).
    private bool RunPictureFromUi(string id, object? payload)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunPictureCommand(id, payload);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // Insert picture: the file, the card, then the command with the bytes and the description. The whole flow (picker, read, card, insert) is one at a time: a second Insert while one runs is ignored. The plan of
    // the other cards is taken at the start, so a document that changed while the picker or the card was open is refused at OK with a note.
    private async Task InsertPictureAsync()
    {
        if (ActiveView is not { } view || _pictureFlowRunning)
        {
            return;
        }

        if (!_rules.Allows(WriteCommandIds.PictureInsert))
        {
            if (_rules.ReasonFor(WriteCommandIds.PictureInsert) is { } reason)
            {
                ShowStructureNote(view, reason);
            }

            return;
        }

        _pictureFlowRunning = true;
        try
        {
            var plan = CardPlan.Of(view);
            string? path;
            _keysDown.Clear();
            try
            {
                path = PictureFilePicker is { } picker ? await picker() : await PickPictureWithSystemDialogAsync();
            }
            finally
            {
                _keysDown.Clear();
            }

            if (path is null)
            {
                return;
            }

            // A pick that has no local path (a provider that gives no file) is no file we can read: say so.
            var (bytes, problem) = path.Length == 0 ? (null, null) : await Task.Run(() => ReadPictureFile(path));
            if (bytes is null)
            {
                ShowStructureNote(view, problem is { } refusal ? PictureWording.ReasonText(refusal) : WriteStrings.NotePictureReadFailed);
                return;
            }

            if (await PictureCards.AskInsertAsync() is not { } alt || CardIsStale(view, plan))
            {
                return;
            }

            _ = RunPictureFromUi(WriteCommandIds.PictureInsert, new PictureFile(bytes, alt));
        }
        finally
        {
            _pictureFlowRunning = false;
        }
    }

    /// <summary>
    /// Reads a picture file for Insert picture: the size is checked BEFORE a byte is read (a file above <see cref="PictureRules.MaxBytes"/> is refused with <see cref="PictureRefusal.TooLarge"/>), and the read itself stops one
    /// byte beyond the limit (a file that grew meanwhile is refused too). An empty file is refused as no picture (<see cref="PictureRefusal.NotAnImage"/>), also a file that reports no length (a pipe or a device). A directory or a file that cannot be read gives no bytes and no refusal (the caller says "could not be read" and never the name or the system message).
    /// </summary>
    internal static (byte[]? Bytes, PictureRefusal? Refusal) ReadPictureFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            {
                return (null, null);
            }

            // An empty file is no picture. A pipe, a device or a file of the system that has no length reports 0 here, so it is refused before it is opened (a pipe would block the read).
            if (info.Length == 0)
            {
                return (null, PictureRefusal.NotAnImage);
            }

            if (info.Length > PictureRules.MaxBytes)
            {
                return (null, PictureRefusal.TooLarge);
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(stream.Length, PictureRules.MaxBytes + 1)];
            var read = 0;
            while (read < buffer.Length)
            {
                var step = stream.Read(buffer, read, buffer.Length - read);
                if (step == 0)
                {
                    break;
                }

                read += step;
            }

            if (read > PictureRules.MaxBytes)
            {
                return (null, PictureRefusal.TooLarge);
            }

            if (read == 0)
            {
                return (null, PictureRefusal.NotAnImage);
            }

            return (read == buffer.Length ? buffer : buffer[..read], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return (null, null);
        }
    }

    private async Task<string?> PickPictureWithSystemDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = WriteStrings.PictureOpenTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(WriteStrings.PictureFilterImages)
                {
                    // Both spellings (a Linux file dialog matches the case), the MIME types for the portals and the types of macOS.
                    Patterns = ["*.png", "*.PNG", "*.jpg", "*.JPG", "*.jpeg", "*.JPEG", "*.gif", "*.GIF", "*.bmp", "*.BMP"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp"],
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "com.compuserve.gif", "com.microsoft.bmp"],
                },
            ],
        });
        // Null: cancelled. Empty: a file was picked that has no local path.
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? string.Empty;
    }

    // Picture properties: the card with the values of the picture as it is now, then the command with the parts that changed (the same plan check).
    private async Task PicturePropertiesAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        if (view.PicturePropertiesAtSelection(out var note) is not { } context)
        {
            ShowStructureNote(view, note ?? WriteStrings.NotePictureFailed);
            return;
        }

        var plan = CardPlan.Of(view);
        if (await PictureCards.AskPropertiesAsync(context) is not { } properties || CardIsStale(view, plan))
        {
            return;
        }

        _ = RunPictureFromUi(WriteCommandIds.PictureProperties, properties);
    }
}
