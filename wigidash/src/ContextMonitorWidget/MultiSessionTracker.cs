using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WigiLlm.Shared;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class MultiSessionTracker
    {
        private readonly JSONLSessionParser _sessionParser;
        private readonly BurnRateCalculator _burnRateCalculator;
        private static readonly string BasePath = WslPaths.ToWindowsPath("/home/platano/.claude/projects");
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromHours(5);
        private const int MaxSessions = 6;
        private string _logFile = @"C:\temp\widget_debug.txt";

        public MultiSessionTracker()
        {
            _sessionParser = new JSONLSessionParser();
            _burnRateCalculator = new BurnRateCalculator();
        }

        private void LogDebug(string message)
        {
            try
            {
                System.IO.File.AppendAllText(_logFile, "[" + DateTime.Now.ToString("HH:mm:ss") + "] TRACKER: " + message + "\n");
            }
            catch { }
        }

        public List<SessionMetrics> GetActiveSessions()
        {
            LogDebug("Checking path: " + BasePath);

            if (!Directory.Exists(BasePath))
            {
                LogDebug("Base path does not exist!");
                return new List<SessionMetrics>();
            }

            try
            {
                var allSessions = _sessionParser.ParseSessions(BasePath, SessionTimeout);
                LogDebug("Total sessions with activity in last 5 hours: " + allSessions.Count);

                foreach (var session in allSessions)
                {
                    _burnRateCalculator.CalculateBurnRate(session);
                }

                var topSessions = allSessions
                    .OrderByDescending(s => s.LastActivity)
                    .Take(MaxSessions)
                    .ToList();

                LogDebug("Returning top " + topSessions.Count + " sessions");
                return topSessions;
            }
            catch (Exception ex)
            {
                LogDebug("Error getting active sessions: " + ex.Message);
                return new List<SessionMetrics>();
            }
        }

    }
}
