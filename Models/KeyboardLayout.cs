namespace XinputG84HE.Models;

public sealed record KeyboardKey(ushort RawKeyId, string Label, double X, double Y, double Width = 36, double Height = 36);

/// <summary>The G84HE's physical layout, using the existing vendor scan-matrix IDs.</summary>
public static class KeyboardLayout
{
    public const double Width = 744;
    public const double Height = 261;

    public static IReadOnlyList<KeyboardKey> Keys { get; } = Array.AsReadOnly(new KeyboardKey[]
    {
        // Function row.
        new(0x00, "Esc", 0, 0),
        new(0x06, "F1", 57, 0), new(0x0C, "F2", 100, 0), new(0x12, "F3", 144, 0), new(0x18, "F4", 187, 0),
        new(0x1E, "F5", 240, 0), new(0x24, "F6", 284, 0), new(0x2A, "F7", 328, 0), new(0x30, "F8", 371, 0),
        new(0x36, "F9", 425, 0), new(0x3C, "F10", 468, 0), new(0x42, "F11", 512, 0), new(0x48, "F12", 555, 0),
        new(0x4E, "Del", 611, 0), new(0x54, "PrtSc", 664, 0), new(0x5A, "Pause", 707, 0),

        // Number row.
        new(0x01, "` ~", 0, 52), new(0x07, "1 !", 45, 52), new(0x0D, "2 @", 88, 52), new(0x13, "3 #", 132, 52),
        new(0x19, "4 $", 175, 52), new(0x1F, "5 %", 219, 52), new(0x25, "6 ^", 262, 52), new(0x2B, "7 &", 306, 52),
        new(0x31, "8 *", 350, 52), new(0x37, "9 (", 393, 52), new(0x3D, "0 )", 436, 52),
        new(0x43, "- _", 481, 52), new(0x49, "= +", 524, 52), new(0x4F, "Backspace", 566, 52, 80),
        new(0x55, "Home", 663, 52), new(0x5B, "PgUp", 707, 52),

        // QWERTY row.
        new(0x02, "Tab", 0, 95, 60),
        new(0x08, "Q", 68, 95), new(0x0E, "W", 111, 95), new(0x14, "E", 155, 95), new(0x1A, "R", 199, 95),
        new(0x20, "T", 242, 95), new(0x26, "Y", 285, 95), new(0x2C, "U", 329, 95), new(0x32, "I", 373, 95),
        new(0x38, "O", 416, 95), new(0x3E, "P", 460, 95), new(0x44, "[ {", 504, 95), new(0x4A, "] }", 547, 95),
        new(0x50, "\\ |", 590, 95, 56), new(0x56, "End", 663, 95), new(0x5C, "PgDn", 707, 95),

        // Home row.
        new(0x03, "Caps", 0, 138, 66),
        new(0x09, "A", 74, 138), new(0x0F, "S", 118, 138), new(0x15, "D", 161, 138), new(0x1B, "F", 204, 138),
        new(0x21, "G", 248, 138), new(0x27, "H", 292, 138), new(0x2D, "J", 335, 138), new(0x33, "K", 380, 138),
        new(0x39, "L", 423, 138), new(0x3F, "; :", 467, 138), new(0x45, "' \"", 511, 138),
        new(0x51, "Enter", 554, 138, 92),

        // Shift row and the inverted-T arrow cluster.
        new(0x04, "Shift", 0, 181, 84),
        new(0x10, "Z", 92, 181), new(0x16, "X", 136, 181), new(0x1C, "C", 180, 181), new(0x22, "V", 224, 181),
        new(0x28, "B", 268, 181), new(0x2E, "N", 311, 181), new(0x34, "M", 355, 181),
        new(0x3A, ", <", 399, 181), new(0x40, ". >", 442, 181), new(0x46, "/ ?", 486, 181),
        new(0x52, "Shift", 529, 181, 117), new(0x58, "↑", 664, 181),

        // Bottom row.
        new(0x05, "Ctrl", 0, 225, 43), new(0x0B, "Win", 52, 225, 43), new(0x11, "Alt", 102, 225, 43),
        new(0x29, "Space", 153, 225, 290), new(0x41, "Alt", 451, 225, 43), new(0x47, "Fn", 503, 225, 42),
        new(0x4D, "Ctrl", 553, 225, 42), new(0x53, "←", 620, 225), new(0x59, "↓", 663, 225), new(0x5F, "→", 707, 225),
    });
}
