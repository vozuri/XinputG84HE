namespace XinputG84HE.Models;

/// <summary>
/// Every physical Xbox 360 controller input that can be bound to a keyboard
/// key. Digital buttons fire once actuation crosses ~50% travel. Stick
/// directions and triggers are analog - the raw actuation depth (0..1) drives
/// the axis/trigger value directly, so partial presses give partial input.
/// </summary>
public enum ControllerInput
{
    // Face buttons
    A, B, X, Y,

    // Shoulders
    LeftBumper, RightBumper,

    // Menu buttons
    Back, Start, Guide,

    // D-pad (digital)
    DpadUp, DpadDown, DpadLeft, DpadRight,

    // Stick clicks (digital)
    LeftStickClick, RightStickClick,

    // Left stick directions (analog)
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,

    // Right stick directions (analog)
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight,

    // Triggers (analog)
    LeftTrigger, RightTrigger,
}

public static class ControllerInputInfo
{
    /// <summary>True for stick directions and triggers - these use the raw actuation depth as an analog value.</summary>
    public static bool IsAnalog(ControllerInput input) => input switch
    {
        ControllerInput.LeftStickUp or ControllerInput.LeftStickDown or
        ControllerInput.LeftStickLeft or ControllerInput.LeftStickRight or
        ControllerInput.RightStickUp or ControllerInput.RightStickDown or
        ControllerInput.RightStickLeft or ControllerInput.RightStickRight or
        ControllerInput.LeftTrigger or ControllerInput.RightTrigger => true,
        _ => false,
    };

    /// <summary>Short label shown on the controller visual and in the bindings list.</summary>
    public static string ShortLabel(ControllerInput input) => input switch
    {
        ControllerInput.A => "A",
        ControllerInput.B => "B",
        ControllerInput.X => "X",
        ControllerInput.Y => "Y",
        ControllerInput.LeftBumper => "LB",
        ControllerInput.RightBumper => "RB",
        ControllerInput.Back => "View",
        ControllerInput.Start => "Menu",
        ControllerInput.Guide => "Guide",
        ControllerInput.DpadUp => "D-Up",
        ControllerInput.DpadDown => "D-Down",
        ControllerInput.DpadLeft => "D-Left",
        ControllerInput.DpadRight => "D-Right",
        ControllerInput.LeftStickClick => "L3",
        ControllerInput.RightStickClick => "R3",
        ControllerInput.LeftStickUp => "LS ↑",
        ControllerInput.LeftStickDown => "LS ↓",
        ControllerInput.LeftStickLeft => "LS ←",
        ControllerInput.LeftStickRight => "LS →",
        ControllerInput.RightStickUp => "RS ↑",
        ControllerInput.RightStickDown => "RS ↓",
        ControllerInput.RightStickLeft => "RS ←",
        ControllerInput.RightStickRight => "RS →",
        ControllerInput.LeftTrigger => "LT",
        ControllerInput.RightTrigger => "RT",
        _ => input.ToString(),
    };
}
