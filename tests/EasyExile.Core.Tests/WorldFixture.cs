using EasyExile.Core.Contract;

namespace EasyExile.Core.Tests;

/// <summary>
/// A complete fake client: the root chain, an area with a local player, a camera
/// and a container of entities. Everything is laid out through GameLayout, so a
/// contract change moves the fixture with it instead of leaving the tests
/// agreeing with a layout the client no longer has.
/// </summary>
internal static class WorldFixture
{
    public static readonly nint ModuleBase = unchecked((nint)0x140000000);

    public const nint Root = 0x1000000;
    public const nint StateArray = 0x1010000;
    public const nint InGameState = 0x1030000;
    public const nint CameraAddress = 0x1500000;

    public const nint FirstArea = 0x2000000;
    public const nint SecondArea = 0x3000000;

    public const nint FirstPlayer = 0x2400000;
    public const nint SecondPlayer = 0x3400000;

    public const nint FirstContainer = 0x4000000;
    public const nint SecondContainer = 0x5000000;

    public const string PlayerName = "gravataicity";
    public const int PlayerLevel = 17;

    public const int Health = 359;
    public const int Mana = 120;
    public const int EnergyShield = 157;

    public static readonly Spatial.Vector3 PlayerPosition = new(4114f, 2625f, -353f);

    /// <summary>Column-major, the same shape the projection tests use.</summary>
    public static float[] Orthographic() => new[]
    {
        0.001f, 0.0001f, 0.0002f, 0f,
        0.0001f, 0.001f, 0.0003f, 0f,
        0.00005f, 0.00005f, 1f, 0f,
        0f, 0f, 0.5f, 1f,
    };

    /// <param name="scenery">
    /// Unmarked <c>/Terrain/</c> entities, written BEFORE the monsters so they
    /// get first claim on the capture budget. That ordering is the point: it is
    /// how a real area behaves, and it is what let scenery push the monsters out
    /// of the snapshot.
    /// </param>
    public static FakeMemory Build(int monsters = 3, int scenery = 0)
    {
        var mem = new FakeMemory { ModuleBase = ModuleBase };

        mem.Zero(ModuleBase + GameLayout.Roots.GlobalSlotRva, 8);
        mem.WritePointer(ModuleBase + GameLayout.Roots.GlobalSlotRva, Root);

        mem.Zero(Root, 0x100);
        mem.Zero(StateArray, 0x40);
        mem.WriteVector(Root + GameLayout.Roots.CurrentStateVector, StateArray, 1, 8);
        mem.WritePointer(StateArray, InGameState);

        mem.Zero(InGameState, 0x400);
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, FirstArea);
        mem.WritePointer(InGameState + GameLayout.Roots.Camera, CameraAddress);

        WriteCamera(mem, CameraAddress, 1920, 1080);
        WriteArea(mem, FirstArea, FirstPlayer, FirstContainer, monsters, scenery);

        return mem;
    }

    /// <summary>Swaps the whole area out, the way a portal does.</summary>
    public static void EnterSecondArea(FakeMemory mem, int monsters = 2)
    {
        WriteArea(mem, SecondArea, SecondPlayer, SecondContainer, monsters);
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, SecondArea);
    }

    public static void WriteCamera(FakeMemory mem, nint camera, int width, int height)
    {
        mem.Zero(camera, GameLayout.View.RequiredReadableSpan + 0x40);

        var matrix = Orthographic();
        for (int i = 0; i < matrix.Length; i++)
            mem.WriteFloat(camera + GameLayout.View.ViewProjection + (i * 4), matrix[i]);

        mem.WriteInt32(camera + GameLayout.View.Viewport, width);
        mem.WriteInt32(camera + GameLayout.View.Viewport + 4, height);
    }

    private static void WriteArea(
        FakeMemory mem, nint area, nint player, nint container, int monsters, int scenery = 0)
    {
        mem.Zero(area, GameLayout.World.RequiredReadableSpan + 0x100);
        mem.WritePointer(area + GameLayout.World.LocalPlayer, player);

        WritePlayer(mem, player);
        WriteContainer(mem, area, container, monsters, scenery);
    }

    /// <summary>An entity with Life, Render and Player components.</summary>
    private static void WritePlayer(FakeMemory mem, nint entity)
    {
        var life = entity + 0x10000;
        var render = entity + 0x20000;
        var identity = entity + 0x30000;

        WriteEntity(mem, entity, "Metadata/Characters/Int/IntFourb", new[]
        {
            (GameLayout.Names.Life, life),
            (GameLayout.Names.Render, render),
            (GameLayout.Names.Player, identity),
        });

        mem.Zero(life, GameLayout.Vitals.EnergyShield + 0x80);
        WriteVital(mem, life, GameLayout.Vitals.Health, 239, Health, Health);
        WriteVital(mem, life, GameLayout.Vitals.Mana, 240, Mana, Mana);
        WriteVital(mem, life, GameLayout.Vitals.EnergyShield, 241, EnergyShield, EnergyShield);

        mem.Zero(render, GameLayout.Spatial.WorldPosition + 0x40);
        WriteWorldPosition(mem, render, PlayerPosition.X, PlayerPosition.Y, PlayerPosition.Z);

        mem.Zero(identity, GameLayout.Identity.Level + 0x400);
        mem.WriteWideString(identity + GameLayout.Identity.Name, identity + 0x300, PlayerName);
        mem.WriteBytes(identity + GameLayout.Identity.Level, (byte)PlayerLevel);
    }

    private static void WriteContainer(FakeMemory mem, nint area, nint head, int monsters, int scenery = 0)
    {
        mem.WritePointer(area + GameLayout.World.AwakeEntities, head);
        mem.WritePointer(area + GameLayout.World.SleepingEntities, 0);

        mem.Zero(head, 0x48);

        var previous = head;

        for (int i = 0; i < monsters + scenery; i++)
        {
            var node = head + 0x1000 + (i * 0x1000);
            var monster = head + 0x100000 + (i * 0x40000);
            var render = monster + 0x10000;

            mem.Zero(node, 0x48);

            // Scenery first, so it competes for the budget the way it does live.
            var metadata = i < scenery
                ? "Metadata/Terrain/Test/Rock" + i
                : "Metadata/Monsters/Test/Monster" + (i - scenery);

            WriteEntity(mem, monster, metadata, new[]
            {
                (GameLayout.Names.Render, render),
            });

            mem.Zero(render, GameLayout.Spatial.WorldPosition + 0x40);
            WriteWorldPosition(mem, render, 4000f + (i * 50), 2600f + (i * 50), -350f);

            mem.WritePointer(node + GameLayout.Native.MapEntityValue, monster);
            mem.WriteInt32(node + GameLayout.Native.MapKey, 100 + i);
            mem.WritePointer(node + GameLayout.Native.MapLeft, head);
            mem.WritePointer(node + GameLayout.Native.MapRight, head);

            // Chained off the previous node so every entity is reachable from the
            // sentinel by the breadth-first walk.
            mem.WritePointer(previous + GameLayout.Native.MapParent, node);
            previous = node;
        }
    }

    private static void WriteVital(FakeMemory mem, nint life, int block, int id, int current, int max)
    {
        mem.WriteInt32(life + block + GameLayout.Vitals.BlockId, id);
        mem.WriteInt32(life + block + GameLayout.Vitals.BlockMax, max);
        mem.WriteInt32(life + block + GameLayout.Vitals.BlockCurrent, current);
    }

    public static void WriteWorldPosition(FakeMemory mem, nint render, float x, float y, float z)
    {
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition, x);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 4, y);
        mem.WriteFloat(render + GameLayout.Spatial.WorldPosition + 8, z);
    }

    private static void WriteEntity(
        FakeMemory mem, nint entity, string metadata, (string Name, nint Address)[] components)
    {
        var details = entity + 0x400;
        var buffer = entity + 0x500;
        var componentArray = entity + 0x600;
        var lookup = entity + 0x700;
        var bucket = entity + 0x780;
        var names = entity + 0x900;

        mem.Zero(entity, 0x40);
        mem.Zero(details, 0x40);
        mem.Zero(componentArray, (components.Length * 8) + 0x20);
        mem.Zero(lookup, 0x40);
        mem.Zero(bucket, (components.Length * GameLayout.Components.EntryStride) + 0x20);
        mem.Zero(names, components.Length * 0x40);

        mem.WritePointer(entity + GameLayout.Components.EntityDetails, details);
        mem.WriteWideString(details + GameLayout.Components.Metadata, buffer, metadata);
        mem.WritePointer(details + GameLayout.Components.ComponentLookup, lookup);

        mem.WriteVector(entity + GameLayout.Components.ComponentList, componentArray, components.Length, 8);
        mem.WriteVector(lookup + GameLayout.Components.NameBucket,
            bucket, components.Length, GameLayout.Components.EntryStride);

        for (int i = 0; i < components.Length; i++)
        {
            var (name, address) = components[i];
            var namePtr = names + (i * 0x40);

            mem.WriteAscii(namePtr, name);
            mem.WritePointer(componentArray + (i * 8), address);
            mem.WritePointer(address + GameLayout.Components.Owner, entity);

            var entry = bucket + (i * GameLayout.Components.EntryStride);
            mem.WritePointer(entry + GameLayout.Components.EntryName, namePtr);
            mem.WriteInt32(entry + GameLayout.Components.EntryIndex, i);
        }
    }
}
