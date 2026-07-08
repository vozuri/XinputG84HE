using System.Threading;
using XinputG84HE.Models;

namespace XinputG84HE.Services;

/// <summary>
/// "Press a key to bind it" logic. There's no static table mapping the
/// keyboard's vendor-specific analog key IDs to standard Win32 virtual-key
/// codes, so instead we watch both event streams at once during a single
/// capture window and pair whichever VK and whichever meaningfully-actuated
/// key ID show up together - reliable as long as exactly one key is pressed,
/// which is what the UI asks the user to do.
///
/// Escape explicitly clears the binding; right-click (or a timeout) cancels
/// without changing anything.
/// </summary>
public enum CaptureOutcome { Success, Unbind, Cancelled }

public sealed record CaptureResult(CaptureOutcome Outcome, KeyBinding? Binding)
{
    public static readonly CaptureResult Cancelled = new(CaptureOutcome.Cancelled, null);
    public static readonly CaptureResult Unbind = new(CaptureOutcome.Unbind, null);
}

public sealed class BindingCaptureService
{
    private const ushort MinCaptureActuation = 40; // filters accidental light grazes
    private const int VK_ESCAPE = 0x1B;

    private readonly KeyboardHookService _hook;
    private readonly HidDeviceService _hid;

    private TaskCompletionSource<CaptureResult>? _pending;
    private int? _seenVk;
    private ushort? _seenKeyId;
    private readonly object _lock = new();

    public BindingCaptureService(KeyboardHookService hook, HidDeviceService hid)
    {
        _hook = hook;
        _hid = hid;
        _hook.RawKeyDown += OnRawKeyDown;
        _hid.KeyReport += OnKeyReport;
    }

    /// <summary>
    /// Waits for the next key press and reports what happened: a successful
    /// capture, an explicit unbind (Escape), or a cancellation (right-click,
    /// via <see cref="CancelCapture"/>, or a timeout). Only one capture can
    /// be in flight at a time.
    /// </summary>
    public async Task<CaptureResult> CaptureNextKeyAsync(TimeSpan timeout)
    {
        lock (_lock)
        {
            _seenVk = null;
            _seenKeyId = null;
            _pending = new TaskCompletionSource<CaptureResult>();
        }

        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            lock (_lock)
            {
                _pending?.TrySetResult(CaptureResult.Cancelled);
            }
        });

        return await _pending.Task;
    }

    /// <summary>Cancels an in-progress capture without changing anything (e.g. right mouse button). No-op if nothing is being captured.</summary>
    public void CancelCapture()
    {
        lock (_lock)
        {
            _pending?.TrySetResult(CaptureResult.Cancelled);
        }
    }

    private void OnRawKeyDown(int vk)
    {
        lock (_lock)
        {
            if (_pending is null)
            {
                return;
            }

            if (vk == VK_ESCAPE)
            {
                _pending.TrySetResult(CaptureResult.Unbind);
                return;
            }

            _seenVk = vk;
            TryComplete();
        }
    }

    private void OnKeyReport(ushort keyId, ushort value)
    {
        if (value < MinCaptureActuation)
        {
            return;
        }

        lock (_lock)
        {
            if (_pending is null)
            {
                return;
            }
            _seenKeyId = keyId;
            TryComplete();
        }
    }

    /// <summary>Must be called while holding _lock.</summary>
    private void TryComplete()
    {
        if (_seenVk.HasValue && _seenKeyId.HasValue && _pending is not null)
        {
            var binding = new KeyBinding(_seenKeyId.Value, _seenVk.Value, VirtualKeyNames.Describe(_seenVk.Value));
            _pending.TrySetResult(new CaptureResult(CaptureOutcome.Success, binding));
            _pending = null;
        }
    }
}

/// <summary>Best-effort human-readable names for common virtual-key codes, for display only.</summary>
public static class VirtualKeyNames
{
    private static readonly Dictionary<int, string> Names = BuildMap();

    public static string Describe(int vk) => Names.TryGetValue(vk, out string? name) ? name : $"VK 0x{vk:X2}";

    private static Dictionary<int, string> BuildMap()
    {
        var map = new Dictionary<int, string>();

        void Add(int vk, string name) => map[vk] = name;

        for (char c = 'A'; c <= 'Z'; c++)
        {
            Add(c, c.ToString());
        }
        for (char c = '0'; c <= '9'; c++)
        {
            Add(c, c.ToString());
        }
        for (int i = 1; i <= 24; i++)
        {
            Add(0x70 + (i - 1), $"F{i}");
        }

        Add(0x20, "Space");
        Add(0x0D, "Enter");
        Add(0x09, "Tab");
        Add(0x1B, "Esc");
        Add(0x08, "Backspace");
        Add(0x2E, "Delete");
        Add(0x2D, "Insert");
        Add(0x24, "Home");
        Add(0x23, "End");
        Add(0x21, "Page Up");
        Add(0x22, "Page Down");
        Add(0x25, "Left");
        Add(0x26, "Up");
        Add(0x27, "Right");
        Add(0x28, "Down");
        Add(0x14, "Caps Lock");
        Add(0x10, "Shift");
        Add(0xA0, "Left Shift");
        Add(0xA1, "Right Shift");
        Add(0x11, "Ctrl");
        Add(0xA2, "Left Ctrl");
        Add(0xA3, "Right Ctrl");
        Add(0x12, "Alt");
        Add(0xA4, "Left Alt");
        Add(0xA5, "Right Alt");
        Add(0xC0, "`");
        Add(0xBD, "-");
        Add(0xBB, "=");
        Add(0xDB, "[");
        Add(0xDD, "]");
        Add(0xDC, "\\");
        Add(0xBA, ";");
        Add(0xDE, "'");
        Add(0xBC, ",");
        Add(0xBE, ".");
        Add(0xBF, "/");

        return map;
    }
}
