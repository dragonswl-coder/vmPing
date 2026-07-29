using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace vmPing.Classes
{
    public class PingLogEntry
    {
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string Output { get; set; }
        public int? RttParsed { get; set; }
        public bool IsTimeout { get; set; }
        public bool IsError { get; set; }

        public string RttDisplay
        {
            get
            {
                if (IsTimeout) return "超时";
                if (IsError) return "错误";
                if (RttParsed.HasValue)
                    return RttParsed.Value < 1 ? "<1毫秒" : $"{RttParsed.Value}毫秒";
                return "";
            }
        }
    }

    public class HostDisplayItem
    {
        public string Hostname { get; set; }
        public string DisplayName { get; set; }
    }

    public class StatusChangeEntry
    {
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string Status { get; set; }
    }

    public class RttTimeBucket
    {
        public DateTime BucketTime { get; set; }
        public string Label { get; set; }
        public double AvgRtt { get; set; }
        public int MinRtt { get; set; }
        public int MaxRtt { get; set; }
        public int SampleCount { get; set; }
        public int TimeoutCount { get; set; }
        public double BarHeight { get; set; }
        public string BarColor { get; set; }
        public string RttDisplay => AvgRtt < 1 ? "<1" : $"{AvgRtt:F0}";
    }

    public static partial class DatabaseService
    {
        private static readonly Regex RttRegex = new Regex(@"\[(<?\d+)\s*毫秒\]");

        public static List<string> GetHosts()
        {
            var hosts = new List<string>();
            if (string.IsNullOrEmpty(_connectionString)) return hosts;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT hostname FROM ping_log ORDER BY hostname";
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                                hosts.Add(rdr.GetString(0));
                        }
                    }
                }
            }
            catch { }
            return hosts;
        }

        private static string BuildWhereClause(string hostname, DateTime? from, DateTime? to)
        {
            var clauses = new List<string>();
            if (!string.IsNullOrEmpty(hostname))
                clauses.Add("hostname = @h");
            if (from.HasValue)
                clauses.Add("timestamp >= @f");
            if (to.HasValue)
                clauses.Add("timestamp <= @t");
            return clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        }

        private static void AddDateParams(SQLiteCommand cmd, DateTime? from, DateTime? to)
        {
            if (from.HasValue)
                cmd.Parameters.AddWithValue("@f", from.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (to.HasValue)
                cmd.Parameters.AddWithValue("@t", to.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public static List<PingLogEntry> GetPingLogs(string hostname, DateTime? from = null, DateTime? to = null, int limit = 500, int offset = 0)
        {
            var logs = new List<PingLogEntry>();
            if (string.IsNullOrEmpty(_connectionString)) return logs;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, timestamp, hostname, alias, output FROM ping_log"
                            + BuildWhereClause(hostname, from, to)
                            + " ORDER BY id DESC LIMIT @lim OFFSET @off";
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        cmd.Parameters.AddWithValue("@lim", limit);
                        cmd.Parameters.AddWithValue("@off", offset);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var output = rdr.GetString(4);
                                var entry = new PingLogEntry
                                {
                                    Id = rdr.GetInt64(0),
                                    Timestamp = rdr.GetString(1),
                                    Hostname = rdr.GetString(2),
                                    Alias = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                                    Output = output,
                                    IsTimeout = output.Contains("超时") || output.Contains("关闭"),
                                    IsError = output.Contains("错误") && !output.Contains("成功")
                                };
                                var m = RttRegex.Match(output);
                                if (m.Success)
                                {
                                    var val = m.Groups[1].Value;
                                    entry.RttParsed = val.StartsWith("<") ? 0 : int.Parse(val);
                                }
                                logs.Add(entry);
                            }
                        }
                    }
                }
            }
            catch { }
            return logs;
        }

        public static List<RttTimeBucket> GetRttTimeSeries(string hostname, DateTime? from, DateTime? to, int bucketMinutes = 60)
        {
            var buckets = new List<RttTimeBucket>();
            if (string.IsNullOrEmpty(_connectionString)) return buckets;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, timestamp, output FROM ping_log"
                            + BuildWhereClause(hostname, from, to)
                            + " ORDER BY id ASC";
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);

                        var raw = new List<Tuple<DateTime, int?>>();
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var ts = DateTime.Parse(rdr.GetString(1));
                                var output = rdr.GetString(2);
                                int? rtt = null;
                                var m = RttRegex.Match(output);
                                if (m.Success)
                                {
                                    var val = m.Groups[1].Value;
                                    rtt = val.StartsWith("<") ? 0 : int.Parse(val);
                                }
                                raw.Add(Tuple.Create(ts, rtt));
                            }
                        }

                        if (raw.Count == 0) return buckets;

                        var span = raw.Max(r => r.Item1) - raw.Min(r => r.Item1);
                        if (span.TotalMinutes < bucketMinutes)
                            bucketMinutes = Math.Max(1, (int)span.TotalMinutes);

                        var grouped = raw
                            .GroupBy(r => new DateTime(r.Item1.Year, r.Item1.Month, r.Item1.Day, r.Item1.Hour, r.Item1.Minute / bucketMinutes * bucketMinutes, 0))
                            .OrderBy(g => g.Key)
                            .ToList();

                        double maxAvg = 1;
                        foreach (var g in grouped)
                        {
                            var rtts = g.Where(x => x.Item2.HasValue).Select(x => x.Item2.Value).ToList();
                            var bucket = new RttTimeBucket
                            {
                                BucketTime = g.Key,
                                Label = g.Key.ToString("MM-dd HH:mm"),
                                SampleCount = g.Count(),
                                TimeoutCount = g.Count(x => !x.Item2.HasValue),
                                AvgRtt = rtts.Count > 0 ? rtts.Average() : 0,
                                MinRtt = rtts.Count > 0 ? rtts.Min() : 0,
                                MaxRtt = rtts.Count > 0 ? rtts.Max() : 0,
                            };
                            if (bucket.AvgRtt > maxAvg) maxAvg = bucket.AvgRtt;
                            buckets.Add(bucket);
                        }

                        foreach (var b in buckets)
                        {
                            b.BarHeight = b.SampleCount > 0 && b.TimeoutCount < b.SampleCount
                                ? (b.AvgRtt / maxAvg) * 180
                                : 0;
                            b.BarColor = b.SampleCount > 0 && b.TimeoutCount == b.SampleCount
                                ? "#cccccc"
                                : b.AvgRtt < 50 ? "#8bc34a"
                                : b.AvgRtt < 150 ? "#ffc107"
                                : "#f44336";
                        }
                    }
                }
            }
            catch { }
            return buckets;
        }

        public static List<StatusChangeEntry> GetStatusChanges(string hostname, DateTime? from = null, DateTime? to = null)
        {
            var logs = new List<StatusChangeEntry>();
            if (string.IsNullOrEmpty(_connectionString)) return logs;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, timestamp, hostname, alias, status FROM status_change"
                            + BuildWhereClause(hostname, from, to)
                            + " ORDER BY id DESC LIMIT 200";
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                logs.Add(new StatusChangeEntry
                                {
                                    Id = rdr.GetInt64(0),
                                    Timestamp = rdr.GetString(1),
                                    Hostname = rdr.GetString(2),
                                    Alias = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                                    Status = rdr.GetString(4)
                                });
                            }
                        }
                    }
                }
            }
            catch { }
            return logs;
        }

        public static long GetPingLogCount(string hostname = null, DateTime? from = null, DateTime? to = null)
        {
            if (string.IsNullOrEmpty(_connectionString)) return 0;
            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM ping_log"
                            + BuildWhereClause(hostname, from, to);
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        return (long)cmd.ExecuteScalar();
                    }
                }
            }
            catch { return 0; }
        }

        public static int DeletePingLogs(string hostname, DateTime? from, DateTime? to)
        {
            if (string.IsNullOrEmpty(_connectionString)) return 0;
            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM ping_log" + BuildWhereClause(hostname, from, to);
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { return 0; }
        }

        public static int DeleteStatusChanges(string hostname, DateTime? from, DateTime? to)
        {
            if (string.IsNullOrEmpty(_connectionString)) return 0;
            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM status_change" + BuildWhereClause(hostname, from, to);
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { return 0; }
        }
    }
}