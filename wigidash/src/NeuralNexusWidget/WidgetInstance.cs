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

namespace NeuralNexusWidget
{
    /// <summary>
    /// Neural Nexus Widget Instance — orbital network topology monitor.
    ///
    /// Polls the MCP Gateway /health endpoint and renders connected servers
    /// as nodes on a rotating orbital ring around a central gateway node.
    ///
    /// Health coloring:
    ///   Green  — connected, last poll succeeded within 10 seconds
    ///   Amber  — stale data (last poll 10-30 seconds ago)
    ///   Red    — disconnected or 3+ consecutive failures
    /// </summary>
    public class NeuralNexusWidgetInstance : IWidgetInstance
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
        private volatile bool _runPoll = false;
        private volatile bool _pausePoll = false;
        private const int PollIntervalMs = 2000;

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

        // Animation state
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private DateTime _lastRotationTime = DateTime.UtcNow;

        // Health state tracking
        private int _totalHealthy = 0;
        private int _totalWarning = 0;
        private int _totalCritical = 0;
        private int _gatewayConsecutiveFailures = 0;

        // Stop requested
        private bool _stopRequested = false;
        private DateTime _stopFlashTime = DateTime.MinValue;

        // ==================================================================
        //  CONSTRUCTOR
        // ==================================================================

        public NeuralNexusWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            int w = size.Width > 0 ? size.Width : 288;
            int h = size.Height > 0 ? size.Height : 288;
            BitmapCurrent = new Bitmap(w, h, PixelFormat.Format16bppRgb565);

            // Initialize graph model
            _graph = new GraphModel();
            try { InitializeGraphModel(); } catch { }

            // Initial paint — never let an exception escape the constructor
            try { DrawFrame(); } catch { }
            try { StartPoll(); } catch { }
        }

        // ==================================================================
        //  GRAPH INITIALIZATION
        // ==================================================================

        /// <summary>
        /// Build the initial graph structure from the MCP Gateway health data.
        /// Called once at startup, then updated by poll ticks.
        /// </summary>
        private void InitializeGraphModel()
        {
            lock (_graphLock)
            {
                _graph.Clear();

                // Center node: MCP Gateway itself
                GraphNode gateway = _graph.AddNode("gateway", "MCP Gateway", 0);
                gateway.Radius = 24f;
                gateway.OrbitRadius = 0f;

                // Peripheral nodes: we'll populate these from the health endpoint
                // For now, add a placeholder for the llama.cpp router
                GraphNode router = _graph.AddNode("llama-router", "llama.cpp Router", 0);
                router.Radius = 16f;
                _graph.ConnectToGateway(router);

                // Add placeholders for known MCP servers (will be updated by poll)
                // These will be removed/replaced when we parse the actual health data
                string[] serverNames = new string[]
                {
                    "context7",
                    "codex-native",
                    "serena",
                    "gemini-enhanced",
                    "chrome-devtools",
                    "stripe",
                    "MCP_DOCKER",
                    "supabase",
                    "mcp-needs-auth"
                };

                foreach (string name in serverNames)
                {
                    GraphNode node = _graph.AddNode(name, FormatDisplayName(name), 0);
                    _graph.ConnectToGateway(node);
                }
            }
        }

        private static string FormatDisplayName(string name)
        {
            // Simple display name formatter: replace hyphens and underscores with spaces, capitalize
            string result = name.Replace('-', ' ').Replace('_', ' ');
            if (result.Length > 0)
                result = char.ToUpper(result[0]) + result.Substring(1);
            return result;
        }

        // ==================================================================
        //  POLL THREAD
        // ==================================================================

        private void StartPoll()
        {
            _pausePoll = false;
            _runPoll = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "NeuralNexus-Poll";
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
                    // Never let an exception escape — would terminate the thread
                    // and likely the host process. Next tick retries.
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

            // Draw the frame
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

        /// <summary>
        /// Poll the MCP Gateway /health endpoint and update the graph model.
        /// The endpoint returns JSON like:
        /// {
        ///   "status": "ok",
        ///   "uptime_seconds": 12345,
        ///   "servers": {
        ///     "serena": { "status": "connected", "tools_count": 21 },
        ///     "chatgpt-enhanced": { "status": "connected", "tools_count": 12 },
        ///     "stitch": { "status": "disconnected", "error": "...", "tools_count": 0 }
        ///   },
        ///   "total_tools": 296
        /// }
        /// </summary>
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
                // Gateway unreachable — mark all nodes as stale
                MarkAllNodesStale(DateTime.UtcNow);
                return;
            }

            if (string.IsNullOrEmpty(json))
            {
                MarkAllNodesStale(DateTime.UtcNow);
                return;
            }

            // Parse the JSON response
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                if (root == null)
                {
                    MarkAllNodesStale(DateTime.UtcNow);
                    return;
                }

                // Check top-level status
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
                    _gatewayConsecutiveFailures = 0;

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
                    // Gateway returned non-OK status
                    _gatewayConsecutiveFailures++;
                    MarkAllNodesStale(now);
                }
            }
            catch
            {
                // JSON parse error
                _gatewayConsecutiveFailures++;
                MarkAllNodesStale(DateTime.UtcNow);
            }
        }

        /// <summary>
        /// Update the graph model based on parsed server data from the health endpoint.
        /// </summary>
        private void UpdateGraphFromServers(Dictionary<string, object> servers, DateTime now)
        {
            lock (_graphLock)
            {
                _totalHealthy = 0;
                _totalWarning = 0;
                _totalCritical = 0;

                // Track which server names we've seen
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Update existing nodes first
                foreach (GraphNode node in _graph.Nodes)
                {
                    if (node.Name == "gateway" || node.Name == "llama-router")
                        continue; // skip special nodes

                    if (servers.ContainsKey(node.Name))
                    {
                        seenNames.Add(node.Name);
                        var serverData = servers[node.Name] as Dictionary<string, object>;
                        if (serverData != null)
                        {
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
                                    if (int.TryParse(tc.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                                        toolsCount = parsed;
                                }
                            }

                            bool connected = (serverStatus == "connected");
                            node.ToolsCount = toolsCount;
                            node.UpdateHealth(connected, 0, now);

                            if (node.Health == NodeHealth.Healthy) _totalHealthy++;
                            else if (node.Health == NodeHealth.Warning) _totalWarning++;
                            else _totalCritical++;
                        }
                    }
                }

                // Add new nodes that weren't in the existing list
                foreach (var kvp in servers)
                {
                    string serverName = kvp.Key;
                    if (seenNames.Contains(serverName))
                        continue;

                    // Check if this is a known server we already have
                    bool found = false;
                    foreach (GraphNode node in _graph.Nodes)
                    {
                        if (node.Name == serverName) { found = true; break; }
                    }
                    if (found) continue;

                    // Add as new peripheral node
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
                                if (int.TryParse(tc.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                                    toolsCount = parsed;
                            }
                        }
                    }

                    GraphNode newNode = _graph.AddNode(serverName, FormatDisplayName(serverName), toolsCount);
                    newNode.UpdateHealth(status == "connected", 0, now);
                    _graph.ConnectToGateway(newNode);

                    if (newNode.Health == NodeHealth.Healthy) _totalHealthy++;
                    else if (newNode.Health == NodeHealth.Warning) _totalWarning++;
                    else _totalCritical++;
                }

                // Re-initialize orbital layout in case node count changed
                int w = BitmapCurrent != null ? BitmapCurrent.Width : 288;
                int h = BitmapCurrent != null ? BitmapCurrent.Height : 288;
                int minDim = Math.Min(w, h);
                float orbitRadius = (float)(minDim * 0.35);
                int cx = w / 2;
                int cy = h / 2;
                OrbitalEngine.InitializeLayout(_graph, cx, cy, orbitRadius);
            }
        }

        /// <summary>
        /// Mark all peripheral nodes as stale (no recent poll data).
        /// </summary>
        private void MarkAllNodesStale(DateTime now)
        {
            lock (_graphLock)
            {
                _gatewayConsecutiveFailures++;
                _totalHealthy = 0;
                _totalWarning = 0;
                _totalCritical = 0;

                foreach (GraphNode node in _graph.Nodes)
                {
                    if (node.Name == "gateway" || node.Name == "llama-router")
                        continue;
                    node.UpdateHealth(false, _gatewayConsecutiveFailures, now);
                    _totalCritical++;
                }
            }
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

            // Pulse animation for all nodes
            lock (_graphLock)
            {
                foreach (GraphNode node in _graph.Nodes)
                {
                    // Pulse phase: 0..1 over 1.5 seconds
                    node.PulsePhase = (float)((now - _lastRotationTime).TotalSeconds * 0.67) % 1f;
                }
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

                // Background — deep space dark
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(8, 10, 16)))
                {
                    g.FillRectangle(bg, 0, 0, w, h);
                }

                // Radar grid background
                OrbitalEngine.DrawRadarGrid(g, new Rectangle(0, 0, w, h));

                // Center point
                int cx = w / 2;
                int cy = h / 2;

                // Draw orbital ring (faint circle at orbit radius)
                lock (_graphLock)
                {
                    GraphNode center = _graph.GetCenterNode();
                    var peripherals = _graph.GetPeripheralNodes();

                    if (center != null && peripherals.Count > 0)
                    {
                        // Compute orbit radius from first peripheral node
                        float orbitR = peripherals[0].OrbitRadius;

                        // Draw orbital ring
                        using (Pen ringPen = new Pen(Color.FromArgb(20, 50, 70, 100), 1f))
                        {
                            g.DrawEllipse(ringPen, cx - orbitR, cy - orbitR, orbitR * 2f, orbitR * 2f);
                        }

                        // Draw energy conduits (center to each node)
                        foreach (GraphNode node in peripherals)
                        {
                            PointF nodePos = OrbitalEngine.ComputePosition(node, cx, cy);
                            Color conduitColor = node.GetColor();
                            OrbitalEngine.DrawConduit(g, new PointF(cx, cy), nodePos, conduitColor);
                        }

                        // Draw peripheral nodes with glow
                        foreach (GraphNode node in peripherals)
                        {
                            PointF nodePos = OrbitalEngine.ComputePosition(node, cx, cy);
                            DrawNode(g, nodePos, node);
                        }

                        // Draw center gateway node
                        if (center != null)
                        {
                            PointF centerPos = OrbitalEngine.ComputePosition(center, cx, cy);
                            DrawNode(g, centerPos, center);
                        }

                        // Draw HUD: health summary at bottom
                        DrawHealthSummary(g, w, h);
                    }
                }

                // Stop flash overlay
                if (_stopRequested && (DateTime.UtcNow - _stopFlashTime).TotalSeconds < 1.5)
                {
                    using (Brush flash = new SolidBrush(Color.FromArgb(80, 255, 80, 80)))
                    {
                        g.FillRectangle(flash, 0, 0, w, h);
                    }
                }
            }
        }

        /// <summary>
        /// Draw a single node with glow ring and label.
        /// </summary>
        private void DrawNode(Graphics g, PointF pos, GraphNode node)
        {
            Color glowColor = node.GetGlowColor();
            Color fillColor = node.GetColor();

            // Outer glow
            OrbitalEngine.DrawGlow(g, pos, node.Radius, node.PulsePhase, glowColor);

            // Solid fill circle
            using (SolidBrush fill = new SolidBrush(fillColor))
            {
                g.FillEllipse(fill, pos.X - node.Radius, pos.Y - node.Radius, node.Radius * 2f, node.Radius * 2f);
            }

            // Inner highlight (top-left light source)
            float innerR = node.Radius * 0.6f;
            using (SolidBrush highlight = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
            {
                g.FillEllipse(highlight, pos.X - innerR * 0.5f - node.Radius * 0.2f,
                              pos.Y - innerR * 0.5f - node.Radius * 0.2f, innerR, innerR);
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
        /// Draw the health summary HUD at the bottom of the widget.
        /// </summary>
        private void DrawHealthSummary(Graphics g, int w, int h)
        {
            // Bottom HUD bar
            int barHeight = 20;
            int barY = h - barHeight;
            using (SolidBrush barBg = new SolidBrush(Color.FromArgb(60, 12, 16, 24)))
            {
                g.FillRectangle(barBg, 0, barY, w, barHeight);
            }

            // Separator line
            using (Pen linePen = new Pen(Color.FromArgb(40, 40, 60, 80), 1f))
            {
                g.DrawLine(linePen, 0, barY, w, barY);
            }

            // Health summary text
            string healthText = string.Format("Nodes: {0} healthy | {1} warning | {2} critical",
                                              _totalHealthy, _totalWarning, _totalCritical);
            using (Font healthFont = new Font("Segoe UI", 8, FontStyle.Regular))
            using (Brush healthBrush = new SolidBrush(Color.FromArgb(160, 180, 200, 220)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(healthText, healthFont, healthBrush,
                             new RectangleF(0, barY + 2, w, barHeight - 4), fmt);
            }

            // Widget title at top-left
            using (Font titleFont = new Font("Segoe UI", 9, FontStyle.Bold))
            using (Brush titleBrush = new SolidBrush(Color.FromArgb(180, 180, 200, 220)))
            {
                g.DrawString("Neural Nexus", titleFont, titleBrush, 6, 4);
            }
        }

        // ==================================================================
        //  TOUCH / SWIPE
        // ==================================================================

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                // Single tap: toggle poll pause/resume
                _pausePoll = !_pausePoll;
                RedrawAndSignal();
            }
            else if (click_type == ClickType.Double)
            {
                // Double tap: toggle stop flash
                _stopRequested = true;
                _stopFlashTime = DateTime.UtcNow;
                RedrawAndSignal();
                // Auto-clear after 1.5s
                Task.Run(delegate
                {
                    try
                    {
                        Thread.Sleep(1500);
                        _stopRequested = false;
                        RedrawAndSignal();
                    }
                    catch { }
                });
            }
        }

        public void SwipeEvent(int direction)
        {
            // Swipe doesn't do anything in Phase 1
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
        public void EnterSleep() { _pausePoll = true; }
        public void ExitSleep() { _pausePoll = false; }

        public void Dispose()
        {
            _runPoll = false;
            _pausePoll = true;
            if (_pollThread != null && _pollThread.IsAlive)
                _pollThread.Join(3000);
            if (_drawingMutex != null) _drawingMutex.Dispose();
            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
    }
}
