using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArcademiaGameLauncher.Utils
{
    internal class ControllerState
    {
        public Keybinds keybinds;
        public bool _isKeymapping;
        public bool _debugMode;

        private readonly int index;
        private readonly bool[] buttonStates;
        private readonly bool[] buttonDownStates;
        private readonly bool[] buttonUpStates;
        private int leftStickX;
        private int leftStickY;
        private readonly int[] direction;
        private volatile int exitButtonHeldFor;

        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private int _pollingRate;

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 7700;
        readonly int joystickMidpoint = 32511;

        public Joystick joystick;
        public JoystickState state;

        private readonly Action _onInputDetected;
        private readonly Action<string, bool> _onSendKey;

        // STATE TRACKING TO PREVENT FLOODING
        private bool _lastSentLeft = false;
        private bool _lastSentRight = false;
        private bool _lastSentUp = false;
        private bool _lastSentDown = false;

        public enum ControllerButtons
        {
            Exit = 0,
            Start = 1,
            A = 2,
            B = 3,
            C = 4,
            D = 5,
            E = 6,
            F = 7,
        }

        public ControllerState(
            Joystick _joystick,
            int _index,
            Action onInputDetected,
            Action<string, bool> onSendKey
        )
        {
            // Set the joystick
            joystick = _joystick;
            // Set the index of the controller
            index = _index;
            // Set the input detected callback
            _onInputDetected = onInputDetected;
            _onSendKey = onSendKey;
            _isKeymapping = true;
            _debugMode = false;

            // Set the keybinds for the player
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                const string resourceName = "ArcademiaGameLauncher.Assets.json.ButtonConfig.json";

                // Read embedded JSON resource from assembly
                using Stream stream =
                    assembly.GetManifestResourceStream(resourceName)
                    ?? throw new FileNotFoundException("Embedded resource not found.", resourceName);
                using StreamReader reader = new(stream, Encoding.UTF8);
                string json = reader.ReadToEnd();

                JObject playerControls = JObject.Parse(json);
                keybinds = new Keybinds(playerControls, index);
            }
            catch { }

            direction = new int[2];

            // Initialize the button states
            state = new JoystickState();
            buttonStates = new bool[128];
            buttonDownStates = new bool[128];
            buttonUpStates = new bool[128];
            try
            {
                state = joystick.GetCurrentState();
            }
            catch { }
        }

        public void StartPolling(int pollingRate)
        {
            _pollingRate = pollingRate;

            if (_pollingTask != null && !_pollingTask.IsCompleted)
                return;

            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(
                async () =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        UpdateButtonStates();
                        await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                    }
                },
                _cts.Token
            );
        }

        public void StopPolling()
        {
            _cts?.Cancel();
            try
            {
                _pollingTask?.Wait(500);
            }
            catch { }
        }

        private void UpdateButtonStates()
        {
            try
            {
                // Poll the joystick for the current state
                joystick.Poll();
                state = joystick.GetCurrentState();

                // Update the joystick states
                SetLeftStickDirection(state.X, state.Y);

                // Update the button states
                for (int i = 0; i < buttonStates.Length; i++)
                    SetButtonState(i, state.Buttons[i]);

                // Update the exit button held for time
                if (buttonStates[0])
                    exitButtonHeldFor += _pollingRate;
                else
                    exitButtonHeldFor = 0;
            }
            catch
            {
                // Handle joystick disconnects gracefully
            }
        }

        // Getter and Setter for the joystick direction
        public int[] GetLeftStickDirection() => direction;

        private void SetLeftStickDirection(int _x, int _y)
        {
            if (_debugMode)
                return;

            leftStickX = _x;
            leftStickY = _y;

            // --- HORIZONTAL AXIS ---
            bool reqLeft = false;
            bool reqRight = false;

            if (leftStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
                reqRight = true;
                if (_isKeymapping)
                    _onInputDetected?.Invoke();
            }
            else if (leftStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
                reqLeft = true;
                if (_isKeymapping)
                    _onInputDetected?.Invoke();
            }
            else
            {
                direction[0] = 0;
            }

            if (_isKeymapping)
            {
                if (reqLeft != _lastSentLeft)
                {
                    _onSendKey?.Invoke(keybinds.Left, reqLeft);
                    _lastSentLeft = reqLeft;
                }
                if (reqRight != _lastSentRight)
                {
                    _onSendKey?.Invoke(keybinds.Right, reqRight);
                    _lastSentRight = reqRight;
                }
            }

            bool reqUp = false;
            bool reqDown = false;

            if (leftStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
                reqDown = true;
                if (_isKeymapping)
                    _onInputDetected?.Invoke();
            }
            else if (leftStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
                reqUp = true;
                if (_isKeymapping)
                    _onInputDetected?.Invoke();
            }
            else
            {
                direction[1] = 0;
            }

            if (_isKeymapping)
            {
                if (reqUp != _lastSentUp)
                {
                    _onSendKey?.Invoke(keybinds.Up, reqUp);
                    _lastSentUp = reqUp;
                }
                if (reqDown != _lastSentDown)
                {
                    _onSendKey?.Invoke(keybinds.Down, reqDown);
                    _lastSentDown = reqDown;
                }
            }
        }

        // Getter and Setter for the button states
        public bool GetButtonState(ControllerButtons _button) => buttonStates[(int)_button];

        public bool GetButtonDownState(ControllerButtons _button) => buttonDownStates[(int)_button];

        public void SetButtonState(int _button, bool _buttonState)
        {
            if (_debugMode)
                return;

            if (!buttonStates[_button] && _buttonState)
                buttonDownStates[_button] = true;
            else
                buttonDownStates[_button] = false;

            if (buttonStates[_button] && !_buttonState)
                buttonUpStates[_button] = true;
            else
                buttonUpStates[_button] = false;

            if ((buttonDownStates[_button] || buttonUpStates[_button]) && _isKeymapping)
            {
                // Send the key press if the button is pressed
                switch (_button)
                {
                    case 0:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.Exit, _buttonState);
                        break;
                    case 1:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.Start, _buttonState);
                        break;
                    case 2:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.A, _buttonState);
                        break;
                    case 3:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.B, _buttonState);
                        break;
                    case 4:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.C, _buttonState);
                        break;
                    case 5:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.D, _buttonState);
                        break;
                    case 6:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.E, _buttonState);
                        break;
                    case 7:
                        if (_buttonState)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.F, _buttonState);
                        break;
                    default:
                        break;
                }
            }

            buttonStates[_button] = _buttonState;
        }

        public void ReleaseButtons()
        {
            // Release all buttons
            for (int i = 0; i < buttonStates.Length; i++)
                if (buttonStates[i])
                    SetButtonState(i, false);

            // Release Directionals
            if (_lastSentLeft)
            {
                _onSendKey?.Invoke(keybinds.Left, false);
                _lastSentLeft = false;
            }
            if (_lastSentRight)
            {
                _onSendKey?.Invoke(keybinds.Right, false);
                _lastSentRight = false;
            }
            if (_lastSentUp)
            {
                _onSendKey?.Invoke(keybinds.Up, false);
                _lastSentUp = false;
            }
            if (_lastSentDown)
            {
                _onSendKey?.Invoke(keybinds.Down, false);
                _lastSentDown = false;
            }
        }

        public int GetExitButtonHeldFor() => exitButtonHeldFor;

        public void SetKeymapping(bool isKeymapping)
        {
            _isKeymapping = isKeymapping;

            if (!isKeymapping)
                ReleaseButtons();
        }

        public void SetDebugMode(bool debugMode)
        {
            _debugMode = debugMode;
            ReleaseButtons();
        }

        public void SetDebugStickState(int dirX, int dirY)
        {
            if (!_debugMode)
                return;

            direction[0] = dirX;
            direction[1] = dirY;

            bool reqLeft = dirX == -1;
            bool reqRight = dirX == 1;
            bool reqUp = dirY == -1;
            bool reqDown = dirY == 1;

            if ((reqLeft || reqRight || reqUp || reqDown) && _isKeymapping)
                _onInputDetected?.Invoke();

            if (_isKeymapping)
            {
                if (reqLeft != _lastSentLeft)
                {
                    _onSendKey?.Invoke(keybinds.Left, reqLeft);
                    _lastSentLeft = reqLeft;
                }
                if (reqRight != _lastSentRight)
                {
                    _onSendKey?.Invoke(keybinds.Right, reqRight);
                    _lastSentRight = reqRight;
                }
                if (reqUp != _lastSentUp)
                {
                    _onSendKey?.Invoke(keybinds.Up, reqUp);
                    _lastSentUp = reqUp;
                }
                if (reqDown != _lastSentDown)
                {
                    _onSendKey?.Invoke(keybinds.Down, reqDown);
                    _lastSentDown = reqDown;
                }
            }
        }

        public void SetDebugButtonState(ControllerButtons button, bool state)
        {
            if (!_debugMode)
                return;

            if (!buttonStates[(int)button] && state)
                buttonDownStates[(int)button] = true;
            else
                buttonDownStates[(int)button] = false;

            if (buttonStates[(int)button] && !state)
                buttonUpStates[(int)button] = true;
            else
                buttonUpStates[(int)button] = false;

            if ((buttonDownStates[(int)button] || buttonUpStates[(int)button]))
            {
                // Send the key press if the button is pressed
                switch ((int)button)
                {
                    case 0:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.Exit, state);
                        break;
                    case 1:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.Start, state);
                        break;
                    case 2:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.A, state);
                        break;
                    case 3:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.B, state);
                        break;
                    case 4:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.C, state);
                        break;
                    case 5:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.D, state);
                        break;
                    case 6:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.E, state);
                        break;
                    case 7:
                        if (state)
                            _onInputDetected?.Invoke();
                        _onSendKey?.Invoke(keybinds.F, state);
                        break;
                    default:
                        break;
                }
            }

            buttonStates[(int)button] = state;
        }

        // Getter for the index
        public int GetIndex() => index;
    }

    struct Keybinds
    {
        public string Up;
        public string Left;
        public string Down;
        public string Right;

        public string Exit;
        public string Start;
        public string A;
        public string B;
        public string C;
        public string D;
        public string E;
        public string F;

        public Keybinds(JObject _playerControls, int _index)
        {
            Up = _playerControls["Up"]?[_index]?.ToString() ?? "UP";
            Left = _playerControls["Left"]?[_index]?.ToString() ?? "LEFT";
            Down = _playerControls["Down"]?[_index]?.ToString() ?? "DOWN";
            Right = _playerControls["Right"]?[_index]?.ToString() ?? "RIGHT";

            Exit = _playerControls["Exit"]?[_index]?.ToString() ?? "ESCAPE";
            Start = _playerControls["Start"]?[_index]?.ToString() ?? "RETURN";
            A = _playerControls["A"]?[_index]?.ToString() ?? "LCONTROL";
            B = _playerControls["B"]?[_index]?.ToString() ?? "LSHIFT";
            C = _playerControls["C"]?[_index]?.ToString() ?? "Z";
            D = _playerControls["D"]?[_index]?.ToString() ?? "X";
            E = _playerControls["E"]?[_index]?.ToString() ?? "C";
            F = _playerControls["F"]?[_index]?.ToString() ?? "V";
        }
    }
}
