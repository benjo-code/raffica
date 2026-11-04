using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

public sealed class SearchSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static async Task<(ViewFixture Fixture, SearchSession Session)> OpenAsync(string file, Func<int>? startPage = null)
    {
        var fixture = await ViewFixture.OpenAsync(file, show: false);
        return (fixture, new SearchSession(fixture.Tab, startPage ?? (() => fixture.View.CurrentPage)));
    }

    private static async Task FinishAsync(SearchSession session)
    {
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task FindsEveryOccurrenceWithItsRectangle()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start("seite", default);
            await FinishAsync(session);

            // "Weiter zu Seite zwei" on page 1 and "Seite zwei" on page 2.
            Assert.Equal([0, 1], session.Hits.Select(h => h.PageIndex));
            Assert.All(session.Hits, h => Assert.All(h.Rects, r => Assert.True(r.Width > 5 && r.Height > 5)));
            var direct = await fixture.Session.Document!.SearchPageAsync(0, "seite", default, TestContext.Current.CancellationToken);
            Assert.Equal(direct[0].Rects, session.Hits[0].Rects);
            Assert.Equal(0, session.Current);
            Assert.Same(session.Hits[0], session.CurrentHit);
            Assert.Single(session.HitsOn(1));
            Assert.Empty(session.HitsOn(7));
            Assert.False(session.IsRunning);
        }
    }

    [AvaloniaFact]
    public async Task StartsOnTheCurrentPageAndWraps()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf", () => 1);
        using (fixture)
        using (session)
        {
            session.Start("seite", default);
            await FinishAsync(session);

            Assert.Equal([1, 0], session.Hits.Select(h => h.PageIndex));
            Assert.Equal(1, session.CurrentHit!.PageIndex);
        }
    }

    [AvaloniaFact]
    public async Task NextAndPreviousCycle()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            var currentChanges = 0;
            session.Start("seite", default);
            await FinishAsync(session);
            session.CurrentChanged += () => currentChanges++;

            session.Next();
            Assert.Equal(1, session.Current);
            session.Next();
            Assert.Equal(0, session.Current);
            session.Previous();
            Assert.Equal(1, session.Current);
            session.Previous();
            Assert.Equal(0, session.Current);
            Assert.Equal(4, currentChanges);
        }
    }

    [AvaloniaFact]
    public async Task NextAndPreviousWithoutHitsDoNothing()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Next();
            session.Previous();
            Assert.Equal(-1, session.Current);
            Assert.Null(session.CurrentHit);
        }
    }

    [AvaloniaFact]
    public async Task NewQueryCancelsTheOldRun()
    {
        var (fixture, session) = await OpenAsync("text-dense.pdf");
        using (fixture)
        using (session)
        {
            session.Start("a", default);
            var old = session.Completion;
            Assert.True(session.IsRunning);
            session.Start("Needle", default);
            await FinishAsync(session);
            await old.WaitAsync(Limit);
            ViewFixture.Pump();

            Assert.Equal(20, session.Hits.Count);
            Assert.All(session.Hits, h => Assert.Equal("needle", h.Snippet.Match, ignoreCase: true));
            Assert.False(session.LimitReached);
            Assert.False(session.IsRunning);
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheTabCancelsTheRun()
    {
        var (fixture, session) = await OpenAsync("text-dense.pdf");
        using (fixture)
        using (session)
        {
            session.Start("a", default);
            Assert.True(session.IsRunning);

            fixture.Tab.Dispose();
            await FinishAsync(session);

            Assert.False(session.IsRunning);
            Assert.True(session.Hits.Count < DocumentSearch.MaxHits);
        }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task EmptyOrWhitespaceQueryClearsTheHits(string query)
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start("seite", default);
            await FinishAsync(session);
            Assert.NotEmpty(session.Hits);
            var currentChanged = 0;
            session.CurrentChanged += () => currentChanged++;

            session.Start(query, default);

            Assert.Empty(session.Hits);
            Assert.Equal(-1, session.Current);
            Assert.Equal(string.Empty, session.Summary);
            Assert.False(session.IsRunning);
            Assert.Equal(1, currentChanged);
            Assert.Empty(session.HitsOn(0));
        }
    }

    [AvaloniaTheory]
    [InlineData(".*")]
    [InlineData("(")]
    [InlineData("[")]
    [InlineData("\\d+")]
    public async Task QueryWithRegexCharactersIsLiteral(string query)
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start(query, default);
            await FinishAsync(session);

            Assert.Empty(session.Hits);
            Assert.Equal(PdfStrings.SearchNoHits, session.Summary);
        }
    }

    [AvaloniaFact]
    public async Task ADotFindsOnlyTheDots()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start(".", default);
            await FinishAsync(session);

            var texts = fixture.Session.EnsurePageTexts()!;
            var dots = 0;
            for (var page = 0; page < 2; page++)
            {
                var text = await texts.GetAsync(page, cancellationToken: TestContext.Current.CancellationToken);
                dots += text.GetText(0, text.Chars.Count).Count(c => c == '.');
            }

            Assert.Equal(dots, session.Hits.Count);
            Assert.All(session.Hits, h => Assert.Equal(".", h.Snippet.Match));
        }
    }

    [AvaloniaFact]
    public async Task OptionsMatchCaseAndWholeWordApply()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start("Zeile", new SearchOptions(MatchCase: true));
            await FinishAsync(session);
            Assert.Single(session.Hits);

            session.Start("zeile", new SearchOptions(MatchCase: true));
            await FinishAsync(session);
            Assert.Empty(session.Hits);

            session.Start("Zeil", new SearchOptions(WholeWord: true));
            await FinishAsync(session);
            Assert.Empty(session.Hits);
        }
    }

    [AvaloniaFact]
    public async Task LimitIsReportedAt10000Hits()
    {
        var (fixture, session) = await OpenAsync("text-dense.pdf");
        using (fixture)
        using (session)
        {
            session.Start("a", default);
            await FinishAsync(session);

            Assert.Equal(DocumentSearch.MaxHits, session.Hits.Count);
            Assert.True(session.LimitReached);
            Assert.False(session.IsRunning);
            Assert.Equal("More than 10,000 hits", session.Summary);
        }
    }

    [AvaloniaFact]
    public async Task GermanAndEnglishSummary()
    {
        var (fixture, session) = await OpenAsync("text-dense.pdf");
        using (fixture)
        using (session)
        {
            session.Start("Needle", default);
            await FinishAsync(session);
            Assert.Equal("20 hits", session.Summary);

            PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("20 Treffer", session.Summary);

            session.Start("Unfindable-word", default);
            await FinishAsync(session);
            Assert.Equal("Keine Treffer", session.Summary);

            session.Start("a", default);
            await FinishAsync(session);
            Assert.Equal("Mehr als 10.000 Treffer", session.Summary);

            PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("More than 10,000 hits", session.Summary);
        }
    }

    [AvaloniaFact]
    public async Task OneHitIsSingular()
    {
        var (fixture, session) = await OpenAsync("text-rich.pdf");
        using (fixture)
        using (session)
        {
            session.Start("Zeile", default);
            await FinishAsync(session);
            Assert.Equal("1 hit", session.Summary);
        }
    }
}
