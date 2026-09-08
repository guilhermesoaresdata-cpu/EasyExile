namespace EasyExile.Core.Diagnostics;

/// <summary>
/// What part of the client to write down, and in how much detail.
/// </summary>
/// <remarks>
/// The first version of the dump had one setting — everything — and produced
/// fifty-four thousand lines. It answered every question and found none of them,
/// which is a fair description of a tool that has no aim.
///
/// Aim is what these are for. A whole tree is right when the question is "does
/// this exist anywhere"; the subtree under the cursor is right when the question
/// is "what am I pointing at", which is nearly always; and the byte view is
/// right when the question is "which offset holds the number I can see on the
/// screen", which is how every field this project reads was found in the first
/// place.
/// </remarks>
public sealed record DumpOptions(
    DumpScope Scope = DumpScope.Visible,
    float CursorX = 0f,
    float CursorY = 0f,
    float UiScale = 1f,
    int HexBytes = 0,
    int HexNodes = 0,
    int MaxNodes = 120000,
    string? Needle = null)
{
    /// <summary>Everything drawn, which is what a first look wants.</summary>
    public static readonly DumpOptions Drawn = new();

    /// <summary>Everything at all, hidden branches included.</summary>
    public static readonly DumpOptions Everything = new(DumpScope.All);

    /// <summary>
    /// Everything related to what was selected, and nothing else.
    /// </summary>
    /// <remarks>
    /// Related means: the element itself in full detail, the ancestors it hangs
    /// from, its whole subtree, and the siblings at each of the nearest levels -
    /// because a rule that has to tell one row from another needs to see the
    /// other rows.
    ///
    /// Everything else is left out deliberately. Adding the rest of the screen
    /// "in case" was my instinct and it is the wrong one: the failure mode all
    /// evening was not missing data, it was too much of it, and a reader that
    /// has to sift is a reader that guesses.
    /// </remarks>
    public static DumpOptions Complete(float x, float y, float uiScale) =>
        new(DumpScope.UnderCursor, x, y, uiScale, HexBytes: 0x400, HexNodes: 24);

    /// <summary>
    /// What the cursor is on, its ancestors, and the bytes behind them.
    /// </summary>
    /// <remarks>
    /// The one that answers questions. Small enough to read end to end, deep
    /// enough to show the siblings a rule has to tell apart, and with the raw
    /// fields so a value seen on screen can be traced to the offset holding it.
    /// </remarks>
    public static DumpOptions Around(float x, float y, float uiScale) =>
        new(DumpScope.UnderCursor, x, y, uiScale, HexBytes: 0x400, HexNodes: 24);

    /// <summary>
    /// Everywhere a value can be seen, shallowest path first.
    /// </summary>
    /// <remarks>
    /// The question that actually gets asked: something is visible on screen and
    /// nobody knows which field holds it. Borrowed from DevTreeMcp, whose
    /// search_live does the same for a reflected object graph - and it is the
    /// technique that found this project's text offsets by hand, made routine.
    /// </remarks>
    public static DumpOptions Find(string needle) =>
        new(DumpScope.Search, Needle: needle, HexBytes: 0x400, HexNodes: 64);
}

public enum DumpScope
{
    /// <summary>Only what the client is drawing.</summary>
    Visible,

    /// <summary>Every element, drawn or not.</summary>
    All,

    /// <summary>The smallest element under the cursor, its ancestors and its subtree.</summary>
    UnderCursor,

    /// <summary>Every element whose text or bytes hold a given value.</summary>
    Search,
}
