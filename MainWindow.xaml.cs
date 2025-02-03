// System Libraries

using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// Custom Libraries

using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using XamlAnimatedGif;
using NAudio.Wave;
using Research_Arcade_Launcher;

namespace ArcademiaGameLauncher
{
    // Launcher State Enum

    enum GameState
    {
        checkingForUpdates,
        downloadingGame,
        downloadingUpdate,
        failed,
        loadingInfo,
        ready,
        launching,
        runningGame
    }

    public partial class MainWindow : Window
    {
        readonly bool production;

        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);
        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public readonly string RootPath;
        private readonly string gameDirectoryPath;

        private readonly string configPath;
        private JObject config;

        private string gameDatabaseURL;
        private readonly string localGameDatabasePath;
        private JObject gameDatabaseFile;

        private int arcadeMachineID;

        private int updateIndexOfGame;
        private System.Timers.Timer updateTimer;

        private int selectionAnimationFrame = 0;
        private readonly int selectionAnimationFrameRate = 100;

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

        private readonly int selectionUpdateInterval = 150;
        private int selectionUpdateIntervalCounter = 0;
        private readonly int selectionUpdateIntervalCounterMax = 10;
        private int selectionUpdateCounter = 0;

        private int currentlySelectedHomeIndex = 0;

        private int currentlySelectedGameIndex;
        private int previousPageIndex = 0;
        private System.Timers.Timer updateGameInfoDisplayDebounceTimer;
        private bool showingDebouncedGame = false;

        private int afkTimer = 0;
        private int noInputTimeout = 0;
        private bool afkTimerActive = false;

        private int timeSinceLastButton = 0;

        private JObject[] gameInfoFilesList;

        private TextBlock[] homeOptionsList;
        private TextBlock[] gameTitlesList;

        private DirectInput directInput;
        private readonly List<ControllerState> controllerStates = new List<ControllerState>();

        private System.Windows.Shapes.Ellipse[] inputMenuJoysticks;
        private System.Windows.Shapes.Ellipse[][] inputMenuButtons;

        private Process currentlyRunningProcess = null;

        private GameState[] gameTitleStates;

        private readonly InfoWindow infoWindow;
        private readonly EmojiParser emojiParser;

        private JArray audioFiles;
        private string[] audioFileNames = new string[0];
        private int[] periodicAudioFiles;

        // MAIN WINDOW

        public MainWindow()
        {
            // Setup closing event
            Closing += Window_Closing;

            // Load the info window
            infoWindow = new InfoWindow();

            InitializeComponent();

            // Setup Input Joysticks
            inputMenuJoysticks = new System.Windows.Shapes.Ellipse[2] { InputMenu_P1_Joy, InputMenu_P2_Joy };

            // Setup Input Buttons
            inputMenuButtons = new System.Windows.Shapes.Ellipse[2][];
            inputMenuButtons[0] = new System.Windows.Shapes.Ellipse[8] { InputMenu_P1_Exit, InputMenu_P1_Start, InputMenu_P1_A, InputMenu_P1_B, InputMenu_P1_C, InputMenu_P1_D, InputMenu_P1_E, InputMenu_P1_F };
            inputMenuButtons[1] = new System.Windows.Shapes.Ellipse[8] { InputMenu_P2_Exit, InputMenu_P2_Start, InputMenu_P2_A, InputMenu_P2_B, InputMenu_P2_C, InputMenu_P2_D, InputMenu_P2_E, InputMenu_P2_F };

            // Setup Directories
            if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Launcher")))
            {
                RootPath = Path.Combine(Directory.GetCurrentDirectory(), "Launcher");
                production = true;
            }
            else
            {
                RootPath = Directory.GetCurrentDirectory();
                production = false;
            }

            emojiParser = new EmojiParser(this);

            configPath = Path.Combine(RootPath, "json", "Config.json");
            gameDirectoryPath = Path.Combine(RootPath, "Games");

            localGameDatabasePath = Path.Combine(gameDirectoryPath, "GameDatabase.json");

            // Get the ID of the arcade machine
            arcadeMachineID = 0;

            // Read the ArcadeMachineID.txt file to get the ID of the arcade machine
            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")))
                arcadeMachineID = int.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")));
            // Write the ID of the arcade machine to the ArcadeMachineID.txt file if it doesn't exist
            else
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt"), arcadeMachineID.ToString());

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(gameDirectoryPath))
                Directory.CreateDirectory(gameDirectoryPath);

            // Set the locations of each item on the start menu
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            // StartMenu_Rect
            double RectActualWidth = Math.Cos((double)5 * (Math.PI / (double)180)) * (double)StartMenu_Rect.Width;
            double RectActualHeight = Math.Sin((double)5 * Math.PI / (double)180) * (double)StartMenu_Rect.Width + Math.Cos((double)5 * Math.PI / (double)180) * (double)StartMenu_Rect.Height;

            Canvas.SetLeft(StartMenu_Rect, (screenWidth / 2) - (RectActualWidth / 2) + ((double)screenWidth / 30));
            Canvas.SetTop(StartMenu_Rect, screenHeight / 2 - RectActualHeight / 2);

            // StartMenu_ArcademiaLogo
            Canvas.SetLeft(StartMenu_ArcademiaLogo, screenWidth / 2 - StartMenu_ArcademiaLogo.Width / 2);
            Canvas.SetTop(StartMenu_ArcademiaLogo, 100);

            // PressStartText
            Canvas.SetLeft(PressStartText, screenWidth / 2 - PressStartText.Width / 2);
            Canvas.SetTop(PressStartText, screenHeight / 2 - PressStartText.Height / 2);

            // Set width and height of the logos
            double logoWidth = (double)screenWidth * (double)0.06;

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
            directInput = new DirectInput();

            // Find a JoyStick Guid
            List<Guid> joystickGuids = new List<Guid>();

            // Find a Gamepad Guid
            foreach (var deviceInstance in directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
                joystickGuids.Add(deviceInstance.InstanceGuid);

            // If no Gamepad is found, find a Joystick
            if (joystickGuids.Count == 0)
                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
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
                Joystick joystick = new Joystick(directInput, joystickGuid);

                // Query all suported ForceFeedback effects
                var allEffects = joystick.GetEffects();
                foreach (var effectInfo in allEffects)
                    Console.WriteLine(effectInfo.Name);

                // Set BufferSize in order to use buffered data.
                joystick.Properties.BufferSize = 128;

                // Acquire the joystick
                joystick.Acquire();

                // Create a new ControllerState object for the joystick
                ControllerState controllerState = new ControllerState(joystick, controllerStates.Count, this);
                controllerStates.Add(controllerState);
            }
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

        // Downloading and Installing Updater Methods

        public void CheckForUpdaterUpdates()
        {
            try
            {
                // Get the online version of the Updater
                WebClient webClient = new WebClient();

                // Create the version file if it doesn't exist
                if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Updater_Version.txt")))
                    File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Updater_Version.txt"), "0.0.0");

                // Get the local version of the Updater
                Version currentVersion = new Version(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Updater_Version.txt")));

                // Get the online version of the Updater
                Version latestVersion = new Version(webClient.DownloadString(EncodeOneDriveLink(config["UpdaterVersionURL"].ToString())));

                // Check if the updater is up to date
                if (currentVersion.IsDifferentVersion(latestVersion))
                {
                    // Close the currently running process
                    if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                    {
                        currentlyRunningProcess.Kill();
                        currentlyRunningProcess = null;
                    }

                    // Find the Updater process and close it
                    Process[] processes = Process.GetProcessesByName("Research-Arcade-Updater");
                    foreach (Process process in processes)
                        process.Kill();

                    // Wait for 1 second to allow the updater to close
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000);

                        // Update the updater
                        UpdateUpdater();

                        // Update the version file
                        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Updater_Version.txt"), latestVersion.ToString());

                        // Start the new updater
                        Process.Start(Path.Combine(Directory.GetCurrentDirectory(), "Research-Arcade-Updater.exe"));

                        // Close the current application
                        try { Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown()); }
                        catch (TaskCanceledException) { }
                    });
                }
            }
            catch (Exception) { }
        }

        private void UpdateUpdater()
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

            // Delete the old updater files (except the Launcher folder and the version file)
            foreach (string file in Directory.GetFiles(Directory.GetCurrentDirectory()))
                if (file != Path.Combine(Directory.GetCurrentDirectory(), "Updater_Version.txt") &&
                    file != Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt") &&
                    file != Path.Combine(Directory.GetCurrentDirectory(), "Launcher"))
                    File.Delete(file);

            try
            {
                // Download the updater
                WebClient webClient = new WebClient();
                webClient.DownloadFile(EncodeOneDriveLink(config["UpdaterURL"].ToString()), Path.Combine(Directory.GetCurrentDirectory(), "Updater.zip"));

                // Extract the updater
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(Path.Combine(Directory.GetCurrentDirectory(), "Updater.zip"), Directory.GetCurrentDirectory(), null);

                // Delete the zip file
                File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "Updater.zip"));
            }
            catch (Exception) { }
        }

        // Downloading and Installing Game Methods

        public bool CheckForGameDatabaseChanges()
        {
            try
            {
                // Get the game database file from the online URL
                WebClient webClient = new WebClient();
                //gameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath)); // For Testing
                gameDatabaseFile = JObject.Parse(webClient.DownloadString(EncodeOneDriveLink(gameDatabaseURL)));

                // If the local game database file does not exist, create it and write the game database to it
                if (!File.Exists(localGameDatabasePath))
                    File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                // Save the FolderName property of each local game and write it to the new game database file
                JObject localGameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath));

                // Check if the ID of the arcade machine is valid
                if (arcadeMachineID >= ((JArray)gameDatabaseFile["Cabinets"]).Count || arcadeMachineID < 0)
                {
                    arcadeMachineID = 0;
                    File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt"), arcadeMachineID.ToString());
                }

                JArray localGames = (JArray)localGameDatabaseFile["Cabinets"][arcadeMachineID]["Games"];

                JArray onlineGames = (JArray)gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"];
                gameInfoFilesList = new JObject[onlineGames.Count];
                gameTitleStates = new GameState[onlineGames.Count];

                for (int i = 0; i < onlineGames.Count; i++)
                    gameTitleStates[i] = GameState.loadingInfo;

                // Show the game titles as "Loading..." until the game database is updated
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        for (int i = previousPageIndex * 10; i < (previousPageIndex + 1) * 10; i++)
                        {
                            if (i < onlineGames.Count)
                            {
                                gameTitlesList[i % 10].Text = "Loading...";
                                gameTitlesList[i % 10].Visibility = Visibility.Visible;
                            }
                            else
                                gameTitlesList[i % 10].Visibility = Visibility.Hidden;
                        }
                    });
                }
                catch (TaskCanceledException) { }


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
                                gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][i]["FolderName"] = localGames[j]["FolderName"].ToString();
                            else
                                gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][i]["FolderName"] = "";
                            break;
                        }
                    }
                }

                File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                JArray games = (JArray)gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"];

                if (games.Count > 0)
                    // In a new thread, check for updates for each game (CheckForUpdatesInit)
                    Task.Run(() => CheckForUpdatesInit(games.Count));
                //else
                //    // If no games are found, show an error message
                //    MessageBox.Show("Failed to get game database: No games found.");

                return true;
            }
            catch (Exception)
            {
                // If the game database cannot be retrieved, show an error message
                //MessageBox.Show($"Failed to get game database: {ex.Message}");

                if (File.Exists(localGameDatabasePath))
                    gameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath));

                arcadeMachineID = 0;

                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")))
                    arcadeMachineID = int.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")));


                JArray games = (JArray)gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"];

                gameInfoFilesList = new JObject[games.Count];
                gameTitleStates = new GameState[games.Count];

                // Show the game titles as "Loading..." until each title is loaded
                for (int i = previousPageIndex * 10; i < (previousPageIndex + 1) * 10; i++)
                {
                    if (i < games.Count)
                    {
                        gameTitlesList[i % 10].Text = "Loading...";
                        gameTitlesList[i % 10].Visibility = Visibility.Visible;
                    }
                    else
                        gameTitlesList[i % 10].Visibility = Visibility.Hidden;
                }

                if (games.Count > 0)
                    // In a new thread, check for updates for each game (CheckForUpdatesInit)
                    Task.Run(() => CheckForUpdatesInit(games.Count));
                //else
                //    // If no games are found, show an error message
                //    MessageBox.Show("Failed to get game database: No games found.");

                return false;
            }
        }

        private void CheckForUpdatesInit(int totalGames)
        {
            // Check for updates for each game
            for (int i = 0; i < totalGames; i++)
                CheckForUpdates(i);
        }

        private void CheckForUpdates(int _updateIndexOfGame)
        {
            SetGameTitleState(_updateIndexOfGame, GameState.checkingForUpdates);
            DebounceUpdateGameInfoDisplay();

            // Set the updateIndexOfGame to the index of the game being updated
            updateIndexOfGame = _updateIndexOfGame;

            if (gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["FolderName"] == null)
                gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["FolderName"] = "";

            string folderName = gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["FolderName"].ToString();
            string localGameInfoPath = "";

            if (folderName != "")
                localGameInfoPath = Path.Combine(gameDirectoryPath, folderName, "GameInfo.json");

            // Check if the game has a local GameInfo.json file
            if (localGameInfoPath != "" && File.Exists(localGameInfoPath))
            {
                gameInfoFilesList[updateIndexOfGame] = JObject.Parse(File.ReadAllText(localGameInfoPath));

                // Update the game title text block if it's on the first page of the Selection Menu
                if (updateIndexOfGame >= previousPageIndex * 10 && updateIndexOfGame < (previousPageIndex + 1) * 10)
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            gameTitlesList[updateIndexOfGame % 10].Text = gameInfoFilesList[updateIndexOfGame]["GameName"].ToString();
                            gameTitlesList[updateIndexOfGame % 10].Visibility = Visibility.Visible;
                        });
                    }
                    catch (TaskCanceledException) { }

                // Get the local version of the game and update the VersionText text block
                Version localVersion = new Version(gameInfoFilesList[updateIndexOfGame]["GameVersion"].ToString());
                try { Application.Current?.Dispatcher?.Invoke(() => { VersionText.Text = "v" + localVersion.ToString(); }); }
                catch (TaskCanceledException) { }

                try
                {
                    // Get the online version of the game
                    WebClient webClient = new WebClient();
                    JObject onlineJson = JObject.Parse(webClient.DownloadString(EncodeOneDriveLink(gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString())));
                    Version onlineVersion = new Version(onlineJson["GameVersion"].ToString());

                    // Compare the local version with the online version to see if an update is needed
                    if (onlineVersion.IsDifferentVersion(localVersion))
                        InstallGameFiles(true, onlineJson, gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
                    else
                        SetGameTitleState(updateIndexOfGame, GameState.ready);
                }
                catch (Exception)
                {
                    SetGameTitleState(updateIndexOfGame, GameState.failed);
                }
            }
            else
                // If the game does not have a local GameInfo.json file, install the game files with a temporary GameInfo.json file of file version 0.0.0
                InstallGameFiles(false, JObject.Parse("{\r\n\"GameVersion\": \"0.0.0\"\r\n}\r\n"), gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
        }

        private void InstallGameFiles(bool _isUpdate, JObject _onlineJson, string _downloadURL)
        {
            try
            {
                WebClient webClient = new WebClient();
                if (_isUpdate)
                {
                    // If the game has an update, set the state to downloadingUpdate
                    SetGameTitleState(updateIndexOfGame, GameState.downloadingUpdate);
                }
                else
                {
                    // If the game doesn't have an update, set the state to downloadingGame
                    SetGameTitleState(updateIndexOfGame, GameState.downloadingGame);

                    // Set _onlineJson to the online JSON object
                    _onlineJson = JObject.Parse(webClient.DownloadString(EncodeOneDriveLink(_downloadURL)));
                }

                // Asynchronously download the game zip file
                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri(EncodeOneDriveLink(_onlineJson["LinkToGameZip"].ToString())), Path.Combine(RootPath, _onlineJson["FolderName"].ToString() + ".zip"), _onlineJson);
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
                SetGameTitleState(updateIndexOfGame, GameState.failed);
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
                JArray games = (JArray)gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"];

                // Find the index of the game being updated from the game database
                for (int i = 0; i < games.Count; i++)
                {
                    if (JObject.Parse(webClient.DownloadString(EncodeOneDriveLink(games[i]["LinkToGameInfo"].ToString())))["LinkToGameZip"].ToString() == onlineJson["LinkToGameZip"].ToString())
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
                string pathToZip = Path.Combine(RootPath, onlineJson["FolderName"].ToString() + ".zip");
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(pathToZip, Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString()), null);
                File.Delete(pathToZip);

                // Update the game database with the new FolderName property
                JObject gameDatabase = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                gameDatabase["Cabinets"][arcadeMachineID]["Games"][currentUpdateIndexOfGame]["FolderName"] = onlineJson["FolderName"].ToString();

                // Write the updated game database to the local game database file (lock to prevent multiple threads writing to the file at the same time)
                lock (gameDatabaseFile)
                {
                    File.WriteAllText(localGameDatabasePath, gameDatabase.ToString());
                }

                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        // Set the game database variable to the updated game database
                        gameDatabaseFile = gameDatabase;

                        // Check if the folder exists, if not create it
                        if (!Directory.Exists(Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString())))
                            Directory.CreateDirectory(Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString()));

                        // Write the GameInfo.json file to the game directory
                        File.WriteAllText(Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString(), "GameInfo.json"), onlineJson.ToString());

                        // Update the gameInfoFilesList with the online JSON object
                        gameInfoFilesList[currentUpdateIndexOfGame] = onlineJson;

                        // Update the game title text block if it's visible
                        if (currentUpdateIndexOfGame >= previousPageIndex * 10 && currentUpdateIndexOfGame < (previousPageIndex + 1) * 10)
                        {
                            gameTitlesList[currentUpdateIndexOfGame % 10].Text = onlineJson["GameName"].ToString();
                            gameTitlesList[currentUpdateIndexOfGame % 10].Visibility = Visibility.Visible;
                        }

                        SetGameTitleState(currentUpdateIndexOfGame, GameState.loadingInfo);
                    });
                }
                catch (TaskCanceledException) { }

                if (currentlySelectedGameIndex == currentUpdateIndexOfGame)
                    DebounceUpdateGameInfoDisplay();

            }
            catch (Exception ex)
            {
                // If the download fails due to (403 Forbidden) retry the download
                if (ex.Message.Contains("403"))
                {
                    DownloadGameCompletedCallback(sender, e);
                    return;
                }

                JObject onlineJson = (JObject)e.UserState;

                if (!ex.Message.Contains(onlineJson["GameThumbnail"].ToString()))
                {
                    // If the download fails, set the state to failed and show an error message
                    SetGameTitleState(updateIndexOfGame, GameState.failed);
                    MessageBox.Show($"Failed to complete download: {ex.Message}");
                }
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
                });
            }
            catch (TaskCanceledException) { }

            // Set the focus to the game launcher
            SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);

            // Set the currently selected game index to 0
            currentlySelectedGameIndex = 0;
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
                    Canvas.SetTop(CreditsPanel, (int)SystemParameters.PrimaryScreenHeight);
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
            currentlySelectedHomeIndex = 0;
            HighlightCurrentHomeMenuOption();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // If the game info display is not showing the currently selected game, return
            if (!showingDebouncedGame || gameTitleStates[currentlySelectedGameIndex] != GameState.ready)
                return;

            // Get the current game folder, game info, and game executable
            string currentGameFolder = gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"][currentlySelectedGameIndex]["FolderName"].ToString();
            string currentGameExe = Path.Combine
                (
                    gameDirectoryPath,
                    currentGameFolder,
                    gameInfoFilesList[currentlySelectedGameIndex]["NameOfExecutable"].ToString()
                );

            // Start the game if the game executable exists and the launcher is ready
            if (File.Exists(currentGameExe))
            {
                // Create a new ProcessStartInfo object and set the Working Directory to the game directory
                ProcessStartInfo startInfo = new ProcessStartInfo(currentGameExe)
                {
                    WorkingDirectory = currentGameFolder
                };

                // Start the game if no process is currently running
                if (currentlyRunningProcess == null || currentlyRunningProcess.HasExited)
                {
                    currentlyRunningProcess = Process.Start(startInfo);

                    StyleStartButtonState(GameState.launching);
                }

                // Set focus to the currently running process
                SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
                    
                // After 3 seconds, set the focus to the currently running process
                Task.Delay(3000).ContinueWith(t =>
                {
                    if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                        SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);

                    SetGameTitleState(currentlySelectedGameIndex, GameState.runningGame);
                    StyleStartButtonState(currentlySelectedGameIndex);
                });
            }
            // Run CheckForUpdatesInit again
            else if (gameTitleStates[currentlySelectedGameIndex] == GameState.failed)
                Task.Run(() => CheckForUpdatesInit(((JArray)gameDatabaseFile["Cabinets"][arcadeMachineID]["Games"]).Count));
        }

        // Event Handlers

        public void Key_Pressed()
        {
            // Keylogger for AFK Timer
            if (afkTimerActive)
            {
                afkTimer = 0;
            }
            else
            {
                afkTimerActive = true;
                afkTimer = 0;
                timeSinceLastButton = 0;

                // Show the Home Menu
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StartMenu.Visibility = Visibility.Collapsed;
                        HomeMenu.Visibility = Visibility.Visible;
                        SelectionMenu.Visibility = Visibility.Collapsed;
                        InputMenu.Visibility = Visibility.Collapsed;

                        currentlySelectedHomeIndex = 0;
                        HighlightCurrentHomeMenuOption();
                    });
                }
                catch (TaskCanceledException) { }

                // Set the focus to the game launcher
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            // Set the Copyright text
            Copyright.Text = "Copyright ©️ 2018 - " + DateTime.Now.Year + "\nUniversity of Lincoln,\nAll rights reserved.";

            // Initialize the TextBlock arrays
            homeOptionsList = new TextBlock[4] { GameLibraryText, InputMenuText, AboutText, ExitText };
            gameTitlesList = new TextBlock[10] { GameTitleText0, GameTitleText1, GameTitleText2, GameTitleText3, GameTitleText4, GameTitleText5, GameTitleText6, GameTitleText7, GameTitleText8, GameTitleText9 };

            // Generate the Credits from the Credits.json file
            GenerateCredits();

            // Load the GameDatabaseURL from the Config.json file
            bool foundGameDatabase = false;
            if (File.Exists(configPath))
            {
                // Load Config.json
                config = JObject.Parse(File.ReadAllText(configPath));

                // Get the GameDatabaseURL and NoInputTimeout from the Config.json file
                gameDatabaseURL = config["GameDatabaseURL"].ToString();
                noInputTimeout = int.Parse(config["NoInputTimeout_ms"].ToString());

                // Start checking for game database changes and game updates
                foundGameDatabase = CheckForGameDatabaseChanges();
            }
            // If the Config.json file does not exist, show an error message
            else
            {
                MessageBox.Show("Failed to get game database URL: Config.json does not exist.");
                Application.Current?.Shutdown();
            }

            // Quit the application
            //if (!foundGameDatabase) Application.Current.Shutdown();

            // Initialize the controller states
            JoyStickInit();

            // Every 30 minutes, check for updates to the updater,
            // and check for game database changes.
            Task.Run(async () =>
            {
                if (production) CheckForUpdaterUpdates();
                while (true)
                {
                    await Task.Delay(30 * 60 * 1000);
                    try
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (production) CheckForUpdaterUpdates();
                            CheckForGameDatabaseChanges();
                        });
                    }
                    catch (TaskCanceledException) { }
                }
            });

            // Perform an initial update of the game info display
            currentlySelectedGameIndex = 0;
            UpdateGameInfoDisplay();

            // Initialize the updateTimer
            InitializeUpdateTimer();
            
            Task.Run(() =>
            {
                // Delete all the audio files
                DeleteAllAudioFiles();

                // Download the audio files
                DownloadAudioFiles();

                // Connect to the WebSocket Server
                int arcadeMachineID = 0;
                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")))
                    arcadeMachineID = int.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "ArcadeMachineID.txt")));

                string arcadeMachineName = arcadeMachineID == 0 ? "Arcade Machine (CSS)" : arcadeMachineID == 1 ? "Arcade Machine (UoL)" : "Arcade Machine (Unknown)";

                if (!production) arcadeMachineName = "Arcade Machine (Test)";

                // Disable if not in production
                if (!production) return;

                // Disable if WS is disabled
                if (!(bool)config["WS_Enabled"]) return;

                new Socket(config["WS_IP"].ToString(), config["WS_Port"].ToString(), arcadeMachineName, this);

                // Every (between 30 mins and an hour), play a random audio file
                Task.Run(async () =>
                {
                    while (true)
                    {
                        await Task.Delay(new Random(DateTime.Now.Millisecond).Next(30 * 60 * 1000, 60 * 60 * 1000));
                        PlayRandomPeriodicAudioFile();
                    }
                });
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
                try { Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown()); }
                catch (TaskCanceledException) { }
            }

            // Update Controller Input
            for (int i = 0; i < controllerStates.Count; i++)
                controllerStates[i].UpdateButtonStates();

            // If exit is held, close the current process
            if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
            {
                int maxExitHeldFor = 0;
                for (int i = 0; i < controllerStates.Count; i++)
                    if (controllerStates[i].GetExitButtonHeldFor() > maxExitHeldFor)
                        maxExitHeldFor = controllerStates[i].GetExitButtonHeldFor();

                Console.WriteLine(maxExitHeldFor);

                // If the user has held the exit button for longer than 1 second, show the ForceExitMenu within the infoWindow
                if (maxExitHeldFor >= 1000)
                {
                    infoWindow?.SetCloseGameName(gameInfoFilesList[currentlySelectedGameIndex]["GameName"].ToString());
                    infoWindow?.ShowWindow(InfoWindowType.ForceExit);
                    infoWindow?.UpdateCountdown(3000 - maxExitHeldFor);
                }
                // Hide the infoWindow and set the focus back if the user has released the exit button
                else
                {
                    if (infoWindow.Visibility == Visibility.Visible && infoWindow.ForceExitMenu.Visibility == Visibility.Visible)
                    {
                        infoWindow?.HideWindow();
                        SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
                    }
                }

                // If the user has held the exit button for 3 seconds, close the currently running application
                if (maxExitHeldFor >= 3000)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        currentlyRunningProcess.Kill();
                        timeSinceLastButton = 0;
                        infoWindow?.HideWindow();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                    });
                }
            }


            // If the user is AFK for the specified time (Default: 2 minutes), Warn them and then close the currently running application
            if (afkTimer >= noInputTimeout)
            {
                infoWindow?.ShowWindow(InfoWindowType.Idle);
                infoWindow?.UpdateCountdown(noInputTimeout + 5000 - afkTimer);

                if (currentlyRunningProcess != null)
                    infoWindow?.SetCloseGameName(gameInfoFilesList[currentlySelectedGameIndex]["GameName"].ToString());
                else
                    infoWindow?.SetCloseGameName(null);

                // If the user is AFK for 5 seconds after the warning, close the currently running application and show the Start Menu
                if (afkTimer >= noInputTimeout + 5000)
                {
                    infoWindow?.UpdateCountdown(0);

                    // Reset the timer
                    afkTimerActive = false;
                    afkTimer = 0;

                    // Hide the Window
                    infoWindow?.HideWindow();

                    // Close the currently running application
                    if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                    {
                        currentlyRunningProcess.Kill();
                        SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                        currentlyRunningProcess = null;
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
                if (infoWindow.Visibility == Visibility.Visible && infoWindow.IdleMenu.Visibility == Visibility.Visible)
                {
                    infoWindow?.HideWindow();
                    timeSinceLastButton = 0;

                    // Set the focus to currently running process
                    if (currentlyRunningProcess != null)
                        SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
                }
            }

            // Increment the selection animation frame
            if ((HomeMenu.Visibility == Visibility.Visible || SelectionMenu.Visibility == Visibility.Visible) && globalCounter % selectionAnimationFrameRate == 0)
            {
                if (selectionAnimationFrame < selectionAnimationFrames.Length - 1)
                    selectionAnimationFrame++;
                else
                    selectionAnimationFrame = 0;

                // Highlight the current menu option
                if (HomeMenu.Visibility == Visibility.Visible)
                {
                    try { Application.Current?.Dispatcher?.Invoke(() => { HighlightCurrentHomeMenuOption(); }); }
                    catch (TaskCanceledException) { }
                }
                // Highlight the current game option
                else if (SelectionMenu.Visibility == Visibility.Visible)
                {
                    try { Application.Current?.Dispatcher?.Invoke(() => { HighlightCurrentGameMenuOption(); }); }
                    catch (TaskCanceledException) { }
                }
            }

            // Update the input menu feedback
            if (InputMenu.Visibility == Visibility.Visible)
            {
                try { Application.Current?.Dispatcher?.Invoke(() => { UpdateInputMenuFeedback(); }); }
                catch (TaskCanceledException) { }
            }

            // Update the Home/Selection Menu's current selection
            if (HomeMenu.Visibility == Visibility.Visible || SelectionMenu.Visibility == Visibility.Visible)
            {
                try { Application.Current?.Dispatcher?.Invoke(() => { UpdateCurrentSelection(); }); }
                catch (TaskCanceledException) { }
            }

            // Auto Scroll the Credits Panel
            if (CreditsPanel.Visibility == Visibility.Visible)
            {
                try { Application.Current?.Dispatcher?.Invoke(() => { AutoScrollCredits(); }); }
                catch (TaskCanceledException) { }
            }

            // Check if the currently running process has exited, and set the focus back to the launcher
            if (currentlyRunningProcess != null && currentlyRunningProcess.HasExited)
            {
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                currentlyRunningProcess = null;

                DebounceUpdateGameInfoDisplay();
            }

            // Flash the Start Button if the Start Menu is visible
            if (StartMenu.Visibility == Visibility.Visible)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
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

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Close the currently running process
            if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                currentlyRunningProcess.Kill();

            // Close the updateTimer
            updateTimer?.Close();
        }

        // Misc

        public void RestartLauncher() => Application.Current?.Dispatcher?.Invoke(() => Application.Current?.Shutdown());

        // Credits

        private void GenerateCredits()
        {
            // Read the Credits.json file
            string creditsPath = Path.Combine(RootPath, "json", "Credits.json");

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
                    JObject creditsObject = null;

                    // Check if creditsArray[i] is an array
                    if (creditsArray[i].Type == JTokenType.Array)
                        creditsObject = (JObject)((JArray)creditsArray[i])[arcadeMachineID];
                    else
                        creditsObject = (JObject)creditsArray[i];

                    switch (creditsObject["Type"].ToString())
                    {
                        case "Title":
                            // Create a new RowDefinition
                            RowDefinition titleRow = new RowDefinition
                            {
                                Height = new GridLength(60, GridUnitType.Pixel)
                            };
                            CreditsPanel.RowDefinitions.Add(titleRow);

                            // Create a new Grid
                            Grid titleGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetRow(titleGrid, 2 * i);

                            // Create 2 new RowDefinitions
                            RowDefinition titleGridTitleRow = new RowDefinition
                            {
                                Height = new GridLength(40, GridUnitType.Pixel)
                            };
                            titleGrid.RowDefinitions.Add(titleGridTitleRow);

                            RowDefinition titleGridSubtitleRow = new RowDefinition
                            {
                                Height = new GridLength(20, GridUnitType.Pixel)
                            };
                            titleGrid.RowDefinitions.Add(titleGridSubtitleRow);

                            // Create a new TextBlock (Title)
                            TextBlock titleText = new TextBlock
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 24,
                                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD9, 0x66)),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };

                            // Add the TextBlock to the Grid
                            Grid.SetRow(titleText, 0);
                            titleGrid.Children.Add(titleText);

                            // Create a new TextBlock (Subtitle)
                            if (creditsObject["Subtitle"] != null)
                            {
                                TextBlock subtitleText = new TextBlock
                                {
                                    Text = creditsObject["Subtitle"].ToString(),
                                    Style = (Style)FindResource("Early GameBoy"),
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Center
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
                            RowDefinition headingRow = new RowDefinition
                            {
                                Height = new GridLength(30 + (subheadingsArray.Count * 25), GridUnitType.Pixel)
                            };
                            CreditsPanel.RowDefinitions.Add(headingRow);

                            // Create a new Grid
                            Grid headingGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetRow(headingGrid, 2 * i);

                            // Create 2 new ColumnDefinitions
                            ColumnDefinition headingGridBorderColumn = new ColumnDefinition
                            {
                                Width = new GridLength(3, GridUnitType.Pixel)
                            };
                            headingGrid.ColumnDefinitions.Add(headingGridBorderColumn);

                            ColumnDefinition headingGridContentColumn = new ColumnDefinition
                            {
                                Width = new GridLength(1, GridUnitType.Star)
                            };
                            headingGrid.ColumnDefinitions.Add(headingGridContentColumn);

                            // Create a Grid to function as a border
                            Grid headingBorderGrid = new Grid
                            {
                                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x33, 0x33)),
                                Margin = new Thickness(0, 10, 0, 10)
                            };

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingBorderGrid, 0);
                            headingGrid.Children.Add(headingBorderGrid);

                            // Create a new Grid to hold the Title and Subheadings
                            Grid headingContentGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(25, 0, 0, 0)
                            };

                            // Add the Grid to the Grid
                            Grid.SetColumn(headingContentGrid, 1);
                            headingGrid.Children.Add(headingContentGrid);

                            // Create 2 new RowDefinitions
                            RowDefinition headingGridTitleRow = new RowDefinition
                            {
                                Height = new GridLength(30, GridUnitType.Pixel)
                            };
                            headingContentGrid.RowDefinitions.Add(headingGridTitleRow);

                            RowDefinition headingGridSubheadingsRow = new RowDefinition
                            {
                                Height = new GridLength(subheadingsArray.Count * 25, GridUnitType.Pixel)
                            };
                            headingContentGrid.RowDefinitions.Add(headingGridSubheadingsRow);

                            // Create a new TextBlock (Title)
                            TextBlock headingText = new TextBlock
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 16,
                                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x85, 0x8E, 0xFF)),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };

                            // Add the TextBlock to the Grid
                            Grid.SetRow(headingText, 0);
                            headingContentGrid.Children.Add(headingText);

                            // Create a new Grid for the Subheadings
                            Grid subheadingsGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetRow(subheadingsGrid, 1);

                            // For each Subheading
                            for (int j = 0; j < subheadingsArray.Count; j++)
                            {
                                // Create new RowDefinitions & for each Subheading
                                RowDefinition subheadingRow = new RowDefinition
                                {
                                    Height = new GridLength(25, GridUnitType.Pixel)
                                };
                                subheadingsGrid.RowDefinitions.Add(subheadingRow);

                                string colour = subheadingsArray[j]["Colour"] != null ? subheadingsArray[j]["Colour"].ToString() : "White";

                                // Create a new TextBlock (Subheading)
                                TextBlock subheadingText = new TextBlock
                                {
                                    Text = subheadingsArray[j]["Value"].ToString(),
                                    Style = (Style)FindResource("Early GameBoy"),
                                    FontSize = 18,
                                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour)),
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    VerticalAlignment = VerticalAlignment.Center
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
                            int noteHeight = 20 + (creditsObject["Value"].ToString().Length / 80 * 20);

                            // Create a new RowDefinition
                            RowDefinition noteRow = new RowDefinition
                            {
                                Height = new GridLength(noteHeight, GridUnitType.Pixel)
                            };
                            CreditsPanel.RowDefinitions.Add(noteRow);

                            // Create a new Grid
                            Grid noteGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 100, 0)
                            };
                            Grid.SetRow(noteGrid, 2 * i);

                            // Create a new TextBlock
                            TextBlock noteText = new TextBlock
                            {
                                Text = creditsObject["Value"].ToString(),
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                LineHeight = 20
                            };

                            // Add the TextBlock to the Grid
                            noteGrid.Children.Add(noteText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(noteGrid);

                            break;
                        case "Break":
                            // Create a new RowDefinition
                            RowDefinition breakRow = new RowDefinition
                            {
                                Height = new GridLength(10, GridUnitType.Pixel)
                            };
                            CreditsPanel.RowDefinitions.Add(breakRow);

                            // Create a new Grid
                            Grid breakGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetRow(breakGrid, 2 * i);

                            // Create a new TextBlock
                            TextBlock breakText = new TextBlock
                            {
                                Text = "----------------------",
                                Style = (Style)FindResource("Early GameBoy"),
                                FontSize = 16,
                                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77)),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };

                            // Add the TextBlock to the Grid
                            breakGrid.Children.Add(breakText);

                            // Add the Grid to the CreditsPanel
                            CreditsPanel.Children.Add(breakGrid);

                            break;
                        case "Image":
                            double overrideHeight = creditsObject["HeightOverride"] != null ? double.Parse(creditsObject["HeightOverride"].ToString()) : 100;
                            string stretch = creditsObject["Stretch"] != null ? creditsObject["Stretch"].ToString() : "Uniform";

                            // Create a new RowDefinition
                            RowDefinition imageRow = new RowDefinition
                            {
                                Height = new GridLength(overrideHeight, GridUnitType.Pixel)
                            };
                            CreditsPanel.RowDefinitions.Add(imageRow);

                            // Create a new Grid
                            Grid imageGrid = new Grid
                            {
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            Grid.SetRow(imageGrid, 2 * i);

                            string imagePath = creditsObject["Path"].ToString();

                            // Create a new Image (Static)
                            Image imageStatic = new Image
                            {
                                Source = new BitmapImage(new Uri(imagePath, UriKind.Relative))
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
                                Image imageGif = CloneXamlElement((Image)GifTemplateElement_Parent.Children[0]);
                                AnimationBehavior.SetSourceUri(imageGif, new Uri(imagePath, UriKind.Relative));

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
                        RowDefinition spaceRow = new RowDefinition
                        {
                            Height = new GridLength(40, GridUnitType.Pixel)
                        };
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

        private void SetGameTitleState(int _index, GameState _state)
        {
            // Set the game title state
            gameTitleStates[_index] = _state;

            // Style the Start Button based on the game title state
            if (currentlySelectedGameIndex == _index)
                StyleStartButtonState(_index);
        }

        // Update Methods

        private void UpdateCurrentSelection()
        {
            // For each Controller State
            for (int i = 0; i < controllerStates.Count; i++)
            {
                // If the infoWindow is visible, don't listen for inputs
                if (infoWindow.Visibility == Visibility.Visible)
                    return;

                // If theres a game running, don't listen for inputs
                if (currentlyRunningProcess != null && !currentlyRunningProcess.HasExited)
                    return;

                // Use a multiplier to speed up the selection update when the stick is held in either direction
                double multiplier = 1.00;
                if (selectionUpdateIntervalCounter > 0)
                    multiplier = (double)1.00 - ((double)selectionUpdateIntervalCounter / ((double)selectionUpdateIntervalCounterMax * 1.6));

                // If the selection update counter is greater than the selection update interval, update the selection
                if (selectionUpdateCounter >= selectionUpdateInterval * multiplier)
                {
                    int[] leftStickDirection = controllerStates[i].GetLeftStickDirection();

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
                        else if (SelectionMenu.Visibility == Visibility.Visible && gameInfoFilesList != null)
                        {
                            currentlySelectedGameIndex -= 1;
                            if (currentlySelectedGameIndex < -1)
                                currentlySelectedGameIndex = -1;
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
                        selectionUpdateCounter = 0;
                        if (selectionUpdateIntervalCounter < selectionUpdateIntervalCounterMax)
                            selectionUpdateIntervalCounter++;

                        // If the Home Menu is visible, increment the currently selected Home Index
                        if (HomeMenu.Visibility == Visibility.Visible)
                        {
                            currentlySelectedHomeIndex += 1;
                            if (currentlySelectedHomeIndex > homeOptionsList.Length - 1)
                                currentlySelectedHomeIndex = homeOptionsList.Length - 1;

                            // Highlight the current Home Menu Option
                            HighlightCurrentHomeMenuOption();
                        }
                        // If the Selection Menu is visible, increment the currently selected Game Index
                        else if (SelectionMenu.Visibility == Visibility.Visible && gameInfoFilesList != null)
                        {
                            currentlySelectedGameIndex += 1;
                            if (currentlySelectedGameIndex > gameInfoFilesList.Length - 1)
                                currentlySelectedGameIndex = gameInfoFilesList.Length - 1;
                            else
                            {
                                // Highlight the current Game Menu Option and debounce the game info display update
                                HighlightCurrentGameMenuOption();
                                DebounceUpdateGameInfoDisplay();
                            }
                        }
                    }
                }

                // Check if the Start/A button is pressed
                if (timeSinceLastButton > 250 && (controllerStates[i].GetButtonState(1) || controllerStates[i].GetButtonState(2)))
                {
                    // Reset the time since the last button press
                    timeSinceLastButton = 0;

                    // Check if the Home Menu is visible
                    if (HomeMenu.Visibility == Visibility.Visible)
                    {
                        // If the Game Library option is selected
                        if (homeOptionsList[currentlySelectedHomeIndex] == GameLibraryText)
                        {
                            // Show the Selection Menu
                            GameLibraryButton_Click(null, null);
                        }
                        // If the About option is selected
                        else if (homeOptionsList[currentlySelectedHomeIndex] == AboutText)
                        {
                            // Show the Credits
                            AboutButton_Click(null, null);
                        }
                        else if (homeOptionsList[currentlySelectedHomeIndex] == InputMenuText)
                        {
                            // Show the Input Menu
                            InputMenuButton_Click(null, null);
                        }
                        // If the Exit option is selected
                        else if (homeOptionsList[currentlySelectedHomeIndex] == ExitText)
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
                if (timeSinceLastButton > 250 && (controllerStates[i].GetButtonState(0) || controllerStates[i].GetButtonState(3)))
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

            // Check if the current page needs to be changed
            int pageIndex = currentlySelectedGameIndex / 10;
            if (pageIndex != previousPageIndex)
                ChangePage(pageIndex);

            //If a game is selected
            if (currentlySelectedGameIndex >= 0)
            {
                // Highlight the currently selected Game Menu Option and add the "<" character
                gameTitlesList[currentlySelectedGameIndex % 10].Foreground = GetCurrentSelectionAnimationBrush();
                if (!gameTitlesList[currentlySelectedGameIndex % 10].Text.EndsWith(" <"))
                    gameTitlesList[currentlySelectedGameIndex % 10].Text += " <";

                // Style the Back Button
                BackFromGameLibraryButton.IsChecked = false;
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
        }

        private void UpdateInputMenuFeedback()
        {
            SolidColorBrush activeFillColour = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00));
            SolidColorBrush activeBorderColour = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xBB, 0x00));

            SolidColorBrush inactiveFillColour = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
            SolidColorBrush inactiveBorderColour = new SolidColorBrush(Color.FromArgb(0xFF, 0xBB, 0x00, 0x00));

            int exitHeldMilliseconds = 1500;

            // Update the held countdown text
            bool exitHeld = false;
            int maxExitHeldFor = 0;
            foreach (ControllerState controllerState in controllerStates)
                if (controllerState.GetButtonState(0))
                {
                    exitHeld = true;
                    maxExitHeldFor = Math.Max(maxExitHeldFor, controllerState.GetExitButtonHeldFor());
                }


            if (exitHeld)
                InputMenu_HoldBackCountdownText.Text = ((double)(exitHeldMilliseconds - maxExitHeldFor) / 1000).ToString("0.0");
            else
                InputMenu_HoldBackCountdownText.Text = "";

            // Check if the exit button has been held for 1.5 seconds
            if (exitHeld && maxExitHeldFor >= exitHeldMilliseconds)
            {
                // Go back to the Start Menu
                ExitButton_Click(null, null);
            }

            // For each Controller State
            for (int i = 0; i < controllerStates.Count; i++)
            {
                // Joystick Input
                int[] leftStickDirection = controllerStates[i].GetLeftStickDirection();
                inputMenuJoysticks[i].Margin = new Thickness(leftStickDirection[0] * 50, leftStickDirection[1] * 50, 0, 0);

                // For each button in the Input Menu
                for (int j = 0; j < inputMenuButtons[i].Length; j++)
                {
                    if (inputMenuButtons[i][j] == null)
                        continue;

                    // If the user is pressing the button, highlight the button
                    if (controllerStates[i].GetButtonState(j))
                    {
                        inputMenuButtons[i][j].Fill = activeFillColour;
                        inputMenuButtons[i][j].Stroke = activeBorderColour;
                    }
                    else
                    {
                        inputMenuButtons[i][j].Fill = inactiveFillColour;
                        inputMenuButtons[i][j].Stroke = inactiveBorderColour;
                    }
                }
            }
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
                if (i + _pageIndex * 10 >= gameInfoFilesList.Length)
                    break;

                if (gameInfoFilesList[i + _pageIndex * 10] == null)
                {
                    // Set the text to "Loading..." and make it visible
                    gameTitlesList[i].Text = "Loading...";
                    gameTitlesList[i].Visibility = Visibility.Visible;
                }
                else
                {
                    // Set the text to the game title and make it visible
                    gameTitlesList[i].Text = gameInfoFilesList[i + _pageIndex * 10]["GameName"].ToString();
                    gameTitlesList[i].Visibility = Visibility.Visible;
                }
            }
        }

        private void UpdateGameInfoDisplay()
        {
            if (gameInfoFilesList == null)
                currentlySelectedGameIndex = -1;

            // Update the game info
            if (currentlySelectedGameIndex != -1 && gameInfoFilesList[currentlySelectedGameIndex] != null)
            {
                ResetGameInfoDisplay();

                StartButton.IsChecked = true;

                // Set the Game Thumbnail
                if (gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString().StartsWith("http"))
                {
                    NonGif_GameThumbnail.Source = new BitmapImage(new Uri(gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString(), UriKind.Absolute));
                    AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri(gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString(), UriKind.Absolute));
                }
                else
                {
                    if (File.Exists(Path.Combine(gameDirectoryPath, gameInfoFilesList[currentlySelectedGameIndex]["FolderName"].ToString(), gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString())))
                    {
                        NonGif_GameThumbnail.Source = new BitmapImage(new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[currentlySelectedGameIndex]["FolderName"].ToString(), gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString()), UriKind.Absolute));
                        AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[currentlySelectedGameIndex]["FolderName"].ToString(), gameInfoFilesList[currentlySelectedGameIndex]["GameThumbnail"].ToString()), UriKind.Absolute));
                    }
                }

                // Set the Game Info and Authors
                GameTitle.Text = emojiParser.ReplaceColonNames(gameInfoFilesList[currentlySelectedGameIndex]["GameName"].ToString());
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
                    GameTag[j].Text = emojiParser.ReplaceColonNames(tags[j]["Name"].ToString());

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
                GameDescription.Text = emojiParser.ReplaceColonNames(gameInfoFilesList[currentlySelectedGameIndex]["GameDescription"].ToString());
                VersionText.Text = "v" + gameInfoFilesList[currentlySelectedGameIndex]["GameVersion"].ToString();

                showingDebouncedGame = true;
                SetGameTitleState(currentlySelectedGameIndex, GameState.ready);
            }

            if (currentlySelectedGameIndex >= 0)
                StyleStartButtonState(currentlySelectedGameIndex);
        }

        public void StyleStartButtonState(int _index) => StyleStartButtonState(gameTitleStates[_index]);
        private void StyleStartButtonState(GameState _gameState)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Style the StartButton
                    switch (_gameState)
                    {
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
                gameTitlesList[i].Visibility = Visibility.Hidden;
        }

        private void ResetGameInfoDisplay()
        {
            // Reset the Thumbnail
            NonGif_GameThumbnail.Source = new BitmapImage(new Uri("Assets/Images/ThumbnailPlaceholder.png", UriKind.Relative));
            AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri("Assets/Images/ThumbnailPlaceholder.png", UriKind.Relative));

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

        // Custom Methods (Debounce, CloneXamlElement, EncodeOneDriveLink, PlayAudioFile)

        private void DebounceUpdateGameInfoDisplay()
        {
            if (currentlySelectedGameIndex >= 0 && currentlySelectedGameIndex < gameInfoFilesList.Length)
                StyleStartButtonState(GameState.loadingInfo);

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
                try { Application.Current?.Dispatcher?.Invoke(() => { UpdateGameInfoDisplay(); }); }
                catch (TaskCanceledException) { }
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
    
        private string EncodeOneDriveLink(string _link)
        {
            // Encode the OneDrive link
            string base64Value = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_link));
            string encodedUrl = "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');

            return "https://api.onedrive.com/v1.0/shares/" + encodedUrl + "/root/content";
        }

        // Audio File Methods

        private void DeleteAllAudioFiles()
        {
            // Check if the Audio folder exists
            if (!Directory.Exists(Path.Combine(RootPath, "Assets", "Audio")))
                return;

            // Delete all audio files
            string[] audioFiles = Directory.GetFiles(Path.Combine(RootPath, "Assets", "Audio"));

            foreach (string audioFile in audioFiles)
                File.Delete(audioFile);
        }

        public void DownloadAudioFiles()
        {
            try
            {
                // Get the audio files from the online DB
                WebClient webClient = new WebClient();
                JObject audioFilesJson = JObject.Parse(webClient.DownloadString(EncodeOneDriveLink(config["AudioFilesURL"].ToString())));

                // Check if audioFiles is unchanged, if so, return
                if (audioFiles != null)
                {
                    bool changed = false;

                    if (audioFiles.Count != ((JArray)audioFilesJson["AudioFiles"]).Count)
                        changed = true;

                    else
                        for (int i = 0; i < audioFiles.Count; i++)
                        {
                            if (audioFiles[i]["URL"].ToString() != ((JArray)audioFilesJson["AudioFiles"])[i]["URL"].ToString())
                            {
                                changed = true;
                                break;

                            }

                            if (audioFiles[i]["URL"].ToString() == "Spacer") continue;

                            if (audioFiles[i]["FileName"].ToString() != ((JArray)audioFilesJson["AudioFiles"])[i]["FileName"].ToString())
                            {
                                changed = true;
                                break;
                            }
                        }

                    if (periodicAudioFiles.Length != ((JArray)audioFilesJson["PeriodicAudio"]).Count)
                        changed = true;

                    else
                        for (int i = 0; i < periodicAudioFiles.Length; i++)
                        {
                            if (periodicAudioFiles[i] != int.Parse(((JArray)audioFilesJson["PeriodicAudio"])[i].ToString()))
                            {
                                changed = true;
                                break;
                            }
                        }

                    if (!changed) return;
                }

                Console.WriteLine("[Audio] Downloading audio files");

                audioFiles = (JArray)audioFilesJson["AudioFiles"];

                JArray periodicAudioFilesArray = (JArray)audioFilesJson["PeriodicAudio"];

                periodicAudioFiles = new int[periodicAudioFilesArray.Count];

                for (int i = 0; i < periodicAudioFilesArray.Count; i++)
                    periodicAudioFiles[i] = int.Parse(periodicAudioFilesArray[i].ToString());

                // Create the Audio folder if it doesn't exist
                if (!Directory.Exists(Path.Combine(RootPath, "Assets", "Audio")))
                    Directory.CreateDirectory(Path.Combine(RootPath, "Assets", "Audio"));

                audioFileNames = new string[audioFiles.Count];

                for (int i = 0; i < audioFiles.Count; i++)
                {
                    if (((JObject)audioFiles[i])["URL"].ToString() == "Spacer")
                    {
                        audioFileNames[i] = "";
                        continue;
                    }

                    string downloadURL = EncodeOneDriveLink(((JObject)audioFiles[i])["URL"].ToString());
                    string fileName = ((JObject)audioFiles[i])["FileName"].ToString();

                    // Try to download the audio file
                    try
                    {
                        webClient.DownloadFile(downloadURL, Path.Combine(RootPath, "Assets", "Audio", fileName + ".wav"));

                        // If the download is successful, set the audio file name
                        audioFileNames[i] = fileName;
                    }
                    catch (Exception)
                    {
                        // If the download fails, set the audio file name to an empty string
                        audioFileNames[i] = "Failed To Load Audio";
                    }
                }

                // Delete all audio files that are not in the online DB
                string[] localAudioFiles = Directory.GetFiles(Path.Combine(RootPath, "Assets", "Audio"));

                foreach (string localAudioFile in localAudioFiles)
                    if (Array.IndexOf(audioFileNames, Path.GetFileNameWithoutExtension(localAudioFile)) == -1)
                        File.Delete(localAudioFile);

                Console.WriteLine("[Audio] Finished downloading audio files");
            }
            catch (Exception)
            {
                audioFiles = new JArray();
                audioFileNames = new string[0];
                periodicAudioFiles = new int[0];
            }
        }

        public string[] GetAudioFileNames() => audioFileNames;

        public void PlayRandomPeriodicAudioFile()
        {
            // Pick a random number between 0 and the length of the periodic audio files array
            int randomIndex = new Random(DateTime.Now.Millisecond).Next(0, periodicAudioFiles.Length);

            // Play the periodic audio file at the random index
            PlayPeriodicAudioFile(randomIndex);
        }

        private void PlayPeriodicAudioFile(int _index)
        {
            // If the index is out of bounds, return
            if (_index < 0 || _index >= periodicAudioFiles.Length)
                return;

            // If the periodic audio file at the index is out of bounds, return
            if (periodicAudioFiles[_index] < 0 || periodicAudioFiles[_index] >= audioFileNames.Length)
                return;

            // Play the periodic audio file at the index
            PlayAudioFile(audioFileNames[periodicAudioFiles[_index]]);
        }

        public void PlayAudioFile(string _audioFile) => Task.Run(() => PlayAudioFileAsync(_audioFile));

        private async Task PlayAudioFileAsync(string _audioFile)
        {
            // Find the audio file
            _audioFile = Path.Combine(RootPath, "Assets", "Audio", _audioFile + ".wav");

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
