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
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

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
        private string nicknamesFile;
        private string Exx;
        private DispatcherTimer timer;
        private DispatcherTimer imageSlideTimer;
        private DispatcherTimer nickScrollTimer;
        private DispatcherTimer statsTimer;
        private CancellationTokenSource cancellationTokenSource;

        // Галерея изображений
        private List<string> galleryImagePaths;
        private int currentImageIndex = 0;
        private Image currentImage;
        private Image nextImage;
        private Border galleryBorder;

        // Бегущая строка ников
        private ObservableCollection<string> nicknames;
        private ListView nickListView;
        private Border nickBorder;
        private int currentNickIndex = 0;

        // Левая рамка со статистикой
        private Border leftBorder;
        private TextBlock internetStatusText;
        private TextBlock pcIdText;
        private TextBlock cpuUsageText;

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

            rootPath = Directory.GetCurrentDirectory();
            versionFile = Path.Combine(rootPath, "Version.txt");
            gameZip = Path.Combine(rootPath, "Tukhtai-006.zip");
            gameDirectory = Path.Combine(rootPath, "TR");
            gameExe = Path.Combine(gameDirectory, "TEST.exe");
            nicknamesFile = Path.Combine(rootPath, "nicknames.txt");
            cancellationTokenSource = new CancellationTokenSource();

            // Создаем необходимые директории
            if (!Directory.Exists(gameDirectory))
            {
                Directory.CreateDirectory(gameDirectory);
            }

            // Инициализируем все элементы
            CreateLeftBorder();
            InitializeNicknames();
            InitializeImageGallery();
            StartStatsTimer();
            Status = LauncherStatus.defalt;
        }

        private void CreateLeftBorder()
        {
            try
            {
                // Создаем левую рамку во всю высоту
                leftBorder = new Border();
                leftBorder.Width = 250;
                leftBorder.HorizontalAlignment = HorizontalAlignment.Left;
                leftBorder.VerticalAlignment = VerticalAlignment.Stretch;
                leftBorder.Margin = new Thickness(0);
                leftBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
                leftBorder.BorderThickness = new Thickness(2);
                leftBorder.CornerRadius = new CornerRadius(0);
                leftBorder.Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x0A, 0x0A, 0x0A));

                // Создаем контейнер для статистики
                var statsPanel = new StackPanel();
                statsPanel.Margin = new Thickness(15);
                statsPanel.Orientation = Orientation.Vertical;
                statsPanel.VerticalAlignment = VerticalAlignment.Top;

                // Заголовок
                var headerText = new TextBlock();
                headerText.Text = "SYSTEM STATS";
                headerText.Foreground = Brushes.White;
                headerText.FontSize = 18;
                headerText.FontWeight = FontWeights.Bold;
                headerText.HorizontalAlignment = HorizontalAlignment.Center;
                headerText.Margin = new Thickness(0, 10, 0, 20);
                statsPanel.Children.Add(headerText);

                // Статус интернета
                var internetPanel = CreateStatPanel("Internet Status:", out internetStatusText);
                statsPanel.Children.Add(internetPanel);

                // ID ПК
                var pcIdPanel = CreateStatPanel("PC Identifier:", out pcIdText);
                statsPanel.Children.Add(pcIdPanel);

                // Загрузка ЦП
                var cpuPanel = CreateStatPanel("CPU Usage:", out cpuUsageText);
                statsPanel.Children.Add(cpuPanel);

                leftBorder.Child = statsPanel;

                // Добавляем в главный Grid
                var mainGrid = Content as Grid;
                if (mainGrid != null)
                {
                    mainGrid.Children.Add(leftBorder);
                    Panel.SetZIndex(leftBorder, 0);
                }

                Debug.WriteLine("Left border with stats created successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create left border: {ex}");
            }
        }

        private StackPanel CreateStatPanel(string label, out TextBlock valueText)
        {
            var panel = new StackPanel();
            panel.Orientation = Orientation.Vertical;
            panel.Margin = new Thickness(0, 0, 0, 15);

            var labelText = new TextBlock();
            labelText.Text = label;
            labelText.Foreground = Brushes.LightGray;
            labelText.FontSize = 12;
            labelText.Margin = new Thickness(0, 0, 0, 5);
            panel.Children.Add(labelText);

            valueText = new TextBlock();
            valueText.Foreground = Brushes.White;
            valueText.FontSize = 14;
            valueText.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(valueText);

            return panel;
        }

        private void StartStatsTimer()
        {
            statsTimer = new DispatcherTimer();
            statsTimer.Interval = TimeSpan.FromSeconds(2);
            statsTimer.Tick += StatsTimer_Tick;
            statsTimer.Start();
        }

        private void StatsTimer_Tick(object sender, EventArgs e)
        {
            UpdateStats();
        }

        private void UpdateStats()
        {
            try
            {
                // Обновляем статус интернета
                UpdateInternetStatus();

                // Обновляем ID ПК (только один раз)
                if (string.IsNullOrEmpty(pcIdText.Text))
                {
                    UpdatePCIdentifier();
                }

                // Обновляем загрузку ЦП
                UpdateCPUUsage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Stats update failed: {ex}");
            }
        }

        private void UpdateInternetStatus()
        {
            try
            {
                bool hasInternet = CheckInternetConnection();
                Dispatcher.Invoke(() =>
                {
                    internetStatusText.Text = hasInternet ? "ONLINE" : "OFFLINE";
                    internetStatusText.Foreground = hasInternet ? Brushes.LightGreen : Brushes.Red;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Internet status check failed: {ex}");
            }
        }

        private bool CheckInternetConnection()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 3000); // Google DNS
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UpdatePCIdentifier()
        {
            try
            {
                string pcId = GetPCIdentifier();
                Dispatcher.Invoke(() =>
                {
                    pcIdText.Text = pcId;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PC ID update failed: {ex}");
                Dispatcher.Invoke(() =>
                {
                    pcIdText.Text = "Unknown";
                });
            }
        }

        private string GetPCIdentifier()
        {
            try
            {
                // Используем комбинацию имени машины и имени пользователя для создания уникального ID
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;
                string domainName = Environment.UserDomainName;

                // Создаем хэш для уникальности
                string combined = $"{machineName}_{userName}_{domainName}";
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToUpper();
                }
            }
            catch
            {
                return "UnknownPC";
            }
        }

        private void UpdateCPUUsage()
        {
            try
            {
                // Простая имитация загрузки ЦП для демонстрации
                // В реальном приложении можно использовать PerformanceCounter
                Random random = new Random();
                float cpuUsage = random.Next(5, 40); // Имитируем загрузку 5-40%

                Dispatcher.Invoke(() =>
                {
                    cpuUsageText.Text = $"{cpuUsage:0}%";
                    // Меняем цвет в зависимости от загрузки
                    if (cpuUsage < 30)
                        cpuUsageText.Foreground = Brushes.LightGreen;
                    else if (cpuUsage < 60)
                        cpuUsageText.Foreground = Brushes.Orange;
                    else
                        cpuUsageText.Foreground = Brushes.Red;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CPU usage update failed: {ex}");
                Dispatcher.Invoke(() =>
                {
                    cpuUsageText.Text = "N/A";
                    cpuUsageText.Foreground = Brushes.Gray;
                });
            }
        }

        private void InitializeNicknames()
        {
            try
            {
                nicknames = new ObservableCollection<string>();

                if (File.Exists(nicknamesFile))
                {
                    string[] lines = File.ReadAllLines(nicknamesFile);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            nicknames.Add(line.Trim());
                        }
                    }
                    Debug.WriteLine($"Loaded {nicknames.Count} nicknames from file");
                }
                else
                {
                    CreateNicknamesFile();
                    string[] lines = File.ReadAllLines(nicknamesFile);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            nicknames.Add(line.Trim());
                        }
                    }
                    Debug.WriteLine($"Created and loaded {nicknames.Count} nicknames");
                }

                CreateNickList();
                StartNickScroll();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Nicknames initialization failed: {ex}");
            }
        }

        private void CreateNicknamesFile()
        {
            try
            {
                List<string> nickList = new List<string>();

                string[] prefixes = {
                    "Shadow", "Dark", "Light", "Cyber", "Neo", "Alpha", "Beta", "Omega", "Ultra", "Mega",
                    "Super", "Hyper", "Ghost", "Phantom", "Stealth", "Silent", "Rapid", "Swift", "Blaze", "Ice",
                    "Fire", "Thunder", "Storm", "Wind", "Earth", "Water", "Metal", "Steel", "Iron", "Gold",
                    "Silver", "Bronze", "Crystal", "Diamond", "Ruby", "Sapphire", "Emerald", "Amber", "Onyx", "Jade",
                    "Dragon", "Phoenix", "Wolf", "Tiger", "Lion", "Eagle", "Hawk", "Falcon", "Raven", "Crow",
                    "Warrior", "Knight", "Mage", "Wizard", "Hunter", "Ranger", "Assassin", "Ninja", "Samurai", "Viking",
                    "Pirate", "Captain", "Commander", "General", "Agent", "Spy", "Master", "Lord", "King", "Queen",
                    "Prince", "Princess", "Duke", "Baron", "Chief", "Leader", "Hero", "Legend", "Myth", "Epic",
                    "Ancient", "Eternal", "Infinite", "Cosmic", "Galactic", "Solar", "Lunar", "Stellar", "Nova", "Quantum"
                };

                string[] suffixes = {
                    "X", "Z", "Pro", "Master", "Expert", "Elite", "Ace", "Lord", "King", "Queen",
                    "Warrior", "Hunter", "Slayer", "Killer", "Destroyer", "Annihilator", "Dominator", "Conqueror", "Champion", "Victor",
                    "Blade", "Arrow", "Bow", "Sword", "Axe", "Hammer", "Shield", "Armor", "Helmet", "Gauntlet",
                    "Rider", "Walker", "Runner", "Jumper", "Flyer", "Swimmer", "Diver", "Climber", "Crawler", "Glider",
                    "Storm", "Blaze", "Frost", "Thunder", "Lightning", "Earth", "Wind", "Water", "Fire", "Ice",
                    "Shadow", "Light", "Dark", "Night", "Day", "Dawn", "Dusk", "Twilight", "Midnight", "Noon",
                    "Alpha", "Beta", "Gamma", "Delta", "Sigma", "Omega", "Theta", "Lambda", "Psi", "Phi",
                    "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
                    "Prime", "Ultimate", "Final", "Last", "First", "New", "Old", "Young", "Ancient", "Modern",
                    "Red", "Blue", "Green", "Yellow", "Black", "White", "Gray", "Purple", "Orange", "Pink"
                };

                Random random = new Random();
                HashSet<string> usedNicks = new HashSet<string>();

                while (nickList.Count < 200)
                {
                    string prefix = prefixes[random.Next(prefixes.Length)];
                    string suffix = suffixes[random.Next(suffixes.Length)];
                    string nick = $"{prefix}{suffix}";

                    if (usedNicks.Contains(nick))
                    {
                        nick = $"{prefix}{suffix}{random.Next(1000)}";
                    }

                    if (!usedNicks.Contains(nick))
                    {
                        usedNicks.Add(nick);
                        nickList.Add(nick);
                    }
                }

                File.WriteAllLines(nicknamesFile, nickList);
                Debug.WriteLine($"Created nicknames file with {nickList.Count} nicks");

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create nicknames file: {ex}");
            }
        }

        private void CreateNickList()
        {
            try
            {
                nickListView = new ListView();
                nickListView.Width = 260;
                nickListView.Height = 120;
                nickListView.Background = Brushes.Transparent;
                nickListView.BorderThickness = new Thickness(0);
                nickListView.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                nickListView.FontSize = 12;
                nickListView.FontWeight = FontWeights.SemiBold;

                ScrollViewer.SetVerticalScrollBarVisibility(nickListView, ScrollBarVisibility.Hidden);
                ScrollViewer.SetHorizontalScrollBarVisibility(nickListView, ScrollBarVisibility.Hidden);

                Style listViewItemStyle = new Style(typeof(ListViewItem));
                listViewItemStyle.Setters.Add(new Setter(ListViewItem.BackgroundProperty, Brushes.Transparent));
                listViewItemStyle.Setters.Add(new Setter(ListViewItem.BorderThicknessProperty, new Thickness(0)));
                listViewItemStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(12, 4, 12, 4)));
                listViewItemStyle.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0)));
                listViewItemStyle.Setters.Add(new Setter(ListViewItem.FocusVisualStyleProperty, null));
                listViewItemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
                listViewItemStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));

                nickListView.ItemContainerStyle = listViewItemStyle;
                nickListView.ItemsSource = nicknames;

                nickBorder = new Border();
                nickBorder.Width = 280;
                nickBorder.Height = 140;
                nickBorder.Margin = new Thickness(0, 230, 15, 0);
                nickBorder.HorizontalAlignment = HorizontalAlignment.Right;
                nickBorder.VerticalAlignment = VerticalAlignment.Top;
                nickBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
                nickBorder.BorderThickness = new Thickness(2);
                nickBorder.CornerRadius = new CornerRadius(0);
                nickBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0A, 0x0A, 0x0A));

                var mainContainer = new StackPanel();
                mainContainer.Background = Brushes.Transparent;
                mainContainer.Children.Add(nickListView);
                nickBorder.Child = mainContainer;

                try
                {
                    nickBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect()
                    {
                        Color = Colors.Black,
                        Direction = 320,
                        ShadowDepth = 5,
                        Opacity = 0.8,
                        BlurRadius = 10
                    };
                }
                catch { }

                var mainGrid = Content as Grid;
                if (mainGrid != null)
                {
                    mainGrid.Children.Add(nickBorder);
                    Panel.SetZIndex(nickBorder, 1);
                }

                Debug.WriteLine("Nick list created successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create nick list: {ex}");
            }
        }

        private void StartNickScroll()
        {
            nickScrollTimer = new DispatcherTimer();
            nickScrollTimer.Interval = TimeSpan.FromMilliseconds(1500);
            nickScrollTimer.Tick += NickScrollTimer_Tick;
            nickScrollTimer.Start();
        }

        private void NickScrollTimer_Tick(object sender, EventArgs e)
        {
            ScrollNicknames();
        }

        private void ScrollNicknames()
        {
            if (nicknames == null || nicknames.Count == 0) return;

            try
            {
                // Простая анимация движения вверх без моргания
                DoubleAnimation scrollAnimation = new DoubleAnimation();
                scrollAnimation.From = 30; // Начинаем ниже
                scrollAnimation.To = -25; // Заканчиваем выше (выходим за рамку)
                scrollAnimation.Duration = TimeSpan.FromMilliseconds(1500);
                scrollAnimation.EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseInOut };

                scrollAnimation.Completed += (s, e) =>
                {
                    // После анимации перемещаем первый ник в конец
                    string firstNick = nicknames[0];
                    nicknames.RemoveAt(0);
                    nicknames.Add(firstNick);

                    // Сбрасываем позицию
                    nickListView.ItemsSource = null;
                    nickListView.ItemsSource = nicknames;

                    // Сбрасываем трансформацию
                    nickListView.RenderTransform = null;
                };

                // Применяем анимацию
                var transform = new TranslateTransform();
                nickListView.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.YProperty, scrollAnimation);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Nick scroll failed: {ex}");
            }
        }

        private void InitializeImageGallery()
        {
            try
            {
                Debug.WriteLine("=== Initializing Image Gallery ===");
                Debug.WriteLine($"Root path: {rootPath}");

                galleryImagePaths = new List<string>();
                string galleryPath = Path.Combine(rootPath, "GGS", "gallery");
                Debug.WriteLine($"Gallery path: {galleryPath}");
                Debug.WriteLine($"Directory exists: {Directory.Exists(galleryPath)}");

                if (Directory.Exists(galleryPath))
                {
                    string[] allFiles = Directory.GetFiles(galleryPath, "*.*", SearchOption.TopDirectoryOnly);
                    Debug.WriteLine($"Total files in gallery: {allFiles.Length}");

                    foreach (string file in allFiles)
                    {
                        string extension = Path.GetExtension(file).ToLower();
                        if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                            extension == ".bmp" || extension == ".gif")
                        {
                            galleryImagePaths.Add(file);
                            Debug.WriteLine($"Found image: {Path.GetFileName(file)}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Gallery directory does not exist");
                }

                Debug.WriteLine($"Total images found: {galleryImagePaths.Count}");

                if (galleryImagePaths.Count == 0)
                {
                    Debug.WriteLine("Creating sample images...");
                    CreateSampleImages();
                }

                CreateGalleryContainer();

                if (galleryImagePaths.Count > 0)
                {
                    LoadCurrentImage();
                }

                if (galleryImagePaths.Count > 1)
                {
                    StartImageSlideShow();
                }

                Debug.WriteLine("=== Gallery initialization complete ===");
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
                Debug.WriteLine("Creating sample images directory...");

                string samplePath = Path.Combine(rootPath, "GGS", "gallery");
                if (!Directory.Exists(samplePath))
                {
                    Directory.CreateDirectory(samplePath);
                }

                for (int i = 1; i <= 4; i++)
                {
                    string imagePath = Path.Combine(samplePath, $"sample{i}.png");
                    CreateSampleImage(imagePath, i);
                    galleryImagePaths.Add(imagePath);
                    Debug.WriteLine($"Created sample image: {imagePath}");
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

                using (var stream = File.Create(filePath))
                {
                    encoder.Save(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create sample image {filePath}: {ex}");
            }
        }

        private void CreateGalleryContainer()
        {
            try
            {
                Debug.WriteLine("Creating gallery container...");

                var galleryGrid = new Grid();
                galleryGrid.Width = 280;
                galleryGrid.Height = 180;
                galleryGrid.HorizontalAlignment = HorizontalAlignment.Center;
                galleryGrid.VerticalAlignment = VerticalAlignment.Center;
                galleryGrid.ClipToBounds = true;

                currentImage = new Image();
                currentImage.Stretch = Stretch.UniformToFill;
                currentImage.HorizontalAlignment = HorizontalAlignment.Center;
                currentImage.VerticalAlignment = VerticalAlignment.Center;

                nextImage = new Image();
                nextImage.Stretch = Stretch.UniformToFill;
                nextImage.HorizontalAlignment = HorizontalAlignment.Center;
                nextImage.VerticalAlignment = VerticalAlignment.Center;
                nextImage.Opacity = 0;

                galleryGrid.Children.Add(currentImage);
                galleryGrid.Children.Add(nextImage);

                // Создаем бордер для галереи с ПРЯМЫМИ углами
                galleryBorder = new Border();
                galleryBorder.Width = 300;
                galleryBorder.Height = 200;
                galleryBorder.Margin = new Thickness(0, 15, 15, 0);
                galleryBorder.HorizontalAlignment = HorizontalAlignment.Right;
                galleryBorder.VerticalAlignment = VerticalAlignment.Top;
                galleryBorder.BorderBrush = new SolidColorBrush(Colors.Gray);
                galleryBorder.BorderThickness = new Thickness(2);
                galleryBorder.CornerRadius = new CornerRadius(0); // ПРЯМЫЕ углы
                galleryBorder.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00));
                galleryBorder.Child = galleryGrid;

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

                var mainGrid = Content as Grid;
                if (mainGrid != null)
                {
                    mainGrid.Children.Add(galleryBorder);
                    Panel.SetZIndex(galleryBorder, 2);
                }

                Debug.WriteLine("Gallery container created successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create gallery container: {ex}");
            }
        }

        private void LoadCurrentImage()
        {
            if (galleryImagePaths.Count == 0 || currentImage == null) return;

            try
            {
                string imagePath = galleryImagePaths[currentImageIndex];
                Debug.WriteLine($"Loading image: {imagePath}");

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();

                currentImage.Source = bitmap;
                Debug.WriteLine("Image loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load image: {ex}");
            }
        }

        private void StartImageSlideShow()
        {
            if (galleryImagePaths.Count <= 1) return;

            imageSlideTimer = new DispatcherTimer();
            imageSlideTimer.Interval = TimeSpan.FromSeconds(5);
            imageSlideTimer.Tick += ImageSlideTimer_Tick;
            imageSlideTimer.Start();

            Debug.WriteLine("Slide show started");
        }

        private void ImageSlideTimer_Tick(object sender, EventArgs e)
        {
            ShowNextImage();
        }

        private void ShowNextImage()
        {
            if (galleryImagePaths.Count <= 1) return;

            try
            {
                currentImageIndex = (currentImageIndex + 1) % galleryImagePaths.Count;

                string nextImagePath = galleryImagePaths[currentImageIndex];
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(nextImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();

                nextImage.Source = bitmap;

                DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));

                fadeOut.Completed += (s, e) =>
                {
                    currentImage.Source = nextImage.Source;
                    currentImage.Opacity = 1;
                    nextImage.Opacity = 0;
                };

                currentImage.BeginAnimation(Image.OpacityProperty, fadeOut);
                nextImage.BeginAnimation(Image.OpacityProperty, fadeIn);
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
                    GameVersion localVersion = new GameVersion(File.ReadAllText(versionFile));
                    Dispatcher.Invoke(() => VersionText.Text = localVersion.ToString());

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
                Status = isUpdate ? LauncherStatus.downloadingUpdate : LauncherStatus.downloadingGame;

                for (int progress = 0; progress <= 100; progress += 10)
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested) return;
                    await Task.Delay(200);
                    Dispatcher.Invoke(() => PlayButton.Content = $"Downloading... {progress}%");
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
                await Task.Run(() => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }));
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
            nickScrollTimer?.Stop();
            statsTimer?.Stop();
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