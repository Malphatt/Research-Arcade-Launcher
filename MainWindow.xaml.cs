using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
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

        private JObject gameDatabaseFile;
        // this should contain the following:
        // game info links
        // local game folder names

        private string localJson;
        private JObject gameInfoFile;
        // this should contain the following:
        // game name
        // author(s)
        // tag(s)
        // game description
        // name of executable
        // game zip link
        // version

        private string gameZip;
        private string gameExe;

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

            localJson = System.IO.Path.Combine(rootPath, "GameInfo.json");
            gameZip = System.IO.Path.Combine(rootPath, "game.zip");
            gameExe = System.IO.Path.Combine(rootPath, "Game", "Game.exe");
        }

        private void CheckForUpdates()
        {
            if (File.Exists(localJson))
            {
                gameInfoFile = JObject.Parse(File.ReadAllText(localJson));

                Version localVersion = new Version(gameInfoFile["GameVersion"].ToString());
                VersionText.Text = "v" + localVersion.ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    JObject onlineJson = JObject.Parse(webClient.DownloadString("https://3iywda.am.files.1drv.com/y4mP7LMMcjZVxDvCtppkkUuNWZQTTJjjnIB4v4uqFQffiRHFSdg5x6HwCd4O2Bxrpj-9Mhiy6bi2bKtnxDl4gMcCJNFVOruvZmsJJhmAVIYpl6W0d8UThBR82E3xX_9GfDa4mJpB26KskGJaM3Ig_MBuO3egoq9EU0gtj_dezg0yhW4TyjT1kgtT3HAmzPdD2PuC6QwS4FhycAFKxvrjrxMpQ"));

                    Version onlineVersion = new Version(onlineJson["GameVersion"].ToString());

                    if (onlineVersion.IsDifferentVersion(localVersion))
                    {
                        InstallGameFiles(true, onlineJson);
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
                InstallGameFiles(false, JObject.Parse("{\r\n\"GameVersion\": \"0.0.0\"\r\n}\r\n"));
            }
        }

        private void InstallGameFiles(bool _isUpdate, JObject _onlineJson)
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
                    _onlineJson = JObject.Parse(webClient.DownloadString("https://3iywda.am.files.1drv.com/y4mP7LMMcjZVxDvCtppkkUuNWZQTTJjjnIB4v4uqFQffiRHFSdg5x6HwCd4O2Bxrpj-9Mhiy6bi2bKtnxDl4gMcCJNFVOruvZmsJJhmAVIYpl6W0d8UThBR82E3xX_9GfDa4mJpB26KskGJaM3Ig_MBuO3egoq9EU0gtj_dezg0yhW4TyjT1kgtT3HAmzPdD2PuC6QwS4FhycAFKxvrjrxMpQ"));
                }

                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri(_onlineJson["LinkToGameZip"].ToString()), gameZip, _onlineJson);
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
                ZipFile.ExtractToDirectory(gameZip, System.IO.Path.Combine(rootPath, onlineJson["GameName"].ToString()));
                File.Delete(gameZip);

                File.WriteAllText(localJson, onlineJson.ToString());

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
            CheckForUpdates();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(gameExe) && State == LauncherState.ready)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(gameExe);
                startInfo.WorkingDirectory = System.IO.Path.Combine(rootPath, "Game");
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
