namespace XinputG84HE.Models;

/// <summary>
/// Maps the keyboard's raw vendor key IDs (its internal scan-matrix
/// addresses, as seen in the analog HID reports) to human-readable key
/// names. This is independent of Win32 virtual-key codes - it's used purely
/// for display (e.g. the live actuation overlay), not for binding, since
/// binding pairs a VK with a raw key ID directly at capture time.
/// </summary>
public static class KnownKeyNames
{
    private static readonly Dictionary<ushort, string> Names = new()
    {
        { 0x00, "Esc" }, { 0x01, "~`" }, { 0x02, "Tab" }, { 0x03, "Caps Lock" },
        { 0x04, "LShift" }, { 0x05, "LCtrl" }, { 0x06, "F1" },
        { 0x07, "1" }, { 0x0D, "2" }, { 0x13, "3" }, { 0x19, "4" }, { 0x1F, "5" },
        { 0x25, "6" }, { 0x2B, "7" }, { 0x31, "8" }, { 0x37, "9" }, { 0x3D, "0" },
        { 0x0B, "Win" }, { 0x0C, "F2" }, { 0x12, "F3" }, { 0x18, "F4" }, { 0x1E, "F5" },
        { 0x24, "F6" }, { 0x2A, "F7" }, { 0x30, "F8" }, { 0x36, "F9" }, { 0x3C, "F10" },
        { 0x42, "F11" }, { 0x48, "F12" },
        { 0x4E, "Del" }, { 0x54, "Print" }, { 0x5A, "Pause" }, { 0x5B, "PgUp" }, { 0x5C, "PgDn" },
        { 0x55, "Home" }, { 0x56, "End" },
        { 0x58, "Up" }, { 0x59, "Down" }, { 0x53, "Left" }, { 0x5F, "Right" },
        { 0x41, "RAlt" }, { 0x11, "LAlt" }, { 0x50, "\\|" }, { 0x51, "Enter" },
        { 0x52, "RShift" }, { 0x4D, "RCtrl" }, { 0x47, "Fn" }, { 0x29, "Space" }, { 0x4F, "Backspace" },
        { 0x08, "Q" }, { 0x0E, "W" }, { 0x14, "E" }, { 0x1A, "R" }, { 0x20, "T" },
        { 0x26, "Y" }, { 0x2C, "U" }, { 0x32, "I" }, { 0x38, "O" }, { 0x3E, "P" },
        { 0x09, "A" }, { 0x0F, "S" }, { 0x15, "D" }, { 0x1B, "F" }, { 0x21, "G" },
        { 0x27, "H" }, { 0x2D, "J" }, { 0x33, "K" }, { 0x39, "L" },
        { 0x10, "Z" }, { 0x16, "X" }, { 0x1C, "C" }, { 0x22, "V" }, { 0x28, "B" },
        { 0x2E, "N" }, { 0x34, "M" },
        { 0x3A, ",<" }, { 0x40, ".>" }, { 0x46, "/?" }, { 0x3F, ";:" }, { 0x45, "'\"" },
        { 0x44, "[{" }, { 0x4A, "]}" }, { 0x43, "-_" }, { 0x49, "=+" },
    };

    public static string GetName(ushort keyId) =>
        Names.TryGetValue(keyId, out string? name) ? name : $"Key 0x{keyId:X2}";
}
