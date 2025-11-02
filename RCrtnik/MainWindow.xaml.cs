using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
        private DispatcherTimer imageSlideTimer;
        private CancellationTokenSource cancellationTokenSource;

        // Галерея изображений
        private List<BitmapImage> galleryImages;
        private int currentImageIndex = 0;
        private Image currentImage;
        private Image nextImage;
        private Border galleryBorder;

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
            InitializeImageGallery();
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

        private void InitializeImageGallery()
        {
            // Инициализируем коллекцию изображений
            galleryImages = new List<BitmapImage>();

            // Добавляем изображения
            try
            {
                // Получаем путь к папке с изображениями относительно корневой директории
                string galleryPath = Path.Combine(rootPath, "GGS", "gallery");

                // Проверяем существование папки
                if (Directory.Exists(galleryPath))
                {
                    Debug.WriteLine($"Gallery directory found: {galleryPath}");

                    // Получаем все файлы изображений
                    string[] imageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" };
                    List<string> imageFiles = new List<string>();

                    foreach (string extension in imageExtensions)
                    {
                        try
                        {
                            var files = Directory.GetFiles(galleryPath, extension, SearchOption.TopDirectoryOnly);
                            imageFiles.AddRange(files);
                            Debug.WriteLine($"Found {files.Length} files with extension {extension}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error searching for {extension} files: {ex.Message}");
                        }
                    }

                    Debug.WriteLine($"Total image files found: {imageFiles.Count}");

                    // Загружаем изображения
                    foreach (string imageFile in imageFiles)
                    {
                        try
                        {
                            BitmapImage bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(imageFile, UriKind.Absolute);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                            bitmap.EndInit();

                            // Проверяем, что изображение загружено корректно
                            if (bitmap.Width > 0 && bitmap.Height > 0)
                            {
                                galleryImages.Add(bitmap);
                                Debug.WriteLine($"Successfully loaded: {Path.GetFileName(imageFile)}");
                            }
                            else
                            {
                                Debug.WriteLine($"Invalid image dimensions: {Path.GetFileName(imageFile)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to load {imageFile}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"Gallery directory not found: {galleryPath}");
                    // Создаем папку для будущего использования
                    try
                    {
                        Directory.CreateDirectory(galleryPath);
                        Debug.WriteLine($"Created gallery directory: {galleryPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to create gallery directory: {ex.Message}");
                    }
                }

                // Если файлы не найдены или не загрузились, создаем тестовые изображения
                if (galleryImages.Count == 0)
                {
                    Debug.WriteLine("No valid images found, creating sample images");
                    CreateSampleImages();
                }
                else
                {
                    Debug.WriteLine($"Loaded {galleryImages.Count} images for gallery");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gallery initialization error: {ex.Message}");
                CreateSampleImages();
            }

            // Создаем контейнер для галереи
            CreateGalleryContainer();

            // Запускаем таймер смены изображений если есть что показывать
            if (galleryImages.Count > 1)
            {
                StartImageSlideShow();
            }
            else if (galleryImages.Count == 1)
            {
                // Если только одно изображение, просто показываем его
                if (currentImage != null)
                {
                    currentImage.Source = galleryImages[0];
                }
            }
        }

        private void CreateSampleImages()
        {
            // Создаем программные изображения для демонстрации
            galleryImages.Clear();

            // Создаем несколько цветных изображений программно
            for (int i = 0; i < 4; i++)
            {
                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                {
                    Color[] colors = {
                        Color.FromArgb(0xFF, 0x1E, 0x3A, 0x8A), // Темно-синий
                        Color.FromArgb(0xFF, 0x16, 0x65, 0x39), // Темно-зеленый
                        Color.FromArgb(0xFF, 0x99, 0x1B, 0x1B), // Темно-красный
                        Color.FromArgb(0xFF, 0x7E, 0x22, 0xCE)  // Фиолетовый
                    };
                    string[] texts = {
                        "Здесь может быть\nваша реклама!",
                        "Здесь может быть\nваша реклама!",
                        "Здесь может быть\nваша реклама!",
                        "Здесь может быть\nваша реклама!" };

                    // Фон
                    drawingContext.DrawRectangle(new SolidColorBrush(colors[i]), null, new Rect(0, 0, 300, 200));

                    // Градиент для красоты
                    var gradient = new LinearGradientBrush(
                        Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
                        Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                        45);
                    drawingContext.DrawRectangle(gradient, null, new Rect(0, 0, 300, 200));

                    // Текст
                    drawingContext.DrawText(
                        new FormattedText(texts[i],
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Arial"),
                            18,
                            Brushes.White,
                            1.0),
                        new Point(20, 80));
                }

                RenderTargetBitmap bmp = new RenderTargetBitmap(300, 200, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(drawingVisual);

                // Конвертируем в BitmapImage
                BitmapImage bitmapImage = new BitmapImage();
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));

                using (MemoryStream stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    stream.Seek(0, SeekOrigin.Begin);

                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                }

                galleryImages.Add(bitmapImage);
            }
        }

        private void CreateGalleryContainer()
        {
            // Создаем контейнер для галереи в правом верхнем углу
            var galleryGrid = new Grid();
            galleryGrid.Width = 280;
            galleryGrid.Height = 180;
            galleryGrid.HorizontalAlignment = HorizontalAlignment.Center;
            galleryGrid.VerticalAlignment = VerticalAlignment.Center;
            galleryGrid.ClipToBounds = true;

            // Добавляем бордер для красивого отображения
            galleryBorder = new Border();
            galleryBorder.Width = 300;
            galleryBorder.Height = 200;
            galleryBorder.Margin = new Thickness(0, 15, 15, 0);
            galleryBorder.HorizontalAlignment = HorizontalAlignment.Right;
            galleryBorder.VerticalAlignment = VerticalAlignment.Top;
            galleryBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            galleryBorder.BorderThickness = new Thickness(2);
            galleryBorder.CornerRadius = new CornerRadius(10);
            galleryBorder.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00));

            // Добавляем тень если доступно
            try
            {
                galleryBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect()
                {
                    Color = Colors.Black,
                    Direction = 320,
                    ShadowDepth = 10,
                    Opacity = 0.6,
                    BlurRadius = 10
                };
            }
            catch
            {
                // Если тень не доступна, продолжаем без нее
            }

            // Создаем изображения для анимации
            currentImage = new Image();
            currentImage.Stretch = Stretch.UniformToFill;
            currentImage.HorizontalAlignment = HorizontalAlignment.Center;
            currentImage.VerticalAlignment = VerticalAlignment.Center;

            nextImage = new Image();
            nextImage.Stretch = Stretch.UniformToFill;
            nextImage.HorizontalAlignment = HorizontalAlignment.Center;
            nextImage.VerticalAlignment = VerticalAlignment.Center;
            nextImage.Opacity = 0;

            if (galleryImages.Count > 0)
            {
                currentImage.Source = galleryImages[0];
            }

            // Добавляем элементы в контейнер
            galleryGrid.Children.Add(currentImage);
            galleryGrid.Children.Add(nextImage);
            galleryBorder.Child = galleryGrid;

            // Добавляем в главный Grid
            var mainGrid = Content as Grid;
            if (mainGrid != null)
            {
                mainGrid.Children.Add(galleryBorder);
                Panel.SetZIndex(galleryBorder, 1);
            }
        }

        private void StartImageSlideShow()
        {
            if (galleryImages.Count <= 1) return;

            imageSlideTimer = new DispatcherTimer();
            imageSlideTimer.Interval = TimeSpan.FromSeconds(5);
            imageSlideTimer.Tick += ImageSlideTimer_Tick;
            imageSlideTimer.Start();
        }

        private void ImageSlideTimer_Tick(object sender, EventArgs e)
        {
            ShowNextImageWithAnimation();
        }

        private void ShowNextImageWithAnimation()
        {
            if (galleryImages.Count <= 1) return;

            // Вычисляем индекс следующего изображения
            currentImageIndex = (currentImageIndex + 1) % galleryImages.Count;

            // Устанавливаем следующее изображение
            nextImage.Source = galleryImages[currentImageIndex];

            // Создаем анимацию перехода
            DoubleAnimation fadeOutAnimation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
            DoubleAnimation fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));

            fadeOutAnimation.Completed += (s, e) =>
            {
                // После завершения анимации меняем изображения местами
                currentImage.Source = nextImage.Source;
                currentImage.Opacity = 1;
                nextImage.Opacity = 0;
            };

            currentImage.BeginAnimation(Image.OpacityProperty, fadeOutAnimation);
            nextImage.BeginAnimation(Image.OpacityProperty, fadeInAnimation);
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
                    GameVersion onlineVersion = new GameVersion("1.0.1");

                    if (onlineVersion.IsDifferentThan(localVersion))
                    {
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
                    File.WriteAllText(versionFile, "1.0.0");
                    Dispatcher.Invoke(() => VersionText.Text = "1.0.0");

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

                for (int progress = 0; progress <= 100; progress += 10)
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    await Task.Delay(200);

                    Dispatcher.Invoke(() =>
                    {
                        PlayButton.Content = $"Downloading... {progress}%";
                    });
                }

                await CreateTestGameFilesAsync();

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
                if (!Directory.Exists(gameDirectory))
                {
                    Directory.CreateDirectory(gameDirectory);
                }

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
                    ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(gameDirectory, "TEST.bat"));
                    startInfo.WorkingDirectory = gameDirectory;
                    startInfo.UseShellExecute = true;
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    await ShowErrorMessageAsync($"Failed to start game: {ex.Message}\n\nMake sure the game files are properly installed.");
                }
            }
            else if (Status == LauncherStatus.failed)
            {
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
            string url = "https://github.com";

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