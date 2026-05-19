using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using WigiLlm.Shared;

namespace MCPPulseWidget
{
    /// <summary>
    /// Log-tailing helper: tracks last position, reads new bytes only,
    /// defensively parses JSON lines from mcp.log.
    ///
    /// Each new line is JSON like:
    /// {"tool_name":"server::tool","server":"<name>","tool":"<name>","success":bool,"duration":<ms>,"timestamp":"ISO"}
    ///
    /// Uses JavaScriptSerializer (no Newtonsoft — minimal deps).
    /// Skips silently on parse failure.
    /// </summary>
    public class ActivityLog
    {
        private static readonly string McpLogPath =
            WslPaths.UnderHome(".claude/logs/mcp.log");

        private long _lastLogPosition = 0;
        private readonly Queue<ActivityEvent> _eventQueue = new Queue<ActivityEvent>();
        private readonly object _queueLock = new object();

        /// <summary>
        /// Initialize or re-initialize file position tracking.
        /// Called at startup and after file rotation errors.
        /// </summary>
        public void Initialize()
        {
            try
            {
                var fi = new FileInfo(McpLogPath);
                if (fi.Exists)
                {
                    _lastLogPosition = fi.Length;
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Poll for new lines since last read. Dequeues any new ActivityEvents.
        /// Returns true if new lines were read (regardless of parse success).
        /// Under caller lock.
        /// </summary>
        public bool Poll()
        {
            try
            {
                var fi = new FileInfo(McpLogPath);
                if (!fi.Exists)
                {
                    return false;
                }

                if (fi.Length <= _lastLogPosition)
                    return false;

                using (var fs = new FileStream(McpLogPath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_lastLogPosition, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            ParseLine(line);
                        }
                        _lastLogPosition = fs.Position;
                    }
                }

                return true;
            }
            catch
            {
                // File may have been rotated or moved
                _lastLogPosition = 0;
                return false;
            }
        }

        /// <summary>
        /// Parse a single log line into an ActivityEvent if valid.
        /// Defensive: handles malformed lines, partial writes, missing fields.
        /// </summary>
        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(line);
                if (dict == null) return;

                string serverName = null;
                string toolName = null;
                bool success = true;
                double durationMs = 0;

                // Extract server name
                if (dict.ContainsKey("server") && dict["server"] != null)
                    serverName = dict["server"].ToString();

                // Extract tool name (try tool_name first, then tool)
                if (dict.ContainsKey("tool_name") && dict["tool_name"] != null)
                    toolName = dict["tool_name"].ToString();
                else if (dict.ContainsKey("tool") && dict["tool"] != null)
                    toolName = dict["tool"].ToString();

                // Extract success
                if (dict.ContainsKey("success"))
                {
                    object val = dict["success"];
                    if (val is bool)
                        success = (bool)val;
                    else
                    {
                        string sval = val != null ? val.ToString() : null;
                        if (sval != null)
                            success = !string.Equals(sval, "false", StringComparison.OrdinalIgnoreCase);
                    }
                }

                // Override on error field
                if (dict.ContainsKey("error") && dict["error"] != null)
                {
                    string errVal = dict["error"] != null ? dict["error"].ToString() : null;
                    if (!string.IsNullOrEmpty(errVal))
                        success = false;
                }

                // Extract duration
                if (dict.ContainsKey("duration") && dict["duration"] != null)
                {
                    double dur;
                    if (double.TryParse(dict["duration"].ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out dur))
                    {
                        durationMs = dur;
                    }
                }

                // Only enqueue if we got meaningful data
                if (!string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(toolName))
                {
                    ActivityEvent evt = new ActivityEvent();
                    evt.ServerName = serverName;
                    evt.ToolName = toolName;
                    evt.Success = success;
                    evt.DurationMs = durationMs;
                    evt.Timestamp = DateTime.UtcNow;

                    lock (_queueLock)
                    {
                        _eventQueue.Enqueue(evt);
                    }
                }
            }
            catch
            {
                // Malformed JSON or unexpected structure — skip silently
            }
        }

        /// <summary>
        /// Dequeue all pending events. Returns a snapshot list.
        /// Thread-safe via internal lock.
        /// </summary>
        public List<ActivityEvent> DequeueAll()
        {
            lock (_queueLock)
            {
                if (_eventQueue.Count == 0)
                    return null;

                var result = new List<ActivityEvent>(_eventQueue.Count);
                while (_eventQueue.Count > 0)
                {
                    result.Add(_eventQueue.Dequeue());
                }
                return result;
            }
        }
    }
}
