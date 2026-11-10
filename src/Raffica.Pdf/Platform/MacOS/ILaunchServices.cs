namespace Raffica.Pdf.Platform.MacOS;

/// <summary>The two LaunchServices questions the default-app service asks; the real one is <see cref="LaunchServicesNative"/>, tests use a fake.</summary>
internal interface ILaunchServices
{
    /// <summary>The bundle identifier of the app that opens <paramref name="contentType"/> by default (lower case as LaunchServices answers), or <c>null</c> when none is set.</summary>
    string? DefaultHandlerOf(string contentType);

    /// <summary>Makes <paramref name="bundleId"/> the default app for <paramref name="contentType"/> in all roles; returns the <c>OSStatus</c> (0 is success).</summary>
    int SetDefaultHandler(string contentType, string bundleId);
}
