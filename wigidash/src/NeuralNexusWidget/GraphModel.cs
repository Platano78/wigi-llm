using System;
using System.Drawing;

namespace NeuralNexusWidget
{
    /// <summary>
    /// Health state for a graph node, derived from poll results.
    /// </summary>
    public enum NodeHealth
    {
        Healthy,
        Warning,
        Critical
    }

    /// <summary>
    /// A single node in the neural network graph.
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
        public float OrbitAngle;   // radians, for orbital position
        public float OrbitRadius;  // pixels from center
        public float PulsePhase;   // 0..1 for pulsing animation
        public float Radius;       // visual radius of the node circle

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
        }

        /// <summary>
        /// Update health state based on poll result.
        /// Green if connected and polled within last 10s.
        /// Amber if stale (last poll > 10s ago but not > 30s).
        /// Red if disconnected or 3+ consecutive failures.
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

            int secondsSinceSuccess = (int)(now - LastSuccessTime).TotalSeconds;
            int secondsSincePoll = (int)(now - LastPollTime).TotalSeconds;

            if (connected && secondsSincePoll <= 10)
            {
                Health = NodeHealth.Healthy;
            }
            else if (secondsSincePoll <= 30)
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
        /// Uses alpha blending for neon glow effect.
        /// </summary>
        public Color GetColor()
        {
            if (Health == NodeHealth.Healthy)
                return Color.FromArgb(200, 0, 230, 255);    // cyan
            if (Health == NodeHealth.Warning)
                return Color.FromArgb(200, 255, 200, 0);    // amber
            return Color.FromArgb(200, 255, 60, 60);        // red
        }

        /// <summary>
        /// Returns a dimmer color for the outer glow ring.
        /// </summary>
        public Color GetGlowColor()
        {
            if (Health == NodeHealth.Healthy)
                return Color.FromArgb(60, 0, 180, 220);
            if (Health == NodeHealth.Warning)
                return Color.FromArgb(60, 200, 160, 0);
            return Color.FromArgb(60, 180, 40, 40);
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
    /// The graph model: a list of nodes and edges.
    /// Populated by polling the MCP Gateway /health endpoint.
    /// </summary>
    public class GraphModel
    {
        public System.Collections.Generic.List<GraphNode> Nodes;
        public System.Collections.Generic.List<GraphEdge> Edges;

        public GraphModel()
        {
            Nodes = new System.Collections.Generic.List<GraphNode>();
            Edges = new System.Collections.Generic.List<GraphEdge>();
        }

        /// <summary>
        /// Clear all nodes and rebuild from scratch.
        /// </summary>
        public void Clear()
        {
            Nodes.Clear();
            Edges.Clear();
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
            node.Health = NodeHealth.Critical; // start offline until first poll
            node.LastPollTime = DateTime.MinValue;
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

        public System.Collections.Generic.List<GraphNode> GetPeripheralNodes()
        {
            var result = new System.Collections.Generic.List<GraphNode>();
            for (int i = 1; i < Nodes.Count; i++)
            {
                result.Add(Nodes[i]);
            }
            return result;
        }
    }
}
