using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class WeeklyUsage
    {
        public decimal TotalSpent { get; set; }
        public decimal WeeklyLimit { get; set; }
        public float Percentage
        {
            get { return (float)(TotalSpent / WeeklyLimit * 100); }
        }
        public TimeSpan TimeUntilReset { get; set; }
        public DateTime WeekStartDate { get; set; }
    }

    public class WeeklyUsageAggregator
    {
        private const string BasePath = @"\\wsl$\Ubuntu\home\platano\.claude\projects";
        private const decimal DefaultWeeklyLimit = 100.00m;
        private string _logFile = @"C:\temp\widget_debug.txt";

        private void LogDebug(string message)
        {
            try
            {
                System.IO.File.AppendAllText(_logFile, "[" + DateTime.Now.ToString("HH:mm:ss") + "] COST: " + message + "\n");
            }
            catch { }
        }

        public WeeklyUsage GetWeeklyUsage()
        {
            var weekStart = GetCurrentWeekStart();
            LogDebug("Week starts at: " + weekStart.ToString("yyyy-MM-dd HH:mm:ss"));
            LogDebug("Current time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            var totalCost = CalculateTotalCost(weekStart);
            var timeUntilReset = CalculateTimeUntilReset();

            LogDebug("Total calculated cost: $" + totalCost.ToString("F2"));

            return new WeeklyUsage
            {
                TotalSpent = totalCost,
                WeeklyLimit = DefaultWeeklyLimit,
                TimeUntilReset = timeUntilReset,
                WeekStartDate = weekStart
            };
        }

        private DateTime GetCurrentWeekStart()
        {
            var now = DateTime.Now;
            var daysSinceThursday = ((int)now.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
            var lastThursday = now.Date.AddDays(-daysSinceThursday);
            var resetTime = lastThursday.AddHours(13).AddMinutes(59);

            if (now < resetTime)
            {
                resetTime = resetTime.AddDays(-7);
            }

            return resetTime;
        }

        private TimeSpan CalculateTimeUntilReset()
        {
            var now = DateTime.Now;
            var currentWeekStart = GetCurrentWeekStart();
            var nextReset = currentWeekStart.AddDays(7);
            return nextReset - now;
        }

        private decimal CalculateTotalCost(DateTime weekStart)
        {
            decimal totalCost = 0;
            int filesProcessed = 0;
            int entriesProcessed = 0;
            int entriesInWeek = 0;

            try
            {
                if (!Directory.Exists(BasePath))
                {
                    LogDebug("Base path does not exist");
                    return 0;
                }

                var projectDirs = Directory.GetDirectories(BasePath);
                LogDebug("Scanning " + projectDirs.Length + " project directories");

                foreach (var projectDir in projectDirs)
                {
                    var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories)
                        .Where(f => !f.Contains("\\subagents\\"));

                    foreach (var jsonlFile in jsonlFiles)
                    {
                        var fileModified = File.GetLastWriteTime(jsonlFile);
                        if (fileModified >= weekStart)
                        {
                            filesProcessed++;
                            var fileCost = CalculateFileCost(jsonlFile, weekStart, ref entriesProcessed, ref entriesInWeek);
                            totalCost += fileCost;
                        }
                    }
                }

                LogDebug("Files processed: " + filesProcessed + ", Entries: " + entriesProcessed + ", In week: " + entriesInWeek);
            }
            catch (Exception ex)
            {
                LogDebug("Error: " + ex.Message);
            }

            return totalCost;
        }

        private decimal CalculateFileCost(string filePath, DateTime weekStart, ref int entriesProcessed, ref int entriesInWeek)
        {
            decimal fileCost = 0;
            bool loggedSample = false;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var settings = new JsonSerializerSettings
                            {
                                DateParseHandling = DateParseHandling.None
                            };
                            var entry = JsonConvert.DeserializeObject<dynamic>(line, settings);

                            if (entry == null) continue;
                            var entryType = entry.type;
                            if (entryType == null || ((object)entryType).ToString() != "assistant") continue;

                            entriesProcessed++;

                            var timestampRaw = entry.timestamp;
                            string timestampStr = timestampRaw == null ? null : ((object)timestampRaw).ToString();
                            if (string.IsNullOrEmpty(timestampStr)) continue;
                            DateTime entryTimeUtc = DateTime.Parse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
                            DateTime entryTimeLocal = entryTimeUtc.ToLocalTime();

                            if (!loggedSample && entriesProcessed <= 2)
                            {
                                LogDebug("Sample: '" + timestampStr + "' -> UTC:" + entryTimeUtc.ToString("yyyy-MM-dd HH:mm:ss") + " -> Local:" + entryTimeLocal.ToString("yyyy-MM-dd HH:mm:ss"));
                                LogDebug("Compare: Local(" + entryTimeLocal.ToString("yyyy-MM-dd HH:mm") + ") >= Week(" + weekStart.ToString("yyyy-MM-dd HH:mm") + ") = " + (entryTimeLocal >= weekStart));
                                loggedSample = true;
                            }

                            if (entryTimeLocal < weekStart) continue;

                            entriesInWeek++;

                            var message = entry.message;
                            string model = "";
                            if (message != null && message.model != null)
                            {
                                model = ((object)message.model).ToString();
                            }
                            var usage = message == null ? null : message.usage;
                            long inputTokens = usage == null ? 0 : (long)(usage.input_tokens ?? 0);
                            long outputTokens = usage == null ? 0 : (long)(usage.output_tokens ?? 0);
                            long cacheCreationTokens = usage == null ? 0 : (long)(usage.cache_creation_input_tokens ?? 0);
                            long cacheReadTokens = usage == null ? 0 : (long)(usage.cache_read_input_tokens ?? 0);

                            fileCost += CalculateCost(model, inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens);
                        }
                        catch (JsonException)
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("Error in file " + Path.GetFileName(filePath) + ": " + ex.Message);
            }

            return fileCost;
        }

        private decimal CalculateCost(string model, long inputTokens, long outputTokens, long cacheCreationTokens, long cacheReadTokens)
        {
            var rates = model.ToLower().Contains("opus")
                ? new { input = 15m, output = 75m, cacheCreation = 18.75m, cacheRead = 1.5m }
                : new { input = 3m, output = 15m, cacheCreation = 3.75m, cacheRead = 0.3m };

            return (inputTokens * rates.input +
                    outputTokens * rates.output +
                    cacheCreationTokens * rates.cacheCreation +
                    cacheReadTokens * rates.cacheRead) / 1000000m;
        }
    }
}
