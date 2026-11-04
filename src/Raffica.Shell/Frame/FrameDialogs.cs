using Avalonia.Controls;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;

namespace Raffica.Shell.Frame;

/// <summary>Whether a window currently shows a modal dialog (a <see cref="DialogHost"/> of the window is open).</summary>
internal static class FrameDialogs
{
    public static bool IsOpen(Window window) =>
        window.GetVisualDescendants().OfType<DialogHost>().Any(host => host.IsOpen);
}
