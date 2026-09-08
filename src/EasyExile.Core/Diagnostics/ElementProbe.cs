using System.Collections.Immutable;

namespace EasyExile.Core.Diagnostics;

/// <summary>
/// One UI element, as picked off the screen.
/// </summary>
/// <remarks>
/// Everything a person needs to recognise what they are pointing at, and
/// everything the next question about it will start from: where it is, what it
/// says in both of the places a caption can live, what it carries, and the trail
/// of parents that got there.
/// </remarks>
public sealed record ElementProbe(
    nint Address,
    float X, float Y, float Width, float Height,
    uint Flags,
    bool Visible,
    int Children,
    nint Carried,
    string? LineText,
    string? WideText,
    ImmutableArray<string> Ancestry)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    /// <summary>The lines a picker draws beside the outline.</summary>
    public ImmutableArray<string> Describe()
    {
        var lines = ImmutableArray.CreateBuilder<string>();

        lines.Add($"0x{Address:X}");
        lines.Add($"({X:0},{Y:0})  {Width:0} x {Height:0}");
        lines.Add($"flags 0x{Flags:X8}  {(Visible ? "visivel" : "oculto")}  filhos {Children}");

        if (Carried != 0) lines.Add($"entidade 0x{Carried:X}");

        // Both, labelled. Which of the two a caption lives in is exactly the
        // thing that is invisible from outside and cost a whole round of this.
        if (LineText is { Length: > 0 }) lines.Add($"texto  +0x538  {Cut(LineText)}");
        if (WideText is { Length: > 0 }) lines.Add($"wstr   +0x360  {Cut(WideText)}");

        if (LineText is not { Length: > 0 } && WideText is not { Length: > 0 })
            lines.Add("sem texto nos dois campos");

        foreach (var step in Ancestry) lines.Add(step);

        return lines.ToImmutable();
    }

    private static string Cut(string text)
    {
        var flat = text.Replace((char)10, (char)124).Replace((char)13, (char)32);

        return flat.Length <= 60 ? flat : flat[..60];
    }
}
