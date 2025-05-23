using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ArcademiaGameLauncher
{
    internal class ControllerState
    {
        // Import GetKeyState from user32.dll for checking the state of the numlock key
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        public static extern short GetKeyState(int keyCode);

        private Keybinds keybinds;
        private bool isKeymapping;

        private readonly int index;
        private readonly bool[] buttonStates;
        private readonly bool[] buttonDownStates;
        private readonly bool[] buttonUpStates;
        private int leftStickX;
        private int leftStickY;
        private readonly int[] direction;
        private int exitButtonHeldFor;

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 7700;
        readonly int joystickMidpoint = 32511;

        public Joystick joystick;
        public JoystickState state;

        private readonly MainWindow mainWindow;
        private readonly Keyboard keyboard;

        public enum ControllerButtons
        {
            Exit = 0,
            Start = 1,
            A = 2,
            B = 3,
            C = 4,
            D = 5,
            E = 6,
            F = 7
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
            this.keyboard = new Keyboard();
            // Set the keymapping to true
            isKeymapping = true;

            // Set the keybinds for the player
            JObject playerControls = JObject.Parse(File.ReadAllText(Path.Combine(mainWindow.RootPath, "json", "ButtonConfig.json")));
            keybinds = new Keybinds(playerControls, index);

            direction = new int[2];

            // Initialize the button states
            state = new JoystickState();
            buttonStates = new bool[128];
            buttonDownStates = new bool[128];
            buttonUpStates = new bool[128];
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

            // Update the exit button held for time
            if (buttonStates[0])
                exitButtonHeldFor += 10;
            else
                exitButtonHeldFor = 0;
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
        public bool GetButtonState(ControllerButtons _button) => buttonStates[((int)_button)];
        public bool GetButtonDownState(ControllerButtons _button) => buttonDownStates[((int)_button)];

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
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.Exit, _buttonState);
                        break;
                    case 1:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.Start, _buttonState);
                        break;
                    case 2:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.A, _buttonState);
                        break;
                    case 3:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.B, _buttonState);
                        break;
                    case 4:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.C, _buttonState);
                        break;
                    case 5:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.D, _buttonState);
                        break;
                    case 6:
                        if (_buttonState) mainWindow.Key_Pressed();
                        SendKey(keybinds.E, _buttonState);
                        break;
                    case 7:
                        if (_buttonState) mainWindow.Key_Pressed();
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
        }

        public int GetExitButtonHeldFor() => exitButtonHeldFor;

        // Toggle the keymapping
        public void ToggleKeymapping() => isKeymapping = !isKeymapping;

        // Getter for the index
        public int GetIndex() => index;

        // Send a key press (Keymapping)
        private void SendKey(string key, bool state)
        {
            // send numlock key press
            if ((((ushort)GetKeyState(0x90)) & 0xffff) != 0)
            {
                keyboard.Send(Keyboard.ScanCodeShort.NUMLOCK);
                keyboard.Release(Keyboard.ScanCodeShort.NUMLOCK);
            }

            // map the string to the key
            Keyboard.ScanCodeShort keyCode = (Keyboard.ScanCodeShort)Enum.Parse(typeof(Keyboard.ScanCodeShort), key);

            // send the key press
            if (state) keyboard.Send(keyCode);
            else keyboard.Release(keyCode);
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