using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>The flow of "Insert file": picker, page count, password, card, insertion, and every way it ends without a change.</summary>
public sealed class InsertFileTests
{
    private const string Password = "hôtel";

    // The pieces of the flow, scripted: the path the picker gives, the passwords the card gives in turn, the choice of the insert card.
    private sealed class Harness
    {
        public string? Path { get; set; }

        public Queue<string?> Passwords { get; } = new();

        public InsertChoice? Choice { get; set; } = new("all", InsertPosition.AtEnd);

        public List<bool> PasswordAsks { get; } = [];

        public List<(string Name, int Pages, string? After)> CardsShown { get; } = [];

        public List<OrganizeResult> Refusals { get; } = [];

        public InsertFileFlow Flow { get; }

        public Harness()
        {
            var engine = new PdfEngine();
            Flow = new InsertFileFlow(
                () => Task.FromResult(Path),
                (_, incorrect) =>
                {
                    PasswordAsks.Add(incorrect);
                    return Task.FromResult(Passwords.Count > 0 ? Passwords.Dequeue() : null);
                },
                (name, pages, after) =>
                {
                    CardsShown.Add((name, pages, after));
                    return Task.FromResult(Choice);
                },
                result =>
                {
                    Refusals.Add(result);
                    return Task.CompletedTask;
                },
                () => engine);
        }
    }

    private static async Task<(OrganizeFile File, OrganizeSession Session)> OpenPlanAsync()
    {
        var file = await OrganizeFile.OpenAsync();
        var session = file.Tab.Organize;
        session.Enter();
        return (file, session);
    }

    [AvaloniaFact]
    public async Task InsertAddsThePagesAtTheChosenPositionAndSelectsThem()
    {
        var (file, session) = await OpenPlanAsync();
        using (file)
        {
            var other = file.CopyOf("pages-marked.pdf", "other.pdf");
            var h = new Harness { Path = other };

            // At the end, a range.
            h.Choice = new InsertChoice("2-3", InsertPosition.AtEnd);
            var result = await h.Flow.RunAsync(file.Tab);
            Assert.True(result?.IsDone);
            Assert.Equal(8, session.Plan.Slots.Count);
            Assert.Equal([6, 7], session.Selection.Positions);
            Assert.Equal("other.pdf", Assert.Single(h.CardsShown).Name);
            Assert.Equal(6, h.CardsShown[0].Pages);
            Assert.Null(h.CardsShown[0].After); // nothing was selected: the choice is not offered

            // At the start.
            h.Choice = new InsertChoice("1", InsertPosition.AtStart);
            Assert.True((await h.Flow.RunAsync(file.Tab))?.IsDone);
            Assert.Equal(9, session.Plan.Slots.Count);
            Assert.Equal([0], session.Selection.Positions);
            Assert.NotEqual(0, session.Plan.Slots[0].Source.Value);

            // After the selected pages (positions 3 and 4 of the plan).
            session.Selection.Set(session.Plan.Slots.Count, [3, 4]);
            h.Choice = new InsertChoice("all", InsertPosition.AfterSelection);
            Assert.True((await h.Flow.RunAsync(file.Tab))?.IsDone);
            Assert.Equal(15, session.Plan.Slots.Count);
            Assert.Equal([5, 6, 7, 8, 9, 10], session.Selection.Positions);
            Assert.Contains("5", h.CardsShown[^1].After); // "after page 5"
            Assert.Empty(h.Refusals);
            Assert.True(file.Tab.IsDirty);
        }
    }

    [AvaloniaFact]
    public async Task EncryptedFileAsksForAPasswordAndInsertsAfterTheRightOne()
    {
        var (file, session) = await OpenPlanAsync();
        using (file)
        {
            var encrypted = file.CopyOf("encrypted_hello_world_r3.pdf");
            var h = new Harness { Path = encrypted, Choice = new InsertChoice("all", InsertPosition.AtEnd) };
            h.Passwords.Enqueue("nope");
            h.Passwords.Enqueue(Password);

            var result = await h.Flow.RunAsync(file.Tab);

            Assert.True(result?.IsDone, result?.Problem.ToString());
            Assert.Equal([false, true], h.PasswordAsks);
            Assert.Equal(7, session.Plan.Slots.Count);
            Assert.Equal(Password, Assert.Single(session.Inserted).Value.File.Password);
            Assert.Empty(h.Refusals);

            // A cancel at the first question says nothing; a cancel after a wrong password says the password is wrong.
            var silent = new Harness { Path = encrypted };
            Assert.Null(await silent.Flow.RunAsync(file.Tab));
            Assert.Empty(silent.Refusals);

            var wrong = new Harness { Path = encrypted };
            wrong.Passwords.Enqueue("nope");
            var refused = await wrong.Flow.RunAsync(file.Tab);
            Assert.Equal(OrganizeProblem.WrongPassword, refused?.Problem);
            Assert.Equal(OrganizeProblem.WrongPassword, Assert.Single(wrong.Refusals).Problem);
            Assert.Equal(7, session.Plan.Slots.Count);
        }
    }

    [AvaloniaFact]
    public async Task EveryRefusalEndsInAReasonAndLeavesThePlan()
    {
        var (file, session) = await OpenPlanAsync();
        using (file)
        {
            var one = file.CopyOf("hello_world.pdf", "one.pdf");
            var h = new Harness { Path = file.Path };
            var plan = session.Plan;
            var version = session.Version;

            async Task Expect(string? path, OrganizeProblem problem, string range = "all")
            {
                h.Path = path;
                h.Choice = new InsertChoice(range, InsertPosition.AtEnd);
                h.Refusals.Clear();
                var result = await h.Flow.RunAsync(file.Tab);
                Assert.Equal(problem, result?.Problem);
                Assert.Equal(problem, Assert.Single(h.Refusals).Problem);
                Assert.False(string.IsNullOrWhiteSpace(PdfStrings.InsertRefusal(h.Refusals[0])));
            }

            // The open file itself is refused before any card.
            await Expect(file.Path, OrganizeProblem.SameFile);
            Assert.Empty(h.CardsShown);

            // Not a PDF, a corrupt file, a missing file.
            await Expect(file.CopyOf("not-a-pdf.pdf"), OrganizeProblem.Unreadable);
            var corrupt = Path.Combine(file.Folder, "corrupt.pdf");
            await File.WriteAllBytesAsync(corrupt, (await File.ReadAllBytesAsync(TestSupportPath("pages-marked.pdf")))[..400]);
            await Expect(corrupt, OrganizeProblem.Unreadable);
            await Expect(Path.Combine(file.Folder, "missing.pdf"), OrganizeProblem.Unreadable);

            // A range outside the file gets past a card that was bypassed (the session still refuses it).
            await Expect(file.CopyOf("pages-marked.pdf", "other.pdf"), OrganizeProblem.BadRange, "99");

            Assert.Same(plan, session.Plan);
            Assert.Equal(version, session.Version);
            Assert.Empty(session.Inserted);
            Assert.False(file.Tab.IsDirty);

            // The 21st file.
            for (var i = 0; i < PageEditLimits.MaxInsertedFiles; i++)
            {
                Assert.True((await session.InsertFileAsync(one, null, "1", session.Plan.Slots.Count)).IsDone);
            }

            var full = session.Plan;
            await Expect(one, OrganizeProblem.TooManyFiles);
            Assert.Same(full, session.Plan);
        }

        // 20 001 pages: the plan is brought to 20 000 (doubling, then a part), one more page is over the limit.
        var (big, bigSession) = await OpenPlanAsync();
        using (big)
        {
            while (bigSession.Plan.Slots.Count * 2 <= PageEditLimits.MaxSlots)
            {
                bigSession.Selection.SelectAll();
                Assert.True(bigSession.Duplicate().IsDone);
            }

            bigSession.Selection.Set(bigSession.Plan.Slots.Count, Enumerable.Range(0, PageEditLimits.MaxSlots - bigSession.Plan.Slots.Count));
            Assert.True(bigSession.Duplicate().IsDone);
            Assert.Equal(PageEditLimits.MaxSlots, bigSession.Plan.Slots.Count);

            var h = new Harness { Path = big.CopyOf("hello_world.pdf", "one.pdf"), Choice = new InsertChoice("1", InsertPosition.AtEnd) };
            var before = bigSession.Plan;
            var result = await h.Flow.RunAsync(big.Tab);

            Assert.Equal(OrganizeProblem.LimitExceeded, result?.Problem);
            Assert.Equal(OrganizeProblem.LimitExceeded, Assert.Single(h.Refusals).Problem);
            Assert.Same(before, bigSession.Plan);
            Assert.Empty(bigSession.Inserted);
        }
    }

    private static string TestSupportPath(string name) => Raffica.TestSupport.TestData.Pdf(name);

    [AvaloniaFact]
    public async Task CancelAnywhereChangesNothing()
    {
        var (file, session) = await OpenPlanAsync();
        using (file)
        {
            var other = file.CopyOf("pages-marked.pdf", "other.pdf");

            // The picker is canceled.
            var none = new Harness { Path = null };
            Assert.Null(await none.Flow.RunAsync(file.Tab));
            Assert.Empty(none.CardsShown);

            // The card is canceled.
            var card = new Harness { Path = other, Choice = null };
            Assert.Null(await card.Flow.RunAsync(file.Tab));
            Assert.Single(card.CardsShown);

            // The mode ends while the card is open.
            var flow = new InsertFileFlow(
                () => Task.FromResult<string?>(other),
                (_, _) => Task.FromResult<string?>(null),
                (_, _, _) =>
                {
                    session.End();
                    return Task.FromResult<InsertChoice?>(new InsertChoice("all", InsertPosition.AtEnd));
                },
                _ => Task.CompletedTask,
                () => new PdfEngine());
            Assert.Null(await flow.RunAsync(file.Tab));

            Assert.False(session.IsActive);
            Assert.Empty(session.Inserted);
            Assert.False(file.Tab.IsDirty);

            // No plan open: nothing is asked at all.
            var closed = new Harness { Path = other };
            Assert.Null(await closed.Flow.RunAsync(file.Tab));
            Assert.Empty(closed.CardsShown);
        }
    }

    [AvaloniaFact]
    public async Task BlankPageTakesTheSizeOfTheNeighbour()
    {
        using var fixture = WindowFixture.Create();
        var tab = await fixture.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        var window = fixture.Window;
        var command = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "pages.blank");
        window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated?.IsActive == true);
        var session = tab.Organize;

        // After the third page (landscape in pages-marked.pdf).
        session.Selection.Select(2);
        command.Run!();
        var first = session.Plan.Slots[3].Blank;
        Assert.NotNull(first);
        Assert.True(first.Width > first.Height);

        // After a blank page: the size of that blank page.
        session.Selection.Select(3);
        command.Run!();
        Assert.Equal(first, session.Plan.Slots[4].Blank);

        // Nothing selected: at the end, the size of the last page.
        session.Selection.Set(session.Plan.Slots.Count, []);
        command.Run!();
        var last = session.Plan.Slots[^1].Blank;
        Assert.NotNull(last);
        Assert.Equal(window.ViewOf(tab)!.PageSizeOf(5)!.Value.Width, last.Width, 3);
    }

    [AvaloniaFact]
    public async Task InsertedFileDocumentIsOpenedOffTheUiThread()
    {
        var (file, session) = await OpenPlanAsync();
        using (file)
        {
            var h = new Harness { Path = file.CopyOf("pages-marked.pdf", "other.pdf") };
            var opened = new List<bool>();
            h.Flow.OpenProbe = () => opened.Add(Dispatcher.UIThread.CheckAccess());

            var result = await h.Flow.RunAsync(file.Tab);

            Assert.True(result?.IsDone);
            Assert.Equal([false], opened);
            Assert.Equal(12, session.Plan.Slots.Count);
        }
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.StartsWith("Insert", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count >= 15, "the insert texts are found by name");
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            PdfStrings.Culture = info;
            var texts = keys.Select(k => PdfStrings.GetIn(k, info)).ToList();
            texts.AddRange(Enum.GetValues<OrganizeProblem>().Select(p => PdfStrings.InsertRefusal(OrganizeResult.Refused(p))));
            Assert.All(texts, t => Assert.False(banned.IsMatch(t), t));
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
    }
}
