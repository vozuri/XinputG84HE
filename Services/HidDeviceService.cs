using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace XinputG84HE.Services;

/// <summary>
/// Finds and reads the keyboard's analog HID interface (wired or 2.4GHz,
/// whichever is actually live), decodes raw actuation reports, and can send
/// the "enable simulation mode" command that turns analog reporting on.
///
/// Device: VID 0x3151, PID 0x5038 (wired) or 0x5030 (2.4GHz dongle).
/// Analog interface: usage page 0xFFFF, usage 0x01, 32-byte reports
///   [reportId=0x05][0x1B][value:u16 LE][keyId:u16 LE][pad...]
/// Control interface (simulation mode toggle): usage page 0xFFFF, usage 0x02,
///   SET_REPORT (Feature, Report ID 0), 64 bytes: 1B 01 00 00 00 00 00 E3 + zero padding.
/// 1 raw actuation unit ~= 0.01mm; ~350 raw units = fully bottomed out
/// (Gateron Magnetic Jade Pro, 3.5mm travel).
/// </summary>
public sealed class HidDeviceService
{
    public const ushort MaxRawTravel = 350;

    // Hall-effect sensors report tiny non-zero noise (1-2 raw units) even
    // when a key is fully released - they almost never settle at a bit-for-bit
    // 0. Anything at or below this floor is treated as "released" rather than
    // a genuine (if extremely light) press. Without this, a key that never
    // quite reports a literal 0 stays in _activeKeys forever and shows up in
    // the live actuation overlay stuck at 0%.
    private const ushort NoiseFloor = 3;

    private const ushort VendorId = 0x3151;
    private static readonly ushort[] ProductIds = { 0x5038, 0x5030 };
    private const ushort AnalogUsagePage = 0xFFFF;
    private const ushort AnalogUsage = 0x01;
    private const ushort ControlUsage = 0x02;
    private const int ExpectedInputReportLength = 32;

    private readonly Dictionary<ushort, ushort> _activeKeys = new();
    private readonly Dictionary<ushort, (ushort value, DateTime since)> _valueTracking = new();
    private readonly object _lock = new();
    private readonly HashSet<string> _activeDevicePaths = new();

    // If a key's raw actuation value stays bit-for-bit identical (and isn't
    // fully bottomed out) for longer than AntiGhostSeconds, treat it as a
    // stuck sensor/report rather than a genuine sustained press, and ignore
    // it until it changes. Configurable/toggleable from the UI ("Anti Ghost
    // Input").
    public bool AntiGhostEnabled { get; set; } = false;
    public double AntiGhostSeconds { get; set; } = 2.5;

    /// <summary>Raised whenever the "any device connected" state changes.</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>Raised for status/log messages the UI can show in a log panel.</summary>
    public event Action<string>? Log;

    /// <summary>Raised on every decoded report: (keyId, rawValue).</summary>
    public event Action<ushort, ushort>? KeyReport;

    public bool IsConnected
    {
        get { lock (_lock) { return _activeDevicePaths.Count > 0; } }
    }

    public void Start()
    {
        new Thread(SupervisorLoop) { IsBackground = true, Name = "HidDeviceService" }.Start();
    }

    public ushort GetRaw(ushort keyId)
    {
        lock (_lock)
        {
            return GetRawLocked(keyId);
        }
    }

    /// <summary>Must be called while already holding _lock.</summary>
    private ushort GetRawLocked(ushort keyId)
    {
        if (!_activeKeys.TryGetValue(keyId, out ushort value))
        {
            return 0;
        }

        if (AntiGhostEnabled
            && value != MaxRawTravel
            && _valueTracking.TryGetValue(keyId, out (ushort value, DateTime since) tracked)
            && tracked.value == value
            && (DateTime.UtcNow - tracked.since).TotalSeconds > AntiGhostSeconds)
        {
            return 0; // stuck at a non-bottomed-out value for too long - ignore it
        }

        return value;
    }

    public double GetNormalized(ushort keyId) => Math.Clamp(GetRaw(keyId) / (double)MaxRawTravel, 0.0, 1.0);

    /// <summary>Raw actuation for every currently-active key, ignoring anti-ghost suppression - used to distinguish a genuine release from a ghost-nullified key in the UI.</summary>
    public Dictionary<ushort, ushort> GetRawActiveKeys()
    {
        lock (_lock)
        {
            return new Dictionary<ushort, ushort>(_activeKeys);
        }
    }

    /// <summary>Key IDs currently being ignored by anti-ghost suppression (stuck at a non-max value for too long).</summary>
    public HashSet<ushort> GetSuppressedKeyIds()
    {
        lock (_lock)
        {
            var result = new HashSet<ushort>();
            foreach (ushort keyId in _activeKeys.Keys)
            {
                if (GetRawLocked(keyId) == 0)
                {
                    result.Add(keyId);
                }
            }
            return result;
        }
    }

    public Dictionary<ushort, ushort> Snapshot()
    {
        lock (_lock)
        {
            var result = new Dictionary<ushort, ushort>();
            foreach (ushort keyId in _activeKeys.Keys)
            {
                ushort effective = GetRawLocked(keyId);
                if (effective > 0)
                {
                    result[keyId] = effective;
                }
            }
            return result;
        }
    }

    /// <summary>Sends the enable-simulation-mode command to every matching control interface. Safe to call any time; harmless if already enabled.</summary>
    public void EnableSimulationMode()
    {
        List<string> controlPaths = FindMatchingDevicePaths(AnalogUsagePage, ControlUsage);
        if (controlPaths.Count == 0)
        {
            Log?.Invoke("Simulation mode: control interface not found (keyboard connected?).");
            return;
        }

        byte[] command = new byte[64];
        command[0] = 0x1B;
        command[1] = 0x01;
        command[7] = 0xE3;

        foreach (string path in controlPaths)
        {
            using SafeFileHandle handle = OpenDevice(path);
            if (handle.IsInvalid)
            {
                Log?.Invoke($"Simulation mode: couldn't open control interface ({path}).");
                continue;
            }

            byte[] withPlaceholder = new byte[65];
            Array.Copy(command, 0, withPlaceholder, 1, command.Length);

            bool ok = NativeHid.HidD_SetFeature(handle, withPlaceholder, (uint)withPlaceholder.Length);
            if (!ok)
            {
                ok = NativeHid.HidD_SetFeature(handle, command, (uint)command.Length);
            }

            Log?.Invoke(ok ? "Simulation mode enabled." : $"Simulation mode: enable command failed (error {Marshal.GetLastWin32Error()}).");
        }
    }

    // =====================================================================
    // Discovery + resilient multi-interface read loop
    // =====================================================================

    private void SupervisorLoop()
    {
        while (true)
        {
            List<string> candidates = FindMatchingDevicePaths(AnalogUsagePage, AnalogUsage, requireInputLength: ExpectedInputReportLength);

            foreach (string path in candidates)
            {
                bool alreadyOpen;
                lock (_lock)
                {
                    alreadyOpen = _activeDevicePaths.Contains(path);
                }
                if (alreadyOpen)
                {
                    continue;
                }

                SafeFileHandle handle = OpenDevice(path);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    Log?.Invoke($"Found keyboard interface but couldn't open it (try running as Administrator).");
                    continue;
                }

                bool wasEmpty;
                lock (_lock)
                {
                    wasEmpty = _activeDevicePaths.Count == 0;
                    _activeDevicePaths.Add(path);
                }

                Log?.Invoke($"Listening on: {path}");
                if (wasEmpty)
                {
                    ConnectionChanged?.Invoke(true);
                }

                EnableSimulationMode();

                new Thread(() => RunReader(path, handle)) { IsBackground = true }.Start();
            }

            Thread.Sleep(1000);
        }
    }

    private void RunReader(string path, SafeFileHandle handle)
    {
        try
        {
            byte[] buffer = new byte[ExpectedInputReportLength];
            while (true)
            {
                bool ok = NativeHid.ReadFile(handle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero);
                if (!ok || bytesRead < 6)
                {
                    if (!ok)
                    {
                        return;
                    }
                    continue;
                }

                if (buffer[0] != 0x05)
                {
                    continue;
                }

                ushort value = BitConverter.ToUInt16(buffer, 2);
                ushort keyId = BitConverter.ToUInt16(buffer, 4);

                lock (_lock)
                {
                    if (value <= NoiseFloor)
                    {
                        _activeKeys.Remove(keyId);
                        _valueTracking.Remove(keyId);
                    }
                    else
                    {
                        _activeKeys[keyId] = value;

                        if (!_valueTracking.TryGetValue(keyId, out (ushort value, DateTime since) tracked) || tracked.value != value)
                        {
                            _valueTracking[keyId] = (value, DateTime.UtcNow);
                        }
                        // else: value unchanged since last report, keep the original "since" timestamp
                    }
                }

                KeyReport?.Invoke(keyId, value);
            }
        }
        finally
        {
            handle.Dispose();

            bool nowEmpty;
            lock (_lock)
            {
                _activeDevicePaths.Remove(path);
                nowEmpty = _activeDevicePaths.Count == 0;
                if (nowEmpty)
                {
                    _activeKeys.Clear();
                    _valueTracking.Clear();
                }
            }

            Log?.Invoke($"Disconnected: {path}");
            if (nowEmpty)
            {
                ConnectionChanged?.Invoke(false);
            }
        }
    }

    private static SafeFileHandle OpenDevice(string devicePath)
    {
        return NativeHid.CreateFile(
            devicePath,
            NativeHid.GENERIC_READ | NativeHid.GENERIC_WRITE,
            NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeHid.OPEN_EXISTING, 0, IntPtr.Zero);
    }

    /// <summary>Finds every present HID interface matching our VID/PID, filtered by usage page/usage (and optionally exact input report length).</summary>
    private static List<string> FindMatchingDevicePaths(ushort usagePage, ushort usage, int? requireInputLength = null)
    {
        var results = new List<string>();

        NativeHid.HidD_GetHidGuid(out Guid hidGuid);

        IntPtr deviceInfoSet = NativeHid.SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeHid.DIGCF_PRESENT | NativeHid.DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return results;
        }

        try
        {
            uint memberIndex = 0;
            while (true)
            {
                var interfaceData = new NativeHid.SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                bool found = NativeHid.SetupDiEnumDeviceInterfaces(
                    deviceInfoSet, IntPtr.Zero, ref hidGuid, memberIndex, ref interfaceData);
                if (!found)
                {
                    break;
                }
                memberIndex++;

                string? path = GetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData);
                if (path is null)
                {
                    continue;
                }

                using SafeFileHandle handle = NativeHid.CreateFile(
                    path, 0,
                    NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeHid.OPEN_EXISTING, 0, IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    continue;
                }

                var attributes = new NativeHid.HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(attributes);
                if (!NativeHid.HidD_GetAttributes(handle, ref attributes))
                {
                    continue;
                }

                if (attributes.VendorID != VendorId || !ProductIds.Contains(attributes.ProductID))
                {
                    continue;
                }

                if (!NativeHid.HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                {
                    continue;
                }

                try
                {
                    if (NativeHid.HidP_GetCaps(preparsedData, out NativeHid.HIDP_CAPS caps) != NativeHid.HIDP_STATUS_SUCCESS)
                    {
                        continue;
                    }

                    if (caps.UsagePage != usagePage || caps.Usage != usage)
                    {
                        continue;
                    }

                    if (requireInputLength is int len && caps.InputReportByteLength != len)
                    {
                        continue;
                    }

                    results.Add(path);
                }
                finally
                {
                    NativeHid.HidD_FreePreparsedData(preparsedData);
                }
            }
        }
        finally
        {
            NativeHid.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static string? GetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref NativeHid.SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        NativeHid.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

        if (requiredSize == 0)
        {
            return null;
        }

        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

            bool ok = NativeHid.SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero);

            return ok ? Marshal.PtrToStringAuto(IntPtr.Add(detailBuffer, 4)) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }
}
