using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace MCPPulseWidget
{
    /// <summary>
    /// MCPPulse Widget Instance — unified MCP server topology + activity visualization.
    ///
    /// Two data layers:
    ///   Layer 1: Topology — polls /health every 2s, builds orbital node graph
    ///   Layer 2: Activity — tails mcp.log, fires beam animations on tool calls
    ///
    /// Two background threads, each with top-level try/catch.
    /// Render runs on the WigiDash draw thread, acquires mutex briefly to snapshot state.
    /// </summary>
    public class MCPPulseWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public System.Windows.Controls.UserControl GetSettingsControl() { return null; }

        // Bitmap and drawing
        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;

        // Threading
        private Thread _pollThread;
        private Thread _tailThread;
        private volatile bool _runPoll = false;
        private volatile bool _runTail = false;
        private volatile bool _pausePoll = false;
        private volatile bool _pauseTail = false;
        private const int PollIntervalMs = 2000;
        private const int TailIntervalMs = 200;

        // HTTP client
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        // MCP Gateway URL
        private const string GatewayUrl = "http://127.0.0.1:8090";

        // Graph data
        private GraphModel _graph;
        private readonly object _graphLock = new object();

        // Activity log tailer
        private ActivityLog _activityLog;

        // Animation state
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private DateTime _lastRotationTime = DateTime.UtcNow;
        private int _animFrame = 0;

        // Render modes (cycled by double-tap)
        private int _renderMode = 0;
        private const int RenderModeFull = 0;
        private const int RenderModeTopology = 1;
        private const int RenderModeActivity = 2;

        // Double-tap detection
        private const int DoubleTapWindowMs = 400;
        private DateTime _lastSingleTapTime = DateTime.MinValue;
        private CancellationTokenSource _pendingSingleAction;

        // Stop requested (for future use)
        private bool _stopRequested = false;
        private DateTime _stopFlashTime = DateTime.MinValue;

        // ==================================================================
        //  CONSTRUCTOR
        // ==================================================================

        public MCPPulseWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            int w = size.Width > 0 ? size.Width : 384;
            int h = size.Height > 0 ? size.Height : 256;
            BitmapCurrent = new Bitmap(w, h, PixelFormat.Format16bppRgb565);

            // Initialize graph model
            _graph = new GraphModel();
            _activityLog = new ActivityLog();
            try { InitializeGraphModel(); } catch { }

            // Initial paint — never let an exception escape the constructor
            try { DrawFrame(); } catch { }
            try { StartPoll(); } catch { }
            try { StartTail(); } catch { }
        }

        // ==================================================================
        //  GRAPH INITIALIZATION
        // ==================================================================

        private void InitializeGraphModel()
        {
            lock (_graphLock)
            {
                _graph.Clear();

                // Center node: MCP Gateway itself
                GraphNode gateway = _graph.AddNode("gateway", "MCP", 0);
                gateway.Radius = 28f;
                gateway.OrbitRadius = 0f;
            }
        }

        private static string FormatDisplayName(string name)
        {
            string result = name.Replace('-', ' ').Replace('_', ' ');
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);
            return result;
        }

        // ==================================================================
        //  POLL THREAD — Layer 1: Topology
        // ==================================================================

        private void StartPoll()
        {
            _pausePoll = false;
            _runPoll = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "MCPPulse-Poll";
            _pollThread.Start();
        }

        private void PollLoop()
        {
            while (_runPoll)
            {
                try
                {
                    PollTick();
                }
                catch
                {
                    // Never let an exception escape — kills the host process
                }
                Thread.Sleep(PollIntervalMs);
            }
        }

        private void PollTick()
        {
            if (_pausePoll) return;

            try
            {
                FetchHealthData();
            }
            catch { }

            try { UpdateVisualizationState(); } catch { }

            // Draw the frame after poll tick
            if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    DrawFrame();
                    SignalUpdate();
                }
                finally
                {
                    try { _drawingMutex.ReleaseMutex(); } catch { }
                }
            }
        }

        // ==================================================================
        //  DATA FETCHING — MCP Gateway /health
        // ==================================================================

        private void FetchHealthData()
        {
            string url = GatewayUrl + "/health";
            string json = null;
            try
            {
                json = _httpClient.GetStringAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                MarkAllNodesStale(DateTime.UtcNow);
                return;
            }

            if (string.IsNullOrEmpty(json))
            {
                MarkAllNodesStale(DateTime.UtcNow);
                return;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                if (root == null)
                {
                    MarkAllNodesStale(DateTime.UtcNow);
                    return;
                }

                string status = "";
                if (root.ContainsKey("status"))
                {
                    object statusObj = root["status"];
                    if (statusObj != null)
                        status = statusObj.ToString();
                }

                DateTime now = DateTime.UtcNow;

                if (status == "ok")
                {
                    _graph.GatewayConsecutiveFailures = 0;

                    // Parse uptime
                    if (root.ContainsKey("uptime_seconds"))
                    {
                        int uptime;
                        if (int.TryParse(root["uptime_seconds"].ToString(),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out uptime))
                        {
                            _graph.GatewayUptimeSeconds = uptime;
                        }
                    }

                    // Parse servers
                    if (root.ContainsKey("servers"))
                    {
                        object serversObj = root["servers"];
                        Dictionary<string, object> servers = serversObj as Dictionary<string, object>;
                        if (servers != null)
                        {
                            UpdateGraphFromServers(servers, now);
                        }
                    }
                }
                else
                {
                    _graph.GatewayConsecutiveFailures++;
                    MarkAllNodesStale(now);
                }
            }
            catch
            {
                _graph.GatewayConsecutiveFailures++;
                MarkAllNodesStale(DateTime.UtcNow);
            }
        }

        private void UpdateGraphFromServers(Dictionary<string, object> servers, DateTime now)
        {
            lock (_graphLock)
            {
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Update existing peripheral nodes
                for (int i = 1; i < _graph.Nodes.Count; i++)
                {
                    GraphNode node = _graph.Nodes[i];
                    string serverName = node.Name;

                    if (!servers.ContainsKey(serverName))
                        continue;

                    seenNames.Add(serverName);

                    var serverData = servers[serverName] as Dictionary<string, object>;
                    if (serverData == null)
                        continue;

                    string serverStatus = "";
                    if (serverData.ContainsKey("status"))
                    {
                        object s = serverData["status"];
                        if (s != null) serverStatus = s.ToString();
                    }

                    int toolsCount = 0;
                    if (serverData.ContainsKey("tools_count"))
                    {
                        object tc = serverData["tools_count"];
                        if (tc != null)
                        {
                            int parsed;
                            if (int.TryParse(tc.ToString(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out parsed))
                                toolsCount = parsed;
                        }
                    }

                    bool connected = (serverStatus == "connected");
                    node.ToolsCount = toolsCount;
                    node.ComputeRadius();
                    node.UpdateHealth(connected, _graph.GatewayConsecutiveFailures, now);
                }

                // Add new nodes not in existing list
                foreach (var kvp in servers)
                {
                    string serverName = kvp.Key;
                    if (seenNames.Contains(serverName))
                        continue;

                    bool found = false;
                    for (int i = 1; i < _graph.Nodes.Count; i++)
                    {
                        if (string.Equals(_graph.Nodes[i].Name, serverName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) continue;

                    var serverData = kvp.Value as Dictionary<string, object>;
                    string status = "";
                    int toolsCount = 0;
                    if (serverData != null)
                    {
                        if (serverData.ContainsKey("status"))
                        {
                            object s = serverData["status"];
                            if (s != null) status = s.ToString();
                        }
                        if (serverData.ContainsKey("tools_count"))
                        {
                            object tc = serverData["tools_count"];
                            if (tc != null)
                            {
                                int parsed;
                                if (int.TryParse(tc.ToString(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out parsed))
                                    toolsCount = parsed;
                            }
                        }
                    }

                    GraphNode newNode = _graph.AddNode(serverName,
                        FormatDisplayName(serverName), toolsCount);
                    newNode.ComputeRadius();
                    newNode.UpdateHealth(status == "connected",
                        _graph.GatewayConsecutiveFailures, now);
                    _graph.ConnectToGateway(newNode);
                }

                // Re-initialize orbital layout
                int w = BitmapCurrent != null ? BitmapCurrent.Width : 384;
                int h = BitmapCurrent != null ? BitmapCurrent.Height : 256;
                int minDim = Math.Min(w, h);
                float orbitRadius = (float)(minDim * 0.35);
                int cx = w / 2;
                int cy = h / 2;
                OrbitalEngine.InitializeLayout(_graph, cx, cy, orbitRadius);

                // Recompute health counters
                _graph.RecomputeHealthCounts();
            }
        }

        private void MarkAllNodesStale(DateTime now)
        {
            lock (_graphLock)
            {
                _graph.GatewayConsecutiveFailures++;

                for (int i = 1; i < _graph.Nodes.Count; i++)
                {
                    _graph.Nodes[i].UpdateHealth(false,
                        _graph.GatewayConsecutiveFailures, now);
                }
                _graph.RecomputeHealthCounts();
            }
        }

        // ==================================================================
        //  TAIL THREAD — Layer 2: Activity
        // ==================================================================

        private void StartTail()
        {
            _pauseTail = false;
            _runTail = true;
            _activityLog.Initialize();
            _tailThread = new Thread(TailLoop);
            _tailThread.IsBackground = true;
            _tailThread.Name = "MCPPulse-Tail";
            _tailThread.Start();
        }

        private void TailLoop()
        {
            while (_runTail)
            {
                try
                {
                    TailTick();
                }
                catch
                {
                    // Never let an exception escape
                }
                Thread.Sleep(TailIntervalMs);
            }
        }

        private void TailTick()
        {
            if (_pauseTail) return;

            // Poll for new log lines
            try
            {
                _activityLog.Poll();
            }
            catch { }

            // Dequeue events and register in graph
            try
            {
                List<ActivityEvent> events = _activityLog.DequeueAll();
                if (events != null)
                {
                    lock (_graphLock)
                    {
                        foreach (ActivityEvent evt in events)
                        {
                            _graph.RegisterActivity(
                                evt.ServerName,
                                evt.ToolName,
                                evt.Success,
                                evt.DurationMs);
                        }
                    }
                }
            }
            catch { }
        }

        // ==================================================================
        //  VISUALIZATION STATE
        // ==================================================================

        private void UpdateVisualizationState()
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - _lastFrameTime).TotalSeconds;
            if (dt > 0.5f) dt = 0.5f;
            _lastFrameTime = now;

            // Update rotation
            float rotDt = (float)(now - _lastRotationTime).TotalSeconds;
            if (rotDt > 0.5f) rotDt = 0.5f;
            _lastRotationTime = now;

            lock (_graphLock)
            {
                // Step orbital rotation
                OrbitalEngine.StepRotation(_graph, rotDt);

                // Update pulse phases
                float pulseSpeed = 1f; // 1Hz
                for (int i = 0; i < _graph.Nodes.Count; i++)
                {
                    _graph.Nodes[i].PulsePhase =
                        (float)((now - _lastRotationTime).TotalSeconds * pulseSpeed) % 1f;
                }

                // Expire old beams
                _graph.ExpireBeams();

                // Tick flash frames
                _graph.TickFlashFrames();

                // Update calls per minute
                _graph.UpdateCallsPerMinute();
            }
        }

        // ==================================================================
        //  RENDERING
        // ==================================================================

        private void DrawFrame()
        {
            int w = BitmapCurrent.Width;
            int h = BitmapCurrent.Height;

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // Background: dark navy #0A0E1A
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(255, 10, 14, 26)))
                {
                    g.FillRectangle(bg, 0, 0, w, h);
                }

                // Radar grid
                OrbitalEngine.DrawRadarGrid(g, new Rectangle(0, 0, w, h));

                int cx = w / 2;
                int cy = h / 2;

                lock (_graphLock)
                {
                    GraphNode center = _graph.GetCenterNode();
                    var peripherals = _graph.GetPeripheralNodes();
                    int cpm = _graph.UpdateCallsPerMinute();

                    if (_renderMode == RenderModeActivity)
                    {
                        // Activity-only: no orbital ring, just beams flowing through nodes
                        DrawActivityOnly(g, cx, cy, peripherals, cpm);
                    }
                    else
                    {
                        // Full or topology-only
                        DrawTopologyBase(g, cx, cy, center, peripherals, cpm);

                        // Draw beams (full mode only)
                        if (_renderMode == RenderModeFull)
                        {
                            DrawBeams(g, cx, cy, cpm);
                        }

                        // Draw peripheral nodes
                        DrawPeripheralNodes(g, cx, cy, peripherals, cpm);

                        // Draw center node
                        if (center != null)
                        {
                            PointF centerPos = OrbitalEngine.ComputePosition(center, cx, cy);
                            DrawCenterNode(g, centerPos, center);
                        }

                        // Draw HUD (full mode only)
                        if (_renderMode == RenderModeFull)
                        {
                            DrawHUD(g, w, h, cpm);
                        }
                    }
                }

                // Stop flash overlay
                if (_stopRequested &&
                    (DateTime.UtcNow - _stopFlashTime).TotalSeconds < 1.5)
                {
                    using (Brush flash = new SolidBrush(
                        Color.FromArgb(80, 255, 80, 80)))
                    {
                        g.FillRectangle(flash, 0, 0, w, h);
                    }
                }
            }
        }

        /// <summary>
        /// Draw topology base: orbital ring + conduits (used in full + topology modes).
        /// </summary>
        private void DrawTopologyBase(Graphics g, int cx, int cy,
            GraphNode center, List<GraphNode> peripherals, int cpm)
        {
            // Draw orbital ring (faint circle at orbit radius)
            if (center != null && peripherals.Count > 0)
            {
                float orbitR = peripherals[0].OrbitRadius;

                using (Pen ringPen = new Pen(Color.FromArgb(20, 50, 70, 100), 1f))
                {
                    g.DrawEllipse(ringPen, cx - orbitR, cy - orbitR,
                        orbitR * 2f, orbitR * 2f);
                }

                // Draw energy conduits (center to each node)
                foreach (GraphNode node in peripherals)
                {
                    PointF nodePos = OrbitalEngine.ComputePosition(node, cx, cy);
                    Color conduitColor = node.GetColor();
                    OrbitalEngine.DrawConduit(g, new PointF(cx, cy), nodePos, conduitColor);
                }
            }
        }

        /// <summary>
        /// Draw activity-only mode: no orbital ring, just beams flowing through nodes.
        /// </summary>
        private void DrawActivityOnly(Graphics g, int cx, int cy,
            List<GraphNode> peripherals, int cpm)
        {
            foreach (GraphNode node in peripherals)
            {
                // Draw node without orbital context
                PointF nodePos = OrbitalEngine.ComputePosition(node, cx, cy);
                DrawPeripheralNodeOnly(g, nodePos, node, cpm);
            }

            // Draw beams
            DrawBeams(g, cx, cy, cpm);
        }

        /// <summary>
        /// Draw all active beam animations.
        /// </summary>
        private void DrawBeams(Graphics g, int cx, int cy, int cpm)
        {
            foreach (BeamEvent beam in _graph.ActiveBeams)
            {
                // Find the source node
                GraphNode sourceNode = _graph.FindPeripheralNode(beam.ServerName);
                if (sourceNode == null) continue;

                PointF sourcePos = OrbitalEngine.ComputePosition(sourceNode, cx, cy);
                PointF centerPos = new PointF((float)cx, (float)cy);

                // Draw beam highlight on the conduit
                OrbitalEngine.DrawBeamHighlight(g, sourcePos, centerPos,
                    beam.SpawnTime, beam.Success, _animFrame);
            }
        }

        /// <summary>
        /// Draw all peripheral nodes with glow and health coloring.
        /// </summary>
        private void DrawPeripheralNodes(Graphics g, int cx, int cy,
            List<GraphNode> peripherals, int cpm)
        {
            foreach (GraphNode node in peripherals)
            {
                PointF nodePos = OrbitalEngine.ComputePosition(node, cx, cy);
                DrawPeripheralNode(g, nodePos, node, cpm);
            }
        }

        /// <summary>
        /// Draw a single peripheral node with glow, health color, label, and flash.
        /// </summary>
        private void DrawPeripheralNode(Graphics g, PointF pos, GraphNode node, int cpm)
        {
            Color glowColor = node.GetGlowColor();
            Color fillColor = node.GetColor();

            // Flash overlay for failed calls (3-frame red flash)
            OrbitalEngine.DrawNodeFlash(g, pos, node.Radius, node.FlashFrames);

            // Outer glow
            OrbitalEngine.DrawGlow(g, pos, node.Radius, node.PulsePhase,
                glowColor, cpm);

            // Solid fill circle
            using (SolidBrush fill = new SolidBrush(fillColor))
            {
                g.FillEllipse(fill, pos.X - node.Radius, pos.Y - node.Radius,
                    node.Radius * 2f, node.Radius * 2f);
            }

            // Inner highlight
            float innerR = node.Radius * 0.6f;
            using (SolidBrush highlight = new SolidBrush(
                Color.FromArgb(80, 255, 255, 255)))
            {
                g.FillEllipse(highlight,
                    pos.X - innerR * 0.5f - node.Radius * 0.2f,
                    pos.Y - innerR * 0.5f - node.Radius * 0.2f,
                    innerR, innerR);
            }

            // Label below the node
            string label = node.DisplayName;
            if (node.ToolsCount > 0)
                label = label + " (" + node.ToolsCount + ")";

            using (Font labelFont = new Font("Segoe UI", 8, FontStyle.Regular))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;

                Color labelColor;
                if (node.Health == NodeHealth.Healthy)
                    labelColor = Color.FromArgb(220, 200, 240, 255);
                else if (node.Health == NodeHealth.Warning)
                    labelColor = Color.FromArgb(220, 255, 220, 100);
                else
                    labelColor = Color.FromArgb(220, 255, 120, 120);

                using (Brush labelBrush = new SolidBrush(labelColor))
                {
                    float labelY = pos.Y + node.Radius + 6f;
                    g.DrawString(label, labelFont, labelBrush, pos.X, labelY);
                }
            }
        }

        /// <summary>
        /// Draw a peripheral node without orbital context (activity-only mode).
        /// </summary>
        private void DrawPeripheralNodeOnly(Graphics g, PointF pos, GraphNode node, int cpm)
        {
            Color glowColor = node.GetGlowColor();
            Color fillColor = node.GetColor();

            OrbitalEngine.DrawNodeFlash(g, pos, node.Radius, node.FlashFrames);
            OrbitalEngine.DrawGlow(g, pos, node.Radius, node.PulsePhase,
                glowColor, cpm);

            using (SolidBrush fill = new SolidBrush(fillColor))
            {
                g.FillEllipse(fill, pos.X - node.Radius, pos.Y - node.Radius,
                    node.Radius * 2f, node.Radius * 2f);
            }

            float innerR = node.Radius * 0.6f;
            using (SolidBrush highlight = new SolidBrush(
                Color.FromArgb(80, 255, 255, 255)))
            {
                g.FillEllipse(highlight,
                    pos.X - innerR * 0.5f - node.Radius * 0.2f,
                    pos.Y - innerR * 0.5f - node.Radius * 0.2f,
                    innerR, innerR);
            }

            string label = node.DisplayName;
            if (node.ToolsCount > 0)
                label = label + " (" + node.ToolsCount + ")";

            using (Font labelFont = new Font("Segoe UI", 8, FontStyle.Regular))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;

                Color labelColor;
                if (node.Health == NodeHealth.Healthy)
                    labelColor = Color.FromArgb(220, 200, 240, 255);
                else if (node.Health == NodeHealth.Warning)
                    labelColor = Color.FromArgb(220, 255, 220, 100);
                else
                    labelColor = Color.FromArgb(220, 255, 120, 120);

                using (Brush labelBrush = new SolidBrush(labelColor))
                {
                    float labelY = pos.Y + node.Radius + 6f;
                    g.DrawString(label, labelFont, labelBrush, pos.X, labelY);
                }
            }
        }

        /// <summary>
        /// Draw the center gateway node (MCP / GATEWAY).
        /// </summary>
        private void DrawCenterNode(Graphics g, PointF pos, GraphNode node)
        {
            Color glowColor = node.GetGlowColor();
            Color fillColor = Color.FromArgb(200, 0, 180, 220);

            // Outer glow
            OrbitalEngine.DrawGlow(g, pos, node.Radius, node.PulsePhase,
                glowColor, 0);

            // Solid fill
            using (SolidBrush fill = new SolidBrush(fillColor))
            {
                g.FillEllipse(fill, pos.X - node.Radius, pos.Y - node.Radius,
                    node.Radius * 2f, node.Radius * 2f);
            }

            // Inner highlight
            float innerR = node.Radius * 0.5f;
            using (SolidBrush highlight = new SolidBrush(
                Color.FromArgb(100, 255, 255, 255)))
            {
                g.FillEllipse(highlight,
                    pos.X - innerR * 0.5f - node.Radius * 0.15f,
                    pos.Y - innerR * 0.5f - node.Radius * 0.15f,
                    innerR, innerR);
            }

            // Label "MCP" in center
            using (Font labelFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (Brush labelBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString("MCP", labelFont, labelBrush, pos.X, pos.Y, fmt);
            }
        }

        /// <summary>
        /// Draw the HUD bar at the bottom of the canvas.
        /// Format: "<healthy>/<total> healthy | <calls_per_minute> calls/min | last: <last_tool_name>"
        /// If no activity in 60s, show "idle".
        /// </summary>
        private void DrawHUD(Graphics g, int w, int h, int cpm)
        {
            int barHeight = 22;
            int barY = h - barHeight;

            // HUD background
            using (SolidBrush barBg = new SolidBrush(Color.FromArgb(60, 12, 16, 24)))
            {
                g.FillRectangle(barBg, 0, barY, w, barHeight);
            }

            // Separator line
            using (Pen linePen = new Pen(Color.FromArgb(40, 40, 60, 80), 1f))
            {
                g.DrawLine(linePen, 0, barY, w, barY);
            }

            // Build HUD text
            string healthText;
            int healthy = _graph.TotalHealthy;
            int total = _graph.Nodes.Count - 1; // subtract center node
            if (total < 0) total = 0;
            healthText = string.Format("{0}/{1} healthy", healthy, total);

            string activityText;
            DateTime lastCall = _graph.LastGlobalCallTime;
            if (lastCall == DateTime.MinValue ||
                (DateTime.UtcNow - lastCall).TotalSeconds > 60)
            {
                activityText = "idle";
            }
            else
            {
                activityText = string.Format("{0} calls/min | last: {1}",
                    cpm, _graph.LastToolName);
            }

            // Left side: health + activity
            string leftText = string.Format("{0} | {1}", healthText, activityText);

            using (Font hudFont = new Font("Courier New", 9, FontStyle.Regular))
            using (Brush hudBrush = new SolidBrush(Color.FromArgb(180, 200, 220, 240)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Near;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(leftText, hudFont, hudBrush,
                    8, barY + barHeight / 2, fmt);
            }

            // Right side: uptime
            string uptimeText = "";
            if (_graph.GatewayUptimeSeconds > 0)
            {
                int hours = _graph.GatewayUptimeSeconds / 3600;
                int mins = (_graph.GatewayUptimeSeconds % 3600) / 60;
                uptimeText = string.Format("up {0}h{1}m", hours, mins);
            }

            if (!string.IsNullOrEmpty(uptimeText))
            {
                using (Font uptimeFont = new Font("Courier New", 8, FontStyle.Regular))
                using (Brush uptimeBrush = new SolidBrush(Color.FromArgb(120, 150, 170, 200)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Far;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString(uptimeText, uptimeFont, uptimeBrush,
                        w - 8, barY + barHeight / 2, fmt);
                }
            }

            // Widget title at top-left
            using (Font titleFont = new Font("Segoe UI", 9, FontStyle.Bold))
            using (Brush titleBrush = new SolidBrush(Color.FromArgb(180, 180, 200, 220)))
            {
                g.DrawString("MCP Pulse", titleFont, titleBrush, 6, 4);
            }
        }

        // ==================================================================
        //  TOUCH / SWIPE — Manual double-tap detection
        // ==================================================================

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastSingleTapTime).TotalMilliseconds < DoubleTapWindowMs)
                {
                    // Second Single within window -> treat as double-tap
                    if (_pendingSingleAction != null)
                        _pendingSingleAction.Cancel();
                    HandleDoubleTap();
                    _lastSingleTapTime = DateTime.MinValue;
                    return;
                }
                _lastSingleTapTime = now;
                // First tap — defer single-tap action so a follow-up can override
                _pendingSingleAction = new CancellationTokenSource();
                CancellationToken token = _pendingSingleAction.Token;
                Task.Run(delegate
                {
                    try
                    {
                        Thread.Sleep(DoubleTapWindowMs);
                        HandleSingleTap();
                    }
                    catch (TaskCanceledException) { }
                    catch { }
                });
            }
            else if (click_type == ClickType.Double)
            {
                // Free fallback for hardware that fires it
                HandleDoubleTap();
            }
        }

        private void HandleSingleTap()
        {
            // Toggle pause/resume of poll + tail threads
            _pausePoll = !_pausePoll;
            _pauseTail = !_pauseTail;
            RedrawAndSignal();
        }

        private void HandleDoubleTap()
        {
            // Cycle through render modes: 0=full, 1=topology-only, 2=activity-only
            _renderMode = (_renderMode + 1) % 3;
            RedrawAndSignal();
        }

        public void SwipeEvent(int direction)
        {
            // Swipe doesn't do anything in v1
        }

        // ==================================================================
        //  PIPE TO HOST
        // ==================================================================

        private void RedrawAndSignal()
        {
            if (_drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    DrawFrame();
                    SignalUpdate();
                }
                finally { _drawingMutex.ReleaseMutex(); }
            }
        }

        private void SignalUpdate()
        {
            WidgetUpdatedEventArgs args = new WidgetUpdatedEventArgs();
            args.WaitMax = 1000;
            args.WidgetBitmap = BitmapCurrent;
            if (WidgetUpdated != null) WidgetUpdated(this, args);
        }

        public void RequestUpdate() { RedrawAndSignal(); }
        public void EnterSleep()
        {
            _pausePoll = true;
            _pauseTail = true;
        }
        public void ExitSleep()
        {
            _pausePoll = false;
            _pauseTail = false;
        }

        public void Dispose()
        {
            _runPoll = false;
            _runTail = false;
            _pausePoll = true;
            _pauseTail = true;

            if (_pollThread != null && _pollThread.IsAlive)
                _pollThread.Join(3000);
            if (_tailThread != null && _tailThread.IsAlive)
                _tailThread.Join(3000);

            if (_drawingMutex != null) _drawingMutex.Dispose();
            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
    }
}
