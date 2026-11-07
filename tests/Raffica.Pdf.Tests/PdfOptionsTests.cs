using Raffica.Core.Distribution;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class PdfOptionsTests
{
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
}
