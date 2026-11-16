namespace Raffica.Write.Model;

/// <summary>Answers what formatting a run (or a paragraph mark) has after every style layer, so an editor can toggle it correctly. Model only: the layout supplies the implementation.</summary>
public interface IEffectiveFormat
{
    /// <summary>The effective formatting of a run of <paramref name="paragraph"/> in <paramref name="story"/>.</summary>
    EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run);

    /// <summary>The effective formatting of the paragraph mark of <paramref name="paragraph"/>.</summary>
    EffectiveRun ForMark(StoryRef story, Paragraph paragraph);
}
