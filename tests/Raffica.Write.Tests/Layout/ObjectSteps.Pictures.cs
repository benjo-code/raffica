using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The picture steps of the object oracle (milestone 4.5a): a small picture inserted at a random place, then resized, wrapped, moved, nudged and deleted, over the pictures of the document (the corpus has
/// cropped, anchored, rotated and tabled ones), each compared with a full layout (the object map included), plus the independent facts that an insert adds one drawing and a delete removes one and that
/// none of the others changes how many there are.
/// </summary>
internal sealed partial class ObjectSteps
{
    private static readonly byte[] s_picture = TestPictures.Png(16, 12);

    private List<(Paragraph Paragraph, Run Run, Picture Picture)> Pictures() =>
    [
        .. ModelWalk.Blocks(_model.Body.Blocks).OfType<Paragraph>()
            .SelectMany(p => ModelWalk.Inlines(p.Inlines).OfType<Run>().SelectMany(r => r.Content.OfType<Picture>().Where(c => PictureView.IsPlain(c)).Select(c => (Paragraph: p, Run: r, Picture: c)))),
    ];

    private int PictureCount() => ModelWalk.RunContents(_model.Body).OfType<Picture>().Count();

    private (ObjectId Id, PictureInfo Info)? PickPicture(Random random, Func<PictureInfo, bool> where)
    {
        var candidates = new List<(ObjectId, PictureInfo)>();
        foreach (var (paragraph, run, picture) in Pictures())
        {
            if (ObjectIds.Of(StoryRef.Body, _model.Body.Blocks, paragraph, run, picture) is { } id && PictureView.Of(picture) is { } info && where(info))
            {
                candidates.Add((id, info));
            }
        }

        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    private int _picturesBefore;

    private async Task<bool> CheckPictureAsync(EditResult? result, string label, int expectedChange)
    {
        var compared = await CheckAsync(result, label, required: false);
        if (compared)
        {
            Xunit.Assert.True(PictureCount() - _picturesBefore == expectedChange, $"{label}: the model holds {PictureCount()} drawings, {_picturesBefore} before the step, expected a change of {expectedChange}");
        }

        return compared;
    }

    private async Task<bool> InsertPictureAsync(Random random, string label)
    {
        var candidates = Paragraphs().Where(p => Editor.RefusalForPicture(new TextPosition(StoryRef.Body, p, 0)) is null).ToList();
        if (candidates.Count == 0)
        {
            return Refuse();
        }

        var paragraph = candidates[random.Next(candidates.Count)];
        var offset = random.Next(TextLength(paragraph) + 1);
        _picturesBefore = PictureCount();
        var result = Editor.InsertPicture(new TextPosition(StoryRef.Body, paragraph, offset), s_picture, "step " + label.Length);
        if (result is not null)
        {
            // The caret is right after the picture and the picture is found there.
            Xunit.Assert.True(Editor.PictureAt(new TextPosition(StoryRef.Body, result.Caret.Paragraph, result.Caret.Offset - 1)) is not null, $"{label}: no picture before the caret of the insert");
        }

        return await CheckPictureAsync(result, label, 1);
    }

    private async Task<bool> ResizePictureAsync(Random random, string label)
    {
        if (PickPicture(random, i => !i.IsRotated) is not var (id, info))
        {
            return Refuse();
        }

        var width = Math.Max(1, info.ExtentEmu.Width * random.Next(40, 200) / 100);
        var height = Math.Max(1, info.ExtentEmu.Height * random.Next(40, 200) / 100);
        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.ResizePicture(id, width, height), label, 0);
    }

    private static readonly PictureWrap[] s_wraps = [PictureWrap.Inline, PictureWrap.Square, PictureWrap.TopAndBottom, PictureWrap.InFront, PictureWrap.Behind];

    private async Task<bool> WrapPictureAsync(Random random, string label)
    {
        if (PickPicture(random, _ => true) is not var (id, info))
        {
            return Refuse();
        }

        // Always another wrap than the one it has.
        var others = s_wraps.Where(w => w != info.Wrap).ToArray();
        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.SetPictureWrap(id, others[random.Next(others.Length)]), label, 0);
    }

    private async Task<bool> MovePictureAsync(Random random, string label)
    {
        if (PickPicture(random, i => i.IsFloating) is not var (id, _))
        {
            return Refuse();
        }

        var paragraphs = Paragraphs();
        var target = paragraphs[random.Next(paragraphs.Count)];
        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.MovePicture(id, new TextPosition(StoryRef.Body, target, 0), random.Next(-200, 200) * 12700L, random.Next(-100, 300) * 12700L), label, 0);
    }

    private async Task<bool> NudgePictureAsync(Random random, string label)
    {
        if (PickPicture(random, i => i.IsFloating) is not var (id, _))
        {
            return Refuse();
        }

        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.NudgePicture(id, random.Next(-20, 21), random.Next(-20, 21)), label, 0);
    }

    // The compound of the properties card: size, description and wrap in one step (a floating wrap is left out for a picture the editor would refuse it for, so the step still changes something).
    private async Task<bool> PicturePropertiesStepAsync(Random random, string label)
    {
        if (PickPicture(random, i => !i.IsRotated) is not var (id, info))
        {
            return Refuse();
        }

        var others = s_wraps.Where(w => w != info.Wrap).ToArray();
        var properties = new PictureProperties(
            Math.Max(1, info.ExtentEmu.Width * random.Next(40, 200) / 100),
            Math.Max(1, info.ExtentEmu.Height * random.Next(40, 200) / 100),
            ResetSize: false,
            AltText: "step " + random.Next(1000),
            Wrap: random.Next(4) == 0 ? null : others[random.Next(others.Length)]);
        if (Editor.RefusalForSetPictureProperties(id, properties) is not null)
        {
            properties = properties with { Wrap = null };
        }

        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.SetPictureProperties(id, properties), label, 0);
    }

    // A picture pasted over a selection in one paragraph (a third of the time a caret): the selected text goes, one drawing comes.
    private async Task<bool> PastePictureStepAsync(Random random, string label)
    {
        var candidates = Paragraphs().Where(p => Editor.RefusalForPicture(new TextPosition(StoryRef.Body, p, 0)) is null && TextLength(p) > 1).ToList();
        if (candidates.Count == 0)
        {
            return Refuse();
        }

        var paragraph = candidates[random.Next(candidates.Count)];
        var length = TextLength(paragraph);
        var start = random.Next(length);
        var end = random.Next(3) == 0 ? start : start + random.Next(1, length - start + 1);
        var range = new TextRange(new TextPosition(StoryRef.Body, paragraph, start), new TextPosition(StoryRef.Body, paragraph, end));
        if (Editor.RefusalForPastePicture(range, s_picture) is not null)
        {
            return Refuse();
        }

        _picturesBefore = PictureCount();
        var gone = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Pieces.Count(p => p.Content is Picture && p.Start >= Math.Min(start, end) && p.Start < Math.Max(start, end));
        var result = Editor.PastePicture(range, s_picture);
        return gone == 0 ? await CheckPictureAsync(result, label, 1) : await CheckAsync(result, label, required: false);
    }

    private async Task<bool> DeletePictureAsync(Random random, string label)
    {
        if (PickPicture(random, _ => true) is not var (id, _))
        {
            return Refuse();
        }

        _picturesBefore = PictureCount();
        return await CheckPictureAsync(Editor.DeletePicture(id), label, -1);
    }
}
