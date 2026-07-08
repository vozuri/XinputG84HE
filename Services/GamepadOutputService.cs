using System.Threading;
using XinputG84HE.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace XinputG84HE.Services;

/// <summary>
/// Reads current key actuation from <see cref="HidDeviceService"/>, applies
/// the active profile's bindings/deadzone/threshold, and pushes the result to
/// a virtual Xbox 360 controller via ViGEmBus at ~125Hz.
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

    public event Action<string>? Log;

    public bool IsRunning => _controller is not null;

    public GamepadOutputService(HidDeviceService hid)
    {
        _hid = hid;
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
            Thread.Sleep(8); // ~125Hz

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

                double leftX = SignedAxis(profile, ControllerInput.LeftStickRight, ControllerInput.LeftStickLeft);
                double leftY = SignedAxis(profile, ControllerInput.LeftStickUp, ControllerInput.LeftStickDown);
                double rightX = SignedAxis(profile, ControllerInput.RightStickRight, ControllerInput.RightStickLeft);
                double rightY = SignedAxis(profile, ControllerInput.RightStickUp, ControllerInput.RightStickDown);

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxisShort(leftX));
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisShort(leftY));
                controller.SetAxisValue(Xbox360Axis.RightThumbX, ToAxisShort(rightX));
                controller.SetAxisValue(Xbox360Axis.RightThumbY, ToAxisShort(rightY));

                controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToTriggerByte(DeadzonedValue(profile, ControllerInput.LeftTrigger)));
                controller.SetSliderValue(Xbox360Slider.RightTrigger, ToTriggerByte(DeadzonedValue(profile, ControllerInput.RightTrigger)));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Gamepad update error: {ex.Message}");
            }
        }
    }

    private double SignedAxis(Profile profile, ControllerInput positive, ControllerInput negative)
    {
        double pos = DeadzonedValue(profile, positive);
        double neg = DeadzonedValue(profile, negative);
        return Math.Clamp(pos - neg, -1.0, 1.0);
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
