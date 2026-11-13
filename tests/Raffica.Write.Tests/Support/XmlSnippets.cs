namespace Raffica.Write.Tests.Support;

/// <summary>Small WordprocessingML documents for the reader and writer tests.</summary>
internal static class XmlSnippets
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>A complete <c>w:document</c> with the namespaces Word declares and <c>mc:Ignorable</c>, holding <paramref name="bodyXml"/> in <c>w:body</c>.</summary>
    public static string Document(string bodyXml) => $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml" xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml" xmlns:wp14="http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing" mc:Ignorable="w14 w15 wp14"><w:body>{{bodyXml}}</w:body></w:document>
        """;
}
