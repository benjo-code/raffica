using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class ModeExclusionTests
{
    private static readonly PdfMode[] All = Enum.GetValues<PdfMode>();

    [Fact]
    public void EveryPairIsDecidedWithAReason()
    {
        var allowed = new List<(PdfMode, PdfMode)>();
        foreach (var first in All)
        {
            foreach (var second in All.Where(m => m != first))
            {
                var modes = new ModeExclusion();
                modes.Enter(first);
                if (modes.CanEnter(second, out var reason))
                {
                    allowed.Add((first, second));
                    Assert.Null(reason);
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(reason), first + " then " + second);
                }
            }
        }

        // Annotating and filling in forms go together; every other pair excludes.
        Assert.Equal([(PdfMode.Annotate, PdfMode.Forms), (PdfMode.Forms, PdfMode.Annotate)], allowed);
    }

    [Fact]
    public void LeavingFreesTheMode()
    {
        foreach (var first in All)
        {
            var modes = new ModeExclusion();
            modes.Enter(first);
            modes.Leave(first);
            Assert.All(All, second => Assert.True(modes.CanEnter(second, out _), first + " then " + second));
        }
    }

    [Fact]
    public void AModeMayBeEnteredWhileItIsActive()
    {
        var modes = new ModeExclusion();
        modes.Enter(PdfMode.Redact);
        Assert.True(modes.CanEnter(PdfMode.Redact, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void TheProbeOfTheWindowCountsAsActive()
    {
        var modes = new ModeExclusion(mode => mode == PdfMode.Organize);
        Assert.True(modes.IsActive(PdfMode.Organize));
        Assert.False(modes.CanEnter(PdfMode.Annotate, out var reason));
        Assert.Equal(PdfStrings.PageChangesLockedReason, reason);
    }
}
