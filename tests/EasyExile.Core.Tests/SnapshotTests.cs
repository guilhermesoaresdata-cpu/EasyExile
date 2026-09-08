using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Core.Tests;

/// <summary>
/// The capture layer: turning a live client into state a renderer can hold.
/// The interesting cases are the ones where the client changes underneath the
/// reading, because a snapshot that mixes two areas is indistinguishable from a
/// correct one by inspection.
/// </summary>
public class SnapshotTests
{
    private static CaptureResult Capture(FakeMemory mem, AreaEpoch? epoch = null, CaptureOptions? options = null) =>
        SnapshotCapture.Capture(mem, WorldFixture.ModuleBase, epoch ?? new AreaEpoch(),
            options ?? CaptureOptions.Default);

    // ---- the happy path -----------------------------------------------------

    [Fact]
    public void A_capture_describes_the_player()
    {
        using var mem = WorldFixture.Build();

        var result = Capture(mem);

        Assert.True(result.Success);

        var player = result.Snapshot!.Player;

        Assert.Equal(WorldFixture.PlayerName, player.Name);
        Assert.Equal(WorldFixture.PlayerLevel, player.Level);
        Assert.Equal(WorldFixture.Health, player.Health);
        Assert.Equal(WorldFixture.Health, player.MaxHealth);
        Assert.Equal(WorldFixture.Mana, player.Mana);
        Assert.Equal(WorldFixture.EnergyShield, player.EnergyShield);
        Assert.Equal(WorldFixture.PlayerPosition, player.WorldPosition);
    }

    [Fact]
    public void A_capture_describes_the_entities()
    {
        using var mem = WorldFixture.Build(monsters: 3);

        var snapshot = Capture(mem).Snapshot!;

        var monsters = snapshot.Entities.Where(e => e.Category == "Monsters").ToArray();

        Assert.Equal(3, monsters.Length);
        Assert.All(monsters, m => Assert.StartsWith("Metadata/Monsters/", m.Metadata, StringComparison.Ordinal));
        Assert.All(monsters, m => Assert.Contains(GameLayout.Names.Render, m.ComponentNames));
        Assert.All(monsters, m => Assert.NotNull(m.WorldPosition));
    }

    [Fact]
    public void Entities_carry_an_identity_that_can_key_a_cache()
    {
        using var mem = WorldFixture.Build(monsters: 3);

        var first = Capture(mem).Snapshot!;
        var second = Capture(mem).Snapshot!;

        var before = first.Entities.Select(e => e.Id).ToHashSet();
        var after = second.Entities.Select(e => e.Id).ToHashSet();

        // Same entities, two readings: a renderer keeping a marker per id must
        // find the same ids again or every marker would blink each update.
        Assert.NotEmpty(before);
        Assert.Equal(before, after);
        Assert.All(before, id => Assert.True(id.IsValid));
    }

    [Fact]
    public void The_entity_ceiling_is_respected()
    {
        using var mem = WorldFixture.Build(monsters: 5);

        var snapshot = Capture(mem, options: new CaptureOptions(MaxEntities: 2)).Snapshot!;

        Assert.Equal(2, snapshot.Entities.Length);
    }

    [Fact]
    public void A_client_outside_an_area_yields_no_area_rather_than_an_empty_snapshot()
    {
        using var mem = WorldFixture.Build();
        mem.WritePointer(WorldFixture.InGameState + GameLayout.Roots.AreaInstance, 0);

        var result = Capture(mem);

        Assert.Equal(CaptureStatus.NoArea, result.Status);
        Assert.Null(result.Snapshot);
        Assert.False(result.Success);
    }

    // ---- the transition race ------------------------------------------------

    [Fact]
    public void A_capture_spanning_an_area_change_is_discarded_rather_than_returned()
    {
        using var mem = WorldFixture.Build();
        WorldFixture.EnterSecondArea(mem);
        mem.WritePointer(WorldFixture.InGameState + GameLayout.Roots.AreaInstance, WorldFixture.FirstArea);

        // The area flips every time the capture LOOKS at it, so the chain it
        // resolves before the walk and the one it re-resolves after can never
        // agree. Keyed on that one address rather than on every read, so the
        // test says what it means and does not quietly depend on how many reads
        // a capture happens to make.
        var toggle = false;
        var areaSlot = WorldFixture.InGameState + GameLayout.Roots.AreaInstance;

        mem.BeforeRead = address =>
        {
            if (address != areaSlot) return;

            toggle = !toggle;
            mem.WritePointer(areaSlot, toggle ? WorldFixture.FirstArea : WorldFixture.SecondArea);
        };

        var result = Capture(mem);

        Assert.Equal(CaptureStatus.InvalidatedByTransition, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void A_single_transition_is_absorbed_by_the_retry()
    {
        using var mem = WorldFixture.Build();
        WorldFixture.EnterSecondArea(mem);
        mem.WritePointer(WorldFixture.InGameState + GameLayout.Roots.AreaInstance, WorldFixture.FirstArea);

        // One change, at the very first read, then the client settles. A radar
        // must not report a failure for something the player experiences as a
        // loading screen.
        mem.BeforeRead = _ =>
        {
            mem.WritePointer(WorldFixture.InGameState + GameLayout.Roots.AreaInstance, WorldFixture.SecondArea);
            mem.BeforeRead = null;
        };

        var result = Capture(mem);

        Assert.Equal(CaptureStatus.Captured, result.Status);
        Assert.Equal(new AreaId(WorldFixture.SecondArea), result.Snapshot!.Area);
    }

    // ---- epoch --------------------------------------------------------------

    [Fact]
    public void Staying_in_one_area_keeps_the_epoch_still()
    {
        using var mem = WorldFixture.Build();
        var epoch = new AreaEpoch();

        var first = Capture(mem, epoch).Snapshot!;
        var second = Capture(mem, epoch).Snapshot!;

        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Equal(first.Area, second.Area);
    }

    [Fact]
    public void Changing_area_moves_the_epoch_on()
    {
        using var mem = WorldFixture.Build();
        var epoch = new AreaEpoch();

        var before = Capture(mem, epoch).Snapshot!;

        WorldFixture.EnterSecondArea(mem);

        var after = Capture(mem, epoch).Snapshot!;

        // The epoch is the signal that everything keyed on an EntityId from the
        // previous snapshot is now meaningless: on a real transition every
        // entity is reallocated, the local player included.
        Assert.True(after.Epoch > before.Epoch);
        Assert.NotEqual(before.Area, after.Area);
    }

    [Fact]
    public void Describing_an_entity_does_not_re_read_what_its_type_already_said()
    {
        // The regression this guards is a performance one with a visual symptom:
        // reading each entity's metadata path and component table per capture
        // took the world lane to 5 Hz, and at 5 Hz the markers step instead of
        // moving. Both facts belong to the entity's TYPE, so a second capture of
        // the same area must be markedly cheaper in reads than the first.
        using var mem = WorldFixture.Build(monsters: 12);

        // The same caches across both passes, which is what a session holds. A
        // throwaway per capture would remember nothing, and the point here is
        // what gets remembered.
        var caches = new CaptureCaches();
        var epoch = new AreaEpoch();

        mem.Reads = 0;
        SnapshotCapture.Capture(mem, WorldFixture.ModuleBase, epoch, CaptureOptions.Default, null, caches);
        var cold = mem.Reads;

        mem.Reads = 0;
        SnapshotCapture.Capture(mem, WorldFixture.ModuleBase, epoch, CaptureOptions.Default, null, caches);
        var warm = mem.Reads;

        Assert.True(warm < cold, $"the second capture read {warm}, the first {cold} - nothing was remembered");
    }

    [Fact]
    public void An_area_change_forgets_what_the_previous_area_said()
    {
        // The cache keys on addresses, and an address in the next area describes
        // something else. Carrying it over would report the old area's metadata
        // for the new area's entities.
        using var mem = WorldFixture.Build(monsters: 3);

        Capture(mem);

        WorldFixture.EnterSecondArea(mem, monsters: 4);

        var after = Capture(mem).Snapshot!;

        Assert.Equal(4, after.Entities.Count(e => e.Category == "Monsters"));
        Assert.All(after.Entities, e => Assert.StartsWith("Metadata/", e.Metadata, StringComparison.Ordinal));
    }

    // ---- the grid stays derived ---------------------------------------------

    [Fact]
    public void The_grid_position_is_derived_from_the_world_position()
    {
        using var mem = WorldFixture.Build();

        var player = Capture(mem).Snapshot!.Player;

        var world = player.WorldPosition!.Value;
        var grid = player.GridPosition!.Value;
        var scale = GameLayout.Spatial.WorldToGrid;

        Assert.Equal(world.X / scale, grid.X, 3);
        Assert.Equal(world.Y / scale, grid.Y, 3);
    }

    [Fact]
    public void Without_a_world_position_there_is_no_grid_position()
    {
        Assert.Null(Projection.ToGrid(null, GameLayout.Spatial.WorldToGrid));
    }

    [Fact]
    public void The_tile_ratio_comes_from_the_contract_and_is_plausible()
    {
        // 250/23 for this build. If a future export dropped the pair, this would
        // become zero and every grid coordinate would silently become infinite.
        Assert.True(GameLayout.Spatial.WorldToGrid > 1f);
        Assert.True(GameLayout.Spatial.WorldToGrid < 100f);
    }

    // ---- the camera ---------------------------------------------------------

    [Fact]
    public void The_camera_is_captured_as_data()
    {
        using var mem = WorldFixture.Build();

        var camera = Capture(mem).Snapshot!.Camera;

        Assert.NotNull(camera);
        Assert.Equal(1920, camera!.Width);
        Assert.Equal(1080, camera.Height);
        Assert.Equal(16, camera.ViewProjection.Length);
    }

    [Fact]
    public void Projection_from_a_captured_camera_is_pure()
    {
        var camera = new CameraSnapshot(WorldFixture.Orthographic().ToImmutableArray(), 1920, 1080);
        var world = new Vector3(0, 0, 0);

        var first = camera.Project(world);
        var second = camera.Project(world);

        // No reader, no session, no client: the same call a thousand times in a
        // Draw() costs nothing but arithmetic and always agrees with itself.
        Assert.Equal(first, second);
        Assert.Equal(ScreenStatus.OnScreen, first.Status);
        Assert.Equal(960f, first.Screen.X, 2);
        Assert.Equal(540f, first.Screen.Y, 2);
    }

    [Fact]
    public void A_captured_camera_reports_a_point_behind_it_rather_than_folding_it_onto_the_screen()
    {
        var behind = new[]
        {
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, -1f,
        };

        var camera = new CameraSnapshot(behind.ToImmutableArray(), 1920, 1080);

        Assert.Equal(ScreenStatus.BehindCamera, camera.Project(new Vector3(10, 10, 10)).Status);
    }

    [Fact]
    public void A_client_whose_camera_cannot_be_resolved_still_produces_a_snapshot()
    {
        using var mem = WorldFixture.Build();
        mem.WritePointer(WorldFixture.InGameState + GameLayout.Roots.Camera, 0);

        var snapshot = Capture(mem).Snapshot!;

        // Losing the camera costs projection and nothing else. A radar can still
        // show a minimap; blanking the whole snapshot would be worse.
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Camera);
        Assert.False(snapshot.CanProject);
        Assert.NotEmpty(snapshot.Entities);
    }
}
