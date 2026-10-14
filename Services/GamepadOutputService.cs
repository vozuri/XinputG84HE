using System.Threading;
using XinputG84HE.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace XinputG84HE.Services;

/// <summary>
/// Reads current key actuation from <see cref="HidDeviceService"/>, applies
/// the active profile's bindings/deadzone/threshold, and pushes the result to
/// a virtual Xbox 360 controller via ViGEmBus.
/// </summary>
public sealed class GamepadOutputService : IDisposable
{
    private static readonly Dictionary<ControllerInput, Xbox360Button> ButtonMap = new()
    {
        [ControllerInput.A] = Xbox360Button.A,
        [ControllerInput.B] = Xbox360Button.B,
        [ControllerInput.X] = Xbox360Button.X,
        [ControllerInput.Y] = Xbox360Button.Y,
        [ControllerInput.LeftBumper] = Xbox360Button.LeftShoulder,
        [ControllerInput.RightBumper] = Xbox360Button.RightShoulder,
        [ControllerInput.Back] = Xbox360Button.Back,
        [ControllerInput.Start] = Xbox360Button.Start,
        [ControllerInput.Guide] = Xbox360Button.Guide,
        [ControllerInput.DpadUp] = Xbox360Button.Up,
        [ControllerInput.DpadDown] = Xbox360Button.Down,
        [ControllerInput.DpadLeft] = Xbox360Button.Left,
        [ControllerInput.DpadRight] = Xbox360Button.Right,
        [ControllerInput.LeftStickClick] = Xbox360Button.LeftThumb,
        [ControllerInput.RightStickClick] = Xbox360Button.RightThumb,
    };

    private readonly HidDeviceService _hid;
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private CancellationTokenSource? _cts;
    private volatile Profile? _profile;
    private volatile int _reportRate = 500;

    public event Action<string>? Log;

    public bool IsRunning => _controller is not null;

    public OutputDiagnostics Diagnostics { get; } = new();

    public GamepadOutputService(HidDeviceService hid)
    {
        _hid = hid;
    }

    public void SetReportRate(int hz)
    {
        _reportRate = hz;
    }

    /// <summary>Swaps the active profile live, even while emulation is running.</summary>
    public void SetProfile(Profile profile) => _profile = profile;

    public bool Start(Profile profile)
    {
        if (IsRunning)
        {
            return true;
        }

        _profile = profile;

        try
        {
            _client = new ViGEmClient();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"ViGEmBus not available: {ex.Message}. Install it from https://github.com/ViGEm/ViGEmBus/releases");
            _client = null;
            return false;
        }

        try
        {
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Failed to connect virtual controller: {ex.Message}");
            _client.Dispose();
            _client = null;
            _controller = null;
            return false;
        }

        Diagnostics.BeginRun();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        new Thread(() => UpdateLoop(token)) { IsBackground = true, Name = "GamepadOutput" }.Start();

        Log?.Invoke("Virtual Xbox 360 controller connected.");
        return true;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;

        if (_controller is not null)
        {
            try
            {
                // Explicitly zero everything before disconnecting - if the
                // device disappears while a key is still held, some
                // combination of ViGEmBus/XInput/the game can otherwise keep
                // reporting that last nonzero value as a stuck "ghost" input
                // that persists even across reconnects, until reboot.
                foreach (Xbox360Button button in ButtonMap.Values)
                {
                    _controller.SetButtonState(button, false);
                }
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            }
            catch
            {
                // best-effort only
            }
        }

        try
        {
            _controller?.Disconnect();
        }
        catch
        {
            // already gone, nothing to do
        }
        _controller = null;

        _client?.Dispose();
        _client = null;
    }

    private void UpdateLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int hz = _reportRate;
            if (hz <= 0) hz = 500;

            if (hz <= 1000)
            {
                int delayMs = 1000 / hz;
                if (delayMs < 1) delayMs = 1;
                Thread.Sleep(delayMs);
            }
            else
            {
                Thread.Sleep(0);
            }

            Profile? profile = _profile;
            IXbox360Controller? controller = _controller;
            if (profile is null || controller is null)
            {
                continue;
            }

            try
            {
                foreach ((ControllerInput input, Xbox360Button button) in ButtonMap)
                {
                    ushort? keyId = profile.GetRawKeyId(input);
                    bool pressed = keyId.HasValue && _hid.GetNormalized(keyId.Value) >= profile.DigitalThreshold;
                    controller.SetButtonState(button, pressed);
                }

                var (leftX, leftY, rightX, rightY) = GetStickAxes(profile);

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxisShort(leftX));
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisShort(leftY));
                controller.SetAxisValue(Xbox360Axis.RightThumbX, ToAxisShort(rightX));
                controller.SetAxisValue(Xbox360Axis.RightThumbY, ToAxisShort(rightY));

                controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToTriggerByte(DeadzonedValue(profile, ControllerInput.LeftTrigger)));
                controller.SetSliderValue(Xbox360Slider.RightTrigger, ToTriggerByte(DeadzonedValue(profile, ControllerInput.RightTrigger)));
                Diagnostics.RecordUpdate();
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Gamepad update error: {ex.Message}");
            }
        }
    }

    /// <summary>Shared stick calculation for output and the SnapStick+ live preview.</summary>
    public (double leftX, double leftY, double rightX, double rightY) GetStickAxes(Profile profile)
    {
        double leftX, leftY;
        double rightX, rightY;

        if (profile.UseRadialDeadzone)
        {
            leftX = RawSignedAxis(profile, ControllerInput.LeftStickRight, ControllerInput.LeftStickLeft);
            leftY = RawSignedAxis(profile, ControllerInput.LeftStickUp, ControllerInput.LeftStickDown);
            ApplyCircularMapping(ref leftX, ref leftY);
            ApplyRadialDeadzone(ref leftX, ref leftY, profile.Deadzone, profile.LeftStickCurve);

            rightX = RawSignedAxis(profile, ControllerInput.RightStickRight, ControllerInput.RightStickLeft);
            rightY = RawSignedAxis(profile, ControllerInput.RightStickUp, ControllerInput.RightStickDown);
            ApplyCircularMapping(ref rightX, ref rightY);
            ApplyRadialDeadzone(ref rightX, ref rightY, profile.Deadzone, profile.RightStickCurve);
        }
        else
        {
            leftX = SignedAxis(profile, ControllerInput.LeftStickRight, ControllerInput.LeftStickLeft, profile.LeftStickCurve);
            leftY = SignedAxis(profile, ControllerInput.LeftStickUp, ControllerInput.LeftStickDown, profile.LeftStickCurve);

            rightX = SignedAxis(profile, ControllerInput.RightStickRight, ControllerInput.RightStickLeft, profile.RightStickCurve);
            rightY = SignedAxis(profile, ControllerInput.RightStickUp, ControllerInput.RightStickDown, profile.RightStickCurve);
        }

        return (leftX, leftY, rightX, rightY);
    }

    private double SignedAxis(Profile profile, ControllerInput positive, ControllerInput negative, double[] curve)
    {
        if (profile.SnapStickEnabled)
        {
            var pair = _hid.GetSnapStickPair(profile.GetRawKeyId(positive), profile.GetRawKeyId(negative));
            double value = pair.positive - pair.negative;
            double magnitude = Math.Abs(value);
            if (magnitude <= profile.Deadzone) return 0;
            magnitude = (magnitude - profile.Deadzone) / (1.0 - profile.Deadzone);
            if (curve != null) magnitude = EvaluateCurve(curve, magnitude);
            return Math.Clamp(Math.Sign(value) * magnitude, -1.0, 1.0);
        }

        double pos = DeadzonedValue(profile, positive);
        double neg = DeadzonedValue(profile, negative);
        if (curve != null)
        {
            pos = EvaluateCurve(curve, pos);
            neg = EvaluateCurve(curve, neg);
        }
        return Math.Clamp(pos - neg, -1.0, 1.0);
    }

    private double RawSignedAxis(Profile profile, ControllerInput positive, ControllerInput negative)
    {
        if (profile.SnapStickEnabled)
        {
            var pair = _hid.GetSnapStickPair(profile.GetRawKeyId(positive), profile.GetRawKeyId(negative));
            return pair.positive - pair.negative;
        }

        double pos = RawValue(profile, positive);
        double neg = RawValue(profile, negative);
        return Math.Clamp(pos - neg, -1.0, 1.0);
    }

    private double RawValue(Profile profile, ControllerInput input)
    {
        ushort? keyId = profile.GetRawKeyId(input);
        return keyId.HasValue ? _hid.GetNormalized(keyId.Value) : 0.0;
    }

    /// <summary>
    /// Applies a radial deadzone to the 2D vector, rescaling the remaining travel to 0..1
    /// </summary>
    private static void ApplyRadialDeadzone(ref double x, ref double y, double deadzone, double[] curve)
    {
        double length = Math.Sqrt(x * x + y * y);
        if (length <= deadzone)
        {
            x = 0.0;
            y = 0.0;
            return;
        }

        double rescaledLength = (length - deadzone) / (1.0 - deadzone);
        rescaledLength = Math.Min(rescaledLength, 1.0);

        if (curve != null)
        {
            rescaledLength = EvaluateCurve(curve, rescaledLength);
        }

        x = (x / length) * rescaledLength;
        y = (y / length) * rescaledLength;
    }

    private static double EvaluateCurve(double[] curve, double x)
    {
        if (curve == null || curve.Length != 11) return x;
        if (x <= 0) return curve[0] / 100.0;
        if (x >= 1.0) return curve[10] / 100.0;

        double scaledX = x * 10.0;
        int index = (int)scaledX;
        double fraction = scaledX - index;

        if (index >= 10) return curve[10] / 100.0;

        double y1 = curve[index];
        double y2 = curve[index + 1];
        return (y1 + (y2 - y1) * fraction) / 100.0;
    }

    /// <summary>
    /// Maps a square input vector to a circular one using elliptical grid mapping.
    /// This prevents diagonal inputs from exceeding a magnitude of 1.0.
    /// </summary>
    private static void ApplyCircularMapping(ref double x, ref double y)
    {
        double mappedX = x * Math.Sqrt(1.0 - (y * y) / 2.0);
        double mappedY = y * Math.Sqrt(1.0 - (x * x) / 2.0);
        x = mappedX;
        y = mappedY;
    }

    /// <summary>0..1 actuation for an analog input, with deadzone applied and rescaled so full press still reaches 1.0.</summary>
    private double DeadzonedValue(Profile profile, ControllerInput input)
    {
        ushort? keyId = profile.GetRawKeyId(input);
        if (!keyId.HasValue)
        {
            return 0.0;
        }

        double normalized = _hid.GetNormalized(keyId.Value);
        if (normalized <= profile.Deadzone)
        {
            return 0.0;
        }

        return (normalized - profile.Deadzone) / (1.0 - profile.Deadzone);
    }

    private static short ToAxisShort(double signedNormalized) =>
        (short)Math.Clamp(signedNormalized * short.MaxValue, short.MinValue, short.MaxValue);

    private static byte ToTriggerByte(double normalized) =>
        (byte)Math.Clamp(normalized * 255.0, 0, 255);

    public void Dispose() => Stop();
}
