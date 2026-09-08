namespace EasyExile.Core.Memory;

/// <summary>Reads raw bytes from wherever the caller keeps them.</summary>
internal delegate bool ByteSource(nint address, int length, out byte[] bytes);

/// <summary>
/// Decoding of the native string layouts the contract describes. It lives apart
/// from any one reader so the live client and the tests decode through the same
/// code — a copy in the test double would only ever prove itself correct.
/// </summary>
internal static class NativeText
{
    public static string? ReadUtf8(ByteSource read, nint address, int maxLength = 128)
    {
        if (address == 0) return null;

        // Read in chunks: a string ending just before an unmapped page would
        // fail a single large read, which is indistinguishable from no string.
        var bytes = new List<byte>(32);

        while (bytes.Count < maxLength)
        {
            if (!read(address + bytes.Count, 8, out var chunk)) break;

            var terminator = Array.IndexOf(chunk, (byte)0);
            if (terminator >= 0)
            {
                bytes.AddRange(chunk[..terminator]);
                break;
            }

            bytes.AddRange(chunk);
        }

        if (bytes.Count == 0) return null;

        foreach (var b in bytes)
        {
            if (b is < 0x20 or > 0x7E) return null;
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// Reads an MSVC wide string. Short strings live inline in the same bytes
    /// that otherwise hold the pointer, which is why capacity decides where to
    /// look rather than the pointer being tested for validity.
    /// </summary>
    /// <summary>
    /// A bare NUL-terminated UTF-16 buffer at a pointer — not a
    /// <c>std::wstring</c>, which carries its own length. Monster mod ids are
    /// stored this way.
    /// </summary>
    public static string? ReadUtf16(ByteSource read, nint address, int maxLength = 128)
    {
        if (address == 0) return null;

        var bytes = new List<byte>(64);

        // Chunked for the same reason as UTF-8: a string ending just before an
        // unmapped page fails one large read and looks like no string at all.
        while (bytes.Count < maxLength * 2)
        {
            if (!read(address + bytes.Count, 8, out var chunk)) break;

            var done = false;

            for (var i = 0; i + 1 < chunk.Length; i += 2)
            {
                if (chunk[i] != 0 || chunk[i + 1] != 0) continue;

                bytes.AddRange(chunk[..i]);
                done = true;
                break;
            }

            if (done) break;

            bytes.AddRange(chunk);
        }

        return bytes.Count == 0 ? null : System.Text.Encoding.Unicode.GetString(bytes.ToArray());
    }

    public static string? ReadWideString(
        ByteSource read, nint wstring, int bufferOffset, int sizeOffset, int capacityOffset, int maxLength = 256)
    {
        if (!ReadInt64(read, wstring + sizeOffset, out var size) || size is < 1 or > 4096) return null;
        if (!ReadInt64(read, wstring + capacityOffset, out var capacity) || capacity < size) return null;

        var take = (int)Math.Min(size, maxLength);

        nint data;
        if (capacity < 8)
        {
            data = wstring + bufferOffset;
        }
        else
        {
            if (!ReadInt64(read, wstring + bufferOffset, out var pointer) || pointer == 0) return null;
            data = (nint)pointer;
        }

        if (!read(data, take * 2, out var bytes)) return null;

        var text = System.Text.Encoding.Unicode.GetString(bytes);

        foreach (var ch in text)
        {
            if (char.IsSurrogate(ch)) return null;
            if (char.IsControl(ch) && ch != '\n' && ch != '\t') return null;
        }

        return text;
    }

    private static bool ReadInt64(ByteSource read, nint address, out long value)
    {
        value = 0;
        if (!read(address, 8, out var bytes)) return false;

        value = BitConverter.ToInt64(bytes, 0);
        return true;
    }
}
