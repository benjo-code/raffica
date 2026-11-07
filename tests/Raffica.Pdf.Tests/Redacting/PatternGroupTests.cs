using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Raffica.Core.Redaction;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class PatternGroupTests
{
    private static readonly HashSet<RedactionPatternKind> All = [RedactionPatternKind.Email, RedactionPatternKind.Iban, RedactionPatternKind.Phone, RedactionPatternKind.Card];

    [AvaloniaFact]
    public async Task ChecksumOkAndShapeOnlyArePreselectedFailedIsNot()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var find = new FindSession(f.Session);
        Assert.True(await find.RunPatternsAsync(All));

        var groups = find.Groups;
        Assert.Equal(6, groups.Count);
        Assert.Equal(
            [
                (RedactionPatternKind.Email, PatternCertainty.ShapeOnly, 3),
                (RedactionPatternKind.Iban, PatternCertainty.ChecksumOk, 6),
                (RedactionPatternKind.Iban, PatternCertainty.ChecksumFailed, 1),
                (RedactionPatternKind.Phone, PatternCertainty.ShapeOnly, 4),
                (RedactionPatternKind.Card, PatternCertainty.ChecksumOk, 3),
                (RedactionPatternKind.Card, PatternCertainty.ChecksumFailed, 1),
            ],
            groups.Select(g => (g.Kind, g.Certainty, g.Hits.Count)));
        Assert.All(groups, g => Assert.Equal(g.Certainty != PatternCertainty.ChecksumFailed, g.Preselected));

        // The ticks follow the groups: the failed ones are not ticked, and cannot be.
        Assert.Equal(16, find.TickedCount);
        var failed = groups.First(g => g.Certainty == PatternCertainty.ChecksumFailed).Hits[0];
        Assert.False(find.SetTicked(find.IndexOf(failed), true));

        // Mark all leaves the failed ones out; "Add these" takes them, and only them.
        Assert.True(find.MarkAll().IsDone);
        Assert.Equal(16, f.Session.Plan.Marks.Count);
        Assert.All(f.Session.Plan.Marks, m => Assert.NotNull(m.Pattern));
        Assert.True(find.AddFailed().IsDone);
        Assert.Equal(18, f.Session.Plan.Marks.Count);
    }

    [AvaloniaFact]
    public async Task PatternMarksNeverDemandGoneFromTheWholeDocument()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var find = new FindSession(f.Session);
        await find.RunPatternsAsync(new HashSet<RedactionPatternKind> { RedactionPatternKind.Iban });

        Assert.True(find.MarkTicked().IsDone);

        Assert.Equal(6, f.Session.Plan.Marks.Count);
        Assert.All(f.Session.Plan.Marks, m =>
        {
            Assert.Equal(RedactionPatternKind.Iban, m.Pattern);
            Assert.False(m.Everywhere);
        });
    }

    [AvaloniaFact]
    public async Task CertaintyIsWordsNotColour()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var pane = new RedactPane(f.Session, f.View);
        pane.ToggleFind();
        var panel = pane.FindPanel!;
        panel.PatternsToggle.IsChecked = true;

        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();

        Assert.Equal("Checksum ok", PdfStrings.FindCertaintyText(PatternCertainty.ChecksumOk));
        Assert.Equal("Shape only", PdfStrings.FindCertaintyText(PatternCertainty.ShapeOnly));
        Assert.Equal("Checksum failed", PdfStrings.FindCertaintyText(PatternCertainty.ChecksumFailed));
        var headings = panel.RowTexts.Where(t => !t.StartsWith("Page ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["E-mail addresses, Shape only", "IBANs, Checksum ok", "IBANs, Checksum failed", "Phone numbers, Shape only", "Card numbers, Checksum ok", "Card numbers, Checksum failed"],
            headings);
        Assert.True(panel.AddFailedButton.IsVisible);
        Assert.Contains("(3)", panel.KindBox(RedactionPatternKind.Email).Content!.ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ReportNeverSaysAllFound()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var pane = new RedactPane(f.Session, f.View);
        pane.ToggleFind();
        var panel = pane.FindPanel!;
        panel.PatternsToggle.IsChecked = true;
        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();

        Assert.StartsWith("Patterns only find what the text shows.", panel.CautionText, StringComparison.Ordinal);
        var forbidden = new Regex(@"\b(all found|all of them|error-free|safe|complete|nothing else)\b", RegexOptions.IgnoreCase);
        Assert.All(new[] { panel.StatusText, panel.CautionText, PdfStrings.FindPatternCaution }, text => Assert.DoesNotMatch(forbidden, text));
        Assert.Equal("18 matches", panel.StatusText);
    }

    [Fact]
    public void GroupsOfNoHitsAreEmptyAndTermHitsAreLeftOut()
    {
        Assert.Empty(PatternGroups.Of([]));
        Assert.Empty(PatternGroups.Of([new RedactionHit(RedactionHitSource.PageText, 0, [], "x", false)]));
    }
}
