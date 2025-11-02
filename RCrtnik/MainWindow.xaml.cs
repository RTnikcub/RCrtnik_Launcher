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
            return major != _otherVersion.major ||
                   minor != _otherVersion.minor ||
                   subMinor != _otherVersion.subMinor;
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
        private string gameDirectory;
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
                            PlayButton.Content = "Error - Click to Retry";
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
                            PlayButton.Content = "Checking For Updates";
                            PlayButton.IsEnabled = false;
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
            gameDirectory = Path.Combine(rootPath, "TR");
            gameExe = Path.Combine(gameDirectory, "TEST.exe");
            cancellationTokenSource = new CancellationTokenSource();

            // Создаем необходимые директории
            if (!Directory.Exists(gameDirectory))
            {
                Directory.CreateDirectory(gameDirectory);
            }
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                // Имитируем задержку сети для реалистичности
                await Task.Delay(1000);

                if (File.Exists(versionFile))
                {
                    GameVersion localVersion = new GameVersion(File.ReadAllText(versionFile));

                    Dispatcher.Invoke(() => VersionText.Text = localVersion.ToString());

                    // ВРЕМЕННО: Имитируем проверку обновлений без реального сервера
                    GameVersion onlineVersion = new GameVersion("1.0.1"); // Предполагаем, что есть новая версия

                    if (onlineVersion.IsDifferentThan(localVersion))
                    {
                        // Показываем сообщение о доступном обновлении
                        var result = await ShowQuestionMessageAsync($"Available update: {onlineVersion}\nCurrent version: {localVersion}\n\nDo you want to download the update?");

                        if (result == MessageBoxResult.Yes)
                        {
                            await InstallGameFilesAsync(true, onlineVersion);
                        }
                        else
                        {
                            Status = LauncherStatus.ready;
                        }
                    }
                    else
                    {
                        Status = LauncherStatus.ready;
                        await ShowInfoMessageAsync("Game is up to date!");
                    }
                }
                else
                {
                    // Первый запуск - создаем базовую версию
                    File.WriteAllText(versionFile, "1.0.0");
                    Dispatcher.Invoke(() => VersionText.Text = "1.0.0");

                    // Создаем тестовый исполняемый файл если его нет
                    await CreateTestGameFilesAsync();

                    Status = LauncherStatus.ready;
                    await ShowInfoMessageAsync("Game ready for first launch!");
                }
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                Exx = ex.ToString();
                await ShowErrorMessageAsync($"Update check failed: {ex.Message}");
            }
        }

        private async Task InstallGameFilesAsync(bool isUpdate, GameVersion onlineVersion)
        {
            try
            {
                if (isUpdate)
                {
                    Status = LauncherStatus.downloadingUpdate;
                }
                else
                {
                    Status = LauncherStatus.downloadingGame;
                }

                // Имитируем процесс загрузки с прогрессом
                for (int progress = 0; progress <= 100; progress += 10)
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    await Task.Delay(200); // Имитируем задержку загрузки

                    Dispatcher.Invoke(() =>
                    {
                        PlayButton.Content = $"Downloading... {progress}%";
                    });
                }

                // Имитируем создание игровых файлов
                await CreateTestGameFilesAsync();

                // Обновляем версию
                File.WriteAllText(versionFile, onlineVersion.ToString());

                Dispatcher.Invoke(() =>
                {
                    VersionText.Text = onlineVersion.ToString();
                    Status = LauncherStatus.ready;
                });

                await ShowInfoMessageAsync($"Game successfully {(isUpdate ? "updated" : "installed")} to version {onlineVersion}!");
            }
            catch (OperationCanceledException)
            {
                Status = LauncherStatus.ready;
                await ShowInfoMessageAsync("Download cancelled.");
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                Exx = ex.ToString();
                await ShowErrorMessageAsync($"Installation failed: {ex.Message}");
            }
        }

        private async Task CreateTestGameFilesAsync()
        {
            await Task.Run(() =>
            {
                // Создаем тестовую директорию игры если её нет
                if (!Directory.Exists(gameDirectory))
                {
                    Directory.CreateDirectory(gameDirectory);
                }

                // Создаем простой тестовый исполняемый файл (в реальности это будет ваша игра)
                string testBatContent = @"
@echo off
echo ===============================
echo     RC Game - Test Version
echo ===============================
echo.
echo This is a test game executable.
echo If this were the real game, it
echo would be running now!
echo.
echo Launcher is working correctly!
echo.
echo Press any key to exit...
pause >nul
";

                File.WriteAllText(Path.Combine(gameDirectory, "TEST.bat"), testBatContent);

                // Также создаем "exe" файл для совместимости с вашим кодом
                // В реальном сценарии здесь будет ваша настоящая игра
                File.WriteAllText(gameExe, "Test game executable - replace with actual game");
            });
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private async Task ShowInfoMessageAsync(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private async Task<MessageBoxResult> ShowQuestionMessageAsync(string message)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                return MessageBox.Show(message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
            });
        }

        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            await CheckForUpdateAsync();
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Path.Combine(gameDirectory, "TEST.bat")) && Status == LauncherStatus.ready)
            {
                try
                {
                    // Запускаем тестовый bat файл
                    ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(gameDirectory, "TEST.bat"));
                    startInfo.WorkingDirectory = gameDirectory;
                    startInfo.UseShellExecute = true;
                    Process.Start(startInfo);

                    // Не закрываем лаунчер сразу, чтобы пользователь видел что игра запустилась
                    // Close();
                }
                catch (Exception ex)
                {
                    await ShowErrorMessageAsync($"Failed to start game: {ex.Message}\n\nMake sure the game files are properly installed.");
                }
            }
            else if (Status == LauncherStatus.failed)
            {
                // При ошибке - повторная проверка обновлений
                await CheckForUpdateAsync();
            }
            else if (Status == LauncherStatus.defalt)
            {
                // Если еще проверяет обновления - ничего не делаем
            }
            else
            {
                await ShowErrorMessageAsync("Game files not found. Please check for updates or reinstall.");
            }
        }

        private async void Button_02_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://github.com"; // Используем существующий URL для теста

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
                await ShowErrorMessageAsync($"Failed to open help page: {ex.Message}\n\nYou can manually visit: {url}");
            }
        }

        private async void Button_03_Click(object sender, RoutedEventArgs e)
        {
            Button_03.IsEnabled = false;
            Button_03.Content = "Checking...";

            try
            {
                await CheckForUpdateAsync();
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
            timer.Interval = TimeSpan.FromSeconds(1.5);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                Button_03.Content = "Check for Updates";
                Button_03.IsEnabled = true;
            };
            timer.Start();
        }

        // Добавляем метод для ручного создания игровых файлов (для тестирования)
        private async void CreateTestFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await CreateTestGameFilesAsync();
                await ShowInfoMessageAsync("Test game files created successfully!\n\nYou can now try to launch the game.");
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Failed to create test files: {ex.Message}");
            }
        }
    }
}