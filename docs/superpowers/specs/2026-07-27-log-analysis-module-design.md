# 日志分析模块设计 (Log Analysis Module Design)

**Date:** 2026-07-27
**Status:** Approved (pending implementation)
**Approach:** HandyControl + OxyPlot

## 1. 概述 / Summary

新增一个独立的、现代化的日志分析模块，替换现有未提交的 `DatabaseWindow`。模块复用现有 `DatabaseService` 数据层，重写 UI 层，采用开源 WPF 库 [HandyControl](https://github.com/HandyOrg/HandyControl)（MIT）和 [OxyPlot](https://github.com/oxyplot/oxyplot)（MIT）实现现代、优雅的界面与交互式图表。

**目标：**
- 替换现有基础 `DatabaseWindow`（未提交的工作进行中文件）。
- 提供仪表盘式概览、趋势图表、记录检索、统计汇总四个视图。
- UI 现代且优雅，交互流畅（分页、搜索、加载状态、空状态、Growl 通知）。
- 模块独立：HandyControl 主题仅作用于本窗口，不影响 vmPing 主程序现有外观。

**非目标（YAGNI）：**
- 不引入 MVVM 框架（沿用现有 code-behind 模式）。
- 不修改主程序其他窗口的样式。
- 不添加单元测试项目（后续可选）。
- 不重构现有 `DatabaseService.cs` / `DatabaseService-Queries.cs` 的已提交逻辑。

## 2. 技术栈与依赖 / Tech Stack

### 2.1 开源库
| 库 | 版本 | 用途 | 许可证 | .NET Framework 支持 |
|---|---|---|---|---|
| HandyControl | 3.5.1 | UI 组件 + 主题 | MIT | net40+ ✓ |
| OxyPlot.Wpf | 2.1.2 | 交互式图表 | MIT | net45+ ✓ |

目标框架：.NET Framework 4.7.2（兼容）。

### 2.2 NuGet 配置
项目当前无 `packages.config`，DLL 以手动引用方式放在 `lib/`。本模块通过 **PackageReference** 直接在 `vmPing.csproj` 中引入 NuGet 包（非 SDK 风格项目也支持）。现有 `lib/` 下的 SQLite DLL 保持不变。

## 3. 架构 / Architecture

### 3.1 文件结构
```
vmPing/
  Classes/
    DatabaseService.cs              (现有 - 不动: 初始化 + 插入)
    DatabaseService-Queries.cs      (现有 - 不动: 主机/日志/时序/状态变更查询)
    DatabaseService-Statistics.cs   (新增 - partial: 概览统计 + 每主机统计)
  UI/
    LogAnalysisWindow.xaml          (新增 - 替换 DatabaseWindow.xaml)
    LogAnalysisWindow.xaml.cs       (新增 - 替换 DatabaseWindow.xaml.cs)
  ResourceDictionaries/
    LogAnalysisTheme.xaml           (新增 - 合并 HandyControl 字典, 仅作用于本窗口)
```

**删除文件：**
- `UI/DatabaseWindow.xaml`
- `UI/DatabaseWindow.xaml.cs`

**修改文件：**
- `vmPing.csproj`：移除 DatabaseWindow 条目；新增 LogAnalysisWindow、LogAnalysisTheme、DatabaseService-Statistics 条目；新增 `<PackageReference>` for HandyControl + OxyPlot.Wpf。
- `UI/MainWindow.xaml.cs`（约 line 573）：将 `new DatabaseWindow()` 改为 `new LogAnalysisWindow()`。

### 3.2 主题作用域（关键）
HandyControl 通常在 `App.xaml` 加载主题，会改变整个应用外观。为保持模块独立，将 HandyControl 资源字典合并到 **`LogAnalysisWindow.Resources`**（通过 `LogAnalysisTheme.xaml`），仅作用于本窗口。`Growl` 通知使用窗口级 `GrowlPanel` 容器，而非应用级。主程序其余窗口不受影响。

### 3.3 编码模式
沿用现有 code-behind 模式（不引入 MVVM 框架）。`LogAnalysisWindow.xaml.cs` 负责 UI 事件绑定与查询调度；所有 SQL 查询放在 `DatabaseService-*.cs` partial 类中。

### 3.4 入口
不变 - `MainWindow` 菜单项打开 `LogAnalysisWindow`；`DatabaseService.Initialize()` 调用位置不变。

## 4. UI 布局 / UI Layout

### 4.1 整体布局
仪表盘式窗口，共享筛选栏 + 左侧导航 + 底部状态栏：

```
┌──────────────────────────────────────────────────┐
│  日志分析                          [刷新] [导出]   │  ← 自定义标题栏
├──────────────────────────────────────────────────┤
│  主机 [▼]  从 [日期] 到 [日期]  [查询]            │  ← 筛选栏（持久）
│  快捷: [1小时] [24小时] [7天] [30天]              │
├──────────┬───────────────────────────────────────┤
│  概览    │                                       │
│  趋势    │       当前视图内容                     │  ← SideMenu + 内容
│  记录    │                                       │
│  统计    │                                       │
├──────────┴───────────────────────────────────────┤
│  ● 3 台主机  ● 12,345 条记录  ● 耗时 0.12s        │  ← 状态栏
└──────────────────────────────────────────────────┘
```

- **HandyControl `SideMenu`** 做左侧导航。
- **筛选栏** 在所有视图持久 — 切换视图时以当前筛选重新查询。
- **快捷时间按钮** 用 `ButtonGroup` — 点击设置"从"为当前时间减去间隔。
- **状态栏** 显示实时计数 + 查询耗时。

### 4.2 视图 1：概览 (Overview)
- 顶部一排 **5 个摘要卡片**（HandyControl `Card` + `Shield` 图标）：主机数、记录总数、平均延迟、丢包率、状态变更数。
- 卡片下方：**迷你 RTT 趋势** 面积图（OxyPlot `AreaSeries`），默认最近 24 小时。
- 底部：最近状态变更紧凑列表（最新 10 条 up/down 事件，带彩色圆点）。

### 4.3 视图 2：趋势 (Trends)
- 顶部：分桶大小选择器（`10分/30分/1时/6时/1天`）+ 图表类型切换（`折线/面积/柱状`）。
- 主图：全宽 **RTT 趋势**（OxyPlot）— 时间 vs RTT，坐标轴可缩放/平移，超时点标注。颜色：绿 <50 / 黄 <150 / 红 ≥150。
- 主图下方：**状态时间线** — 水平时间轴显示 up/down 时段（OxyPlot `IntervalBarSeries`），一眼可见中断时段。

### 4.4 视图 3：记录 (Records)
- HandyControl `SearchBar` 按内容过滤日志。
- **分页 Ping 日志表格**（HandyControl `Pagination`，每页 100 条）：列 时间 / 主机 / 内容 / 延迟。行按 RTT 着色（复用 `RttToColorConverter` 逻辑）。
- 下方：状态变更表格（可折叠）。
- 右上：**导出 CSV** 按钮。

### 4.5 视图 4：统计 (Statistics)
- **可排序 DataGrid** 展示每主机统计：主机 / 别名 / 记录数 / 最小延迟 / 最大延迟 / 平均延迟 / 丢包率 / 在线率 / 最后活动。
- 顶部一行：所有主机的汇总合计。

### 4.6 空状态与加载
- 查询中：HandyControl `Loading` 遮罩。
- 无数据：友好空状态卡片（图标 + "所选范围内无记录"）。
- 成功/错误反馈：HandyControl `Growl` 浮动通知（窗口级 `GrowlPanel`）。

## 5. 数据层 / Data Layer

### 5.1 新增：`DatabaseService-Statistics.cs`

```csharp
public class OverviewStats
{
    public int HostCount { get; set; }
    public long TotalRecords { get; set; }
    public double AvgRtt { get; set; }       // ms, 排除超时
    public double LossRate { get; set; }      // 0-100
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
    public double LossRate { get; set; }       // 0-100
    public double UptimeRate { get; set; }      // 0-100
    public string LastActivity { get; set; }
}
```

**方法：**
- `GetOverviewStatistics(from, to)` -> `OverviewStats` — 单次查询返回概览卡片所需全部汇总。
- `GetAllHostStatistics(from, to)` -> `List<HostStatistics>` — 每主机聚合。
  - **UptimeRate** 计算：取出该主机按时序排列的状态变更，在 C# 中累计 downtime（`down` 事件到下一个 `up` 事件的时间差），uptime% = (总时长 - downtime) / 总时长 × 100。

**使用 SQL 聚合**（`COUNT`/`MIN`/`MAX`/`AVG`）提高效率，不将所有行载入内存。RTT 解析复用现有 `RttRegex` 正则。

### 5.2 复用现有方法（不修改）
- `GetHosts()` — 主机下拉框。
- `GetPingLogs(host, from, to, limit)` — 记录视图分页（通过 limit + offset 实现分页）。
- `GetStatusChanges(host, from, to)` — 状态变更列表 + 时间线计算。
- `GetRttTimeSeries(host, from, to, bucketMinutes)` — 趋势图表数据。
- `GetPingLogCount(host, from, to)` — 分页总数。

**趋势图表数据转换：** `RttTimeBucket.BucketTime`/`AvgRtt` 转为 OxyPlot `DataPoint`；状态时间线由 `GetStatusChanges` 构建间隔。

**分页支持：** 修改现有 `GetPingLogs` 签名，新增 `int offset = 0` 参数（默认值 0，向后兼容），SQL 末尾改为 `LIMIT @lim OFFSET @off`。UI 层按 `页码 × pageSize` 计算 offset。

## 6. 错误处理 / Error Handling

### 6.1 数据层
- 保持现有 `catch { return empty; }` 模式以向后兼容。
- 新增 `System.Diagnostics.Debug.WriteLine` 输出异常信息，便于调试。

### 6.2 UI 层
- 每次查询调用在 code-behind 中用 try/catch 包裹。
- 异常时：显示 HandyControl `Growl` 错误通知 + 空状态面板（不崩溃、不白屏）。

### 6.3 数据库未初始化
- 渲染前检查 `ApplicationOptions.DatabasePath` / 连接状态。
- 未启用时显示居中空状态："数据库未启用 — 请在选项中开启日志记录"，并提供按钮打开选项窗口。

## 7. 测试与验证 / Testing & Verification

项目无测试基础设施。验证方式：

1. **构建验证：** `msbuild vmPing.sln /p:Configuration=Debug` — 确保编译通过。
2. **手动验证：**
   - 运行应用，ping 数个主机，等待数据积累。
   - 打开日志分析窗口，逐一检查四个视图。
   - 验证筛选、分页、搜索、导出 CSV。
   - 验证空状态（无数据的主机/时间范围）。
   - 验证错误状态（数据库未启用时）。
3. 单元测试项目为 out-of-scope（如需可后续提出）。

## 8. 许可证 / Licensing
- HandyControl: MIT — 安全可用。
- OxyPlot: MIT — 安全可用。
- 无 copyleft 风险。如需可在 README 添加致谢说明。

## 9. 实施步骤概要 / Implementation Outline

1. 配置 NuGet PackageReference（HandyControl + OxyPlot.Wpf），还原包，验证构建。
2. 创建 `LogAnalysisTheme.xaml` 资源字典（合并 HandyControl 字典）。
3. 创建 `DatabaseService-Statistics.cs`（概览统计 + 每主机统计 + 分页重载）。
4. 创建 `LogAnalysisWindow.xaml` + `.xaml.cs`（整体框架 + 筛选栏 + SideMenu + 状态栏）。
5. 实现视图 1：概览（摘要卡片 + 迷你趋势图 + 最近状态变更）。
6. 实现视图 2：趋势（RTT 图表 + 状态时间线）。
7. 实现视图 3：记录（分页表格 + 搜索 + 导出 CSV）。
8. 实现视图 4：统计（每主机统计 DataGrid）。
9. 删除 `DatabaseWindow.xaml` + `.xaml.cs`，更新 `MainWindow.xaml.cs` 引用，更新 `.csproj`。
10. 构建验证 + 手动验证全部视图与状态。
