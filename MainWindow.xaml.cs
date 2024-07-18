using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using SharpDX.DirectInput;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using XamlAnimatedGif;

namespace ArcademiaGameLauncher
{
    enum LauncherState
    {
        ready,
        failed,
        downloadingGame,
        downloadingUpdate
    }

    public class ControllerState
    {
        private int index;
        private bool[] buttonStates;
        private int leftStickX;
        private int leftStickY;
        private int rightStickX;
        private int rightStickY;

        int joystickDeadzone = 7700;
        int joystickMidpoint = 32767;

        public Joystick joystick;
        public JoystickState state;

        public ControllerState(Joystick _joystick, int index)
        {
            this.index = index;
            joystick = _joystick;
            state = new JoystickState();

            buttonStates = new bool[128];
            state = joystick.GetCurrentState();
        }

        public void UpdateButtonStates()
        {
            joystick.Poll();
            state = joystick.GetCurrentState();

            leftStickX = state.X;
            leftStickY = state.Y;

            rightStickX = state.RotationX;
            rightStickY = state.RotationY;

            for (int i = 0; i < buttonStates.Length; i++)
            {
                SetButtonState(i, state.Buttons[i]);
            }
        }

        public int GetLeftStickX()
        {
            return leftStickX;
        }
        public int GetLeftStickY() {
            return leftStickY;
        }
        public int GetRightStickX()
        {
            return rightStickX;
        }
        public int GetRightStickY()
        {
            return rightStickY;
        }

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

        public int[] GetRightStickDirection()
        {
            int[] direction = new int[2];

            if (rightStickX > joystickMidpoint + joystickDeadzone)
            {
                direction[0] = 1;
            }
            else if (rightStickX < joystickMidpoint - joystickDeadzone)
            {
                direction[0] = -1;
            }
            else
            {
                direction[0] = 0;
            }

            if (rightStickY > joystickMidpoint + joystickDeadzone)
            {
                direction[1] = 1;
            }
            else if (rightStickY < joystickMidpoint - joystickDeadzone)
            {
                direction[1] = -1;
            }
            else
            {
                direction[1] = 0;
            }

            return direction;
        }

        public void SetButtonState(int _button, bool _state)
        {
            buttonStates[_button] = _state;
        }

        public bool GetButtonState(int _button)
        {
            return buttonStates[_button];
        }
    }

    public partial class MainWindow : Window
    {
        [DllImport("User32.dll")]
        public static extern int GetAsyncKeyState(Int32 i);

        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);



        private string rootPath;
        private string gameDirectoryPath;

        private string gameDatabaseURLPath;
        private string gameDatabaseURL;

        private string localGameDatabasePath;
        private JObject gameDatabaseFile;

        private string localGameInfoPath;
        private JObject gameInfoFile;

        int updateIndexOfGame;
        private System.Timers.Timer aTimer;

        int selectionUpdateInterval = 150;
        int selectionUpdateInternalCounter = 0;
        int selectionUpdateInternalCounterMax = 10;
        int selectionUpdateCounter = 0;
        int currentlySelectedGameIndex;
        int previousPageIndex = 0;

        int afkTimer = 0;
        int timeSinceStart = 0;
        bool afkTimerActive = false;

        private JObject[] gameInfoFilesList;

        private TextBlock[] gameTitlesList;

        private DirectInput directInput;
        private List<ControllerState> controllerStates = new List<ControllerState>();

        private Process currentlyRunningProcess;

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

        public MainWindow()
        {
            InitializeComponent();

            rootPath = Directory.GetCurrentDirectory();

            gameDatabaseURLPath = Path.Combine(rootPath, "GameDatabaseURL.json");
            gameDirectoryPath = Path.Combine(rootPath, "Games");

            localGameDatabasePath = Path.Combine(gameDirectoryPath, "GameDatabase.json");
            localGameInfoPath = "";

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(gameDirectoryPath))
                Directory.CreateDirectory(gameDirectoryPath);
        }

        private void CheckForUpdates()
        {
            if (gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] == null)
                gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"] = "";

            string folderName = gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"].ToString();

            if (folderName != "")
                localGameInfoPath = Path.Combine(gameDirectoryPath, folderName, "GameInfo.json");

            if (localGameInfoPath != "" && File.Exists(localGameInfoPath))
            {
                gameInfoFile = JObject.Parse(File.ReadAllText(localGameInfoPath));
                gameInfoFilesList[updateIndexOfGame] = gameInfoFile;
                if (updateIndexOfGame < 10)
                {
                    gameTitlesList[updateIndexOfGame].Text = gameInfoFile["GameName"].ToString();
                    gameTitlesList[updateIndexOfGame].Visibility = Visibility.Visible;
                }

                Version localVersion = new Version(gameInfoFile["GameVersion"].ToString());
                VersionText.Text = "v" + localVersion.ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    JObject onlineJson = JObject.Parse(webClient.DownloadString(gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString()));

                    Version onlineVersion = new Version(onlineJson["GameVersion"].ToString());

                    if (onlineVersion.IsDifferentVersion(localVersion))
                    {
                        InstallGameFiles(true, onlineJson, gameDatabaseFile["Games"][updateIndexOfGame]["LinkToGameInfo"].ToString());
                    }
                    else
                    {
                        State = LauncherState.ready;
                    }
                }
                catch (Exception ex)
                {
                    State = LauncherState.failed;
                    MessageBox.Show($"Failed to check for updates: {ex.Message}");
                }
            }
            else
            {
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
                    State = LauncherState.downloadingUpdate;
                }
                else
                {
                    State = LauncherState.downloadingGame;
                    _onlineJson = JObject.Parse(webClient.DownloadString(_downloadURL));
                }

                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri(_onlineJson["LinkToGameZip"].ToString()), Path.Combine(rootPath, _onlineJson["GameName"].ToString() + ".zip"), _onlineJson);
            }
            catch (Exception ex)
            {
                State = LauncherState.failed;
                MessageBox.Show($"Failed installing game files: {ex.Message}");
            }
        }

        private void DownloadGameCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                JObject onlineJson = (JObject)e.UserState;

                int currentUpdateIndexOfGame = -1;

                WebClient webClient = new WebClient();
                JArray games = (JArray)gameDatabaseFile["Games"];
                for (int i = 0; i < games.Count; i++)
                {
                    if (JObject.Parse(webClient.DownloadString(games[i]["LinkToGameInfo"].ToString()))["LinkToGameZip"].ToString() == onlineJson["LinkToGameZip"].ToString())
                    {
                        currentUpdateIndexOfGame = i;
                        break;
                    }
                }

                if (currentUpdateIndexOfGame == -1)
                {
                    MessageBox.Show("Failed to update game: Game not found in database.");
                    return;
                }

                string pathToZip = Path.Combine(rootPath, onlineJson["FolderName"].ToString() + ".zip");
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(pathToZip, Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString()), null);
                File.Delete(pathToZip);

                JObject gameDatabase = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                gameDatabase["Games"][currentUpdateIndexOfGame]["FolderName"] = onlineJson["FolderName"].ToString();
                File.WriteAllText(localGameDatabasePath, gameDatabase.ToString());

                gameDatabaseFile = gameDatabase;

                localGameInfoPath = Path.Combine(gameDirectoryPath, onlineJson["FolderName"].ToString(), "GameInfo.json");

                File.WriteAllText(localGameInfoPath, onlineJson.ToString());

                gameInfoFile = onlineJson;
                gameInfoFilesList[currentUpdateIndexOfGame] = onlineJson;
                if (currentUpdateIndexOfGame < 10)
                {
                    gameTitlesList[currentUpdateIndexOfGame].Text = onlineJson["GameName"].ToString();
                    gameTitlesList[currentUpdateIndexOfGame].Visibility = Visibility.Visible;
                }

                State = LauncherState.ready;
            }
            catch (Exception ex)
            {
                State = LauncherState.failed;
                MessageBox.Show($"Failed to complete download: {ex.Message}");
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            gameTitlesList = new TextBlock[10] { GameTitleText0, GameTitleText1, GameTitleText2, GameTitleText3, GameTitleText4, GameTitleText5, GameTitleText6, GameTitleText7, GameTitleText8, GameTitleText9 };

            bool foundGameDatabase = false;

            if (File.Exists(gameDatabaseURLPath))
            {
                gameDatabaseURL = JObject.Parse(File.ReadAllText(gameDatabaseURLPath))["URL"].ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    gameDatabaseFile = JObject.Parse(webClient.DownloadString(gameDatabaseURL));

                    foundGameDatabase = true;

                    if (!File.Exists(localGameDatabasePath))
                    {
                        File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());
                    }

                    // Save the FolderName property of each local game and write it to the new game database file
                    JObject localGameDatabaseFile = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                    JArray localGames = (JArray)localGameDatabaseFile["Games"];

                    gameInfoFilesList = new JObject[localGames.Count];

                    for (int i = 0; i < localGames.Count; i++)
                    {
                        // Check if the game is missing the FolderName property
                        if (localGames[i]["FolderName"] == null)
                        {
                            gameDatabaseFile["Games"][i]["FolderName"] = "";
                        }
                        else
                        {
                            gameDatabaseFile["Games"][i]["FolderName"] = localGames[i]["FolderName"];
                        }
                    }

                    File.WriteAllText(localGameDatabasePath, gameDatabaseFile.ToString());

                    JArray games = (JArray)gameDatabaseFile["Games"];

                    if (games.Count > 0)
                    {
                        for (int i = 0; i < games.Count; i++)
                        {
                            updateIndexOfGame = i;
                            CheckForUpdates();
                        }
                    }
                    else
                    {
                        MessageBox.Show("Failed to get game database: No games found.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to get game database: {ex.Message}");
                }
            }
            else MessageBox.Show("Failed to get game database URL: GameDatabaseURL.json does not exist.");
            
            if (!foundGameDatabase)
            {
                // Quit the application
                Application.Current.Shutdown();
            }


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

            currentlySelectedGameIndex = 0;

            UpdateGameInfoDisplay();

            // Timer Setup
            aTimer = new System.Timers.Timer();
            aTimer.Interval = 10;

            // Hook up the Elapsed event for the timer. 
            aTimer.Elapsed += OnTimedEvent;

            // Have the timer fire repeated events (true is the default)
            aTimer.AutoReset = true;

            // Start the timer
            aTimer.Enabled = true;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string currentGameFolder = gameDatabaseFile["Games"][currentlySelectedGameIndex]["FolderName"].ToString();

            JObject currentGameInfo = JObject.Parse(File.ReadAllText(Path.Combine(gameDirectoryPath, currentGameFolder, "GameInfo.json")));
            string currentGameExe = Path.Combine(gameDirectoryPath, currentGameFolder, currentGameInfo["NameOfExecutable"].ToString());

            if (File.Exists(currentGameExe) && State == LauncherState.ready)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(currentGameExe);
                startInfo.WorkingDirectory = currentGameFolder;
                if (currentlyRunningProcess == null || currentlyRunningProcess.HasExited)
                    currentlyRunningProcess = Process.Start(startInfo);
                else // Set focus to the currently running process
                    SetForegroundWindow(currentlyRunningProcess.MainWindowHandle);
            }
            else if (State == LauncherState.failed)
            {
                CheckForUpdates();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            aTimer.Stop();
            aTimer.Dispose();
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
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

                        // Show the Selection Menu
                        if (Application.Current != null && Application.Current.Dispatcher != null)
                        {
                            try
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    SelectionMenu.Visibility = Visibility.Visible;
                                    StartMenu.Visibility = Visibility.Collapsed;
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
            if (afkTimer >= /*18000*/0)
            {
                if (afkTimer >= /*18*/5000)
                {
                    // Reset the timer
                    afkTimerActive = false;
                    afkTimer = 0;
                    timeSinceStart = 0;

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
                                SelectionMenu.Visibility = Visibility.Collapsed;
                            });
                        }
                        catch (TaskCanceledException) { }
                    }

                }
                else
                {
                    // Warn the user

                }
            }
            else
            {
                // Hide the warning

            }

            // Update the Selection Menu
            if (timeSinceStart > 500 && Application.Current != null && Application.Current.Dispatcher != null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateCurrentSelection();
                    });
                }
                catch (TaskCanceledException) { }
            }

            // Check if the currently running process has exited, and set the focus back to the launcher
            if (currentlyRunningProcess != null && currentlyRunningProcess.HasExited)
            {
                SetForegroundWindow(Process.GetCurrentProcess().MainWindowHandle);
                currentlyRunningProcess = null;
            }

            if (SelectionMenu.Visibility == Visibility.Visible && timeSinceStart <= 500)
                timeSinceStart += 10;

            if (afkTimerActive)
                afkTimer += 10;

            if (selectionUpdateCounter > selectionUpdateInterval)
                selectionUpdateInternalCounter = 0;
            selectionUpdateCounter += 10;
        }

        private void UpdateCurrentSelection()
        {
            double multiplier = 1.00;

            if (selectionUpdateInternalCounter > 0)
                multiplier = (double)1.00 - ((double)selectionUpdateInternalCounter / ((double)selectionUpdateInternalCounterMax * 1.6));


            if (selectionUpdateCounter >= selectionUpdateInterval * multiplier)
            {
                int[] leftStickDirection = controllerStates[0].GetLeftStickDirection();
                int[] rightStickDirection = controllerStates[0].GetRightStickDirection();

                if (leftStickDirection[1] == -1 || rightStickDirection[1] == -1)
                {
                    currentlySelectedGameIndex -= 1;
                    if (currentlySelectedGameIndex < -1)
                        currentlySelectedGameIndex = -1;

                    selectionUpdateCounter = 0;
                    if (selectionUpdateInternalCounter < selectionUpdateInternalCounterMax)
                        selectionUpdateInternalCounter++;
                    UpdateGameInfoDisplay();
                }
                else if (leftStickDirection[1] == 1 || rightStickDirection[1] == 1)
                {
                    currentlySelectedGameIndex += 1;
                    if (currentlySelectedGameIndex > gameInfoFilesList.Length - 1)
                        currentlySelectedGameIndex = gameInfoFilesList.Length - 1;

                    selectionUpdateCounter = 0;
                    if (selectionUpdateInternalCounter < selectionUpdateInternalCounterMax)
                        selectionUpdateInternalCounter++;
                    UpdateGameInfoDisplay();
                }
            }

            // Check if the A button is pressed
            if (controllerStates[0].GetButtonState(0))
                if (currentlySelectedGameIndex >= 0) StartButton_Click(null, null);
                else CloseButton_Click(null, null);
        }

        private void ChangePage(int _pageIndex)
        {
            if (_pageIndex < 0)
                _pageIndex = 0;
            else if (_pageIndex > gameInfoFilesList.Length / 10)
                _pageIndex = gameInfoFilesList.Length / 10;

            previousPageIndex = _pageIndex;

            for (int i = 0; i < 10; i++)
            {
                gameTitlesList[i].Visibility = Visibility.Hidden;
            }

            for (int i = 0; i < 10; i++)
            {
                if (i + _pageIndex * 10 >= gameInfoFilesList.Length)
                    break;

                gameTitlesList[i].Text = gameInfoFilesList[i + _pageIndex * 10]["GameName"].ToString();
                gameTitlesList[i].Visibility = Visibility.Visible;
            }
        }

        private void ResetTitles()
        {
            for (int i = 0; i < 10; i++)
            {
                gameTitlesList[i].Visibility = Visibility.Hidden;
            }
        }

        private void ResetGameInfoDisplay()
        {
            NonGif_GameThumbnail.Source = new BitmapImage(new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));
            AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri("Images/ThumbnailPlaceholder.png", UriKind.Relative));

            GameTitle.Text = "Select A Game";
            GameAuthors.Text = "";
            GameDescription.Text = "Select a game using the joystick and by pressing A.";
            VersionText.Text = "";

            GameTagBorder0.Visibility = Visibility.Hidden;
            GameTagBorder1.Visibility = Visibility.Hidden;
            GameTagBorder2.Visibility = Visibility.Hidden;
            GameTagBorder3.Visibility = Visibility.Hidden;
            GameTagBorder4.Visibility = Visibility.Hidden;
            GameTagBorder5.Visibility = Visibility.Hidden;
            GameTagBorder6.Visibility = Visibility.Hidden;
            GameTagBorder7.Visibility = Visibility.Hidden;
            GameTagBorder8.Visibility = Visibility.Hidden;

            GameTag0.Text = "";
            GameTag1.Text = "";
            GameTag2.Text = "";
            GameTag3.Text = "";
            GameTag4.Text = "";
            GameTag5.Text = "";
            GameTag6.Text = "";
            GameTag7.Text = "";
            GameTag8.Text = "";
            
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

        private void UpdateGameInfoDisplay()
        {

            if (currentlySelectedGameIndex < 0)
            {
                ResetGameInfoDisplay();

                foreach (TextBlock title in gameTitlesList)
                    title.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));

                CloseButton.IsChecked = true;
                StartButton.IsChecked = false;
                StartButton.Content = "Select a Game";
                StartButton.IsEnabled = false;

                return;
            }
            CloseButton.IsChecked = false;
            StartButton.Content = "Start";

            int pageIndex = currentlySelectedGameIndex / 10;
            if (pageIndex != previousPageIndex)
            {
                ChangePage(pageIndex);
            }

            for (int i = pageIndex * 10; i < (pageIndex + 1) * 10; i++)
            {

                if (i == currentlySelectedGameIndex)
                {
                    gameTitlesList[i % 10].Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xBA, 0x3D, 0x71));

                    // Update the game info
                    if (gameInfoFilesList[i] != null)
                    {
                        ResetGameInfoDisplay();

                        StartButton.IsChecked = true;

                        NonGif_GameThumbnail.Source = new BitmapImage(new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[i]["FolderName"].ToString(), gameInfoFilesList[i]["GameThumbnail"].ToString()), UriKind.Absolute));
                        AnimationBehavior.SetSourceUri(Gif_GameThumbnail, new Uri(Path.Combine(gameDirectoryPath, gameInfoFilesList[i]["FolderName"].ToString(), gameInfoFilesList[i]["GameThumbnail"].ToString()), UriKind.Absolute));

                        GameTitle.Text = gameInfoFilesList[i]["GameName"].ToString();
                        GameAuthors.Text = string.Join(", ", gameInfoFilesList[i]["GameAuthors"].ToObject<string[]>());

                        Border[] GameTagBorder = new Border[9] { GameTagBorder0, GameTagBorder1, GameTagBorder2, GameTagBorder3, GameTagBorder4, GameTagBorder5, GameTagBorder6, GameTagBorder7, GameTagBorder8 };
                        TextBlock[] GameTag = new TextBlock[9] { GameTag0, GameTag1, GameTag2, GameTag3, GameTag4, GameTag5, GameTag6, GameTag7, GameTag8 };

                        JArray tags = (JArray)gameInfoFilesList[i]["GameTags"];

                        for (int j = 0; j < tags.Count; j++)
                        {
                            // Change Visibility
                            GameTagBorder[j].Visibility = Visibility.Visible;

                            // Change Text Content
                            GameTag[j].Text = tags[j]["Name"].ToString();

                            // Change Border and Text Colour
                            string colour = "#FF777777";

                            if (tags[j]["Colour"] != null && tags[j]["Colour"].ToString() != "")
                            {
                                colour = tags[j]["Colour"].ToString();
                            }

                            GameTag[j].Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                            GameTagBorder[j].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
                        }

                        GameDescription.Text = gameInfoFilesList[i]["GameDescription"].ToString();

                        VersionText.Text = "v" + gameInfoFilesList[i]["GameVersion"].ToString();
                    }
                }
                else
                {
                    gameTitlesList[i % 10].Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
                }
            }
        }
    }

    struct Version
    {
        internal static Version zero = new Version(0, 0, 0);

        public int major;
        public int minor;
        public int subMinor;

        internal Version(short _major, short _minor, short _subMinor)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
        }

        internal Version(string version)
        {
            string[] parts = version.Split('.');
            
            if (parts.Length != 3)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                return;
            }

            major = int.Parse(parts[0]);
            minor = int.Parse(parts[1]);
            subMinor = int.Parse(parts[2]);
        }

        internal bool IsDifferentVersion(Version _otherVersion)
        {
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
            return $"{major}.{minor}.{subMinor}";
        }
    }
}
