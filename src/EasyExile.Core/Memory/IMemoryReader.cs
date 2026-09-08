namespace EasyExile.Core.Memory;

internal readonly record struct MemoryRegion(long Start, long End, bool Readable);

/// <summary>
/// Read-only view of another process. Nothing here writes, injects or sends
/// input — the client is observed and never touched.
/// </summary>
/// <remarks>
/// Internal. This is the boundary the product depends on: EasyExile.Radar and
/// every future visual module receive captured snapshots, never a reader. An
/// architecture test asserts Radar cannot reach this type.
/// </remarks>
internal interface IMemoryReader : IDisposable
{
    nint ModuleBase { get; }

    bool IsReadable(nint address, int size = 1);
    bool TryRead<T>(nint address, out T value) where T : unmanaged;
    bool TryReadPointer(nint address, out nint value);
    bool TryReadBytes(nint address, int length, out byte[] bytes);
    string? TryReadUtf8(nint address, int maxLength = 128);
    string? TryReadUtf16(nint address, int maxLength = 128);
    string? TryReadWideString(nint wstring, int bufferOffset, int sizeOffset, int capacityOffset, int maxLength = 256);
}
