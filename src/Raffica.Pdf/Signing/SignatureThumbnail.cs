using Avalonia;
using Avalonia.Controls;
using Raffica.Core.Annotations;
using Raffica.Core.Signatures;

namespace Raffica.Pdf.Signing;

/// <summary>
/// A small picture of a saved signature for the picker: the strokes of a drawn one (known at once) or the cutout of an imported one (read
/// from the library and shrunk off the UI thread, then shown). It wraps a <see cref="SignaturePreview"/>, so the checkerboard shows transparent
/// paper. <see cref="LoadTask"/> ends when the picture is there or could not be made; an answer that arrives after <see cref="Dispose"/> is
/// dropped. Disposing frees the bitmap. Used on the UI thread only.
/// </summary>
internal sealed class SignatureThumbnail : Border, IDisposable
{
    /// <summary>The longest side of the cutout kept for the picture, in pixels (the picture is about 100 DIPs wide).</summary>
    private const int MaxSide = 240;

    private readonly SignaturePreview _preview = new() { Width = 104, Height = 44 };
    private bool _disposed;

    public SignatureThumbnail(SavedSignature signature, SignatureLibrary library)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(library);
        Child = _preview;
        Focusable = false;
        if (signature.Kind == SavedSignatureKind.Drawn && signature.Strokes is { Count: > 0 } strokes)
        {
            _preview.ShowStrokes(strokes, signature.Color);
            HasContent = true;
            LoadTask = Task.CompletedTask;
        }
        else if (signature.Kind == SavedSignatureKind.Image)
        {
            LoadTask = LoadImageAsync(signature.Id, library);
        }
        else
        {
            LoadTask = Task.CompletedTask;
        }
    }

    /// <summary>Gets a task that ends when the picture is shown or could not be made (it never fails).</summary>
    public Task LoadTask { get; }

    /// <summary>Gets whether the thumbnail shows a signature now.</summary>
    public bool HasContent { get; private set; }

    /// <summary>Gets whether <see cref="Dispose"/> was called.</summary>
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HasContent = false;
        _preview.ShowStrokes(null, DrawColor.Black); // frees the bitmap
    }

    private async Task LoadImageAsync(string id, SignatureLibrary library)
    {
        try
        {
            var image = await library.ReadImageAsync(id);
            var small = await Task.Run(() => SignatureCutout.FitWithin(image, MaxSide));
            if (_disposed)
            {
                return; // the card closed while the file was read: nothing to show, no bitmap to leak
            }

            _preview.ShowImage(small);
            HasContent = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A file that cannot be read shows an empty picture; the name next to it still lets the user choose or delete it.
        }
    }
}
