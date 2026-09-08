using System.Collections.Immutable;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The ground tags the sweep found, together with the UI elements they came
/// from, so a later frame can ask where they are without looking for them
/// again.
/// </summary>
/// <remarks>
/// Finding a tag and locating one are two jobs on two clocks. Finding means
/// walking the UI tree — thousands of nodes, a few times a second, and confined
/// to the ground-label subtree so a stash panel cannot masquerade as loot.
/// Locating is a handful of reads per tag and has to keep up with the camera,
/// because the game redraws its own label every frame and a chip that does not
/// is the stutter.
///
/// This is the seam between them. It is also why the addresses never leave
/// Core: the renderer is handed rectangles it did not have to ask for.
/// </remarks>
internal sealed record LabelTargets(
    ImmutableArray<LootLabelSnapshot> Labels,
    ImmutableArray<nint> Elements,
    float Scale)
{
    public static readonly LabelTargets Empty = new(
        ImmutableArray<LootLabelSnapshot>.Empty,
        ImmutableArray<nint>.Empty,
        1f);
}
