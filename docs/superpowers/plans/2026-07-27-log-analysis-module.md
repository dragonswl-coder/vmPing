# 日志分析模块实现计划 (Log Analysis Module Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the existing `DatabaseWindow` with a modern, elegant log analysis module using HandyControl + OxyPlot.

**Architecture:** Scoped HandyControl theme (window-level only) + OxyPlot charts. Code-behind pattern (no MVVM). Reuses existing `DatabaseService` data layer + adds a `DatabaseService-Statistics.cs` partial for aggregate queries.

**Tech Stack:** .NET Framework 4.7.2, WPF, HandyControl 3.5.1 (MIT), OxyPlot.Wpf 2.1.2 (MIT), System.Data.SQLite (existing).

**Build command:** `& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" vmPing.sln /p:Configuration=Debug /restore`

**Testing:** Project has no test infrastructure. Verification = build success + manual exercise. Unit tests out-of-scope per approved spec.

**Spec:** `docs/superpowers/specs/2026-07-27-log-analysis-module-design.md`

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `vmPing/vmPing.csproj` | Modify | Add PackageReferences, swap DatabaseWindow → LogAnalysisWindow entries |
| `vmPing/ResourceDictionaries/LogAnalysisTheme.xaml` | Create | Merge HandyControl dictionaries (scoped to window) |
| `vmPing/Classes/DatabaseService-Statistics.cs` | Create | OverviewStats + HostStatistics queries |
| `vmPing/Classes/DatabaseService-Queries.cs` | Modify | Add `offset` param to `GetPingLogs` for pagination |
| `vmPing/UI/LogAnalysisWindow.xaml` | Create | Full window XAML (4 views) |
| `vmPing/UI/LogAnalysisWindow.xaml.cs` | Create | Code-behind: query dispatch, UI events, export |
| `vmPing/UI/MainWindow.xaml.cs` | Modify | `new DatabaseWindow()` → `new LogAnalysisWindow()` |
| `vmPing/UI/DatabaseWindow.xaml` | Delete | Replaced |
| `vmPing/UI/DatabaseWindow.xaml.cs` | Delete | Replaced |

---

## Task 1: Add NuGet PackageReferences + build verify

**Files:**
- Modify: `vmPing/vmPing.csproj`

- [ ] **Step 1: Add PackageReference items to csproj**

Insert before `</ItemGroup>` of the References ItemGroup (after the SQLite reference, before the closing `</ItemGroup>` on the line containing `PresentationFramework`):

```xml
    <PackageReference Include="HandyControl" Version="3.5.1" />
    <PackageReference Include="OxyPlot.Wpf" Version="2.1.2" />
```

- [ ] **Step 2: Build with restore**

Run:
```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "D:\proj\vmping\vmPing.sln" /p:Configuration=Debug /restore
```
Expected: Build succeeds (packages restore from nuget.org).

- [ ] **Step 3: Commit**

```bash
git add vmPing/vmPing.csproj
git commit -m "build: add HandyControl + OxyPlot NuGet references"
```

---

## Task 2: Create LogAnalysisTheme.xaml

**Files:**
- Create: `vmPing/ResourceDictionaries/LogAnalysisTheme.xaml`
- Modify: `vmPing/vmPing.csproj` (add Page entry)

- [ ] **Step 1: Create the theme resource dictionary**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml"/>
        <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/Theme.xaml"/>
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

- [ ] **Step 2: Add to csproj as a Page**

Add inside the `<ItemGroup>` containing other `<Page>` entries:
```xml
    <Page Include="ResourceDictionaries\LogAnalysisTheme.xaml">
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
```

- [ ] **Step 3: Build to verify**

Run the msbuild command. Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add vmPing/ResourceDictionaries/LogAnalysisTheme.xaml vmPing/vmPing.csproj
git commit -m "feat: add scoped HandyControl theme for log analysis window"
```

---

## Task 3: Create DatabaseService-Statistics.cs + add offset to GetPingLogs

**Files:**
- Create: `vmPing/Classes/DatabaseService-Statistics.cs`
- Modify: `vmPing/Classes/DatabaseService-Queries.cs:111` (GetPingLogs signature + SQL)
- Modify: `vmPing/vmPing.csproj` (add Compile entry)

- [ ] **Step 1: Add offset parameter to GetPingLogs**

In `DatabaseService-Queries.cs`, change the method signature and SQL:

Old:
```csharp
public static List<PingLogEntry> GetPingLogs(string hostname, DateTime? from = null, DateTime? to = null, int limit = 500)
```
New:
```csharp
public static List<PingLogEntry> GetPingLogs(string hostname, DateTime? from = null, DateTime? to = null, int limit = 500, int offset = 0)
```

Old SQL:
```csharp
cmd.CommandText = "SELECT id, timestamp, hostname, alias, output FROM ping_log"
    + BuildWhereClause(hostname, from, to)
    + " ORDER BY id DESC LIMIT @lim";
```
New SQL:
```csharp
cmd.CommandText = "SELECT id, timestamp, hostname, alias, output FROM ping_log"
    + BuildWhereClause(hostname, from, to)
    + " ORDER BY id DESC LIMIT @lim OFFSET @off";
```

Add after `cmd.Parameters.AddWithValue("@lim", limit);`:
```csharp
                        cmd.Parameters.AddWithValue("@off", offset);
```

- [ ] **Step 2: Create DatabaseService-Statistics.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;

namespace vmPing.Classes
{
    public class OverviewStats
    {
        public int HostCount { get; set; }
        public long TotalRecords { get; set; }
        public double AvgRtt { get; set; }
        public double LossRate { get; set; }
        public int StatusChangeCount { get; set; }
    }

    public class HostStatistics
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public long TotalPings { get; set; }
        public int MinRtt { get; set; }
        public int MaxRtt { get; set; }
        public double AvgRtt { get; set; }
        public int TimeoutCount { get; set; }
        public double LossRate { get; set; }
        public double UptimeRate { get; set; }
        public string LastActivity { get; set; }
    }

    public static partial class DatabaseService
    {
        public static OverviewStats GetOverviewStatistics(DateTime? from, DateTime? to)
        {
            var stats = new OverviewStats();
            if (string.IsNullOrEmpty(_connectionString)) return stats;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var where = BuildWhereClause(null, from, to);
                        cmd.CommandText = "SELECT COUNT(DISTINCT hostname), COUNT(*) FROM ping_log" + where;
                        AddDateParams(cmd, from, to);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                stats.HostCount = rdr.GetInt32(0);
                                stats.TotalRecords = rdr.GetInt64(1);
                            }
                        }

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT COUNT(*) FROM status_change" + where;
                        AddDateParams(cmd, from, to);
                        stats.StatusChangeCount = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                var hosts = GetHosts();
                double totalRtt = 0;
                int rttCount = 0;
                int timeoutTotal = 0;
                foreach (var host in hosts)
                {
                    var hs = GetHostStatistics(host, from, to);
                    if (hs.TotalPings == 0) continue;
                    totalRtt += hs.AvgRtt * (hs.TotalPings - hs.TimeoutCount);
                    rttCount += (int)(hs.TotalPings - hs.TimeoutCount);
                    timeoutTotal += hs.TimeoutCount;
                }
                stats.AvgRtt = rttCount > 0 ? totalRtt / rttCount : 0;
                stats.LossRate = stats.TotalRecords > 0 ? (double)timeoutTotal / stats.TotalRecords * 100 : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetOverviewStatistics error: {ex.Message}");
            }
            return stats;
        }

        public static HostStatistics GetHostStatistics(string hostname, DateTime? from, DateTime? to)
        {
            var hs = new HostStatistics { Hostname = hostname, Alias = "" };
            if (string.IsNullOrEmpty(_connectionString)) return hs;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var where = BuildWhereClause(hostname, from, to);
                        cmd.CommandText = "SELECT alias, output, timestamp FROM ping_log" + where + " ORDER BY id ASC";
                        cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);

                        var rtts = new List<int>();
                        int timeoutCount = 0;
                        string lastActivity = "";
                        string alias = "";

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                if (string.IsNullOrEmpty(alias) && !rdr.IsDBNull(0))
                                    alias = rdr.GetString(0);
                                var output = rdr.GetString(1);
                                lastActivity = rdr.GetString(2);

                                var m = RttRegex.Match(output);
                                if (m.Success)
                                {
                                    var val = m.Groups[1].Value;
                                    rtts.Add(val.StartsWith("<") ? 0 : int.Parse(val));
                                }
                                else
                                {
                                    timeoutCount++;
                                }
                            }
                        }

                        hs.Alias = alias;
                        hs.TotalPings = rtts.Count + timeoutCount;
                        hs.TimeoutCount = timeoutCount;
                        hs.MinRtt = rtts.Count > 0 ? rtts.Min() : 0;
                        hs.MaxRtt = rtts.Count > 0 ? rtts.Max() : 0;
                        hs.AvgRtt = rtts.Count > 0 ? rtts.Average() : 0;
                        hs.LossRate = hs.TotalPings > 0 ? (double)timeoutCount / hs.TotalPings * 100 : 0;
                        hs.LastActivity = lastActivity;
                    }

                    // Uptime from status_change
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT timestamp, status FROM status_change WHERE hostname = @h"
                            + (from.HasValue ? " AND timestamp >= @f" : "")
                            + (to.HasValue ? " AND timestamp <= @t" : "")
                            + " ORDER BY id ASC";
                        cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);

                        var changes = new List<KeyValuePair<DateTime, string>>();
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                changes.Add(new KeyValuePair<DateTime, string>(
                                    DateTime.Parse(rdr.GetString(0)), rdr.GetString(1)));
                            }
                        }

                        if (changes.Count > 0 && (from.HasValue || to.HasValue))
                        {
                            var rangeStart = from ?? changes[0].Key;
                            var rangeEnd = to ?? DateTime.Now;
                            var totalSpan = (rangeEnd - rangeStart).TotalSeconds;
                            if (totalSpan > 0)
                            {
                                double downtime = 0;
                                DateTime? downSince = null;
                                foreach (var c in changes)
                                {
                                    if (c.Value.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0 && !downSince.HasValue)
                                        downSince = c.Key;
                                    else if (c.Value.IndexOf("up", StringComparison.OrdinalIgnoreCase) >= 0 && downSince.HasValue)
                                    {
                                        downtime += (c.Key - downSince.Value).TotalSeconds;
                                        downSince = null;
                                    }
                                }
                                if (downSince.HasValue)
                                    downtime += (rangeEnd - downSince.Value).TotalSeconds;
                                hs.UptimeRate = Math.Max(0, (totalSpan - downtime) / totalSpan * 100);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetHostStatistics error: {ex.Message}");
            }
            return hs;
        }

        public static List<HostStatistics> GetAllHostStatistics(DateTime? from, DateTime? to)
        {
            var list = new List<HostStatistics>();
            foreach (var host in GetHosts())
            {
                list.Add(GetHostStatistics(host, from, to));
            }
            return list;
        }
    }
}
```

- [ ] **Step 3: Add Compile entry to csproj**

Add near other `<Compile Include="Classes\DatabaseService...">` entries:
```xml
    <Compile Include="Classes\DatabaseService-Statistics.cs" />
```

- [ ] **Step 4: Build to verify**

Run the msbuild command. Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add vmPing/Classes/DatabaseService-Statistics.cs vmPing/Classes/DatabaseService-Queries.cs vmPing/vmPing.csproj
git commit -m "feat: add statistics queries + pagination support for log analysis"
```

---

## Task 4: Create LogAnalysisWindow skeleton

**Files:**
- Create: `vmPing/UI/LogAnalysisWindow.xaml`
- Create: `vmPing/UI/LogAnalysisWindow.xaml.cs`
- Modify: `vmPing/vmPing.csproj` (add Page + Compile entries)

- [ ] **Step 1: Create LogAnalysisWindow.xaml** (full XAML - see Task 4 XAML block below)

- [ ] **Step 2: Create LogAnalysisWindow.xaml.cs** (full code-behind - see Task 4 CS block below)

- [ ] **Step 3: Add to csproj**

```xml
    <Compile Include="UI\LogAnalysisWindow.xaml.cs">
      <DependentUpon>LogAnalysisWindow.xaml</DependentUpon>
    </Compile>
```
```xml
    <Page Include="UI\LogAnalysisWindow.xaml">
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
```

- [ ] **Step 4: Build + run to verify window opens**

- [ ] **Step 5: Commit**

---

## Task 5-8: Implement the 4 views

Each view's XAML + code-behind is added to `LogAnalysisWindow.xaml` / `.xaml.cs`. Build + manual verify after each. Commit after each.

- [ ] **Task 5: Overview view** (cards + mini chart + recent status)
- [ ] **Task 6: Trends view** (RTT chart + status timeline)
- [ ] **Task 7: Records view** (paginated grid + search + CSV export)
- [ ] **Task 8: Statistics view** (per-host stats DataGrid)

---

## Task 9: Delete DatabaseWindow + update references

**Files:**
- Delete: `vmPing/UI/DatabaseWindow.xaml`, `vmPing/UI/DatabaseWindow.xaml.cs`
- Modify: `vmPing/UI/MainWindow.xaml.cs:573`
- Modify: `vmPing/vmPing.csproj` (remove entries)

- [ ] **Step 1: Update MainWindow.xaml.cs** - change `new DatabaseWindow` to `new LogAnalysisWindow`
- [ ] **Step 2: Delete DatabaseWindow files**
- [ ] **Step 3: Remove DatabaseWindow entries from csproj**
- [ ] **Step 4: Build to verify**
- [ ] **Step 5: Commit**

---

## Task 10: Final build + verification

- [ ] **Step 1: Full clean build**
- [ ] **Step 2: Manual exercise of all 4 views, filters, pagination, export, empty/error states**
- [ ] **Step 3: Commit**
