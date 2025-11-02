using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace RCrtnik
{
    enum LauncherStatus
    {
        ready,
        failed,
        downloadingGame,
        downloadingUpdate,
        defalt
    }

    // Переименованная структура версии
    struct GameVersion
    {
        internal static GameVersion zero = new GameVersion(0, 0, 0);

        private short major;
        private short minor;
        private short subMinor;

        internal GameVersion(short _major, short _minor, short _subMinor)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
        }

        internal GameVersion(string _version)
        {
            string[] _versionStrings = _version.Split('.');
            if (_versionStrings.Length != 3)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                return;
            }

            major = short.Parse(_versionStrings[0]);
            minor = short.Parse(_versionStrings[1]);
            subMinor = short.Parse(_versionStrings[2]);
        }

        internal bool IsDifferentThan(GameVersion _otherVersion)
        {
            if (major != _otherVersion.major)
            {
                return true;
            }
            else
            {
                if (minor != _otherVersion.minor)
                {
                    return true;
                }
                else
                {
                    if (subMinor != _otherVersion.subMinor)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override string ToString()
        {
            return $"{major}.{minor}.{subMinor}";
        }
    }

    public partial class MainWindow : Window
    {
        private string rootPath;
        private string versionFile;
        private string gameZip;
        private string gameExe;
        private string Exx;
        private DispatcherTimer timer;
        private CancellationTokenSource cancellationTokenSource;

        private LauncherStatus _status;
        internal LauncherStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                Dispatcher.Invoke(() =>
                {
                    switch (_status)
                    {
                        case LauncherStatus.ready:
                            PlayButton.Content = "Play";
                            PlayButton.IsEnabled = true;
                            break;
                        case LauncherStatus.failed:
                            PlayButton.Content = "Error";
                            PlayButton.IsEnabled = true;
                            break;
                        case LauncherStatus.downloadingGame:
                            PlayButton.Content = "Downloading Game...";
                            PlayButton.IsEnabled = false;
                            break;
                        case LauncherStatus.downloadingUpdate:
                            PlayButton.Content = "Downloading Update...";
                            PlayButton.IsEnabled = false;
                            break;
                        case LauncherStatus.defalt:
                            PlayButton.Content = "PRO";
                            PlayButton.IsEnabled = true;
                            break;
                    }
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Status = LauncherStatus.defalt;
            rootPath = Directory.GetCurrentDirectory();
            versionFile = Path.Combine(rootPath, "Version.txt");
            gameZip = Path.Combine(rootPath, "Tukhtai-006.zip");
            gameExe = Path.Combine(rootPath, "TR", "TEST.exe");
            cancellationTokenSource = new CancellationTokenSource();
        }

        // Асинхронная проверка обновлений
        private async Task CheckForUpdateAsync()
        {
            try
            {
                if (File.Exists(versionFile))
                {
                    GameVersion localVersion = new GameVersion(File.ReadAllText(versionFile));

                    Dispatcher.Invoke(() => VersionText.Text = localVersion.ToString());

                    using (WebClient webClient = new WebClient())
                    {
                        string onlineVersionString = await webClient.DownloadStringTaskAsync("Addres");
                        GameVersion onlineVersion = new GameVersion(onlineVersionString);

                        if (onlineVersion.IsDifferentThan(localVersion))
                        {
                            await InstallGameFilesAsync(true, onlineVersion);
                        }
                        else
                        {
                            Status = LauncherStatus.ready;
                        }
                    }
                }
                else
                {
                    await InstallGameFilesAsync(false, GameVersion.zero);
                }
            }
            catch (OperationCanceledException)
            {
                Status = LauncherStatus.ready;
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                Exx = ex.ToString();
                await ShowErrorMessageAsync($"Update check failed: {ex.Message}");
            }
        }

        // Асинхронная установка файлов игры
        private async Task InstallGameFilesAsync(bool isUpdate, GameVersion onlineVersion)
        {
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    if (isUpdate)
                    {
                        Status = LauncherStatus.downloadingUpdate;
                    }
                    else
                    {
                        Status = LauncherStatus.downloadingGame;
                        string onlineVersionString = await webClient.DownloadStringTaskAsync("Addres");
                        onlineVersion = new GameVersion(onlineVersionString);
                    }

                    // Событие прогресса загрузки
                    webClient.DownloadProgressChanged += (s, e) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PlayButton.Content = $"Downloading... {e.ProgressPercentage}%";
                        });
                    };

                    await webClient.DownloadFileTaskAsync(new Uri("AddresZip"), gameZip);
                    await ExtractGameFilesAsync(onlineVersion);
                }
            }
            catch (OperationCanceledException)
            {
                Status = LauncherStatus.ready;
                if (File.Exists(gameZip))
                    File.Delete(gameZip);
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                Exx = ex.ToString();
                await ShowErrorMessageAsync($"Installation failed: {ex.Message}");
            }
        }

        // Асинхронная распаковка файлов
        private async Task ExtractGameFilesAsync(GameVersion onlineVersion)
        {
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(gameZip, rootPath, true);
                File.Delete(gameZip);
                File.WriteAllText(versionFile, onlineVersion.ToString());
            });

            Dispatcher.Invoke(() =>
            {
                VersionText.Text = onlineVersion.ToString();
                Status = LauncherStatus.ready;
            });
        }

        // Асинхронное сообщение об ошибке
        private async Task ShowErrorMessageAsync(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            await CheckForUpdateAsync();
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(gameExe) && Status == LauncherStatus.ready)
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(gameExe);
                    startInfo.WorkingDirectory = Path.Combine(rootPath, "TR");
                    Process.Start(startInfo);
                    Close();
                }
                catch (Exception ex)
                {
                    await ShowErrorMessageAsync($"Failed to start game: {ex.Message}");
                }
            }
            else if (Status == LauncherStatus.failed)
            {
                await CheckForUpdateAsync();
            }
        }

        private async void Button_02_Click(object sender, RoutedEventArgs e)
        {
            string url = "C:/Site/index.html";

            try
            {
                await Task.Run(() =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                });
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                await ShowErrorMessageAsync($"Failed to open help: {ex.Message}");
            }
        }

        private async void Button_03_Click(object sender, RoutedEventArgs e)
        {
            Button_03.IsEnabled = false;
            Button_03.Content = "Checking...";

            try
            {
                await CheckForUpdateAsync();

                if (Status == LauncherStatus.ready)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("No updates available");
                    });
                }
            }
            finally
            {
                StartTimer();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            base.OnClosing(e);
        }

        private void StartTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.5);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                Button_03.Content = "Check for Updates";
                Button_03.IsEnabled = true;
            };
            timer.Start();
        }
    }
}