using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ArcademiaGameLauncher.Windows;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;

namespace ArcademiaGameLauncher.Utilis
{
    internal class ControllerState
    {
        // Import GetKeyState from user32.dll for checking the state of the numlock key
        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto,
            ExactSpelling = true,
            CallingConvention = CallingConvention.Winapi
        )]
        public static extern short GetKeyState(int keyCode);

        private Keybinds keybinds;
        private bool isKeymapping;
        private static JObject _cachedPlayerControls;

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

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 7700;
        readonly int joystickMidpoint = 32511;

        public Joystick joystick;
        public JoystickState state;

        private readonly MainWindow mainWindow;
        private readonly Keyboard keyboard;

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

        public ControllerState(Joystick _joystick, int _index, MainWindow mainWindow)
        {
            // Set the joystick
            joystick = _joystick;
            // Set the index of the controller
            index = _index;
            // Set the main window
            this.mainWindow = mainWindow;
            // Set the keyboard
            keyboard = new Keyboard();
            // Set the keymapping to true
            isKeymapping = true;

            // Set the keybinds for the player
            try
            {
                JObject playerControls = JObject.Parse(
                    File.ReadAllText(
                        Path.Combine(
                            mainWindow._applicationPath,
                            "Configuration",
                            "ButtonConfig.json"
                        )
                    )
                );
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

        public void StartPolling()
        {
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
                    exitButtonHeldFor += 10;
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
            leftStickX = _x;
            leftStickY = _y;

            // --- HORIZONTAL AXIS ---
            bool reqLeft = false;
            bool reqRight = false;

            if (leftStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
                reqRight = true;
                if (isKeymapping)
                    mainWindow.Key_Pressed();
            }
            else if (leftStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
                reqLeft = true;
                if (isKeymapping)
                    mainWindow.Key_Pressed();
            }
            else
            {
                direction[0] = 0;
            }

            if (isKeymapping)
            {
                if (reqLeft != _lastSentLeft)
                {
                    SendKey(keybinds.Left, reqLeft);
                    _lastSentLeft = reqLeft;
                }
                if (reqRight != _lastSentRight)
                {
                    SendKey(keybinds.Right, reqRight);
                    _lastSentRight = reqRight;
                }
            }

            bool reqUp = false;
            bool reqDown = false;

            if (leftStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
                reqDown = true;
                if (isKeymapping)
                    mainWindow.Key_Pressed();
            }
            else if (leftStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
                reqUp = true;
                if (isKeymapping)
                    mainWindow.Key_Pressed();
            }
            else
            {
                direction[1] = 0;
            }

            if (isKeymapping)
            {
                if (reqUp != _lastSentUp)
                {
                    SendKey(keybinds.Up, reqUp);
                    _lastSentUp = reqUp;
                }
                if (reqDown != _lastSentDown)
                {
                    SendKey(keybinds.Down, reqDown);
                    _lastSentDown = reqDown;
                }
            }
        }

        // Getter and Setter for the button states
        public bool GetButtonState(ControllerButtons _button) => buttonStates[(int)_button];

        public bool GetButtonDownState(ControllerButtons _button) => buttonDownStates[(int)_button];

        public void SetButtonState(int _button, bool _buttonState)
        {
            if (!buttonStates[_button] && _buttonState)
                buttonDownStates[_button] = true;
            else
                buttonDownStates[_button] = false;

            if (buttonStates[_button] && !_buttonState)
                buttonUpStates[_button] = true;
            else
                buttonUpStates[_button] = false;

            if ((buttonDownStates[_button] || buttonUpStates[_button]) && isKeymapping)
            {
                // Send the key press if the button is pressed
                switch (_button)
                {
                    case 0:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.Exit, _buttonState);
                        break;
                    case 1:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.Start, _buttonState);
                        break;
                    case 2:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.A, _buttonState);
                        break;
                    case 3:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.B, _buttonState);
                        break;
                    case 4:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.C, _buttonState);
                        break;
                    case 5:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.D, _buttonState);
                        break;
                    case 6:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.E, _buttonState);
                        break;
                    case 7:
                        if (_buttonState)
                            mainWindow.Key_Pressed();
                        SendKey(keybinds.F, _buttonState);
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
                SendKey(keybinds.Left, false);
                _lastSentLeft = false;
            }
            if (_lastSentRight)
            {
                SendKey(keybinds.Right, false);
                _lastSentRight = false;
            }
            if (_lastSentUp)
            {
                SendKey(keybinds.Up, false);
                _lastSentUp = false;
            }
            if (_lastSentDown)
            {
                SendKey(keybinds.Down, false);
                _lastSentDown = false;
            }
        }

        public int GetExitButtonHeldFor() => exitButtonHeldFor;

        // Toggle the keymapping
        public void ToggleKeymapping() => isKeymapping = !isKeymapping;

        // Getter for the index
        public int GetIndex() => index;

        // Send a key press (Keymapping)
        private void SendKey(string key, bool state)
        {
            try
            {
                // send numlock key press
                if (((ushort)GetKeyState(0x90) & 0xffff) != 0)
                {
                    keyboard.Send(Keyboard.ScanCodeShort.NUMLOCK);
                    keyboard.Release(Keyboard.ScanCodeShort.NUMLOCK);
                }

                // map the string to the key
                if (Enum.TryParse(key, out Keyboard.ScanCodeShort keyCode))
                {
                    // send the key press
                    if (state)
                        keyboard.Send(keyCode);
                    else
                        keyboard.Release(keyCode);
                }
            }
            catch
            {
                // Suppress input errors to prevent crash loop
            }
        }
    }

    struct Keybinds(JObject _playerControls, int _index)
    {
        public string Up = _playerControls["Up"]?[_index]?.ToString() ?? "UP";
        public string Left = _playerControls["Left"]?[_index]?.ToString() ?? "LEFT";
        public string Down = _playerControls["Down"]?[_index]?.ToString() ?? "DOWN";
        public string Right = _playerControls["Right"]?[_index]?.ToString() ?? "RIGHT";

        public string Exit = _playerControls["Exit"]?[_index]?.ToString() ?? "ESCAPE";
        public string Start = _playerControls["Start"]?[_index]?.ToString() ?? "RETURN";
        public string A = _playerControls["A"]?[_index]?.ToString() ?? "LCONTROL";
        public string B = _playerControls["B"]?[_index]?.ToString() ?? "LSHIFT";
        public string C = _playerControls["C"]?[_index]?.ToString() ?? "Z";
        public string D = _playerControls["D"]?[_index]?.ToString() ?? "X";
        public string E = _playerControls["E"]?[_index]?.ToString() ?? "C";
        public string F = _playerControls["F"]?[_index]?.ToString() ?? "V";
    }
}
