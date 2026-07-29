using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class LogAnalysisWindow : Window
    {
        private string _currentHost;
        private int _currentPage;
        private int _pageSize = 100;
        private long _totalRecords;
        private List<PingLogEntry> _allRecords;
        private static readonly int[] BucketMinutes = { 10, 30, 60, 360, 1440 };
        private Stopwatch _queryStopwatch;

        public LogAnalysisWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
            ToDatePicker.SelectedDate = DateTime.Today.AddDays(1);
            LoadHosts();
        }

        private void LoadHosts()
        {
            var hosts = DatabaseService.GetHosts();
            var items = new List<HostDisplayItem> { new HostDisplayItem { Hostname = null, DisplayName = "全部主机" } };
            items.AddRange(hosts.Select(h => new HostDisplayItem { Hostname = h, DisplayName = h }));
            HostSelector.ItemsSource = items;
            HostSelector.SelectedIndex = 0;
        }

        private void UpdateStatusBar(int hostCount, long recordCount)
        {
            StatusHosts.Text = $"● {hostCount} 台主机";
            StatusRecords.Text = $"● {recordCount} 条记录";
        }

        private DateTime? GetFromDate()
        {
            return FromDatePicker.SelectedDate;
        }

        private DateTime? GetToDate()
        {
            return ToDatePicker.SelectedDate?.AddDays(1);
        }

        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowGrowl(string message, bool isError = false)
        {
            try
            {
                if (isError)
                    HandyControl.Controls.Growl.Error(message);
                else
                    HandyControl.Controls.Growl.Success(message);
            }
            catch { }
        }

        private void HostSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HostSelector.SelectedItem is HostDisplayItem item)
            {
                _currentHost = item.Hostname;
                HeaderSubtitle.Text = string.IsNullOrEmpty(_currentHost) ? "全部主机" : _currentHost;
            }
        }

        private void QueryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadCurrentTab();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHosts();
            LoadCurrentTab();
        }

        private void QuickTime_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton rb) || rb.Tag == null) return;
            if (!int.TryParse(rb.Tag.ToString(), out int hours)) return;
            ToDatePicker.SelectedDate = DateTime.Today.AddDays(1);
            FromDatePicker.SelectedDate = DateTime.Now.AddHours(-hours);
            LoadCurrentTab();
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (e.Source is TabControl)
                LoadCurrentTab();
        }

        private bool _isLoading;

        private async void LoadCurrentTab()
        {
            if (_currentHost == null && MainTabs.SelectedIndex != 0 && MainTabs.SelectedIndex != 3) return;
            if (_isLoading) return;

            _isLoading = true;
            _queryStopwatch = Stopwatch.StartNew();
            ShowLoading(true);

            try
            {
                switch (MainTabs.SelectedIndex)
                {
                    case 0: await LoadOverview(); break;
                    case 1: await LoadTrends(); break;
                    case 2: await LoadRecords(); break;
                    case 3: await LoadStatistics(); break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCurrentTab error: {ex.Message}");
                ShowGrowl($"查询失败: {ex.Message}", true);
            }

            ShowLoading(false);
            _queryStopwatch.Stop();
            StatusQueryTime.Text = $"● 耗时 {_queryStopwatch.Elapsed.TotalSeconds:F2}s";
            _isLoading = false;
        }

        private async Task LoadOverview()
        {
            var from = GetFromDate();
            var to = GetToDate();
            var host = _currentHost;

            var stats = await Task.Run(() => DatabaseService.GetOverviewStatistics(host, from, to));
            var series = await Task.Run(() => DatabaseService.GetRttTimeSeries(host, from, to, 60));
            var statusChanges = await Task.Run(() => DatabaseService.GetStatusChanges(host, from, to));

            CardHostCount.Text = stats.HostCount.ToString();
            CardTotalRecords.Text = stats.TotalRecords.ToString("N0");
            CardAvgRtt.Text = $"{stats.AvgRtt:F0} ms";
            CardLossRate.Text = $"{stats.LossRate:F1}%";
            CardStatusChanges.Text = stats.StatusChangeCount.ToString();
            UpdateStatusBar(stats.HostCount, stats.TotalRecords);
            OverviewChart.Model = CreateRttModel(series, 1);
            var recent = statusChanges.Take(50).ToList();
            OverviewStatusGrid.ItemsSource = recent;
            OverviewEmptyText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadTrends()
        {
            if (_currentHost == null) return;
            var from = GetFromDate();
            var to = GetToDate();
            var host = _currentHost;
            var bucketIdx = BucketSizeSelector.SelectedIndex;
            if (bucketIdx < 0) bucketIdx = 2;
            var bucketMin = BucketMinutes[bucketIdx];
            var chartType = ChartTypeSelector.SelectedIndex;

            var series = await Task.Run(() => DatabaseService.GetRttTimeSeries(host, from, to, bucketMin));
            var statusChanges = await Task.Run(() => DatabaseService.GetStatusChanges(host, from, to));

            TrendsChart.Model = CreateRttModel(series, chartType);
            StatusTimelineChart.Model = CreateTimelineModel(statusChanges, from, to);
        }

        private async Task LoadRecords()
        {
            if (_currentHost == null) return;
            _currentPage = 0;
            await LoadRecordsPage();

            var from = GetFromDate();
            var to = GetToDate();
            var host = _currentHost;
            var statusChanges = await Task.Run(() => DatabaseService.GetStatusChanges(host, from, to));
            RecordsStatusGrid.ItemsSource = statusChanges;
        }

        private async Task LoadRecordsPage()
        {
            var from = GetFromDate();
            var to = GetToDate();
            var host = _currentHost;
            var offset = _currentPage * _pageSize;

            var data = await Task.Run(() =>
            {
                var count = DatabaseService.GetPingLogCount(host, from, to);
                var logs = DatabaseService.GetPingLogs(host, from, to, _pageSize, offset);
                var hostCount = DatabaseService.GetHosts().Count;
                return new { count, logs, hostCount };
            });

            _totalRecords = data.count;
            _allRecords = data.logs;
            ApplySearchFilter();

            var totalPages = Math.Max(1, (int)((_totalRecords + _pageSize - 1) / _pageSize));
            PageInfoText.Text = $"{_currentPage + 1} / {totalPages}";
            RecordsCountText.Text = $"共 {_totalRecords:N0} 条记录";
            UpdateStatusBar(data.hostCount, _totalRecords);
        }

        private void ApplySearchFilter()
        {
            if (_allRecords == null) return;
            var keyword = SearchBox.Text?.Trim() ?? "";
            IEnumerable<PingLogEntry> filtered = _allRecords;
            if (!string.IsNullOrEmpty(keyword))
                filtered = _allRecords.Where(l => (l.Output?.Contains(keyword) ?? false) || (l.Hostname?.Contains(keyword) ?? false));
            RecordsGrid.ItemsSource = filtered.ToList();
        }

        private async Task LoadStatistics()
        {
            var from = GetFromDate();
            var to = GetToDate();

            var allStats = await Task.Run(() => DatabaseService.GetAllHostStatistics(from, to));
            StatsGrid.ItemsSource = allStats;

            var totalPings = allStats.Sum(s => s.TotalPings);
            var avgLoss = allStats.Count > 0 ? allStats.Average(s => s.LossRate) : 0;
            StatsSummaryText.Text = $"{allStats.Count} 台主机，共 {totalPings:N0} 条记录，平均丢包率 {avgLoss:F1}%";
            UpdateStatusBar(allStats.Count, totalPings);
        }

        private PlotModel CreateRttModel(List<RttTimeBucket> buckets, int chartType)
        {
            var model = new PlotModel { PlotAreaBackground = OxyColors.White };
            var minTime = buckets.Count > 0 ? buckets.Min(b => b.BucketTime) : DateTime.Now.AddDays(-1);
            var maxTime = buckets.Count > 0 ? buckets.Max(b => b.BucketTime) : DateTime.Now;
            var maxRtt = buckets.Count > 0 ? Math.Max(10, buckets.Max(b => b.AvgRtt)) : 100;

            var timeAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间",
                StringFormat = "MM-dd HH:mm",
                MajorGridlineStyle = LineStyle.Dot,
                MinorGridlineStyle = LineStyle.None
            };
            var rttAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "延迟 (ms)",
                Minimum = 0,
                Maximum = maxRtt * 1.1,
                MajorGridlineStyle = LineStyle.Dot,
                MinorGridlineStyle = LineStyle.None
            };
            model.Axes.Add(timeAxis);
            model.Axes.Add(rttAxis);

            if (buckets.Count == 0) return model;

            var points = buckets.Select(b => DateTimeAxis.CreateDataPoint(b.BucketTime, b.AvgRtt)).ToList();

            if (chartType >= 1)
            {
                var areaSeries = new AreaSeries
                {
                    Color = OxyColor.FromRgb(0x3b, 0x82, 0xf6),
                    Fill = OxyColor.FromArgb(80, 0x3b, 0x82, 0xf6),
                    MarkerType = MarkerType.None
                };
                foreach (var p in points) areaSeries.Points.Add(p);
                foreach (var p in points) areaSeries.Points2.Add(new DataPoint(p.X, 0));
                model.Series.Add(areaSeries);
            }
            else
            {
                var lineSeries = new LineSeries
                {
                    Color = OxyColor.FromRgb(0x3b, 0x82, 0xf6),
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 3,
                    MarkerFill = OxyColor.FromRgb(0x3b, 0x82, 0xf6)
                };
                foreach (var p in points) lineSeries.Points.Add(p);
                model.Series.Add(lineSeries);
            }

            return model;
        }

        private PlotModel CreateTimelineModel(List<StatusChangeEntry> changes, DateTime? from, DateTime? to)
        {
            var model = new PlotModel { PlotAreaBackground = OxyColors.White };
            var startTime = from ?? (changes.Count > 0 ? DateTime.Parse(changes.Min(c => c.Timestamp)) : DateTime.Now.AddDays(-1));
            var endTime = to ?? DateTime.Now;

            var timeAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间",
                StringFormat = "MM-dd HH:mm",
                MajorGridlineStyle = LineStyle.Dot
            };
            var valueAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                Title = "状态"
            };
            valueAxis.Labels.Add("down");
            valueAxis.Labels.Add("up");
            model.Axes.Add(timeAxis);
            model.Axes.Add(valueAxis);

            if (changes.Count == 0) return model;

            var sorted = changes.OrderBy(c => c.Id).ToList();
            DateTime? downSince = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                var c = sorted[i];
                var ts = DateTime.Parse(c.Timestamp);
                var isDown = c.Status.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0;
                var isUp = c.Status.IndexOf("up", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isDown && !downSince.HasValue)
                    downSince = ts;
                else if (isUp && downSince.HasValue)
                {
                    var bar = new IntervalBarSeries();
                    bar.Items.Add(new IntervalBarItem
                    {
                        Start = DateTimeAxis.ToDouble(downSince.Value),
                        End = DateTimeAxis.ToDouble(ts),
                        CategoryIndex = 0,
                        Color = OxyColor.FromRgb(0xef, 0x44, 0x44)
                    });
                    model.Series.Add(bar);
                    downSince = null;
                }
            }
            if (downSince.HasValue)
            {
                var bar = new IntervalBarSeries();
                bar.Items.Add(new IntervalBarItem
                {
                    Start = DateTimeAxis.ToDouble(downSince.Value),
                    End = DateTimeAxis.ToDouble(endTime),
                    CategoryIndex = 0,
                    Color = OxyColor.FromRgb(0xef, 0x44, 0x44)
                });
                model.Series.Add(bar);
            }

            return model;
        }

        private async void BucketSizeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || TrendsChart == null) return;
            await LoadTrends();
        }

        private async void ChartTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || TrendsChart == null) return;
            await LoadTrends();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                await LoadRecordsPage();
            }
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = Math.Max(1, (int)((_totalRecords + _pageSize - 1) / _pageSize));
            if (_currentPage < totalPages - 1)
            {
                _currentPage++;
                await LoadRecordsPage();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var from = GetFromDate();
            var to = GetToDate();
            var hostLabel = string.IsNullOrEmpty(_currentHost) ? "全部主机" : _currentHost;

            var pingCount = DatabaseService.GetPingLogCount(_currentHost, from, to);
            var statusChanges = DatabaseService.GetStatusChanges(_currentHost, from, to);

            if (pingCount == 0 && statusChanges.Count == 0)
            {
                ShowGrowl("所选范围内无记录可清除", true);
                return;
            }

            var fromStr = from?.ToString("yyyy-MM-dd") ?? "最早";
            var toStr = ToDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "至今";
            var msg = $"将删除 [{hostLabel}] 在 [{fromStr} ~ {toStr}] 内的记录：\n\n  Ping 记录: {pingCount:N0} 条\n  状态变更: {statusChanges.Count} 条\n\n此操作不可撤销，确认删除？";

            if (MessageBox.Show(msg, "确认清除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var deletedPings = DatabaseService.DeletePingLogs(_currentHost, from, to);
                var deletedChanges = DatabaseService.DeleteStatusChanges(_currentHost, from, to);
                ShowGrowl($"已删除 {deletedPings:N0} 条 Ping 记录, {deletedChanges} 条状态变更");
                LoadHosts();
                LoadCurrentTab();
            }
            catch (Exception ex)
            {
                ShowGrowl($"清除失败: {ex.Message}", true);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHost == null && MainTabs.SelectedIndex != 3)
            {
                ShowGrowl("请先选择主机", true);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"vmping_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var from = GetFromDate();
                var to = GetToDate();
                var logs = DatabaseService.GetPingLogs(_currentHost, from, to, 100000, 0);
                var sb = new StringBuilder();
                sb.AppendLine("时间,主机,别名,内容,延迟");
                foreach (var log in logs)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(log.Timestamp),
                        EscapeCsv(log.Hostname),
                        EscapeCsv(log.Alias ?? ""),
                        EscapeCsv(log.Output),
                        EscapeCsv(log.RttDisplay ?? "")));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                ShowGrowl($"已导出 {logs.Count} 条记录");
            }
            catch (Exception ex)
            {
                ShowGrowl($"导出失败: {ex.Message}", true);
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }
    }
}
