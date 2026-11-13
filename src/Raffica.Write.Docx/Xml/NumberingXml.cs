using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>The numbering part (<c>word/numbering.xml</c>): a stream in, <see cref="NumberingDefinitions"/> out, and back. Children and attributes the model does not name are kept in their place.</summary>
internal static class NumberingXml
{
    // The order of the children of w:lvl (CT_Lvl), used only to place a child made in code.
    private static readonly string[] s_levelOrder =
        ["start", "numFmt", "lvlRestart", "pStyle", "isLgl", "suff", "lvlText", "lvlPicBulletId", "legacy", "lvlJc", "pPr", "rPr"];

    private static readonly string[] s_rootOrder = ["numPicBullet", "abstractNum", "num", "numIdMacAtCleanup"];
    private static readonly string[] s_abstractOrder = ["nsid", "multiLevelType", "tmpl", "name", "styleLink", "numStyleLink", "lvl"];
    private static readonly string[] s_instanceOrder = ["abstractNumId", "lvlOverride"];
    private static readonly string[] s_overrideOrder = ["startOverride", "lvl"];

    public static NumberingDefinitions Read(Stream stream) => PartXml.Read(stream, root => new NumberingDefinitions(root), ReadChild);

    private static void ReadChild(NumberingDefinitions model, XElement element, int index)
    {
        if (element.Name.Namespace == Names.W)
        {
            switch (element.Name.LocalName)
            {
                case "abstractNum":
                    model.Abstracts.Add(ReadAbstract(element, index));
                    return;
                case "num":
                    model.Instances.Add(ReadInstance(element, index));
                    return;
            }
        }

        PartXml.Keep(model, element, index);
    }

    private static AbstractNumbering ReadAbstract(XElement element, int position)
    {
        var abstractNumbering = new AbstractNumbering { Position = position };
        abstractNumbering.AbstractNumId = PartXml.ReadAttributes(abstractNumbering, element, Names.AbstractNumId);
        var index = 0;
        foreach (var child in element.Elements())
        {
            if (child.Name == Names.W + "lvl")
            {
                abstractNumbering.Levels.Add(ReadLevel(child, index));
            }
            else
            {
                PartXml.Keep(abstractNumbering, child, index);
            }

            index++;
        }

        return abstractNumbering;
    }

    private static NumberingLevel ReadLevel(XElement element, int position)
    {
        var level = new NumberingLevel { Position = position };
        level.Level = PartXml.ReadAttributes(level, element, Names.Ilvl);
        var index = 0;
        foreach (var child in element.Elements())
        {
            ReadLevelChild(level, child, index++);
        }

        return level;
    }

    private static void ReadLevelChild(NumberingLevel level, XElement child, int index)
    {
        if (child.Name.Namespace == Names.W)
        {
            var key = child.Name.LocalName;
            switch (key)
            {
                case "start" or "numFmt" or "lvlText" or "lvlJc":
                    if (PartXml.SimpleValue(child) is { } value
                        && (key != "start" || PartXml.TryCanonicalInt(value, out _))
                        && PartXml.Claim(level, key, index))
                    {
                        switch (key)
                        {
                            case "start":
                                level.Start = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                                break;
                            case "numFmt":
                                level.Format = value;
                                break;
                            case "lvlText":
                                level.Text = value;
                                break;
                            default:
                                level.Justification = value;
                                break;
                        }

                        return;
                    }

                    break;
                case "pPr" when PartXml.Claim(level, key, index):
                    PartXml.Fill(level.ParagraphProperties, child);
                    return;
                case "rPr" when PartXml.Claim(level, key, index):
                    PartXml.Fill(level.RunProperties, child);
                    return;
            }
        }

        PartXml.Keep(level, child, index);
    }

    private static NumberingInstance ReadInstance(XElement element, int position)
    {
        var instance = new NumberingInstance { Position = position };
        instance.NumId = PartXml.ReadAttributes(instance, element, Names.NumId);
        var index = 0;
        foreach (var child in element.Elements())
        {
            if (child.Name == Names.AbstractNumId
                && PartXml.SimpleValue(child) is { } value
                && PartXml.TryCanonicalInt(value, out var id)
                && PartXml.Claim(instance, "abstractNumId", index))
            {
                instance.AbstractNumId = id;
            }
            else if (child.Name == Names.W + "lvlOverride")
            {
                instance.Overrides.Add(ReadOverride(child, index));
            }
            else
            {
                PartXml.Keep(instance, child, index);
            }

            index++;
        }

        return instance;
    }

    private static LevelOverride ReadOverride(XElement element, int position)
    {
        var levelOverride = new LevelOverride { Position = position };
        levelOverride.Level = PartXml.ReadAttributes(levelOverride, element, Names.Ilvl);
        var index = 0;
        foreach (var child in element.Elements())
        {
            if (child.Name == Names.W + "startOverride"
                && PartXml.SimpleValue(child) is { } value
                && PartXml.TryCanonicalInt(value, out var start)
                && PartXml.Claim(levelOverride, "startOverride", index))
            {
                levelOverride.StartOverride = start;
            }
            else if (child.Name == Names.W + "lvl" && PartXml.Claim(levelOverride, "lvl", index))
            {
                levelOverride.Definition = ReadLevel(child, index);
            }
            else
            {
                PartXml.Keep(levelOverride, child, index);
            }

            index++;
        }

        return levelOverride;
    }

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(NumberingDefinitions model, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(model);
        PartXml.Write(stream, model.Root, writer =>
        {
            var entries = new List<PartXml.Entry>();
            foreach (var abstractNumbering in model.Abstracts)
            {
                entries.Add(new PartXml.Entry(abstractNumbering.Position, 1, w => WriteAbstract(abstractNumbering, w)));
            }

            foreach (var instance in model.Instances)
            {
                entries.Add(new PartXml.Entry(instance.Position, 2, w => WriteInstance(instance, w)));
            }

            PartXml.AddKept(entries, model, PartXml.OrderOf(s_rootOrder));
            PartXml.Flush(writer, entries);
        });
    }

    private static void WriteAbstract(AbstractNumbering abstractNumbering, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "abstractNum");
        XmlKit.UnknownAttributes(writer, abstractNumbering);
        PartXml.WriteIdAttribute(writer, Names.AbstractNumId, abstractNumbering.AbstractNumId);
        var entries = new List<PartXml.Entry>();
        foreach (var level in abstractNumbering.Levels)
        {
            entries.Add(new PartXml.Entry(level.Position, 6, w => WriteLevel(level, w)));
        }

        PartXml.AddKept(entries, abstractNumbering, PartXml.OrderOf(s_abstractOrder));
        PartXml.Flush(writer, entries);
        writer.WriteEndElement();
    }

    private static int LevelTie(string key) => Array.IndexOf(s_levelOrder, key);

    private static void WriteLevel(NumberingLevel level, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "lvl");
        XmlKit.UnknownAttributes(writer, level);
        PartXml.WriteIdAttribute(writer, Names.Ilvl, level.Level);
        var entries = new List<PartXml.Entry>();
        PartXml.AddValue(entries, level, "start", LevelTie("start"), level.Start);
        PartXml.AddValue(entries, level, "numFmt", LevelTie("numFmt"), level.Format);
        PartXml.AddValue(entries, level, "lvlText", LevelTie("lvlText"), level.Text);
        PartXml.AddValue(entries, level, "lvlJc", LevelTie("lvlJc"), level.Justification);
        PartXml.AddBag(entries, level, "pPr", LevelTie("pPr"), level.ParagraphProperties);
        PartXml.AddBag(entries, level, "rPr", LevelTie("rPr"), level.RunProperties);
        PartXml.AddKept(entries, level, PartXml.OrderOf(s_levelOrder));
        PartXml.Flush(writer, entries);
        writer.WriteEndElement();
    }

    private static void WriteInstance(NumberingInstance instance, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "num");
        XmlKit.UnknownAttributes(writer, instance);
        PartXml.WriteIdAttribute(writer, Names.NumId, instance.NumId);
        var entries = new List<PartXml.Entry>();
        PartXml.AddValue(entries, instance, "abstractNumId", 0, instance.AbstractNumId);
        foreach (var levelOverride in instance.Overrides)
        {
            entries.Add(new PartXml.Entry(levelOverride.Position, 1, w => WriteOverride(levelOverride, w)));
        }

        PartXml.AddKept(entries, instance, PartXml.OrderOf(s_instanceOrder));
        PartXml.Flush(writer, entries);
        writer.WriteEndElement();
    }

    private static void WriteOverride(LevelOverride levelOverride, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "lvlOverride");
        XmlKit.UnknownAttributes(writer, levelOverride);
        PartXml.WriteIdAttribute(writer, Names.Ilvl, levelOverride.Level);
        var entries = new List<PartXml.Entry>();
        PartXml.AddValue(entries, levelOverride, "startOverride", 0, levelOverride.StartOverride);
        if (levelOverride.Definition is { } definition)
        {
            entries.Add(new PartXml.Entry(levelOverride.PositionOf("lvl"), 1, w => WriteLevel(definition, w)));
        }

        PartXml.AddKept(entries, levelOverride, PartXml.OrderOf(s_overrideOrder));
        PartXml.Flush(writer, entries);
        writer.WriteEndElement();
    }
}
