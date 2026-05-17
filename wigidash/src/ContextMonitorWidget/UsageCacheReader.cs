using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using WigiLlm.Shared;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class UsageCacheData
    {
        public bool IsValid { get; set; }
        public double FiveHourPct { get; set; }
        public DateTime FiveHourResetUtc { get; set; }
        public double SevenDayPct { get; set; }
        public DateTime SevenDayResetUtc { get; set; }
    }

    public class UsageCacheReader
    {
        private static readonly string CacheFile = WslPaths.ToWindowsPath("/dev/shm/claude_usage_cache.json");
        private string _logFile = @"C:\temp\widget_debug.txt";

        private void LogDebug(string message)
        {
            try { File.AppendAllText(_logFile, "[" + DateTime.Now.ToString("HH:mm:ss") + "] USAGE: " + message + "\n"); }
            catch { }
        }

        public UsageCacheData Read()
        {
            UsageCacheData result = new UsageCacheData();
            try
            {
                if (!File.Exists(CacheFile))
                {
                    LogDebug("Cache file missing: " + CacheFile);
                    return result;
                }
                string json;
                using (FileStream s = new FileStream(CacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader r = new StreamReader(s))
                    json = r.ReadToEnd();
                JObject root = JObject.Parse(json);
                JToken data = root["data"];
                if (data != null)
                {
                    JToken fh = data["five_hour"];
                    if (fh != null)
                    {
                        JToken u = fh["utilization"];
                        if (u != null && u.Type != JTokenType.Null) result.FiveHourPct = u.Value<double>();
                        JToken ra = fh["resets_at"];
                        if (ra != null && ra.Type != JTokenType.Null)
                        {
                            DateTime parsed;
                            if (DateTime.TryParse(ra.Value<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                                result.FiveHourResetUtc = parsed;
                        }
                    }
                    JToken sd = data["seven_day"];
                    if (sd != null)
                    {
                        JToken u = sd["utilization"];
                        if (u != null && u.Type != JTokenType.Null) result.SevenDayPct = u.Value<double>();
                        JToken ra = sd["resets_at"];
                        if (ra != null && ra.Type != JTokenType.Null)
                        {
                            DateTime parsed;
                            if (DateTime.TryParse(ra.Value<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                                result.SevenDayResetUtc = parsed;
                        }
                    }
                }
                result.IsValid = true;
            }
            catch (Exception ex)
            {
                LogDebug("Read error: " + ex.Message);
            }
            return result;
        }

        public static string FormatCountdown(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "?";
            TimeSpan diff = utc.ToLocalTime() - DateTime.Now;
            if (diff.TotalSeconds <= 0) return "now";
            if (diff.TotalHours >= 24) return ((int)diff.TotalDays) + "d" + diff.Hours + "h";
            if (diff.TotalMinutes >= 60) return diff.Hours + "h" + diff.Minutes + "m";
            return diff.Minutes + "m";
        }
    }
}