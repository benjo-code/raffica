using Raffica.Core.Distribution;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class PdfOptionsTests
{
    [Fact]
    public void ProcessSerialNumberArgumentIsIgnored()
    {
        Assert.Equal(["a.pdf"], PdfOptions.Parse(["-psn_0_12345", "a.pdf"]).Files);
    }

    [Fact]
    public void ADashedFileNameThatIsNotPsnStaysAFile()
    {
        Assert.Equal(["-report.pdf"], PdfOptions.Parse(["-report.pdf"]).Files);
    }

    [Fact]
    public void RegistrationSwitchesParse()
    {
        var register = PdfOptions.Parse(["--register-file-types=user"]);
        var unregister = PdfOptions.Parse(["--unregister-file-types=machine"]);

        Assert.Equal(RegistrationScope.User, register.RegisterFileTypes);
        Assert.Null(register.UnregisterFileTypes);
        Assert.Equal(RegistrationScope.Machine, unregister.UnregisterFileTypes);
        Assert.Null(unregister.RegisterFileTypes);
        Assert.Null(PdfOptions.Parse([]).RegisterFileTypes);
    }

    [Fact]
    public void TheUpdateSwitchesParse()
    {
        const string Staging = @"C:\Users\Zoë Müller\AppData\Roaming\Raffica\updates\0.2.0";
        var helper = PdfOptions.Parse(["--finish-update", Staging, "--after-process", "4242"]);
        var restart = PdfOptions.Parse(["--after-process", "7", "--restore-session", "a.pdf"]);

        Assert.Equal(Staging, helper.FinishUpdate);
        Assert.Equal(4242, helper.AfterProcess);
        Assert.False(helper.RestoreSession);
        Assert.Null(restart.FinishUpdate);
        Assert.Equal(7, restart.AfterProcess);
        Assert.True(restart.RestoreSession);
        Assert.Equal(["a.pdf"], restart.Files);
        Assert.Null(PdfOptions.Parse([]).FinishUpdate);
        Assert.Null(PdfOptions.Parse([]).AfterProcess);
    }

    [Theory]
    [InlineData("--finish-update")]
    [InlineData("--after-process")]
    public void AnUpdateSwitchWithoutItsValueIsRefused(string name) => Assert.Throws<ArgumentException>(() => PdfOptions.Parse([name]));

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("")]
    public void AProcessIdThatIsNotAPositiveNumberIsRefused(string value) => Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--after-process", value]));
}
