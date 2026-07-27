namespace XinputG84HE.Models;

/// <summary>
/// A physical keyboard key, as learned during bind mode: the raw vendor key
/// ID (used to read actuation depth from the analog HID interface) paired
/// with its standard Win32 virtual-key code (used by the keyboard hook to
/// block it). Both are captured from the same key press at bind time, since
/// there's no static table mapping one to the other.
/// </summary>
public sealed record KeyBinding(ushort RawKeyId, int VirtualKey, string DisplayName);

/// <summary>
/// A saved, nameable preset: which keyboard key drives each controller
/// input, plus per-profile tuning. Serialized directly to JSON.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "Default";

    /// <summary>Controller input -> bound key. Absent/null entries are unbound.</summary>
    public Dictionary<ControllerInput, KeyBinding?> Bindings { get; set; } = new();

    /// <summary>0.0-1.0 fraction of travel ignored at the start of each analog input.</summary>
    public double Deadzone { get; set; } = 0.05;

    /// <summary>0.0-1.0 fraction of travel at which digital buttons register as pressed.</summary>
    public double DigitalThreshold { get; set; } = 0.50;

    /// <summary>Whether mapped keys are swallowed system-wide while this profile is active and emulation is on.</summary>
    public bool BlockMappedKeys { get; set; } = true;

    /// <summary>When true, use radial/circular deadzone for joysticks rather than axial (square). False by default.</summary>
    public bool UseRadialDeadzone { get; set; } = false;

    /// <summary>When true, this preset's bindings and tuning can't be changed until unlocked.</summary>
    public bool Locked { get; set; }

    /// <summary>11 points defining the response curve (0-100) for the left stick.</summary>
    public double[] LeftStickCurve { get; set; } = new double[11] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    /// <summary>11 points defining the response curve (0-100) for the right stick.</summary>
    public double[] RightStickCurve { get; set; } = new double[11] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    public KeyBinding? GetBinding(ControllerInput input) =>
        Bindings.TryGetValue(input, out KeyBinding? binding) ? binding : null;

    public ushort? GetRawKeyId(ControllerInput input) => GetBinding(input)?.RawKeyId;
}
