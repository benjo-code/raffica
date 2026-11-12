using System.Runtime.Versioning;
using System.Text;
using PrintSpike.Interop;

namespace PrintSpike;

/// <summary>Linux and macOS: hand a PDF in memory to CUPS as a job (cupsCreateJob, cupsStartDocument, cupsWriteRequestData, cupsFinishDocument).</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public static unsafe class CupsJob
{
    const int HttpContinue = 100;

    public static IReadOnlyList<string> Printers()
    {
        if (!CupsNative.IsAvailable) return Array.Empty<string>();
        CupsDest* dests = null;
        int n = CupsNative.cupsGetDests2(0, &dests);
        var list = new List<string>();
        for (int i = 0; i < n; i++) list.Add(Utf8(dests[i].Name));
        CupsNative.cupsFreeDests(n, dests);
        return list;
    }

    /// <summary>Returns the job id. Streams the PDF in 64 KiB chunks; throws with CUPS's own message when a step fails.</summary>
    public static int Print(string printer, Stream pdf, string title)
    {
        if (!CupsNative.IsAvailable) throw new InvalidOperationException("libcups is not available");
        int job = CupsNative.cupsCreateJob(0, printer, title, 0, null);
        if (job == 0) throw new InvalidOperationException("cupsCreateJob failed: " + LastError());
        int status = CupsNative.cupsStartDocument(0, printer, job, title + ".pdf", "application/pdf", 1);
        if (status != HttpContinue) throw new InvalidOperationException($"cupsStartDocument failed ({status}): " + LastError());
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = pdf.Read(buffer, 0, buffer.Length)) > 0)
            fixed (byte* p = buffer)
                if (CupsNative.cupsWriteRequestData(0, p, (nuint)read) != HttpContinue)
                    throw new InvalidOperationException("cupsWriteRequestData failed: " + LastError());
        int finish = CupsNative.cupsFinishDocument(0, printer);
        if (finish >= 0x0300) throw new InvalidOperationException($"cupsFinishDocument failed ({finish}): " + LastError());
        return job;
    }

    static string LastError() => Utf8(CupsNative.cupsLastErrorString());

    static string Utf8(byte* p)
    {
        if (p == null) return "";
        int n = 0;
        while (p[n] != 0) n++;
        return Encoding.UTF8.GetString(p, n);
    }
}
