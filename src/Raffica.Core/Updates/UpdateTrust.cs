using System.Globalization;

namespace Raffica.Core.Updates;

/// <summary>
/// What the program trusts for updates: the public keys and the addresses, compiled in with no way to override them
/// (no environment variable, no switch). This file is the release build. A test build swaps it for
/// <c>UpdateTrust.TestBuild.cs</c> only with the build property <c>RafficaUpdateTestBuild=true</c>, which no release
/// sets (Raffica.Core.csproj; packaging/README.md, section "Manual update check"); both files declare the same members.
/// </summary>
public static class UpdateTrust
{
    /// <summary>The identifier of the placeholder key below.</summary>
    public const string PlaceholderKeyId = "placeholder";

    /// <summary>
    /// The accepted public keys, any of which may have signed a manifest. This is the public half of the release key
    /// <c>release-1</c> (its private half is the environment secret <c>RAFFICA_UPDATE_SIGNING_KEY</c> of
    /// <c>release-signing</c>); a new key is added next to the old one first (key rotation).
    /// </summary>
    public static IReadOnlyList<UpdateKey> Keys { get; } =
    [
        new UpdateKey(
            "release-1",
            Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEO13/2Mf7aZsR+S3guOS0mnDoMUFkeYyawV5GPXGnKZl8v52pOcClpjbqorNKOUp/lsf14s/aAGvoU1TLt8u1ew==")),
    ];

    /// <summary>
    /// The address of the newest stable release (GitHub lists no pre-releases or drafts there). The three addresses point at
    /// the releases of this repository (Docs/DECISIONS.md, entry "Updater: key and addresses until the first release").
    /// </summary>
    public static string ReleasesLatestUrl => "https://api.github.com/repos/benjo-code/raffica/releases/latest";

    /// <summary>
    /// The page of the newest release, where a form that cannot update itself (the Debian package, the portable archive)
    /// gets the new version by hand; it is opened only through the confirmation card of external addresses.
    /// </summary>
    public static string ReleasesPageUrl => "https://github.com/benjo-code/raffica/releases/latest";

    /// <summary>The address the release files are below; a file is <c>&lt;base&gt;/&lt;tag&gt;/&lt;name&gt;</c>.</summary>
    public static string DownloadBaseUrl => "https://github.com/benjo-code/raffica/releases/download";

    /// <summary>
    /// What the about card shows after the version to mark a test build; <see langword="null"/> here, in the release
    /// build. The text lives only in the test build's file, so a release carries none of it.
    /// </summary>
    /// <param name="culture">The language of the user interface.</param>
    public static string? TestBuildNote(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return null;
    }
}
