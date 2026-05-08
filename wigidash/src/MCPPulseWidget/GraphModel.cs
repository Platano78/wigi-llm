using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MCPPulseWidget
{
    /// <summary>
    /// Health state for a graph node, derived from poll results.
    ///
    /// Healthy: status=="connected" AND last successful poll <= 10s ago
    /// Warning: stale 10-30s OR status="disconnected" without an error message
    /// Critical: stale > 30s OR last 3 polls failed OR status="disconnected" with error
    /// </summary>
    public enum NodeHealth
    {
        Healthy,
        Warning,
        Critical
    }

    /// <summary>
    /// A single node in the MCP server topology graph.
    /// </summary>
    public class GraphNode
    {
        public string Name;
        public string DisplayName;
        public int ToolsCount;
        public bool IsConnected;
        public NodeHealth Health;
        public int ConsecutiveFailures;
        public DateTime LastPollTime;
        public DateTime LastSuccessTime;
        public float OrbitAngle;
        public float OrbitRadius;
        public float PulsePhase;
        public float Radius;

        // Activity tracking
        public int TotalCalls;
        public DateTime LastCallTime;
        public string LastToolName;
        public bool LastSuccess;
        public int FlashFrames; // remaining frames for red flash on failure

        // Health state colors (spec-compliant)
        // Healthy = green cyan-leaning #00FFB0
        // Warning = amber #FFB000
        // Critical = red #FF3030
        private static readonly Color HealthyColor = Color.FromArgb(200, 0, 255, 176);
        private static readonly Color WarningColor = Color.FromArgb(200, 255, 176, 0);
        private static readonly Color CriticalColor = Color.FromArgb(200, 255, 48, 48);

        public GraphNode()
        {
            Name = "";
            DisplayName = "";
            ToolsCount = 0;
            IsConnected = false;
            Health = NodeHealth.Critical;
            ConsecutiveFailures = 0;
            LastPollTime = DateTime.MinValue;
            LastSuccessTime = DateTime.MinValue;
            OrbitAngle = 0f;
            OrbitRadius = 100f;
            PulsePhase = 0f;
            Radius = 18f;
            TotalCalls = 0;
            LastCallTime = DateTime.MinValue;
            LastToolName = "";
            LastSuccess = true;
            FlashFrames = 0;
        }

        /// <summary>
        /// Update health state based on poll result.
        /// </summary>
        public void UpdateHealth(bool connected, int consecutiveFailures, DateTime now)
        {
            ConsecutiveFailures = consecutiveFailures;
            IsConnected = connected;
            LastPollTime = now;

            if (connected)
            {
                LastSuccessTime = now;
            }

            int secondsSincePoll = (int)(now - LastPollTime).TotalSeconds;
            int secondsSinceSuccess = (int)(now - LastSuccessTime).TotalSeconds;

            if (connected && secondsSincePoll <= 10)
            {
                Health = NodeHealth.Healthy;
            }
            else if (secondsSincePoll <= 30 || (secondsSinceSuccess <= 30 && !connected))
            {
                Health = NodeHealth.Warning;
            }
            else
            {
                Health = NodeHealth.Critical;
            }
        }

        /// <summary>
        /// Returns the color for this node's current health state.
        /// </summary>
        public Color GetColor()
        {
            if (Health == NodeHealth.Healthy)
                return HealthyColor;
            if (Health == NodeHealth.Warning)
                return WarningColor;
            return CriticalColor;
        }

        /// <summary>
        /// Returns a dimmer color for the outer glow ring.
        /// </summary>
        public Color GetGlowColor()
        {
            if (Health == NodeHealth.Healthy)
                return Color.FromArgb(60, 0, 200, 140);
            if (Health == NodeHealth.Warning)
                return Color.FromArgb(60, 200, 160, 0);
            return Color.FromArgb(60, 180, 40, 40);
        }

        /// <summary>
        /// Compute node radius from tools_count: base + 4 * sqrt(tools_count), clamped 6..20.
        /// </summary>
        public void ComputeRadius()
        {
            float r = 6f + 4f * (float)Math.Sqrt((double)ToolsCount);
            if (r > 20f) r = 20f;
            if (r < 6f) r = 6f;
            Radius = r;
        }
    }

    /// <summary>
    /// A connection between two nodes. Used for drawing energy conduits.
    /// </summary>
    public class GraphEdge
    {
        public GraphNode Source;
        public GraphNode Target;

        public GraphEdge() { }

        public GraphEdge(GraphNode source, GraphNode target)
        {
            Source = source;
            Target = target;
        }
    }

    /// <summary>
    /// A single activity event from the MCP log.
    /// </summary>
    public class ActivityEvent
    {
        public string ServerName;
        public string ToolName;
        public bool Success;
        public double DurationMs;
        public DateTime Timestamp;
    }

    /// <summary>
    /// A beam animation event: a node firing toward center.
    /// </summary>
    public class BeamEvent
    {
        public string ServerName;
        public double SpawnTime; // seconds since epoch
        public bool Success;
    }

    /// <summary>
    /// The graph model: nodes, edges, health states, and activity tracking.
    /// </summary>
    public class GraphModel
    {
        public List<GraphNode> Nodes;
        public List<GraphEdge> Edges;
        public List<ActivityEvent> PendingEvents;
        public List<BeamEvent> ActiveBeams;

        // Health counters
        public int TotalHealthy;
        public int TotalWarning;
        public int TotalCritical;
        public int GatewayConsecutiveFailures;
        public int GatewayUptimeSeconds;

        // Activity counters
        public int TotalCalls;
        public int ErrorCount;
        public string LastToolName;
        public DateTime LastGlobalCallTime;
        public Queue<DateTime> CallsPerMinuteWindow;

        public GraphModel()
        {
            Nodes = new List<GraphNode>();
            Edges = new List<GraphEdge>();
            PendingEvents = new List<ActivityEvent>();
            ActiveBeams = new List<BeamEvent>();
            TotalHealthy = 0;
            TotalWarning = 0;
            TotalCritical = 0;
            GatewayConsecutiveFailures = 0;
            GatewayUptimeSeconds = 0;
            TotalCalls = 0;
            ErrorCount = 0;
            LastToolName = "";
            LastGlobalCallTime = DateTime.MinValue;
            CallsPerMinuteWindow = new Queue<DateTime>();
        }

        /// <summary>
        /// Clear all nodes and rebuild from scratch.
        /// </summary>
        public void Clear()
        {
            Nodes.Clear();
            Edges.Clear();
            PendingEvents.Clear();
            ActiveBeams.Clear();
            CallsPerMinuteWindow.Clear();
            TotalHealthy = 0;
            TotalWarning = 0;
            TotalCritical = 0;
            GatewayConsecutiveFailures = 0;
            GatewayUptimeSeconds = 0;
            TotalCalls = 0;
            ErrorCount = 0;
            LastToolName = "";
            LastGlobalCallTime = DateTime.MinValue;
        }

        /// <summary>
        /// Add a node and connect it to the center gateway node.
        /// Returns the added node for further configuration.
        /// </summary>
        public GraphNode AddNode(string name, string displayName, int toolsCount)
        {
            GraphNode node = new GraphNode();
            node.Name = name;
            node.DisplayName = displayName;
            node.ToolsCount = toolsCount;
            node.Health = NodeHealth.Critical;
            node.LastPollTime = DateTime.MinValue;
            node.ComputeRadius();
            Nodes.Add(node);
            return node;
        }

        /// <summary>
        /// Connect a peripheral node to the center gateway node.
        /// </summary>
        public void ConnectToGateway(GraphNode peripheral)
        {
            GraphNode center = GetCenterNode();
            if (center != null)
            {
                Edges.Add(new GraphEdge(center, peripheral));
            }
        }

        public GraphNode GetCenterNode()
        {
            if (Nodes.Count > 0)
                return Nodes[0];
            return null;
        }

        public List<GraphNode> GetPeripheralNodes()
        {
            var result = new List<GraphNode>();
            for (int i = 1; i < Nodes.Count; i++)
            {
                result.Add(Nodes[i]);
            }
            return result;
        }

        /// <summary>
        /// Find a peripheral node by name (case-insensitive).
        /// </summary>
        public GraphNode FindPeripheralNode(string name)
        {
            for (int i = 1; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return Nodes[i];
            }
            return null;
        }

        /// <summary>
        /// Register an activity event and create a beam event.
        /// Under caller lock.
        /// </summary>
        public void RegisterActivity(string serverName, string toolName, bool success, double durationMs)
        {
            TotalCalls++;
            LastGlobalCallTime = DateTime.UtcNow;
            LastToolName = toolName;
            CallsPerMinuteWindow.Enqueue(DateTime.UtcNow);

            if (!success)
            {
                ErrorCount++;
            }

            // Find or create peripheral node for this server
            GraphNode node = FindPeripheralNode(serverName);
            if (node == null)
            {
                node = AddNode(serverName, FormatDisplayName(serverName), 0);
                ConnectToGateway(node);
            }

            node.TotalCalls++;
            node.LastCallTime = DateTime.UtcNow;
            node.LastToolName = toolName;
            node.LastSuccess = success;

            if (success)
            {
                node.LastSuccessTime = DateTime.UtcNow;
            }

            // Trigger flash on failure
            if (!success)
            {
                node.FlashFrames = 3;
            }

            // Create a beam event
            BeamEvent beam = new BeamEvent();
            beam.ServerName = serverName;
            beam.Success = success;
            beam.SpawnTime = DateTime.UtcNow.ToOADate();
            ActiveBeams.Add(beam);
        }

        /// <summary>
        /// Update calls-per-minute using a sliding 60-second window.
        /// Returns the current calls/min value.
        /// </summary>
        public int UpdateCallsPerMinute()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (CallsPerMinuteWindow.Count > 0 && CallsPerMinuteWindow.Peek() < cutoff)
            {
                CallsPerMinuteWindow.Dequeue();
            }
            return CallsPerMinuteWindow.Count;
        }

        /// <summary>
        /// Expire old beam events (older than 2 seconds).
        /// </summary>
        public void ExpireBeams()
        {
            double now = DateTime.UtcNow.ToOADate();
            for (int i = ActiveBeams.Count - 1; i >= 0; i--)
            {
                double age = (now - ActiveBeams[i].SpawnTime) * 86400.0; // convert to seconds
                if (age > 2.0)
                {
                    ActiveBeams.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Decrement flash frames on all nodes.
        /// </summary>
        public void TickFlashFrames()
        {
            for (int i = 1; i < Nodes.Count; i++)
            {
                if (Nodes[i].FlashFrames > 0)
                {
                    Nodes[i].FlashFrames--;
                }
            }
        }

        /// <summary>
        /// Recompute health counters from node states.
        /// </summary>
        public void RecomputeHealthCounts()
        {
            TotalHealthy = 0;
            TotalWarning = 0;
            TotalCritical = 0;
            for (int i = 1; i < Nodes.Count; i++)
            {
                if (Nodes[i].Health == NodeHealth.Healthy)
                    TotalHealthy++;
                else if (Nodes[i].Health == NodeHealth.Warning)
                    TotalWarning++;
                else
                    TotalCritical++;
            }
        }

        private static string FormatDisplayName(string name)
        {
            string result = name.Replace('-', ' ').Replace('_', ' ');
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);
            return result;
        }
    }
}
