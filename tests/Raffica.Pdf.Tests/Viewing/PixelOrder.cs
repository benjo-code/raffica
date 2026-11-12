using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Raffica.Pdf.Tests.Viewing;

/// <summary>
/// The tests read captured frames as B, G, R, A bytes. A render target of the platform may hold R, G, B, A instead (Skia on macOS),
/// so a capture is put in the B, G, R, A order before a test reads it.
/// </summary>
internal static class PixelOrder
{
    /// <summary>Swaps red and blue in place when <paramref name="format"/> is an R, G, B, A format.</summary>
    public static void ToBgra(byte[] pixels, PixelFormat? format)
    {
        if (format != PixelFormat.Rgba8888)
        {
            return;
        }

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }
}
