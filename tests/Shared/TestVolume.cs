namespace Raffica.TestSupport;

/// <summary>Probes how the volume of a test folder treats file names (a Mac volume is usually case-insensitive and normalizing, a Linux one is not).</summary>
internal static class TestVolume
{
    /// <summary>True when <c>probe.tmp</c> is also found as <c>PROBE.TMP</c> in <paramref name="folder"/>.</summary>
    public static bool IgnoresCase(string folder) => Probe(folder, "raffica-case-probe.tmp", "RAFFICA-CASE-PROBE.TMP");

    /// <summary>True when a name written in NFC is also found in its NFD spelling in <paramref name="folder"/>.</summary>
    public static bool TreatsNfcAndNfdAsOneName(string folder)
    {
        var nfc = "raffica-Ü-probe.tmp".Normalize(System.Text.NormalizationForm.FormC);
        return Probe(folder, nfc, nfc.Normalize(System.Text.NormalizationForm.FormD));
    }

    private static bool Probe(string folder, string name, string otherSpelling)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, []);
        try
        {
            return File.Exists(Path.Combine(folder, otherSpelling));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
