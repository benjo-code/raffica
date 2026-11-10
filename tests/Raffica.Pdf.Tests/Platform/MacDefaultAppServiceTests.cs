using Raffica.Core.Distribution;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.MacOS;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacDefaultAppServiceTests
{
    private const string Installed = "/Applications/Raffica PDF.app";

    private sealed class FakeServices : ILaunchServices
    {
        public Func<string?> Handler { get; set; } = () => null;

        public int Status { get; set; }

        public List<string> Reads { get; } = [];

        public List<(string ContentType, string BundleId)> Sets { get; } = [];

        public string? DefaultHandlerOf(string contentType)
        {
            Reads.Add(contentType);
            return Handler();
        }

        public int SetDefaultHandler(string contentType, string bundleId)
        {
            Sets.Add((contentType, bundleId));
            return Status;
        }
    }

    private static MacDefaultAppService Make(FakeServices fake, string? path = Installed) => new(fake, path);

    [Fact]
    public async Task NoHandlerIsNotDefault()
    {
        var fake = new FakeServices { Handler = () => null };
        Assert.Equal(DefaultAppStatus.NotDefault, await Make(fake).GetStatusAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("io.github.benjo-code.raffica.pdf")]
    [InlineData("IO.GITHUB.BENJO-CODE.RAFFICA.PDF")]
    [InlineData("Io.GitHub.Benjo-Code.Raffica.Pdf")]
    public async Task TheHandlerComparesIgnoringCase(string handler)
    {
        var fake = new FakeServices { Handler = () => handler };
        Assert.Equal(DefaultAppStatus.IsDefault, await Make(fake).GetStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnotherHandlerIsNotDefault()
    {
        var fake = new FakeServices { Handler = () => "com.apple.preview" };
        Assert.Equal(DefaultAppStatus.NotDefault, await Make(fake).GetStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnExceptionIsUnknown()
    {
        var fake = new FakeServices { Handler = () => throw new InvalidOperationException("boom") };
        Assert.Equal(DefaultAppStatus.Unknown, await Make(fake).GetStatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MakeDefaultIsDoneOnlyWhenTheReadBackMatches()
    {
        var fake = new FakeServices { Status = 0, Handler = () => "io.github.benjo-code.raffica.pdf" };
        Assert.Equal(DefaultAppResult.Done, await Make(fake).MakeDefaultAsync(CancellationToken.None));
        Assert.Single(fake.Sets);
        Assert.Single(fake.Reads);
    }

    [Fact]
    public async Task ANonZeroStatusIsFailedAndAReadBackThatDiffersIsFailed()
    {
        var failing = new FakeServices { Status = -54, Handler = () => MacBundle.Identifier };
        Assert.Equal(DefaultAppResult.Failed, await Make(failing).MakeDefaultAsync(CancellationToken.None));
        Assert.Empty(failing.Reads);

        var ignored = new FakeServices { Status = 0, Handler = () => "com.apple.preview" };
        Assert.Equal(DefaultAppResult.Failed, await Make(ignored).MakeDefaultAsync(CancellationToken.None));

        var none = new FakeServices { Status = 0, Handler = () => null };
        Assert.Equal(DefaultAppResult.Failed, await Make(none).MakeDefaultAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnExceptionWhileSettingIsFailed()
    {
        var fake = new FakeServices { Handler = () => throw new InvalidOperationException("boom") };
        Assert.Equal(DefaultAppResult.Failed, await Make(fake).MakeDefaultAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("/private/var/folders/x/AppTranslocation/ABC/d/Raffica PDF.app", false)]
    [InlineData("/Volumes/Raffica PDF/Raffica PDF.app", false)]
    [InlineData("/Applications/Raffica PDF.app", true)]
    public void NotSupportedOutsideAnInstalledBundle(string? path, bool supported)
    {
        Assert.Equal(supported, Make(new FakeServices(), path).IsSupported);
    }

    [Fact]
    public async Task TheContentTypeAndBundleIdArePassedExactly()
    {
        var fake = new FakeServices { Status = 0, Handler = () => MacBundle.Identifier };
        var service = Make(fake);
        await service.GetStatusAsync(CancellationToken.None);
        await service.MakeDefaultAsync(CancellationToken.None);

        Assert.All(fake.Reads, r => Assert.Equal("com.adobe.pdf", r));
        Assert.Equal(("com.adobe.pdf", "io.github.benjo-code.raffica.pdf"), Assert.Single(fake.Sets));
    }

    [Fact]
    public void ADevBuildOnMacOsHasNoDefaultAppService()
    {
        // A process outside a bundle has no bundle path, so the service is not supported.
        Assert.Null(MacBundle.PathOf("/Users/ada/src/raffica/bin/RafficaPdf"));
        Assert.False(Make(new FakeServices(), null).IsSupported);
    }

    [Fact]
    public void LaunchServicesAnswersOnMacOs()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("macOS: LaunchServices exists only on a Mac.");
            return;
        }

        var handler = new LaunchServicesNative().DefaultHandlerOf("com.adobe.pdf");
        Assert.False(string.IsNullOrEmpty(handler));
    }
}
