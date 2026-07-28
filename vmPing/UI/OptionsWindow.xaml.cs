using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class OptionsWindow : Window
    {
        // Imports and constants for hiding minimize and maximize buttons.
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x10000; //maximize button
        private const int WS_MINIMIZEBOX = 0x20000; //minimize button

        public OptionsWindow()
        {
            InitializeComponent();

            PopulateGeneralOptions();
            PopulateNotificationOptions();
            PopulateEmailAlertOptions();
            PopulateAudioAlertOptions();
            PopulateLogOutputOptions();
            PopulateAdvancedOptions();
            PopulateDisplayOptions();
            PopulateLayoutOptions();
        }

        private bool? ShowError(string message, TabItem tabItem, Control control, bool isWarning = false)
        {
            // Switch to specified tab.
            tabItem?.Focus();

            // Show warning or error?
            DialogWindow errorWindow;
            if (isWarning == true)
            {
                errorWindow = DialogWindow.WarningWindow(message, "保存");
            }
            else
            {
                errorWindow = DialogWindow.ErrorWindow(message);
            }

            // Display dialog and capture result.
            errorWindow.Owner = this;
            var result = errorWindow.ShowDialog();

            // Set focus to specified control.
            control?.Focus();

            return result;
        }

        private void PopulateGeneralOptions()
        {
            string pingIntervalUnits;
            int pingIntervalDivisor;
            int pingInterval = ApplicationOptions.PingInterval;
            int pingTimeout = ApplicationOptions.PingTimeout;

            if (ApplicationOptions.PingInterval >= 3600000 && ApplicationOptions.PingInterval % 3600000 == 0)
            {
                pingIntervalUnits = "小时";
                pingIntervalDivisor = 3600000;
            }
            else if (ApplicationOptions.PingInterval >= 60000 && ApplicationOptions.PingInterval % 60000 == 0)
            {
                pingIntervalUnits = "分";
                pingIntervalDivisor = 60000;
            }
            else if (ApplicationOptions.PingInterval >= 1000 && ApplicationOptions.PingInterval % 1000 == 0)
            {
                pingIntervalUnits = "秒";
                pingIntervalDivisor = 1000;
            }
            else
            {
                pingIntervalUnits = "毫秒";
                pingIntervalDivisor = 1;
            }

            pingInterval /= pingIntervalDivisor;
            pingTimeout /= 1000;

            PingInterval.Text = pingInterval.ToString();
            PingTimeout.Text = pingTimeout.ToString();
            AlertThreshold.Text = ApplicationOptions.AlertThreshold.ToString();
            PingIntervalUnits.Text = pingIntervalUnits;

            // Get startup mode settings.
            InitialProbeCount.Text = ApplicationOptions.InitialProbeCount.ToString();
            InitialColumnCount.Text = ApplicationOptions.InitialColumnCount.ToString();
            StartupMode.SelectedIndex = (int)ApplicationOptions.InitialStartMode;
            InitialFavorite.ItemsSource = Favorite.GetTitles();
            InitialFavorite.Text = ApplicationOptions.InitialFavorite ?? string.Empty;
        }

        private void PopulateNotificationOptions()
        {
            PopupsDisabledOption.IsChecked = false;
            PopupsMinimizedOption.IsChecked = false;
            PopupsAlwaysOption.IsChecked = false;
            switch (ApplicationOptions.PopupOption)
            {
                case ApplicationOptions.PopupNotificationOption.Never:
                    PopupsDisabledOption.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.WhenMinimized:
                    PopupsMinimizedOption.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.Always:
                    PopupsAlwaysOption.IsChecked = true;
                    break;
            }
            IsAutoDismissEnabled.IsChecked = ApplicationOptions.IsAutoDismissEnabled;
            AutoDismissInterval.Text = (ApplicationOptions.AutoDismissMilliseconds / 1000).ToString();
        }

        private void PopulateEmailAlertOptions()
        {
            IsEmailAlertsEnabled.IsChecked = ApplicationOptions.IsEmailAlertEnabled;
            IsSmtpAuthenticationRequired.IsChecked = ApplicationOptions.IsEmailAuthenticationRequired;
            IsSmtpSslEnabled.IsChecked = ApplicationOptions.IsEmailSslEnabled;
            SmtpServer.Text = ApplicationOptions.EmailServer;
            SmtpPort.Text = ApplicationOptions.EmailPort;
            SmtpUsername.Text = ApplicationOptions.EmailUser;
            SmtpPassword.Password = ApplicationOptions.EmailPassword;
            EmailRecipientAddress.Text = ApplicationOptions.EmailRecipient;
            EmailFromAddress.Text = ApplicationOptions.EmailFromAddress;
        }
        private void PopulateAudioAlertOptions()
        {
            IsAudioDownAlertEnabled.IsChecked = ApplicationOptions.IsAudioDownAlertEnabled;
            AudioDownFilePath.Text = ApplicationOptions.AudioDownFilePath;
            IsAudioUpAlertEnabled.IsChecked = ApplicationOptions.IsAudioUpAlertEnabled;
            AudioUpFilePath.Text = ApplicationOptions.AudioUpFilePath;
        }

        private void PopulateLogOutputOptions()
        {
            LogPath.Text = ApplicationOptions.LogPath;
            IsLogOutputEnabled.IsChecked = ApplicationOptions.IsLogOutputEnabled;
            LogStatusChangesPath.Text = ApplicationOptions.LogStatusChangesPath;
            IsLogStatusChangesEnabled.IsChecked = ApplicationOptions.IsLogStatusChangesEnabled;
            DatabasePath.Text = ApplicationOptions.DatabasePath;
            IsDatabaseEnabled.IsChecked = ApplicationOptions.IsDatabaseEnabled;
        }

        private void PopulateAdvancedOptions()
        {
            TTL.Text = ApplicationOptions.TTL.ToString();
            DontFragment.IsChecked = ApplicationOptions.DontFragment;

            if (ApplicationOptions.UseCustomBuffer)
            {
                UseCustomPacketOption.IsChecked = true;
                PacketData.Text = Encoding.ASCII.GetString(ApplicationOptions.Buffer);
            }
            else
            {
                PacketSizeOption.IsChecked = true;
                PacketSize.Text = ApplicationOptions.Buffer.Length.ToString();
            }

            UpdateByteCount();
        }

        private void PopulateDisplayOptions()
        {
            IsAlwaysOnTopEnabled.IsChecked = ApplicationOptions.IsAlwaysOnTopEnabled;
            IsMinimizeToTrayEnabled.IsChecked = ApplicationOptions.IsMinimizeToTrayEnabled;
            IsExitToTrayEnabled.IsChecked = ApplicationOptions.IsExitToTrayEnabled;
        }

        private void PopulateLayoutOptions()
        {
            BackgroundColor_Probe_Inactive.Text = ApplicationOptions.BackgroundColor_Probe_Inactive;
            BackgroundColor_Probe_Up.Text = ApplicationOptions.BackgroundColor_Probe_Up;
            BackgroundColor_Probe_Down.Text = ApplicationOptions.BackgroundColor_Probe_Down;
            BackgroundColor_Probe_Error.Text = ApplicationOptions.BackgroundColor_Probe_Error;
            BackgroundColor_Probe_Indeterminate.Text = ApplicationOptions.BackgroundColor_Probe_Indeterminate;
            ForegroundColor_Probe_Inactive.Text = ApplicationOptions.ForegroundColor_Probe_Inactive;
            ForegroundColor_Probe_Up.Text = ApplicationOptions.ForegroundColor_Probe_Up;
            ForegroundColor_Probe_Down.Text = ApplicationOptions.ForegroundColor_Probe_Down;
            ForegroundColor_Probe_Error.Text = ApplicationOptions.ForegroundColor_Probe_Error;
            ForegroundColor_Probe_Indeterminate.Text = ApplicationOptions.ForegroundColor_Probe_Indeterminate;
            ForegroundColor_Stats_Inactive.Text = ApplicationOptions.ForegroundColor_Stats_Inactive;
            ForegroundColor_Stats_Up.Text = ApplicationOptions.ForegroundColor_Stats_Up;
            ForegroundColor_Stats_Down.Text = ApplicationOptions.ForegroundColor_Stats_Down;
            ForegroundColor_Stats_Error.Text = ApplicationOptions.ForegroundColor_Stats_Error;
            ForegroundColor_Stats_Indeterminate.Text = ApplicationOptions.ForegroundColor_Stats_Inactive;
            ForegroundColor_Alias_Inactive.Text = ApplicationOptions.ForegroundColor_Alias_Inactive;
            ForegroundColor_Alias_Up.Text = ApplicationOptions.ForegroundColor_Alias_Up;
            ForegroundColor_Alias_Down.Text = ApplicationOptions.ForegroundColor_Alias_Down;
            ForegroundColor_Alias_Error.Text = ApplicationOptions.ForegroundColor_Alias_Error;
            ForegroundColor_Alias_Indeterminate.Text = ApplicationOptions.ForegroundColor_Alias_Indeterminate;
        }


        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (SaveGeneralOptions() == false) return;
            if (SaveNotificationOptions() == false) return;
            if (SaveEmailAlertOptions() == false) return;
            if (SaveAudioAlertOptions() == false) return;
            if (SaveLogOutputOptions() == false) return;
            if (SaveAdvancedOptions() == false) return;
            if (SaveLayoutOptions() == false) return;
            if (SaveDisplayOptions() == false) return;

            if (SaveAsDefaults.IsChecked == true)
            {
                Configuration.Save();
            }

            DialogResult = true;
        }

        private bool SaveGeneralOptions()
        {
            if (PingInterval.Text.Length == 0)
            {
                ShowError("请输入有效的 Ping 间隔。", GeneralTab, PingInterval);
                return false;
            }
            else if (PingTimeout.Text.Length == 0)
            {
                ShowError("请输入有效的 Ping 超时。", GeneralTab, PingTimeout);
                return false;
            }
            else if (AlertThreshold.Text.Length == 0)
            {
                ShowError("请输入有效的告警阈值。", GeneralTab, AlertThreshold);
                return false;
            }

            // Ping interval.
            int pingInterval;
            int multiplier = 1000;

            switch (PingIntervalUnits.Text)
            {
                case "毫秒":
                    multiplier = 1;
                    break;
                case "秒":
                    multiplier = 1000;
                    break;
                case "分":
                    multiplier = 1000 * 60;
                    break;
                case "小时":
                    multiplier = 1000 * 60 * 60;
                    break;
            }

            if (int.TryParse(PingInterval.Text, out pingInterval) && pingInterval > 0 && pingInterval <= 86400)
            {
                pingInterval *= multiplier;
            }
            else
            {
                pingInterval = Constants.DefaultInterval;
            }
            ApplicationOptions.PingInterval = pingInterval;

            // Ping timeout.
            int pingTimeout;
            if (int.TryParse(PingTimeout.Text, out pingTimeout) && pingTimeout > 0 && pingTimeout <= 60)
            {
                pingTimeout *= 1000;
            }
            else
            {
                pingTimeout = Constants.DefaultTimeout;
            }
            ApplicationOptions.PingTimeout = pingTimeout;

            // Alert threshold.
            int alertThreshold;

            var isThresholdValid = int.TryParse(AlertThreshold.Text, out alertThreshold) && alertThreshold > 0 && alertThreshold <= 60;
            if (!isThresholdValid)
            {
                alertThreshold = 1;
            }

            ApplicationOptions.AlertThreshold = alertThreshold;

            // Startup mode.
            ApplicationOptions.InitialStartMode = (ApplicationOptions.StartMode)StartupMode.SelectedIndex;
            switch (StartupMode.SelectedIndex)
            {
                case ((int)ApplicationOptions.StartMode.Blank):
                case ((int)ApplicationOptions.StartMode.MultiInput):
                    // Initial probe count.
                    int count;
                    if (int.TryParse(InitialProbeCount.Text, out count))
                    {
                        if (count < 1)
                        {
                            count = 1;
                        }
                        else if (count > 20)
                        {
                            count = 2;
                        }
                    }
                    else
                    {
                        count = 2;
                    }
                    ApplicationOptions.InitialProbeCount = count;

                    // Initial column count.
                    if (int.TryParse(InitialColumnCount.Text, out count))
                    {
                        if (count < 1)
                        {
                            count = 1;
                        }
                        else if (count > 10)
                        {
                            count = 10;
                        }
                    }
                    else
                    {
                        count = 2;
                    }
                    ApplicationOptions.InitialColumnCount = count;
                    break;
                case ((int)ApplicationOptions.StartMode.Favorite):
                    // Initial favorite.
                    ApplicationOptions.InitialFavorite = InitialFavorite.Text;
                    break;
            }

            return true;
        }

        private bool SaveNotificationOptions()
        {
            if (IsAutoDismissEnabled.IsChecked == true)
            {
                if (int.TryParse(AutoDismissInterval.Text, out int result) && result > 0 && result < 100)
                {
                    ApplicationOptions.AutoDismissMilliseconds = result * 1000;
                    ApplicationOptions.IsAutoDismissEnabled = true;
                }
                else
                {
                    ShowError("请输入有效的自动关闭间隔秒数。", PopupAlertsTab, AutoDismissInterval);
                    return false;
                }
            }
            else
            {
                ApplicationOptions.IsAutoDismissEnabled = false;
            }

            if (PopupsMinimizedOption.IsChecked == true)
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.WhenMinimized;
            }
            else if (PopupsAlwaysOption.IsChecked == true)
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Always;
            }
            else
            {
                ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Never;
            }

            return true;
        }

        private bool SaveAdvancedOptions()
        {
            // Validate input.

            var regex = new Regex("^\\d+$");

            // Validate TTL.
            if (!regex.IsMatch(TTL.Text) || int.Parse(TTL.Text) < 1 || int.Parse(TTL.Text) > 255)
            {
                ShowError("请输入有效的生存时间 (TTL)，范围 1 到 255。", AdvancedTab, TTL);
                return false;
            }

            // Apply TTL.
            ApplicationOptions.TTL = int.Parse(TTL.Text);

            // Validate packet size.
            if (PacketSizeOption.IsChecked == true)
            {
                if (!regex.IsMatch(PacketSize.Text) || int.Parse(PacketSize.Text) < 0 || int.Parse(PacketSize.Text) > 65500)
                {
                    ShowError("请输入有效的数据大小，范围 0 到 65,500。", AdvancedTab, PacketSize);
                    return false;
                }

                // Apply packet size.
                ApplicationOptions.Buffer = new byte[int.Parse(PacketSize.Text)];
                ApplicationOptions.UseCustomBuffer = false;

                // Fill buffer with default text.
                if (ApplicationOptions.Buffer.Length >= 33)
                {
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes(Constants.DefaultIcmpData), 0, ApplicationOptions.Buffer, 0, 33);
                }
            }
            else
            {
                // Use custom packet data.
                ApplicationOptions.Buffer = Encoding.ASCII.GetBytes(PacketData.Text);
                ApplicationOptions.UseCustomBuffer = true;
            }

            // Apply fragment / don't fragment option.
            if (DontFragment.IsChecked == true)
            {
                ApplicationOptions.DontFragment = true;
            }
            else
            {
                ApplicationOptions.DontFragment = false;
            }

            // Update ping options (TTL / Don't fragment settings)
            ApplicationOptions.UpdatePingOptions();

            return true;
        }

        private bool SaveEmailAlertOptions()
        {
            // Validate input.
            if (IsEmailAlertsEnabled.IsChecked == true)
            {
                var regex = new Regex("^\\d+$");

                if (SmtpServer.Text.Length == 0)
                {
                    ShowError("请输入有效的 SMTP 服务器地址。", EmailAlertsTab, SmtpServer);
                    return false;
                }
                else if (SmtpPort.Text.Length == 0 || !regex.IsMatch(SmtpPort.Text))
                {
                    ShowError("请输入有效的 SMTP 服务器端口号。", EmailAlertsTab, SmtpPort);
                    return false;
                }
                else if (EmailRecipientAddress.Text.Length == 0)
                {
                    ShowError("请输入有效的收件人邮箱地址。该地址将接收来自 vmPing 的邮件提醒。", EmailAlertsTab, EmailRecipientAddress);
                    return false;
                }
                else if (EmailFromAddress.Text.Length == 0)
                {
                    ShowError("请输入有效的发件人地址。该地址将显示为所有提醒的发件人。", EmailAlertsTab, EmailFromAddress);
                    return false;
                }
                if (IsSmtpAuthenticationRequired.IsChecked == true)
                {
                    ApplicationOptions.IsEmailAuthenticationRequired = true;
                    if (SmtpUsername.Text.Length == 0)
                    {
                        ShowError("请输入有效的邮件服务器用户名。", EmailAlertsTab, SmtpUsername);
                        return false;
                    }
                }
                else
                {
                    ApplicationOptions.IsEmailAuthenticationRequired = false;
                    SmtpUsername.Text = string.Empty;
                    SmtpPassword.Password = string.Empty;
                }

                ApplicationOptions.IsEmailAlertEnabled = true;
                ApplicationOptions.EmailServer = SmtpServer.Text;
                ApplicationOptions.EmailPort = SmtpPort.Text;
                ApplicationOptions.EmailUser = SmtpUsername.Text;
                ApplicationOptions.EmailPassword = SmtpPassword.Password;
                ApplicationOptions.EmailRecipient = EmailRecipientAddress.Text;
                ApplicationOptions.EmailFromAddress = EmailFromAddress.Text;
                ApplicationOptions.IsEmailSslEnabled = IsSmtpSslEnabled.IsChecked == true ? true : false;

                return true;
            }
            else
            {
                ApplicationOptions.IsEmailAlertEnabled = false;
                return true;
            }
        }

        private bool SaveAudioAlertOptions()
        {
            if (IsAudioDownAlertEnabled.IsChecked == true)
            {
                try
                {
                    if (Path.GetFileName(AudioDownFilePath.Text).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        !File.Exists(AudioDownFilePath.Text) ||
                        Path.GetFileName(AudioDownFilePath.Text).Length < 1)
                    {
                        throw new Exception();
                    }
                }
                catch
                {
                    ShowError("指定的路径不存在。请输入有效的路径。", AudioAlertTab, AudioDownFilePath);
                    return false;
                }
                ApplicationOptions.IsAudioDownAlertEnabled = true;
                ApplicationOptions.AudioDownFilePath = AudioDownFilePath.Text;
            }
            else
            {
                ApplicationOptions.IsAudioDownAlertEnabled = false;
            }

            if (IsAudioUpAlertEnabled.IsChecked == true)
            {
                try
                {
                    if (Path.GetFileName(AudioUpFilePath.Text).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        !File.Exists(AudioUpFilePath.Text) ||
                        Path.GetFileName(AudioUpFilePath.Text).Length < 1)
                    {
                        throw new Exception();
                    }
                }
                catch
                {
                    ShowError("指定的路径不存在。请输入有效的路径。", AudioAlertTab, AudioUpFilePath);
                    return false;
                }
                ApplicationOptions.IsAudioUpAlertEnabled = true;
                ApplicationOptions.AudioUpFilePath = AudioUpFilePath.Text;
            }
            else
            {
                ApplicationOptions.IsAudioUpAlertEnabled = false;
            }

            return true;
        }

        private bool SaveLogOutputOptions()
        {
            if (IsLogOutputEnabled.IsChecked == true)
            {
                if (!Directory.Exists(LogPath.Text))
                {
                    ShowError("指定的路径不存在。请输入有效的路径。", LogOutputTab, LogPath);
                    return false;
                }

                ApplicationOptions.IsLogOutputEnabled = true;
                ApplicationOptions.LogPath = LogPath.Text;
            }
            else
            {
                ApplicationOptions.IsLogOutputEnabled = false;
            }

            if (IsLogStatusChangesEnabled.IsChecked == true)
            {
                try
                {
                    if (Path.GetFileName(LogStatusChangesPath.Text).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        !Directory.Exists(Path.GetDirectoryName(LogStatusChangesPath.Text)) ||
                        Path.GetFileName(LogStatusChangesPath.Text).Length < 1)
                    {
                        throw new Exception();
                    }
                }
                catch
                {
                    ShowError("指定的路径不存在。请输入有效的路径。", LogOutputTab, LogStatusChangesPath);
                    return false;
                }

                ApplicationOptions.IsLogStatusChangesEnabled = true;
                ApplicationOptions.LogStatusChangesPath = LogStatusChangesPath.Text;
            }
            else
            {
                ApplicationOptions.IsLogStatusChangesEnabled = false;
            }

            if (IsDatabaseEnabled.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(DatabasePath.Text))
                {
                    ShowError("请输入有效的数据库路径。", LogOutputTab, DatabasePath);
                    return false;
                }

                var dbDir = Path.GetDirectoryName(DatabasePath.Text);
                if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
                {
                    try { Directory.CreateDirectory(dbDir); }
                    catch
                    {
                        ShowError("无法创建数据库目录。请输入有效的路径。", LogOutputTab, DatabasePath);
                        return false;
                    }
                }

                ApplicationOptions.IsDatabaseEnabled = true;
                ApplicationOptions.DatabasePath = DatabasePath.Text;
                DatabaseService.Initialize(ApplicationOptions.DatabasePath);
            }
            else
            {
                ApplicationOptions.IsDatabaseEnabled = false;
            }

            return true;
        }

        private bool SaveDisplayOptions()
        {
            ApplicationOptions.IsAlwaysOnTopEnabled = IsAlwaysOnTopEnabled.IsChecked == true;
            ApplicationOptions.IsMinimizeToTrayEnabled = IsMinimizeToTrayEnabled.IsChecked == true;
            ApplicationOptions.IsExitToTrayEnabled = IsExitToTrayEnabled.IsChecked == true;

            return true;
        }

        private bool SaveLayoutOptions()
        {
            // Validate input.
            foreach (var control in ColorsDockPanel.GetChildren())
            {
                if (control is TextBox box)
                {
                    if (!Util.IsValidHtmlColor(box.Text))
                    {
                        ShowError("请输入有效的 HTML 颜色代码。支持的格式为 #RGB、#RRGGBB 和 #AARRGGBB。示例：#3266CF", LayoutTab, box);
                        box.SelectAll();

                        return false;
                    }
                }
            }

            ApplicationOptions.BackgroundColor_Probe_Inactive = BackgroundColor_Probe_Inactive.Text;
            ApplicationOptions.BackgroundColor_Probe_Up = BackgroundColor_Probe_Up.Text;
            ApplicationOptions.BackgroundColor_Probe_Down = BackgroundColor_Probe_Down.Text;
            ApplicationOptions.BackgroundColor_Probe_Indeterminate = BackgroundColor_Probe_Indeterminate.Text;
            ApplicationOptions.BackgroundColor_Probe_Error = BackgroundColor_Probe_Error.Text;
            ApplicationOptions.ForegroundColor_Probe_Inactive = ForegroundColor_Probe_Inactive.Text;
            ApplicationOptions.ForegroundColor_Probe_Up = ForegroundColor_Probe_Up.Text;
            ApplicationOptions.ForegroundColor_Probe_Down = ForegroundColor_Probe_Down.Text;
            ApplicationOptions.ForegroundColor_Probe_Indeterminate = ForegroundColor_Probe_Indeterminate.Text;
            ApplicationOptions.ForegroundColor_Probe_Error = ForegroundColor_Probe_Error.Text;
            ApplicationOptions.ForegroundColor_Stats_Inactive = ForegroundColor_Stats_Inactive.Text;
            ApplicationOptions.ForegroundColor_Stats_Up = ForegroundColor_Stats_Up.Text;
            ApplicationOptions.ForegroundColor_Stats_Down = ForegroundColor_Stats_Down.Text;
            ApplicationOptions.ForegroundColor_Stats_Indeterminate = ForegroundColor_Stats_Indeterminate.Text;
            ApplicationOptions.ForegroundColor_Stats_Error = ForegroundColor_Stats_Error.Text;
            ApplicationOptions.ForegroundColor_Alias_Inactive = ForegroundColor_Alias_Inactive.Text;
            ApplicationOptions.ForegroundColor_Alias_Up = ForegroundColor_Alias_Up.Text;
            ApplicationOptions.ForegroundColor_Alias_Down = ForegroundColor_Alias_Down.Text;
            ApplicationOptions.ForegroundColor_Alias_Indeterminate = ForegroundColor_Alias_Indeterminate.Text;
            ApplicationOptions.ForegroundColor_Alias_Error = ForegroundColor_Alias_Error.Text;

            return true;
        }


        private void NumericTextbox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9.-]+");
            if (regex.IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        private void HtmlColor_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[#a-fA-F0-9]");
            if (!regex.IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        private void EmailRecipientAddress_LostFocus(object sender, RoutedEventArgs e)
        {
            if (EmailFromAddress.Text.Length == 0 && EmailRecipientAddress.Text.IndexOf('@') >= 0)
            {
                EmailFromAddress.Text = "vmPing" + EmailRecipientAddress.Text.Substring(EmailRecipientAddress.Text.IndexOf('@'));
            }
        }

        private void IsEmailAlertsEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (IsEmailAlertsEnabled.IsChecked == true && SmtpServer.Text.Length == 0)
            {
                SmtpServer.Focus();
            }
        }

        private void IsSmtpAuthenticationRequired_Click(object sender, RoutedEventArgs e)
        {
            if (IsSmtpAuthenticationRequired.IsChecked == true)
            {
                SmtpUsername.Focus();
            }
        }

        private async void TestEmail_Click(object sender, RoutedEventArgs e)
        {
            TestEmailButton.IsEnabled = false;
            TestEmailButton.Content = "发送中...";
            var serverAddress = SmtpServer.Text;
            var serverPort = SmtpPort.Text;
            var isSslEnabled = IsSmtpSslEnabled.IsChecked == true;
            var isAuthRequired = IsSmtpAuthenticationRequired.IsChecked == true;
            var username = SmtpUsername.Text;
            var password = SmtpPassword.SecurePassword;
            var mailFrom = EmailFromAddress.Text;
            var mailRecipient = EmailRecipientAddress.Text;

            await Task.Run(() =>
            {
                try
                {
                    Util.SendTestEmail(
                        serverAddress,
                        serverPort,
                        isSslEnabled,
                        isAuthRequired,
                        username,
                        password,
                        mailFrom,
                        mailRecipient);
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            if (IsLoaded)
                            {
                                var dialogWindow = new DialogWindow(
                                    DialogWindow.DialogIcon.Info,
                                    "邮件测试",
                                    "测试邮件已发送。",
                                    "确定",
                                    false)
                                {
                                    Owner = this
                                };
                                dialogWindow.ShowDialog();
                            }
                        }));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            if (IsLoaded)
                            {
                                ShowError($"测试失败：{ex.Message}", EmailAlertsTab, TestEmailButton);
                            }
                        }));
                }
            });
            TestEmailButton.IsEnabled = true;
            TestEmailButton.Content = "测试";
        }

        private void BrowseLogPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择日志文件的存放位置。";
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    LogPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseLogStatusChangesPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择日志文件的存放位置。";
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    LogStatusChangesPath.Text = dialog.SelectedPath + "\\vmping-status.txt";
                }
            }
        }

        private void BrowseDatabasePath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                dialog.Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*";
                dialog.FileName = "vmping.db";
                dialog.Title = "选择数据库文件保存位置";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    DatabasePath.Text = dialog.FileName;
                }
            }
        }

        private void AudioDownBrowse_Click(object sender, RoutedEventArgs e)
        {
            AudioFileBrowse(AudioDownFilePath);
        }

        private void AudioUpBrowse_Click(object sender, RoutedEventArgs e)
        {
            AudioFileBrowse(AudioUpFilePath);
        }

        private void AudioFileBrowse(TextBox tb)
        {
            using (var audiofileDialog = new System.Windows.Forms.OpenFileDialog())
            {
                audiofileDialog.Title = "选择音频文件";
                audiofileDialog.RestoreDirectory = true;
                audiofileDialog.Multiselect = false;
                audiofileDialog.Filter = "WAV 文件 (*.wav)|*.wav|所有文件|*.*";
                audiofileDialog.DefaultExt = ".wav";

                if (audiofileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    tb.Text = audiofileDialog.FileName;
                }
            }
        }

        private void AudioDownPlay_Click(object sender, RoutedEventArgs e)
        {
            AudioFilePlay(AudioDownFilePath.Text);
        }

        private void AudioUpPlay_Click(object sender, RoutedEventArgs e)
        {
            AudioFilePlay(AudioUpFilePath.Text);
        }

        private void AudioFilePlay(string path)
        {
            try
            {
                using (var player = new SoundPlayer(path))
                {
                    player.Play();
                }
            }
            catch
            {
                ShowError("无法播放音频文件。", AudioAlertTab, AudioAlertTab);
            }
        }

        private void IsAudioDownAlertEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (AudioDownFilePath.Text.Length == 0)
            {
                if (File.Exists(Environment.ExpandEnvironmentVariables(Constants.DefaultAudioDownFilePath)))
                {
                    AudioDownFilePath.Text = Environment.ExpandEnvironmentVariables(Constants.DefaultAudioDownFilePath);
                }
            }
        }

        private void IsAudioUpAlertEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (AudioUpFilePath.Text.Length == 0)
            {
                if (File.Exists(Environment.ExpandEnvironmentVariables(Constants.DefaultAudioUpFilePath)))
                {
                    AudioUpFilePath.Text = Environment.ExpandEnvironmentVariables(Constants.DefaultAudioUpFilePath);
                }
            }
        }

        private void UpdateByteCount()
        {
            var regex = new Regex("^\\d+$");
            if (PacketSizeOption.IsChecked == true)
            {
                if (PacketSize != null && regex.IsMatch(PacketSize.Text))
                {
                    Bytes.Text = (int.Parse(PacketSize.Text) + 28).ToString();
                }
                else
                {
                    Bytes.Text = "?";
                }
            }
            else
            {
                Bytes.Text = (PacketData.Text.Length + 28).ToString();
            }
        }

        private void PacketData_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateByteCount();
        }

        private void PacketSizeOption_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                UpdateByteCount();
            }
        }

        private void UseCustomPacketOption_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                UpdateByteCount();
            }
        }

        private void RestoreDefaultColors_Click(object sender, RoutedEventArgs e)
        {
            BackgroundColor_Probe_Inactive.Text = Constants.Color_Probe_Background_Inactive;
            BackgroundColor_Probe_Up.Text = Constants.Color_Probe_Background_Up;
            BackgroundColor_Probe_Down.Text = Constants.Color_Probe_Background_Down;
            BackgroundColor_Probe_Error.Text = Constants.Color_Probe_Background_Error;
            BackgroundColor_Probe_Indeterminate.Text = Constants.Color_Probe_Background_Indeterminate;
            ForegroundColor_Probe_Inactive.Text = Constants.Color_Probe_Foreground_Inactive;
            ForegroundColor_Probe_Up.Text = Constants.Color_Probe_Foreground_Up;
            ForegroundColor_Probe_Down.Text = Constants.Color_Probe_Foreground_Down;
            ForegroundColor_Probe_Error.Text = Constants.Color_Probe_Foreground_Error;
            ForegroundColor_Probe_Indeterminate.Text = Constants.Color_Probe_Foreground_Indeterminate;
            ForegroundColor_Stats_Inactive.Text = Constants.Color_Statistics_Foreground_Inactive;
            ForegroundColor_Stats_Up.Text = Constants.Color_Statistics_Foreground_Up;
            ForegroundColor_Stats_Down.Text = Constants.Color_Statistics_Foreground_Down;
            ForegroundColor_Stats_Error.Text = Constants.Color_Statistics_Foreground_Error;
            ForegroundColor_Stats_Indeterminate.Text = Constants.Color_Statistics_Foreground_Inactive;
            ForegroundColor_Alias_Inactive.Text = Constants.Color_Alias_Foreground_Inactive;
            ForegroundColor_Alias_Up.Text = Constants.Color_Alias_Foreground_Up;
            ForegroundColor_Alias_Down.Text = Constants.Color_Alias_Foreground_Down;
            ForegroundColor_Alias_Error.Text = Constants.Color_Alias_Foreground_Error;
            ForegroundColor_Alias_Indeterminate.Text = Constants.Color_Alias_Foreground_Indeterminate;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Hide minimize and maximize buttons.
            IntPtr _windowHandle = new WindowInteropHelper(this).Handle;
            if (_windowHandle == null)
            {
                return;
            }

            SetWindowLong(_windowHandle, GWL_STYLE, GetWindowLong(_windowHandle, GWL_STYLE) & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }
    }
}
