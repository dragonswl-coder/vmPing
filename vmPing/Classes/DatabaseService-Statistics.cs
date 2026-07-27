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
                        cmd.CommandText = "SELECT COUNT(DISTINCT hostname), COUNT(*) FROM ping_log" + where;
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
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
                        if (!string.IsNullOrEmpty(hostname))
                            cmd.Parameters.AddWithValue("@h", hostname);
                        AddDateParams(cmd, from, to);
                        stats.StatusChangeCount = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                if (!string.IsNullOrEmpty(hostname))
                {
                    var hs = GetHostStatistics(hostname, from, to);
                    stats.AvgRtt = hs.AvgRtt;
                    stats.LossRate = hs.LossRate;
                }
                else
                {
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
