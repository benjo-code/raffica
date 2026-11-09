using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>The German UI addresses the user as "Sie" everywhere; a "du" form in either project's strings fails here.</summary>
public sealed class GermanRegisterTests
{
    private static readonly Regex s_duForm = new(
        @"\b(du|dich|dir|dein\w*)\b|\b(wähle|gib|öffne|beende|verwirf|wende|lass|klicke|drücke|schließe|speichere|prüfe|versuche)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static TheoryData<string> Projects => new() { "Raffica.Pdf", "Raffica.Shell" };

    [Theory]
    [MemberData(nameof(Projects))]
    public void NoDuForms(string project)
    {
        var values = XDocument.Load(RepoPaths.Combine("src", project, "Resources", "Strings.de.resx"))
            .Root!.Elements("data")
            .Select(d => (Name: (string)d.Attribute("name")!, Value: (string?)d.Element("value") ?? string.Empty))
            .ToList();
        Assert.NotEmpty(values);

        var hits = values
            .Where(v => s_duForm.IsMatch(v.Value))
            .Select(v => $"{v.Name}: {v.Value}")
            .ToList();
        Assert.True(hits.Count == 0, "German texts in the du form (use Sie):" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }
}
