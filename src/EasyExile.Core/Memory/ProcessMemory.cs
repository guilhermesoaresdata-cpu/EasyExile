using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyExile.Core.Runtime;

namespace EasyExile.Core.Memory;

/// <summary>
/// Reads the running client through the Win32 API. Region lookups are cached
/// because a query is a syscall and the same pages are asked about constantly;
/// caching the layout is safe in a way caching object identity is not.
/// </summary>
internal sealed class ProcessMemory : IMemoryReader
{
    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;
    private const int MemCommit = 0x1000;
    private const int PageNoAccess = 0x01;
    private const int PageGuard = 0x100;

    private readonly nint _handle;
    private readonly List<MemoryRegion> _regions = new();
    private bool _disposed;

    public ProcessMemory(Process process)
    {
        _handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);

        if (_handle == 0)
            throw new InvalidOperationException(CoreText.ProcessOpenFailed(process.Id));

        ModuleBase = process.MainModule?.BaseAddress ?? 0;
    }

    public nint ModuleBase { get; }

    public bool IsReadable(nint address, int size = 1)
    {
        if (_disposed || address == 0 || size <= 0) return false;

        var start = (long)address;
        var end = start + size;

        var index = FindRegion(start);
        if (index >= 0)
        {
            var cached = _regions[index];
            return cached.Readable && end <= cached.End;
        }

        var querySize = Marshal.SizeOf<MemoryBasicInformation>();
        if (VirtualQueryEx(_handle, address, out var info, querySize) == 0) return false;

        var regionStart = (long)info.BaseAddress;
        var regionEnd = regionStart + (long)info.RegionSize;

        var readable = info.State == MemCommit
                    && (info.Protect & PageNoAccess) == 0
                    && (info.Protect & PageGuard) == 0;

        Remember(regionStart, regionEnd, readable);

        return readable && start >= regionStart && end <= regionEnd;
    }

    public bool TryReadBytes(nint address, int length, out byte[] bytes)
    {
        bytes = new byte[length];

        if (_disposed || address == 0 || length <= 0) return false;

        ReadCounter.Count();

        return ReadProcessMemory(_handle, address, bytes, length, out var read) && read == length;
    }

    public bool TryRead<T>(nint address, out T value) where T : unmanaged
    {
        value = default;

        var size = Marshal.SizeOf<T>();
        if (!TryReadBytes(address, size, out var buffer)) return false;

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

    public string? TryReadUtf8(nint address, int maxLength = 128) =>
        NativeText.ReadUtf8(TryReadBytes, address, maxLength);

    public string? TryReadUtf16(nint address, int maxLength = 128) =>
        NativeText.ReadUtf16(TryReadBytes, address, maxLength);

    public string? TryReadWideString(
        nint wstring, int bufferOffset, int sizeOffset, int capacityOffset, int maxLength = 256) =>
        NativeText.ReadWideString(TryReadBytes, wstring, bufferOffset, sizeOffset, capacityOffset, maxLength);

    private int FindRegion(long address)
    {
        int low = 0, high = _regions.Count - 1;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            var region = _regions[mid];

            if (address < region.Start) high = mid - 1;
            else if (address >= region.End) low = mid + 1;
            else return mid;
        }

        return -1;
    }

    private void Remember(long start, long end, bool readable)
    {
        // Protection can change, so the cache is dropped wholesale rather than
        // grown without bound or trusted forever.
        if (_regions.Count > 4096) _regions.Clear();

        int insert = 0;
        while (insert < _regions.Count && _regions[insert].Start < start) insert++;

        if (insert < _regions.Count && _regions[insert].Start == start) return;

        _regions.Insert(insert, new MemoryRegion(start, end, readable));
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        if (_handle != 0) CloseHandle(_handle);
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
    private static extern nint OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        nint process, nint address, [Out] byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(
        nint process, nint address, out MemoryBasicInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
