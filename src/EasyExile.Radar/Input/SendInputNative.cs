using System.Runtime.InteropServices;

namespace EasyExile.Radar.Input;

/// <summary>
/// Ported verbatim from POE2Radar (MIT), <c>Input/SendInputNative.cs</c>, with
/// only the namespace and the gating reference changed.
/// </summary>
/// <remarks>
/// Minimal keyboard input via Win32 <c>SendInput</c>, scancode-based (KEYEVENTF_SCANCODE) which
/// games read more reliably than virtual-key events. Used by the auto-flask feature; all firing
/// is gated by <c>AutoPotion</c> (foreground + in-game + cooldown + kill-switch).
///
/// This is the ONE place in the product that writes anything anywhere. It sends
/// an ordinary keystroke to whatever has focus, exactly as a keyboard does — no
/// injection, no hooks, no PostMessage to reach an unfocused window, and nothing
/// written to the client's memory. Everything else in EasyExile only reads.
/// </remarks>
public static class SendInputNative
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>Press and release a virtual key (e.g. 0x31 = '1') as a scancode keystroke.</summary>
    /// <summary>Set in tests, so the gating can be exercised without a keyboard.</summary>
    public static Action<ushort>? Intercept { get; set; }

    public static void Tap(ushort vk)
    {
        if (Intercept is { } intercept)
        {
            intercept(vk);
            return;
        }

        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        if (scan == 0) return;

        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP };

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
}
