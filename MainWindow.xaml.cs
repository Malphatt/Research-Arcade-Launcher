using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows;

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
        private string versionFile;
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

            // print the current directory
            MessageBox.Show(rootPath);

            versionFile = System.IO.Path.Combine(rootPath, "Version.txt");
            gameZip = System.IO.Path.Combine(rootPath, "game.zip");
            gameExe = System.IO.Path.Combine(rootPath, "Game", "Game.exe");
        }

        private void CheckForUpdates()
        {
            if (File.Exists(versionFile))
            {
                Version localVersion = new Version(File.ReadAllText(versionFile));
                VersionText.Text = localVersion.ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    Version onlineVersion = new Version(webClient.DownloadString("https://0n9dag.am.files.1drv.com/y4mBrLQI3x4d63TAUjTRXNSExnIGSZb5IymzBVcy19NAM4KnXYMh3qGaNUGmdwfNVIHM2v9V8-BpR03fDPMQaGFiaaCv3e0xxHQNrUW3DVkvbLStN00smpFbjsYtfQdTr48DfMkldb82sZA1DgCza2eJFDN-zm7L_PuNlUUTxWHGnTp8O32TSAzHUkTIiCoSGlLyfDfl4DKUPmzMAGZlJCcXQ"));

                    if (onlineVersion.IsDifferentVersion(localVersion))
                    {
                        InstallGameFiles(true, onlineVersion);
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
                InstallGameFiles(false, Version.zero);
            }
        }

        private void InstallGameFiles(bool _isUpdate, Version _onlineVersion)
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
                    _onlineVersion = new Version(webClient.DownloadString("https://0n9dag.am.files.1drv.com/y4mBrLQI3x4d63TAUjTRXNSExnIGSZb5IymzBVcy19NAM4KnXYMh3qGaNUGmdwfNVIHM2v9V8-BpR03fDPMQaGFiaaCv3e0xxHQNrUW3DVkvbLStN00smpFbjsYtfQdTr48DfMkldb82sZA1DgCza2eJFDN-zm7L_PuNlUUTxWHGnTp8O32TSAzHUkTIiCoSGlLyfDfl4DKUPmzMAGZlJCcXQ"));
                }

                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri("https://0npo9q.am.files.1drv.com/y4meBbHRkvORxovZ7cNP40jZGawTVyNv3aHWsPRnr-iboavooDmyW4SGcinXxaCZIsYfKWgmsr9cmYlLp25VnCVkMBQQryaOg1bLDSHDvaZw304xiLDqa3NsXlwyQqDBRRxPOa0SFyvhvCgYW85yyFoJ2b6gcVYRHz46YzERxEK9qcnvt8MlIQwfQGCVJ7kY_4IkRYGkdI754O-vSYWKkOMUw"), gameZip, _onlineVersion);
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
                string onlineVersion = ((Version)e.UserState).ToString();
                ZipFile.ExtractToDirectory(gameZip, System.IO.Path.Combine(rootPath, "Game"));
                File.Delete(gameZip);

                File.WriteAllText(versionFile, onlineVersion);

                VersionText.Text = onlineVersion;
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
