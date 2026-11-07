using System.Runtime.Versioning;
using Microsoft.Win32;
using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform.Windows;

/// <summary>
/// Writes and removes the file type registration (<see cref="FileTypeRegistration"/>) below <c>HKCU</c> or <c>HKLM</c>, or
/// below a given root (a sandbox subkey in tests), then tells the shell. Only the keys of <see cref="FileTypeRegistration"/>
/// are touched, never <c>UserChoice</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFileTypeRegistry : IRegistryWriter
{
    private readonly RegistryKey? _root;

    /// <summary>Uses <c>HKCU</c> or <c>HKLM</c> by scope.</summary>
    public WindowsFileTypeRegistry()
    {
    }

    /// <summary>Uses <paramref name="root"/> for every scope (tests).</summary>
    public WindowsFileTypeRegistry(RegistryKey root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
    }

    /// <inheritdoc/>
    public void Write(RegistrationScope scope, IReadOnlyList<RegistryEntry> entries)
    {
        var root = RootFor(scope);
        foreach (var entry in entries)
        {
            using var key = root.CreateSubKey(entry.KeyPath, writable: true);
            key.SetValue(entry.ValueName, entry.Value, RegistryValueKind.String);
        }

        Notify();
    }

    /// <inheritdoc/>
    public void Remove(RegistrationScope scope, IReadOnlyList<RegistryRemoval> removals)
    {
        var root = RootFor(scope);
        foreach (var removal in removals)
        {
            if (removal.ValueName is null)
            {
                root.DeleteSubKeyTree(removal.KeyPath, throwOnMissingSubKey: false);
                continue;
            }

            using var key = root.OpenSubKey(removal.KeyPath, writable: true);
            key?.DeleteValue(removal.ValueName, throwOnMissingValue: false);
        }

        Notify();
    }

    private RegistryKey RootFor(RegistrationScope scope) =>
        _root ?? (scope == RegistrationScope.Machine ? Registry.LocalMachine : Registry.CurrentUser);

    // A sandbox root is not the real registry: no reason to wake the shell.
    private void Notify()
    {
        if (_root is null)
        {
            ShellNative.NotifyAssociationsChanged();
        }
    }
}
