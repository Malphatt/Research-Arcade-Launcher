using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;

namespace TutorialWPFApp
{
    enum LauncherState
    {
        ready,
        failed,
        downloadingGame,
        downloadingUpdate
    }

    public partial class MainWindow : Window
    {
        private string rootPath;
        private string gameDirectoryPath;

        private string gameDatabaseURLPath;
        private string gameDatabaseURL;

        private string localGameDatabasePath;
        private JObject gameDatabaseFile;

        private string localGameInfoPath = "";
        private JObject gameInfoFile;

        int updateIndexOfGame;

        int currentlySelectedGameIndex;

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

            // Create the games directory if it doesn't exist
            if (!Directory.Exists(gameDirectoryPath))
            {
                Directory.CreateDirectory(gameDirectoryPath);
            }
        }

        private void CheckForUpdates()
        {
            string folderName = gameDatabaseFile["Games"][updateIndexOfGame]["FolderName"].ToString();

            if (folderName != "")
            {
                localGameInfoPath = Path.Combine(gameDirectoryPath, folderName, "GameInfo.json");
            }

            if (localGameInfoPath != "" && File.Exists(localGameInfoPath))
            {
                gameInfoFile = JObject.Parse(File.ReadAllText(localGameInfoPath));

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
                        GameTitle.Text = onlineJson["GameName"].ToString();
                        GameAuthors.Text = string.Join(", ", onlineJson["GameAuthors"].ToObject<string[]>());

                        Border[] GameTagBorder = new Border[9] { GameTagBorder0, GameTagBorder1, GameTagBorder2, GameTagBorder3, GameTagBorder4, GameTagBorder5, GameTagBorder6, GameTagBorder7, GameTagBorder8 };
                        TextBlock[] GameTag = new TextBlock[9] { GameTag0, GameTag1, GameTag2, GameTag3, GameTag4, GameTag5, GameTag6, GameTag7, GameTag8 };

                        string[] tags = onlineJson["GameTags"].ToObject<string[]>();
                        for (int i = 0; i < tags.Length; i++)
                        {
                            GameTagBorder[i].Visibility = Visibility.Visible;
                            GameTag[i].Text = tags[i];
                        }

                        GameDescription.Text = onlineJson["GameDescription"].ToString();

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

                string pathToZip = Path.Combine(rootPath, onlineJson["GameName"].ToString() + ".zip");
                FastZip fastZip = new FastZip();
                fastZip.ExtractZip(pathToZip, Path.Combine(gameDirectoryPath, onlineJson["GameName"].ToString()), null);
                File.Delete(pathToZip);

                JObject gameDatabase = JObject.Parse(File.ReadAllText(localGameDatabasePath));
                gameDatabase["Games"][updateIndexOfGame]["FolderName"] = onlineJson["GameName"].ToString();
                File.WriteAllText(localGameDatabasePath, gameDatabase.ToString());

                gameDatabaseFile = gameDatabase;

                localGameInfoPath = Path.Combine(gameDirectoryPath, onlineJson["GameName"].ToString(), "GameInfo.json");

                File.WriteAllText(localGameInfoPath, onlineJson.ToString());

                gameInfoFile = onlineJson;

                GameTitle.Text = onlineJson["GameName"].ToString();
                GameAuthors.Text = string.Join(", ", onlineJson["GameAuthors"].ToObject<string[]>());

                Border[] GameTagBorder = new Border[9] { GameTagBorder0, GameTagBorder1, GameTagBorder2, GameTagBorder3, GameTagBorder4, GameTagBorder5, GameTagBorder6, GameTagBorder7, GameTagBorder8 };
                TextBlock[] GameTag = new TextBlock[9] { GameTag0, GameTag1, GameTag2, GameTag3, GameTag4, GameTag5, GameTag6, GameTag7, GameTag8 };

                string[] tags = onlineJson["GameTags"].ToObject<string[]>();
                for (int i = 0; i < tags.Length; i++)
                {
                    GameTagBorder[i].Visibility = Visibility.Visible;
                    GameTag[i].Text = tags[i];
                }

                GameDescription.Text = onlineJson["GameDescription"].ToString();

                VersionText.Text = "v" + onlineJson["GameVersion"].ToString();
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
            bool foundGameDatabase = false;

            if (File.Exists(gameDatabaseURLPath))
            {
                gameDatabaseURL = JObject.Parse(File.ReadAllText(gameDatabaseURLPath))["URL"].ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    gameDatabaseFile = JObject.Parse(webClient.DownloadString(gameDatabaseURL));

                    foundGameDatabase = true;

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
                Process.Start(startInfo);
            }
            else if (State == LauncherState.failed)
            {
                CheckForUpdates();
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
