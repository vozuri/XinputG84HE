using System.Runtime.InteropServices;

namespace XinputG84HE.Services;

/// <summary>
/// System-wide low-level keyboard hook. Two independent jobs:
///  1) Optionally swallow specific virtual-key codes (the ones currently
///     bound to a controller input) so they don't also type into whatever
///     window has focus while emulation is running.
///  2) Watch for any number of named, independently configurable hotkeys
///     (toggle emulation, next/previous preset, etc.) - these work even
///     while other keys are being blocked, since the hook sees every
///     keystroke before Windows delivers it anywhere else.
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    public const int ModShift = 0x1;
    public const int ModControl = 0x2;
    public const int ModAlt = 0x4;
    public const int ModWin = 0x8;

    private readonly NativeHook.LowLevelKeyboardProc _proc;
    private readonly Dictionary<string, (int modifiers, int vk)> _hotkeys = new();
    private IntPtr _hookId = IntPtr.Zero;
    private bool _ctrlDown, _altDown, _shiftDown, _winDown;
    private Action<int, int>? _hotkeyCaptureCallback;

    /// <summary>Virtual-key codes that get swallowed while <see cref="BlockingEnabled"/> is true.</summary>
    public HashSet<int> BlockedVirtualKeys { get; } = new();

    public bool BlockingEnabled { get; set; }

    /// <summary>Fires for every key-down, blocked or not - used by the bind-capture flow to learn which VK corresponds to a physical key.</summary>
    public event Action<int>? RawKeyDown;

    /// <summary>Fires with the hotkey's name when a configured combo is pressed.</summary>
    public event Action<string>? HotkeyPressed;

    public KeyboardHookService()
    {
        _proc = HookCallback;
    }

    public void SetHotkey(string name, int modifiers, int vk) => _hotkeys[name] = (modifiers, vk);

    public (int modifiers, int vk)? GetHotkey(string name) =>
        _hotkeys.TryGetValue(name, out (int modifiers, int vk) combo) ? combo : null;

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule!;
        IntPtr moduleHandle = NativeHook.GetModuleHandle(module.ModuleName);
        _hookId = NativeHook.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeHook.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Waits for the next non-modifier key press and reports it (plus whichever
    /// modifiers were held) via <paramref name="callback"/>, then stops
    /// listening. Used to let the user set a new hotkey by just pressing it.
    /// </summary>
    public void CaptureNextHotkey(Action<int, int> callback)
    {
        _hotkeyCaptureCallback = callback;
    }

    public void CancelHotkeyCapture() => _hotkeyCaptureCallback = null;

    public static string DescribeCombo(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(VirtualKeyNames.Describe(vk));
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(int vk) =>
        vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5; // left/right specific variants

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field
            long msg = wParam.ToInt64();
            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;

            if (vk is VK_CONTROL or 0xA2 or 0xA3)
            {
                _ctrlDown = isDown || (!isUp && _ctrlDown);
            }
            if (vk is VK_MENU or 0xA4 or 0xA5)
            {
                _altDown = isDown || (!isUp && _altDown);
            }
            if (vk is VK_SHIFT or 0xA0 or 0xA1)
            {
                _shiftDown = isDown || (!isUp && _shiftDown);
            }
            if (vk is VK_LWIN or VK_RWIN)
            {
                _winDown = isDown || (!isUp && _winDown);
            }

            if (isDown)
            {
                if (_hotkeyCaptureCallback is not null && !IsModifierKey(vk))
                {
                    int mods = (_ctrlDown ? ModControl : 0) | (_altDown ? ModAlt : 0) | (_shiftDown ? ModShift : 0) | (_winDown ? ModWin : 0);
                    Action<int, int> callback = _hotkeyCaptureCallback;
                    _hotkeyCaptureCallback = null;
                    callback.Invoke(mods, vk);
                    return (IntPtr)1;
                }

                int currentMods = (_ctrlDown ? ModControl : 0) | (_altDown ? ModAlt : 0) | (_shiftDown ? ModShift : 0) | (_winDown ? ModWin : 0);

                foreach ((string name, (int modifiers, int hotkeyVk) combo) in _hotkeys)
                {
                    if (vk == combo.hotkeyVk && currentMods == combo.modifiers)
                    {
                        HotkeyPressed?.Invoke(name);
                        return (IntPtr)1;
                    }
                }

                RawKeyDown?.Invoke(vk);

                if (BlockingEnabled && BlockedVirtualKeys.Contains(vk))
                {
                    return (IntPtr)1;
                }
            }
            else if (isUp && BlockingEnabled && BlockedVirtualKeys.Contains(vk))
            {
                return (IntPtr)1;
            }
        }

        return NativeHook.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

internal static class NativeHook
{
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
