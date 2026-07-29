using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace vmPing.Classes
{
    public static partial class DatabaseService
    {
        private static string _connectionString;

        public static void Initialize(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _connectionString = $"Data Source={dbPath};Version=3;";

            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ping_log (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            timestamp TEXT NOT NULL,
                            hostname TEXT NOT NULL,
                            alias TEXT,
                            output TEXT NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS status_change (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            timestamp TEXT NOT NULL,
                            hostname TEXT NOT NULL,
                            alias TEXT,
                            status TEXT NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_ping_log_hostname ON ping_log(hostname);
                        CREATE INDEX IF NOT EXISTS idx_ping_log_timestamp ON ping_log(timestamp);
                        CREATE INDEX IF NOT EXISTS idx_status_change_hostname ON status_change(hostname);
                        CREATE INDEX IF NOT EXISTS idx_status_change_timestamp ON status_change(timestamp);
                    ";
                    cmd.ExecuteNonQuery();
                }

                EnsureRttColumn(conn);
            }
        }

        private static void EnsureRttColumn(SQLiteConnection conn)
        {
            bool hasRttColumn = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(ping_log)";
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        if (rdr.GetString(1).Equals("rtt", StringComparison.OrdinalIgnoreCase))
                        {
                            hasRttColumn = true;
                            break;
                        }
                    }
                }
            }

            if (!hasRttColumn)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE ping_log ADD COLUMN rtt INTEGER";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE ping_log SET rtt = CAST(substr(output, instr(output, '[') + 1) AS INTEGER) WHERE output LIKE '%毫秒]' AND instr(output, '[') > 0";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void InsertPingLog(string hostname, string alias, string output)
        {
            if (string.IsNullOrEmpty(_connectionString)) return;

            int? rtt = TryParseRtt(output);

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO ping_log (timestamp, hostname, alias, output, rtt) VALUES (@t, @h, @a, @o, @r)";
                        cmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        cmd.Parameters.AddWithValue("@h", hostname);
                        cmd.Parameters.AddWithValue("@a", alias ?? "");
                        cmd.Parameters.AddWithValue("@o", output);
                        if (rtt.HasValue)
                            cmd.Parameters.AddWithValue("@r", rtt.Value);
                        else
                            cmd.Parameters.AddWithValue("@r", DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InsertPingLog error: {ex.Message}");
            }
        }

        public static void InsertStatusChange(string hostname, string alias, string status)
        {
            if (string.IsNullOrEmpty(_connectionString)) return;

            try
            {
                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO status_change (timestamp, hostname, alias, status) VALUES (@t, @h, @a, @s)";
                        cmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        cmd.Parameters.AddWithValue("@h", hostname);
                        cmd.Parameters.AddWithValue("@a", alias ?? "");
                        cmd.Parameters.AddWithValue("@s", status);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }
    }
}
