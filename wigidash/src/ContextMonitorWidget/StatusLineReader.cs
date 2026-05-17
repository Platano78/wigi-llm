using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using WigiLlm.Shared;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class SessionContext
    {
        public string SessionId { get; set; }
        public long ContextLength { get; set; }
        public long ContextWindowSize { get; set; }
        public string ModelName { get; set; }
        public long TotalTokens { get; set; }
        public double NativeCost { get; set; }
        public DateTime Timestamp { get; set; }

        public float UsagePercentage
        {
            get
            {
                return ContextWindowSize > 0
                    ? (float)(ContextLength * 100.0 / ContextWindowSize)
                    : 0f;
            }
        }

        public long RemainingTokens
        {
            get { return ContextWindowSize - ContextLength; }
        }
    }

    public class McpServerStatus
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public double UptimeSeconds { get; set; }
        public bool IsHealthy
        {
            get { return Status != null && Status.ToLower() == "healthy"; }
        }
    }

    public class StatusLineData
    {
        public SessionContext Session { get; set; }
        public List<McpServerStatus> McpServers { get; set; }
        public bool IsValid { get; set; }
        public string Error { get; set; }
    }

    public class MultiSessionData
    {
        public List<StatusLineData> Sessions { get; set; }
        public int TotalSessions
        {
            get { return Sessions == null ? 0 : Sessions.Count; }
        }
        public int ActiveSessions
        {
            get
            {
                if (Sessions == null) return 0;
                return Sessions.Count(s => s.IsValid && IsRecent(s.Session.Timestamp));
            }
        }

        private bool IsRecent(DateTime timestamp)
        {
            return (DateTime.Now - timestamp).TotalMinutes < 5;
        }
    }

    public class StatusLineReader
    {
        private static readonly string StatusLineDir = WslPaths.ToWindowsPath("/dev/shm");
        private const string StatusLinePattern = "claude_statusline*.json";
        private string _logFile = @"C:\temp\widget_debug.txt";

        private void LogDebug(string message)
        {
            try
            {
                File.AppendAllText(_logFile, "[" + DateTime.Now.ToString("HH:mm:ss") + "] STATUSLINE: " + message + "\n");
            }
            catch { }
        }

        public StatusLineData ReadCurrentStatus()
        {
            // For backward compat - return most recent session
            var multi = ReadAllSessions();
            if (multi.Sessions.Count > 0)
            {
                // Return the most recently updated session
                var recent = multi.Sessions
                    .Where(s => s.IsValid)
                    .OrderByDescending(s => s.Session.Timestamp)
                    .FirstOrDefault();
                return recent ?? multi.Sessions[0];
            }
            return new StatusLineData { IsValid = false, Error = "No sessions found" };
        }

        public MultiSessionData ReadAllSessions()
        {
            var result = new MultiSessionData { Sessions = new List<StatusLineData>() };

            try
            {
                if (!Directory.Exists(StatusLineDir))
                {
                    LogDebug("Status directory not found");
                    return result;
                }

                var files = Directory.GetFiles(StatusLineDir, StatusLinePattern);
                LogDebug("Found " + files.Length + " statusline files");

                foreach (var file in files)
                {
                    var session = ReadSessionFile(file);
                    if (session != null)
                    {
                        result.Sessions.Add(session);
                    }
                }

                // Hide sessions with no activity in the last 10 minutes.
                DateTime staleCutoff = DateTime.Now.Subtract(TimeSpan.FromMinutes(10));
                result.Sessions = result.Sessions
                    .Where(s => s.Session != null && s.Session.Timestamp >= staleCutoff)
                    .OrderByDescending(s => s.Session.Timestamp)
                    .ToList();
                LogDebug("After stale filter: " + result.Sessions.Count + " active sessions");
            }
            catch (Exception ex)
            {
                LogDebug("Error scanning sessions: " + ex.Message);
            }

            return result;
        }

        private StatusLineData ReadSessionFile(string filePath)
        {
            var result = new StatusLineData
            {
                Session = new SessionContext(),
                McpServers = new List<McpServerStatus>(),
                IsValid = false
            };

            try
            {
                // Extract session ID from filename
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileName.StartsWith("claude_statusline_"))
                {
                    result.Session.SessionId = fileName.Replace("claude_statusline_", "");
                }
                else
                {
                    result.Session.SessionId = "default";
                }

                // Read with file sharing
                string json;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                var data = JObject.Parse(json);

                // Parse timestamp
                if (data["timestamp"] != null)
                {
                    double unixTime = data["timestamp"].Value<double>();
                    result.Session.Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)unixTime).LocalDateTime;
                }

                // Parse metrics
                var metrics = data["metrics"];
                if (metrics != null)
                {
                    var ctxLen = metrics["context_length"];
                    result.Session.ContextLength = ctxLen != null ? ctxLen.Value<long>() : 0;
                    var ctxWin = metrics["context_window_size"];
                    result.Session.ContextWindowSize = ctxWin != null ? ctxWin.Value<long>() : 200000;
                    var mdlName = metrics["model_name"];
                    result.Session.ModelName = mdlName != null ? mdlName.Value<string>() : "Unknown";
                    var totTok = metrics["total_tokens"];
                    result.Session.TotalTokens = totTok != null ? totTok.Value<long>() : 0;
                    var natCost = metrics["native_cost"];
                    result.Session.NativeCost = natCost != null ? natCost.Value<double>() : 0;
                }

                // Parse MCP servers
                var servers = data["mcp_servers"] as JArray;
                if (servers != null)
                {
                    foreach (var server in servers)
                    {
                        var nameTok = server["name"];
                        var statusTok = server["status"];
                        var uptimeTok = server["uptime_sec"];
                        result.McpServers.Add(new McpServerStatus
                        {
                            Name = nameTok != null ? nameTok.Value<string>() : "Unknown",
                            Status = statusTok != null ? statusTok.Value<string>() : "unknown",
                            UptimeSeconds = uptimeTok != null ? uptimeTok.Value<double>() : 0
                        });
                    }
                }

                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }
    }
}
