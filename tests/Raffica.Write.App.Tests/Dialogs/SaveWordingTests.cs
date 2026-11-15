using System.Globalization;
using Raffica.Core.Saving;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Docx;
using Xunit;

namespace Raffica.WriteApp.Tests.Dialogs;

/// <summary>The words of the save cards: every failure and check reason has wording in both languages, the UI never shows the text of an exception, and nothing overpromises.</summary>
public sealed class SaveWordingTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    public static TheoryData<SaveFailureReason> Failures => [.. Enum.GetValues<SaveFailureReason>()];

    public static TheoryData<DocxSaveReason> Checks => [.. Enum.GetValues<DocxSaveReason>()];

    private static IEnumerable<string> SaveKeys() =>
    [
        "CmdSave", "CmdSaveAs", "SaveDialogTitle", "ReasonNothingToSave", "ReasonSaveFaulted", "ReasonSaveAsFaulted", "SaveErrorTitle", "SaveErrorKept", "SaveLeftoverNoteFormat",
        "ChangedTitle", "ChangedMessageFormat", "ChangedOverwrite", "UnsavedTitle", "UnsavedMessageFormat", "UnsavedDontSave", "EditFailed",
        "SaveFailNotWritable", "SaveFailDiskFull", "SaveFailWriteFailed", "SaveFailTargetExists", "SaveFailOriginalChanged", "SaveFailOther", "SaveFailCheck", "SaveFailUnreadable",
        "SaveFailOpenElsewhere", "SaveFailUnexpected",
    ];

    private static void AssertWorded(string key)
    {
        var english = WriteStrings.GetIn(key, English);
        var german = WriteStrings.GetIn(key, German);
        Assert.False(string.IsNullOrWhiteSpace(english), key);
        Assert.False(string.IsNullOrWhiteSpace(german), key);
        Assert.NotEqual(english, german);
        Assert.EndsWith(".", english, StringComparison.Ordinal);
        Assert.EndsWith(".", german, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void EveryFailureReasonHasWordingInBothLanguages(SaveFailureReason reason) => AssertWorded(WriteStrings.SaveFailureKey(reason));

    [Theory]
    [MemberData(nameof(Checks))]
    public void EveryCheckReasonHasWordingInBothLanguages(DocxSaveReason reason) => AssertWorded(WriteStrings.SaveCheckKey(reason));

    [Fact]
    public void AnUnknownValueHasNoWordingAndTheOtherProblemsHaveTheirs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WriteStrings.SaveFailureKey((SaveFailureReason)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => WriteStrings.SaveCheckKey((DocxSaveReason)99));
        AssertWorded("SaveFailOpenElsewhere");
        AssertWorded("SaveFailUnexpected");
        Assert.Equal(WriteStrings.SaveFailUnexpected, new Raffica.Write.Documents.SaveProblem(Raffica.Write.Documents.SaveProblemKind.Unexpected).Text);
    }

    [Fact]
    public void AProblemIsWordedFromTheExceptionTypeAndNeverFromItsText()
    {
        var failure = Raffica.Write.Documents.SaveProblem.Of(new SaveFailureException(SaveFailureReason.DiskFull, "The disk is full: C:\\secret\\thesis.docx"));
        Assert.Equal(WriteStrings.ForSaveFailure(SaveFailureReason.DiskFull), failure.Text);
        Assert.DoesNotContain("secret", failure.Text, StringComparison.Ordinal);
        var check = Raffica.Write.Documents.SaveProblem.Of(new DocxSaveException(DocxSaveReason.ModelNotReadable, "/word/document.xml"));
        Assert.Equal(WriteStrings.ForSaveCheck(DocxSaveReason.ModelNotReadable), check.Text);
        Assert.Equal(WriteStrings.SaveFailUnexpected, Raffica.Write.Documents.SaveProblem.Of(new InvalidOperationException("secret text of the document")).Text);
    }

    [Fact]
    public void TheWordingNeverNamesTheSystemOrOverpromises()
    {
        foreach (var key in SaveKeys())
        {
            foreach (var culture in new[] { English, German })
            {
                var text = WriteStrings.GetIn(key, culture);
                Assert.DoesNotContain("Exception", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("HRESULT", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("0x", text, StringComparison.OrdinalIgnoreCase);
                foreach (var word in new[] { "safe", "guarantee", "sicher", "garantiert", "correct", "korrekt", "intact", "unversehrt" })
                {
                    Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void TheEditFailedHintIsTrueAfterASaveToo()
    {
        foreach (var culture in new[] { English, German })
        {
            var text = WriteStrings.GetIn("EditFailed", culture);
            Assert.DoesNotContain("has not been changed", text, StringComparison.Ordinal);
            Assert.DoesNotContain("nicht verändert", text, StringComparison.Ordinal);
        }

        Assert.Contains("since the last save may be lost", WriteStrings.GetIn("EditFailed", English), StringComparison.Ordinal);
        Assert.Contains("seit dem letzten Speichern", WriteStrings.GetIn("EditFailed", German), StringComparison.Ordinal);
        Assert.Contains("Save as", WriteStrings.GetIn("EditFailed", English), StringComparison.Ordinal);
        Assert.Contains(WriteStrings.GetIn("CmdSaveAs", German), WriteStrings.GetIn("EditFailed", German), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalHasItsReasonAndOnlyNoDocumentHasNone()
    {
        foreach (var refusal in Enum.GetValues<SaveRefusal>())
        {
            var reason = WriteCommandRules.SaveReason(refusal, saveAs: false);
            if (refusal == SaveRefusal.NoDocument)
            {
                Assert.Null(reason);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(reason), refusal.ToString());
            }
        }

        Assert.NotEqual(WriteCommandRules.SaveReason(SaveRefusal.Unavailable, saveAs: false), WriteCommandRules.SaveReason(SaveRefusal.Unavailable, saveAs: true));
    }
}
