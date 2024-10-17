using SharpDX.DirectInput;

namespace ArcademiaGameLauncher
{
    internal class ControllerState
    {
        private readonly int index;
        private readonly bool[] buttonStates;
        private readonly bool[] buttonDownStates;
        private int leftStickX;
        private int leftStickY;

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 7700;
        readonly int joystickMidpoint = 32511;

        public Joystick joystick;
        public JoystickState state;

        public ControllerState(Joystick _joystick, int _index)
        {
            // Set the index of the controller
            index = _index;
            // Set the joystick
            joystick = _joystick;

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
            leftStickX = state.X;
            leftStickY = state.Y;

            // Update the button states
            for (int i = 0; i < buttonStates.Length; i++)
            {
                SetButtonState(i, state.Buttons[i]);
            }
        }

        // Getters for the joystick states
        public int GetLeftStickX()
        {
            return leftStickX;
        }
        public int GetLeftStickY()
        {
            return leftStickY;
        }

        // Getters for the joystick directions
        public int[] GetLeftStickDirection()
        {
            int[] direction = new int[2];

            if (leftStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
            }
            else if (leftStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
            }
            else
            {
                direction[0] = 0;
            }

            if (leftStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
            }
            else if (leftStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
            }
            else
            {
                direction[1] = 0;
            }

            return direction;
        }

        // Getter and Setter for the button states
        public bool GetButtonState(int _button)
        {
            return buttonStates[_button];
        }
        public bool GetButtonDownState(int _button)
        {
            return buttonDownStates[_button];
        }
        public void SetButtonState(int _button, bool _buttonState)
        {
            if (!buttonStates[_button] && _buttonState)
                buttonDownStates[_button] = true;
            else
                buttonDownStates[_button] = false;

            buttonStates[_button] = _buttonState;
        }

        // Getter for the index
        public int GetIndex()
        {
            return index;
        }
    }
}
