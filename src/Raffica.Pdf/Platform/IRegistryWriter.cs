using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform;

/// <summary>Writes and removes the file type registration; the Windows implementation is <c>WindowsFileTypeRegistry</c>, tests use a fake.</summary>
internal interface IRegistryWriter
{
    /// <summary>Writes every entry below the root of <paramref name="scope"/>; throws if a key cannot be written.</summary>
    void Write(RegistrationScope scope, IReadOnlyList<RegistryEntry> entries);

    /// <summary>Removes what was written; whatever is missing is skipped.</summary>
    void Remove(RegistrationScope scope, IReadOnlyList<RegistryRemoval> removals);
}
