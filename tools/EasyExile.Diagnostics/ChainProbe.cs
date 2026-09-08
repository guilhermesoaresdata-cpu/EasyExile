using EasyExile.Core.Memory;
﻿using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Where the chain stops resolving, stage by stage.
/// </summary>
/// <remarks>
/// The resolver returns null and null says nothing: a missing root, an empty
/// state list, an unreadable area and a rejected player all look the same from
/// outside. And the Analyzer resolves this very client at this very moment, so
/// "the character is not in an area" — which is what the tool prints — is not
/// what is happening.
/// </remarks>
public static class ChainProbe
{
    public static int Run(GameSession session)
    {
        Console.WriteLine();
        Console.WriteLine("== CHAIN PROBE =============================================");

        var memory = session.Memory;
        var slot = memory.ModuleBase + GameLayout.Roots.GlobalSlotRva;

        Console.WriteLine($" modulo         0x{memory.ModuleBase:X}");
        Console.WriteLine($" slot rva       0x{GameLayout.Roots.GlobalSlotRva:X}");
        Console.WriteLine($" slot           0x{slot:X}");

        if (!memory.TryReadPointer(slot, out var root))
        {
            Console.WriteLine(" o slot nao e legivel.");

            return 0;
        }

        Console.WriteLine($" root           0x{root:X}  plausivel={GameEntity.IsPlausibleAddress(root)}");

        if (!GameEntity.IsPlausibleAddress(root)) return 0;

        var states = 0;

        foreach (var state in States(memory, root))
        {
            states++;

            if (!GameEntity.IsPlausibleAddress(state)) continue;

            Console.Write($"   estado 0x{state:X}");

            if (!memory.TryReadPointer(state + GameLayout.Roots.AreaInstance, out var area))
            {
                Console.WriteLine("  AreaInstance ilegivel");

                continue;
            }

            Console.Write($"  area 0x{area:X}");

            if (!GameEntity.IsPlausibleAddress(area))
            {
                Console.WriteLine("  area implausivel");

                continue;
            }

            if (!memory.IsReadable(area, GameLayout.World.RequiredReadableSpan))
            {
                Console.WriteLine($"  area nao cobre 0x{GameLayout.World.RequiredReadableSpan:X}");

                continue;
            }

            if (!memory.TryReadPointer(area + GameLayout.World.LocalPlayer, out var player))
            {
                Console.WriteLine("  LocalPlayer ilegivel");

                continue;
            }

            var entity = new GameEntity(memory, player);

            Console.WriteLine($"  player 0x{player:X}  valido={entity.IsValid}  {entity.Metadata}");
        }

        Console.WriteLine($" estados examinados: {states}");
        Console.WriteLine();

        return 0;
    }

    /// <summary>The same two places the resolver looks, kept in the same order.</summary>
    private static IEnumerable<nint> States(IMemoryReader memory, nint root)
    {
        if (memory.TryReadPointer(
                root + GameLayout.Roots.CurrentStateVector + GameLayout.Native.VectorFirst,
                out var first) &&
            memory.TryReadPointer(
                root + GameLayout.Roots.CurrentStateVector + GameLayout.Native.VectorLast,
                out var last) &&
            first != 0 && last >= first)
        {
            var count = (int)((last - first) / 8);

            Console.WriteLine($" vetor de estados: {count}");

            if (count is > 0 and <= 64)
            {
                for (var i = 0; i < count; i++)
                {
                    if (memory.TryReadPointer(first + (i * 8), out var state)) yield return state;
                }
            }
        }
        else
        {
            Console.WriteLine(" vetor de estados: ilegivel ou vazio");
        }

        Console.WriteLine($" slots inline: {GameLayout.Roots.StateSlotCount}");

        for (var i = 0; i < GameLayout.Roots.StateSlotCount; i++)
        {
            if (memory.TryReadPointer(
                    root + GameLayout.Roots.StatesArray + (i * GameLayout.Roots.StateSlotStride),
                    out var state) && state != 0)
                yield return state;
        }
    }
}
