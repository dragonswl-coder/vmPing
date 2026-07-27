using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
            HostSelector.ItemsSource = hosts.Select(h => new HostDisplayItem { Hostname = h }).ToList();
            if (hosts.Count > 0)
                HostSelector.SelectedIndex = 0;
            else
                UpdateStatusBar(0, 0);
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
                HeaderSubtitle.Text = _currentHost;
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

        private void LoadCurrentTab()
        {
            if (_currentHost == null && MainTabs.SelectedIndex != 0 && MainTabs.SelectedIndex != 3) return;
            _queryStopwatch = Stopwatch.StartNew();

            try
            {
                switch (MainTabs.SelectedIndex)
                {
                    case 0: LoadOverview(); break;
                    case 1: LoadTrends(); break;
                    case 2: LoadRecords(); break;
                    case 3: LoadStatistics(); break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCurrentTab error: {ex.Message}");
                ShowGrowl($"查询失败: {ex.Message}", true);
            }

            _queryStopwatch.Stop();
            StatusQueryTime.Text = $"● 耗时 {_queryStopwatch.Elapsed.TotalSeconds:F2}s";
        }

        private void LoadOverview()
        {
            var from = GetFromDate();
            var to = GetToDate();

            var stats = DatabaseService.GetOverviewStatistics(_currentHost, from, to);
            CardHostCount.Text = stats.HostCount.ToString();
            CardTotalRecords.Text = stats.TotalRecords.ToString("N0");
            CardAvgRtt.Text = $"{stats.AvgRtt:F0} ms";
            CardLossRate.Text = $"{stats.LossRate:F1}%";
            CardStatusChanges.Text = stats.StatusChangeCount.ToString();

            UpdateStatusBar(stats.HostCount, stats.TotalRecords);

            var series = DatabaseService.GetRttTimeSeries(_currentHost, from, to, 60);
            OverviewChart.Model = CreateRttModel(series, 1);

            var statusChanges = DatabaseService.GetStatusChanges(_currentHost, from, to);
            var recent = statusChanges.Take(50).ToList();
            OverviewStatusGrid.ItemsSource = recent;
            OverviewEmptyText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadTrends()
        {
            if (_currentHost == null) return;
            var from = GetFromDate();
            var to = GetToDate();
            var bucketIdx = BucketSizeSelector.SelectedIndex;
            if (bucketIdx < 0) bucketIdx = 2;
            var bucketMin = BucketMinutes[bucketIdx];

            var series = DatabaseService.GetRttTimeSeries(_currentHost, from, to, bucketMin);
            var chartType = ChartTypeSelector.SelectedIndex;
            TrendsChart.Model = CreateRttModel(series, chartType);

            var statusChanges = DatabaseService.GetStatusChanges(_currentHost, from, to);
            StatusTimelineChart.Model = CreateTimelineModel(statusChanges, from, to);
        }

        private void LoadRecords()
        {
            if (_currentHost == null) return;
            _currentPage = 0;
            LoadRecordsPage();

            var from = GetFromDate();
            var to = GetToDate();
            var statusChanges = DatabaseService.GetStatusChanges(_currentHost, from, to);
            RecordsStatusGrid.ItemsSource = statusChanges;
        }

        private void LoadRecordsPage()
        {
            var from = GetFromDate();
            var to = GetToDate();
            _totalRecords = DatabaseService.GetPingLogCount(_currentHost, from, to);
            var offset = _currentPage * _pageSize;
            var logs = DatabaseService.GetPingLogs(_currentHost, from, to, _pageSize, offset);
            _allRecords = logs;

            ApplySearchFilter();

            var totalPages = Math.Max(1, (int)((_totalRecords + _pageSize - 1) / _pageSize));
            PageInfoText.Text = $"{_currentPage + 1} / {totalPages}";
            RecordsCountText.Text = $"共 {_totalRecords:N0} 条记录";
            UpdateStatusBar(DatabaseService.GetHosts().Count, _totalRecords);
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

        private void LoadStatistics()
        {
            var from = GetFromDate();
            var to = GetToDate();
            var allStats = DatabaseService.GetAllHostStatistics(from, to);
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

            // chartType 0 = line, 1 or 2 = area
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

        private void BucketSizeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || TrendsChart == null) return;
            LoadTrends();
        }

        private void ChartTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || TrendsChart == null) return;
            LoadTrends();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                LoadRecordsPage();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = Math.Max(1, (int)((_totalRecords + _pageSize - 1) / _pageSize));
            if (_currentPage < totalPages - 1)
            {
                _currentPage++;
                LoadRecordsPage();
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
