using System.Security.Cryptography;
using System.Text;
using Raffica.Core.Tests.Updates;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

/// <summary>Records every program the applier or helper started and answers with a prepared outcome; no process is ever created.</summary>
internal sealed class FakeStarter : IUpdateProcessStarter
{
    public List<(string File, IReadOnlyList<string> Arguments)> Starts { get; } = [];

    public List<int> Waits { get; } = [];

    public bool StartResult { get; set; } = true;

    /// <summary>Gets or sets an exception the wait for the app throws (an unexpected one, to see that the helper still ends in its worded failure).</summary>
    public Exception? WaitThrows { get; set; }

    public bool Start(string file, IReadOnlyList<string> arguments)
    {
        Starts.Add((file, arguments.ToArray()));
        return StartResult;
    }

    public Task WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Waits.Add(processId);
        if (WaitThrows is { } exception)
        {
            throw exception;
        }

        return Task.CompletedTask;
    }
}

/// <summary>The setup program (or any other waited-for program): records the call and answers with the next prepared exit code.</summary>
internal sealed class FakeRunner : IProcessRunner
{
    public List<(string File, IReadOnlyList<string> Arguments)> Runs { get; } = [];

    public Queue<ProcessOutcome?> Outcomes { get; } = new();

    public Action? OnRun { get; set; }

    public Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add((file, args.ToArray()));
        OnRun?.Invoke();
        return Task.FromResult(Outcomes.Count > 0 ? Outcomes.Dequeue() : new ProcessOutcome(0, string.Empty));
    }
}

/// <summary>
/// A user folder with spaces and non-ASCII characters (<c>Users/Zoë Müller/...</c>), a config folder, a release source that signs with a throwaway key, and staged
/// updates in the shape the app leaves them (file, signed manifest, signature, request).
/// </summary>
internal sealed class ApplierKit : IDisposable
{
    public const string Running = "0.1.0";

    public ApplierKit()
    {
        Root = Directory.CreateTempSubdirectory("raffica-applier-").FullName;
        Home = Path.Combine(Root, "Users", "Zoë Müller");
        Config = Path.Combine(Home, "AppData", "Roaming", "Raffica");
        Directory.CreateDirectory(Config);
        Clock = new ManualClock(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    }

    public string Root { get; }

    public string Home { get; }

    public string Config { get; }

    public ManualClock Clock { get; }

    public FakeUpdateSource Source { get; } = new();

    public IReadOnlyList<UpdateKey> Keys => Source.Keys;

    public static UpdateVersion RunningVersion => UpdateVersion.TryParse(Running, out var v) ? v : default;

    public string UpdaterDirectory => Path.Combine(Home, "AppData", "Local", "Raffica", "updater");

    public string SessionPath => Path.Combine(Config, SessionStore.FileName);

    /// <summary>A program folder as the installer makes it (a path with a space), holding the executable and a library.</summary>
    public string MakeProgramDirectory(string exeName = "RafficaPdf.exe")
    {
        var directory = Path.Combine(Home, "AppData", "Local", "Programs", "Raffica PDF");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, exeName), "old program");
        File.WriteAllText(Path.Combine(directory, "libpdfium.dll"), "library");
        File.WriteAllText(Path.Combine(directory, "unins000.exe"), "uninstaller");
        return directory;
    }

    /// <summary>Stages a release: the payload as the file of the manifest kind, signed manifest, signature and the request of the app.</summary>
    public StagedUpdate Stage(string version, string kind, byte[]? payload = null, string? exePath = null, string? targetDirectory = null, PackageForm form = PackageForm.WindowsInstaller)
    {
        payload ??= RandomNumberGenerator.GetBytes(3000);
        var name = kind switch
        {
            UpdateFileKinds.LinuxAppImage => $"RafficaPdf-{version}-x86_64.AppImage",
            UpdateFileKinds.MacOsArm64Dmg => $"RafficaPdf-{version}-osx-arm64.dmg",
            UpdateFileKinds.MacOsX64Dmg => $"RafficaPdf-{version}-osx-x64.dmg",
            _ => $"RafficaPdf-{version}-win-x64-setup.exe",
        };
        var directory = UpdateStaging.DirectoryFor(Config, UpdateVersion.TryParse(version, out var parsed) ? parsed : default);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), payload);
        var json = "{\"format\":1,\"version\":\"" + version + "\",\"files\":[{\"kind\":\"" + kind + "\",\"name\":\"" + name + "\",\"size\":" + payload.Length
            + ",\"sha256\":\"" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant() + "\"}]}";
        var documents = Source.Sign(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(Path.Combine(directory, UpdateStaging.ManifestFileName), documents.Manifest);
        File.WriteAllText(Path.Combine(directory, UpdateStaging.SignatureFileName), documents.Signature, new UTF8Encoding(false));
        var request = new UpdateRequest { Version = version, FileName = name, Form = form, ExePath = exePath, TargetDirectory = targetDirectory };
        Assert.True(request.TryWrite(Path.Combine(directory, UpdateStaging.RequestFileName)));
        var file = new UpdateFile(kind, name, payload.Length, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
        return new StagedUpdate(directory, parsed, file, form);
    }

    public void Dispose()
    {
        Source.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that is still in use is left for the system to clean.
        }
    }
}
