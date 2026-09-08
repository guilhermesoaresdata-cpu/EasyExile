using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Rendering;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// A price chip has to move with the game's own label, not with the sweep that
/// discovered it.
/// </summary>
/// <remarks>
/// The symptom that led here: minions glided and prices hopped. A minion's
/// marker is a world position projected through the current frame's camera, so
/// it is as current as the frame. A tag's rectangle was measured by the UI
/// sweep eight times a second and drawn at a hundred and forty, so it moved
/// eight times a second no matter how fast anything else ran.
/// </remarks>
public class GroundTagTrackingTests
{
    private const nint Leaf = 0x400000;
    private const nint Middle = 0x410000;
    private const nint Root = 0x420000;

    [Fact]
    public void An_elements_rectangle_is_the_sum_of_it_and_its_parents()
    {
        using var mem = Tree();

        var (x, y, w, h) = MapUiReader.AbsoluteRect(mem, Leaf);

        Assert.Equal(111f, x, 3);
        Assert.Equal(222f, y, 3);
        Assert.Equal(160f, w, 3);
        Assert.Equal(33f, h, 3);
    }

    [Fact]
    public void Reading_the_node_whole_agrees_with_reading_it_field_by_field()
    {
        // The fast path fetches a node in one crossing instead of six. It is
        // only worth having if it is the same answer, and the slow path is kept
        // for a layout the block read cannot span — so the two must not drift.
        using var mem = Tree();

        Assert.Equal(MapUiReader.Walk(mem, Leaf), MapUiReader.AbsoluteRect(mem, Leaf));
    }

    [Fact]
    public void A_position_modifier_only_counts_when_its_flag_is_set()
    {
        using var mem = Tree();

        mem.WriteFloat(Leaf + GameLayout.Ui.PositionModifierX, 40f);
        mem.WriteFloat(Leaf + GameLayout.Ui.PositionModifierY, 50f);

        Assert.Equal(111f, MapUiReader.AbsoluteRect(mem, Leaf).X, 3);

        mem.WriteInt32(Leaf + GameLayout.Ui.Flags, 1 << GameLayout.Ui.ModifyPositionBit);

        var moved = MapUiReader.AbsoluteRect(mem, Leaf);

        Assert.Equal(151f, moved.X, 3);
        Assert.Equal(272f, moved.Y, 3);
    }

    [Fact]
    public void Moving_the_element_moves_the_rectangle_without_re_finding_it()
    {
        // This is the fix in one assertion. The sweep found the element once;
        // the frame asks the same element again and gets where the game has
        // since put it.
        using var mem = Tree();

        mem.WriteFloat(Leaf + GameLayout.Ui.RelativeX, 300f);

        Assert.Equal(410f, MapUiReader.AbsoluteRect(mem, Leaf).X, 3);
    }

    [Fact]
    public void The_frames_own_tags_are_preferred_to_the_sweeps()
    {
        var sweptCamera = Camera();
        var frameCamera = Camera();

        var frame = Frame(
            ui: new UiSnapshot(
                ImmutableArray.Create(Tag("Exalted Orb", 10f)),
                ImmutableArray<ItemSlotSnapshot>.Empty,
                sweptCamera),
            mapFrame: MapFrame(frameCamera, ImmutableArray.Create(Tag("Exalted Orb", 640f))));

        var (tags, camera) = frame.GroundTags;

        Assert.Equal(640f, Assert.Single(tags).X);

        // The camera travels with the rectangles. Correcting a fresh rectangle
        // against the sweep's older view would move it away from where the game
        // just drew it.
        Assert.Same(frameCamera, camera);
    }

    [Fact]
    public void Without_a_reading_this_frame_the_sweeps_tags_still_draw()
    {
        var sweptCamera = Camera();

        var frame = Frame(
            ui: new UiSnapshot(
                ImmutableArray.Create(Tag("Exalted Orb", 10f)),
                ImmutableArray<ItemSlotSnapshot>.Empty,
                sweptCamera),
            mapFrame: MapFrame(Camera(), default));

        var (tags, camera) = frame.GroundTags;

        Assert.Equal(10f, Assert.Single(tags).X);
        Assert.Same(sweptCamera, camera);
    }

    [Fact]
    public void A_tag_that_moved_is_refreshed_to_where_it_moved()
    {
        using var mem = Tree();

        mem.WriteFloat(Leaf + GameLayout.Ui.RelativeX, 300f);

        var refreshed = Assert.Single(SnapshotCapture.RefreshLabels(mem, Targets()));

        Assert.Equal(410f, refreshed.X, 3);
        Assert.Equal("Exalted Orb", refreshed.Text);
    }

    [Fact]
    public void An_element_that_is_no_longer_that_tag_keeps_the_sweeps_rectangle()
    {
        // Measured live: a tag that is really there never changes size — zero
        // changes in 258 readings — and one whose element had been freed read
        // as (-87140, 616085). The size is the tell, and the sweep's rectangle
        // is the honest answer until the next sweep drops the tag for good.
        using var mem = Tree();

        mem.WriteFloat(Leaf + GameLayout.Ui.SizeWidth, 9f);
        mem.WriteFloat(Leaf + GameLayout.Ui.RelativeX, -90000f);

        var kept = Assert.Single(SnapshotCapture.RefreshLabels(mem, Targets()));

        Assert.Equal(11f, kept.X, 3);
        Assert.Equal(160f, kept.Width, 3);
    }

    [Fact]
    public void An_unreadable_element_keeps_the_sweeps_rectangle_rather_than_vanishing()
    {
        // The floor this whole refresh has to clear: before it existed, every
        // tag was drawn where the sweep last saw it. A refresh that can hide a
        // tag is worse than no refresh.
        using var mem = Tree();

        var kept = Assert.Single(
            SnapshotCapture.RefreshLabels(mem, Targets() with { Elements = [0x999000] }));

        Assert.Equal(11f, kept.X, 3);
    }

    [Fact]
    public void Nothing_swept_yet_is_not_the_same_as_nothing_there()
    {
        using var mem = Tree();

        Assert.True(SnapshotCapture.RefreshLabels(mem, LabelTargets.Empty).IsDefault);
    }

    [Fact]
    public void Tags_the_frame_watched_disappear_are_not_resurrected_from_the_sweep()
    {
        // The item was picked up between the sweep and this frame. Falling back
        // to what the sweep saw would leave a price floating over bare ground.
        var frame = Frame(
            ui: new UiSnapshot(
                ImmutableArray.Create(Tag("Exalted Orb", 10f)),
                ImmutableArray<ItemSlotSnapshot>.Empty,
                Camera()),
            mapFrame: MapFrame(Camera(), ImmutableArray<LootLabelSnapshot>.Empty));

        Assert.Empty(frame.GroundTags.Tags);
    }

    [Fact]
    public void A_frame_that_never_read_the_ui_has_no_tags_rather_than_throwing()
    {
        var (tags, camera) = Frame(ui: null, mapFrame: null).GroundTags;

        Assert.Empty(tags);
        Assert.Null(camera);
    }

    // ---- fixtures --------------------------------------------------------

    /// <summary>Leaf at (1,2) inside Middle at (10,20) inside Root at (100,200).</summary>
    private static FakeMemory Tree()
    {
        var mem = new FakeMemory();

        Element(mem, Leaf, 1f, 2f, Middle);
        Element(mem, Middle, 10f, 20f, Root);
        Element(mem, Root, 100f, 200f, Root);

        mem.WriteFloat(Leaf + GameLayout.Ui.SizeWidth, 160f);
        mem.WriteFloat(Leaf + GameLayout.Ui.SizeHeight, 33f);

        return mem;
    }

    private static void Element(FakeMemory mem, nint at, float x, float y, nint parent)
    {
        // Zeroed past the last field the walk reads, so a block read of the
        // whole node succeeds the way it does against a live element.
        mem.Zero(at, GameLayout.Ui.SizeHeight + 0x40);

        mem.WriteFloat(at + GameLayout.Ui.RelativeX, x);
        mem.WriteFloat(at + GameLayout.Ui.RelativeY, y);
        mem.WritePointer(at + GameLayout.Ui.Parent, parent);
    }

    private static CameraSnapshot Camera() =>
        new(ImmutableArray.Create(new[]
        {
            0.001f, 0f, 0f, 0f,
            0f, 0.001f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0.5f, 1f,
        }), 1920, 1080);

    private static LootLabelSnapshot Tag(string text, float x) => new(text, x, 100f, 160f, 33f);

    /// <summary>The sweep's answer for the leaf: where it was, at scale 1.</summary>
    private static LabelTargets Targets() =>
        new(ImmutableArray.Create(new LootLabelSnapshot("Exalted Orb", 11f, 222f, 160f, 33f)),
            ImmutableArray.Create(Leaf),
            1f);

    private static MapFrameSnapshot MapFrame(
        CameraSnapshot camera, ImmutableArray<LootLabelSnapshot> labels) =>
        new(DateTimeOffset.UtcNow, 1, new AreaId(0x1000), default, default, MapSnapshot.Closed,
            camera, default, labels);

    private static RenderFrame Frame(UiSnapshot? ui, MapFrameSnapshot? mapFrame) =>
        new(null, CaptureStatus.Captured, TimeSpan.Zero, false,
            new ScreenRect(0, 0, 1920, 1080), false, mapFrame, ui);
}
