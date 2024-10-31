using SharpDX.DirectInput;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using System.IO;

namespace ArcademiaGameLauncher
{
    internal class ControllerState
    {
        // Send a key press
        [DllImport("User32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private Keybinds keybinds;
        private bool isKeymapping;

        private readonly int index;
        private readonly bool[] buttonStates;
        private readonly bool[] buttonDownStates;
        private int leftStickX;
        private int leftStickY;
        private readonly int[] direction;

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 7700;
        readonly int joystickMidpoint = 32511;

        public Joystick joystick;
        public JoystickState state;

        public ControllerState(Joystick _joystick, int _index, MainWindow mainWindow)
        {
            // Set the index of the controller
            index = _index;
            // Set the joystick
            joystick = _joystick;
            // Set the keymapping to true
            isKeymapping = true;

            // Set the keybinds for the player
            JObject playerControls = JObject.Parse(Path.Combine(mainWindow.RootPath, "ButtonConfig.json"));
            keybinds = new Keybinds(playerControls, index);

            direction = new int[2];

            // Initialize the button states
            state = new JoystickState();
            buttonStates = new bool[128];
            buttonDownStates = new bool[128];
            state = joystick.GetCurrentState();
        }

        public void UpdateButtonStates()
        {
            // Poll the joystick for the current state
            joystick.Poll();
            state = joystick.GetCurrentState();


            // Update the joystick states
            SetLeftStickDirection(state.X, state.Y);

            // Update the button states
            for (int i = 0; i < buttonStates.Length; i++)
                SetButtonState(i, state.Buttons[i]);
        }

        // Getter and Setter for the joystick direction
        public int[] GetLeftStickDirection() => direction;
        
        private void SetLeftStickDirection(int _x, int _y)
        {
            leftStickX = _x;
            leftStickY = _y;

            if (leftStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;

                if (!isKeymapping) return;

                SendKey(keybinds.Left, false);
                SendKey(keybinds.Right, true);
            }
            else if (leftStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;

                if (!isKeymapping) return;

                SendKey(keybinds.Right, false);
                SendKey(keybinds.Left, true);
            }
            else
            {
                direction[0] = 0;

                if (!isKeymapping) return;

                SendKey(keybinds.Left, false);
                SendKey(keybinds.Right, false);
            }

            if (leftStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;

                if (!isKeymapping) return;

                SendKey(keybinds.Up, false);
                SendKey(keybinds.Down, true);
            }
            else if (leftStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;

                if (!isKeymapping) return;

                SendKey(keybinds.Down, false);
                SendKey(keybinds.Up, true);
            }
            else
            {
                direction[1] = 0;

                if (!isKeymapping) return;

                SendKey(keybinds.Up, false);
                SendKey(keybinds.Down, false);
            }
        }

        // Getter and Setter for the button states
        public bool GetButtonState(int _button) => buttonStates[_button];
        public bool GetButtonDownState(int _button) => buttonDownStates[_button];

        public void SetButtonState(int _button, bool _buttonState)
        {
            if (!buttonStates[_button] && _buttonState)
                buttonDownStates[_button] = true;
            else
                buttonDownStates[_button] = false;

            buttonStates[_button] = _buttonState;

            if (!isKeymapping) return;

            // Send the key press if the button is pressed
            switch (_button)
            {
                case 0:
                    SendKey(keybinds.Exit, _buttonState);
                    break;
                case 1:
                    SendKey(keybinds.Start, _buttonState);
                    break;
                case 2:
                    SendKey(keybinds.A, _buttonState);
                    break;
                case 3:
                    SendKey(keybinds.B, _buttonState);
                    break;
                case 4:
                    SendKey(keybinds.C, _buttonState);
                    break;
                case 5:
                    SendKey(keybinds.D, _buttonState);
                    break;
                case 6:
                    SendKey(keybinds.E, _buttonState);
                    break;
                case 7:
                    SendKey(keybinds.F, _buttonState);
                    break;
                default:
                    break;
            }
        }

        // Toggle the keymapping
        public void ToggleKeymapping() => isKeymapping = !isKeymapping;

        // Getter for the index
        public int GetIndex() => index;

        // Send a key press (Keymapping)
        private void SendKey(string key, bool state)
        {
            switch (key)
            {
                // For Non-ASCII keys
                case "UP_ARROW":
                    if (state) keybd_event(38, 72, 0, 0);
                    else keybd_event(38, 72, 2, 0);
                    break;
                case "LEFT_ARROW":
                    if (state) keybd_event(37, 75, 0, 0);
                    else keybd_event(37, 75, 2, 0);
                    break;
                case "DOWN_ARROW":
                    if (state) keybd_event(40, 80, 0, 0);
                    else keybd_event(40, 80, 2, 0);
                    break;
                case "RIGHT_ARROW":
                    if (state) keybd_event(39, 77, 0, 0);
                    else keybd_event(39, 77, 2, 0);
                    break;
                case "ESC":
                    if (state) keybd_event(33, 0, 0, 0);
                    else keybd_event(33, 0, 2, 0);
                    break;
                case "ENTER":
                    if (state) keybd_event(13, 0, 0, 0);
                    else keybd_event(13, 0, 2, 0);
                    break;
                case "RETURN":
                    if (state) keybd_event(13, 0, 0, 0);
                    else keybd_event(13, 0, 2, 0);
                    break;
                case "BACKSPACE":
                    if (state) keybd_event(10, 0, 0, 0);
                    else keybd_event(10, 0, 2, 0);
                    break;

                // For ASCII keys
                default:
                    byte asciiValue = (byte)key.ToUpper()[0];

                    if (state) keybd_event(asciiValue, 0, 0, 0);
                    else keybd_event(asciiValue, 0, 2, 0);
                    break;
            }
        }
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
            // Set the keybinds for the player
            Up = _playerControls["Up"][_index].ToString();
            Left = _playerControls["Left"][_index].ToString();
            Down = _playerControls["Down"][_index].ToString();
            Right = _playerControls["Right"][_index].ToString();

            Exit = _playerControls["Exit"][_index].ToString();
            Start = _playerControls["Start"][_index].ToString();
            A = _playerControls["A"][_index].ToString();
            B = _playerControls["B"][_index].ToString();
            C = _playerControls["C"][_index].ToString();
            D = _playerControls["D"][_index].ToString();
            E = _playerControls["E"][_index].ToString();
            F = _playerControls["F"][_index].ToString();
        }
    }

}