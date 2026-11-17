using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Raffica.Shell.Controls;
using Raffica.Write;
using Raffica.Write.Dialogs;
using Raffica.Write.Recovery;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

public sealed class RecoveryCardTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    private static List<RecoveryEntry> Entries(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new RecoveryEntry(RecoveryStore.NewId(), $"doc{i}.docx", null, s_now.AddMinutes(-i), 10, 1, s_now))];

    private static List<Button> Buttons(Dialog dialog) => [.. Assert.IsType<StackPanel>(dialog.Footer).Children.OfType<Button>()];

    [AvaloniaFact]
    public void TheFooterIsRestoreDiscardAndDecideLater()
    {
        var buttons = Buttons(RecoveryCard.Create(Entries(2), _ => { }));

        Assert.Equal(["PART_Restore", "PART_Discard", "PART_Cancel"], buttons.Select(b => b.Name));
        Assert.Equal([WriteStrings.RecoveryRestore, WriteStrings.RecoveryDiscard, WriteStrings.RecoveryLater], buttons.Select(b => b.Content));
        Assert.Equal("PART_Restore", Assert.Single(buttons, b => b.IsDefault).Name);
        Assert.Equal("PART_Cancel", Assert.Single(buttons, b => b.IsCancel).Name);
    }

    [AvaloniaFact]
    public void EachButtonGivesItsAnswerOnce()
    {
        foreach (var (button, expected) in new[] { ("PART_Restore", RecoveryChoice.Restore), ("PART_Discard", RecoveryChoice.Discard), ("PART_Cancel", RecoveryChoice.Later) })
        {
            var answers = new List<RecoveryChoice>();
            var dialog = RecoveryCard.Create(Entries(1), answers.Add);
            Buttons(dialog).Single(b => b.Name == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Buttons(dialog).Single(b => b.Name == "PART_Restore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([expected], answers);
        }
    }

    [AvaloniaFact]
    public void ThreeEntriesAreAllNamedWithoutAMoreLine()
    {
        var dialog = RecoveryCard.Create(Entries(3), _ => { });
        var list = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(dialog.Content).Children[1]);

        Assert.Equal(3, list.Children.Count);
        Assert.DoesNotContain(list.Children.OfType<TextBlock>(), t => t.Name == "PART_More");
        Assert.Equal(WriteStrings.RecoveryTitle, dialog.Title);
    }

    [AvaloniaFact]
    public void FiftyEntriesBuildFastAndListTwentyAndTheRest()
    {
        var entries = Entries(50);
        var watch = Stopwatch.StartNew();
        var dialog = RecoveryCard.Create(entries, _ => { });
        watch.Stop();

        var list = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(dialog.Content).Children[1]);
        Assert.Equal(21, list.Children.Count);
        Assert.Equal("… and 30 more", Assert.IsType<TextBlock>(list.Children[^1]).Text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"the card took {watch.Elapsed}");
    }
}
