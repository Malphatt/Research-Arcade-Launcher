using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using ArcademiaGameLauncher.Models;
using ArcademiaGameLauncher.Services;
using ArcademiaGameLauncher.Utilis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using XamlAnimatedGif;
using static ArcademiaGameLauncher.Utilis.ControllerState;

namespace ArcademiaGameLauncher.Windows
{
    public partial class MainWindow : Window
    {
        readonly bool production;

        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);

        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private static IHost _host;
        private readonly IUpdaterService _updater;

        public readonly string _applicationPath;
        private readonly string _gameDirectoryPath;

        private readonly JObject _config;
        private JObject[] _gameInfoList;

        private System.Timers.Timer _updateTimer;

        private int _selectionAnimationFrame = 0;
        private readonly int _selectionAnimationFrameRate = 100;

        // Colours for the Text Blocks Selection Animation
        private readonly SolidColorBrush[] _selectionAnimationFrames =
        [
            new(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)),
            new(Color.FromArgb(0xFF, 0xE5, 0xC3, 0x5C)),
            new(Color.FromArgb(0xFF, 0xBF, 0xA3, 0x4C)),
            new(Color.FromArgb(0xFF, 0x9E, 0x86, 0x3F)),
            new(Color.FromArgb(0xFF, 0x7F, 0x6c, 0x33)),
            new(Color.FromArgb(0xFF, 0x9E, 0x86, 0x3F)),
            new(Color.FromArgb(0xFF, 0xBF, 0xA3, 0x4C)),
            new(Color.FromArgb(0xFF, 0xE5, 0xC3, 0x5C)),
        ];

        private int _globalCounter = 0;

        private readonly int _selectionUpdateInterval = 250;
        private int _selectionUpdateIntervalCounter = 0;
        private readonly int _selectionUpdateIntervalCounterMax = 10;
        private int _selectionUpdateCounter = 0;

        private int _currentlySelectedHomeIndex = 0;

        private int _currentlySelectedGameIndex;
        private int _previousPageIndex = 0;
        private System.Timers.Timer _updateGameInfoDisplayDebounceTimer;
        private bool _showingDebouncedGame = false;

        private int _afkTimer = 0;
        private readonly int _noInputTimeout = 0;
        private bool _afkTimerActive = false;

        private int _timeSinceLastButton = 0;

        private TextBlock[] _homeOptionsList;
        private TextBlock[] _gameTitlesList;

        private DirectInput _directInput;
        private readonly List<ControllerState> _controllerStates = [];

        private readonly System.Windows.Shapes.Ellipse[] _inputMenuJoysticks;
        private readonly System.Windows.Shapes.Ellipse[][] _inputMenuButtons;

        private Process _currentlyRunningProcess = null;

        private GameState[] _gameTitleStates;

        private readonly InfoWindow _infoWindow;
        private readonly EmojiParser _emojiParser;

        private JArray _audioFiles;
        private string[] _audioFileNames = [];
        private int[] _periodicAudioFiles;

        // MAIN WINDOW

        public MainWindow()
        {
            // Setup closing event
            Closing += Window_Closing;

            // Load the info window
            _infoWindow = new();

            InitializeComponent();

            // Setup Input Joysticks
            _inputMenuJoysticks = [InputMenu_P1_Joy, InputMenu_P2_Joy];

            // Setup Input Buttons
            _inputMenuButtons = new System.Windows.Shapes.Ellipse[2][];
            _inputMenuButtons[0] =
            [
                InputMenu_P1_Exit,
                InputMenu_P1_Start,
                InputMenu_P1_A,
                InputMenu_P1_B,
                InputMenu_P1_C,
                InputMenu_P1_D,
                InputMenu_P1_E,
                InputMenu_P1_F,
            ];
            _inputMenuButtons[1] =
            [
                InputMenu_P2_Exit,
                InputMenu_P2_Start,
                InputMenu_P2_A,
                InputMenu_P2_B,
                InputMenu_P2_C,
                InputMenu_P2_D,
                InputMenu_P2_E,
                InputMenu_P2_F,
            ];

            // Setup Directories
            if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Launcher")))
            {
                _applicationPath = Path.Combine(Directory.GetCurrentDirectory(), "Launcher");
                production = true;
            }
            else
            {
                _applicationPath = Directory.GetCurrentDirectory();
                production = false;
            }

            _emojiParser = new();

            // Load the Config.json file
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Config.json")))
            {
                MessageBox.Show(
                    "Config.json file not found. Please ensure it exists in the application directory.",
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current?.Shutdown();
                return;
            }

            _config = JObject.Parse(
                File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Config.json"))
            );
            _gameDirectoryPath = Path.Combine(_applicationPath, "Games");

            if (_config != null && _config.ContainsKey("NoInputTimeout_ms"))
                _noInputTimeout = _config.ContainsKey("NoInputTimeout_ms")
                    ? int.Parse(_config["NoInputTimeout_ms"].ToString())
                    : 120000; // Default to 2 minutes if not set in config

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(_gameDirectoryPath))
                Directory.CreateDirectory(_gameDirectoryPath);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(
                    (context, services) =>
                    {
                        var host = _config["ApiHost"]?.ToString() ?? "https://localhost:5001";
                        var user = _config["ApiUser"]?.ToString() ?? "Research-Arcade-User";
                        var pass = _config["ApiPass"]?.ToString() ?? "Research-Arcade-Password";

                        var creds = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes($"{user}:{pass}")
                        );

                        services.AddHttpClient<IApiClient, ApiClient>(client =>
                        {
                            client.BaseAddress = new(host);
                            client.DefaultRequestHeaders.Authorization = new(
                                "ArcadeMachine",
                                creds
                            );
                        });

                        services.AddSingleton<string>(_applicationPath);
                        services.AddSingleton<IUpdaterService, UpdaterService>();
                    }
                )
                .Build();

            _updater = _host.Services.GetRequiredService<IUpdaterService>();
            _updater.GameStateChanged += Updater_GameStateChanged;
            _updater.GameDatabaseFetched += Updater_GameDatabaseFetched;
            _updater.GameUpdateCompleted += Updater_GameUpdateCompleted;
            _updater.CloseGameAndUpdater += Updater_CloseGameAndUpdater;
            _updater.RelaunchUpdater += Updater_RelaunchUpdater;

            // Set the locations of each item on the start menu
            string logicalScreenWidth_str = TryFindResource("LogicalSizeWidth").ToString();
            string logicalScreenHeight_str = TryFindResource("LogicalSizeHeight").ToString();

            double logicalScreenWidth = double.Parse(logicalScreenWidth_str);
            double logicalScreenHeight = double.Parse(logicalScreenHeight_str);

            // StartMenu_Rect
            double RectActualWidth = Math.Cos(5f * (Math.PI / 180f)) * (double)StartMenu_Rect.Width;
            double RectActualHeight =
                Math.Sin(5f * Math.PI / 180f) * (double)StartMenu_Rect.Width
                + Math.Cos(5f * Math.PI / 180f) * (double)StartMenu_Rect.Height;

            Canvas.SetLeft(
                StartMenu_Rect,
                (logicalScreenWidth / 2f) - (RectActualWidth / 2f) + (logicalScreenWidth / 30f)
            );
            Canvas.SetTop(StartMenu_Rect, logicalScreenHeight / 2f - RectActualHeight / 2f);

            // StartMenu_ArcademiaLogo
            Canvas.SetLeft(
                StartMenu_ArcademiaLogo,
                logicalScreenWidth / 2f - StartMenu_ArcademiaLogo.Width / 2f
            );
            Canvas.SetTop(StartMenu_ArcademiaLogo, 100);

            // PressStartText
            Canvas.SetLeft(PressStartText, logicalScreenWidth / 2f - PressStartText.Width / 2f);
            Canvas.SetTop(PressStartText, logicalScreenHeight / 2f - PressStartText.Height / 2f);

            // Set width and height of the logos
            double logoWidth = logicalScreenWidth * 0.06f;

            // UoL_Logo
            UoL_Logo.Width = logoWidth;
            UoL_Logo.Height = logoWidth;

            // intlab_Logo
            intlab_Logo.Width = logoWidth;
            intlab_Logo.Height = logoWidth;
            Canvas.SetRight(intlab_Logo, 10 + logoWidth);

            // CSS_Logo
            CSS_Logo.Width = logoWidth;
            CSS_Logo.Height = logoWidth;
            Canvas.SetRight(CSS_Logo, 2 * (10 + logoWidth));

            // Show the Start Menu
            StartMenu.Visibility = Visibility.Visible;
        }

        // Initialization

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
                // Instantiate the joystick
                Joystick joystick = new(_directInput, joystickGuid);

                // Query all suported ForceFeedback effects
                var allEffects = joystick.GetEffects();
                foreach (var effectInfo in allEffects)
                    Console.WriteLine(effectInfo.Name);

                // Set BufferSize in order to use buffered data.
                joystick.Properties.BufferSize = 128;

                // Acquire the joystick
                joystick.Acquire();

                // Create a new ControllerState object for the joystick
                ControllerState controllerState = new(joystick, _controllerStates.Count, this);
                _controllerStates.Add(controllerState);
            }
        }

        private void InitializeUpdateTimer()
        {
            // Timer Setup
            _updateTimer = new System.Timers.Timer { Interval = 10 };

            // Hook up the Elapsed event for the timer.
            _updateTimer.Elapsed += OnTimedEvent;

            // Have the timer fire repeated events (true is the default)
            _updateTimer.AutoReset = true;

            // Start the timer
            _updateTimer.Enabled = true;
        }

        private void LoadGameDatabase()
        {
            // Load the game database from the GameDatabase.json file
            string gameDatabasePath = Path.Combine(_gameDirectoryPath, "GameDatabase.json");
            if (File.Exists(gameDatabasePath))
            {
                JArray gameInfoArray = JArray.Parse(File.ReadAllText(gameDatabasePath));

                _gameInfoList = new JObject[gameInfoArray.Count];
                for (int i = 0; i < gameInfoArray.Count; i++)
                    _gameInfoList[i] = gameInfoArray[i].ToObject<JObject>();

                _gameTitleStates = new GameState[_gameInfoList.Length];
                for (int i = 0; i < _gameTitleStates.Length; i++)
                    _gameTitleStates[i] = GameState.fetchingInfo;

                // Load the game titles into the TextBlocks
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        for (
                            int i = _previousPageIndex * 10;
                            i < (_previousPageIndex + 1) * 10;
                            i++
                        )
                        {
                            if (i < _gameInfoList.Length)
                            {
                                _gameTitlesList[i % 10].Text = _emojiParser.ReplaceColonNames(
                                    _gameInfoList[i]["Name"].ToString()
                                );
                                _gameTitlesList[i % 10].Visibility = Visibility.Visible;
                            }
                            else
                                _gameTitlesList[i % 10].Visibility = Visibility.Hidden;
                        }
                    });
                }
                catch (TaskCanceledException) { }
            }
            else
            {
                _gameInfoList = [];
                _gameTitleStates = [];
            }
        }

        // Updater Methods

        public async Task CheckForUpdaterUpdates() =>
            await _updater.CheckUpdaterAndUpdateAsync(CancellationToken.None);

        public async Task<bool> CheckForGameDatabaseChanges()
        {
            try
            {
                await _updater.CheckGamesAndUpdateAsync(_gameInfoList, CancellationToken.None);
                return true;
            }
            catch (Exception)
            {
                string localGameDatabasePath = Path.Combine(_applicationPath, "GameDatabase.json");

                if (File.Exists(localGameDatabasePath))
                {
                    JArray gameInfoArray = JArray.Parse(File.ReadAllText(localGameDatabasePath));

                    _gameInfoList = new JObject[gameInfoArray.Count];
                    for (int i = 0; i < gameInfoArray.Count; i++)
                        _gameInfoList[i] = gameInfoArray[i].ToObject<JObject>();
                }
                else
                    return false;

                _gameTitleStates = new GameState[_gameInfoList.Length];

                // Show the game titles as "Loading..." until each title is loaded
                for (int i = _previousPageIndex * 10; i < (_previousPageIndex + 1) * 10; i++)
                {
                    if (i < _gameInfoList.Length)
                    {
                        _gameTitlesList[i % 10].Text = "Loading...";
                        _gameTitlesList[i % 10].Visibility = Visibility.Visible;
                    }
                    else
                        _gameTitlesList[i % 10].Visibility = Visibility.Hidden;
                }

                return false;
            }
        }

        // Custom TextBlock Buttons

        private void GameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Selection Menu
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Collapsed;
                    HomeMenu.Visibility = Visibility.Collapsed;
                    SelectionMenu.Visibility = Visibility.Visible;
                    InputMenu.Visibility = Visibility.Collapsed;

                    // Set the page to 0
                    ChangePage(0);
                });
            }
            catch (TaskCanceledException) { }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected game index to 0
            _currentlySelectedGameIndex = 0;
            DebounceUpdateGameInfoDisplay();
        }

        private void InputMenuButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Input Menu
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Collapsed;
                    HomeMenu.Visibility = Visibility.Collapsed;
                    SelectionMenu.Visibility = Visibility.Collapsed;
                    InputMenu.Visibility = Visibility.Visible;
                });
            }
            catch (TaskCanceledException) { }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the About Menu
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Collapsed;
                    HomeMenu.Visibility = Visibility.Visible;
                    SelectionMenu.Visibility = Visibility.Collapsed;
                    InputMenu.Visibility = Visibility.Collapsed;

                    HomeImage.Opacity = 0.2;
                    CreditsPanel.Visibility = Visibility.Visible;

                    // Show the CreditsPanel Logos
                    UoL_Logo.Visibility = Visibility.Visible;
                    intlab_Logo.Visibility = Visibility.Visible;
                    CSS_Logo.Visibility = Visibility.Visible;

                    // Set Canvas.Top of the CreditsPanel to the screen height
                    string logicalScreenHeight_str = TryFindResource("LogicalSizeHeight")
                        .ToString();
                    double logicalScreenHeight = double.Parse(logicalScreenHeight_str);

                    Canvas.SetTop(CreditsPanel, logicalScreenHeight);
                });
            }
            catch (TaskCanceledException) { }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Start Menu
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Visible;
                    HomeMenu.Visibility = Visibility.Collapsed;
                    SelectionMenu.Visibility = Visibility.Collapsed;
                    InputMenu.Visibility = Visibility.Collapsed;

                    HomeImage.Opacity = 1;
                    CreditsPanel.Visibility = Visibility.Collapsed;

                    // Hide the CreditsPanel Logos
                    UoL_Logo.Visibility = Visibility.Collapsed;
                    intlab_Logo.Visibility = Visibility.Collapsed;
                    CSS_Logo.Visibility = Visibility.Collapsed;
                });
            }
            catch (TaskCanceledException) { }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Reset AFK Timer after Half a Second
            Task.Delay(500)
                .ContinueWith(t =>
                {
                    _afkTimerActive = false;
                    _afkTimer = 0;
                });
        }

        // ToggleButton Methods

        private void BackFromGameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Home Menu
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Collapsed;
                    HomeMenu.Visibility = Visibility.Visible;
                    SelectionMenu.Visibility = Visibility.Collapsed;
                    InputMenu.Visibility = Visibility.Collapsed;
                });
            }
            catch (TaskCanceledException) { }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected Home Index to 0 and highlight the current Home Menu Option
            _currentlySelectedHomeIndex = 0;
            HighlightCurrentHomeMenuOption();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // If the game info display is not showing the currently selected game, return
            if (
                !_showingDebouncedGame
                || _gameTitleStates[_currentlySelectedGameIndex] != GameState.ready
            )
                return;

            // Get the current game folder, game info, and game executable
            string currentGameFolder = _gameInfoList[_currentlySelectedGameIndex]
                ["FolderName"]
                .ToString();
            string currentGameExe = Path.Combine(
                _gameDirectoryPath,
                currentGameFolder,
                _gameInfoList[_currentlySelectedGameIndex]["NameOfExecutable"].ToString()
            );

            // Start the game if the game executable exists and the launcher is ready
            if (File.Exists(currentGameExe))
            {
                // Create a new ProcessStartInfo object and set the Working Directory to the game directory
                ProcessStartInfo startInfo = new(currentGameExe)
                {
                    WorkingDirectory = Path.Combine(_gameDirectoryPath, currentGameFolder),
                };

                // Start the game if no process is currently running
                if (_currentlyRunningProcess == null || _currentlyRunningProcess.HasExited)
                {
                    _currentlyRunningProcess = Process.Start(startInfo);

                    StyleStartButtonState(GameState.launching);
                }

                // Set focus to the currently running process
                SetForegroundWindow(_currentlyRunningProcess.MainWindowHandle);

                // After 3 seconds, set the focus to the currently running process
                await Task.Delay(3000);

                if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
                    SetForegroundWindow(_currentlyRunningProcess.MainWindowHandle);

                SetGameTitleState(_currentlySelectedGameIndex, GameState.runningGame);
                StyleStartButtonState(_currentlySelectedGameIndex);
            }
        }

        // Event Handlers

        public void Key_Pressed()
        {
            // Keylogger for AFK Timer
            if (_afkTimerActive)
            {
                _afkTimer = 0;
            }
            else
            {
                _afkTimerActive = true;
                _afkTimer = 0;
                _timeSinceLastButton = 0;

                // Show the Home Menu
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Visible;
                        SelectionMenu.Visibility = Visibility.Collapsed;
                        InputMenu.Visibility = Visibility.Collapsed;

                        _currentlySelectedHomeIndex = 0;
                        HighlightCurrentHomeMenuOption();
                    });
                }
                catch (TaskCanceledException) { }

                // Set the focus to the game launcher
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
            }
        }

        private void ResetControllerStates()
        {
            _timeSinceLastButton = 0;
            // Reset the controller states
            for (int i = 0; i < _controllerStates.Count; i++)
                _controllerStates[i].ReleaseButtons();
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            // Set the Copyright text
            Copyright.Text =
                "Copyright ©️ 2018 - "
                + DateTime.Now.Year
                + "\nUniversity of Lincoln,\nAll rights reserved.";

            // Initialize the TextBlock arrays
            _homeOptionsList = [GameLibraryText, InputMenuText, AboutText, ExitText];
            _gameTitlesList =
            [
                GameTitleText0,
                GameTitleText1,
                GameTitleText2,
                GameTitleText3,
                GameTitleText4,
                GameTitleText5,
                GameTitleText6,
                GameTitleText7,
                GameTitleText8,
                GameTitleText9,
            ];

            // Generate the Credits from the Credits.json file
            GenerateCredits();

            // Initialize the controller states
            JoyStickInit();

            LoadGameDatabase();

            // Every 30 minutes, check for updates to the updater,
            // and check for game database changes.
            Task.Run(async () =>
            {
                if (production)
                    await CheckForUpdaterUpdates();
                await CheckForGameDatabaseChanges();

                while (true)
                {
                    await Task.Delay(30 * 60 * 1000);
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(async () =>
                        {
                            if (production)
                                await CheckForUpdaterUpdates();
                            await CheckForGameDatabaseChanges();
                        });
                    }
                    catch (TaskCanceledException) { }
                }
            });

            // Perform an initial update of the game info display
            _currentlySelectedGameIndex = 0;
            UpdateGameInfoDisplay();

            // Initialize the updateTimer
            InitializeUpdateTimer();

            Task.Run(() =>
            {
                //// Delete all the audio files
                //DeleteAllAudioFiles();

                //// Download the audio files
                //DownloadAudioFiles();

                // Connect to the WebSocket Server
                string arcadeMachineName = "Arcade Machine (" + _config["ApiUser"] + ")";

                if (!production)
                    arcadeMachineName = "Arcade Machine (Test)";

                // Disable if not in production
                if (!production)
                    return;

                // Disable if WS is disabled
                if (!(bool)_config["WS_Enabled"])
                    return;

                _ = new Socket(
                    _config["WS_IP"].ToString(),
                    _config["WS_Port"].ToString(),
                    arcadeMachineName,
                    this
                );

                //// Every (between 30 mins and an hour), play a random audio file
                //Task.Run(async () =>
                //{
                //    while (true)
                //    {
                //        await Task.Delay(new Random(DateTime.Now.Millisecond).Next(30 * 60 * 1000, 60 * 60 * 1000));
                //        PlayRandomPeriodicAudioFile();
                //    }
                //});
            });
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            // If the application is undergoing maintenance, pause main loop
            if (MaintenanceScreen.Visibility == Visibility.Visible)
                return;

            // Check if the exit key sent from the updater is pressed
            if (GetAsyncKeyState(69) != 0)
            {
                // Trigger Window_Closing event
                Window_Closing(null, null);

                // Close the application
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown());
                }
                catch (TaskCanceledException) { }
            }

            // Update Controller Input
            for (int i = 0; i < _controllerStates.Count; i++)
                _controllerStates[i].UpdateButtonStates();

            // If exit is held, close the current process
            if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
            {
                int maxExitHeldFor = 0;
                for (int i = 0; i < _controllerStates.Count; i++)
                    if (_controllerStates[i].GetExitButtonHeldFor() > maxExitHeldFor)
                        maxExitHeldFor = _controllerStates[i].GetExitButtonHeldFor();

                // If the user has held the exit button for longer than 1 second, show the ForceExitMenu within the infoWindow
                if (maxExitHeldFor >= 1000)
                {
                    _infoWindow?.SetCloseGameName(
                        _gameInfoList[_currentlySelectedGameIndex]["Name"].ToString()
                    );
                    _infoWindow?.ShowWindow(InfoWindowType.ForceExit);
                    _infoWindow?.UpdateCountdown(3000 - maxExitHeldFor);
                }
                // Hide the infoWindow and set the focus back if the user has released the exit button
                else
                {
                    if (
                        _infoWindow.Visibility == Visibility.Visible
                        && _infoWindow.ForceExitMenu.Visibility == Visibility.Visible
                    )
                    {
                        _infoWindow?.HideWindow();
                        SetForegroundWindow(_currentlyRunningProcess.MainWindowHandle);
                    }
                }

                // GAME EXIT
                // If the user has held the exit button for 3 seconds, close the currently running application
                if (maxExitHeldFor >= 3000)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        ResetControllerStates();
                        _currentlyRunningProcess.Kill();
                        _infoWindow?.HideWindow();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                    });
                }
            }
            else
            {
                // Hide the infoWindow if the currently running process has exited
                if (
                    _infoWindow.Visibility == Visibility.Visible
                    && _infoWindow.ForceExitMenu.Visibility == Visibility.Visible
                )
                    _infoWindow?.HideWindow();
            }

            // If the user is AFK for the specified time (Default: 2 minutes), Warn them and then close the currently running application
            if (_afkTimer >= _noInputTimeout)
            {
                _infoWindow?.ShowWindow(InfoWindowType.Idle);
                _infoWindow?.UpdateCountdown(_noInputTimeout + 5000 - _afkTimer);

                if (_currentlyRunningProcess != null)
                    _infoWindow?.SetCloseGameName(
                        _gameInfoList[_currentlySelectedGameIndex]["Name"].ToString()
                    );
                else
                    _infoWindow?.SetCloseGameName(null);

                // If the user is AFK for 5 seconds after the warning, close the currently running application and show the Start Menu
                if (_afkTimer >= _noInputTimeout + 5000)
                {
                    _infoWindow?.UpdateCountdown(0);

                    // Reset the timer
                    _afkTimerActive = false;
                    _afkTimer = 0;

                    // Hide the Window
                    _infoWindow?.HideWindow();

                    // GAME EXIT
                    // Close the currently running application
                    if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
                    {
                        ResetControllerStates();
                        _currentlyRunningProcess.Kill();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                        _currentlyRunningProcess = null;
                    }

                    // Show the Start Menu
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StartMenu.Visibility = Visibility.Visible;
                            HomeMenu.Visibility = Visibility.Collapsed;
                            SelectionMenu.Visibility = Visibility.Collapsed;
                            InputMenu.Visibility = Visibility.Collapsed;
                        });
                    }
                    catch (TaskCanceledException) { }
                }
            }
            else
            {
                // Hide the Window
                if (
                    _infoWindow.Visibility == Visibility.Visible
                    && _infoWindow.IdleMenu.Visibility == Visibility.Visible
                )
                {
                    _infoWindow?.HideWindow();
                    _timeSinceLastButton = 0;

                    // Set the focus to currently running process
                    if (_currentlyRunningProcess != null)
                        SetForegroundWindow(_currentlyRunningProcess.MainWindowHandle);
                }
            }

            // Increment the selection animation frame
            if (
                (
                    HomeMenu.Visibility == Visibility.Visible
                    || SelectionMenu.Visibility == Visibility.Visible
                )
                && _globalCounter % _selectionAnimationFrameRate == 0
            )
            {
                if (_selectionAnimationFrame < _selectionAnimationFrames.Length - 1)
                    _selectionAnimationFrame++;
                else
                    _selectionAnimationFrame = 0;

                // Highlight the current menu option
                if (HomeMenu.Visibility == Visibility.Visible)
                {
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            HighlightCurrentHomeMenuOption();
                        });
                    }
                    catch (TaskCanceledException) { }
                }
                // Highlight the current game option
                else if (SelectionMenu.Visibility == Visibility.Visible)
                {
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            HighlightCurrentGameMenuOption();
                        });
                    }
                    catch (TaskCanceledException) { }
                }
            }

            // Update the input menu feedback
            if (InputMenu.Visibility == Visibility.Visible)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        UpdateInputMenuFeedback();
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Update the Home/Selection Menu's current selection
            if (
                HomeMenu.Visibility == Visibility.Visible
                || SelectionMenu.Visibility == Visibility.Visible
            )
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        UpdateCurrentSelection();
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Auto Scroll the Credits Panel
            if (CreditsPanel.Visibility == Visibility.Visible)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        AutoScrollCredits();
                    });
                }
                catch (TaskCanceledException) { }
            }

            // GAME EXIT
            // Check if the currently running process has exited, and set the focus back to the launcher
            if (_currentlyRunningProcess != null && _currentlyRunningProcess.HasExited)
            {
                ResetControllerStates();
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                _currentlyRunningProcess = null;

                DebounceUpdateGameInfoDisplay();
            }

            // Flash the Start Button if the Start Menu is visible
            if (StartMenu.Visibility == Visibility.Visible)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (_timeSinceLastButton % 300 == 0)
                            PressStartText.Visibility =
                                PressStartText.Visibility == Visibility.Visible
                                    ? Visibility.Hidden
                                    : Visibility.Visible;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Increment the afkTimer
            if (_afkTimerActive)
                _afkTimer += 10;

            // Reset the selectionUpdateIntervalCounter if enough time has passed from releasing the analog stick
            if (_selectionUpdateCounter > _selectionUpdateInterval)
                _selectionUpdateIntervalCounter = 0;
            // Increment selectionUpdateCounter and timeSinceLastButton
            _selectionUpdateCounter += 10;
            _timeSinceLastButton += 10;

            // Increment the global counter
            _globalCounter += 10;
        }

        private void Updater_GameStateChanged(object sender, GameStateChangedEventArgs e)
        {
            // Find the index of the game with the name e.GameName in the game database
            int gameIndex = -1;
            for (int i = 0; i < _gameInfoList.Length; i++)
            {
                if (_gameInfoList[i]["Name"].ToString() == e.GameName)
                {
                    gameIndex = i;
                    break;
                }
            }

            // If the game is not found, return
            if (gameIndex == -1)
            {
                Console.WriteLine($"{e.GameName} not found in game database.");
                return;
            }

            // Set the game title state to the new state
            SetGameTitleState(gameIndex, e.NewState);
        }

        private void Updater_GameDatabaseFetched(object sender, GameDatabaseFetchedEventArgs e)
        {
            SimplifiedGameInfo[] games = e.Games;

            // Convert to JsonArray for local storage
            JArray gameInfoArray = [];
            foreach (var game in games)
            {
                JArray gameTagsArray = [];
                foreach (var tag in game.Tags)
                {
                    JObject tagObject = new() { ["Name"] = tag.Name };

                    // If the tag has a colour, add it to the tagObject
                    if (tag.Colour != null)
                        tagObject["Colour"] = tag.Colour.ToString();

                    gameTagsArray.Add(tagObject);
                }

                JObject gameInfo = new()
                {
                    ["VersionNumber"] = game.VersionNumber,
                    ["Name"] = game.Name,
                    ["Description"] = game.Description,
                    ["ThumbnailUrl"] = game.ThumbnailUrl,
                    ["Authors"] = new JArray(game.Authors),
                    ["Tags"] = gameTagsArray,
                    ["NameOfExecutable"] = game.NameOfExecutable,
                    ["FolderName"] = game.FolderName,
                };
                gameInfoArray.Add(gameInfo);
            }

            // Write the game info array to the local game database file
            File.WriteAllText(
                Path.Combine(_gameDirectoryPath, "GameDatabase.json"),
                gameInfoArray.ToString((Newtonsoft.Json.Formatting)Formatting.Indented)
            );

            // Update the gameInfoList with the new game info array
            _gameInfoList = new JObject[gameInfoArray.Count];
            for (int i = 0; i < gameInfoArray.Count; i++)
                _gameInfoList[i] = (JObject)gameInfoArray[i];

            _gameTitleStates = new GameState[_gameInfoList.Length];
            for (int i = 0; i < _gameInfoList.Length; i++)
                _gameTitleStates[i] = GameState.fetchingInfo;

            // Show the game titles as "Loading..." until the game database is updated
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    for (int i = _previousPageIndex * 10; i < (_previousPageIndex + 1) * 10; i++)
                    {
                        if (i < _gameInfoList.Length)
                        {
                            if (_gameTitlesList[i % 10].Text != _gameInfoList[i]["Name"].ToString())
                                _gameTitlesList[i % 10].Text = "Loading...";
                            _gameTitlesList[i % 10].Visibility = Visibility.Visible;
                        }
                        else
                            _gameTitlesList[i % 10].Visibility = Visibility.Hidden;
                    }
                });
            }
            catch (TaskCanceledException) { }

            // Update the game info display if the currently selected game is in the new game database
            DebounceUpdateGameInfoDisplay();
        }

        private void Updater_GameUpdateCompleted(object sender, GameUpdateCompletedEventArgs e)
        {
            // Find the index of the game with the name e.GameName in the game database
            int gameIndex = -1;
            for (int i = 0; i < _gameInfoList.Length; i++)
            {
                if (_gameInfoList[i]["Name"].ToString() == e.GameName)
                {
                    gameIndex = i;
                    break;
                }
            }

            // If the game is not found, return
            if (gameIndex == -1)
            {
                Console.WriteLine($"{e.GameName} not found in game database.");
                return;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Update the game title text block if it's visible
                if (
                    gameIndex >= _previousPageIndex * 10
                    && gameIndex < (_previousPageIndex + 1) * 10
                )
                {
                    _gameTitlesList[gameIndex % 10].Text = _emojiParser.ReplaceColonNames(
                        _gameInfoList[gameIndex]["Name"].ToString()
                    );
                    _gameTitlesList[gameIndex % 10].Visibility = Visibility.Visible;
                }

                SetGameTitleState(gameIndex, GameState.ready);
            });

            if (_currentlySelectedGameIndex == gameIndex)
                DebounceUpdateGameInfoDisplay();
        }

        private void Updater_CloseGameAndUpdater(object sender, EventArgs e)
        {
            // Alert the user that the application is Undergoing Maintenance
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StartMenu.Visibility = Visibility.Collapsed;
                    HomeMenu.Visibility = Visibility.Collapsed;
                    SelectionMenu.Visibility = Visibility.Collapsed;

                    MaintenanceScreen.Visibility = Visibility.Visible;
                });
            }
            catch (TaskCanceledException) { }

            // Close the currently running process
            if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
            {
                _currentlyRunningProcess.Kill();
                _currentlyRunningProcess = null;
            }

            // Find the Updater process and close it
            Process[] processes = Process.GetProcessesByName("Research-Arcade-Updater");
            foreach (Process process in processes)
                process.Kill();
        }

        private void Updater_RelaunchUpdater(object sender, EventArgs e)
        {
            // Start the new updater
            Process.Start(
                Path.Combine(Directory.GetCurrentDirectory(), "Research-Arcade-Updater.exe")
            );

            // Close the current application
            try
            {
                Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown());
            }
            catch (TaskCanceledException) { }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Close the currently running process
            if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
                _currentlyRunningProcess.Kill();

            // Close the updateTimer
            _updateTimer?.Close();
        }

        // Misc

        public static void RestartLauncher() =>
            Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown());

        // Credits

        private void GenerateCredits()
        {
            // Read the Credits.json file
            string creditsPath = Path.Combine(_applicationPath, "Configuration", "Credits.json");

            if (File.Exists(creditsPath))
            {
                // Parse the Credits.json file and get the Credits array
                JObject creditsFile = JObject.Parse(File.ReadAllText(creditsPath));
                JArray creditsArray = (JArray)creditsFile["Credits"];

                // Clear the CreditsPanel
                CreditsPanel.RowDefinitions.Clear();
                CreditsPanel.Children.Clear();

                // Create a new TextBlock for each credit
                for (int i = 0; i < creditsArray.Count; i++)
                {
                    JObject creditsObject = (JObject)creditsArray[i];

                    switch (creditsObject["Type"].ToString())
                    {
                        case "Title":
                            // Create a new RowDefinition
                            RowDefinition titleRow = new() { Height = new(60, GridUnitType.Pixel) };
                            CreditsPanel.RowDefinitions.Add(titleRow);

                            // Create a new Grid
                            Grid titleGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(titleGrid, 2 * i);

                            // Create 2 new RowDefinitions
                            RowDefinition titleGridTitleRow = new()
                            {
                                Height = new GridLength(40, GridUnitType.Pixel),
                            };
                            titleGrid.RowDefinitions.Add(titleGridTitleRow);

                            RowDefinition titleGridSubtitleRow = new()
                            {
                                Height = new(20, GridUnitType.Pixel),
                            };
                            titleGrid.RowDefinitions.Add(titleGridSubtitleRow);

                            // Create a new TextBlock (Title)
                            TextBlock titleText = new()
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 24,
                                Foreground = new SolidColorBrush(
                                    Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)
                                ),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };

                            // Add the TextBlock to the Grid
                            Grid.SetRow(titleText, 0);
                            titleGrid.Children.Add(titleText);

                            // Create a new TextBlock (Subtitle)
                            if (creditsObject["Subtitle"] != null)
                            {
                                TextBlock subtitleText = new()
                                {
                                    Text = creditsObject["Subtitle"].ToString(),
                                    Style = (Style)FindResource("Early GameBoy"),
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(
                                        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                                    ),
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Center,
                                };

                                // Add the TextBlock to the Grid
                                Grid.SetRow(subtitleText, 1);
                                titleGrid.Children.Add(subtitleText);
                            }

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(titleGrid);

                            break;
                        case "Heading":
                            // Check the Subheadings property
                            JArray subheadingsArray = (JArray)creditsObject["Subheadings"];

                            // Create a new RowDefinition
                            RowDefinition headingRow = new()
                            {
                                Height = new GridLength(
                                    30 + (subheadingsArray.Count * 25),
                                    GridUnitType.Pixel
                                ),
                            };
                            CreditsPanel.RowDefinitions.Add(headingRow);

                            // Create a new Grid
                            Grid headingGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(headingGrid, 2 * i);

                            // Create 2 new ColumnDefinitions
                            ColumnDefinition headingGridBorderColumn = new()
                            {
                                Width = new(3, GridUnitType.Pixel),
                            };
                            headingGrid.ColumnDefinitions.Add(headingGridBorderColumn);

                            ColumnDefinition headingGridContentColumn = new()
                            {
                                Width = new(1, GridUnitType.Star),
                            };
                            headingGrid.ColumnDefinitions.Add(headingGridContentColumn);

                            // Create a Grid to function as a border
                            Grid headingBorderGrid = new()
                            {
                                Background = new SolidColorBrush(
                                    Color.FromArgb(0xFF, 0x33, 0x33, 0x33)
                                ),
                                Margin = new(0, 10, 0, 10),
                            };

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingBorderGrid, 0);
                            headingGrid.Children.Add(headingBorderGrid);

                            // Create a new Grid to hold the Title and Subheadings
                            Grid headingContentGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new(25, 0, 0, 0),
                            };

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingContentGrid, 1);
                            headingGrid.Children.Add(headingContentGrid);

                            // Create 2 new RowDefinitions
                            RowDefinition headingGridTitleRow = new()
                            {
                                Height = new(30, GridUnitType.Pixel),
                            };
                            headingContentGrid.RowDefinitions.Add(headingGridTitleRow);

                            RowDefinition headingGridSubheadingsRow = new()
                            {
                                Height = new(subheadingsArray.Count * 25, GridUnitType.Pixel),
                            };
                            headingContentGrid.RowDefinitions.Add(headingGridSubheadingsRow);

                            // Create a new TextBlock (Title)
                            TextBlock headingText = new()
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 16,
                                Foreground = new SolidColorBrush(
                                    Color.FromArgb(0xFF, 0x85, 0x8E, 0xFF)
                                ),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };

                            // Add the TextBlock to the Grid
                            Grid.SetRow(headingText, 0);
                            headingContentGrid.Children.Add(headingText);

                            // Create a new Grid for the Subheadings
                            Grid subheadingsGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(subheadingsGrid, 1);

                            // For each Subheading
                            for (int j = 0; j < subheadingsArray.Count; j++)
                            {
                                // Create new RowDefinitions & for each Subheading
                                RowDefinition subheadingRow = new()
                                {
                                    Height = new(25, GridUnitType.Pixel),
                                };
                                subheadingsGrid.RowDefinitions.Add(subheadingRow);

                                string colour =
                                    subheadingsArray[j]["Colour"] != null
                                        ? subheadingsArray[j]["Colour"].ToString()
                                        : "White";

                                // Create a new TextBlock (Subheading)
                                TextBlock subheadingText = new()
                                {
                                    Text = subheadingsArray[j]["Value"].ToString(),
                                    Style = (Style)FindResource("Early GameBoy"),
                                    FontSize = 18,
                                    Foreground = new SolidColorBrush(
                                        (Color)ColorConverter.ConvertFromString(colour)
                                    ),
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Center,
                                };

                                // Add the TextBlock to the Grid
                                Grid.SetRow(subheadingText, j);
                                subheadingsGrid.Children.Add(subheadingText);
                            }

                            // Add the Subheading Grid to the Heading Grid
                            headingContentGrid.Children.Add(subheadingsGrid);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(headingGrid);

                            break;
                        case "Note":
                            int noteHeight =
                                20 + (creditsObject["Value"].ToString().Length / 80 * 20);

                            // Create a new RowDefinition
                            RowDefinition noteRow = new()
                            {
                                Height = new(noteHeight, GridUnitType.Pixel),
                            };
                            CreditsPanel.RowDefinitions.Add(noteRow);

                            // Create a new Grid
                            Grid noteGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new(0, 0, 100, 0),
                            };
                            Grid.SetRow(noteGrid, 2 * i);

                            // Create a new TextBlock
                            TextBlock noteText = new()
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 11,
                                Foreground = new SolidColorBrush(
                                    Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                                ),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                LineHeight = 20,
                            };

                            // Add the TextBlock to the Grid
                            noteGrid.Children.Add(noteText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(noteGrid);

                            break;
                        case "Break":
                            // Create a new RowDefinition
                            RowDefinition breakRow = new() { Height = new(10, GridUnitType.Pixel) };
                            CreditsPanel.RowDefinitions.Add(breakRow);

                            // Create a new Grid
                            Grid breakGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(breakGrid, 2 * i);

                            // Create a new TextBlock
                            TextBlock breakText = new()
                            {
                                Text = "----------------------",
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 16,
                                Foreground = new SolidColorBrush(
                                    Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
                                ),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };

                            // Add the TextBlock to the Grid
                            breakGrid.Children.Add(breakText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(breakGrid);

                            break;
                        case "Image":
                            double overrideHeight =
                                creditsObject["HeightOverride"] != null
                                    ? double.Parse(creditsObject["HeightOverride"].ToString())
                                    : 100;
                            string stretch =
                                creditsObject["Stretch"] != null
                                    ? creditsObject["Stretch"].ToString()
                                    : "Uniform";

                            // Create a new RowDefinition
                            RowDefinition imageRow = new()
                            {
                                Height = new(overrideHeight, GridUnitType.Pixel),
                            };
                            CreditsPanel.RowDefinitions.Add(imageRow);

                            // Create a new Grid
                            Grid imageGrid = new()
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(imageGrid, 2 * i);

                            string imagePath = creditsObject["Path"].ToString();

                            // Create a new Image (Static)
                            Image imageStatic = new()
                            {
                                Source = new BitmapImage(new Uri(imagePath, UriKind.Relative)),
                            };

                            // Set Image Stretch
                            switch (stretch)
                            {
                                case "Fill":
                                    imageStatic.Stretch = Stretch.Fill;
                                    break;
                                case "None":
                                    imageStatic.Stretch = Stretch.None;
                                    break;
                                case "Uniform":
                                    imageStatic.Stretch = Stretch.Uniform;
                                    break;
                                case "UniformToFill":
                                    imageStatic.Stretch = Stretch.UniformToFill;
                                    break;
                            }

                            // Add the Image to the Grid
                            imageGrid.Children.Add(imageStatic);

                            // Set Grid Height to Image Height
                            imageGrid.Height = overrideHeight;

                            // Create a new Image (Gif)
                            if (imagePath.EndsWith(".gif"))
                            {
                                // Copy GifTemplateElement_Parent's child element to make a new Image
                                Image imageGif = CloneXamlElement(
                                    (Image)GifTemplateElement_Parent.Children[0]
                                );
                                AnimationBehavior.SetSourceUri(
                                    imageGif,
                                    new(imagePath, UriKind.Relative)
                                );

                                // Set Image Stretch
                                switch (stretch)
                                {
                                    case "Fill":
                                        imageGif.Stretch = Stretch.Fill;
                                        break;
                                    case "None":
                                        imageGif.Stretch = Stretch.None;
                                        break;
                                    case "Uniform":
                                        imageGif.Stretch = Stretch.Uniform;
                                        break;
                                    case "UniformToFill":
                                        imageGif.Stretch = Stretch.UniformToFill;
                                        break;
                                }

                                // Add the Image to the Grid
                                imageGrid.Children.Add(imageGif);

                                AnimationBehavior.AddLoadedHandler(
                                    imageGif,
                                    (sender, e) =>
                                    {
                                        // Hide the static image
                                        imageStatic.Visibility = Visibility.Collapsed;
                                    }
                                );
                            }

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(imageGrid);

                            break;
                        default:
                            break;
                    }

                    // Create a space between each credit
                    if (i < creditsArray.Count - 1)
                    {
                        // Create a new RowDefinition
                        RowDefinition spaceRow = new() { Height = new(40, GridUnitType.Pixel) };
                        CreditsPanel.RowDefinitions.Add(spaceRow);
                    }
                }
            }
            else
            {
                // If the Credits.json file does not exist, show an error message
                MessageBox.Show("Failed to generate credits: Credits.json does not exist.");
            }
        }

        private void AutoScrollCredits()
        {
            // Change Canvas.Top of the CreditsPanel
            double currentTop = Canvas.GetTop(CreditsPanel);
            double newTop = currentTop - (double)0.5;
            Canvas.SetTop(CreditsPanel, newTop);

            // If the CreditsPanel is off the screen, reset it to the bottom
            string logicalScreenHeight_str = TryFindResource("LogicalSizeHeight").ToString();
            double logicalScreenHeight = double.Parse(logicalScreenHeight_str);

            if (newTop < -CreditsPanel.ActualHeight)
                Canvas.SetTop(CreditsPanel, logicalScreenHeight);
        }

        // Getters & Setters

        private SolidColorBrush GetCurrentSelectionAnimationBrush() =>
            _selectionAnimationFrames[_selectionAnimationFrame];

        private void SetGameTitleState(int _index, GameState _state)
        {
            // Set the game title state
            _gameTitleStates[_index] = _state;

            // Style the Start Button based on the game title state
            if (_currentlySelectedGameIndex == _index)
                StyleStartButtonState(_index);
        }

        // Update Methods

        private void UpdateCurrentSelection()
        {
            bool updatedIntervalCounter = false;

            // For each Controller State
            for (int i = 0; i < _controllerStates.Count; i++)
            {
                // If the infoWindow is visible, don't listen for inputs
                if (_infoWindow.Visibility == Visibility.Visible)
                    return;

                // If theres a game running, don't listen for inputs
                if (_currentlyRunningProcess != null && !_currentlyRunningProcess.HasExited)
                    return;

                // Use a multiplier to speed up the selection update when the stick is held in either direction
                double multiplier = 1.00;
                if (_selectionUpdateIntervalCounter > 0)
                    multiplier =
                        (double)1.00
                        - (
                            (double)_selectionUpdateIntervalCounter
                            / ((double)_selectionUpdateIntervalCounterMax * 1.6)
                        );

                // If the selection update counter is greater than the selection update interval, update the selection
                if (_selectionUpdateCounter >= _selectionUpdateInterval * multiplier)
                {
                    int[] leftStickDirection = _controllerStates[i].GetLeftStickDirection();

                    // If the left of right stick's direction is up
                    if (leftStickDirection[1] == -1)
                    {
                        // Reset the selection update counter and increment the selection update interval counter
                        _selectionUpdateCounter = 0;
                        if (
                            _selectionUpdateIntervalCounter < _selectionUpdateIntervalCounterMax
                            && !updatedIntervalCounter
                        )
                        {
                            _selectionUpdateIntervalCounter++;
                            updatedIntervalCounter = true;
                        }

                        // If the Home Menu is visible, decrement the currently selected Home Index
                        if (HomeMenu.Visibility == Visibility.Visible)
                        {
                            _currentlySelectedHomeIndex -= 1;
                            if (_currentlySelectedHomeIndex < 0)
                                _currentlySelectedHomeIndex = 0;

                            // Highlight the current Home Menu Option
                            HighlightCurrentHomeMenuOption();
                        }
                        // If the Selection Menu is visible, decrement the currently selected Game Index
                        else if (
                            SelectionMenu.Visibility == Visibility.Visible
                            && _gameInfoList != null
                        )
                        {
                            _currentlySelectedGameIndex -= 1;
                            if (_currentlySelectedGameIndex < -1)
                                _currentlySelectedGameIndex = -1;
                            else
                            {
                                // Highlight the current Game Menu Option and debounce the game info display update
                                HighlightCurrentGameMenuOption();
                                DebounceUpdateGameInfoDisplay();
                            }
                        }
                    }
                    // If the left of right stick's direction is down
                    else if (leftStickDirection[1] == 1)
                    {
                        // Reset the selection update counter and increment the selection update interval counter
                        _selectionUpdateCounter = 0;
                        if (
                            _selectionUpdateIntervalCounter < _selectionUpdateIntervalCounterMax
                            && !updatedIntervalCounter
                        )
                        {
                            _selectionUpdateIntervalCounter++;
                            updatedIntervalCounter = true;
                        }

                        // If the Home Menu is visible, increment the currently selected Home Index
                        if (HomeMenu.Visibility == Visibility.Visible)
                        {
                            _currentlySelectedHomeIndex += 1;
                            if (_currentlySelectedHomeIndex > _homeOptionsList.Length - 1)
                                _currentlySelectedHomeIndex = _homeOptionsList.Length - 1;

                            // Highlight the current Home Menu Option
                            HighlightCurrentHomeMenuOption();
                        }
                        // If the Selection Menu is visible, increment the currently selected Game Index
                        else if (
                            SelectionMenu.Visibility == Visibility.Visible
                            && _gameInfoList != null
                        )
                        {
                            _currentlySelectedGameIndex += 1;
                            if (_currentlySelectedGameIndex > _gameInfoList.Length - 1)
                                _currentlySelectedGameIndex = _gameInfoList.Length - 1;
                            else
                            {
                                // Highlight the current Game Menu Option and debounce the game info display update
                                HighlightCurrentGameMenuOption();
                                DebounceUpdateGameInfoDisplay();
                            }
                        }
                    }
                    else
                        _selectionUpdateIntervalCounter = 0;
                }

                // Check if the Start/A button is pressed
                if (
                    _timeSinceLastButton > 250
                    && (
                        _controllerStates[i]
                            .GetButtonDownState(ControllerState.ControllerButtons.Start)
                        || _controllerStates[i]
                            .GetButtonDownState(ControllerState.ControllerButtons.A)
                    )
                )
                {
                    // Reset the time since the last button press
                    _timeSinceLastButton = 0;

                    // Check if the Home Menu is visible
                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        // If the Game Library option is selected
                        if (_homeOptionsList[_currentlySelectedHomeIndex] == GameLibraryText)
                        {
                            // Show the Selection Menu
                            GameLibraryButton_Click(null, null);
                        }
                        // If the About option is selected
                        else if (_homeOptionsList[_currentlySelectedHomeIndex] == AboutText)
                        {
                            // Show the Credits
                            AboutButton_Click(null, null);
                        }
                        else if (_homeOptionsList[_currentlySelectedHomeIndex] == InputMenuText)
                        {
                            // Show the Input Menu
                            InputMenuButton_Click(null, null);
                        }
                        // If the Exit option is selected
                        else if (_homeOptionsList[_currentlySelectedHomeIndex] == ExitText)
                        {
                            // Go back to the Start Menu
                            ExitButton_Click(null, null);
                        }
                    }
                    // Else check if the Selection Menu is visible
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        // If a game is selected, attempt to start the game
                        if (_currentlySelectedGameIndex >= 0)
                            Task.Run(() =>
                            {
                                StartButton_Click(null, null);
                            });
                        // If the back button is selected, return to the Home Menu
                        else
                            BackFromGameLibraryButton_Click(null, null);
                    }
                }

                // Check if the Exit/B button is pressed
                if (
                    _timeSinceLastButton > 250
                    && (
                        _controllerStates[i]
                            .GetButtonDownState(ControllerState.ControllerButtons.Exit)
                        || _controllerStates[i]
                            .GetButtonDownState(ControllerState.ControllerButtons.B)
                    )
                )
                {
                    // Reset the time since the last button press
                    _timeSinceLastButton = 0;

                    // If the Home Menu is visible
                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        // Go back to the Start Menu
                        ExitButton_Click(null, null);
                    }
                    // Else if the Selection Menu is visible
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        // Go back to the Home Menu
                        BackFromGameLibraryButton_Click(null, null);
                    }
                }
            }
        }

        private void HighlightCurrentHomeMenuOption()
        {
            // Reset the colour of all Home Menu Options and remove the "<" character if present
            foreach (TextBlock option in _homeOptionsList)
            {
                option.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                if (option.Text.EndsWith(" <"))
                    option.Text = option.Text[..^2];
            }

            // Highlight the currently selected Home Menu Option and add the "<" character
            _homeOptionsList[_currentlySelectedHomeIndex].Foreground =
                GetCurrentSelectionAnimationBrush();
            if (!_homeOptionsList[_currentlySelectedHomeIndex].Text.EndsWith(" <"))
                _homeOptionsList[_currentlySelectedHomeIndex].Text += " <";
        }

        private void HighlightCurrentGameMenuOption()
        {
            // Reset the colour of all Game Menu Options and remove the "<" character if present
            foreach (TextBlock title in _gameTitlesList)
            {
                title.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                if (title.Text.EndsWith(" <"))
                    title.Text = title.Text.Substring(0, title.Text.Length - 2);
            }

            // Check if the current page needs to be changed
            int pageIndex = _currentlySelectedGameIndex / 10;
            if (pageIndex != _previousPageIndex)
                ChangePage(pageIndex);

            //If a game is selected
            if (_currentlySelectedGameIndex >= 0)
            {
                // Highlight the currently selected Game Menu Option and add the "<" character
                _gameTitlesList[_currentlySelectedGameIndex % 10].Foreground =
                    GetCurrentSelectionAnimationBrush();
                if (!_gameTitlesList[_currentlySelectedGameIndex % 10].Text.EndsWith(" <"))
                    _gameTitlesList[_currentlySelectedGameIndex % 10].Text += " <";

                // Style the Back Button
                BackFromGameLibraryButton.IsChecked = false;
            }

            // Check if the page needs to be changed
            if (_currentlySelectedGameIndex < 0)
            {
                // Reset the game info display
                ResetGameInfoDisplay();

                // Highlight the Back Button and disable the Start Button
                BackFromGameLibraryButton.IsChecked = true;
                StartButton.IsChecked = false;
                StartButton.Content = "Select a Game";
                StartButton.IsEnabled = false;

                return;
            }
        }

        private void UpdateInputMenuFeedback()
        {
            SolidColorBrush activeFillColour = new(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00));
            SolidColorBrush activeBorderColour = new(Color.FromArgb(0xFF, 0x00, 0xBB, 0x00));

            SolidColorBrush inactiveFillColour = new(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
            SolidColorBrush inactiveBorderColour = new(Color.FromArgb(0xFF, 0xBB, 0x00, 0x00));

            int exitHeldMilliseconds = 1500;

            // Update the held countdown text
            int maxExitHeldFor = 0;
            for (int i = 0; i < _controllerStates.Count; i++)
                if (_controllerStates[i].GetExitButtonHeldFor() > maxExitHeldFor)
                    maxExitHeldFor = _controllerStates[i].GetExitButtonHeldFor();

            if (maxExitHeldFor > 0)
                InputMenu_HoldBackCountdownText.Text = (
                    (double)(exitHeldMilliseconds - maxExitHeldFor) / 1000
                ).ToString("0.0");
            else
                InputMenu_HoldBackCountdownText.Text = "";

            // Check if the exit button has been held for 1.5 seconds, if so, go back to the Start Menu
            if (maxExitHeldFor >= exitHeldMilliseconds)
                ExitButton_Click(null, null);

            // For each Controller State
            for (int i = 0; i < _controllerStates.Count; i++)
            {
                // Joystick Input
                int[] leftStickDirection = _controllerStates[i].GetLeftStickDirection();
                _inputMenuJoysticks[i].Margin = new(
                    leftStickDirection[0] * 50,
                    leftStickDirection[1] * 50,
                    0,
                    0
                );

                // For each button in the Input Menu
                for (int j = 0; j < _inputMenuButtons[i].Length; j++)
                {
                    if (_inputMenuButtons[i][j] == null)
                        continue;

                    // If the user is pressing the button, highlight the button
                    if (_controllerStates[i].GetButtonState((ControllerButtons)j))
                    {
                        _inputMenuButtons[i][j].Fill = activeFillColour;
                        _inputMenuButtons[i][j].Stroke = activeBorderColour;
                    }
                    else
                    {
                        _inputMenuButtons[i][j].Fill = inactiveFillColour;
                        _inputMenuButtons[i][j].Stroke = inactiveBorderColour;
                    }
                }
            }
        }

        private void ChangePage(int _pageIndex)
        {
            // Check if the page index is within the bounds of the game info files list
            if (_pageIndex < 0)
                _pageIndex = 0;
            else if (_pageIndex > _gameInfoList.Length / 10)
                _pageIndex = _gameInfoList.Length / 10;

            // Set the previous page index to the current page index
            _previousPageIndex = _pageIndex;

            ResetTitles();

            // For each title on the current page
            for (int i = 0; i < 10; i++)
            {
                // Break if the current index is out of bounds
                if (i + _pageIndex * 10 >= _gameInfoList.Length)
                    break;

                if (_gameInfoList[i + _pageIndex * 10] == null)
                {
                    // Set the text to "Loading..." and make it visible
                    _gameTitlesList[i].Text = "Loading...";
                    _gameTitlesList[i].Visibility = Visibility.Visible;
                }
                else
                {
                    // Set the text to the game title and make it visible
                    _gameTitlesList[i].Text = _gameInfoList[i + _pageIndex * 10]["Name"].ToString();
                    _gameTitlesList[i].Visibility = Visibility.Visible;
                }
            }

            // Show the up scroll arrow if there is a previous page and the down scroll arrow if there is a next page
            if (_pageIndex > 0)
                ScrollArrow_Up.Visibility = Visibility.Visible;
            else
                ScrollArrow_Up.Visibility = Visibility.Collapsed;

            if (_gameInfoList.Length > (_pageIndex + 1) * 10)
                ScrollArrow_Down.Visibility = Visibility.Visible;
            else
                ScrollArrow_Down.Visibility = Visibility.Collapsed;
        }

        private void UpdateGameInfoDisplay()
        {
            if (_gameInfoList == null || _gameInfoList.Length == 0)
                _currentlySelectedGameIndex = -1;

            // Update the game info
            if (
                _currentlySelectedGameIndex != -1
                && _gameInfoList[_currentlySelectedGameIndex] != null
            )
            {
                ResetGameInfoDisplay();

                StartButton.IsChecked = true;

                // Set the Game Thumbnail
                if (
                    _gameInfoList[_currentlySelectedGameIndex]
                        ["ThumbnailUrl"]
                        .ToString()
                        .StartsWith("http")
                )
                {
                    NonGif_GameThumbnail.Source = new BitmapImage(
                        new(
                            _gameInfoList[_currentlySelectedGameIndex]["ThumbnailUrl"].ToString(),
                            UriKind.Absolute
                        )
                    );
                    AnimationBehavior.SetSourceUri(
                        Gif_GameThumbnail,
                        new(
                            _gameInfoList[_currentlySelectedGameIndex]["ThumbnailUrl"].ToString(),
                            UriKind.Absolute
                        )
                    );
                }
                else
                {
                    if (
                        File.Exists(
                            Path.Combine(
                                _gameDirectoryPath,
                                _gameInfoList[_currentlySelectedGameIndex]["FolderName"].ToString(),
                                _gameInfoList[_currentlySelectedGameIndex]
                                    ["ThumbnailUrl"]
                                    .ToString()
                            )
                        )
                    )
                    {
                        NonGif_GameThumbnail.Source = new BitmapImage(
                            new(
                                Path.Combine(
                                    _gameDirectoryPath,
                                    _gameInfoList[_currentlySelectedGameIndex]
                                        ["FolderName"]
                                        .ToString(),
                                    _gameInfoList[_currentlySelectedGameIndex]
                                        ["ThumbnailUrl"]
                                        .ToString()
                                ),
                                UriKind.Absolute
                            )
                        );
                        AnimationBehavior.SetSourceUri(
                            Gif_GameThumbnail,
                            new(
                                Path.Combine(
                                    _gameDirectoryPath,
                                    _gameInfoList[_currentlySelectedGameIndex]
                                        ["FolderName"]
                                        .ToString(),
                                    _gameInfoList[_currentlySelectedGameIndex]
                                        ["ThumbnailUrl"]
                                        .ToString()
                                ),
                                UriKind.Absolute
                            )
                        );
                    }
                }

                // Set the Game Info and Authors
                GameTitle.FitTextToTextBlock(
                    desiredText: _emojiParser.ReplaceColonNames(
                        _gameInfoList[_currentlySelectedGameIndex]["Name"].ToString()
                    ),
                    targetFontSize: 32,
                    maxLines: 1,
                    minFontSize: 8,
                    precision: 0.1
                );
                GameAuthors.FitTextToTextBlock(
                    desiredText: string.Join(
                        ", ",
                        _gameInfoList[_currentlySelectedGameIndex]["Authors"].ToObject<string[]>()
                    ),
                    targetFontSize: 14,
                    maxLines: 2,
                    minFontSize: 8,
                    precision: 0.1
                );

                // Fetch the Game Tag Elements (Borders and TextBlocks)
                Border[] GameTagBorder =
                [
                    GameTagBorder0,
                    GameTagBorder1,
                    GameTagBorder2,
                    GameTagBorder3,
                    GameTagBorder4,
                    GameTagBorder5,
                    GameTagBorder6,
                    GameTagBorder7,
                    GameTagBorder8,
                ];
                TextBlock[] GameTag =
                [
                    GameTag0,
                    GameTag1,
                    GameTag2,
                    GameTag3,
                    GameTag4,
                    GameTag5,
                    GameTag6,
                    GameTag7,
                    GameTag8,
                ];
                JArray tags = (JArray)_gameInfoList[_currentlySelectedGameIndex]["Tags"];

                // For each Stated Game Tag
                for (int j = 0; j < tags.Count; j++)
                {
                    // Change Visibility
                    GameTagBorder[j].Visibility = Visibility.Visible;

                    // Change Text Content
                    GameTag[j].Text = _emojiParser.ReplaceColonNames(tags[j]["Name"].ToString());

                    // Change Border and Text Colour
                    string colour = "#FF777777";

                    // If the Colour is not null or empty, set the colour
                    if (tags[j]["Colour"] != null && tags[j]["Colour"].ToString() != "")
                        colour = tags[j]["Colour"].ToString();

                    // Set the Border and Text Colour
                    GameTag[j].Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(colour)
                    );
                    GameTagBorder[j].BorderBrush = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(colour)
                    );
                }

                // Set the Game Description and Version
                GameDescription.FitTextToTextBlock(
                    desiredText: _emojiParser.ReplaceColonNames(
                        // Make sure to replace \n with an actual newline character in the description
                        _gameInfoList[_currentlySelectedGameIndex]
                            ["Description"]
                            .ToString()
                            .Replace("\\n", "\n")
                    ),
                    targetFontSize: 14,
                    maxLines: 100,
                    minFontSize: 8,
                    precision: 0.1
                );
                VersionText.Text =
                    "v" + _gameInfoList[_currentlySelectedGameIndex]["VersionNumber"].ToString();

                _showingDebouncedGame = true;
            }

            if (_currentlySelectedGameIndex >= 0)
                StyleStartButtonState(_currentlySelectedGameIndex);
        }

        public void StyleStartButtonState(int _index) =>
            StyleStartButtonState(_gameTitleStates[_index]);

        private void StyleStartButtonState(GameState _gameState)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Style the StartButton
                    switch (_gameState)
                    {
                        case GameState.fetchingInfo:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Fetching Game Info...";
                            break;
                        case GameState.checkingForUpdates:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Checking for Updates...";
                            break;
                        case GameState.downloadingGame:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Downloading Game...";
                            break;
                        case GameState.downloadingUpdate:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Updating Game...";
                            break;
                        case GameState.failed:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Failed";
                            break;
                        case GameState.loadingInfo:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Loading Game Info...";
                            break;
                        case GameState.ready:
                            StartButton.IsChecked = true;
                            StartButton.Content = "Start";
                            break;
                        case GameState.launching:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Launching Game...";
                            break;
                        case GameState.runningGame:
                            StartButton.IsChecked = false;
                            StartButton.Content = "Running Game...";
                            break;
                        default:
                            break;
                    }
                });
            }
            catch (TaskCanceledException) { }
        }

        // Reset Methods

        private void ResetTitles()
        {
            // Reset the visibility of all titles
            for (int i = 0; i < 10; i++)
                _gameTitlesList[i].Visibility = Visibility.Hidden;
        }

        private void ResetGameInfoDisplay()
        {
            // Reset the Thumbnail
            NonGif_GameThumbnail.Source = new BitmapImage(
                new("Assets/Images/ThumbnailPlaceholder.png", UriKind.Relative)
            );
            AnimationBehavior.SetSourceUri(
                Gif_GameThumbnail,
                new("Assets/Images/ThumbnailPlaceholder.png", UriKind.Relative)
            );

            // Reset the Text Content of each element
            GameTitle.Text = "Select A Game";
            GameAuthors.Text = "";
            GameDescription.Text = "Select a game using the joystick and by pressing A.";
            VersionText.Text = "";

            GameTag0.Text = "";
            GameTag1.Text = "";
            GameTag2.Text = "";
            GameTag3.Text = "";
            GameTag4.Text = "";
            GameTag5.Text = "";
            GameTag6.Text = "";
            GameTag7.Text = "";
            GameTag8.Text = "";

            // Reset the Visibility of each Game Tag
            GameTagBorder0.Visibility = Visibility.Hidden;
            GameTagBorder1.Visibility = Visibility.Hidden;
            GameTagBorder2.Visibility = Visibility.Hidden;
            GameTagBorder3.Visibility = Visibility.Hidden;
            GameTagBorder4.Visibility = Visibility.Hidden;
            GameTagBorder5.Visibility = Visibility.Hidden;
            GameTagBorder6.Visibility = Visibility.Hidden;
            GameTagBorder7.Visibility = Visibility.Hidden;
            GameTagBorder8.Visibility = Visibility.Hidden;

            // Reset the Border and Text Colour of each Game Tag
            GameTag0.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag1.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag2.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag3.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag4.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag5.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag6.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag7.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTag8.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));

            GameTagBorder0.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder1.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder2.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder3.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder4.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder5.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder6.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder7.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
            GameTagBorder8.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0xFF, 0x77, 0x77, 0x77)
            );
        }

        // Custom Methods (Debounce, CloneXamlElement, EncodeOneDriveLink, PlayAudioFile)

        private void DebounceUpdateGameInfoDisplay()
        {
            if (_gameInfoList == null)
            {
                _currentlySelectedGameIndex = -1;
                return;
            }

            if (
                _currentlySelectedGameIndex >= 0
                && _currentlySelectedGameIndex < _gameInfoList.Length
            )
                StyleStartButtonState(GameState.loadingInfo);

            _showingDebouncedGame = false;

            if (_updateGameInfoDisplayDebounceTimer != null)
            {
                _updateGameInfoDisplayDebounceTimer.Stop();
                _updateGameInfoDisplayDebounceTimer.Dispose();
            }

            // Create a new Timer for Debouncing the UpdateGameInfoDisplay method to prevent it from being called more than once every 500ms
            _updateGameInfoDisplayDebounceTimer = new System.Timers.Timer(500);
            _updateGameInfoDisplayDebounceTimer.Elapsed += (sender, e) =>
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        UpdateGameInfoDisplay();
                    });
                }
                catch (TaskCanceledException) { }
            };

            // Set the Timer to AutoReset and Enabled
            _updateGameInfoDisplayDebounceTimer.AutoReset = false;
            _updateGameInfoDisplayDebounceTimer.Enabled = true;
        }

        private static T CloneXamlElement<T>(T element)
            where T : UIElement
        {
            // Clone the XAML element and return it
            string xaml = XamlWriter.Save(element);
            StringReader stringReader = new(xaml);
            XmlReader xmlReader = XmlReader.Create(stringReader);
            return (T)XamlReader.Load(xmlReader);
        }

        // Audio File Methods

        private void DeleteAllAudioFiles()
        {
            // Check if the Audio folder exists
            if (!Directory.Exists(Path.Combine(_applicationPath, "Audio")))
                return;

            // Delete all audio files
            string[] audioFiles = Directory.GetFiles(Path.Combine(_applicationPath, "Audio"));

            foreach (string audioFile in audioFiles)
                File.Delete(audioFile);
        }

        public void DownloadAudioFiles()
        {
            //try
            //{
            //    // Get the audio files from the online DB
            //    WebClient webClient = new WebClient();
            //    JObject audioFilesJson = JObject.Parse(webClient.DownloadString(EncodeLink(_config["AudioFilesURL"].ToString())));

            //    // Check if audioFiles is unchanged, if so, return
            //    if (_audioFiles != null)
            //    {
            //        bool changed = false;

            //        if (_audioFiles.Count != ((JArray)audioFilesJson["AudioFiles"]).Count)
            //            changed = true;

            //        else
            //            for (int i = 0; i < _audioFiles.Count; i++)
            //            {
            //                if (_audioFiles[i]["URL"].ToString() != ((JArray)audioFilesJson["AudioFiles"])[i]["URL"].ToString())
            //                {
            //                    changed = true;
            //                    break;

            //                }

            //                if (_audioFiles[i]["URL"].ToString() == "Spacer") continue;

            //                if (_audioFiles[i]["FileName"].ToString() != ((JArray)audioFilesJson["AudioFiles"])[i]["FileName"].ToString())
            //                {
            //                    changed = true;
            //                    break;
            //                }
            //            }

            //        if (_periodicAudioFiles.Length != ((JArray)audioFilesJson["PeriodicAudio"]).Count)
            //            changed = true;

            //        else
            //            for (int i = 0; i < _periodicAudioFiles.Length; i++)
            //            {
            //                if (_periodicAudioFiles[i] != int.Parse(((JArray)audioFilesJson["PeriodicAudio"])[i].ToString()))
            //                {
            //                    changed = true;
            //                    break;
            //                }
            //            }

            //        if (!changed) return;
            //    }

            //    Console.WriteLine("[Audio] Downloading audio files");

            //    _audioFiles = (JArray)audioFilesJson["AudioFiles"];

            //    JArray periodicAudioFilesArray = (JArray)audioFilesJson["PeriodicAudio"];

            //    _periodicAudioFiles = new int[periodicAudioFilesArray.Count];

            //    for (int i = 0; i < periodicAudioFilesArray.Count; i++)
            //        _periodicAudioFiles[i] = int.Parse(periodicAudioFilesArray[i].ToString());

            //    // Create the Audio folder if it doesn't exist
            //    if (!Directory.Exists(Path.Combine(_applicationPath, "Audio")))
            //        Directory.CreateDirectory(Path.Combine(_applicationPath, "Audio"));

            //    _audioFileNames = new string[_audioFiles.Count];

            //    for (int i = 0; i < _audioFiles.Count; i++)
            //    {
            //        if (((JObject)_audioFiles[i])["URL"].ToString() == "Spacer")
            //        {
            //            _audioFileNames[i] = "";
            //            continue;
            //        }

            //        string downloadURL = EncodeLink(((JObject)_audioFiles[i])["URL"].ToString());
            //        string fileName = ((JObject)_audioFiles[i])["FileName"].ToString();

            //        // Try to download the audio file
            //        try
            //        {
            //            webClient.DownloadFile(downloadURL, Path.Combine(_applicationPath, "Audio", fileName + ".wav"));

            //            // If the download is successful, set the audio file name
            //            _audioFileNames[i] = fileName;
            //        }
            //        catch (Exception)
            //        {
            //            // If the download fails, set the audio file name to an empty string
            //            _audioFileNames[i] = "Failed To Load Audio";
            //        }
            //    }

            //    // Delete all audio files that are not in the online DB
            //    string[] localAudioFiles = Directory.GetFiles(Path.Combine(_applicationPath, "Audio"));

            //    foreach (string localAudioFile in localAudioFiles)
            //        if (Array.IndexOf(_audioFileNames, Path.GetFileNameWithoutExtension(localAudioFile)) == -1)
            //            File.Delete(localAudioFile);

            //    Console.WriteLine("[Audio] Finished downloading audio files");
            //}
            //catch (Exception)
            //{
            //    _audioFiles = new JArray();
            //    _audioFileNames = new string[0];
            //    _periodicAudioFiles = new int[0];
            //}
        }

        public string[] GetAudioFileNames() => _audioFileNames;

        public void PlayRandomPeriodicAudioFile()
        {
            //// Pick a random number between 0 and the length of the periodic audio files array
            //int randomIndex = new Random(DateTime.Now.Millisecond).Next(0, _periodicAudioFiles.Length);

            //// Play the periodic audio file at the random index
            //PlayPeriodicAudioFile(randomIndex);
        }

        private void PlayPeriodicAudioFile(int _index)
        {
            //// If the index is out of bounds, return
            //if (_index < 0 || _index >= _periodicAudioFiles.Length)
            //    return;

            //// If the periodic audio file at the index is out of bounds, return
            //if (_periodicAudioFiles[_index] < 0 || _periodicAudioFiles[_index] >= _audioFileNames.Length)
            //    return;

            //// Play the periodic audio file at the index
            //PlayAudioFile(_audioFileNames[_periodicAudioFiles[_index]]);
        }

        public void PlayAudioFile(string _audioFile) =>
            Task.Run(() => PlayAudioFileAsync(_audioFile));

        private async Task PlayAudioFileAsync(string _audioFile)
        {
            // Find the audio file
            _audioFile = Path.Combine(_applicationPath, "Audio", _audioFile + ".wav");

            // If the audio file does not exist, return
            if (!File.Exists(_audioFile))
                return;

            // Play the audio file
            using (var audioFile = new AudioFileReader(_audioFile))
            using (var outputDevice = new WaveOutEvent())
            {
                outputDevice.Init(audioFile);
                outputDevice.Play();

                // Wait until the audio file has finished playing
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                    await Task.Delay(1000);
            }
        }
    }
}
