using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class SessionMetrics
    {
        public string SessionId { get; set; }
        public string ProjectName { get; set; }
        public string ModelName { get; set; }
        public long TotalTokens { get; set; }
        public DateTime SessionStart { get; set; }
        public DateTime LastActivity { get; set; }

        public double BurnRate { get; set; }
        public double HoursToTokenLimit { get; set; }
        public double TimeRemainingHours { get; set; }
        public LimitingFactor LimitingFactor { get; set; }
        public double MinimumRemainingHours { get; set; }
        public HealthStatus HealthStatus { get; set; }
    }

    public class JSONLSessionParser
    {
        public List<SessionMetrics> ParseSessions(string projectsPath, TimeSpan activityWindow)
        {
            var sessions = new Dictionary<string, SessionMetrics>();
            var cutoffTime = DateTime.Now.Subtract(activityWindow);

            try
            {
                if (!Directory.Exists(projectsPath))
                    return new List<SessionMetrics>();

                var projectDirs = Directory.GetDirectories(projectsPath);

                foreach (var projectDir in projectDirs)
                {
                    var projectName = Path.GetFileName(projectDir);
                    var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories)
                        .Where(f => !f.Contains("\\subagents\\"))
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .Take(50);  // Only scan 50 most recent files to prevent hanging

                    foreach (var jsonlFile in jsonlFiles)
                    {
                        ProcessJsonlFile(jsonlFile, projectName, sessions, cutoffTime);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing sessions: " + ex.Message);
            }

            var activeSessions = sessions.Values
                .Where(s => s.LastActivity >= cutoffTime)
                .ToList();

            return activeSessions;
        }

        private void ProcessJsonlFile(string filePath, string projectName, Dictionary<string, SessionMetrics> sessions, DateTime cutoffTime)
        {
            try
            {
                var lastLines = ReadLastLines(filePath, 100);

                foreach (var line in lastLines)
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

                        var sessionIdRaw = entry.sessionId;
                        string sessionId = sessionIdRaw == null ? null : ((object)sessionIdRaw).ToString();
                        if (string.IsNullOrEmpty(sessionId)) continue;

                        var timestampRaw = entry.timestamp;
                        string timestampStr = timestampRaw == null ? null : ((object)timestampRaw).ToString();
                        if (string.IsNullOrEmpty(timestampStr)) continue;
                        DateTime timestamp = DateTime.Parse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();

                        if (timestamp < cutoffTime) continue;

                        var message = entry.message;
                        var usage = message == null ? null : message.usage;
                        long inputTokens = usage == null ? 0 : (long)(usage.input_tokens ?? 0);
                        long outputTokens = usage == null ? 0 : (long)(usage.output_tokens ?? 0);
                        long cacheCreationTokens = usage == null ? 0 : (long)(usage.cache_creation_input_tokens ?? 0);
                        long cacheReadTokens = usage == null ? 0 : (long)(usage.cache_read_input_tokens ?? 0);

                        long totalTokens = inputTokens + outputTokens + cacheCreationTokens + cacheReadTokens;
                        string model = "";
                        if (message != null && message.model != null)
                        {
                            model = ((object)message.model).ToString();
                        }

                        if (!sessions.ContainsKey(sessionId))
                        {
                            sessions[sessionId] = new SessionMetrics
                            {
                                SessionId = sessionId,
                                ProjectName = projectName,
                                ModelName = model,
                                TotalTokens = 0,
                                SessionStart = timestamp,
                                LastActivity = timestamp
                            };
                        }
                        else
                        {
                            var session = sessions[sessionId];
                            if (timestamp < session.SessionStart)
                                session.SessionStart = timestamp;
                            if (timestamp > session.LastActivity)
                                session.LastActivity = timestamp;
                        }

                        sessions[sessionId].TotalTokens += totalTokens;

                        if (string.IsNullOrEmpty(sessions[sessionId].ModelName) && !string.IsNullOrEmpty(model))
                        {
                            sessions[sessionId].ModelName = model;
                        }
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing file " + filePath + ": " + ex.Message);
            }
        }

        private IEnumerable<string> ReadLastLines(string filePath, int count)
        {
            var lines = new Queue<string>();

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Enqueue(line);
                    if (lines.Count > count)
                        lines.Dequeue();
                }
            }

            return lines;
        }
    }
}
