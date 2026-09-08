using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;

namespace EasyExile.Core.World;

/// <summary>
/// Reads the walkable grid out of the area's terrain block.
/// </summary>
/// <remarks>
/// Ported from <c>POE2Radar.Core/Game/Poe2Live.cs</c>, method <c>Terrain</c>
/// (MIT, https://github.com/…/POE2Radar). The unpacking loop, the bounds and the
/// width convention are the reference's; only the offsets come from our contract
/// instead of its own table.
///
/// The block is INLINE. Its address is <c>areaInstance + Root</c> — an addition,
/// never a pointer read. Dereferencing there returns the struct's own vtable
/// pointer, which is an address inside the executable image and reads as a bad
/// offset. That mistake cost a whole sprint once.
/// </remarks>
internal static class TerrainReader
{
    /// <summary>The most a single area's walkable grid may occupy, from the reference.</summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    public static TerrainSnapshot? Read(IMemoryReader memory, nint areaInstance)
    {
        if (areaInstance == 0) return null;

        var terrain = areaInstance + GameLayout.Terrain.Root;

        if (!memory.TryReadPointer(terrain + GameLayout.Terrain.WalkableGrid, out var first) || first == 0)
            return null;

        if (!memory.TryReadPointer(terrain + GameLayout.Terrain.WalkableGrid + GameLayout.Native.VectorLast, out var last))
            return null;

        if (!memory.TryRead<int>(terrain + GameLayout.Terrain.BytesPerRow, out var bytesPerRow))
            return null;

        if (bytesPerRow is <= 0 or > 65536) return null;

        var totalBytes = (long)last - (long)first;
        if (totalBytes <= 0 || totalBytes > MaxBytes) return null;

        var rows = (int)(totalBytes / bytesPerRow);
        if (rows is <= 0 or > 65536) return null;

        if (!memory.TryReadBytes(first, (int)totalBytes, out var raw)) return null;

        // Two cells per byte: the low nibble is the even cell, the high nibble
        // the odd one. Non-zero means walkable.
        var width = bytesPerRow * 2;
        var walkable = new byte[width * rows];

        for (var y = 0; y < rows; y++)
        {
            var rowBase = (long)y * bytesPerRow;

            for (var x = 0; x < width; x++)
            {
                var b = raw[rowBase + (x >> 1)];
                var nibble = (x & 1) == 0 ? b & 0x0F : b >> 4;

                walkable[(y * width) + x] = (byte)(nibble != 0 ? 1 : 0);
            }
        }

        return new TerrainSnapshot(walkable, width, rows);
    }
}
