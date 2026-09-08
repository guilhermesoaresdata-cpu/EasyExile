using System.Runtime.InteropServices;
using System.Text;
using EasyExile.Core.Runtime;

namespace EasyExile.Diagnostics;

/// <summary>
/// Finds a string the client is displaying, then finds who is holding it.
/// </summary>
/// <remarks>
/// Every search before this one asked the same question — "is there a UI element
/// whose Text field says this" — and every one answered no. But the text is on
/// screen, so it is in the process. What the answer really meant is that an
/// assumption was wrong somewhere, most likely that a tooltip keeps its text
/// where a ground label keeps its own.
///
/// So this asks nothing about structure. It scans committed memory for the
/// UTF-16 bytes, scans again for pointers to whatever it found, and for each
/// pointer works backwards to see whether the thing holding it is a UI element —
/// which is testable by itself, because an element's Self field points at the
/// element.
///
/// That yields both answers at once: where the text lives, and at what offset
/// its owner keeps it.
/// </remarks>
public static class TextHunt
{
    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;
    private const int MemCommit = 0x1000;
    private const int PageNoAccess = 0x01;
    private const int PageGuard = 0x100;

    /// <summary>The one field that identifies a UI element without any other.</summary>
    private const int UiSelf = 0x08;

    public static int Run(GameSession session, string needle)
    {
        Console.WriteLine();
        Console.WriteLine("== TEXT HUNT ===============================================");
        Console.WriteLine($" procurando: {needle}");
        Console.WriteLine();

        var handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, session.Process.Id);

        if (handle == 0)
        {
            Console.WriteLine(" nao consegui abrir o processo.");

            return 1;
        }

        try
        {
            var regions = Regions(handle);
            var bytes = Encoding.Unicode.GetBytes(needle);

            Console.WriteLine($" regioes legiveis: {regions.Count}");

            var found = Scan(handle, regions, bytes, limit: 40);

            Console.WriteLine($" ocorrencias do texto: {found.Count}");

            foreach (var at in found.Take(10)) Console.WriteLine($"   0x{at:X}");

            if (found.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine(" o texto nao esta na memoria como UTF-16.");

                return 0;
            }

            // The hits are unaligned, which says the phrase sits INSIDE a
            // longer string rather than being one. Nothing points at the middle
            // of a string, so each hit is walked back to its start first.
            var starts = found.Select(at => Start(handle, at)).Distinct().ToList();

            Console.WriteLine();
            Console.WriteLine($" inicios de string distintos: {starts.Count}");

            foreach (var at in starts.Take(6))
            {
                Read(handle, at, 220, out var head);

                var preview = Encoding.Unicode.GetString(head).Replace((char)10, (char)124);
                var cut = preview.IndexOf((char)0);

                if (cut > 0) preview = preview[..cut];

                Console.WriteLine($"   0x{at:X}  {preview}");
            }

            Console.WriteLine();
            Console.WriteLine(" procurando quem aponta para eles...");

            var owners = Owners(handle, regions, starts, limit: 40);

            Console.WriteLine($" ponteiros encontrados: {owners.Count}");
            Console.WriteLine();

            foreach (var (holder, target) in owners.Take(20))
            {
                Console.WriteLine($"   0x{holder:X} -> 0x{target:X}");

                // Walk back from the pointer looking for an object whose Self
                // field points at itself. If one is there, the distance back is
                // the offset the text lives at.
                for (var back = 0; back <= 0x800; back += 8)
                {
                    var candidate = holder - back;

                    if (!Read(handle, candidate + UiSelf, 8, out var self)) continue;
                    if (BitConverter.ToInt64(self) != candidate) continue;

                    // Where it is drawn, and how many lines it holds. Together
                    // those give a per-line rectangle without ever finding a
                    // per-line element: the block is one string, split by
                    // newlines, in a box of known height.
                    var box = Rect(handle, candidate);

                    Read(handle, target, 4000, out var whole);

                    var full = Encoding.Unicode.GetString(whole);
                    var end = full.IndexOf((char)0);

                    if (end > 0) full = full[..end];

                    var lines = full.Split((char)10).Length;

                    Console.WriteLine(
                        $"      ELEMENTO 0x{candidate:X}  texto em +0x{back:X}  " +
                        $"rect ({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0}  {lines} linhas");

                    break;
                }
            }

            Console.WriteLine();
        }
        finally
        {
            CloseHandle(handle);
        }

        return 0;
    }

    /// <summary>
    /// An element's absolute rectangle, summed up its parents.
    /// </summary>
    /// <remarks>
    /// The same walk the overlay does, repeated here rather than shared because
    /// this reaches the client through its own handle: the point of this tool is
    /// to answer a question the product cannot yet ask.
    /// </remarks>
    private static (float X, float Y, float W, float H) Rect(nint handle, nint element)
    {
        const int relative = 0x100;
        const int parent = 0x0B8;
        const int width = 0x270;
        const int height = 0x274;

        float x = 0, y = 0;

        var node = element;

        for (var depth = 0; depth < 32 && node != 0; depth++)
        {
            if (!Read(handle, node + relative, 8, out var position)) break;

            x += BitConverter.ToSingle(position, 0);
            y += BitConverter.ToSingle(position, 4);

            if (!Read(handle, node + parent, 8, out var up)) break;

            var next = (nint)BitConverter.ToInt64(up);

            if (next == node) break;

            node = next;
        }

        Read(handle, element + width, 4, out var w);
        Read(handle, element + height, 4, out var h);

        return (x, y, BitConverter.ToSingle(w), BitConverter.ToSingle(h));
    }

    /// <summary>
    /// Where the UTF-16 string containing this address begins.
    /// </summary>
    /// <remarks>
    /// A pointer names the start of a string, never the middle of one, so a hit
    /// found inside a longer block has to be walked back before asking who holds
    /// it. Bounded: a runaway scan over a region with no terminator would read
    /// backwards forever.
    /// </remarks>
    private static nint Start(nint handle, nint hit)
    {
        var at = hit;

        for (var back = 0; back < 8192; back += 2)
        {
            var candidate = hit - back;

            if (!Read(handle, candidate, 2, out var pair)) break;
            if (pair[0] == 0 && pair[1] == 0) return candidate + 2;

            at = candidate;
        }

        return at;
    }

    /// <summary>Committed, readable, and not enormous.</summary>
    private static List<(nint Start, long Size)> Regions(nint handle)
    {
        var regions = new List<(nint, long)>();
        var address = (nint)0x10000;
        var size = Marshal.SizeOf<MemoryBasicInformation>();

        while ((long)address < 0x00007FFFFFFFFFFF && regions.Count < 8192)
        {
            if (VirtualQueryEx(handle, address, out var info, size) == 0) break;

            var length = (long)info.RegionSize;

            if (length <= 0) break;

            var readable = info.State == MemCommit
                        && (info.Protect & PageNoAccess) == 0
                        && (info.Protect & PageGuard) == 0;

            if (readable && length <= 256L * 1024 * 1024) regions.Add((info.BaseAddress, length));

            address = (nint)((long)info.BaseAddress + length);
        }

        return regions;
    }

    private static List<nint> Scan(
        nint handle, List<(nint Start, long Size)> regions, byte[] needle, int limit)
    {
        var found = new List<nint>();
        var buffer = new byte[1 << 20];

        foreach (var (start, size) in regions)
        {
            for (long at = 0; at < size && found.Count < limit; at += buffer.Length - needle.Length)
            {
                var take = (int)Math.Min(buffer.Length, size - at);

                if (take < needle.Length) break;

                if (!ReadProcessMemory(handle, start + (nint)at, buffer, take, out var read) ||
                    read < needle.Length)
                    continue;

                var span = buffer.AsSpan(0, read);

                for (var i = 0; i + needle.Length <= span.Length; i++)
                {
                    if (!span.Slice(i, needle.Length).SequenceEqual(needle)) continue;

                    found.Add(start + (nint)at + i);

                    if (found.Count >= limit) break;
                }
            }

            if (found.Count >= limit) break;
        }

        return found;
    }

    /// <summary>Every place holding a pointer to one of the strings.</summary>
    private static List<(nint Holder, nint Target)> Owners(
        nint handle, List<(nint Start, long Size)> regions, List<nint> targets, int limit)
    {
        var wanted = new HashSet<long>(targets.Select(t => (long)t));
        var owners = new List<(nint, nint)>();
        var buffer = new byte[1 << 20];

        foreach (var (start, size) in regions)
        {
            for (long at = 0; at < size && owners.Count < limit; at += buffer.Length)
            {
                var take = (int)Math.Min(buffer.Length, size - at);

                if (take < 8) break;

                if (!ReadProcessMemory(handle, start + (nint)at, buffer, take, out var read) || read < 8)
                    continue;

                for (var i = 0; i + 8 <= read; i += 8)
                {
                    var value = BitConverter.ToInt64(buffer, i);

                    if (!wanted.Contains(value)) continue;

                    owners.Add((start + (nint)at + i, (nint)value));

                    if (owners.Count >= limit) break;
                }
            }

            if (owners.Count >= limit) break;
        }

        return owners;
    }

    private static bool Read(nint handle, nint at, int length, out byte[] bytes)
    {
        bytes = new byte[length];

        return ReadProcessMemory(handle, at, bytes, length, out var read) && read == length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public int AllocationProtect;
        public nint RegionSize;
        public int State;
        public int Protect;
        public int Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        nint process, nint address, [Out] byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(
        nint process, nint address, out MemoryBasicInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
