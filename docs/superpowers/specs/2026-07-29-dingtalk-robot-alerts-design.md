# DingTalk Robot Alerts — Design Spec

**Date:** 2026-07-29
**Topic:** Add DingTalk (钉钉) custom robot notification on host status change

## Goal

When a monitored host changes status (up / down / error / latency change), send a
Markdown message to a DingTalk group via a custom robot webhook, in addition to
the existing email alert. Configuration is added as a new Options tab placed
directly below the existing Email Alerts tab, mirroring the email alert feature's
structure and conventions.

## Non-Goals

- No DingTalk "at" (@mention) targeting. All group members see the message.
- No retry/backoff beyond what the existing email alert does (fire-and-forget,
  silently ignore errors).
- No refactoring of the existing email alert code.
- No support for IP-whitelist-only configuration UI (IP whitelist is configured
  in the DingTalk admin console, not in vmPing).

## Decisions (from brainstorming)

- **Security mode:** Support both 加签 (HMAC-SHA256 signing with secret) and
  关键词/IP白名单 modes. If `DingtalkSecret` is filled, signing is used; if left
  blank, the webhook is called directly (relying on keyword/IP whitelist set in
  DingTalk). Messages always contain the literal "vmPing" so the keyword mode
  works out of the box when the user sets "vmPing" as their keyword.
- **Message format:** Markdown.

## Architecture

The feature mirrors the existing Email Alerts feature across the same six files.
Each alert channel (popup, email, audio) already has its own Options tab; DingTalk
follows the same one-channel-per-tab convention.

```
ApplicationOptions.cs   -> option properties
Configuration.cs        -> XML persist (encrypt secret)
OptionsWindow.xaml      -> "钉钉" tab UI (between 邮件 and 声音 tabs)
OptionsWindow.xaml.cs   -> Populate / Save / Test handlers
Util.cs                 -> SendDingtalk (async) + SendTestDingtalk (sync)
Probe-Util.cs           -> trigger in OnStatusChange()
```

## Components

### 1. ApplicationOptions.cs

Add a new property block immediately after the Email notifications block (after
line 59):

```csharp
// DingTalk notifications.
public static bool IsDingtalkAlertEnabled { get; set; } = false;
public static string DingtalkWebhookUrl { get; set; }
public static string DingtalkSecret { get; set; }  // optional; if set, 加签 signing is used
```

### 2. Configuration.cs

**Save** (in the save section near line 184, after the email nodes):
- `Node("IsDingtalkAlertEnabled", ApplicationOptions.IsDingtalkAlertEnabled)`
- `Node("DingtalkWebhookUrl", ApplicationOptions.DingtalkWebhookUrl)`
- `Node("DingtalkSecret", string.IsNullOrWhiteSpace(...) ? "" : Util.EncryptStringAES(...))`
  — mirrors the `EmailPassword` encryption pattern.

**Load** (in the load section near line 489, after the email keys):
- Read `IsDingtalkAlertEnabled` (bool.Parse).
- Read `DingtalkWebhookUrl` (string).
- Read `DingtalkSecret`; decrypt via `Util.DecryptStringAES` when non-empty,
  wrapped in a guard so a missing/blank key leaves the value null (backward
  compatibility with config files created before this feature).

All three reads use the existing `options.TryGetValue(...)` guard pattern so old
config files without these keys load without error.

### 3. OptionsWindow.xaml

New `<TabItem Header="钉钉" Name="DingtalkAlertsTab">` inserted between the
邮件 tab (ends line 465) and the 声音 tab (line 468).

Layout (reuses existing styles `OptionHeaderTextStyle`,
`OptionSubHeaderTextStyle`, `BooleanToVisibilityConverter`,
`ButtonStandardStyle`, and the same 120px label-grid width as the email tab):

- Header: "钉钉提醒"
- Enable checkbox `Name="IsDingtalkAlertsEnabled"` content "启用钉钉提醒",
  with tooltip label "启用后，每当主机状态发生变化时通过钉钉机器人发送提醒。"
- A visibility-bound DockPanel (bound to the checkbox `IsChecked`) containing:
  - Subheading "机器人配置"
  - Webhook URL row: label "Webhook 地址" + `TextBox Name="DingtalkWebhookUrl"`
  - Secret row: label "加签密钥" + `PasswordBox Name="DingtalkSecret"` + tooltip
    label "可选。留空使用关键词/IP白名单模式；填写则使用加签验证。"
    (PasswordBox used to mask the secret, mirroring `SmtpPassword`.)
  - Test button `Name="TestDingtalkButton"` content "测试",
    `Click="TestDingtalk_Click"`, visibility-bound to the enable checkbox.

### 4. OptionsWindow.xaml.cs

Three members mirroring the email equivalents:

- `PopulateDingtalkAlertOptions()` — called from the constructor populate section
  (near line 33, after `PopulateEmailAlertOptions()`). Sets checkbox + text fields
  from `ApplicationOptions`.
- `SaveDingtalkAlertOptions()` — called from the save path (near line 217, after
  `SaveEmailAlertOptions()`). When enabled, validates `DingtalkWebhookUrl` is
  non-empty (else `ShowError(..., DingtalkAlertsTab, DingtalkWebhookUrl)`). Writes
  to `ApplicationOptions`. When disabled, clears `IsDingtalkAlertEnabled`. Returns
  bool (false on validation failure) so the caller can abort save, matching
  `SaveEmailAlertOptions`'s contract.
- `TestDingtalk_Click(object, RoutedEventArgs)` — async void. Disables button,
  sets content "发送中...", reads field values, `await Task.Run(() =>
  Util.SendTestDingtalk(webhookUrl, secret))`, then shows a success DialogWindow
  or `ShowError` on exception — mirroring `TestEmail_Click` exactly.

### 5. Util.cs

Add `using System.Security.Cryptography;` is already present (used by AES). Add
`using System.Web;` for `HttpUtility.UrlEncode` — **note**: verify
`System.Web` reference exists in the project; if not available on the target
.NET Framework profile, use `Uri.EscapeDataString` instead (functionally
equivalent for DingTalk's sign parameter). Implementation must prefer
`Uri.EscapeDataString` to avoid adding a new assembly reference.

**`SendDingtalk(string status, string hostname, string alias)`** — `public static
async void`, silent catch (mirrors `SendEmail`):
1. Build display name: `alias` if present, else `hostname`; long form
   `"{alias} ({hostname})"` when alias set.
2. Choose color by status string:
   - "离线" -> `#FF0000` (red)
   - "在线", "正常延迟" -> `#008000` (green)
   - "错误", "高延迟" -> `#FFA500` (orange)
3. Build markdown body:
   ```json
   {
     "msgtype": "markdown",
     "markdown": {
       "title": "vmPing 状态变化",
       "text": "### vmPing 状态变化\n\n**主机**: {longName}\n**状态**: <font color=\"{color}\">{status}</font>\n**时间**: {now}"
     }
   }
   ```
4. POST JSON to the webhook URL (with signing params appended when secret set).
5. Read response; ignore result. Catch all exceptions silently.

**`SendTestDingtalk(string webhookUrl, string secret)`** — `public static`, throws
on failure (mirrors `SendTestEmail`):
1. Build test markdown body:
   ```json
   {
     "msgtype": "markdown",
     "markdown": {
       "title": "vmPing 测试",
       "text": "### vmPing 钉钉机器人测试\n\n这是 vmPing 于 {now} 发送的测试消息"
     }
   }
   ```
2. POST to webhook (with signing if secret set).
3. Throw `Exception` with DingTalk's `errmsg` when response `errcode != 0`.

**Signing helper** (private static, used by both methods):
- `timestamp` = milliseconds since epoch (string).
- `stringToSign` = `timestamp + "\n" + secret`.
- `sign` = `Uri.EscapeDataString(Convert.ToBase64String(new HMACSHA256(
  Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes(stringToSign))))`.
- Return `webhookUrl + "&timestamp=" + timestamp + "&sign=" + sign`.

**HTTP POST helper** (private static, used by both methods to avoid duplication):
- `PostDingtalkMessage(string webhookUrl, string secret, string jsonBody)` ->
  string response body. Uses `HttpWebRequest` (available in .NET Framework 4.5
  without extra packages) with `Method="POST"`, `ContentType="application/json;
  charset=utf-8"`. Appends signing params when `secret` non-empty. Returns the
  response body string; throws on non-2xx HTTP status.

### 6. Probe-Util.cs

In `OnStatusChange` (line 235), add immediately after the email block:

```csharp
if (ApplicationOptions.IsDingtalkAlertEnabled)
{
    Util.SendDingtalk(alertType, Hostname, Alias);
}
```

The `alertType` values passed in are: "在线", "离线", "错误", "高延迟",
"正常延迟" (from `Probe-Icmp.cs` / `Probe-Tcp.cs`). These map to the color
table above.

## Data Flow

```
Probe status change
  -> Probe.OnStatusChange(newStatus, alertType)
     -> if IsDingtalkAlertEnabled: Util.SendDingtalk(alertType, hostname, alias)
        -> build markdown JSON
        -> (if secret) append &timestamp=&sign= to webhook URL
        -> HttpWebRequest POST -> DingTalk -> group chat
        -> (errors silently ignored)
```

## Error Handling

- **Runtime alerts (`SendDingtalk`):** All exceptions caught and silently
  ignored, exactly like `SendEmail`. A bad webhook or network failure must never
  crash the app or interrupt ping monitoring.
- **Test button (`SendTestDingtalk`):** Throws; the UI handler catches and shows
  `ShowError` with the message, mirroring `TestEmail_Click`.
- **Config load:** Missing keys in old config files are tolerated via
  `TryGetValue` guards; secret decrypt failure leaves the field null.
- **Validation:** Saving with DingTalk enabled but empty webhook URL shows an
  error and aborts the save (does not persist the enabled state).

## Testing

The project has no automated test framework (it is a WPF app). Verification is
manual:
1. Build the solution (no compile errors / warnings in changed files).
2. Open Options -> 钉钉 tab appears between 邮件 and 声音.
3. Enable, fill a real DingTalk webhook (+ optional secret), click 测试 —
   message arrives in the DingTalk group; success dialog shown.
4. Clear webhook, enable, click OK on options — validation error shown.
5. Start a probe against a host, take it down/up — DingTalk markdown message
   arrives with correct status + color.
6. Restart vmPing — DingTalk settings persisted (secret re-masked).
7. Load an old vmPing.xml without DingTalk keys — app starts normally.

## Files Changed

| File | Change |
|------|--------|
| `vmPing/Classes/ApplicationOptions.cs` | +3 properties |
| `vmPing/Classes/Configuration.cs` | save + load 3 keys (secret encrypted) |
| `vmPing/UI/OptionsWindow.xaml` | new 钉钉 TabItem |
| `vmPing/UI/OptionsWindow.xaml.cs` | Populate/Save/Test handlers |
| `vmPing/Classes/Util.cs` | SendDingtalk + SendTestDingtalk + helpers |
| `vmPing/Classes/Probe-Util.cs` | trigger call in OnStatusChange |
