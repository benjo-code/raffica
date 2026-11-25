using System.Buffers.Binary;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using SkiaSharp;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>Every picture type the insert command accepts must decode in the layout: it gets a picture box in the object map (so it can be selected), never a grey placeholder.</summary>
public sealed class InsertedPictureFormatTests
{
    /// <summary>The accepted kinds.</summary>
    public static TheoryData<string> Kinds() => ["png", "jpeg", "gif", "bmp"];

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(8, 6);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    // A 2 x 2 GIF, one frame of four pixels of the first palette entry (hand-made LZW: clear, 0, 6, 0, end). It is no smaller than 2 pixels because a picture under one point is a placeholder.
    private static byte[] Gif() =>
    [
        .. "GIF89a"u8,
        0x02, 0x00, 0x02, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00,
        0x02, 0x03, 0x84, 0xA1, 0x00, 0x00, 0x3B,
    ];

    // A 24-bit 2 x 2 bitmap: two rows of six pixel bytes, each padded to eight.
    private static byte[] Bmp()
    {
        var bytes = new byte[54 + 16];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34), 16);
        Array.Fill(bytes, (byte)0x80, 54, 16);
        return bytes;
    }

    private static byte[] Bytes(string kind) => kind switch
    {
        "png" => TestPictures.Png(8, 6),
        "jpeg" => Jpeg(),
        "gif" => Gif(),
        _ => Bmp(),
    };

    private static string Local(string id) => id[(id.IndexOf('#', StringComparison.Ordinal) + 1)..];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryAcceptedPictureTypeGetsAPictureBoxAfterTheInsert(string kind)
    {
        var document = Document("<w:p><w:r><w:t>alpha</w:t></w:r></w:p>" + Sect());
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var at = new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 2);

        Assert.NotNull(editor.InsertPicture(at, Bytes(kind), null));

        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions(ImageBytes: id => document.Media.TryGet(Local(id)) ?? ReadOnlyMemory<byte>.Empty)));
        var box = Assert.Single(layout.Page(0).Objects.Pictures);
        Assert.NotNull(box.Id);
    }
}
