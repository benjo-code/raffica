namespace Raffica.Write.Panes;

/// <summary>A pane of one tab that can fill the right slot of the window (the formatting pane and the Sections pane share it, one at a time).</summary>
internal interface IRightSlotPane
{
    /// <summary>The pane left the screen: it lets go of the session and the layout and drops a waiting refresh.</summary>
    void Hidden();

    /// <summary>The pane is on screen again: it follows the session again and shows the state now.</summary>
    void Shown();

    /// <summary>Puts the keyboard focus on the first control of the pane that can take it.</summary>
    void FocusContent();

    /// <summary>Asks for a refresh once typing and selecting pause.</summary>
    void RequestRefresh();

    /// <summary>Reads the rules of the buttons again (cheap); the state shown waits for <see cref="RequestRefresh"/>.</summary>
    void RefreshButtons();
}
