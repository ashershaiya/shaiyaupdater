using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Handlers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Updater.Common;
using Updater.Core;
using Updater.Helpers;
using Updater.Resources;

namespace Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker _backgroundWorker1;
        private readonly HttpClient _httpClient;
        private static readonly HttpClient _apiClient = new HttpClient();
        private readonly Image _image167 = new();
        private readonly Image _image168 = new();
        private readonly Image _image169 = new();
        private readonly Image _image170 = new();
        private readonly Image _image185 = new();
        private readonly Image _image187 = new();
        private readonly Image _image188 = new();
        private BitmapImage? _backgroundImage = null;
        private string _loggedInUserId = string.Empty;
        private int _loggedInPoints = 0;

        private readonly string ApiBaseUrl;
        private readonly string _gameServerHost;
        private readonly int _gameServerPort;
        private readonly DispatcherTimer _serverStatusTimer;
        private static readonly string SessionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".session");

        public MainWindow()
        {
            InitializeComponent();

            // Load connection string from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            ApiBaseUrl = configuration["ApiUrl"]
                ?? throw new InvalidOperationException("ApiUrl not found in appsettings.json.");

            // Load game server host/port for status check
            _gameServerHost = configuration["GameServer:Host"] ?? "127.0.0.1";
            _gameServerPort = int.TryParse(configuration["GameServer:Port"], out var port) ? port : 30800;

            // Start server status polling timer (every 30 seconds)
            _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverStatusTimer.Tick += async (s, args) => await CheckServerStatusAsync();
            _serverStatusTimer.Start();

            _backgroundWorker1 = new BackgroundWorker();
            _backgroundWorker1.WorkerReportsProgress = true;
            _backgroundWorker1.DoWork += BackgroundWorker1_DoWork;
            _backgroundWorker1.ProgressChanged += BackgroundWorker1_ProgressChanged;

            var handler = new ProgressMessageHandler(new HttpClientHandler());
            handler.HttpReceiveProgress += ProgressMessageHandler_HttpReceiveProgress;
            _httpClient = new HttpClient(handler, true);

            Button1.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Button1_Click), true);
            Button2.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Button2_Click), true);

            LoginPassword.PasswordChanged += (s, args) =>
            {
                if (LoginPassword.Template.FindName("placeholder", LoginPassword) is System.Windows.Controls.TextBlock ph)
                {
                    ph.Visibility = string.IsNullOrEmpty(LoginPassword.Password) ? Visibility.Visible : Visibility.Collapsed;
                }
            };
        }

        private static void ImageInit(Image image, string resourceName)
        {
            var bitmapImage = BitmapImageHelper.FromManifestResource(resourceName);
            if (bitmapImage != null)
            {
                image.Width = bitmapImage.PixelWidth;
                image.Height = bitmapImage.PixelHeight;
                image.Source = bitmapImage;
            }
        }

        private void ProgressMessageHandler_HttpReceiveProgress(object? sender, HttpProgressEventArgs e)
        {
            if (sender == null)
                return;

            _backgroundWorker1.ReportProgress(e.ProgressPercentage, new ProgressReport("ProgressBar1"));
        }

        private void BackgroundWorker1_DoWork(object? sender, DoWorkEventArgs e)
        {
            Program.DoWork(_httpClient, _backgroundWorker1);
        }

        private void BackgroundWorker1_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is string text)
            {
                TextBox1.Text = text;
            }

            if (e.UserState is ProgressReport progressReport)
            {
                if (progressReport.Value != null)
                {
                    if (progressReport.Value is string value)
                    {
                        if (value == ProgressBar1.Name)
                        {
                            ProgressBar1.Value = e.ProgressPercentage;
                        }

                        if (value == ProgressBar2.Name)
                        {
                            ProgressBar2.Value = e.ProgressPercentage;
                        }
                    }
                }
            }
        }

        private void Window1_Initialized(object sender, EventArgs e)
        {
            if (DllImport.FindWindowW("GAME", "Shaiya") != IntPtr.Zero)
            {
                MessageBox.Show(Strings.GameWindow, Title, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                Application.Current.Shutdown(0);
            }

            ImageInit(_image167, "Bitmap167.bmp");
            ImageInit(_image168, "Bitmap168.bmp");
            ImageInit(_image169, "Bitmap169.bmp");
            ImageInit(_image170, "Bitmap170.bmp");
            ImageInit(_image185, "Bitmap185.bmp");
            ImageInit(_image187, "Bitmap187.bmp");
            ImageInit(_image188, "Bitmap188.bmp");
            _backgroundImage = BitmapImageHelper.FromManifestResource("Background.png");
        }

        private void Window1_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the new background image on the Grid's ImageBrush
            if (_backgroundImage != null)
            {
                BackgroundBrush.ImageSource = _backgroundImage;
            }
            else
            {
                // Fallback to original background
                BackgroundBrush.ImageSource = _image167.Source;
            }

            Button1.Content = _image185;
            WebBrowser1.Navigate(Constants.WebBrowserSource);
            _backgroundWorker1.RunWorkerAsync();

            // Try auto-login from saved session
            _ = TryAutoLoginAsync();

            // Initial server status check
            _ = CheckServerStatusAsync();
        }

        private void Window1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private async Task CheckServerStatusAsync()
        {
            bool isOnline = false;
            try
            {
                using var client = new TcpClient();
                // 3-second timeout for the connection attempt
                var connectTask = client.ConnectAsync(_gameServerHost, _gameServerPort);
                var timeoutTask = Task.Delay(3000);
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == connectTask && client.Connected)
                {
                    isOnline = true;
                }
            }
            catch
            {
                // Ignore connection errors (server is offline)
            }

            UpdateServerStatusUI(isOnline);
        }

        private void UpdateServerStatusUI(bool isOnline)
        {
            Dispatcher.Invoke(() =>
            {
                if (isOnline)
                {
                    ServerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(68, 255, 68)); // #44FF44
                    ServerStatusText.Text = "ONLINE";
                    ServerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(136, 255, 136)); // #88FF88
                }
                else
                {
                    ServerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // #FF4444
                    ServerStatusText.Text = "OFFLINE";
                    ServerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 136, 136)); // #FF8888
                }
            });
        }

        private void Button1_Click(object sender, RoutedEventArgs e)
        {
            if (_backgroundWorker1.IsBusy)
                return;

            Application.Current.Shutdown(0);
        }

        private void Button1_MouseEnter(object sender, MouseEventArgs e)
        {
            Button1.Content = _image187;
        }

        private void Button1_MouseLeave(object sender, MouseEventArgs e)
        {
            Button1.Content = _image185;
        }

        private void Button1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Button1.Content = _image188;
        }

        private void Button1_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Button1.Content = _image187;
        }

        private void Button2_Click(object sender, RoutedEventArgs e)
        {
            if (_backgroundWorker1.IsBusy)
                return;

            try
            {
                var fileName = Path.Combine(Directory.GetCurrentDirectory(), "game.exe");
                Process.Start(fileName, "start game");

                var currentProcess = Process.GetCurrentProcess();
                currentProcess.Kill();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown(ex.HResult);
            }
        }



        private string GetConfigIniPath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "CONFIG.INI");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create dummy CONFIG.INI if it doesn't exist for testing/fresh installs
                if (!File.Exists(GetConfigIniPath()))
                {
                    File.WriteAllText(GetConfigIniPath(), "FULLSCREEN=FALSE\r\nSIZE_X=1920\r\nSIZE_Y=1080\r\n");
                }

                LoadSettings();
                SettingsOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void LoadSettings()
        {
            string configPath = GetConfigIniPath();
            if (!File.Exists(configPath)) return;

            string[] lines = File.ReadAllLines(configPath);
            string fullScreen = "FALSE";
            string sizeX = "1920";
            string sizeY = "1080";

            foreach (var line in lines)
            {
                if (line.StartsWith("FULLSCREEN=", StringComparison.OrdinalIgnoreCase))
                    fullScreen = line.Split('=')[1].Trim().ToUpper();
                else if (line.StartsWith("SIZE_X=", StringComparison.OrdinalIgnoreCase))
                    sizeX = line.Split('=')[1].Trim();
                else if (line.StartsWith("SIZE_Y=", StringComparison.OrdinalIgnoreCase))
                    sizeY = line.Split('=')[1].Trim();
            }

            chkFullScreen.IsChecked = fullScreen == "TRUE";

            string resToMatch = $"{sizeX}x{sizeY}";
            
            // Find the radio button with the matching tag and check it
            foreach (var element in FindVisualChildren<RadioButton>(SettingsOverlay))
            {
                if (element.Tag?.ToString() == resToMatch)
                {
                    element.IsChecked = true;
                }
            }
        }

        private void SaveSettings()
        {
            string configPath = GetConfigIniPath();
            if (!File.Exists(configPath)) return;

            string fullScreenVal = (chkFullScreen.IsChecked == true) ? "TRUE" : "FALSE";
            string sizeX = "1920";
            string sizeY = "1080";

            foreach (var element in FindVisualChildren<RadioButton>(SettingsOverlay))
            {
                if (element.IsChecked == true && element.Tag != null)
                {
                    var parts = element.Tag.ToString().Split('x');
                    if (parts.Length == 2)
                    {
                        sizeX = parts[0];
                        sizeY = parts[1];
                    }
                    break;
                }
            }

            string[] lines = File.ReadAllLines(configPath);
            bool fsFound = false, sxFound = false, syFound = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("FULLSCREEN=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "FULLSCREEN=" + fullScreenVal;
                    fsFound = true;
                }
                else if (lines[i].StartsWith("SIZE_X=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "SIZE_X=" + sizeX;
                    sxFound = true;
                }
                else if (lines[i].StartsWith("SIZE_Y=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "SIZE_Y=" + sizeY;
                    syFound = true;
                }
            }

            // Append if not found
            var newLines = new System.Collections.Generic.List<string>(lines);
            if (!fsFound) newLines.Add("FULLSCREEN=" + fullScreenVal);
            if (!sxFound) newLines.Add("SIZE_X=" + sizeX);
            if (!syFound) newLines.Add("SIZE_Y=" + sizeY);

            File.WriteAllLines(configPath, newLines);
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        public static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t)
                {
                    yield return t;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://discord.gg/nkk56ucJzk") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WebsiteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://sites.google.com/view/shaiyaessentials/") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RankingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/ranking") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DropsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/drops") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BossesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/bosses") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load the login artwork image
                var artworkUri = new Uri("pack://application:,,,/Resources/Bitmap/LoginArtwork.png", UriKind.Absolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = artworkUri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                LoginArtwork.Source = bitmap;
            }
            catch
            {
                // Artwork not found, leave blank
            }

            LoginUsername.Text = string.Empty;
            LoginPassword.Password = string.Empty;
            LoginOverlay.Visibility = Visibility.Visible;
        }

        private void CloseLoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginOverlay.Visibility = Visibility.Collapsed;
        }

        private async void LoginSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string username = LoginUsername.Text;
            string password = LoginPassword.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter your username and password.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (await PerformLoginAsync(username, password))
            {
                // Save session if "Keep me logged in" is checked
                if (chkKeepLoggedIn.IsChecked == true)
                {
                    SaveSession(username, password);
                }

                LoginOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> PerformLoginAsync(string username, string password)
        {
            try
            {
                int factionCountry = 2; // default N/A

                var loginData = new { username = username, password = password };
                var jsonContent = new StringContent(JsonSerializer.Serialize(loginData), Encoding.UTF8, "application/json");

                var response = await _apiClient.PostAsync($"{ApiBaseUrl.TrimEnd('/')}/login.php", jsonContent);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                bool success = root.GetProperty("success").GetBoolean();
                if (!success)
                {
                    string message = root.GetProperty("message").GetString() ?? "Login failed.";
                    MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                _loggedInUserId = root.GetProperty("userId").GetString() ?? username;
                _loggedInPoints = root.GetProperty("points").GetInt32();
                factionCountry = root.GetProperty("factionCountry").GetInt32();

                // Update UI
                LoginButton.Visibility = Visibility.Collapsed;
                LoggedInPanelButton.Visibility = Visibility.Visible;
                LoggedInUsername.Text = _loggedInUserId;
                LoggedInPoints.Text = _loggedInPoints.ToString();

                // Set faction icon
                if (factionCountry == 0)
                {
                    FactionIcon.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Bitmap/faction-light.png", UriKind.Absolute));
                    FactionIcon.Visibility = Visibility.Visible;
                }
                else if (factionCountry == 1)
                {
                    FactionIcon.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Bitmap/faction-dark.png", UriKind.Absolute));
                    FactionIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    FactionIcon.Visibility = Visibility.Collapsed;
                }

                // Enable PLAY button
                Button2.Content = "PLAY";
                Button2.IsEnabled = true;
                Button2.FontSize = 18;

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API connection error:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task TryAutoLoginAsync()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return;

                byte[] encryptedBytes = File.ReadAllBytes(SessionFilePath);
                byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decryptedBytes);
                var session = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (session != null && session.ContainsKey("u") && session.ContainsKey("p"))
                {
                    await PerformLoginAsync(session["u"], session["p"]);
                }
            }
            catch
            {
                // Session file corrupted or invalid, delete it
                ClearSession();
            }
        }

        private static void SaveSession(string username, string password)
        {
            try
            {
                var session = new Dictionary<string, string> { { "u", username }, { "p", password } };
                string json = JsonSerializer.Serialize(session);
                byte[] plainBytes = Encoding.UTF8.GetBytes(json);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(SessionFilePath, encryptedBytes);
                File.SetAttributes(SessionFilePath, FileAttributes.Hidden);
            }
            catch
            {
                // Failed to save session, silently ignore
            }
        }

        private static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch { }
        }

        private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/registeracc") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProfileDropdownButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileDropdownPopup.IsOpen = !ProfileDropdownPopup.IsOpen;
        }

        private void UserPanelButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileDropdownPopup.IsOpen = false;
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/userpanel") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DonateButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileDropdownPopup.IsOpen = false;
            try
            {
                var startInfo = new ProcessStartInfo("https://google.com/donate") { UseShellExecute = true };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogOutButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileDropdownPopup.IsOpen = false;
            _loggedInUserId = string.Empty;
            _loggedInPoints = 0;

            // Clear saved session
            ClearSession();

            // Reset UI to logged-out state
            LoggedInPanelButton.Visibility = Visibility.Collapsed;
            LoginButton.Visibility = Visibility.Visible;

            // Disable PLAY button
            Button2.Content = "Login required";
            Button2.IsEnabled = false;
            Button2.FontSize = 13;
        }
    }
}

