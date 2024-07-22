using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Xml;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Runtime.InteropServices;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using System.Threading.Tasks;
using XamlAnimatedGif;

namespace ArcademiaGameLauncher
{
    // Launcher State Enum

    enum LauncherState
    {
        ready,
        failed,
        downloadingGame,
        downloadingUpdate
    }

    // Controller State Class

    public class ControllerState
    {
        private int index;
        private bool[] buttonStates;
        private int leftStickX;
        private int leftStickY;

        // Deadzone and Midpoint values for the joystick
        readonly int joystickDeadzone = 1000;
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
        public int GetLeftStickY() {
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
        public void SetButtonState(int _button, bool _state)
        {
            buttonStates[_button] = _state;
            if (_state)
            {
                Console.WriteLine(_button);
            }
        }
    }

    public partial class MainWindow : Window
    {
        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);

        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly string rootPath;
        private readonly string gameDirectoryPath;

        private readonly string configPath;
        private string gameDatabaseURL;

        private readonly string localGameDatabasePath;
        private JObject gameDatabaseFile;

        private int updateIndexOfGame;
        private System.Timers.Timer updateTimer;

        private int selectionAnimationFrame = 0;
        private int selectionAnimationFrameRate = 100;

        // Colours for the Text Blocks Selection Animation
        private readonly SolidColorBrush[] selectionAnimationFrames = new SolidColorBrush[8]
        {
            new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xC3, 0x5C)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0xBF, 0xA3, 0x4C)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0x9E, 0x86, 0x3F)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0x7F, 0x6c, 0x33)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0x9E, 0x86, 0x3F)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0xBF, 0xA3, 0x4C)),
            new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xC3, 0x5C)),
        };

        private int globalCounter = 0;

        private int selectionUpdateInterval = 150;
        private int selectionUpdateIntervalCounter = 0;
        private int selectionUpdateIntervalCounterMax = 10;
        private int selectionUpdateCounter = 0;

        private int currentlySelectedHomeIndex = 0;

        private int currentlySelectedGameIndex;
        private int previousPageIndex = 0;
        private System.Timers.Timer updateGameInfoDisplayDebounceTimer;
        private bool showingDebouncedGame = false;

        private int afkTimer = 0;
        private bool afkTimerActive = false;

        private int timeSinceLastButton = 0;

        private JObject[] gameInfoFilesList;

        private TextBlock[] homeOptionsList;
        private TextBlock[] gameTitlesList;

        private DirectInput directInput;
        private List<ControllerState> controllerStates = new List<ControllerState>();

        private Process currentlyRunningProcess = null;

        private LauncherState _state;
        internal LauncherState State
        {
            get => _state;
            set
            {
                _state = value;
                switch (_state)
                {
                    case LauncherState.ready:
                        StartButton.IsEnabled = true;
                        StartButton.Content = "Start";
                        break;
                    case LauncherState.failed:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Failed";
                        break;
                    case LauncherState.downloadingGame:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Downloading...";
                        break;
                    case LauncherState.downloadingUpdate:
                        StartButton.IsEnabled = false;
                        StartButton.Content = "Updating...";
                        break;
                    default:
                        break;
                }
            }
        }

        // MAIN WINDOW

        public MainWindow()
        {
            InitializeComponent();

            // Setup Directories
            rootPath = Directory.GetCurrentDirectory();

            configPath = Path.Combine(rootPath, "Config.json");
            gameDirectoryPath = Path.Combine(rootPath, "Games");

            localGameDatabasePath = Path.Combine(gameDirectoryPath, "GameDatabase.json");

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(gameDirectoryPath))
                Directory.CreateDirectory(gameDirectoryPath);
        }

        // Initialization

        private void JoyStickInit()
        {
            // Initialize Direct Input
            directInput = new DirectInput();

            // Find a JoyStick Guid
            var joystickGuid = Guid.Empty;

            // Find a Gamepad Guid
            foreach (var deviceInstance in directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
            {
                joystickGuid = deviceInstance.InstanceGuid;
                break;
            }

            // If no Gamepad is found, find a Joystick
            if (joystickGuid == Guid.Empty)
            {
                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                {
                    joystickGuid = deviceInstance.InstanceGuid;
                    break;
                }
            }

            // If no Joystick is found, throw an error
            if (joystickGuid == Guid.Empty)
            {
                MessageBox.Show("No joystick or gamepad found.");
                Application.Current.Shutdown();
                return;
            }

            // Instantiate the joystick
            Joystick joystick = new Joystick(directInput, joystickGuid);

            // Query all suported ForceFeedback effects
            var allEffects = joystick.GetEffects();
            foreach (var effectInfo in allEffects)
            {
                Console.WriteLine(effectInfo.Name);
            }

            // Set BufferSize in order to use buffered data.
            joystick.Properties.BufferSize = 128;

            // Acquire the joystick
            joystick.Acquire();

            // Create a new ControllerState object for the joystick
            ControllerState controllerState = new ControllerState(joystick, controllerStates.Count);
            controllerStates.Add(controllerState);
        }

        private void InitializeUpdateTimer()
        {
            // Timer Setup
            updateTimer = new System.Timers.Timer { Interval = 10 };

            // Hook up the Elapsed event for the timer. 
            updateTimer.Elapsed += OnTimedEvent;

            // Have the timer fire repeated events (true is the default)
            updateTimer.AutoReset = true;

            // Start the timer
            updateTimer.Enabled = true;
        }

        // Downloading and Installing Game Methods

        private bool CheckForGameDatabaseChanges()
        {
            try
            {
                // Get the game database file from the online URL
                WebClient webClient = new WebClient();
                gameDatabaseFile = JObject.Parse(webClient.DownloadString(gameDatabaseURL));

                // If the local game database file does not exist, create it and write the game database to it
                if (!File.Exists(localGameDatabasePath))
                    File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                // Save the FolderName property of each local game and write it to the new game database file
                JObject localGameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                JArray localGames = (JArray)localGameDatabaseFile["Games"];

                JArray onlineGames = (JArray)gameDatabaseFile["Games"];
                gameInfoFilesList = new JObject[onlineGames.Count];

                // Update the FolderName property of each game in the local game database
                for (int i = 0; i < onlineGames.Count; i++)
                {
                    for (int j = 0; j < localGames.Count; j++)
                    {
                        // If the game is found in the local game database using the LinkToGameInfo property as the Primary Key
                        if (onlineGames[i]["LinkToGameInfo"].ToString() == localGames[j]["LinkToGameInfo"].ToString())
                        {
                            // Update the FolderName property of the game in the updated game database file
                            if (localGames[j]["FolderName"] != null)
                                gameDatabaseFile["Games"][i]["FolderName"] = localGames[j]["FolderName"].ToString();
                            else
                                gameDatabaseFile["Games"][i]["FolderName"] = "";
                            break;
                        }
                    }
                }

                File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                JArray games = (JArray)gameDatabaseFile["Games"];

                if (games.Count > 0)
                {
                    // In a new thread, check for updates for each game (CheckForUpdatesInit)
                    Task.Run(() => CheckForUpdatesInit(games.Count));
                }
                // If no games are found, show an error message
                else MessageBox.Show("Failed to get game database: No games found.");

                return true;
            }
            catch (Exception ex)
            {
                // If the game database cannot be retrieved, show an error message
                MessageBox.Show($"Failed to get game database: {ex.Message}");

                return false;
            }
        }

        private void CheckForUpdatesInit(int totalGames)
        {
            // Check for updates for each game
            for (int i = 0; i < totalGames; i++)
                CheckForUpdates(i);

            // Once all games have been checked for updates, set the state to ready
            if (Application.Current != null && Application.Current.Dispatcher != null)
                try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.ready; }); }
                catch (TaskCanceledException) { }
        }

        private void CheckForUpdates(int _updateIndexOfGame)
        {
            // Set the updateIndexOfGame to the index of the game being updated
            updateIndexOfGame = _updateIndexOfGame;

            if (gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] == null)
                gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] = "";

            string folderName = gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"].ToString();
            string localGameInfoPath = "";

            if (folderName != "")
                localGameInfoPath = Path.Combine(gameDirectoryPath, folderName, "GameInfo.json");

            // Check if the game has a local GameInfo.json file
            if (localGameInfoPath != "" && File.Exists(localGameInfoPath))
            {
                gameInfoFilesList[updateIndexOfGame] = JObject.Parse(File.ReadAllText(localGameInfoPath));

                // Update the game title text block if it's on the first page of the Selection Menu
                if (updateIndexOfGame < 10)
                {
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                gameTitlesList[updateIndexOfGame].Text = gameInfoFilesList[updateIndexOfGame]["GameName"].ToString();
                                gameTitlesList[updateIndexOfGame].Visibility = Visibility.Visible;
                            });
                        }
                        catch (TaskCanceledException) { }
                    }
                }

                // Get the local version of the game and update the VersionText text block
                Version localVersion = new Version(gameInfoFilesList[updateIndexOfGame]["GameVersion"].ToString());
                if (Application.Current != null && Application.Current.Dispatcher != null)
                    try { Application.Current.Dispatcher.Invoke(() => { VersionText.Text = "v" + localVersion.ToString(); }); }
                    catch (TaskCanceledException) { }

                try
                {
                    // Get the online version of the game
                    WebClient webClient = new WebClient();
                    JObject onlineJson = JObject.Parse(webClient.DownloadString(gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString()));
                    Version onlineVersion = new Version(onlineJson["GameVersion"].ToString());

                    // Compare the local version with the online version to see if an update is needed
                    if (onlineVersion.IsDifferentVersion(localVersion))
                        InstallGameFiles(true, onlineJson, gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
                }
                catch (Exception ex)
                {
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                        try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.failed; }); }
                        catch (TaskCanceledException) { }

                    MessageBox.Show($"Failed to check for updates: {ex.Message}");
                }
            }
            else
            {
                // If the game does not have a local GameInfo.json file, install the game files with a temporary GameInfo.json file of file version 0.0.0
                InstallGameFiles(false, JObject.Parse("{\r\n\"GameVersion\": \"0.0.0\"\r\n}\r\n"), gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
            }
        }

        private void InstallGameFiles(bool _isUpdate, JObject _onlineJson, string _downloadURL)
        {
            try
            {
                WebClient webClient = new WebClient();
                if (_isUpdate)
                {
                    // If the game has an update, set the state to downloadingUpdate
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                        try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.downloadingUpdate; }); }
                        catch (TaskCanceledException) { }
                }
                else
                {
                    // If the game doesn't have an update, set the state to downloadingGame
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                        try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.downloadingGame; }); }
                        catch (TaskCanceledException) { }

                    // Set _onlineJson to the online JSON object
                    _onlineJson = JObject.Parse(webClient.DownloadString(_downloadURL));
                }

                // Asynchronously download the game zip file
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri(_onlineJson["LinkToGameZip"].ToString()), Path.Combine(rootPath, _onlineJson["GameName"].ToString() + ".zip"), _onlineJson);
            }
            catch (Exception ex)
            {
                // If the download fails due to (403 Forbidden) retry the download
                if (ex.Message.Contains("403"))
                {
                    InstallGameFiles(_isUpdate, _onlineJson, _downloadURL);
                    return;
                }

                // If the download fails, set the state to failed and show an error message
                if (Application.Current != null && Application.Current.Dispatcher != null)
                    try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.failed; }); }
                    catch (TaskCanceledException) { }
                MessageBox.Show($"Failed installing game files: {ex.Message}");
            }
        }

        private void DownloadGameCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                // Get the online JSON object from the AsyncCompletedEventArgs
                JObject onlineJson = (JObject)e.UserState;

                int currentUpdateIndexOfGame = -1;

                WebClient webClient = new WebClient();
                JArray games = (JArray)gameDatabaseFile["Games"];

                // Find the index of the game being updated from the game database
                for (int i = 0; i < games.Count; i++)
                {
                    if (JObject.Parse(webClient.DownloadString(games[i]["LinkToGameInfo"].ToString()))["LinkToGameZip"].ToString() == onlineJson["LinkToGameZip"].ToString())
                    {
                        currentUpdateIndexOfGame = i;
                        break;
                    }
                }

                // If the game is not found in the game database, show an error message and return
                if (currentUpdateIndexOfGame == -1)
                {
                    MessageBox.Show("Failed to update game: Game not found in database.");
                    return;
                }

                // Extract the downloaded zip file to the game directory
                string pathToZip = Path.Combine(rootPath, onlineJson["FolderName"].ToString() + ".zip");
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(pathToZip, Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString()), null);
                File.Delete(pathToZip);

                // Update the game database with the new FolderName property
                JObject gameDatabase = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                gameDatabase["Games"][currentUpdateIndexOfGame]["FolderName"] = onlineJson["FolderName"].ToString();
                
                // Write the updated game database to the local game database file (lock to prevent multiple threads writing to the file at the same time)
                lock (gameDatabaseFile)
                {
                    File.WriteAllText(localGameDatabasePath, gameDatabase.ToString());
                }

                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    try {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Set the game database variable to the updated game database
                            gameDatabaseFile = gameDatabase;

                            // Write the GameInfo.json file to the game directory
                            File.WriteAllText(Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString(), "GameInfo.json"), onlineJson.ToString());

                            // Update the gameInfoFilesList with the online JSON object
                            gameInfoFilesList[currentUpdateIndexOfGame] = onlineJson;

                            // Update the game title text block if it's on the first page of the Selection Menu
                            if (currentUpdateIndexOfGame < 10)
                            {
                                gameTitlesList[currentUpdateIndexOfGame].Text = onlineJson["GameName"].ToString();
                                gameTitlesList[currentUpdateIndexOfGame].Visibility = Visibility.Visible;
                            }
                        });
                    }
                    catch (TaskCanceledException) { }
                }
            }
            catch (Exception ex)
            {
                // If the download fails due to (403 Forbidden) retry the download
                if (ex.Message.Contains("403"))
                {
                    DownloadGameCompletedCallback(sender, e);
                    return;
                }

                // If the download fails, set the state to failed and show an error message
                if (Application.Current != null && Application.Current.Dispatcher != null)
                    try { Application.Current.Dispatcher.Invoke(() => { State = LauncherState.failed; }); }
                    catch (TaskCanceledException) { }
                MessageBox.Show($"Failed to complete download: {ex.Message}");
            }
        }

        // Custom TextBlock Buttons

        private void GameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Selection Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Collapsed;
                        SelectionMenu.Visibility = Visibility.Visible;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected game index to 0
            currentlySelectedGameIndex = 0;
            UpdateGameInfoDisplay();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the About Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Visible;
                        SelectionMenu.Visibility = Visibility.Collapsed;

                        HomeImage.Visibility = Visibility.Collapsed;
                        CreditsPanel.Visibility = Visibility.Visible;

                        // Show the CreditsPanel Logos
                        UoL_Logo.Visibility = Visibility.Visible;
                        intlab_Logo.Visibility = Visibility.Visible;

                        // Set Canvas.Top of the CreditsPanel to the screen height
                        Canvas.SetTop(CreditsPanel, (int)SystemParameters.PrimaryScreenHeight);
                    });
                }
                catch (TaskCanceledException) { }
            }


        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Start Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Visible;
                        HomeMenu.Visibility = Visibility.Collapsed;
                        SelectionMenu.Visibility = Visibility.Collapsed;

                        HomeImage.Visibility = Visibility.Visible;
                        CreditsPanel.Visibility = Visibility.Collapsed;

                        // Hide the CreditsPanel Logos
                        UoL_Logo.Visibility = Visibility.Collapsed;
                        intlab_Logo.Visibility = Visibility.Collapsed;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Reset AFK Timer after Half a Second
            Task.Delay(500).ContinueWith(t =>
            {
                afkTimerActive = false;
                afkTimer = 0;
            });
        }

        // ToggleButton Methods

        private void BackFromGameLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the Home Menu
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Visible;
                        SelectionMenu.Visibility = Visibility.Collapsed;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected Home Index to 0 and highlight the current Home Menu Option
            currentlySelectedHomeIndex = 0;
            HighlightCurrentHomeMenuOption();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // If the game info display is not showing the currently selected game, return
            if (!showingDebouncedGame) return;

            // Get the current game folder, game info, and game executable
            string currentGameFolder = gameDatabaseFile["Games"][currentlySelectedGameIndex]["FolderName"].ToString();
            string currentGameExe = Path.Combine
                (
                    gameDirectoryPath,
                    currentGameFolder,
                    gameInfoFilesList[currentlySelectedGameIndex]["NameOfExecutable"].ToString()
                );

            // Start the game if the game executable exists and the launcher is ready
            if (File.Exists(currentGameExe) && State == LauncherState.ready)
            {
                // Create a new ProcessStartInfo object and set the Working Directory to the game directory
                ProcessStartInfo startInfo = new ProcessStartInfo(currentGameExe);
                startInfo.WorkingDirectory = currentGameFolder;

                // Start the game if no process is currently running
                if (currentlyRunningProcess == null || currentlyRunningProcess.HasExited)
                    currentlyRunningProcess = Process.Start(startInfo);

                // Set focus to the currently running process
                else
                    SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
            }
            else if (State == LauncherState.failed)
            {
                // Run CheckForUpdatesInit again
                Task.Run(() => CheckForUpdatesInit(((JArray)gameDatabaseFile["Games"]).Count));
            }
        }

        // Event Handlers

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            // Set the Copyright text
            Copyright.Text = "Copyright ©️ 2018 - " + DateTime.Now.Year + "\nUniversity of Lincoln,\nAll rights reserved.";

            // Initialize the TextBlock arrays
            homeOptionsList = new TextBlock[3] { GameLibraryText, AboutText, ExitText };
            gameTitlesList = new TextBlock[10] { GameTitleText0, GameTitleText1, GameTitleText2, GameTitleText3, GameTitleText4, GameTitleText5, GameTitleText6, GameTitleText7, GameTitleText8, GameTitleText9 };

            // Generate the Credits from the Credits.json file
            GenerateCredits();

            // Load the GameDatabaseURL from the Config.json file
            bool foundGameDatabase = false;
            if (File.Exists(configPath))
            {
                gameDatabaseURL = JObject.Parse(File.ReadAllText(configPath))["GameDatabaseURL"].ToString();
                foundGameDatabase = CheckForGameDatabaseChanges();
            }
            // If the Config.json file does not exist, show an error message
            else MessageBox.Show("Failed to get game database URL: Config.json does not exist.");

            // Quit the application
            if (!foundGameDatabase) Application.Current.Shutdown();

            // Initialize the controller states
            JoyStickInit();

            // Perform an initial update of the game info display
            currentlySelectedGameIndex = 0;
            UpdateGameInfoDisplay();

            // Initialize the updateTimer
            InitializeUpdateTimer();
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            if (controllerStates.Count > 0)
                controllerStates[0].UpdateButtonStates();

            // Keylogger for AFK Timer
            if (afkTimerActive)
            {
                // Check for any key press and reset the timer if any key is pressed
                for (int i = 8; i < 91; i++)
                    if (GetAsyncKeyState(i) != 0)
                    {
                        afkTimer = 0;
                        break;
                    }
            }
            else
            {
                // Check for any key press and start the timer if any key is pressed
                for (int i = 8; i < 91; i++)
                    if (GetAsyncKeyState(i) != 0)
                    {
                        afkTimerActive = true;
                        afkTimer = 0;
                        timeSinceLastButton = 0;

                        // Show the Home Menu
                        if (Application.Current != null && Application.Current.Dispatcher != null)
                        {
                            try
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    StartMenu.Visibility = Visibility.Collapsed;
                                    HomeMenu.Visibility = Visibility.Visible;
                                    SelectionMenu.Visibility = Visibility.Collapsed;

                                    currentlySelectedHomeIndex = 0;
                                    HighlightCurrentHomeMenuOption();
                                });
                            }
                            catch (TaskCanceledException) { }
                        }

                        // Set the focus to the game launcher
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

                        break;
                    }
            }

            // If the user is AFK for 3 minutes, Warn them and then close the currently running application
            if (afkTimer >= 180000)
            {
                if (afkTimer >= 185000)
                {
                    // Reset the timer
                    afkTimerActive = false;
                    afkTimer = 0;

                    // Close the currently running application
                    if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                    {
                        currentlyRunningProcess.Kill();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                        currentlyRunningProcess = null;
                    }

                    // Show the Start Menu
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StartMenu.Visibility = Visibility.Visible;
                                HomeMenu.Visibility = Visibility.Collapsed;
                                SelectionMenu.Visibility = Visibility.Collapsed;
                            });
                        }
                        catch (TaskCanceledException) { }
                    }

                }
                else
                {
                    // Warn the user (Optional: To Be Implemented)

                }
            }
            else
            {
                // Hide the warning (Optional: To Be Implemented)

            }

            // Increment the selection animation frame
            if ((HomeMenu.Visibility == Visibility.Visible || SelectionMenu.Visibility == Visibility.Visible) && globalCounter % selectionAnimationFrameRate == 0)
            {
                if (selectionAnimationFrame < selectionAnimationFrames.Length - 1)
                    selectionAnimationFrame++;
                else
                    selectionAnimationFrame = 0;

                // Highlight the current menu option
                if (HomeMenu.Visibility == Visibility.Visible && Application.Current != null && Application.Current.Dispatcher != null)
                {
                    try { Application.Current.Dispatcher.Invoke(() => { HighlightCurrentHomeMenuOption(); }); }
                    catch (TaskCanceledException) { }
                }
                // Highlight the current game option
                else if (SelectionMenu.Visibility == Visibility.Visible && Application.Current != null && Application.Current.Dispatcher != null)
                {
                    try { Application.Current.Dispatcher.Invoke(() => { HighlightCurrentGameMenuOption(); }); }
                    catch (TaskCanceledException) { }
                }
            }

            // Update the Home/Selection Menu's current selection
            if ((HomeMenu.Visibility == Visibility.Visible || SelectionMenu.Visibility == Visibility.Visible) &&
                Application.Current != null &&
                Application.Current.Dispatcher != null)
            {
                try { Application.Current.Dispatcher.Invoke(() => { UpdateCurrentSelection(); }); }
                catch (TaskCanceledException) { }
            }

            // Auto Scroll the Credits Panel
            if (CreditsPanel.Visibility == Visibility.Visible && Application.Current != null && Application.Current.Dispatcher != null)
            {
                try { Application.Current.Dispatcher.Invoke(() => { AutoScrollCredits(); }); }
                catch (TaskCanceledException) { }
            }

            // Check if the currently running process has exited, and set the focus back to the launcher
            if (currentlyRunningProcess != null && currentlyRunningProcess.HasExited)
            {
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                currentlyRunningProcess = null;
            }

            // Flash the Start Button if the Start Menu is visible
            if (StartMenu.Visibility == Visibility.Visible && Application.Current != null && Application.Current.Dispatcher != null)
            {
                try {
                    Application.Current.Dispatcher.Invoke(() => {
                        if (timeSinceLastButton % 300 == 0)
                            PressStartText.Visibility = PressStartText.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Increment the afkTimer
            if (afkTimerActive)
                afkTimer += 10;

            // Reset the selectionUpdateIntervalCounter if enough time has passed from releasing the analog stick
            if (selectionUpdateCounter > selectionUpdateInterval)
                selectionUpdateIntervalCounter = 0;
            // Increment selectionUpdateCounter and timeSinceLastButton
            selectionUpdateCounter += 10;
            timeSinceLastButton += 10;

            // Increment the global counter
            globalCounter += 10;
        }

        // Credits

        private void GenerateCredits()
        {
            // Read the Credits.json file
            string creditsPath = Path.Combine(rootPath, "Credits.json");

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
                    switch (creditsArray[i]["Type"].ToString())
                    {
                        case "Title":
                            // Create a new RowDefinition
                            RowDefinition titleRow = new RowDefinition();
                            titleRow.Height = new GridLength(60, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(titleRow);

                            // Create a new Grid
                            Grid titleGrid = new Grid();
                            Grid.SetRow(titleGrid, 2 * i);
                            titleGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            titleGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create 2 new RowDefinitions
                            RowDefinition titleGridTitleRow = new RowDefinition();
                            titleGridTitleRow.Height = new GridLength(40, GridUnitType.Pixel);
                            titleGrid.RowDefinitions.Add(titleGridTitleRow);

                            RowDefinition titleGridSubtitleRow = new RowDefinition();
                            titleGridSubtitleRow.Height = new GridLength(20, GridUnitType.Pixel);
                            titleGrid.RowDefinitions.Add(titleGridSubtitleRow);

                            // Create a new TextBlock (Title)
                            TextBlock titleText = new TextBlock();
                            titleText.Text = creditsArray[i]["Value"].ToString();
                            titleText.Style = (Style)FindResource("Early GameBoy");
                            titleText.FontSize = 24;
                            titleText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66));
                            titleText.HorizontalAlignment = HorizontalAlignment.Left;
                            titleText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            Grid.SetRow(titleText, 0);
                            titleGrid.Children.Add(titleText);

                            // Create a new TextBlock (Subtitle)
                            if (creditsArray[i]["Subtitle"] != null)
                            {
                                TextBlock subtitleText = new TextBlock();
                                subtitleText.Text = creditsArray[i]["Subtitle"].ToString();
                                subtitleText.Style = (Style)FindResource("Early GameBoy");
                                subtitleText.FontSize = 16;
                                subtitleText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                                subtitleText.HorizontalAlignment = HorizontalAlignment.Left;
                                subtitleText.VerticalAlignment = VerticalAlignment.Center;

                                // Add the TextBlock to the Grid
                                Grid.SetRow(subtitleText, 1);
                                titleGrid.Children.Add(subtitleText);
                            }

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(titleGrid);

                            break;
                        case "Heading":
                            // Check the Subheadings property
                            JArray subheadingsArray = (JArray)creditsArray[i]["Subheadings"];

                            // Create a new RowDefinition
                            RowDefinition headingRow = new RowDefinition();
                            headingRow.Height = new GridLength(30 + (subheadingsArray.Count * 25), GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(headingRow);

                            // Create a new Grid
                            Grid headingGrid = new Grid();
                            Grid.SetRow(headingGrid, 2 * i);
                            headingGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            headingGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create 2 new ColumnDefinitions
                            ColumnDefinition headingGridBorderColumn = new ColumnDefinition();
                            headingGridBorderColumn.Width = new GridLength(3, GridUnitType.Pixel);
                            headingGrid.ColumnDefinitions.Add(headingGridBorderColumn);

                            ColumnDefinition headingGridContentColumn = new ColumnDefinition();
                            headingGridContentColumn.Width = new GridLength(1, GridUnitType.Star);
                            headingGrid.ColumnDefinitions.Add(headingGridContentColumn);

                            // Create a Grid to function as a border
                            Grid headingBorderGrid = new Grid();
                            headingBorderGrid.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33));
                            headingBorderGrid.Margin = new Thickness(0, 10, 0, 10);

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingBorderGrid, 0);
                            headingGrid.Children.Add(headingBorderGrid);

                            // Create a new Grid to hold the Title and Subheadings
                            Grid headingContentGrid = new Grid();
                            headingContentGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            headingContentGrid.VerticalAlignment = VerticalAlignment.Center;
                            headingContentGrid.Margin = new Thickness(25, 0, 0, 0);

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingContentGrid, 1);
                            headingGrid.Children.Add(headingContentGrid);

                            // Create 2 new RowDefinitions
                            RowDefinition headingGridTitleRow = new RowDefinition();
                            headingGridTitleRow.Height = new GridLength(30, GridUnitType.Pixel);
                            headingContentGrid.RowDefinitions.Add(headingGridTitleRow);

                            RowDefinition headingGridSubheadingsRow = new RowDefinition();
                            headingGridSubheadingsRow.Height = new GridLength(subheadingsArray.Count * 25, GridUnitType.Pixel);
                            headingContentGrid.RowDefinitions.Add(headingGridSubheadingsRow);

                            // Create a new TextBlock (Title)
                            TextBlock headingText = new TextBlock();
                            headingText.Text = creditsArray[i]["Value"].ToString();
                            headingText.Style = (Style)FindResource("Early GameBoy");
                            headingText.FontSize = 16;
                            headingText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x85, 0x8E, 0xFF));
                            headingText.HorizontalAlignment = HorizontalAlignment.Left;
                            headingText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            Grid.SetRow(headingText, 0);
                            headingContentGrid.Children.Add(headingText);

                            // Create a new Grid for the Subheadings
                            Grid subheadingsGrid = new Grid();
                            Grid.SetRow(subheadingsGrid, 1);
                            subheadingsGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            subheadingsGrid.VerticalAlignment = VerticalAlignment.Center;

                            // For each Subheading
                            for (int j = 0; j < subheadingsArray.Count; j++)
                            {
                                // Create new RowDefinitions & for each Subheading
                                RowDefinition subheadingRow = new RowDefinition();
                                subheadingRow.Height = new GridLength(25, GridUnitType.Pixel);
                                subheadingsGrid.RowDefinitions.Add(subheadingRow);

                                // Create a new TextBlock (Subheading)
                                TextBlock subheadingText = new TextBlock();
                                subheadingText.Text = subheadingsArray[j]["Value"].ToString();
                                subheadingText.Style = (Style)FindResource("Early GameBoy");
                                subheadingText.FontSize = 18;
                                subheadingText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(subheadingsArray[j]["Colour"].ToString()));
                                subheadingText.HorizontalAlignment = HorizontalAlignment.Left;
                                subheadingText.VerticalAlignment = VerticalAlignment.Center;

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
                            int noteHeight = 25 + (creditsArray[i]["Value"].ToString().Length / 150 * 25);

                            // Create a new RowDefinition
                            RowDefinition noteRow = new RowDefinition();
                            noteRow.Height = new GridLength(noteHeight, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(noteRow);

                            // Create a new Grid
                            Grid noteGrid = new Grid();
                            Grid.SetRow(noteGrid, 2 * i);
                            noteGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            noteGrid.VerticalAlignment = VerticalAlignment.Center;
                            noteGrid.Margin = new Thickness(0, 0, 100, 0);

                            // Create a new TextBlock
                            TextBlock noteText = new TextBlock();
                            noteText.Text = creditsArray[i]["Value"].ToString();
                            noteText.Style = (Style)FindResource("Early GameBoy");
                            noteText.FontSize = 11;
                            noteText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                            noteText.HorizontalAlignment = HorizontalAlignment.Left;
                            noteText.VerticalAlignment = VerticalAlignment.Center;
                            noteText.TextWrapping = TextWrapping.Wrap;
                            noteText.LineHeight = 15;

                            // Add the TextBlock to the Grid
                            noteGrid.Children.Add(noteText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(noteGrid);

                            break;
                        case "Break":
                            // Create a new RowDefinition
                            RowDefinition breakRow = new RowDefinition();
                            breakRow.Height = new GridLength(10, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(breakRow);

                            // Create a new Grid
                            Grid breakGrid = new Grid();
                            Grid.SetRow(breakGrid, 2 * i);
                            breakGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            breakGrid.VerticalAlignment = VerticalAlignment.Center;

                            // Create a new TextBlock
                            TextBlock breakText = new TextBlock();
                            breakText.Text = "----------------------";
                            breakText.Style = (Style)FindResource("Early GameBoy");
                            breakText.FontSize = 16;
                            breakText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                            breakText.HorizontalAlignment = HorizontalAlignment.Left;
                            breakText.VerticalAlignment = VerticalAlignment.Center;

                            // Add the TextBlock to the Grid
                            breakGrid.Children.Add(breakText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(breakGrid);

                            break;
                        case "Image":
                            // Create a new RowDefinition
                            RowDefinition imageRow = new RowDefinition();
                            imageRow.Height = new GridLength(100, GridUnitType.Pixel);
                            CreditsPanel.RowDefinitions.Add(imageRow);

                            // Create a new Grid
                            Grid imageGrid = new Grid();
                            Grid.SetRow(imageGrid, 2 * i);
                            imageGrid.HorizontalAlignment = HorizontalAlignment.Left;
                            imageGrid.VerticalAlignment = VerticalAlignment.Center;

                            string imagePath = creditsArray[i]["Path"].ToString();

                            // Create a new Image (Static)
                            Image imageStatic = new Image();
                            imageStatic.Source = new BitmapImage(new Uri(imagePath, UriKind.Relative));
                            imageStatic.Stretch = Stretch.None;

                            // Add the Image to the Grid
                            imageGrid.Children.Add(imageStatic);

                            // Set Grid Height to Image Height
                            double imageHeight = imageStatic.Source.Height;
                            imageGrid.Height = imageHeight;

                            // Create a new Image (Gif)
                            if (imagePath.EndsWith(".gif"))
                            {
                                // Copy GifTemplateElement_Parent's child element to make a new Image
                                Image imageGif = CloneXamlElement((Image)GifTemplateElement_Parent.Children[0]);
                                AnimationBehavior.SetSourceUri(imageGif, new Uri(imagePath, UriKind.Relative));
                                imageGif.Stretch = Stretch.None;

                                // Add the Image to the Grid
                                imageGrid.Children.Add(imageGif);

                                AnimationBehavior.AddLoadedHandler(imageGif, (sender, e) =>
                                {
                                    // Hide the static image
                                    imageStatic.Visibility = Visibility.Collapsed;
                                });
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
                        RowDefinition spaceRow = new RowDefinition();
                        spaceRow.Height = new GridLength(50, GridUnitType.Pixel);
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
            if (newTop < -CreditsPanel.ActualHeight)
                Canvas.SetTop(CreditsPanel, (int)SystemParameters.PrimaryScreenHeight);
        }

        // Getters & Setters

        private SolidColorBrush GetCurrentSelectionAnimationBrush()
        {
            // Return the current selection animation frame
            return selectionAnimationFrames[selectionAnimationFrame];
        }

        // Update Methods

        private void UpdateCurrentSelection()
        {
            // If theres a game running, don't listen for inputs
            if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
            {
                // If the user hits the exit button (Button 0) close the application
                if (controllerStates[0].GetButtonState(0))
                {
                    currentlyRunningProcess.Kill();
                    SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                }
                else return;
            }

            // Use a multiplier to speed up the selection update when the stick is held in either direction
            double multiplier = 1.00;
            if (selectionUpdateIntervalCounter > 0)
                multiplier = (double)1.00 - ((double)selectionUpdateIntervalCounter / ((double)selectionUpdateIntervalCounterMax * 1.6));

            // If the selection update counter is greater than the selection update interval, update the selection
            if (selectionUpdateCounter >= selectionUpdateInterval * multiplier)
            {
                int[] leftStickDirection = controllerStates[0].GetLeftStickDirection();

                // If the left of right stick's direction is up
                if (leftStickDirection[1] == -1)
                {
                    // Reset the selection update counter and increment the selection update interval counter
                    selectionUpdateCounter = 0;
                    if (selectionUpdateIntervalCounter < selectionUpdateIntervalCounterMax)
                        selectionUpdateIntervalCounter++;

                    // If the Home Menu is visible, decrement the currently selected Home Index
                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedHomeIndex -= 1;
                        if (currentlySelectedHomeIndex < 0)
                            currentlySelectedHomeIndex = 0;

                        // Highlight the current Home Menu Option
                        HighlightCurrentHomeMenuOption();
                    }
                    // If the Selection Menu is visible, decrement the currently selected Game Index
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedGameIndex -= 1;
                        if (currentlySelectedGameIndex < -1)
                            currentlySelectedGameIndex = -1;

                        // Highlight the current Game Menu Option and debounce the game info display update
                        HighlightCurrentGameMenuOption();
                        DebounceUpdateGameInfoDisplay();
                    }
                }
                // If the left of right stick's direction is down
                else if (leftStickDirection[1] == 1)
                {
                    // Reset the selection update counter and increment the selection update interval counter
                    selectionUpdateCounter = 0;
                    if (selectionUpdateIntervalCounter < selectionUpdateIntervalCounterMax)
                        selectionUpdateIntervalCounter++;

                    // If the Home Menu is visible, increment the currently selected Home Index
                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedHomeIndex += 1;
                        if (currentlySelectedHomeIndex > 2)
                            currentlySelectedHomeIndex = 2;

                        // Highlight the current Home Menu Option
                        HighlightCurrentHomeMenuOption();
                    }
                    // If the Selection Menu is visible, increment the currently selected Game Index
                    else if (SelectionMenu.Visibility == Visibility.Visible)
                    {
                        currentlySelectedGameIndex += 1;
                        if (currentlySelectedGameIndex > gameInfoFilesList.Length - 1)
                            currentlySelectedGameIndex = gameInfoFilesList.Length - 1;

                        // Highlight the current Game Menu Option and debounce the game info display update
                        HighlightCurrentGameMenuOption();
                        DebounceUpdateGameInfoDisplay();
                    }
                }
            }

            // Check if the Start/A button is pressed
            if (timeSinceLastButton > 250 && (controllerStates[0].GetButtonState(1) || controllerStates[0].GetButtonState(2)))
            {
                // Reset the time since the last button press
                timeSinceLastButton = 0;

                // Check if the Home Menu is visible
                if (HomeMenu.Visibility == Visibility.Visible)
                {
                    // If the Game Library option is selected
                    if (currentlySelectedHomeIndex == 0)
                    {
                        // Show the Selection Menu
                        GameLibraryButton_Click(null, null);
                    }
                    // If the About option is selected
                    else if (currentlySelectedHomeIndex == 1)
                    {
                        // Show the Credits
                        AboutButton_Click(null, null);
                    }
                    // If the Exit option is selected
                    else if (currentlySelectedHomeIndex == 2)
                    {
                        // Go back to the Start Menu
                        ExitButton_Click(null, null);
                    }
                }
                // Else check if the Selection Menu is visible
                else if (SelectionMenu.Visibility == Visibility.Visible)
                {
                    // If a game is selected, attempt to start the game
                    if (currentlySelectedGameIndex >= 0) StartButton_Click(null, null);
                    // If the back button is selected, return to the Home Menu
                    else BackFromGameLibraryButton_Click(null, null);
                }
            }

            // Check if the Exit/B button is pressed
            if (timeSinceLastButton > 250 && (controllerStates[0].GetButtonState(0) || controllerStates[0].GetButtonState(3)))
            {
                // Reset the time since the last button press
                timeSinceLastButton = 0;

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

        private void HighlightCurrentHomeMenuOption()
        {
            // Reset the colour of all Home Menu Options and remove the "<" character if present
            foreach (TextBlock option in homeOptionsList)
            {
                option.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                if (option.Text.EndsWith(" <"))
                    option.Text = option.Text.Substring(0, option.Text.Length - 2);
            }

            // Highlight the currently selected Home Menu Option and add the "<" character
            homeOptionsList[currentlySelectedHomeIndex].Foreground = GetCurrentSelectionAnimationBrush();
            if (!homeOptionsList[currentlySelectedHomeIndex].Text.EndsWith(" <"))
                homeOptionsList[currentlySelectedHomeIndex].Text += " <";
        }

        private void HighlightCurrentGameMenuOption()
        {
            // Reset the colour of all Game Menu Options and remove the "<" character if present
            foreach (TextBlock title in gameTitlesList)
            {
                title.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                if (title.Text.EndsWith(" <"))
                    title.Text = title.Text.Substring(0, title.Text.Length - 2);
            }

            //If a game is selected
            if (currentlySelectedGameIndex >= 0)
            {
                // Highlight the currently selected Game Menu Option and add the "<" character
                gameTitlesList[currentlySelectedGameIndex % 10].Foreground = GetCurrentSelectionAnimationBrush();
                if (!gameTitlesList[currentlySelectedGameIndex % 10].Text.EndsWith(" <"))
                    gameTitlesList[currentlySelectedGameIndex % 10].Text += " <";
            }

            // Check if the page needs to be changed
            if (currentlySelectedGameIndex < 0)
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
            // Enable the Start Button
            BackFromGameLibraryButton.IsChecked = false;
            StartButton.Content = "Start";

            // Check if the current page needs to be changed
            int pageIndex = currentlySelectedGameIndex / 10;
            if (pageIndex != previousPageIndex)
                ChangePage(pageIndex);
        }

        private void ChangePage(int _pageIndex)
        {
            // Check if the page index is within the bounds of the game info files list
            if (_pageIndex < 0)
                _pageIndex = 0;
            else if (_pageIndex > gameInfoFilesList.Length / 10)
                _pageIndex = gameInfoFilesList.Length / 10;

            // Set the previous page index to the current page index
            previousPageIndex = _pageIndex;

            ResetTitles();

            // For each title on the current page
            for (int i = 0; i < 10; i++)
            {
                // Break if the current index is out of bounds
                if (i + _pageIndex * 10 >= gameInfoFilesList.Length || gameInfoFilesList[i + _pageIndex * 10] == null)
                    break;

                // Set the text of the title and make it visible
                gameTitlesList[i].Text = gameInfoFilesList[i + _pageIndex * 10]["GameName"].ToString();
                gameTitlesList[i].Visibility = Visibility.Visible;
            }
        }

        private void UpdateGameInfoDisplay()
        {
            // Update the game info
            if (currentlySelectedGameIndex != -1 && gameInfoFilesList[currentlySelectedGameIndex] != null)
            {
                ResetGameInfoDisplay();

                StartButton.IsChecked = true;

                // Set the Game Thumbnail
                NonGif_GameThumbnail.Source = new BitmapImage(new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[currentlySelectedGameIndex]["FolderName"].ToString(), gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString()), UriKind.Absolute));
                AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[currentlySelectedGameIndex]["FolderName"].ToString(), gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString()), UriKind.Absolute));

                // Set the Game Info and Authors
                GameTitle.Text = gameInfoFilesList[currentlySelectedGameIndex]["GameName"].ToString();
                GameAuthors.Text = string.Join(", ", gameInfoFilesList[currentlySelectedGameIndex]["GameAuthors"].ToObject<string[]>());

                // Fetch the Game Tag Elements (Borders and TextBlocks)
                Border[] GameTagBorder = new Border[9] { GameTagBorder0, GameTagBorder1, GameTagBorder2, GameTagBorder3, GameTagBorder4, GameTagBorder5, GameTagBorder6, GameTagBorder7, GameTagBorder8 };
                TextBlock[] GameTag = new TextBlock[9] { GameTag0, GameTag1, GameTag2, GameTag3, GameTag4, GameTag5, GameTag6, GameTag7, GameTag8 };
                JArray tags = (JArray)gameInfoFilesList[currentlySelectedGameIndex]["GameTags"];

                // For each Stated Game Tag
                for (int j = 0; j < tags.Count; j++)
                {
                    // Change Visibility
                    GameTagBorder[j].Visibility = Visibility.Visible;

                    // Change Text Content
                    GameTag[j].Text = tags[j]["Name"].ToString();

                    // Change Border and Text Colour
                    string colour = "#FF777777";

                    // If the Colour is not null or empty, set the colour
                    if (tags[j]["Colour"] != null && tags[j]["Colour"].ToString() != "")
                        colour = tags[j]["Colour"].ToString();

                    // Set the Border and Text Colour
                    GameTag[j].Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                    GameTagBorder[j].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                }

                // Set the Game Description and Version
                GameDescription.Text = gameInfoFilesList[currentlySelectedGameIndex]["GameDescription"].ToString();
                VersionText.Text = "v" + gameInfoFilesList[currentlySelectedGameIndex]["GameVersion"].ToString();
            }

            showingDebouncedGame = true;
        }

        // Reset Methods

        private void ResetTitles()
        {
            // Reset the visibility of all titles
            for (int i = 0; i < 10; i++)
                gameTitlesList[i].Visibility = Visibility.Hidden;
        }

        private void ResetGameInfoDisplay()
        {
            // Reset the Thumbnail
            NonGif_GameThumbnail.Source = new BitmapImage(new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));
            AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));

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

            GameTagBorder0.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder1.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder2.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder3.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder4.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder5.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder6.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder7.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
            GameTagBorder8.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
        }

        // Custom Methods (Debounce, CloneXamlElement)

        private void DebounceUpdateGameInfoDisplay()
        {
            showingDebouncedGame = false;

            if (updateGameInfoDisplayDebounceTimer != null)
            {
                updateGameInfoDisplayDebounceTimer.Stop();
                updateGameInfoDisplayDebounceTimer.Dispose();
            }

            // Create a new Timer for Debouncing the UpdateGameInfoDisplay method to prevent it from being called more than once every 500ms
            updateGameInfoDisplayDebounceTimer = new System.Timers.Timer(500);
            updateGameInfoDisplayDebounceTimer.Elapsed += (sender, e) =>
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    try { Application.Current.Dispatcher.Invoke(() => { UpdateGameInfoDisplay(); }); }
                    catch (TaskCanceledException) { }
                }
            };

            // Set the Timer to AutoReset and Enabled
            updateGameInfoDisplayDebounceTimer.AutoReset = false;
            updateGameInfoDisplayDebounceTimer.Enabled = true;
        }
    
        private T CloneXamlElement<T>(T element) where T : UIElement
        {
            // Clone the XAML element and return it
            string xaml = XamlWriter.Save(element);
            StringReader stringReader = new StringReader(xaml);
            XmlReader xmlReader = XmlReader.Create(stringReader);
            return (T)XamlReader.Load(xmlReader);
        }
    }

    struct Version
    {
        // Zero value for the Version struct
        internal static Version zero = new Version(0, 0, 0);

        public int major;
        public int minor;
        public int subMinor;

        internal Version(short _major, short _minor, short _subMinor)
        {
            // Initialize the version number
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
        }

        internal Version(string version)
        {
            string[] parts = version.Split('.');
            
            // Reset the version number if it is not in the correct format
            if (parts.Length != 3)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                return;
            }

            // Parse the version number
            major = int.Parse(parts[0]);
            minor = int.Parse(parts[1]);
            subMinor = int.Parse(parts[2]);
        }

        internal bool IsDifferentVersion(Version _otherVersion)
        {
            // Compare each part of the version number
            if (major != _otherVersion.major)
                return true;
            else if (minor != _otherVersion.minor)
                return true;
            else if (subMinor != _otherVersion.subMinor)
                return true;
            else return false;
        }

        public override string ToString()
        {
            // Return the version number as a string
            return $"{major}.{minor}.{subMinor}";
        }
    }
}
