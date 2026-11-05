using Raffica.Core.Annotations;
using Raffica.Core.Imaging;
using Raffica.Core.Signatures;

namespace Raffica.Pdf.Signing;

/// <summary>
/// The saved signatures of this machine, as the UI uses them: Core's <see cref="SignatureStore"/> (which repairs names, keeps the
/// limit of ten, moves a corrupt index aside and writes atomically) behind a lazy start, work off the UI thread and a
/// <see cref="Changed"/> event. The folder is asked for, read and created only when the library is first used; a signature never
/// leaves it except inside a PDF the user saves.
/// </summary>
internal sealed class SignatureLibrary(Func<string> directory)
{
    private readonly object _gate = new();
    private SignatureStore? _store;

    /// <summary>Raised after a signature was added or removed (on the context that awaited the change).</summary>
    public event Action? Changed;

    /// <summary>Opens the library afresh from its folder (it sees changes of another window) and lists its signatures in the order they were added.</summary>
    public async Task<IReadOnlyList<SavedSignature>> LoadAsync()
    {
        var store = await Task.Run(() => SignatureStore.Load(directory())).ConfigureAwait(false);
        lock (_gate)
        {
            _store = store;
        }

        return store.Items;
    }

    /// <summary>Saves a drawn signature.</summary>
    /// <exception cref="SignatureLimitException">The library already holds ten.</exception>
    /// <exception cref="ArgumentException">The strokes are not usable (see <see cref="SignatureStore.AddDrawn"/>).</exception>
    /// <exception cref="IOException">The library could not be written; nothing changed.</exception>
    public async Task<SavedSignature> AddDrawnAsync(string name, IReadOnlyList<IReadOnlyList<DisplayPoint>> strokes, DrawColor color)
    {
        var store = Store();
        var saved = await Task.Run(() => store.AddDrawn(name, strokes, color)).ConfigureAwait(false);
        Changed?.Invoke();
        return saved;
    }

    /// <summary>Saves an imported signature, a cutout with transparent paper (a side longer than 1200 pixels is shrunk first).</summary>
    /// <exception cref="SignatureLimitException">The library already holds ten.</exception>
    /// <exception cref="ArgumentException">The image shows nothing or its aspect ratio is outside 0.05 to 20.</exception>
    /// <exception cref="IOException">The library could not be written; nothing changed.</exception>
    public async Task<SavedSignature> AddImageAsync(string name, SignatureImage cutout)
    {
        ArgumentNullException.ThrowIfNull(cutout);
        if (cutout.IsEmpty)
        {
            throw new ArgumentException("The image shows nothing.", nameof(cutout));
        }

        var store = Store();
        var saved = await Task.Run(() =>
        {
            var stored = SignatureCutout.FitWithin(cutout, SignatureLimits.MaxStoredSide);
            return store.AddImage(name, PngEncoder.EncodeWithAlpha(stored), (double)stored.Width / stored.Height);
        }).ConfigureAwait(false);
        Changed?.Invoke();
        return saved;
    }

    /// <summary>Removes a signature (an unknown id changes nothing).</summary>
    /// <exception cref="IOException">The library could not be written; nothing changed.</exception>
    public async Task RemoveAsync(string id)
    {
        var store = Store();
        await Task.Run(() => store.Remove(id)).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>Reads the pixels of an imported signature.</summary>
    /// <exception cref="FileNotFoundException">The id is no imported signature of the library or its file is gone.</exception>
    /// <exception cref="InvalidDataException">The stored file cannot be decoded.</exception>
    public async Task<SignatureImage> ReadImageAsync(string id)
    {
        var store = Store();
        var png = await Task.Run(() => store.ReadPng(id)).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The signature has no image.");
        var result = await SignatureImageCodec.DecodeAsync(new MemoryStream(png, writable: false)).ConfigureAwait(false);
        return result is SignatureImageResult.Ok ok ? ok.Image : throw new InvalidDataException("The stored image cannot be read.");
    }

    private SignatureStore Store()
    {
        lock (_gate)
        {
            // Opening reads the index (a small file); the first use is a user action, not the start.
            return _store ??= SignatureStore.Load(directory());
        }
    }
}
