using System.Text;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class StylesNumberingSettingsTests
{
    private static readonly XNamespace W = XmlSnippets.W;

    private const string Declarations =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:x=\"urn:test\" mc:Ignorable=\"w14\"";

    private static string Thesis => Path.Combine(TestData.Root, "spikes", "write", "testdoc", "thesis.docx");

    private static string Wrap(string root, string inner) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:{root} {Declarations}>{inner}</w:{root}>";

    private static void AssertEquivalent(string xml, byte[] written)
    {
        var text = Encoding.UTF8.GetString(written);
        var difference = XmlEquivalence.Explain(XElement.Parse(xml), XElement.Parse(text));
        Assert.True(difference is null, difference + "\n" + text);
    }

    private static (StyleSheet Model, byte[] Written) StylesRoundTrip(byte[] source)
    {
        using var input = new MemoryStream(source);
        var model = StylesXml.Read(input);
        using var output = new MemoryStream();
        StylesXml.Write(model, output);
        AssertEquivalent(Encoding.UTF8.GetString(source), output.ToArray());
        return (model, output.ToArray());
    }

    private static (NumberingDefinitions Model, byte[] Written) NumberingRoundTrip(byte[] source)
    {
        using var input = new MemoryStream(source);
        var model = NumberingXml.Read(input);
        using var output = new MemoryStream();
        NumberingXml.Write(model, output);
        AssertEquivalent(Encoding.UTF8.GetString(source), output.ToArray());
        return (model, output.ToArray());
    }

    private static (DocumentSettings Model, byte[] Written) SettingsRoundTrip(byte[] source)
    {
        using var input = new MemoryStream(source);
        var model = SettingsXml.Read(input);
        using var output = new MemoryStream();
        SettingsXml.Write(model, output);
        AssertEquivalent(Encoding.UTF8.GetString(source), output.ToArray());
        return (model, output.ToArray());
    }

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    [Fact]
    public void StylesReadTypedAndKeepTheRest()
    {
        var (model, _) = StylesRoundTrip(PartComparer.ReadPart(Thesis, "word/styles.xml"));

        var heading = model.Find("Heading2");
        Assert.NotNull(heading);
        Assert.Equal("heading 2", heading.Name);
        Assert.Equal("Normal", heading.BasedOn);
        Assert.Equal("Normal", heading.Next);
        Assert.Equal("paragraph", heading.Type);
        Assert.True(heading.ParagraphProperties.WasPresent);
        Assert.Equal(1, heading.ParagraphProperties.NumberingLevel);
        Assert.NotNull(model.Defaults);
        Assert.Equal(W + "docDefaults", model.Defaults.Name);
        Assert.Null(model.Find("NoSuchStyle"));
    }

    [Fact]
    public void StylesKeepDefaultsLatentStylesAndUnknownChildrenInAnyOrder()
    {
        var xml = Wrap(
            "styles",
            """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal" w:customStyle="0" x:extra="e"><x:before a="1"/><w:name w:val="Normal"/><x:between/><w:qFormat/>
              <w:pPr><w:spacing w:after="0"/></w:pPr><x:mid/><w:rPr><w:b/></w:rPr><w:tblPr/><x:after><x:deep w14:x="1"/></x:after></w:style>
            <x:first/>
            <w:latentStyles w:defLockedState="0"><w:lsdException w:name="Normal" w:uiPriority="0"/></w:latentStyles>
            <x:second w:val="2"/>
            <w:style w:type="character" w:styleId="Strong"><w:name w:val="Strong"/><w:link w:val="Other"/><w:rPr><w:b/></w:rPr></w:style>
            <w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
            <w:style w:type="made-up-kind" w:styleId="Odd"/>
            <x:last/>
            """);
        var (model, _) = StylesRoundTrip(Bytes(xml));

        Assert.Equal(3, model.Styles.Count);
        var normal = model.Find("Normal")!;
        Assert.True(normal.IsDefault);
        Assert.Equal("Normal", normal.Name);
        Assert.Equal(2, normal.UnknownAttributes.Count);
        Assert.True(normal.RunProperties.Bold);
        Assert.True(normal.TableProperties.WasPresent);
        Assert.Equal("Other", model.Find("Strong")!.Link);
        Assert.Equal("made-up-kind", model.Find("Odd")!.Type);
        Assert.NotNull(model.LatentStyles);
        Assert.Contains("mc:Ignorable", model.Root.Attributes.Select(a => a.Name.Namespace == XNamespace.Get("http://schemas.openxmlformats.org/markup-compatibility/2006") ? "mc:" + a.Name.LocalName : a.Name.LocalName));
    }

    [Fact]
    public void StyleDefaultSpellingAndDuplicatesSurvive()
    {
        var xml = Wrap(
            "styles",
            """<w:style w:type="paragraph" w:default="true" w:styleId="A"><w:name w:val="A"/><w:name w:val="again"/><w:basedOn w:val="X" w:extra="1"/><w:next w:val="B"/></w:style><w:style w:styleId="B"/>""");
        var (model, written) = StylesRoundTrip(Bytes(xml));
        Assert.True(model.Find("A")!.IsDefault);
        Assert.Equal("A", model.Find("A")!.Name);
        Assert.Null(model.Find("A")!.BasedOn);
        Assert.Equal("B", model.Find("A")!.Next);
        Assert.Null(model.Find("B")!.Type);

        model.Find("B")!.IsDefault = true;
        model.Find("B")!.Type = "paragraph";
        using var output = new MemoryStream();
        StylesXml.Write(model, output);
        var again = XElement.Parse(Encoding.UTF8.GetString(output.ToArray()));
        var b = again.Elements(W + "style").Last();
        Assert.Equal("1", (string?)b.Attribute(W + "default"));
        Assert.Equal("paragraph", (string?)b.Attribute(W + "type"));
        Assert.NotEmpty(written);
    }

    [Fact]
    public void NumberingReadsLevelsAndInstances()
    {
        var (model, _) = NumberingRoundTrip(PartComparer.ReadPart(Thesis, "word/numbering.xml"));

        var abstractNumbering = model.Abstracts[0];
        Assert.Equal(9, abstractNumbering.Levels.Count);
        var second = abstractNumbering.Levels[1];
        Assert.Equal(1, second.Level);
        Assert.Equal(1, second.Start);
        Assert.Equal("decimal", second.Format);
        Assert.Equal("%1.%2", second.Text);
        Assert.Equal("left", second.Justification);
        Assert.True(second.ParagraphProperties.WasPresent);
        Assert.NotEmpty(model.Instances);
        Assert.Equal(model.Abstracts[0].AbstractNumId, model.Instances[0].AbstractNumId);
        Assert.NotNull(model.FindInstance(model.Instances[0].NumId));
        Assert.NotNull(model.FindAbstract(model.Abstracts[0].AbstractNumId));
    }

    [Fact]
    public void NumberingKeepsOverridesUnknownChildrenAndAttributes()
    {
        var xml = Wrap(
            "numbering",
            """
            <x:lead/>
            <w:numPicBullet w:numPicBulletId="0"><w:pict/></w:numPicBullet>
            <w:abstractNum w:abstractNumId="7" w15:restartNumberingAfterBreak="0" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml">
              <w:nsid w:val="1A2B3C4D"/><x:between/><w:multiLevelType w:val="hybridMultilevel"/><w:tmpl w:val="00000000"/>
              <w:lvl w:ilvl="0" w:tplc="04070001" w:tentative="1" x:u="1">
                <w:start w:val="3"/><w:numFmt w:val="upperRoman"/><x:inLvl/><w:lvlRestart w:val="0"/><w:pStyle w:val="Heading1"/><w:isLgl/>
                <w:lvlText w:val="%1)"/><w:lvlJc w:val="right"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:b/></w:rPr><x:tail/>
              </w:lvl>
              <w:lvl w:ilvl="1"><w:numFmt w:val="fancy-new-format"/><w:lvlText w:val=""/></w:lvl>
              <x:afterLevels/>
            </w:abstractNum>
            <x:mid/>
            <w:num w:numId="3" x:flag="1"><x:a/><w:abstractNumId w:val="7"/>
              <w:lvlOverride w:ilvl="0"><w:startOverride w:val="5"/></w:lvlOverride>
              <x:b/>
              <w:lvlOverride w:ilvl="1"><w:lvl w:ilvl="1"><w:start w:val="9"/><w:numFmt w:val="bullet"/></w:lvl></w:lvlOverride>
            </w:num>
            <w:num w:numId="4"><w:abstractNumId w:val="7"/></w:num>
            <w:numIdMacAtCleanup w:val="2"/>
            """);
        var (model, _) = NumberingRoundTrip(Bytes(xml));

        var abstractNumbering = Assert.Single(model.Abstracts);
        Assert.Equal(7, abstractNumbering.AbstractNumId);
        Assert.Equal(2, abstractNumbering.UnknownAttributes.Count);
        Assert.Equal(2, abstractNumbering.Levels.Count);
        var level = abstractNumbering.Levels[0];
        Assert.Equal(3, level.Start);
        Assert.Equal("upperRoman", level.Format);
        Assert.Equal("%1)", level.Text);
        Assert.Equal("right", level.Justification);
        Assert.Equal(3, level.UnknownAttributes.Count);
        Assert.True(level.RunProperties.Bold);
        Assert.Equal("fancy-new-format", abstractNumbering.Levels[1].Format);
        Assert.Equal(string.Empty, abstractNumbering.Levels[1].Text);

        var instance = model.FindInstance(3)!;
        Assert.Equal(7, instance.AbstractNumId);
        Assert.Equal(2, instance.Overrides.Count);
        Assert.Equal(0, instance.Overrides[0].Level);
        Assert.Equal(5, instance.Overrides[0].StartOverride);
        Assert.Equal(9, instance.Overrides[1].Definition!.Start);
        Assert.Single(instance.UnknownAttributes);
    }

    [Fact]
    public void SettingsFlagsReadAndTheRestSurvives()
    {
        var (model, _) = SettingsRoundTrip(PartComparer.ReadPart(Thesis, "word/settings.xml"));
        Assert.False(model.TrackRevisions);
        Assert.False(model.AutoHyphenation);
        Assert.False(model.EvenAndOddHeaders);
        Assert.False(model.UpdateFields);
        Assert.NotNull(model.Properties.Get(W + "compat"));

        var xml = Wrap(
            "settings",
            """<w:zoom w:percent="100"/><w:trackRevisions/><x:between/><w:autoHyphenation w:val="0"/><w:evenAndOddHeaders w:val="on"/><w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat><w:rsids><w:rsidRoot w:val="00112233"/><w:rsid w:val="00112233"/></w:rsids><w:updateFields w:val="true"/><x:last/>""");
        var (flags, _) = SettingsRoundTrip(Bytes(xml));
        Assert.True(flags.TrackRevisions);
        Assert.False(flags.AutoHyphenation);
        Assert.True(flags.EvenAndOddHeaders);
        Assert.True(flags.UpdateFields);
        Assert.NotNull(flags.Properties.Get(W + "rsids"));
        Assert.Contains(flags.Root.Attributes, a => a.Name.LocalName == "Ignorable" && a.Value == "w14");
    }

    [Fact]
    public void SettingFlagsWritesAndRemovesTheElementAtTheRightPlace()
    {
        var xml = Wrap("settings", """<w:zoom w:percent="100"/><x:between/><w:defaultTabStop w:val="720"/><w:compat/><x:last/>""");
        using var input = new MemoryStream(Bytes(xml));
        var model = SettingsXml.Read(input);
        model.TrackRevisions = true;
        model.UpdateFields = true;
        model.AutoHyphenation = true;
        model.AutoHyphenation = false;
        using var output = new MemoryStream();
        SettingsXml.Write(model, output);

        var names = XElement.Parse(Encoding.UTF8.GetString(output.ToArray())).Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["zoom", "between", "trackRevisions", "defaultTabStop", "updateFields", "compat", "last"], names);

        using var again = new MemoryStream(output.ToArray());
        var reread = SettingsXml.Read(again);
        Assert.True(reread.TrackRevisions);
        Assert.True(reread.UpdateFields);
        Assert.False(reread.AutoHyphenation);
    }

    [Fact]
    public void ModelBuiltInCodeWritesAndReadsBack()
    {
        var root = new OoxmlRoot(W + "numbering", [new XAttribute(XNamespace.Xmlns + "w", XmlSnippets.W)]);
        var model = new NumberingDefinitions(root);
        var abstractNumbering = new AbstractNumbering { AbstractNumId = 2 };
        var level = new NumberingLevel { Level = 0, Start = 1, Format = "bullet", Text = "-", Justification = "left" };
        level.ParagraphProperties.Set(W + "ind", new XElement(W + "ind", new XAttribute(W + "left", "360")));
        abstractNumbering.Levels.Add(level);
        model.Abstracts.Add(abstractNumbering);
        model.Instances.Add(new NumberingInstance { NumId = 1, AbstractNumId = 2 });
        using var output = new MemoryStream();
        NumberingXml.Write(model, output);

        var lvl = XElement.Parse(Encoding.UTF8.GetString(output.ToArray())).Element(W + "abstractNum")!.Element(W + "lvl")!;
        Assert.Equal(["start", "numFmt", "lvlText", "lvlJc", "pPr"], lvl.Elements().Select(e => e.Name.LocalName).ToArray());
        using var again = new MemoryStream(output.ToArray());
        var reread = NumberingXml.Read(again);
        Assert.Equal("-", reread.Abstracts[0].Levels[0].Text);
        Assert.Equal(2, reread.FindInstance(1)!.AbstractNumId);
    }

    [Fact]
    public void WrittenPartsStartWithADeclarationAndNoByteOrderMark()
    {
        using var input = new MemoryStream(PartComparer.ReadPart(Thesis, "word/styles.xml"));
        using var output = new MemoryStream();
        StylesXml.Write(StylesXml.Read(input), output);
        var bytes = output.ToArray();
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", Encoding.UTF8.GetString(bytes));
    }
}
