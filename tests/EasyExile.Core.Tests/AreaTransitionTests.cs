using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Tests;

/// <summary>
/// The chain walk and what an area change does to it. An area transition frees
/// the whole previous area, so anything held across one is a pointer into memory
/// the client has already reused.
/// </summary>
public class AreaTransitionTests
{
    private static readonly nint ModuleBase = unchecked((nint)0x140000000);
    private const nint Root = 0x1000000;
    private const nint StateArray = 0x1010000;
    private const nint LoadingState = 0x1020000;
    private const nint InGameState = 0x1030000;

    private const nint FirstArea = 0x2000000;
    private const nint SecondArea = 0x3000000;
    private const nint FirstPlayer = 0x2100000;
    private const nint SecondPlayer = 0x3100000;

    /// <summary>Two states, only the second of which carries a usable area.</summary>
    private static FakeMemory BuildChain()
    {
        var mem = new FakeMemory { ModuleBase = ModuleBase };

        mem.Zero(ModuleBase + GameLayout.Roots.GlobalSlotRva, 8);
        mem.WritePointer(ModuleBase + GameLayout.Roots.GlobalSlotRva, Root);

        mem.Zero(Root, 0x100);
        mem.Zero(StateArray, 0x40);
        mem.WriteVector(Root + GameLayout.Roots.CurrentStateVector, StateArray, 2, 8);
        mem.WritePointer(StateArray, LoadingState);
        mem.WritePointer(StateArray + 8, InGameState);

        // The loading state has no area at all.
        mem.Zero(LoadingState, 0x400);

        mem.Zero(InGameState, 0x400);
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, FirstArea);

        WriteArea(mem, FirstArea, FirstPlayer, "Metadata/Characters/Int/IntFourb");

        return mem;
    }

    private static void WriteArea(FakeMemory mem, nint area, nint player, string metadata)
    {
        mem.Zero(area, 0x800);
        mem.WritePointer(area + GameLayout.World.LocalPlayer, player);

        var details = player + 0x8000;
        var buffer = player + 0x9000;

        mem.Zero(player, 0x40);
        mem.Zero(details, 0x40);
        mem.WritePointer(player + GameLayout.Components.EntityDetails, details);
        mem.WriteWideString(details + GameLayout.Components.Metadata, buffer, metadata);
    }

    [Fact]
    public void The_chain_resolves_to_the_state_that_carries_an_area()
    {
        using var mem = BuildChain();

        var state = GameSession.ResolveChain(mem, ModuleBase);

        Assert.NotNull(state);
        Assert.Equal(Root, state!.GameStateRoot);
        Assert.Equal(InGameState, state.InGameState);
        Assert.Equal(FirstArea, state.AreaInstance);
        Assert.Equal(FirstPlayer, state.LocalPlayer);
    }

    [Fact]
    public void A_client_sitting_outside_an_area_resolves_to_nothing()
    {
        using var mem = BuildChain();
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, 0);

        Assert.Null(GameSession.ResolveChain(mem, ModuleBase));
    }

    [Fact]
    public void A_missing_global_slot_resolves_to_nothing()
    {
        using var mem = BuildChain();
        mem.WritePointer(ModuleBase + GameLayout.Roots.GlobalSlotRva, 0);

        Assert.Null(GameSession.ResolveChain(mem, ModuleBase));
    }

    [Fact]
    public void An_absurd_state_vector_resolves_to_nothing()
    {
        using var mem = BuildChain();
        mem.WritePointer(Root + GameLayout.Roots.CurrentStateVector + GameLayout.Native.VectorLast,
            StateArray + (500 * 8));

        Assert.Null(GameSession.ResolveChain(mem, ModuleBase));
    }

    [Fact]
    public void After_an_area_change_a_fresh_walk_lands_in_the_new_area()
    {
        using var mem = BuildChain();

        var before = GameSession.ResolveChain(mem, ModuleBase);

        WriteArea(mem, SecondArea, SecondPlayer, "Metadata/Characters/Int/IntFourb");
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, SecondArea);

        var after = GameSession.ResolveChain(mem, ModuleBase);

        Assert.NotNull(after);
        Assert.NotEqual(before!.AreaInstance, after!.AreaInstance);
        Assert.Equal(SecondArea, after.AreaInstance);
        Assert.Equal(SecondPlayer, after.LocalPlayer);
    }

    [Fact]
    public void An_entity_held_across_an_area_change_stops_validating()
    {
        // This is the point of never storing addresses: the old player object is
        // freed, and what remains is not an entity any more.
        using var mem = BuildChain();

        var state = GameSession.ResolveChain(mem, ModuleBase)!;
        var stale = new GameEntity(mem, state.LocalPlayer);

        Assert.True(stale.IsValid);

        mem.Zero(FirstPlayer, 0x40);
        mem.Zero(FirstPlayer + 0x8000, 0x40);
        WriteArea(mem, SecondArea, SecondPlayer, "Metadata/Characters/Int/IntFourb");
        mem.WritePointer(InGameState + GameLayout.Roots.AreaInstance, SecondArea);

        Assert.False(stale.IsValid);
        Assert.True(new GameEntity(mem, GameSession.ResolveChain(mem, ModuleBase)!.LocalPlayer).IsValid);
    }

    [Fact]
    public void Freed_memory_reused_by_something_else_is_not_mistaken_for_the_old_entity()
    {
        using var mem = BuildChain();

        var state = GameSession.ResolveChain(mem, ModuleBase)!;
        var stale = new GameEntity(mem, state.LocalPlayer);

        // The allocator hands the same bytes to an item.
        WriteArea(mem, FirstArea, FirstPlayer, "Metadata/Items/Currency/CurrencyRerollRare");

        Assert.True(stale.IsValid);
        Assert.StartsWith("Metadata/Items/", stale.Metadata);
    }
}
