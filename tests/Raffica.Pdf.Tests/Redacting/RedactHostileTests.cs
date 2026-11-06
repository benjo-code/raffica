using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// The consolidated refusals of the redact mode that a hostile input can reach through the session (the one every UI path ends in): each ends
/// in a reason in the UI language (English and German from <see cref="PdfStrings"/>, never Core's sentence) and leaves the plan and its history as
/// they were. The refusals of the apply run are pinned where the run is: an encrypted file and a page with <c>/Thumb</c> end in the card of
/// <c>RedactApplyFlowTests.EncryptedAndPageKeysOfferNoRescue</c>; the limit on a find (marks, a term, areas) in <c>RedactFindTests</c>.
/// </summary>
public sealed class RedactHostileTests
{
    [AvaloniaFact]
    public async Task EveryRefusalEndsInAReasonAndLeavesThePlan()
    {
        using var f = await RedactFixture.OpenAsync();
        Assert.True(f.Session.AddArea(0, new PageRect(10, 10, 60, 40)).IsDone);
        var plan = f.Session.Plan;
        var version = f.Session.Version;
        var id = plan.Marks[0].Id;
        var de = CultureInfo.GetCultureInfo("de");

        static RedactionMark Hit(int page, string term, params PageRect[] areas) =>
            new(Guid.NewGuid(), RedactionKind.Hit, page, areas, term, false);

        var tooMany = Enumerable.Range(0, RedactionLimits.MaxMarks + 1)
            .Select(i => Hit(0, "t" + i.ToString(CultureInfo.InvariantCulture), new PageRect(1 + (i % 500), 1 + (i / 500), 5 + (i % 500), 5 + (i / 500))))
            .ToList();
        var manyAreas = Enumerable.Range(0, RedactionLimits.MaxAreasPerMark + 1).Select(i => new PageRect(1, 1 + i, 5, 3 + i)).ToArray();

        var refusals = new List<(string Case, RedactResult Result)>
        {
            ("10 001 marks", f.Session.AddMarks(tooMany, out _)),
            ("a mark with 501 areas", f.Session.AddMarks([Hit(0, "x", manyAreas)], out _)),
            ("a term of 1 001 characters", f.Session.AddMarks([Hit(0, new string('x', RedactionLimits.MaxTermLength + 1), new PageRect(1, 1, 5, 5))], out _)),
            ("an overlay text of 201 characters", f.Session.SetOverlay(new HashSet<Guid> { id }, new string('x', RedactionLimits.MaxOverlayTextLength + 1), plan.Overlay)),
            ("a page the document lacks", f.Session.AddArea(999, new PageRect(10, 10, 60, 40))),
            ("a negative page", f.Session.AddArea(-1, new PageRect(10, 10, 60, 40))),
        };

        foreach (var (name, result) in refusals)
        {
            Assert.False(result.IsDone, name);
            var problem = result.Problem ?? throw new InvalidOperationException(name);
            var en = PdfStrings.RedactRefusal(result);
            Assert.False(string.IsNullOrWhiteSpace(en), name);
            Assert.NotEqual(RedactProblem.NotActive, problem);

            // The German text exists for the same refusal and is a different sentence.
            var key = "RedactRefusal" + problem switch
            {
                RedactProblem.TooManyMarks => "TooManyFormat",
                RedactProblem.TextNotMarkable => "TextNotMarkable",
                RedactProblem.OverlayTooLong => "OverlayTooLongFormat",
                RedactProblem.OutOfRange => "OutOfRange",
                _ => throw new InvalidOperationException(name + ": " + problem),
            };
            Assert.NotEqual(PdfStrings.GetIn(key, CultureInfo.GetCultureInfo("en")), PdfStrings.GetIn(key, de));
            Assert.Same(plan, f.Session.Plan);
            Assert.Equal(version, f.Session.Version);
        }

        // The limits are named in the reasons of the two that have one.
        Assert.Contains("10000", PdfStrings.RedactRefusal(refusals[0].Result).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal));
        Assert.Contains("200", PdfStrings.RedactRefusal(refusals[3].Result), StringComparison.Ordinal);
        Assert.Single(f.Session.Plan.Marks);
    }
}
