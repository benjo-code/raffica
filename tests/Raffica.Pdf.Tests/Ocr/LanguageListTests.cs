using Raffica.Core.Ocr;
using Raffica.Pdf.Ocr;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class LanguageListTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("raffica-langs-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Put(string folder, string language, int length = 16)
    {
        File.WriteAllBytes(Path.Combine(folder, language + ".traineddata"), new byte[length]);
    }

    private TessdataStore Store(string[] shipped, string[] imported)
    {
        var shippedFolder = Folder("shipped");
        var userFolder = Folder("user");
        foreach (var name in shipped)
        {
            Put(shippedFolder, name);
        }

        foreach (var name in imported)
        {
            Put(userFolder, name);
        }

        return new TessdataStore(userFolder, shippedFolder);
    }

    [Fact]
    public void DefaultIsUiLanguagePlusEnglishAndOnlyIfInstalled()
    {
        var store = Store(["deu", "eng"], []);

        var german = new LanguageList(store.List(), "de", stored: null);
        Assert.Equal("deu+eng", german.Default);
        Assert.Equal("deu+eng", german.Selection);

        // French has no file: it is not offered, not chosen and not in the default.
        var french = new LanguageList(store.List(), "fr", stored: null);
        Assert.DoesNotContain(french.Installed, l => l.Name == "fra");
        Assert.Equal("eng", french.Default);
        Assert.Equal(["eng"], french.Selected);
        Assert.Equal(LanguageChoice.NotInstalled, french.Select("fra", on: true));
        Assert.DoesNotContain("fra", french.Selected);

        // A stored choice wins, as far as its files are still there; the rest is dropped.
        var stored = new LanguageList(store.List(), "de", "eng+fra");
        Assert.Equal(["eng"], stored.Selected);
        Assert.Equal(["deu", "eng"], new LanguageList(store.List(), "de", "xx1").Selected);

        // Nothing installed: no default, nothing chosen.
        var none = new LanguageList([], "de", stored: null);
        Assert.Equal(string.Empty, none.Default);
        Assert.Null(none.Selection);
    }

    [Fact]
    public void MoreThanThreeWarnsAboutSpeedAndAccuracy()
    {
        var store = Store(["deu", "eng", "fra", "spa", "ita"], []);
        var list = new LanguageList(store.List(), "xx", stored: null);
        Assert.Equal(["eng"], list.Selected);
        foreach (var name in new[] { "deu", "fra" })
        {
            Assert.Equal(LanguageChoice.Done, list.Select(name, on: true));
        }

        Assert.Equal(3, list.Selected.Count);
        Assert.False(list.IsSlow);

        Assert.Equal(LanguageChoice.Done, list.Select("spa", on: true));
        Assert.True(list.IsSlow);

        // Four is a warning, not a refusal; dropping one takes the warning away.
        list.Select("spa", on: false);
        Assert.False(list.IsSlow);
    }

    [Fact]
    public void MoreThanEightIsRefused()
    {
        var names = new[] { "aaa", "bbb", "ccc", "ddd", "eee", "fff", "ggg", "hhh", "iii" };
        var store = Store(names, []);
        var list = new LanguageList(store.List(), "xx", stored: null);
        // Neither the interface language nor English is installed: the first installed language stands in.
        Assert.Equal(["aaa"], list.Selected);

        foreach (var name in names.Take(OcrLanguages.MaxLanguages))
        {
            Assert.Equal(LanguageChoice.Done, list.Select(name, on: true));
        }

        Assert.Equal(LanguageChoice.TooMany, list.Select(names[^1], on: true));
        Assert.Equal(OcrLanguages.MaxLanguages, list.Selected.Count);
        Assert.DoesNotContain(names[^1], list.Selected);

        // A refusal never changes the choice, and the one that was chosen first keeps its place.
        Assert.Equal(names.Take(OcrLanguages.MaxLanguages), list.Selected);
        Assert.Equal(LanguageChoice.Done, list.Select(names[0], on: true));
        Assert.False(string.IsNullOrWhiteSpace(PdfStrings.OcrLanguageRefusal(LanguageChoice.TooMany)));
    }

    [Fact]
    public void ImportedFileBeatsTheBundledOne()
    {
        var store = Store(["eng", "deu"], []);
        var imported = Path.Combine(_root, "source");
        Directory.CreateDirectory(imported);
        File.WriteAllBytes(Path.Combine(imported, "eng.traineddata"), new byte[64]);

        Assert.Equal(TessdataImportError.None, store.Import(Path.Combine(imported, "eng.traineddata")).Error);

        var list = new LanguageList(store.List(), "en", stored: null);
        var eng = list.Installed.Single(l => l.Name == "eng");
        Assert.Equal(TessdataOrigin.Imported, eng.Origin);
        Assert.Equal(64, eng.Length);
        Assert.Equal(TessdataOrigin.Shipped, list.Installed.Single(l => l.Name == "deu").Origin);

        // The bundled file is as it was.
        Assert.Equal(16, new FileInfo(Path.Combine(_root, "shipped", "eng.traineddata")).Length);
        Assert.Single(list.Installed, l => l.Name == "eng");
    }

    [Fact]
    public void ImportCopiesAndNeverMoves()
    {
        var store = Store([], []);
        var source = Path.Combine(Folder("source"), "fra.traineddata");
        var content = Enumerable.Range(0, 300).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(source, content);

        var result = store.Import(source);

        Assert.Equal(TessdataImportError.None, result.Error);
        Assert.True(File.Exists(source), "the original stays where it is");
        Assert.Equal(content, File.ReadAllBytes(source));
        Assert.Equal(content, File.ReadAllBytes(result.Path!));
        Assert.Equal(Path.Combine(_root, "user"), Path.GetDirectoryName(result.Path));
    }
}
