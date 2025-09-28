using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Policy;
using System.Windows;

namespace RCrtnik
{

    enum LauncherStatus
    {
        ready,
        failed,
        downlodingDame,
        downlodingUpdate
    }



    public partial class MainWindow : Window
    {
        private string rootPath;
        private string versionFile;
        private string gameZip;
        private string gameExe;

        private LauncherStatus _status;
        internal LauncherStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                switch (_status) // Текст на кнопке
                {
                    case LauncherStatus.ready:
                        PlayButton.Content = "Play";
                        break;
                    case LauncherStatus.failed:
                        PlayButton.Content = "Error";
                        break;
                    case LauncherStatus.downlodingDame:
                        PlayButton.Content = "Downd Update";
                        break;
                    case LauncherStatus.downlodingUpdate:
                        PlayButton.Content = "Downd Game";
                        break;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();


            rootPath = Directory.GetCurrentDirectory();
            versionFile = Path.Combine(rootPath, "Version.txt");
            gameZip = Path.Combine(rootPath, "Tukhtai-006.zip");
            gameExe = Path.Combine(rootPath, "TR", "TEST.exe");
        }

        private void CheckForUpdate() //Провека обновлений
        {
            if (File.Exists(versionFile)) // Обновление
            {
                Version localVersion = new Version(File.ReadAllText(versionFile));
                VersionText.Text = localVersion.ToString();

                try
                {
                    WebClient webClient = new WebClient();
                    Version onlineVersion = new Version(webClient.DownloadString("Addres"));//в двух местах сылка номера версий

                    if (onlineVersion.IsDifferentThan(localVersion))
                    {
                        InstallGameFiles(true, onlineVersion);
                    }
                    else
                    {
                        Status = LauncherStatus.ready;
                    }
                }
                catch (Exception ex)
                {
                    Status = LauncherStatus.failed;
                    MessageBox.Show($"Error checking for game updates: {ex}");
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
                    Status = LauncherStatus.downlodingUpdate;
                }
                else
                {
                    Status = LauncherStatus.downlodingDame;
                    _onlineVersion = new Version(webClient.DownloadString("Addres"));//в двух местах сылка номера версий
                    
                }

                webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                webClient.DownloadFileAsync(new Uri("https://onedrive.live.com/download?resid=2B1021F94D044543%21107&authkey=!AJIGhKzE3aau0LE"), gameZip, _onlineVersion);// Сылка на зип
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                MessageBox.Show($"Error installing game files:\n {ex}");
                //
            }
        }

        private void DownloadGameCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                string onlineVersion = ((Version)e.UserState).ToString();
                ZipFile.ExtractToDirectory(gameZip, rootPath, true);
                File.Delete(gameZip);

                File.WriteAllText(versionFile, onlineVersion);

                VersionText.Text = onlineVersion;
                Status = LauncherStatus.ready;
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
                MessageBox.Show($"Error finishing download: {ex}");
            }
        }


        private void Window_ContentRendered(object sender, EventArgs e) // Вызов функций
        {
            CheckForUpdate();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)//Открытие списка ошибок
        {
            if (File.Exists(gameExe) && Status == LauncherStatus.ready)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(gameExe);
                startInfo.WorkingDirectory = Path.Combine(rootPath, "TR");
                Process.Start(startInfo);
                Environment.Exit(0);
                

                Close();
            }
            else if (Status == LauncherStatus.failed)
            {
                CheckForUpdate();
            }
        }
        private void Button_02_Click(object sender, RoutedEventArgs e)//Кнопка 02
        {
            string url = "https://rtnikcub.github.io/RC.io/";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.failed;
            }
        }

    }




    //Проверка версий

    struct Version
    {
        internal static Version zero = new Version(0,0,0);

        private short major;
        private short minor;
        private short subMinor;

        internal Version(short _major, short _minor, short _subMinor)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;

        }
        internal Version(string _version)
        {
            string[] _versionStrings = _version.Split('.');//Разделяет 10.29.3 на 10 29 3
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

        internal bool IsDifferentThan(Version _otherVersion)
        {
            if (major != _otherVersion.major) // 1 цыфра
            {
                return true;
            }
            else
            {
                if (minor != _otherVersion.minor) // 2 цыфра
                {
                    return true;
                }
                else
                {
                    if (subMinor != _otherVersion.subMinor) // 3 цыфра
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public override string ToString()
        {
            return $"{major}.{minor}.{subMinor}"; //Выдает версию
        }
    }

}

//+100
/*
 no
 */
