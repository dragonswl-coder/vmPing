# DingTalk Robot Alerts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send a DingTalk markdown message via custom robot webhook whenever a monitored host changes status, configurable from a new 钉钉 Options tab placed below the Email Alerts tab.

**Architecture:** Mirrors the existing Email Alerts feature across the same six files: option properties (ApplicationOptions), XML persistence with AES-encrypted secret (Configuration), a new Options tab (OptionsWindow.xaml + code-behind), the HTTP send logic with optional HMAC-SHA256 signing (Util), and a trigger call in the probe status-change handler (Probe-Util).

**Tech Stack:** .NET Framework 4.7.2 WPF (non-SDK csproj). DingTalk custom robot webhook API. No new NuGet packages required (HttpWebRequest + HMACSHA256 are in the framework).

**Build command (close vmPing.exe before building):**
```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild vmPing.sln /t:Build /p:Configuration=Debug /v:minimal /nologo
```
A clean compile shows zero `error CS` lines. If `vmPing.exe` is running, expect only `MSB3021`/`MSB3027` file-lock errors (copy step) - those are NOT compile errors. Kill the process and rebuild to confirm.

**No test framework:** This is a WPF app with no automated tests. Each task verifies via compile, and the final task does manual end-to-end verification.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `vmPing/Classes/ApplicationOptions.cs` | 3 new static properties for DingTalk config |
| `vmPing/Classes/Util.cs` | `SendDingtalk` (async, silent) + `SendTestDingtalk` (sync, throws) + private helpers (signing, HTTP POST, JSON escape, status color) |
| `vmPing/Classes/Configuration.cs` | Save/load 3 keys; secret AES-encrypted like EmailPassword |
| `vmPing/UI/OptionsWindow.xaml` | New 钉钉 TabItem between 邮件 and 声音 |
| `vmPing/UI/OptionsWindow.xaml.cs` | Populate / Save / Test handlers |
| `vmPing/Classes/Probe-Util.cs` | Trigger `Util.SendDingtalk` in `OnStatusChange` |

---

## Task 1: Add DingTalk option properties

**Files:**
- Modify: `vmPing/Classes/ApplicationOptions.cs` (after line 59, the `EmailFromAddress` property)

- [ ] **Step 1: Add the three properties**

In `ApplicationOptions.cs`, find this block (lines 50-59):

```csharp
        // Email notifications.
        public static bool IsEmailAlertEnabled { get; set; } = false;
        public static bool IsEmailAuthenticationRequired { get; set; } = false;
        public static bool IsEmailSslEnabled { get; set; } = false;
        public static string EmailServer { get; set; }
        public static string EmailUser { get; set; }
        public static string EmailPassword { get; set; }
        public static string EmailPort { get; set; } = "25";
        public static string EmailRecipient { get; set; }
        public static string EmailFromAddress { get; set; }
```

Insert immediately AFTER it (before the `// Audio alerts.` comment on line 61):

```csharp

        // DingTalk notifications.
        public static bool IsDingtalkAlertEnabled { get; set; } = false;
        public static string DingtalkWebhookUrl { get; set; }
        public static string DingtalkSecret { get; set; }
```

- [ ] **Step 2: Compile to verify**

Run the build command. Expected: zero `error CS` lines. (The properties are unused so far - that is fine.)

- [ ] **Step 3: Commit**

```powershell
git add vmPing/Classes/ApplicationOptions.cs
git commit -m "feat: add DingTalk alert option properties"
```

## Task 2: Add DingTalk send methods and helpers to Util.cs

**Files:**
- Modify: `vmPing/Classes/Util.cs` (add `using System.Threading.Tasks;`; add methods after `SendTestEmail`, before `ShowError`)

- [ ] **Step 1: Add the missing using**

The existing usings in `Util.cs` are:

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using vmPing.Properties;
```

Add `using System.Threading.Tasks;` after `using System.Text;`. (`HMACSHA256` is in `System.Security.Cryptography` - already present. `HttpWebRequest`/`WebRequest`/`HttpWebResponse` are in `System.Net` - already present. `DateTimeOffset.ToUnixTimeMilliseconds()` is in `System` - already present, available since .NET 4.6.)

- [ ] **Step 2: Add the new methods**

`SendTestEmail` ends at line 89. Insert the following methods immediately AFTER its closing brace (before `public static void ShowError`):

```csharp
        public static async void SendDingtalk(string status, string hostname, string alias)
        {
            try
            {
                var affectedLongName = string.IsNullOrWhiteSpace(alias) ? hostname : alias + " (" + hostname + ")";
                var color = GetDingtalkStatusColor(status);
                var now = DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToLongTimeString();
                var text = "### vmPing 状态变化\n\n**主机**: " + affectedLongName +
                    "\n**状态**: <font color=\"" + color + "\">" + status + "</font>\n**时间**: " + now;
                var json = "{\"msgtype\":\"markdown\",\"markdown\":{\"title\":\"vmPing 状态变化\",\"text\":\"" +
                    JsonEscape(text) + "\"}}";
                await Task.Run(() => PostDingtalkMessage(
                    ApplicationOptions.DingtalkWebhookUrl,
                    ApplicationOptions.DingtalkSecret,
                    json));
            }
            catch
            {
                // Silently ignore errors - mirrors SendEmail behavior.
            }
        }

        public static void SendTestDingtalk(string webhookUrl, string secret)
        {
            var now = DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToLongTimeString();
            var text = "### vmPing 钉钉机器人测试\n\n这是 vmPing 于 " + now + " 发送的测试消息";
            var json = "{\"msgtype\":\"markdown\",\"markdown\":{\"title\":\"vmPing 测试\",\"text\":\"" +
                JsonEscape(text) + "\"}}";
            var response = PostDingtalkMessage(webhookUrl, secret, json);
            // DingTalk returns {"errcode":0,"errmsg":"ok"} on success.
            if (response.Contains("\"errcode\":0") == false)
            {
                throw new Exception("钉钉返回错误：" + response);
            }
        }

        private static string GetDingtalkStatusColor(string status)
        {
            switch (status)
            {
                case "离线":
                    return "#FF0000";
                case "在线":
                case "正常延迟":
                    return "#008000";
                case "错误":
                case "高延迟":
                    return "#FFA500";
                default:
                    return "#000000";
            }
        }

        private static string BuildSignedDingtalkUrl(string webhookUrl, string secret)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var stringToSign = timestamp + "\n" + secret;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var sign = Uri.EscapeDataString(
                    Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))));
                return webhookUrl + "&timestamp=" + timestamp + "&sign=" + sign;
            }
        }

        private static string PostDingtalkMessage(string webhookUrl, string secret, string jsonBody)
        {
            var url = string.IsNullOrEmpty(secret) ? webhookUrl : BuildSignedDingtalkUrl(webhookUrl, secret);
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u" + ((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
```

- [ ] **Step 3: Compile to verify**

Run the build command. Expected: zero `error CS` lines. The new methods reference `ApplicationOptions.DingtalkWebhookUrl` / `DingtalkSecret` which exist from Task 1.

- [ ] **Step 4: Commit**

```powershell
git add vmPing/Classes/Util.cs
git commit -m "feat: add DingTalk send methods with signing and JSON helpers"
```

## Task 3: Persist DingTalk settings in Configuration.cs

**Files:**
- Modify: `vmPing/Classes/Configuration.cs` (save section near line 184; load section near line 491)

- [ ] **Step 1: Add save nodes**

Find the email save block (lines 183-184):

```csharp
                Node("EmailRecipient", ApplicationOptions.EmailRecipient),
                Node("EmailFromAddress", ApplicationOptions.EmailFromAddress),
```

Insert immediately AFTER line 184 (before the `Node("IsAudioUpAlertEnabled", ...)` line):

```csharp
                Node("IsDingtalkAlertEnabled", ApplicationOptions.IsDingtalkAlertEnabled),
                Node("DingtalkWebhookUrl", ApplicationOptions.DingtalkWebhookUrl),
                Node("DingtalkSecret", string.IsNullOrWhiteSpace(ApplicationOptions.DingtalkSecret)
                    ? string.Empty
                    : Util.EncryptStringAES(ApplicationOptions.DingtalkSecret)),
```

- [ ] **Step 2: Add load keys**

Find the email secret load block (lines 485-491):

```csharp
            if (options.TryGetValue("EmailPassword", out optionValue))
            {
                if (optionValue.Length > 0)
                {
                    ApplicationOptions.EmailPassword = Util.DecryptStringAES(optionValue);
                }
            }
```

Insert immediately AFTER its closing brace (after line 491, before the `IsAlwaysOnTopEnabled` block):

```csharp
            if (options.TryGetValue("IsDingtalkAlertEnabled", out optionValue))
            {
                ApplicationOptions.IsDingtalkAlertEnabled = bool.Parse(optionValue);
            }
            if (options.TryGetValue("DingtalkWebhookUrl", out optionValue))
            {
                ApplicationOptions.DingtalkWebhookUrl = optionValue;
            }
            if (options.TryGetValue("DingtalkSecret", out optionValue))
            {
                if (optionValue.Length > 0)
                {
                    ApplicationOptions.DingtalkSecret = Util.DecryptStringAES(optionValue);
                }
            }
```

- [ ] **Step 3: Compile to verify**

Run the build command. Expected: zero `error CS` lines.

- [ ] **Step 4: Commit**

```powershell
git add vmPing/Classes/Configuration.cs
git commit -m "feat: persist DingTalk settings (encrypted secret) in config"
```

## Task 4: Add the 钉钉 Options tab UI

**Files:**
- Modify: `vmPing/UI/OptionsWindow.xaml` (insert a new TabItem between the 邮件 tab and the 声音 tab)

- [ ] **Step 1: Insert the new TabItem**

The 邮件 tab closes at line 465 (`</TabItem>`) and the 声音 tab opens at line 468. Find this boundary:

```xml
            </TabItem>

            <!-- Tab: Audio Alerts -->
            <TabItem Header="声音" Name="AudioAlertTab">
```

Insert the following new tab BETWEEN `</TabItem>` and `<!-- Tab: Audio Alerts -->`:

```xml
            <!-- Tab: DingTalk Alerts -->
            <TabItem Header="钉钉" Name="DingtalkAlertsTab">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <DockPanel Margin="20,0,20,15">

                        <!-- Header -->
                        <TextBlock DockPanel.Dock="Top"
                                   Text="钉钉提醒"
                                   Style="{StaticResource OptionHeaderTextStyle}"/>

                        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,15,0,15">
                            <CheckBox DockPanel.Dock="Top"
                                      Name="IsDingtalkAlertsEnabled"
                                      Content="启用钉钉提醒"
                                      IsChecked="True"/>
                            <Label Style="{StaticResource LabelToolTip}">
                                启用后，每当主机状态发生变化时通过钉钉机器人发送提醒。
                            </Label>
                        </StackPanel>

                        <DockPanel LastChildFill="False"
                                   DockPanel.Dock="Top"
                                   Margin="0"
                                   Visibility="{Binding IsChecked, ElementName=IsDingtalkAlertsEnabled, Converter={StaticResource BooleanToVisibilityConverter}}">
                            <DockPanel.Resources>
                                <Style TargetType="{x:Type TextBlock}" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="HorizontalAlignment" Value="Right"/>
                                    <Setter Property="Margin" Value="0,0,12,0"/>
                                </Style>
                                <Style TargetType="{x:Type DockPanel}">
                                    <Setter Property="Margin" Value="0,3,0,3"/>
                                    <Setter Property="DockPanel.Dock" Value="Top"/>
                                </Style>
                            </DockPanel.Resources>

                            <!-- Robot configuration subheading -->
                            <TextBlock DockPanel.Dock="Top"
                                       Text="机器人配置"
                                       Style="{StaticResource OptionSubHeaderTextStyle}"/>

                            <!-- Webhook URL -->
                            <DockPanel>
                                <Grid Width="120">
                                    <TextBlock Text="Webhook 地址"/>
                                </Grid>
                                <TextBox Name="DingtalkWebhookUrl"/>
                            </DockPanel>

                            <!-- Secret -->
                            <DockPanel>
                                <Grid Width="120">
                                    <TextBlock Text="加签密钥"/>
                                </Grid>
                                <PasswordBox Name="DingtalkSecret"/>
                            </DockPanel>
                            <Label DockPanel.Dock="Top"
                                   Style="{StaticResource LabelToolTip}"
                                   Margin="120,2,0,0">
                                可选。留空使用关键词/IP白名单模式；填写则使用加签验证。
                            </Label>

                            <!-- Test button -->
                            <Button Style="{StaticResource ButtonStandardStyle}"
                                    Name="TestDingtalkButton"
                                    Visibility="{Binding IsChecked, ElementName=IsDingtalkAlertsEnabled, Converter={StaticResource BooleanToVisibilityConverter}}"
                                    DockPanel.Dock="Top"
                                    Click="TestDingtalk_Click"
                                    Width="85"
                                    Margin="0,18,0,0"
                                    Content="测试"
                                    HorizontalAlignment="Left"/>
                        </DockPanel>
                    </DockPanel>
                </ScrollViewer>
            </TabItem>
```

- [ ] **Step 2: Compile to verify**

Run the build command. Expected: zero `error CS`/`error MC` lines. (The `Click="TestDingtalk_Click"` handler does not exist yet - WPF resolves event handlers at runtime, so XAML compiles fine. Do NOT click Test until Task 5 is done.)

- [ ] **Step 3: Commit**

```powershell
git add vmPing/UI/OptionsWindow.xaml
git commit -m "feat: add DingTalk alerts Options tab UI"
```

## Task 5: Add OptionsWindow code-behind handlers

**Files:**
- Modify: `vmPing/UI/OptionsWindow.xaml.cs` (constructor populate call; OK_Click save call; three new methods)

- [ ] **Step 1: Add populate call in constructor**

Find the constructor (lines 31-34):

```csharp
            PopulateGeneralOptions();
            PopulateNotificationOptions();
            PopulateEmailAlertOptions();
            PopulateAudioAlertOptions();
```

Insert `PopulateDingtalkAlertOptions();` after `PopulateEmailAlertOptions();`:

```csharp
            PopulateGeneralOptions();
            PopulateNotificationOptions();
            PopulateEmailAlertOptions();
            PopulateDingtalkAlertOptions();
            PopulateAudioAlertOptions();
```

- [ ] **Step 2: Add save call in OK_Click**

Find the OK_Click save chain (lines 215-218):

```csharp
            if (SaveGeneralOptions() == false) return;
            if (SaveNotificationOptions() == false) return;
            if (SaveEmailAlertOptions() == false) return;
            if (SaveAudioAlertOptions() == false) return;
```

Insert `if (SaveDingtalkAlertOptions() == false) return;` after the email save line:

```csharp
            if (SaveGeneralOptions() == false) return;
            if (SaveNotificationOptions() == false) return;
            if (SaveEmailAlertOptions() == false) return;
            if (SaveDingtalkAlertOptions() == false) return;
            if (SaveAudioAlertOptions() == false) return;
```

- [ ] **Step 3: Add the Populate and Save methods**

`SaveEmailAlertOptions` ends at line 508. Insert the following two methods immediately AFTER it (before `SaveAudioAlertOptions`):

```csharp
        private void PopulateDingtalkAlertOptions()
        {
            IsDingtalkAlertsEnabled.IsChecked = ApplicationOptions.IsDingtalkAlertEnabled;
            DingtalkWebhookUrl.Text = ApplicationOptions.DingtalkWebhookUrl;
            DingtalkSecret.Password = ApplicationOptions.DingtalkSecret ?? string.Empty;
        }

        private bool SaveDingtalkAlertOptions()
        {
            if (IsDingtalkAlertsEnabled.IsChecked == true)
            {
                if (DingtalkWebhookUrl.Text.Length == 0)
                {
                    ShowError("请输入有效的钉钉 Webhook 地址。", DingtalkAlertsTab, DingtalkWebhookUrl);
                    return false;
                }

                ApplicationOptions.IsDingtalkAlertEnabled = true;
                ApplicationOptions.DingtalkWebhookUrl = DingtalkWebhookUrl.Text;
                ApplicationOptions.DingtalkSecret = DingtalkSecret.Password;
                return true;
            }
            else
            {
                ApplicationOptions.IsDingtalkAlertEnabled = false;
                return true;
            }
        }
```

- [ ] **Step 4: Add the Test handler**

`TestEmail_Click` ends at line 788. Insert the following method immediately AFTER it:

```csharp
        private async void TestDingtalk_Click(object sender, RoutedEventArgs e)
        {
            TestDingtalkButton.IsEnabled = false;
            TestDingtalkButton.Content = "发送中...";
            var webhookUrl = DingtalkWebhookUrl.Text;
            var secret = DingtalkSecret.Password;

            await Task.Run(() =>
            {
                try
                {
                    Util.SendTestDingtalk(webhookUrl, secret);
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            if (IsLoaded)
                            {
                                var dialogWindow = new DialogWindow(
                                    DialogWindow.DialogIcon.Info,
                                    "钉钉测试",
                                    "测试消息已发送。",
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
                                ShowError("测试失败：" + ex.Message, DingtalkAlertsTab, TestDingtalkButton);
                            }
                        }));
                }
            });
            TestDingtalkButton.IsEnabled = true;
            TestDingtalkButton.Content = "测试";
        }
```

- [ ] **Step 5: Compile to verify**

Run the build command. Expected: zero `error CS` lines. All XAML control names (`IsDingtalkAlertsEnabled`, `DingtalkWebhookUrl`, `DingtalkSecret`, `TestDingtalkButton`, `DingtalkAlertsTab`) now resolve to both the XAML and code-behind.

- [ ] **Step 6: Commit**

```powershell
git add vmPing/UI/OptionsWindow.xaml.cs
git commit -m "feat: add DingTalk options populate/save/test handlers"
```

---

## Task 6: Wire up the status-change trigger

**Files:**
- Modify: `vmPing/Classes/Probe-Util.cs` (in `OnStatusChange`, after the email block at line 235-238)

- [ ] **Step 1: Add the trigger call**

Find the email trigger block in `OnStatusChange` (lines 235-238):

```csharp
            if (ApplicationOptions.IsEmailAlertEnabled)
            {
                Util.SendEmail(alertType, Hostname, Alias);
            }
```

Insert immediately AFTER it:

```csharp
            if (ApplicationOptions.IsDingtalkAlertEnabled)
            {
                Util.SendDingtalk(alertType, Hostname, Alias);
            }
```

- [ ] **Step 2: Compile to verify**

Run the build command. Expected: zero `error CS` lines. The full runtime path is now wired: status change -> `Util.SendDingtalk` -> DingTalk webhook.

- [ ] **Step 3: Commit**

```powershell
git add vmPing/Classes/Probe-Util.cs
git commit -m "feat: trigger DingTalk alert on host status change"
```

---

## Task 7: Manual end-to-end verification

**Files:** None (verification only)

- [ ] **Step 1: Full clean build**

Close any running vmPing instance, then:

```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild vmPing.sln /t:Build /p:Configuration=Debug /v:minimal /nologo
```

Expected: zero `error CS`/`error MC` lines, build succeeds (only warnings allowed).

- [ ] **Step 2: Launch and configure**

Run `vmPing\bin\Debug\vmPing.exe`. Open Options (gear icon). Confirm the 钉钉 tab appears between 邮件 and 声音. Enable it, enter a real DingTalk webhook URL (and optional secret). Click 测试 - a markdown test message arrives in the DingTalk group and a success dialog appears.

- [ ] **Step 3: Validation error check**

Clear the Webhook URL field, keep DingTalk enabled, click OK. Expected: validation error "请输入有效的钉钉 Webhook 地址。" shown, save aborted.

- [ ] **Step 4: Status-change alert**

Configure a probe against a reachable host (e.g. `127.0.0.1`). Start it. Then change the target to an unreachable address (e.g. `192.0.2.1`) or disconnect network to force a status change. Expected: a DingTalk markdown message arrives with the host name, status (离线, red), and timestamp. Restore connectivity - another message arrives (在线, green).

- [ ] **Step 5: Persistence**

Enable DingTalk with a webhook + secret, check "Save as vmPing defaults", click OK. Restart vmPing. Open Options -> 钉钉. Expected: webhook URL and secret (masked) are still populated.

- [ ] **Step 6: Backward compatibility**

If an old `vmPing.xml` config (without DingTalk keys) is available, load it. Expected: app starts normally, DingTalk disabled by default. (The `TryGetValue` guards handle missing keys.)

- [ ] **Step 7: Final commit (if any fixups were needed)**

If verification uncovered issues that required fixes, commit them. Otherwise no commit needed - all prior task commits are the deliverable.

