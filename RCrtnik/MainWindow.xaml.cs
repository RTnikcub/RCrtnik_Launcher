using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net.Http;

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
        internal static GameVersion zero = new GameVersion(0, 0, 0, 0);

        private short major;
        private short minor;
        private short subMinor;
        private short dinor;

        internal GameVersion(short _major, short _minor, short _subMinor, short _dinor)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
            dinor = _dinor;
        }

        internal GameVersion(string _version)
        {
            string[] _versionStrings = _version.Split('.');
            if (_versionStrings.Length != 4)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                dinor = 0;
                return;
            }

            major = short.Parse(_versionStrings[0]);
            minor = short.Parse(_versionStrings[1]);
            subMinor = short.Parse(_versionStrings[2]);
            dinor = short.Parse(_versionStrings[3]);
        }

        internal bool IsDifferentThan(GameVersion _otherVersion)
        {
            return major != _otherVersion.major ||
                   minor != _otherVersion.minor ||
                   subMinor != _otherVersion.subMinor ||
                   dinor != _otherVersion.dinor;
        }

        public override string ToString()
        {
            return $"{major}.{minor}.{subMinor}.{dinor}";
        }
    }

    public partial class MainWindow : Window
    {
        private string rootPath;
        private string versionFile;
        private string gameZip;
        private string gameExe;
        private string gameDirectory;
        private DispatcherTimer timer;
        private DispatcherTimer imageSlideTimer;
        private CancellationTokenSource cancellationTokenSource;

        // Галерея изображений
        private List<string> galleryImagePaths;
        private int currentImageIndex = 0;

        // Системная информация
        private string pcIdentifier;
        private Random cpuRandom = new Random();

        private LauncherStatus _status;
        internal LauncherStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                Dispatcher.Invoke(() =>
                {
                    PlayButton.Content = _status switch
                    {
                        LauncherStatus.ready => "Play",
                        LauncherStatus.failed => "Error - Click to Retry",
                        LauncherStatus.downloadingGame => "Downloading Game...",
                        LauncherStatus.downloadingUpdate => "Downloading Update...",
                        _ => "Checking For Updates"
                    };
                    PlayButton.IsEnabled = _status == LauncherStatus.ready || _status == LauncherStatus.failed;
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            rootPath = Directory.GetCurrentDirectory();
            versionFile = Path.Combine(rootPath, "Version.txt");
            gameZip = Path.Combine(rootPath, "Tukhtai-006.zip");
            gameDirectory = Path.Combine(rootPath, "TR");
            gameExe = Path.Combine(gameDirectory, "TEST.exe");
            cancellationTokenSource = new CancellationTokenSource();

            // Создаем необходимые директории
            if (!Directory.Exists(gameDirectory))
                Directory.CreateDirectory(gameDirectory);

            // Запускаем инициализацию
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Инициализируем системную информацию
                InitializeSystemInfo();

                // Инициализируем галерею
                InitializeImageGallery();

                // Проверяем обновления
                await CheckForUpdateAsync();

                // Скрываем окно загрузки
                await Task.Delay(1500);
                HideLoadingWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization failed: {ex}");
                HideLoadingWindow();
            }
        }

        private void HideLoadingWindow()
        {
            Dispatcher.Invoke(() => LoadingWindow.Visibility = Visibility.Collapsed);
        }

        private void InitializeSystemInfo()
        {
            // Получаем ID ПК
            pcIdentifier = GetPCIdentifier();
            PcIdText.Text = pcIdentifier;

            // Обновляем загрузку ЦП
            UpdateCPUUsage();

            // Проверяем интернет
            UpdateInternetStatus();
        }

        private async void RefreshInternetBtn_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;

            await UpdateInternetStatusAsync();

            button.IsEnabled = true;
        }

        private async Task UpdateInternetStatusAsync()
        {
            InternetStatusText.Text = "CHECKING...";
            InternetStatusText.Foreground = Brushes.Yellow;

            bool hasInternet = await Task.Run(() => CheckInternetConnection());

            InternetStatusText.Text = hasInternet ? "ONLINE" : "OFFLINE";
            InternetStatusText.Foreground = hasInternet ? Brushes.LightGreen : Brushes.Red;
        }

        private void UpdateInternetStatus()
        {
            _ = UpdateInternetStatusAsync();
        }

        private bool CheckInternetConnection()
        {
            try
            {
                // Способ 1: Пинг Google DNS
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 2000);
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        Debug.WriteLine("Internet check: Ping success");
                        return true;
                    }
                }
            }
            catch (Exception pingEx)
            {
                Debug.WriteLine($"Ping failed: {pingEx.Message}");
            }

            try
            {
                // Способ 2: HTTP запрос к Google с таймаутом через WebRequest
                var request = (HttpWebRequest)WebRequest.Create("http://www.google.com");
                request.UserAgent = "Mozilla/5.0";
                request.Timeout = 3000;
                request.Method = "HEAD"; // Используем HEAD для быстрой проверки

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Debug.WriteLine("Internet check: HTTP success");
                    return true;
                }
            }
            catch (WebException webEx)
            {
                // Если получили любой HTTP ответ - значит интернет есть
                if (webEx.Response != null)
                {
                    Debug.WriteLine("Internet check: HTTP response received");
                    return true;
                }
                Debug.WriteLine($"HTTP check failed: {webEx.Message}");
            }
            catch (Exception httpEx)
            {
                Debug.WriteLine($"HTTP check failed: {httpEx.Message}");
            }

            try
            {
                // Способ 3: Проверка через generate_204
                var request = (HttpWebRequest)WebRequest.Create("http://clients3.google.com/generate_204");
                request.UserAgent = "Mozilla/5.0";
                request.Timeout = 3000;
                request.Method = "HEAD";

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Debug.WriteLine("Internet check: Google generate_204 success");
                    return true;
                }
            }
            catch (WebException webEx)
            {
                // generate_204 возвращает 204 статус, который считается успехом
                if (webEx.Response != null)
                {
                    Debug.WriteLine("Internet check: Google 204 response received");
                    return true;
                }
                Debug.WriteLine($"Google check failed: {webEx.Message}");
            }
            catch (Exception googleEx)
            {
                Debug.WriteLine($"Google check failed: {googleEx.Message}");
            }

            Debug.WriteLine("Internet check: All methods failed - OFFLINE");
            return false;
        }

        private string GetPCIdentifier()
        {
            try
            {
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;

                using SHA256 sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{machineName}_{userName}"));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToUpper();
            }
            catch
            {
                return "UnknownPC";
            }
        }

        private void UpdateCPUUsage()
        {
            // Имитация загрузки ЦП
            float cpuUsage = cpuRandom.Next(5, 40);

            CpuUsageText.Text = $"{cpuUsage:0}%";
            CpuUsageText.Foreground = cpuUsage switch
            {
                < 30 => Brushes.LightGreen,
                < 60 => Brushes.Orange,
                _ => Brushes.Red
            };
        }

        private void InitializeImageGallery()
        {
            try
            {
                galleryImagePaths = new List<string>();
                string galleryPath = Path.Combine(rootPath, "GGS", "gallery");

                if (Directory.Exists(galleryPath))
                {
                    foreach (string file in Directory.GetFiles(galleryPath, "*.*"))
                    {
                        string extension = Path.GetExtension(file).ToLower();
                        if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif")
                            galleryImagePaths.Add(file);
                    }
                }

                if (galleryImagePaths.Count == 0)
                    CreateSampleImages();

                if (galleryImagePaths.Count > 0)
                    LoadCurrentImage();

                if (galleryImagePaths.Count > 1)
                    StartImageSlideShow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gallery initialization failed: {ex}");
            }
        }

        private void CreateSampleImages()
        {
            try
            {
                string samplePath = Path.Combine(rootPath, "GGS", "gallery");
                if (!Directory.Exists(samplePath))
                    Directory.CreateDirectory(samplePath);

                for (int i = 1; i <= 4; i++)
                {
                    string imagePath = Path.Combine(samplePath, $"sample{i}.png");
                    CreateSampleImage(imagePath, i);
                    galleryImagePaths.Add(imagePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create sample images: {ex}");
            }
        }

        private void CreateSampleImage(string filePath, int index)
        {
            try
            {
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    Color[] colors = {
                        Color.FromRgb(30, 58, 138),
                        Color.FromRgb(22, 101, 57),
                        Color.FromRgb(153, 27, 27),
                        Color.FromRgb(126, 34, 206)
                    };

                    context.DrawRectangle(new SolidColorBrush(colors[index - 1]), null, new Rect(0, 0, 300, 200));

                    var text = new FormattedText(
                        $"Sample {index}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        24,
                        Brushes.White,
                        1.0);

                    context.DrawText(text, new Point(50, 80));
                }

                var bitmap = new RenderTargetBitmap(300, 200, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = File.Create(filePath);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create sample image: {ex}");
            }
        }

        private void LoadCurrentImage()
        {
            if (galleryImagePaths.Count == 0) return;

            try
            {
                string imagePath = galleryImagePaths[currentImageIndex];
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();

                CurrentImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load image: {ex}");
            }
        }

        private void StartImageSlideShow()
        {
            imageSlideTimer = new DispatcherTimer();
            imageSlideTimer.Interval = TimeSpan.FromSeconds(5);
            imageSlideTimer.Tick += (s, e) => ShowNextImage();
            imageSlideTimer.Start();
        }

        private void ShowNextImage()
        {
            if (galleryImagePaths.Count <= 1) return;

            try
            {
                currentImageIndex = (currentImageIndex + 1) % galleryImagePaths.Count;

                string nextImagePath = galleryImagePaths[currentImageIndex];
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(nextImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                NextImage.Source = bitmap;

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));

                fadeOut.Completed += (s, e) =>
                {
                    CurrentImage.Source = NextImage.Source;
                    CurrentImage.Opacity = 1;
                    NextImage.Opacity = 0;
                };

                CurrentImage.BeginAnimation(Image.OpacityProperty, fadeOut);
                NextImage.BeginAnimation(Image.OpacityProperty, fadeIn);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show next image: {ex}");
            }
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                await Task.Delay(1000);

                if (File.Exists(versionFile))
                {
                    var localVersion = new GameVersion(File.ReadAllText(versionFile));
                    VersionText.Text = localVersion.ToString();

                    var onlineVersion = new GameVersion("0.0.0.1");

                    if (onlineVersion.IsDifferentThan(localVersion))
                    {
                        var result = MessageBox.Show(
                            $"Available update: {onlineVersion}\nCurrent version: {localVersion}\n\nDo you want to download the update?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                            await InstallGameFilesAsync(true, onlineVersion);
                        else
                            Status = LauncherStatus.ready;
                    }
                    else
                    {
                        Status = LauncherStatus.ready;
                    }
                }
                else
                {
                    File.WriteAllText(versionFile, "0.0.0.0");
                    VersionText.Text = "0.0.0.0";
                    await CreateTestGameFilesAsync();
                    Status = LauncherStatus.ready;
                }
            }
            catch (Exception)
            {
                Status = LauncherStatus.failed;
            }
        }

        private async Task InstallGameFilesAsync(bool isUpdate, GameVersion onlineVersion)
        {
            try
            {
                Status = isUpdate ? LauncherStatus.downloadingUpdate : LauncherStatus.downloadingGame;

                for (int progress = 0; progress <= 100; progress += 10)
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested) return;
                    await Task.Delay(200);
                    PlayButton.Content = $"Downloading... {progress}%";
                }

                await CreateTestGameFilesAsync();
                File.WriteAllText(versionFile, onlineVersion.ToString());

                VersionText.Text = onlineVersion.ToString();
                Status = LauncherStatus.ready;
            }
            catch (OperationCanceledException)
            {
                Status = LauncherStatus.ready;
            }
            catch (Exception)
            {
                Status = LauncherStatus.failed;
            }
        }

        private async Task CreateTestGameFilesAsync()
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(gameDirectory))
                    Directory.CreateDirectory(gameDirectory);

                const string testBatContent = @"
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
                File.WriteAllText(gameExe, "Test game executable - replace with actual game");
            });
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Path.Combine(gameDirectory, "TEST.bat")) && Status == LauncherStatus.ready)
            {
                try
                {
                    var startInfo = new ProcessStartInfo(Path.Combine(gameDirectory, "TEST.bat"))
                    {
                        WorkingDirectory = gameDirectory,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start game: {ex.Message}\n\nMake sure the game files are properly installed.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (Status == LauncherStatus.failed)
            {
                await CheckForUpdateAsync();
            }
            else
            {
                MessageBox.Show("Game files not found. Please check for updates or reinstall.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_02_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://rtnikcub.github.io/RC.io/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open help page: {ex.Message}\n\nYou can manually visit: https://rtnikcub.github.io/RC.io/",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            imageSlideTimer?.Stop();
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
    }
}

