using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Raffica.Pdf.Documents;

/// <summary>What a drop onto the window carries: every dropped file is offered to the open flow, whatever its extension (Core names the problem).</summary>
internal static class FileDrop
{
    public static bool Accepts(IDataTransfer data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Contains(DataFormat.File);
    }

    /// <summary>The local paths of the dropped files, in order; entries without a local path (a folder of a remote provider) are left out.</summary>
    public static IReadOnlyList<string> Paths(IDataTransfer data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var files = data.TryGetFiles();
        return files is null ? [] : files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }
}
