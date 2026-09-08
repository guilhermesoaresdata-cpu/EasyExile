using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Tests;

/// <summary>
/// The consumer foundation. Everything here goes through GameLayout, so if an
/// offset were ever written as a literal outside the contract these tests would
/// keep passing while the real client broke — which is why the layout wrapper
/// is used even in the fixtures.
/// </summary>
public class ClientFoundationTests
{
    private const nint EntityAddress = 0x100000;
    private const nint DetailsAddress = 0x200000;
    private const nint MetadataBuffer = 0x210000;
    private const nint LookupAddress = 0x300000;
    private const nint BucketAddress = 0x310000;
    private const nint NameArea = 0x320000;
    private const nint ComponentArray = 0x400000;
    private const nint ComponentBase = 0x500000;

    private static readonly string[] Names = { "Life", "Render", "Player", "Positioned" };

    /// <summary>An entity with a metadata path and four named components.</summary>
    private static FakeMemory BuildEntity(
        string metadata = "Metadata/Characters/Int/IntFourb",
        int componentCount = 4,
        bool ownerBackReference = true)
    {
        var mem = new FakeMemory();

        mem.Zero(EntityAddress, 0x40);
        mem.WritePointer(EntityAddress + GameLayout.Components.EntityDetails, DetailsAddress);

        mem.Zero(DetailsAddress, 0x40);
        mem.WriteWideString(DetailsAddress + GameLayout.Components.Metadata, MetadataBuffer, metadata);
        mem.WritePointer(DetailsAddress + GameLayout.Components.ComponentLookup, LookupAddress);

        mem.Zero(ComponentArray, (componentCount * 8) + 0x20);
        mem.WriteVector(EntityAddress + GameLayout.Components.ComponentList, ComponentArray, componentCount, 8);

        for (int i = 0; i < componentCount; i++)
        {
            var component = ComponentBase + (i * 0x1000);
            mem.Zero(component, 0x400);
            mem.WritePointer(ComponentArray + (i * 8), component);

            if (ownerBackReference)
                mem.WritePointer(component + GameLayout.Components.Owner, EntityAddress);
        }

        mem.Zero(LookupAddress, 0x40);
        mem.Zero(BucketAddress, (componentCount * GameLayout.Components.EntryStride) + 0x20);
        mem.WriteVector(LookupAddress + GameLayout.Components.NameBucket,
            BucketAddress, componentCount, GameLayout.Components.EntryStride);

        for (int i = 0; i < componentCount && i < Names.Length; i++)
        {
            var entry = BucketAddress + (i * GameLayout.Components.EntryStride);
            var namePtr = NameArea + (i * 0x40);

            mem.Zero(namePtr, 0x40);
            mem.WriteAscii(namePtr, Names[i]);

            mem.WritePointer(entry + GameLayout.Components.EntryName, namePtr);
            mem.WriteInt32(entry + GameLayout.Components.EntryIndex, i);
        }

        return mem;
    }

    private static nint ComponentAt(int index) => ComponentBase + (index * 0x1000);

    // ---- contract identity --------------------------------------------------

    [Fact]
    public void The_contract_is_resolved_and_carries_its_schema()
    {
        Assert.NotEqual("UNRESOLVED", GameLayout.Build.Fingerprint);
        Assert.Equal(24, GameLayout.Build.Fingerprint.Length);
        Assert.Equal(1, GameLayout.Build.SchemaVersion);
        Assert.NotEmpty(GameLayout.Build.ExportHash);
    }

    [Fact]
    public void The_chain_entry_point_comes_from_the_contract()
    {
        // Without this the consumer would have to hard-code the one number the
        // contract exists to supply.
        Assert.True(GameLayout.Roots.GlobalSlotRva > 0);
        Assert.True(GameLayout.Roots.GlobalSlotRva < 0x10000000);
    }

    // ---- build gate -----------------------------------------------------------

    [Fact]
    public void A_client_from_another_build_is_refused()
    {
        var contract = GameLayout.Build.Fingerprint;
        var other = BuildGate.StableId("PathOfExile.exe", 0xDEADBEEF, 0x01000000, new string('a', 64));

        Assert.NotEqual(contract, other);
    }

    [Fact]
    public void The_fingerprint_formula_is_stable()
    {
        var first = BuildGate.StableId("PathOfExile.exe", 0x6A8292EE, 0x04C23000, new string('b', 64));
        var second = BuildGate.StableId("PathOfExile.exe", 0x6A8292EE, 0x04C23000, new string('b', 64));

        Assert.Equal(first, second);
        Assert.Equal(24, first.Length);
    }

    [Fact]
    public void Any_component_of_the_identity_changes_the_fingerprint()
    {
        var sha = new string('c', 64);
        var baseline = BuildGate.StableId("PathOfExile.exe", 1, 2, sha);

        Assert.NotEqual(baseline, BuildGate.StableId("Other.exe", 1, 2, sha));
        Assert.NotEqual(baseline, BuildGate.StableId("PathOfExile.exe", 9, 2, sha));
        Assert.NotEqual(baseline, BuildGate.StableId("PathOfExile.exe", 1, 9, sha));
        Assert.NotEqual(baseline, BuildGate.StableId("PathOfExile.exe", 1, 2, new string('d', 64)));
    }

    // ---- entity and component resolver -------------------------------------------

    [Fact]
    public void An_entity_exposes_its_metadata()
    {
        using var mem = BuildEntity();

        Assert.Equal("Metadata/Characters/Int/IntFourb", new GameEntity(mem, EntityAddress).Metadata);
    }

    [Fact]
    public void Something_without_a_metadata_path_is_not_an_entity()
    {
        using var mem = BuildEntity(metadata: "not a metadata path");

        Assert.False(new GameEntity(mem, EntityAddress).IsValid);
    }

    [Fact]
    public void Unmapped_memory_is_not_an_entity()
    {
        using var mem = BuildEntity();

        Assert.False(new GameEntity(mem, unchecked((nint)0xDEAD0000)).IsValid);
    }

    [Fact]
    public void An_unaligned_address_is_not_an_entity()
    {
        Assert.False(GameEntity.IsPlausibleAddress(EntityAddress + 3));
        Assert.False(GameEntity.IsPlausibleAddress(0));
    }

    [Fact]
    public void Components_resolve_by_name()
    {
        using var mem = BuildEntity();

        var components = new GameEntity(mem, EntityAddress).Components();

        Assert.Equal(4, components.Count);
        Assert.Equal(ComponentAt(0), components["Life"]);
        Assert.Equal(ComponentAt(1), components["Render"]);
        Assert.Equal(ComponentAt(2), components["Player"]);
    }

    [Fact]
    public void An_index_outside_the_component_list_is_skipped()
    {
        using var mem = BuildEntity();
        mem.WriteInt32(BucketAddress + GameLayout.Components.EntryIndex, 99);

        var components = new GameEntity(mem, EntityAddress).Components();

        Assert.DoesNotContain("Life", components.Keys);
        Assert.Equal(3, components.Count);
    }

    [Fact]
    public void A_negative_index_is_skipped()
    {
        using var mem = BuildEntity();
        mem.WriteInt32(BucketAddress + GameLayout.Components.EntryIndex, -1);

        Assert.DoesNotContain("Life", new GameEntity(mem, EntityAddress).Components().Keys);
    }

    [Fact]
    public void An_inverted_component_vector_yields_nothing()
    {
        using var mem = BuildEntity();
        mem.WritePointer(EntityAddress + GameLayout.Components.ComponentList + GameLayout.Native.VectorLast,
            ComponentArray - 8);

        Assert.Empty(new GameEntity(mem, EntityAddress).Components());
    }

    [Fact]
    public void An_absurd_component_count_yields_nothing()
    {
        using var mem = BuildEntity();
        mem.WritePointer(EntityAddress + GameLayout.Components.ComponentList + GameLayout.Native.VectorLast,
            ComponentArray + (5000 * 8));

        Assert.Empty(new GameEntity(mem, EntityAddress).Components());
    }

    [Fact]
    public void The_owner_back_reference_points_at_the_entity()
    {
        using var mem = BuildEntity();
        var entity = new GameEntity(mem, EntityAddress);

        Assert.All(entity.Components().Values, c => Assert.Equal(EntityAddress, entity.OwnerOf(c)));
    }

    [Fact]
    public void A_missing_owner_back_reference_is_visible()
    {
        using var mem = BuildEntity(ownerBackReference: false);
        var entity = new GameEntity(mem, EntityAddress);

        Assert.All(entity.Components().Values, c => Assert.NotEqual(EntityAddress, entity.OwnerOf(c)));
    }

    [Fact]
    public void The_same_resolver_serves_an_item_as_well_as_a_character()
    {
        // Items are entities. A separate component system for them would be a
        // second thing to keep correct for no gain.
        using var mem = BuildEntity(metadata: "Metadata/Items/Belts/FourBelt1");
        var item = new GameEntity(mem, EntityAddress);

        Assert.True(item.IsValid);
        Assert.Equal(4, item.Components().Count);
    }

    // ---- typed reads ------------------------------------------------------------------

    [Fact]
    public void Vitals_are_read_from_their_blocks()
    {
        using var mem = BuildEntity();
        var life = ComponentAt(0);

        mem.WriteInt32(life + GameLayout.Vitals.Health + GameLayout.Vitals.BlockId, 239);
        mem.WriteInt32(life + GameLayout.Vitals.Health + GameLayout.Vitals.BlockMax, 359);
        mem.WriteInt32(life + GameLayout.Vitals.Health + GameLayout.Vitals.BlockCurrent, 200);

        var health = new GameEntity(mem, EntityAddress).Vitals().Single(v => v.Name == "Health");

        Assert.Equal(239, health.Id);
        Assert.Equal(200, health.Current);
        Assert.Equal(359, health.Max);
    }

    [Fact]
    public void An_entity_without_a_life_component_reports_no_vitals()
    {
        using var mem = BuildEntity(componentCount: 2);

        // Only Life and Render exist here; drop Life by pointing its name
        // elsewhere so the component cannot be resolved.
        mem.WriteAscii(NameArea, "Chest");

        Assert.Empty(new GameEntity(mem, EntityAddress).Vitals());
    }

    [Fact]
    public void World_position_is_read_as_three_finite_floats()
    {
        using var mem = BuildEntity();
        var render = ComponentAt(1);

        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition, 4048.9f);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 4, 2712f);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 8, -353.4f);

        var position = new GameEntity(mem, EntityAddress).WorldPosition();

        Assert.NotNull(position);
        Assert.Equal(4048.9f, position!.Value.X, 1);
        Assert.Equal(-353.4f, position.Value.Z, 1);
    }

    [Fact]
    public void A_non_finite_world_position_is_rejected()
    {
        using var mem = BuildEntity();
        var render = ComponentAt(1);

        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition, float.NaN);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 4, 1f);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 8, 1f);

        Assert.Null(new GameEntity(mem, EntityAddress).WorldPosition());
    }

    [Fact]
    public void The_grid_position_is_derived_from_the_world_position()
    {
        // The contract no longer carries a grid offset, so this must come out of
        // the world position and the tile ratio the client uses.
        using var mem = BuildEntity();
        var render = ComponentAt(1);

        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition, 4125f);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 4, 2548.9f);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 8, -353.4f);

        var grid = new GameEntity(mem, EntityAddress).GridPosition();

        Assert.NotNull(grid);
        Assert.Equal(4125f / (250f / 23f), grid!.Value.X, 1);
        Assert.Equal(2548.9f / (250f / 23f), grid.Value.Y, 1);
    }

    [Fact]
    public void A_grid_position_needs_a_world_position_to_derive_from()
    {
        using var mem = BuildEntity();
        var render = ComponentAt(1);

        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition, float.NaN);

        Assert.Null(new GameEntity(mem, EntityAddress).GridPosition());
    }

    [Fact]
    public void The_tile_ratio_comes_from_the_contract()
    {
        Assert.Equal(250f / 23f, GameLayout.Spatial.WorldToGrid, 4);
    }

    [Fact]
    public void Player_identity_is_read()
    {
        using var mem = BuildEntity();
        var player = ComponentAt(2);

        mem.WriteWideString(player + GameLayout.Identity.Name, 0x600000, "gravataicity");
        mem.WriteBytes(player + GameLayout.Identity.Level, 17);

        var (name, level) = new GameEntity(mem, EntityAddress).Identity();

        Assert.Equal("gravataicity", name);
        Assert.Equal(17, level);
    }

    // ---- lifetime ------------------------------------------------------------------------

    [Fact]
    public void An_entity_whose_details_are_gone_stops_being_valid()
    {
        // Readable is not alive: freed memory keeps returning whatever it held.
        //
        // Asked of a FRESH entity, because that is how the walk asks. A
        // GameEntity is a handle built to describe one address during one
        // capture and thrown away — it caches what it reads so that resolving
        // six components does not cross into the client thirty times — so the
        // guarantee that matters is that the NEXT walk sees the truth, not that
        // a handle nobody keeps notices a change under it.
        using var mem = BuildEntity();

        Assert.True(new GameEntity(mem, EntityAddress).IsValid);

        mem.WritePointer(EntityAddress + GameLayout.Components.EntityDetails, 0);

        Assert.False(new GameEntity(mem, EntityAddress).IsValid);
    }

    [Fact]
    public void Component_addresses_are_never_remembered_past_the_walk_that_read_them()
    {
        using var mem = BuildEntity();

        Assert.Equal(ComponentAt(0), new GameEntity(mem, EntityAddress).Component("Life"));

        mem.WritePointer(ComponentArray, 0x900000);
        mem.Zero(0x900000, 0x40);

        // Nothing about the slot survives the handle. The per-TYPE caches hold
        // the metadata path and the name-to-slot table, which belong to the type
        // descriptor; a component ADDRESS is assigned per entity at runtime, so
        // remembering one past the walk that read it would outlive the entity.
        Assert.Equal(0x900000, new GameEntity(mem, EntityAddress).Component("Life"));
    }

    // ---- projection ------------------------------------------------------------------------

    private static float[] Orthographic(float scale = 0.001f) => new[]
    {
        scale, 0.0001f, 0.0002f, 0f,
        0.0001f, scale, 0.0003f, 0f,
        0.00005f, 0.00005f, 1f, 0f,
        0f, 0f, 0.5f, 1f,
    };

    [Fact]
    public void The_origin_projects_to_the_centre_of_the_viewport()
    {
        var point = GameCamera.Project(Orthographic(), new Vector3(0, 0, 0), 1920, 1080);

        Assert.Equal(ScreenStatus.OnScreen, point.Status);
        Assert.Equal(960f, point.Screen.X, 2);
        Assert.Equal(540f, point.Screen.Y, 2);
    }

    [Fact]
    public void A_larger_world_y_maps_further_up_the_screen()
    {
        var point = GameCamera.Project(Orthographic(), new Vector3(0, 500, 0), 1920, 1080);

        Assert.True(point.Screen.Y < 540f);
    }

    [Fact]
    public void A_point_outside_the_frustum_is_reported_rather_than_clamped()
    {
        var point = GameCamera.Project(Orthographic(), new Vector3(5000, 0, 0), 1920, 1080);

        Assert.Equal(ScreenStatus.OffScreen, point.Status);
        Assert.True(point.Screen.X > 1920f);
    }

    [Fact]
    public void A_point_behind_the_camera_is_reported_rather_than_folded_onto_the_screen()
    {
        var matrix = Orthographic();
        matrix[15] = 0f;
        matrix[11] = -1f;

        Assert.Equal(ScreenStatus.BehindCamera,
            GameCamera.Project(matrix, new Vector3(0, 0, 100), 1920, 1080).Status);
    }

    [Fact]
    public void A_matrix_producing_non_finite_clip_is_invalid()
    {
        var matrix = Orthographic();
        matrix[0] = float.NaN;

        Assert.Equal(ScreenStatus.Invalid,
            GameCamera.Project(matrix, new Vector3(1, 1, 1), 1920, 1080).Status);
    }

    [Fact]
    public void A_short_matrix_is_invalid()
    {
        Assert.Equal(ScreenStatus.Invalid,
            GameCamera.Project(new float[8], new Vector3(0, 0, 0), 1920, 1080).Status);
    }

    // ---- native layouts -------------------------------------------------------------------------

    [Fact]
    public void The_native_vector_layout_comes_from_the_contract()
    {
        Assert.Equal(0x00, GameLayout.Native.VectorFirst);
        Assert.Equal(0x08, GameLayout.Native.VectorLast);
        Assert.Equal(0x10, GameLayout.Native.VectorEnd);
    }

    [Fact]
    public void A_wide_string_round_trips_through_the_reader()
    {
        var mem = new FakeMemory();
        mem.Zero(0x700000, 0x40);
        mem.WriteWideString(0x700000, 0x710000, "Search Results");

        Assert.Equal("Search Results", mem.TryReadWideString(
            0x700000,
            GameLayout.Native.StringBuffer,
            GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity));

        mem.Dispose();
    }

    [Fact]
    public void A_wide_string_whose_length_exceeds_its_capacity_is_rejected()
    {
        var mem = new FakeMemory();
        mem.Zero(0x700000, 0x40);
        mem.WriteWideString(0x700000, 0x710000, "hello");
        mem.WriteBytes(0x700000 + GameLayout.Native.StringCapacity, BitConverter.GetBytes(2L));

        Assert.Null(mem.TryReadWideString(
            0x700000,
            GameLayout.Native.StringBuffer,
            GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity));

        mem.Dispose();
    }

    [Fact]
    public void The_map_node_layout_comes_from_the_contract()
    {
        Assert.Equal(0x00, GameLayout.Native.MapLeft);
        Assert.Equal(0x08, GameLayout.Native.MapParent);
        Assert.Equal(0x10, GameLayout.Native.MapRight);
    }

    // ---- entity world ------------------------------------------------------------------------------

    [Fact]
    public void A_container_cycle_does_not_hang_enumeration()
    {
        var mem = new FakeMemory();
        const nint area = 0x800000;
        const nint node = 0x810000;

        mem.Zero(area, 0x800);
        mem.Zero(node, 0x80);
        mem.WritePointer(area + GameLayout.World.AwakeEntities, node);

        // Every link points back at the node itself.
        mem.WritePointer(node + GameLayout.Native.MapLeft, node);
        mem.WritePointer(node + GameLayout.Native.MapParent, node);
        mem.WritePointer(node + GameLayout.Native.MapRight, node);

        Assert.Empty(EntityWorld.Enumerate(mem, area, GameLayout.World.AwakeEntities));
        mem.Dispose();
    }

    [Fact]
    public void An_empty_container_enumerates_to_nothing()
    {
        var mem = new FakeMemory();
        const nint area = 0x800000;

        mem.Zero(area, 0x800);

        Assert.Empty(EntityWorld.Enumerate(mem, area, GameLayout.World.AwakeEntities));
        mem.Dispose();
    }
}
