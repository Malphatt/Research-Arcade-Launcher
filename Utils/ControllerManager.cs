using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ArcademiaGameLauncher.Models;
using ArcademiaGameLauncher.Windows;
using Microsoft.Extensions.Logging;
using SharpDX.DirectInput;

namespace ArcademiaGameLauncher.Utils
{
    class ControllerManager
    {
        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto,
            ExactSpelling = true,
            CallingConvention = CallingConvention.Winapi
        )]
        public static extern short GetAsyncKeyState(int keyCode);

        private readonly MainWindow _mainWindow;
        private readonly ILogger<ControllerManager> _logger;
        private readonly Keyboard keyboard;

        private readonly int _pollingRate;
        private bool _isKeymapping;
        private bool _debugKeyDown;
        private bool _debugMode;
        private bool _debugModeRunning = false;

        private DirectInput _directInput;
        private readonly List<ControllerState> _controllerStates = [];

        public ControllerManager(
            MainWindow mainWindow,
            int pollingRate,
            ILogger<ControllerManager> logger
        )
        {
            _mainWindow = mainWindow;
            _pollingRate = pollingRate;
            _logger = logger;

            keyboard = new Keyboard();
            _isKeymapping = true;
            _debugMode = false;

            JoyStickInit();
        }

        public void Dispose()
        {
            foreach (var controllerState in _controllerStates)
            {
                controllerState.StopPolling();
            }
            _directInput.Dispose();
        }

        private void JoyStickInit()
        {
            // Initialize Direct Input
            _directInput = new();

            // Find a JoyStick Guid
            List<Guid> joystickGuids = [];

            // Find a Gamepad Guid
            foreach (
                var deviceInstance in _directInput.GetDevices(
                    DeviceType.Gamepad,
                    DeviceEnumerationFlags.AllDevices
                )
            )
                joystickGuids.Add(deviceInstance.InstanceGuid);

            // If no Gamepad is found, find a Joystick
            if (joystickGuids.Count == 0)
                foreach (
                    var deviceInstance in _directInput.GetDevices(
                        DeviceType.Joystick,
                        DeviceEnumerationFlags.AllDevices
                    )
                )
                    joystickGuids.Add(deviceInstance.InstanceGuid);

            // If no Joystick is found, throw an error
            if (joystickGuids.Count == 0)
            {
                //MessageBox.Show("No joystick or gamepad found.");
                //Application.Current?.Shutdown();
                return;
            }

            // For each Joystick Guid, create a new Joystick object
            foreach (Guid joystickGuid in joystickGuids)
            {
                Joystick joystick = new(_directInput, joystickGuid);

                var allEffects = joystick.GetEffects();
                foreach (var effectInfo in allEffects)
                    Console.WriteLine(effectInfo.Name);

                joystick.Properties.BufferSize = 128;
                joystick.Acquire();

                ControllerState controllerState = new(
                    joystick,
                    _controllerStates.Count,
                    () => _mainWindow.Key_Pressed(),
                    SendKey
                );
                _controllerStates.Add(controllerState);
            }

            StartPolling();
            ListenForDebugKey();
        }

        public void StartPolling()
        {
            foreach (var controllerState in _controllerStates)
                controllerState.StartPolling(_pollingRate);
        }

        public void StopPolling()
        {
            foreach (var controllerState in _controllerStates)
                controllerState.StopPolling();
        }

        public int[] GetEitherLeftStickDirection()
        {
            int[] result = new int[2];
            foreach (var controllerState in _controllerStates)
            {
                int[] dir = controllerState.GetLeftStickDirection();
                if (dir[0] != 0 || dir[1] != 0)
                {
                    result[0] = dir[0];
                    result[1] = dir[1];
                    return result;
                }
            }

            return result;
        }

        public int[] GetPlayerLeftStickDirection(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _controllerStates.Count)
                return new int[2];

            return _controllerStates[playerIndex].GetLeftStickDirection();
        }

        public bool GetEitherButtonState(ControllerState.ControllerActions button)
        {
            foreach (var controllerState in _controllerStates)
                if (controllerState.GetButtonState(button))
                    return true;

            return false;
        }

        public bool GetPlayerButtonState(int playerIndex, ControllerState.ControllerActions button)
        {
            if (playerIndex < 0 || playerIndex >= _controllerStates.Count)
                return false;

            return _controllerStates[playerIndex].GetButtonState(button);
        }

        public bool GetEitherButtonDownState(ControllerState.ControllerActions button)
        {
            foreach (var controllerState in _controllerStates)
                if (controllerState.GetButtonDownState(button))
                    return true;

            return false;
        }

        public bool GetPlayerButtonDownState(
            int playerIndex,
            ControllerState.ControllerActions button
        )
        {
            if (playerIndex < 0 || playerIndex >= _controllerStates.Count)
                return false;

            return _controllerStates[playerIndex].GetButtonDownState(button);
        }

        public void ReleaseAllButtons()
        {
            foreach (var controllerState in _controllerStates)
                controllerState.ReleaseButtons();
        }

        public void ReleasePlayerButtons(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _controllerStates.Count)
                return;
            _controllerStates[playerIndex].ReleaseButtons();
        }

        public int GetExitButtonHeldFor()
        {
            int maxHeldFor = 0;

            foreach (var controllerState in _controllerStates)
            {
                int heldFor = controllerState.GetExitButtonHeldFor();
                if (heldFor > maxHeldFor)
                    maxHeldFor = heldFor;
            }

            return maxHeldFor;
        }

        public void ToggleKeymapping()
        {
            _isKeymapping = !_isKeymapping;

            foreach (var controllerState in _controllerStates)
                controllerState.SetKeymapping(_isKeymapping);
        }

        public void ToggleDebugMode()
        {
            _debugMode = !_debugMode;
            _logger.LogInformation($"[ControllerManager] Debug Mode: {_debugMode}");

            foreach (var controllerState in _controllerStates)
                controllerState.SetDebugMode(_debugMode);

            if (_debugMode)
                StartDebugMode();
        }

        // Send a key press (Keymapping)
        private void SendKey(string key, bool state)
        {
            try
            {
                // send numlock key press
                if (((ushort)GetAsyncKeyState(0x90) & 0xffff) != 0)
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

        public int GetControllerCount()
        {
            return _controllerStates.Count;
        }

        private void ListenForDebugKey()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    bool _debugKeyState =
                        (GetAsyncKeyState((int)Keyboard.VirtualKeyShort.F4) & 0x8000) != 0;

                    if (!_debugKeyState && _debugKeyDown)
                        ToggleDebugMode();

                    _debugKeyDown = _debugKeyState;

                    await Task.Delay(_pollingRate);
                }
            });
        }

        // Debug mode input listener
        private void StartDebugMode()
        {
            if (_debugModeRunning)
                return;

            Task.Run(async () =>
            {
                _debugModeRunning = true;

                while (_debugMode)
                {
                    foreach (var controllerState in _controllerStates)
                    {
                        Keybinds keybinds = controllerState.keybinds;

                        // --- Directional Input ---
                        int dirX = 0;
                        int dirY = 0;

                        // Up
                        if (((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Up)) & 0x8000) != 0)
                            dirY = -1;

                        // Down
                        if (((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Down)) & 0x8000) != 0)
                            dirY = 1;

                        // Left
                        if (((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Left)) & 0x8000) != 0)
                            dirX = -1;

                        // Right
                        if (((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Right)) & 0x8000) != 0)
                            dirX = 1;

                        controllerState.SetDebugStickState(dirX, dirY);

                        // --- Button Input ---

                        // Exit
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.Exit,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Exit)) & 0x8000) != 0
                        );

                        // Start
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.Start,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.Start)) & 0x8000) != 0
                        );

                        // A
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.A,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.A)) & 0x8000) != 0
                        );

                        // B
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.B,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.B)) & 0x8000) != 0
                        );

                        // C
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.C,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.C)) & 0x8000) != 0
                        );

                        // D
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.D,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.D)) & 0x8000) != 0
                        );

                        // E
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.E,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.E)) & 0x8000) != 0
                        );

                        // F
                        controllerState.SetDebugAction(
                            ControllerState.ControllerActions.F,
                            ((ushort)GetAsyncKeyState(GetVirtualKey(keybinds.F)) & 0x8000) != 0
                        );
                    }

                    await Task.Delay(_pollingRate);
                }

                _debugModeRunning = false;
            });
        }

        public void UpdateMapping(ControllerMapping mapping)
        {
            _logger.LogInformation("[ControllerManager] Updating controller mappings...");
            foreach (var controllerState in _controllerStates)
                controllerState.UpdateMapping(mapping);
        }

        private static int GetVirtualKey(string key)
        {
            if (Enum.TryParse(key, true, out Keyboard.VirtualKeyShort result))
                return (int)result;

            if (Enum.TryParse("KEY_" + key, true, out Keyboard.VirtualKeyShort resultPrefix))
                return (int)resultPrefix;

            return 0;
        }
    }
}
