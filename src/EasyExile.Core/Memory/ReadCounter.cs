namespace EasyExile.Core.Memory;

/// <summary>
/// How many times the client has actually been read. A diagnostic, not a
/// feature.
/// </summary>
/// <remarks>
/// Every field read is one <c>ReadProcessMemory</c>, and a syscall is a couple
/// of microseconds whether it fetches four bytes or four hundred. So the honest
/// unit for "why is a capture slow" is not milliseconds — it is the number of
/// crossings. Counting them turns a guess about which step is expensive into a
/// number that can be divided by the entity count.
///
/// A plain increment: the counter is read between captures, not during, and
/// making it interlocked would put a lock prefix in the hottest path in the
/// program to sharpen a number nobody reads at that resolution.
/// </remarks>
internal static class ReadCounter
{
    private static long _reads;

    public static long Reads => _reads;

    public static void Count() => _reads++;

    public static long Since(long mark) => _reads - mark;
}
