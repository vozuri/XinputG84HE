namespace XinputG84HE.Services;

public static class DeviceConnection
{
    // Corrected from the keyboard/dongle IDs reported by the user.
    public const ushort WiredProductId = 0x5030;
    public const ushort WirelessProductId = 0x5038;

    public static string Describe(IEnumerable<string> paths)
    {
        bool wireless = false;
        foreach (string path in paths)
        {
            if (path.Contains("pid_5030", StringComparison.OrdinalIgnoreCase))
                return "Wired"; // Wired takes priority when the dongle is also present.
            wireless |= path.Contains("pid_5038", StringComparison.OrdinalIgnoreCase);
        }
        return wireless ? "Wireless" : "Offline";
    }
}
