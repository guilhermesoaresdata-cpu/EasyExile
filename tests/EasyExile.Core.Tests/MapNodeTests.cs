using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Tests;

/// <summary>
/// Decoding of the entity containers. The mapped entity now comes from a
/// confirmed contract offset, so these tests are about reading it correctly and
/// about refusing everything that merely looks like it.
/// </summary>
public class MapNodeTests
{
    private const nint Head = 0x80000;
    private const nint NodeBase = 0x100000;
    private const nint EntityBase = 0x200000;
    private const nint Area = 0x900000;

    /// <summary>
    /// A three-node tree: root with a left and a right child, each carrying an
    /// entity at the contract offset, plus a head sentinel that carries none.
    /// </summary>
    private static FakeMemory BuildTree(int entityCount = 3, bool valueAtContractOffset = true)
    {
        var mem = new FakeMemory();

        mem.Zero(Area, 0x800);
        mem.WritePointer(Area + GameLayout.World.AwakeEntities, Head);
        mem.WritePointer(Area + GameLayout.World.SleepingEntities, Head);

        mem.Zero(Head, 0x48);

        for (int i = 0; i < entityCount; i++)
        {
            var node = NodeBase + (i * 0x1000);
            var entity = EntityBase + (i * 0x1000);

            mem.Zero(node, 0x48);
            WriteEntity(mem, entity, $"Metadata/Monsters/Test/Monster{i}");

            var slot = valueAtContractOffset ? GameLayout.Native.MapEntityValue : 0x30;
            mem.WritePointer(node + slot, entity);
            mem.WriteInt32(node + GameLayout.Native.MapKey, 100 + i);

            // Leaves point both child links back at the sentinel, the way the
            // real container does.
            mem.WritePointer(node + GameLayout.Native.MapLeft, Head);
            mem.WritePointer(node + GameLayout.Native.MapRight, Head);
        }

        // head -> root, root -> two children
        mem.WritePointer(Head + GameLayout.Native.MapParent, NodeBase);

        if (entityCount >= 3)
        {
            mem.WritePointer(NodeBase + GameLayout.Native.MapLeft, NodeBase + 0x1000);
            mem.WritePointer(NodeBase + GameLayout.Native.MapRight, NodeBase + 0x2000);
        }

        return mem;
    }

    private static void WriteEntity(FakeMemory mem, nint entity, string metadata)
    {
        var details = entity + 0x400;
        var buffer = entity + 0x500;
        var componentArray = entity + 0x600;
        var lookup = entity + 0x700;
        var bucket = entity + 0x780;
        var namePtr = entity + 0x7C0;
        var component = entity + 0x800;

        mem.Zero(entity, 0x40);
        mem.Zero(details, 0x40);
        mem.Zero(componentArray, 0x20);
        mem.Zero(lookup, 0x40);
        mem.Zero(bucket, 0x40);
        mem.Zero(namePtr, 0x40);
        mem.Zero(component, 0x40);

        mem.WritePointer(entity + GameLayout.Components.EntityDetails, details);
        mem.WriteWideString(details + GameLayout.Components.Metadata, buffer, metadata);
        mem.WritePointer(details + GameLayout.Components.ComponentLookup, lookup);

        mem.WriteVector(entity + GameLayout.Components.ComponentList, componentArray, 1, 8);
        mem.WritePointer(componentArray, component);

        mem.WriteVector(lookup + GameLayout.Components.NameBucket, bucket, 1, GameLayout.Components.EntryStride);
        mem.WriteAscii(namePtr, "Render");
        mem.WritePointer(bucket + GameLayout.Components.EntryName, namePtr);
        mem.WriteInt32(bucket + GameLayout.Components.EntryIndex, 0);
    }

    // ---- contract ----------------------------------------------------------

    [Fact]
    public void The_contract_supplies_the_mapped_entity_offset()
    {
        Assert.Equal(0x28, GameLayout.Native.MapEntityValue);
        Assert.Equal(0x20, GameLayout.Native.MapKey);
    }

    [Fact]
    public void The_value_slot_does_not_collide_with_the_links()
    {
        var links = new[] { GameLayout.Native.MapLeft, GameLayout.Native.MapParent, GameLayout.Native.MapRight };

        Assert.DoesNotContain(GameLayout.Native.MapEntityValue, links);
        Assert.DoesNotContain(GameLayout.Native.MapKey, links);
        Assert.NotEqual(GameLayout.Native.MapKey, GameLayout.Native.MapEntityValue);
    }

    // ---- decoding ------------------------------------------------------------

    [Fact]
    public void The_awake_map_decodes_to_its_entities()
    {
        using var mem = BuildTree();

        var entities = EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities);

        Assert.Equal(3, entities.Length);
        Assert.All(entities, e => Assert.True(new GameEntity(mem, e).IsValid));
    }

    [Fact]
    public void The_sleeping_map_decodes_through_the_same_layout()
    {
        // Both containers are std::map<uint, Entity*>; a second decoder for one
        // of them would be a second thing to keep correct.
        using var mem = BuildTree();

        var awake = EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities);
        var sleeping = EntityWorld.Enumerate(mem, Area, GameLayout.World.SleepingEntities);

        Assert.Equal(awake, sleeping);
    }

    [Fact]
    public void The_head_sentinel_contributes_no_entity()
    {
        using var mem = BuildTree();

        // The sentinel is walked through — it is how the root is reached — but it
        // holds no value, so it must not appear as an entity.
        Assert.Equal(3, EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities).Length);
    }

    [Fact]
    public void An_entity_reachable_from_several_nodes_is_reported_once()
    {
        using var mem = BuildTree();
        mem.WritePointer(NodeBase + 0x1000 + GameLayout.Native.MapEntityValue, EntityBase);

        var entities = EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities);

        Assert.Equal(entities.Length, entities.Distinct().Count());
    }

    // ---- discrimination --------------------------------------------------------

    [Fact]
    public void An_entity_at_an_adjacent_slot_is_not_picked_up()
    {
        // This is the whole reason the offset had to be confirmed rather than
        // searched for: with probing, a value one slot over read exactly the same.
        using var mem = BuildTree(valueAtContractOffset: false);

        Assert.Empty(EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities));
    }

    [Fact]
    public void A_readable_pointer_that_is_not_an_entity_is_rejected()
    {
        using var mem = BuildTree();

        mem.Zero(0x800000, 0x40);
        mem.WritePointer(NodeBase + GameLayout.Native.MapEntityValue, 0x800000);

        var entities = EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities);

        Assert.Equal(2, entities.Length);
        Assert.DoesNotContain((nint)0x800000, entities);
    }

    [Fact]
    public void An_unaligned_value_is_rejected()
    {
        using var mem = BuildTree();
        mem.WritePointer(NodeBase + GameLayout.Native.MapEntityValue, EntityBase + 4);

        Assert.Equal(2, EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities).Length);
    }

    [Fact]
    public void An_empty_container_yields_nothing()
    {
        using var mem = BuildTree();
        mem.WritePointer(Area + GameLayout.World.AwakeEntities, 0);

        Assert.Empty(EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities));
    }

    [Fact]
    public void A_cycle_in_the_links_does_not_hang_enumeration()
    {
        using var mem = BuildTree();

        foreach (var link in new[] { GameLayout.Native.MapLeft, GameLayout.Native.MapParent, GameLayout.Native.MapRight })
            mem.WritePointer(NodeBase + link, NodeBase);

        Assert.NotEmpty(EntityWorld.Enumerate(mem, Area, GameLayout.World.AwakeEntities));
    }

    // ---- no probing left --------------------------------------------------------

    [Fact]
    public void The_consumer_does_not_probe_for_the_mapped_entity()
    {
        var source = ReadCoreSource(Path.Combine("World", "EntityWorld.cs"));

        // A slot-stepping loop is exactly the heuristic this replaced.
        Assert.DoesNotContain("slot", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x20;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("+= 8", source, StringComparison.Ordinal);
        Assert.Contains("GameLayout.Native.MapEntityValue", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_consumer_file_outside_the_layout_wrapper_hardcodes_a_map_offset()
    {
        foreach (var file in new[]
                 {
                     Path.Combine("World", "EntityWorld.cs"),
                     Path.Combine("World", "GameEntity.cs"),
                     Path.Combine("Runtime", "GameSession.cs"),
                     Path.Combine("Camera", "GameCamera.cs"),
                 })
        {
            var source = ReadCoreSource(file);

            Assert.DoesNotContain("0x28", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0x6E0", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ReadCoreSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "EasyExile.Core", relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{relativePath} nao encontrado a partir de {AppContext.BaseDirectory}");
    }
}
