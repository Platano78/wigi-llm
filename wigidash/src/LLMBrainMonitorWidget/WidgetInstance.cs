using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// "Glass Cockpit" brain monitor — live HUD over a token-driven particle field,
    /// with a 120-second tok/s sparkline trail at the bottom edge. Replaces the
    /// previous decorative sin() waveform with visualization driven entirely by
    /// real router metrics.
    ///
    /// Layout (480x320 — 5x4 fullscreen):
    ///   0-90    : top HUD row    — model identity (left)   | throughput ring (right)
    ///   90-220  : particle field — token-driven rising particles
    ///   220-290 : bottom HUD row — VRAM bar (left)         | request queue (right)
    ///   290-320 : sparkline      — last 120s of tokens/sec
    /// </summary>
    public class LLMBrainMonitorWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public UserControl GetSettingsControl() { return null; }

        public Bitmap BitmapCurrent;
        private string _resourcePath;

        // Threading
        private Thread _pollThread;
        private volatile bool _runPoll = false;
        private volatile bool _pausePoll = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;
        private const int PollIntervalMs = 1500;

        // ----- LLM state from endpoints (fetched verbatim from the original) -----
        private string _modelName = "";
        private string _serverStatus = "unknown";
        private float _loadProgress = 0f;
        private float _tokensPerSec = 0f;
        private double _prevTokensTotal = -1;
        private DateTime _prevTokensTime = DateTime.MinValue;
        private int _metricsPort = 0;
        private int _requestsProcessing = 0;
        private int _requestsDeferred = 0;
        private double _promptTokensTotal = 0;
        private double _prevPromptTokensTotal = -1;
        private float _promptTokensPerSec = 0f;
        private int _contextWindow = 0;
        private int _slotCount = 0;

        // ----- Visualization state -----
        private ParticleField _particles;
        private Sparkline _sparkline;
        private double _accumTokenDelta = 0; // fractional remainder for particle spawning
        private DateTime _lastFrameTime = DateTime.UtcNow;

        // ----- VRAM state (read from GpuInfo) -----
        private double _vramUsedGb = 0;
        private double _vramTotalGb = 0;

        // ----- Touch state -----
        private bool _stopRequested = false;
        private DateTime _stopFlashTime = DateTime.MinValue;
        private bool _killArmed = false;
        private DateTime _killArmedTime = DateTime.MinValue;
        private bool _killFlash = false;
        // Autopilot game mode — owns its own animation thread for projectile motion
        private bool _gameMode = false;
        private GameMode _game;
        private Thread _gameThread;
        private volatile bool _gameRunning = false;
        private double _gameTokenDeltaPending = 0; // accumulator for spawn weight
        private DateTime _gameLastFrame = DateTime.UtcNow;

        // Manual double-tap detection — WigiDash's ClickType.Double doesn't fire
        // reliably on this hardware; rapid Single taps come in as two distinct
        // events. We defer stop-request actions by DoubleTapWindowMs so a second
        // Single can convert the pair into a double-tap (game-mode toggle).
        private const int DoubleTapWindowMs = 350;
        private DateTime _lastSingleTapTime = DateTime.MinValue;
        private System.Threading.CancellationTokenSource _pendingStopCts;

        // ----- HTTP -----
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        // ----- Colors -----
        private static readonly Color BgTop = Color.FromArgb(14, 18, 28);
        private static readonly Color BgBot = Color.FromArgb(8, 10, 18);
        private static readonly Color RedAlert = Color.FromArgb(255, 60, 60);

        public LLMBrainMonitorWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);

            _particles = new ParticleField(160);
            _sparkline = new Sparkline(120);

            DrawFrame();
            StartPoll();
        }

        private void StartPoll()
        {
            _pausePoll = false;
            _runPoll = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "LLMBrainMonitor-Poll";
            _pollThread.Start();
        }

        // Poll loop runs at PollIntervalMs cadence. Particle physics step at the same
        // cadence — the field is intended to feel "alive" but it doesn't need 30 FPS
        // motion to read correctly, and avoiding a separate animation thread keeps
        // CPU draw cost flat with idle.
        private void PollLoop()
        {
            while (_runPoll)
            {
                if (!_pausePoll)
                {
                    try
                    {
                        FetchAllDataAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch { }

                    UpdateVramFromGpuInfo();
                    UpdateVisualizationState();

                    if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
                    {
                        try
                        {
                            DrawFrame();
                            SignalUpdate();
                        }
                        finally
                        {
                            _drawingMutex.ReleaseMutex();
                        }
                    }
                }
                Thread.Sleep(PollIntervalMs);
            }
        }

        // ====================================================================
        //  DATA FETCHING — preserved verbatim from the previous BrainMonitor.
        //  These are the canonical, working metrics-fetch implementations.
        // ====================================================================

        private async Task FetchAllDataAsync()
        {
            var healthTask = FetchHealthAsync();
            var modelsTask = FetchModelsAsync();
            var metricsTask = _metricsPort > 0 ? FetchMetricsAsync() : Task.CompletedTask;
            await Task.WhenAll(healthTask, modelsTask, metricsTask);
        }

        private async Task FetchHealthAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://127.0.0.1:8081/health");
                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    if (body.Contains("\"ok\""))
                    {
                        _serverStatus = "online";
                        _loadProgress = 1f;
                    }
                    else if (body.Contains("\"loading\""))
                    {
                        _serverStatus = "loading";
                        int progIdx = body.IndexOf("\"progress\"");
                        if (progIdx >= 0)
                        {
                            int colonIdx = body.IndexOf(':', progIdx + 10);
                            if (colonIdx >= 0)
                            {
                                string numStr = "";
                                for (int i = colonIdx + 1; i < body.Length; i++)
                                {
                                    char c = body[i];
                                    if (char.IsDigit(c) || c == '.' || c == '-')
                                        numStr += c;
                                    else if (numStr.Length > 0)
                                        break;
                                }
                                float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out _loadProgress);
                            }
                        }
                    }
                    else if (body.Contains("\"no_slot\""))
                    {
                        _serverStatus = "loading";
                    }
                    else
                    {
                        _serverStatus = "online"; // assume reachable
                    }
                }
                else
                {
                    _serverStatus = "offline";
                }
            }
            catch
            {
                _serverStatus = "offline";
                _loadProgress = 0f;
            }
        }

        private async Task FetchModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://127.0.0.1:8081/v1/models");
                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();

                    int loadedIdx = body.IndexOf("\"value\":\"loaded\"");
                    if (loadedIdx >= 0)
                    {
                        int searchStart = Math.Max(0, loadedIdx - 2000);
                        string region = body.Substring(searchStart, loadedIdx - searchStart);
                        int lastIdIdx = region.LastIndexOf("\"id\"");
                        if (lastIdIdx >= 0)
                        {
                            int absIdIdx = searchStart + lastIdIdx;
                            string afterId = body.Substring(absIdIdx + 4, Math.Min(200, body.Length - absIdIdx - 4));
                            int q1 = afterId.IndexOf('"');
                            if (q1 >= 0)
                            {
                                int q2 = afterId.IndexOf('"', q1 + 1);
                                if (q2 > q1)
                                    _modelName = afterId.Substring(q1 + 1, q2 - q1 - 1);
                            }
                        }

                        int portArgIdx = body.IndexOf("\"--port\"", loadedIdx);
                        if (portArgIdx < 0)
                        {
                            int entryStart = Math.Max(0, loadedIdx - 3000);
                            string beforeLoaded = body.Substring(entryStart, loadedIdx - entryStart);
                            int lastPortArg = beforeLoaded.LastIndexOf("\"--port\"");
                            if (lastPortArg >= 0)
                                portArgIdx = entryStart + lastPortArg;
                        }
                        if (portArgIdx >= 0)
                        {
                            string afterPort = body.Substring(portArgIdx + 8, Math.Min(50, body.Length - portArgIdx - 8));
                            int pq1 = afterPort.IndexOf('"');
                            if (pq1 >= 0)
                            {
                                int pq2 = afterPort.IndexOf('"', pq1 + 1);
                                if (pq2 > pq1)
                                {
                                    string portStr = afterPort.Substring(pq1 + 1, pq2 - pq1 - 1);
                                    int port;
                                    if (int.TryParse(portStr, out port) && port > 0)
                                        _metricsPort = port;
                                }
                            }
                        }

                        // Pull --ctx-size and -np (slots) from args for the HUD label
                        ExtractIntArg(body, loadedIdx, "\"--ctx-size\"", ref _contextWindow);
                        ExtractIntArg(body, loadedIdx, "\"-np\"", ref _slotCount);
                    }
                    else
                    {
                        _modelName = "(no model loaded)";
                        _metricsPort = 0;
                    }
                }
            }
            catch { }
        }

        private static void ExtractIntArg(string body, int loadedIdx, string argKey, ref int target)
        {
            int idx = body.IndexOf(argKey, Math.Max(0, loadedIdx - 3000));
            if (idx < 0) return;
            int after = idx + argKey.Length;
            int q1 = body.IndexOf('"', after);
            if (q1 < 0) return;
            int q2 = body.IndexOf('"', q1 + 1);
            if (q2 < 0) return;
            string val = body.Substring(q1 + 1, q2 - q1 - 1);
            int parsed;
            if (int.TryParse(val, out parsed)) target = parsed;
        }

        private async Task FetchMetricsAsync()
        {
            if (_metricsPort <= 0) return;
            try
            {
                var response = await _httpClient.GetAsync("http://127.0.0.1:" + _metricsPort + "/metrics");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    string[] lines = content.Split('\n');

                    double tokensTotal = 0;
                    bool foundTokens = false;
                    float directTps = -1f;

                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("#")) continue;

                        if (line.StartsWith("llamacpp:predicted_tokens_seconds "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) directTps = (float)val;
                        }
                        else if (line.StartsWith("llamacpp:tokens_predicted_total "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) { tokensTotal = val; foundTokens = true; }
                        }
                        else if (line.StartsWith("llamacpp:requests_processing "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) _requestsProcessing = (int)val;
                        }
                        else if (line.StartsWith("llamacpp:requests_deferred "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) _requestsDeferred = (int)val;
                        }
                        else if (line.StartsWith("llamacpp:prompt_tokens_total "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) _promptTokensTotal = val;
                        }
                        else if (line.StartsWith("llamacpp:prompt_tokens_seconds "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val)) _promptTokensPerSec = (float)val;
                        }
                    }

                    // Generation tok/s — direct gauge if available, else compute delta
                    if (directTps >= 0)
                    {
                        _tokensPerSec = directTps;
                    }
                    else if (foundTokens)
                    {
                        DateTime now = DateTime.UtcNow;
                        if (_prevTokensTotal >= 0 && _prevTokensTime != DateTime.MinValue)
                        {
                            double deltaTokens = tokensTotal - _prevTokensTotal;
                            double deltaSec = (now - _prevTokensTime).TotalSeconds;
                            if (deltaSec > 0.1 && deltaTokens >= 0)
                                _tokensPerSec = (float)(deltaTokens / deltaSec);
                        }
                        _prevTokensTotal = tokensTotal;
                        _prevTokensTime = now;
                    }

                    // Track raw delta for particle spawning (independent of tps gauge)
                    if (foundTokens && _prevTokensTotal >= 0)
                    {
                        double rawDelta = tokensTotal - _prevTokensTotal;
                        if (rawDelta > 0) _accumTokenDelta += rawDelta;
                        _prevTokensTotal = tokensTotal;
                    }
                }
            }
            catch { }
        }

        private double ParsePrometheusValue(string line)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string valStr = parts[parts.Length - 1];
                double val;
                if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    return val;
            }
            return double.NaN;
        }

        private void UpdateVramFromGpuInfo()
        {
            try
            {
                VramInfo info = GpuInfo.GetLocalVram();
                if (info != null && info.Available)
                {
                    _vramUsedGb = info.UsedGB;
                    _vramTotalGb = info.TotalGB;
                }
            }
            catch { }
        }

        // Convert per-poll state into visualization deltas: spawn particles based
        // on the accumulated token delta since last frame, and push a sparkline sample.
        private void UpdateVisualizationState()
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - _lastFrameTime).TotalSeconds;
            if (dt > 0.5f) dt = 0.5f; // Cap dt across long sleeps so particles don't teleport
            _lastFrameTime = now;

            // Particle field area — used only to know spawn x-range
            int w = BitmapCurrent != null ? BitmapCurrent.Width : 480;
            int h = BitmapCurrent != null ? BitmapCurrent.Height : 320;
            Rectangle field = new Rectangle(0, 90, w, 130);

            // Spawn N particles based on accumulated token delta. Cap to avoid bursts
            // that flood the field on a multi-second poll.
            const double TokensPerParticle = 5.0;
            int spawnCount = (int)(_accumTokenDelta / TokensPerParticle);
            if (spawnCount > 0)
            {
                if (spawnCount > 30) spawnCount = 30;
                _particles.Spawn(spawnCount, field, _tokensPerSec);
                _accumTokenDelta -= spawnCount * TokensPerParticle;
            }

            _particles.Step(field, dt);
            _sparkline.Push(_tokensPerSec);
        }

        // ====================================================================
        //  RENDERING
        // ====================================================================

        private void DrawFrame()
        {
            int w = BitmapCurrent.Width;
            int h = BitmapCurrent.Height;

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // Background — slight vertical gradient
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, w, h), BgTop, BgBot, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, 0, 0, w, h);
                }

                if (_gameMode)
                {
                    DrawGameModePlaceholder(g, w, h);
                    return;
                }

                // Layer 1 — particles (drawn under the HUD glass panes)
                Rectangle field = new Rectangle(0, 90, w, 130);
                _particles.Draw(g, field);

                // Layer 3 — sparkline strip (drawn before HUD so HUD glass overlaps cleanly if it ever extends)
                Rectangle spark = new Rectangle(0, h - 30, w, 30);
                double spkMax = Math.Max(40, _sparkline.Max * 1.15);
                _sparkline.Draw(g, spark, spkMax);

                // Layer 2 — HUD panels in 4 corners
                int margin = 6;
                int topH = 80;
                int botY = h - 30 - 70;
                int botH = 70;

                Rectangle topLeft = new Rectangle(margin, margin, (w / 2) - margin * 2, topH);
                Rectangle topRight = new Rectangle(w / 2 + margin, margin, (w / 2) - margin * 2, topH);
                Rectangle botLeft = new Rectangle(margin, botY, (w / 2) - margin * 2, botH);
                Rectangle botRight = new Rectangle(w / 2 + margin, botY, (w / 2) - margin * 2, botH);

                HudPanel.DrawTopLeft(g, topLeft, _modelName, _contextWindow, _slotCount, _serverStatus);
                HudPanel.DrawTopRight(g, topRight, _tokensPerSec, 80.0); // 80 tok/s scale max
                // Approximate weights vs KV split: assume KV grows during inference,
                // weights are static. Without a direct llama.cpp metric we treat
                // total VRAM used as "weights + KV" and don't try to split it.
                HudPanel.DrawBottomLeft(g, botLeft, _vramUsedGb, 0.0, _vramTotalGb);
                HudPanel.DrawBottomRight(g, botRight, _requestsProcessing, _requestsDeferred, _promptTokensPerSec);

                // Touch hints — minimal, only if no other state needs the space
                DrawTouchHint(g, w, h);

                // Stop flash overlay
                if (_stopRequested && (DateTime.UtcNow - _stopFlashTime).TotalSeconds < 1.5)
                {
                    using (Brush flash = new SolidBrush(Color.FromArgb(80, 255, 80, 80)))
                    {
                        g.FillRectangle(flash, 0, 0, w, h);
                    }
                }

                // Kill armed border flash
                if (_killArmed && (DateTime.UtcNow - _killArmedTime).TotalSeconds < 3)
                {
                    _killFlash = !_killFlash;
                    if (_killFlash)
                    {
                        using (Pen redPen = new Pen(RedAlert, 3))
                        {
                            g.DrawRectangle(redPen, 1, 1, w - 3, h - 3);
                        }
                    }
                }
                else
                {
                    _killArmed = false;
                }
            }
        }

        private void DrawTouchHint(Graphics g, int w, int h)
        {
            // Inline single-line hint between the bottom HUD and the sparkline strip
            using (Font f = new Font("Arial", 7, FontStyle.Regular))
            using (Brush b = new SolidBrush(Color.FromArgb(80, 100, 130)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                g.DrawString("tap=stop  2-tap=autopilot  long-press 2x=kill",
                             f, b, new RectangleF(0, h - 42, w, 10), fmt);
            }
        }

        private void DrawGameModePlaceholder(Graphics g, int w, int h)
        {
            // Should not normally be reached — game mode renders via GameMode.Draw().
            // Kept as a safety stub for the brief window between toggle and animation
            // thread spinning up.
            if (_game != null)
            {
                _game.Draw(g, w, h, _modelName, _tokensPerSec);
            }
        }

        // ---------- Autopilot game mode lifecycle ----------

        private void StartGameMode()
        {
            int w = BitmapCurrent != null ? BitmapCurrent.Width : 480;
            _game = new GameMode(w);
            _gameLastFrame = DateTime.UtcNow;
            _gameTokenDeltaPending = 0;

            _gameRunning = true;
            _gameThread = new Thread(GameLoop);
            _gameThread.IsBackground = true;
            _gameThread.Name = "BrainMonitor-GameLoop";
            _gameThread.Start();
        }

        private void StopGameMode()
        {
            _gameRunning = false;
            if (_gameThread != null && _gameThread.IsAlive)
                _gameThread.Join(500);
            _gameThread = null;
            if (_game != null)
            {
                _game.OnExit();
                _game = null;
            }
        }

        private void GameLoop()
        {
            const int FrameMs = 33; // ~30 FPS
            while (_gameRunning && _gameMode)
            {
                if (!_pausePoll && BitmapCurrent != null && _drawingMutex.WaitOne(MutexTimeout))
                {
                    try
                    {
                        DateTime now = DateTime.UtcNow;
                        float dt = (float)(now - _gameLastFrame).TotalSeconds;
                        if (dt > 0.2f) dt = 0.2f;
                        _gameLastFrame = now;

                        // Pull pending token delta atomically. Poll loop accumulates
                        // _accumTokenDelta from real metrics; we transfer it here so
                        // the game spawns enemies aligned with actual token output.
                        double tokenDelta = _accumTokenDelta;
                        _accumTokenDelta = 0;

                        if (_game != null)
                        {
                            _game.Step(dt, BitmapCurrent.Width, BitmapCurrent.Height,
                                       _tokensPerSec, tokenDelta);

                            using (Graphics g = Graphics.FromImage(BitmapCurrent))
                            {
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                                _game.Draw(g, BitmapCurrent.Width, BitmapCurrent.Height,
                                           _modelName, _tokensPerSec);
                            }
                            SignalUpdate();
                        }
                    }
                    finally { _drawingMutex.ReleaseMutex(); }
                }
                Thread.Sleep(FrameMs);
            }
        }

        // ====================================================================
        //  TOUCH
        // ====================================================================

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                DateTime now = DateTime.UtcNow;
                double sinceLast = (now - _lastSingleTapTime).TotalMilliseconds;
                _lastSingleTapTime = now;

                // Second Single within the window — cancel pending stop, toggle game mode
                if (sinceLast > 0 && sinceLast < DoubleTapWindowMs)
                {
                    if (_pendingStopCts != null)
                    {
                        try { _pendingStopCts.Cancel(); } catch { }
                        _pendingStopCts = null;
                    }
                    _gameMode = !_gameMode;
                    if (_gameMode) StartGameMode();
                    else StopGameMode();
                    RedrawAndSignal();
                    return;
                }

                // First tap — defer the stop request so a follow-up tap can override it
                if (_pendingStopCts != null)
                {
                    try { _pendingStopCts.Cancel(); } catch { }
                }
                _pendingStopCts = new System.Threading.CancellationTokenSource();
                System.Threading.CancellationToken token = _pendingStopCts.Token;

                Task.Run(async delegate
                {
                    try
                    {
                        await Task.Delay(DoubleTapWindowMs, token);
                    }
                    catch (TaskCanceledException) { return; }
                    if (token.IsCancellationRequested) return;

                    _stopRequested = true;
                    _stopFlashTime = DateTime.UtcNow;
                    RedrawAndSignal();
                    try
                    {
                        var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
                        await _httpClient.PostAsync("http://127.0.0.1:8081/stop", content);
                    }
                    catch { }
                    await Task.Delay(1500);
                    _stopRequested = false;
                    RedrawAndSignal();
                });
                return;
            }

            if (click_type == ClickType.Long)
            {
                if (!_killArmed)
                {
                    _killArmed = true;
                    _killArmedTime = DateTime.UtcNow;
                    RedrawAndSignal();
                }
                else if ((DateTime.UtcNow - _killArmedTime).TotalSeconds < 3)
                {
                    _killArmed = false;
                    Task.Run(delegate
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = "/F /IM llama-server.exe",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch { }
                    });
                    RedrawAndSignal();
                }
                return;
            }

            // Some hardware does fire ClickType.Double natively — keep this path
            // alive as a free fallback. The manual rapid-pair detector above will
            // do the right thing when this doesn't fire.
            if (click_type == ClickType.Double)
            {
                if (_pendingStopCts != null)
                {
                    try { _pendingStopCts.Cancel(); } catch { }
                    _pendingStopCts = null;
                }
                _gameMode = !_gameMode;
                if (_gameMode) StartGameMode();
                else StopGameMode();
                RedrawAndSignal();
                return;
            }
        }

        public void SwipeEvent(int direction) { }

        // ====================================================================
        //  PIPE TO HOST
        // ====================================================================

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
            StopGameMode();
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
