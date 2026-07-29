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
        public static OverviewStats GetOverviewStatistics(string hostname, DateTime? from, DateTime? to)
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
                        var where = BuildWhereClause(hostname, from, to);
                        cmd.CommandText = "SELECT COUNT(DISTINCT hostname), COUNT(*), AVG(rtt), SUM(CASE WHEN rtt IS NULL THEN 1 ELSE 0 END) FROM ping_log" + where;
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                stats.HostCount = rdr.GetInt32(0);
                                stats.TotalRecords = rdr.GetInt64(1);
                                stats.AvgRtt = rdr.IsDBNull(2) ? 0 : rdr.GetDouble(2);
                                var timeoutTotal = rdr.IsDBNull(3) ? 0 : rdr.GetInt64(3);
                                stats.LossRate = stats.TotalRecords > 0 ? (double)timeoutTotal / stats.TotalRecords * 100 : 0;
                            }
                        }

                        cmd.Parameters.Clear();
                        cmd.CommandText = "SELECT COUNT(*) FROM status_change" + where;
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        stats.StatusChangeCount = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
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
                        cmd.CommandText = @"SELECT
                            COUNT(*),
                            SUM(CASE WHEN rtt IS NULL THEN 1 ELSE 0 END),
                            MIN(rtt), MAX(rtt), AVG(rtt),
                            MAX(timestamp),
                            (SELECT alias FROM ping_log WHERE hostname = @h ORDER BY id DESC LIMIT 1)
                        FROM ping_log" + where;
                        cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                hs.TotalPings = rdr.GetInt64(0);
                                hs.TimeoutCount = (int)(rdr.IsDBNull(1) ? 0 : rdr.GetInt64(1));
                                hs.MinRtt = rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2);
                                hs.MaxRtt = rdr.IsDBNull(3) ? 0 : rdr.GetInt32(3);
                                hs.AvgRtt = rdr.IsDBNull(4) ? 0 : rdr.GetDouble(4);
                                hs.LastActivity = rdr.IsDBNull(5) ? "" : rdr.GetString(5);
                                hs.Alias = rdr.IsDBNull(6) ? "" : rdr.GetString(6);
                                hs.LossRate = hs.TotalPings > 0 ? (double)hs.TimeoutCount / hs.TotalPings * 100 : 0;
                            }
                        }
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        var clauses = new List<string> { "hostname = @h" };
                        if (from.HasValue) clauses.Add("timestamp >= @f");
                        if (to.HasValue) clauses.Add("timestamp <= @t");
                        cmd.CommandText = "SELECT timestamp, status FROM status_change WHERE " + string.Join(" AND ", clauses) + " ORDER BY id ASC";
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

                        if (changes.Count > 0)
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
