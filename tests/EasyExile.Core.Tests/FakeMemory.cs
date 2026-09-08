using System.Runtime.InteropServices;
using EasyExile.Core.Memory;

namespace EasyExile.Core.Tests;

/// <summary>
/// A sparse address space so game structures can be laid out by hand and read
/// through the same interface as the live client.
/// </summary>
internal sealed class FakeMemory : IMemoryReader
{
    private readonly Dictionary<nint, byte> _bytes = new();

    public nint ModuleBase { get; set; } = unchecked((nint)0x140000000);

    public void WriteBytes(nint address, params byte[] data)
    {
        for (int i = 0; i < data.Length; i++) _bytes[address + i] = data[i];
    }

    public void Zero(nint address, int length) => WriteBytes(address, new byte[length]);

    public void WritePointer(nint address, nint value) =>
        WriteBytes(address, BitConverter.GetBytes((long)value));

    public void WriteInt32(nint address, int value) => WriteBytes(address, BitConverter.GetBytes(value));

    public void WriteFloat(nint address, float value) => WriteBytes(address, BitConverter.GetBytes(value));

    public void WriteAscii(nint address, string text)
    {
        WriteBytes(address, System.Text.Encoding.ASCII.GetBytes(text));
        _bytes[address + text.Length] = 0;
    }

    /// <summary>Lays out an MSVC wide string, heap-backed.</summary>
    public void WriteWideString(nint wstring, nint buffer, string text)
    {
        Zero(buffer, (text.Length + 4) * 2);
        WriteBytes(buffer, System.Text.Encoding.Unicode.GetBytes(text));

        WritePointer(wstring, buffer);
        WriteBytes(wstring + 0x10, BitConverter.GetBytes((long)text.Length));
        WriteBytes(wstring + 0x18, BitConverter.GetBytes((long)text.Length + 8));
    }

    /// <summary>Inline first/last/end triple.</summary>
    public void WriteVector(nint address, nint first, int count, int stride)
    {
        WritePointer(address, first);
        WritePointer(address + 8, first + (count * stride));
        WritePointer(address + 16, first + (count * stride));
    }

    public bool IsReadable(nint address, int size = 1)
    {
        for (int i = 0; i < size; i++)
        {
            if (!_bytes.ContainsKey(address + i)) return false;
        }

        return true;
    }

    /// <summary>
    /// Called before every read. A live client mutates while it is being read,
    /// and this is how a test reproduces that - most importantly an area
    /// transition landing in the middle of a capture.
    /// </summary>
    public Action<nint>? BeforeRead { get; set; }

    /// <summary>Reads served. What a capture actually costs, countable.</summary>
    public long Reads { get; set; }

    public bool TryReadBytes(nint address, int length, out byte[] bytes)
    {
        Reads++;
        BeforeRead?.Invoke(address);

        bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            if (!_bytes.TryGetValue(address + i, out var b)) return false;
            bytes[i] = b;
        }

        return true;
    }

    public bool TryRead<T>(nint address, out T value) where T : unmanaged
    {
        value = default;
        if (!TryReadBytes(address, Marshal.SizeOf<T>(), out var buffer)) return false;

        value = MemoryMarshal.Read<T>(buffer);
        return true;
    }

    public bool TryReadPointer(nint address, out nint value)
    {
        value = 0;
        if (!TryRead<long>(address, out var raw)) return false;

        value = (nint)raw;
        return true;
    }

    // Decoding goes through the same code the live reader uses; a second
    // implementation here would only ever agree with itself.
    public string? TryReadUtf8(nint address, int maxLength = 128) =>
        NativeText.ReadUtf8(TryReadBytes, address, maxLength);

    public string? TryReadUtf16(nint address, int maxLength = 128) =>
        NativeText.ReadUtf16(TryReadBytes, address, maxLength);

    public string? TryReadWideString(
        nint wstring, int bufferOffset, int sizeOffset, int capacityOffset, int maxLength = 256) =>
        NativeText.ReadWideString(TryReadBytes, wstring, bufferOffset, sizeOffset, capacityOffset, maxLength);

    public void Dispose() { }
}
