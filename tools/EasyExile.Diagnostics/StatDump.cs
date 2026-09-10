using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Every stat the client keeps on the player, by its own key.
/// </summary>
/// <remarks>
/// The contract is explicit that these keys have no established meaning, so
/// this names nothing: it prints the pairs and leaves the matching to somebody
/// who can also see the character sheet. A resistance read from a key nobody
/// verified is a number that looks right and is not.
/// </remarks>
public static class StatDump
{
    public static int Run(GameSession session)
    {
        Console.WriteLine();
        Console.WriteLine("== STATS DO PERSONAGEM =====================================");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        var player = new GameEntity(session.Memory, chain.LocalPlayer);
        var components = player.Components();

        if (!components.TryGetValue(GameLayout.Names.Stats, out var stats))
        {
            Console.WriteLine(" o player nao tem componente Stats.");

            return 0;
        }

        if (!session.Memory.TryReadPointer(stats + GameLayout.StatTable.Struct, out var table) ||
            table == 0)
        {
            Console.WriteLine(" StatsStructInternal nao resolveu.");

            return 0;
        }

        if (!session.Memory.TryReadPointer(
                table + GameLayout.StatTable.Vector + GameLayout.Native.VectorFirst, out var first) ||
            !session.Memory.TryReadPointer(
                table + GameLayout.StatTable.Vector + GameLayout.Native.VectorLast, out var last) ||
            first == 0 || last <= first)
        {
            Console.WriteLine(" o vetor de stats esta vazio.");

            return 0;
        }

        var count = (int)((last - first) / GameLayout.StatTable.Stride);

        Console.WriteLine($" {count} entradas. Compare com a ficha do personagem (C).");
        Console.WriteLine();

        for (var i = 0; i < count && i < 400; i++)
        {
            var at = first + (i * GameLayout.StatTable.Stride);

            if (!session.Memory.TryRead<int>(at + GameLayout.StatTable.KeyOffset, out var key)) continue;
            if (!session.Memory.TryRead<int>(at + GameLayout.StatTable.ValueOffset, out var value)) continue;

            Console.WriteLine($"   chave {key,6}   valor {value,8}");
        }

        Console.WriteLine();

        return 0;
    }
}
