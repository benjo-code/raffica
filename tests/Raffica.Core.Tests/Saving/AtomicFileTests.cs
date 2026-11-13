using System.Text.RegularExpressions;
using Raffica.Core.Saving;
using Raffica.Core.Tests.Annotations;
using Xunit;

namespace Raffica.Core.Tests.Saving;

public sealed partial class AtomicFileTests : IDisposable
{
    private readonly TempFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public async Task ReplacesAnExistingFile()
    {
        var target = Path.Combine(folder.Path, "target.bin");
        await File.WriteAllBytesAsync(target, [1, 2, 3], TestContext.Current.CancellationToken);
        var temporary = AtomicFile.NewTemporaryPath(target);
        await File.WriteAllBytesAsync(temporary, [9, 8, 7, 6], TestContext.Current.CancellationToken);

        AtomicFile.MoveIntoPlace(temporary, target);

        Assert.Equal([9, 8, 7, 6], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public async Task WithoutReplaceExistingAFileAtTheTargetEndsInTargetExistsAndStaysUntouched()
    {
        var target = Path.Combine(folder.Path, "target.bin");
        await File.WriteAllBytesAsync(target, [1, 2, 3], TestContext.Current.CancellationToken);
        var temporary = AtomicFile.NewTemporaryPath(target);
        await File.WriteAllBytesAsync(temporary, [9, 8, 7, 6], TestContext.Current.CancellationToken);

        var failure = Assert.Throws<SaveFailureException>(() => AtomicFile.MoveIntoPlace(temporary, target, replaceExisting: false));

        Assert.Equal(SaveFailureReason.TargetExists, failure.Reason);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithoutReplaceExistingAFreeTargetIsCreated()
    {
        var target = Path.Combine(folder.Path, "new.bin");
        var temporary = AtomicFile.NewTemporaryPath(target);
        await File.WriteAllBytesAsync(temporary, [5], TestContext.Current.CancellationToken);

        AtomicFile.MoveIntoPlace(temporary, target, replaceExisting: false);

        Assert.Equal([5], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SymbolicLinkIsFollowed()
    {
        var real = Path.Combine(folder.Path, "real.bin");
        await File.WriteAllBytesAsync(real, [1, 2, 3], TestContext.Current.CancellationToken);
        var link = Path.Combine(folder.Path, "link.bin");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This account cannot create symbolic links here ({ex.GetType().Name}: {ex.Message}); on Windows that needs developer mode or administrator rights.");
        }

        var resolved = AtomicFile.ResolveLink(link);
        Assert.Equal(real, resolved);
        Assert.Equal(real, AtomicFile.ResolveLink(real));

        var temporary = AtomicFile.NewTemporaryPath(resolved);
        await File.WriteAllBytesAsync(temporary, [4, 5], TestContext.Current.CancellationToken);
        AtomicFile.MoveIntoPlace(temporary, resolved);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal([4, 5], await File.ReadAllBytesAsync(real, TestContext.Current.CancellationToken));
        Assert.Equal([4, 5], await File.ReadAllBytesAsync(link, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NewTemporaryPathLiesNextToTheResolvedTarget()
    {
        var target = Path.Combine(folder.Path, "a very long file name that does not matter.docx");

        var temporary = AtomicFile.NewTemporaryPath(target);

        Assert.Equal(folder.Path, Path.GetDirectoryName(temporary));
        Assert.Matches(TemporaryName(), Path.GetFileName(temporary));
        Assert.NotEqual(temporary, AtomicFile.NewTemporaryPath(target));
    }

    [Fact]
    public async Task NewTemporaryPathFollowsALinkToItsTargetFolder()
    {
        var sub = Directory.CreateDirectory(Path.Combine(folder.Path, "sub")).FullName;
        var real = Path.Combine(sub, "real.bin");
        await File.WriteAllBytesAsync(real, [1], TestContext.Current.CancellationToken);
        var link = Path.Combine(folder.Path, "link.bin");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This account cannot create symbolic links here ({ex.GetType().Name}: {ex.Message}); on Windows that needs developer mode or administrator rights.");
        }

        Assert.Equal(sub, Path.GetDirectoryName(AtomicFile.NewTemporaryPath(link)));
    }

    [Fact]
    public async Task FlushToDiskKeepsTheContent()
    {
        var path = Path.Combine(folder.Path, "flush.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);

        AtomicFile.FlushToDisk(path);

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TargetMetadataOfAReadOnlyFileIsRefusedAndOfAMissingFileIsEmpty()
    {
        var path = Path.Combine(folder.Path, "readonly.bin");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var error = Assert.Throws<SaveFailureException>(() => AtomicFile.TargetMetadata.Read(path));
            Assert.Equal(SaveFailureReason.NotWritable, error.Reason);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        var missing = AtomicFile.TargetMetadata.Read(Path.Combine(folder.Path, "missing.bin"));
        Assert.Null(missing.UnixMode);
        missing.ApplyTo(path);
        missing.AfterReplace(path);
    }

    [GeneratedRegex(@"^\.pw-[0-9a-f]{12}\.tmp$")]
    private static partial Regex TemporaryName();
}
