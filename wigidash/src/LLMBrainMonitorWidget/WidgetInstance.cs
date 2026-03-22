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
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace LLMBrainMonitorWidget
{
    public class LLMBrainMonitorWidgetInstance : IWidgetInstance
    {
        // IWidgetInstance properties
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public UserControl GetSettingsControl() { return null; }

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;

        // Threading
        private Thread _pollThread;
        private volatile bool _runPoll = false;
        private volatile bool _pausePoll = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;
        private const int PollIntervalMs = 2000;

        // Waveform buffer (circular, 480 samples for full width)
        private const int WaveformWidth = 480;
        private float[] _waveformBuffer = new float[WaveformWidth];

        // LLM state from endpoints
        private string _modelName = "";
        private string _serverStatus = "unknown"; // "ok", "loading", "no_slot", "error", "offline"
        private float _loadProgress = 0f;
        private float _tokensPerSec = 0f;
        private double _prevTokensTotal = -1;
        private DateTime _prevTokensTime = DateTime.MinValue;
        private int _metricsPort = 0; // Dynamic port of loaded model subprocess
        private int _kvCacheUsed = 0;
        private int _kvCacheTotal = 0;
        private int _requestsProcessing = 0;
        private double _promptTokensTotal = 0;
        private double _prevPromptTokensTotal = -1;
        private float _estimatedTtftMs = 0f;
        private int _batchSize = 512;

        // Touch state
        private float _temperature = 0.7f;
        private bool _showTempOverlay = false;
        private DateTime _tempOverlayTime = DateTime.MinValue;
        private bool _stopRequested = false;
        private DateTime _stopFlashTime = DateTime.MinValue;
        private bool _killArmed = false;
        private DateTime _killArmedTime = DateTime.MinValue;
        private bool _killFlash = false;

        // Animation state
        private float _idlePhase = 0f;
        private Random _rng = new Random();

        // HTTP client
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        // Fonts — sized for fullscreen 5x4 (480x320 on 7" LCD)
        private readonly Font _titleFont = new Font("Segoe UI", 18f, FontStyle.Bold);
        private readonly Font _normalFont = new Font("Segoe UI", 14f);
        private readonly Font _smallFont = new Font("Segoe UI", 12f);
        private readonly Font _valueFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font _labelFont = new Font("Segoe UI", 12f);
        private readonly Font _hintFont = new Font("Segoe UI", 11f);
        private readonly Font _overlayFont = new Font("Segoe UI", 28f, FontStyle.Bold);

        // Colors
        private static readonly Color BgColor = Color.FromArgb(10, 10, 18);
        private static readonly Color HeaderGradTop = Color.FromArgb(20, 25, 40);
        private static readonly Color HeaderGradBot = Color.FromArgb(12, 15, 28);
        private static readonly Color CyanAccent = Color.FromArgb(0, 200, 255);
        private static readonly Color CyanDim = Color.FromArgb(0, 140, 180);
        private static readonly Color CyanGlow = Color.FromArgb(60, 0, 200, 255);
        private static readonly Color CyanGlowFade = Color.FromArgb(0, 0, 200, 255);
        private static readonly Color PurpleSegment = Color.FromArgb(100, 60, 180);
        private static readonly Color BlueSegment = Color.FromArgb(60, 140, 200);
        private static readonly Color GreenSegment = Color.FromArgb(80, 200, 120);
        private static readonly Color DimText = Color.FromArgb(120, 120, 140);
        private static readonly Color BarBg = Color.FromArgb(25, 25, 35);
        private static readonly Color BarBorder = Color.FromArgb(45, 50, 65);
        private static readonly Color AmberWarn = Color.FromArgb(255, 180, 40);
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

            // Initialize waveform with gentle idle sine
            for (int i = 0; i < WaveformWidth; i++)
            {
                _waveformBuffer[i] = (float)(Math.Sin(i * 0.04) * 10.0);
            }

            DrawFrame();
            StartPoll();
        }

        private void StartPoll()
        {
            _pausePoll = false;
            _runPoll = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Start();
        }

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

                    UpdateWaveform();

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

        // === DATA FETCHING ===

        private async Task FetchAllDataAsync()
        {
            // Fetch health and models in parallel; metrics uses port discovered from models
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
                    // Parse simple JSON: {"status":"ok"} or {"status":"loading","progress":0.5}
                    if (body.Contains("\"ok\""))
                    {
                        _serverStatus = "ok";
                        _loadProgress = 1f;
                    }
                    else if (body.Contains("\"loading\""))
                    {
                        _serverStatus = "loading";
                        // Extract progress value
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
                        _serverStatus = "no_slot";
                    }
                    else
                    {
                        _serverStatus = "error";
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

                    // Find the loaded model: look for "value":"loaded"
                    int loadedIdx = body.IndexOf("\"value\":\"loaded\"");
                    if (loadedIdx >= 0)
                    {
                        // Find the "id" field for this model entry (search backwards for nearest "id")
                        int searchStart = Math.Max(0, loadedIdx - 2000);
                        string region = body.Substring(searchStart, loadedIdx - searchStart);
                        int lastIdIdx = region.LastIndexOf("\"id\"");
                        if (lastIdIdx >= 0)
                        {
                            int absIdIdx = searchStart + lastIdIdx;
                            string afterId = body.Substring(absIdIdx + 4, Math.Min(200, body.Length - absIdIdx - 4));
                            // Parse: "id":"model-name" — skip :"
                            int q1 = afterId.IndexOf('"');
                            if (q1 >= 0)
                            {
                                int q2 = afterId.IndexOf('"', q1 + 1);
                                if (q2 > q1)
                                    _modelName = afterId.Substring(q1 + 1, q2 - q1 - 1);
                            }
                        }

                        // Extract --port from the loaded model's args
                        int portArgIdx = body.IndexOf("\"--port\"", loadedIdx);
                        if (portArgIdx < 0)
                        {
                            // Port arg might be before "loaded" in the same model entry
                            int entryStart = Math.Max(0, loadedIdx - 3000);
                            string beforeLoaded = body.Substring(entryStart, loadedIdx - entryStart);
                            int lastPortArg = beforeLoaded.LastIndexOf("\"--port\"");
                            if (lastPortArg >= 0)
                                portArgIdx = entryStart + lastPortArg;
                        }
                        if (portArgIdx >= 0)
                        {
                            // Next quoted value after "--port" is the port number
                            string afterPort = body.Substring(portArgIdx + 8, Math.Min(50, body.Length - portArgIdx - 8));
                            int pq1 = afterPort.IndexOf('"');
                            if (pq1 >= 0)
                            {
                                int pq2 = afterPort.IndexOf('"', pq1 + 1);
                                if (pq2 > pq1)
                                {
                                    string portStr = afterPort.Substring(pq1 + 1, pq2 - pq1 - 1);
                                    if (int.TryParse(portStr, out int port) && port > 0)
                                        _metricsPort = port;
                                }
                            }
                        }

                        // VRAM is now read from GpuInfo in the drawing code
                    }
                    else
                    {
                        // No loaded model — check if any model exists at all
                        _modelName = "(no model loaded)";
                        _metricsPort = 0;
                    }
                }
            }
            catch { }
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

                        // llama.cpp uses colon separator: llamacpp:metric_name
                        if (line.StartsWith("llamacpp:predicted_tokens_seconds "))
                        {
                            // Direct gauge: average generation throughput
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val))
                                directTps = (float)val;
                        }
                        else if (line.StartsWith("llamacpp:tokens_predicted_total "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val))
                            {
                                tokensTotal = val;
                                foundTokens = true;
                            }
                        }
                        else if (line.StartsWith("llamacpp:requests_processing "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val))
                                _requestsProcessing = (int)val;
                        }
                        else if (line.StartsWith("llamacpp:prompt_tokens_total "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val))
                                _promptTokensTotal = val;
                        }
                        else if (line.StartsWith("llamacpp:prompt_tokens_seconds "))
                        {
                            double val = ParsePrometheusValue(line);
                            if (!double.IsNaN(val) && val > 0)
                                _estimatedTtftMs = (float)(1000.0 / val); // 1/throughput ≈ latency per token
                        }
                    }

                    // Use direct throughput gauge if available, else fall back to delta calculation
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
                            {
                                _tokensPerSec = (float)(deltaTokens / deltaSec);
                            }
                        }
                        _prevTokensTotal = tokensTotal;
                        _prevTokensTime = now;
                    }

                    // Estimate TTFT from prompt tokens delta
                    if (_prevPromptTokensTotal >= 0)
                    {
                        double promptDelta = _promptTokensTotal - _prevPromptTokensTotal;
                        if (promptDelta > 0 && _estimatedTtftMs < 10)
                        {
                            _estimatedTtftMs = (float)(promptDelta / 1000.0 * 1000.0);
                            if (_estimatedTtftMs < 10) _estimatedTtftMs = 0;
                        }
                    }
                    _prevPromptTokensTotal = _promptTokensTotal;

                    // Estimate KV cache total from context if not directly available
                    if (_kvCacheTotal == 0 && _kvCacheUsed > 0)
                    {
                        int[] contextSizes = { 2048, 4096, 8192, 16384, 32768, 65536, 131072 };
                        foreach (int cs in contextSizes)
                        {
                            if (_kvCacheUsed <= cs)
                            {
                                _kvCacheTotal = cs;
                                break;
                            }
                        }
                        if (_kvCacheTotal == 0) _kvCacheTotal = 131072;
                    }
                }
            }
            catch { }
        }

        private double ParsePrometheusValue(string line)
        {
            // Prometheus format: metric_name{labels} value
            // or: metric_name value
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string valStr = parts[parts.Length - 1];
                if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    return val;
            }
            return double.NaN;
        }

        // === WAVEFORM GENERATION ===

        private void UpdateWaveform()
        {
            int pixelsToShift = 3;

            // Shift buffer left
            for (int i = 0; i < WaveformWidth - pixelsToShift; i++)
            {
                _waveformBuffer[i] = _waveformBuffer[i + pixelsToShift];
            }

            // Generate new samples on the right
            for (int i = WaveformWidth - pixelsToShift; i < WaveformWidth; i++)
            {
                _waveformBuffer[i] = GenerateWaveformSample(i);
            }

            _idlePhase += 0.15f;
            if (_idlePhase > (float)(Math.PI * 200))
                _idlePhase -= (float)(Math.PI * 200);
        }

        private float GenerateWaveformSample(int x)
        {
            if (_serverStatus == "offline" || _serverStatus == "unknown")
            {
                // Gentle idle sine wave
                return (float)(Math.Sin(_idlePhase + x * 0.04) * 10.0);
            }

            if (_serverStatus == "loading")
            {
                // Chaotic noise during loading
                float noise = (float)(_rng.NextDouble() * 2.0 - 1.0) * 40f;
                float sine = (float)(Math.Sin(_idlePhase * 2 + x * 0.1) * 15.0);
                return noise * 0.6f + sine * 0.4f;
            }

            if (_tokensPerSec > 0.5f)
            {
                // Active generation: rhythmic pulses proportional to t/s
                float freq = 0.05f + _tokensPerSec * 0.003f;
                float amplitude = Math.Min(45f, 15f + _tokensPerSec * 0.5f);
                float primary = (float)(Math.Sin(_idlePhase + x * freq) * amplitude);
                float harmonic = (float)(Math.Sin(_idlePhase * 1.5 + x * freq * 2.1) * amplitude * 0.3);
                float noise = (float)(_rng.NextDouble() * 2.0 - 1.0) * 3f;
                return primary + harmonic + noise;
            }

            // Server ok but idle - gentle breathing
            float breath = (float)(Math.Sin(_idlePhase * 0.5 + x * 0.03) * 12.0);
            float micro = (float)(_rng.NextDouble() * 2.0 - 1.0) * 2f;
            return breath + micro;
        }

        // === RENDERING ===

        private void DrawFrame()
        {
            int w = BitmapCurrent.Width;  // 480
            int h = BitmapCurrent.Height; // 320

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // Clear background
                using (var bgBrush = new SolidBrush(BgColor))
                {
                    g.FillRectangle(bgBrush, 0, 0, w, h);
                }

                // === HEADER BAR (0-50px) ===
                DrawHeader(g, w);

                // === WAVEFORM AREA (52-185px) ===
                DrawWaveform(g, w);

                // === CONTEXT WINDOW BAR (188-250px) ===
                DrawContextBar(g, w);

                // === METRICS ROW (254-290px) ===
                DrawMetricsRow(g, w);

                // === TOUCH CONTROLS HINT (294-320px) ===
                DrawTouchHints(g, w, h);

                // === OVERLAYS ===
                DrawOverlays(g, w, h);

                // Kill armed: red border flash
                if (_killArmed && (DateTime.UtcNow - _killArmedTime).TotalSeconds < 3)
                {
                    _killFlash = !_killFlash;
                    if (_killFlash)
                    {
                        using (var redPen = new Pen(RedAlert, 3))
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

        private void DrawHeader(Graphics g, int w)
        {
            int headerH = 50;

            // Header gradient background
            using (var headerBrush = new LinearGradientBrush(
                new Rectangle(0, 0, w, headerH), HeaderGradTop, HeaderGradBot, LinearGradientMode.Vertical))
            {
                g.FillRectangle(headerBrush, 0, 0, w, headerH);
            }

            // Bottom accent line
            using (var accentPen = new Pen(Color.FromArgb(40, 0, 200, 255), 1))
            {
                g.DrawLine(accentPen, 0, headerH - 1, w, headerH - 1);
            }

            // Left: Model name
            string displayModel = string.IsNullOrEmpty(_modelName) ? "Waiting for LLM..." : _modelName;
            SizeF modelSize = g.MeasureString(displayModel, _titleFont);
            if (modelSize.Width > 220)
            {
                while (displayModel.Length > 4 && g.MeasureString(displayModel + "..", _titleFont).Width > 220)
                    displayModel = displayModel.Substring(0, displayModel.Length - 1);
                displayModel += "..";
            }

            var modelColor = _serverStatus == "offline" ? DimText : Color.White;
            using (var modelBrush = new SolidBrush(modelColor))
            {
                g.DrawString(displayModel, _titleFont, modelBrush, 8, 12);
            }

            // Status dot
            Color dotColor;
            switch (_serverStatus)
            {
                case "ok": dotColor = Color.FromArgb(80, 200, 80); break;
                case "loading": dotColor = AmberWarn; break;
                case "no_slot": dotColor = AmberWarn; break;
                default: dotColor = Color.FromArgb(100, 100, 120); break;
            }
            float dotX = 8 + g.MeasureString(displayModel, _titleFont).Width + 6;
            using (var dotBrush = new SolidBrush(dotColor))
            {
                g.FillEllipse(dotBrush, dotX, 20, 10, 10);
            }

            // Center: tokens/sec
            string tpsStr = _tokensPerSec > 0.1f ? $"{_tokensPerSec:F1} t/s" : "-- t/s";
            using (var tpsBrush = new SolidBrush(_tokensPerSec > 0.1f ? CyanAccent : DimText))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(tpsStr, _valueFont, tpsBrush, new RectangleF(w / 2 - 60, 12, 120, 30), sf);
            }

            // Right: VRAM mini-gauge (real GPU data via nvidia-smi)
            int vramBarW = 140;
            int vramBarX = w - vramBarW - 10;
            int vramBarY = 10;
            int vramBarH = 28;

            using (var vramBg = new SolidBrush(Color.FromArgb(30, 30, 42)))
            {
                g.FillRectangle(vramBg, vramBarX, vramBarY, vramBarW, vramBarH);
            }

            var vram = GpuInfo.GetLocalVram();
            float vramPct = vram.Available ? vram.Percent : 0f;
            if (_serverStatus != "ok" && _serverStatus != "loading" && _serverStatus != "no_slot") vramPct = 0f;
            int vramFillW = (int)(vramBarW * Math.Min(vramPct, 1f));
            if (vramFillW > 1)
            {
                Color vramColor = vramPct < 0.8f ? Color.FromArgb(60, 180, 60) :
                                  vramPct < 0.95f ? AmberWarn : RedAlert;
                using (var vramFill = new SolidBrush(vramColor))
                {
                    g.FillRectangle(vramFill, vramBarX, vramBarY, vramFillW, vramBarH);
                }
            }

            using (var vramBorder = new Pen(BarBorder, 1))
            {
                g.DrawRectangle(vramBorder, vramBarX, vramBarY, vramBarW, vramBarH);
            }

            string vramText = vram.Available ? $"VRAM {vram.UsedGB:F1}/{vram.TotalGB:F0}GB" : "VRAM --";
            using (var vramTextBrush = new SolidBrush(Color.White))
            {
                g.DrawString(vramText, _smallFont, vramTextBrush, vramBarX + 4, vramBarY + 5);
            }
        }

        private void DrawWaveform(Graphics g, int w)
        {
            int topY = 52;
            int botY = 185;
            int centerY = (topY + botY) / 2; // 107
            int maxAmplitude = 50;

            // Build points array for the curve
            // Use every 2nd pixel for performance with DrawCurve
            int step = 2;
            int pointCount = WaveformWidth / step;
            PointF[] linePoints = new PointF[pointCount];
            PointF[] fillPoints = new PointF[pointCount + 2];

            for (int i = 0; i < pointCount; i++)
            {
                int bufIdx = i * step;
                float sample = _waveformBuffer[bufIdx];
                // Clamp to amplitude range
                sample = Math.Max(-maxAmplitude, Math.Min(maxAmplitude, sample));
                float y = centerY - sample;
                linePoints[i] = new PointF(bufIdx, y);
                fillPoints[i] = new PointF(bufIdx, y);
            }

            // Close the fill polygon at the bottom
            fillPoints[pointCount] = new PointF(WaveformWidth - 1, botY);
            fillPoints[pointCount + 1] = new PointF(0, botY);

            // Draw filled area with gradient
            using (var fillBrush = new LinearGradientBrush(
                new Rectangle(0, topY, w, botY - topY),
                CyanGlow, CyanGlowFade, LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBrush, fillPoints);
            }

            // Draw glow line (thick, semi-transparent)
            using (var glowPen = new Pen(Color.FromArgb(80, 0, 200, 255), 4))
            {
                g.DrawCurve(glowPen, linePoints, 0.4f);
            }

            // Draw sharp main line
            using (var linePen = new Pen(CyanAccent, 1.5f))
            {
                g.DrawCurve(linePen, linePoints, 0.4f);
            }

            // Center line (very dim reference)
            using (var refPen = new Pen(Color.FromArgb(20, 0, 200, 255), 1))
            {
                g.DrawLine(refPen, 0, centerY, w, centerY);
            }

            // Loading progress overlay on waveform area
            if (_serverStatus == "loading")
            {
                string loadText = $"Loading... {(_loadProgress * 100):F0}%";
                SizeF textSize = g.MeasureString(loadText, _normalFont);
                float textX = (w - textSize.Width) / 2;
                using (var loadBrush = new SolidBrush(AmberWarn))
                {
                    g.DrawString(loadText, _normalFont, loadBrush, textX, topY + 5);
                }
            }
            else if (_serverStatus == "offline")
            {
                string waitText = "Waiting for LLM...";
                SizeF textSize = g.MeasureString(waitText, _normalFont);
                float textX = (w - textSize.Width) / 2;
                using (var waitBrush = new SolidBrush(DimText))
                {
                    g.DrawString(waitText, _normalFont, waitBrush, textX, topY + 5);
                }
            }
        }

        private void DrawContextBar(Graphics g, int w)
        {
            int barX = 10;
            int barW = 460;
            int barY = 210;
            int barH = 22;
            int labelY = 190;

            // KV Cache label
            float kvPct = _kvCacheTotal > 0 ? (float)_kvCacheUsed / _kvCacheTotal : 0f;
            string kvText = _kvCacheTotal > 0
                ? $"KV Cache: {_kvCacheUsed}/{_kvCacheTotal} ({kvPct * 100:F0}%)"
                : "KV Cache: --";

            using (var labelBrush = new SolidBrush(CyanAccent))
            {
                g.DrawString(kvText, _smallFont, labelBrush, barX, labelY);
            }

            // Bar background
            using (var barBg = new SolidBrush(BarBg))
            {
                g.FillRectangle(barBg, barX, barY, barW, barH);
            }

            if (_kvCacheTotal > 0 && _kvCacheUsed > 0)
            {
                int usedW = (int)(barW * kvPct);
                usedW = Math.Min(usedW, barW);

                // Segment the used portion: system (10%), history (middle), query (last portion)
                int systemW = Math.Max(1, (int)(usedW * 0.10f));
                int queryW = Math.Max(1, (int)(usedW * 0.15f));
                int historyW = usedW - systemW - queryW;
                if (historyW < 0) historyW = 0;

                int segX = barX;

                // System prompt segment (purple)
                if (systemW > 0)
                {
                    using (var sysBrush = new SolidBrush(PurpleSegment))
                    {
                        g.FillRectangle(sysBrush, segX, barY, systemW, barH);
                    }
                    segX += systemW;
                }

                // History segment (blue)
                if (historyW > 0)
                {
                    using (var histBrush = new SolidBrush(BlueSegment))
                    {
                        g.FillRectangle(histBrush, segX, barY, historyW, barH);
                    }
                    segX += historyW;
                }

                // Current query segment (green)
                if (queryW > 0)
                {
                    using (var queryBrush = new SolidBrush(GreenSegment))
                    {
                        g.FillRectangle(queryBrush, segX, barY, queryW, barH);
                    }
                }
            }

            // Bar border
            using (var borderPen = new Pen(BarBorder, 1))
            {
                g.DrawRectangle(borderPen, barX, barY, barW, barH);
            }

            // Segment legend below bar
            int legendY = barY + barH + 3;
            int legX = barX;

            DrawLegendItem(g, ref legX, legendY, PurpleSegment, "system");
            DrawLegendItem(g, ref legX, legendY, BlueSegment, "history");
            DrawLegendItem(g, ref legX, legendY, GreenSegment, "query");
        }

        private void DrawLegendItem(Graphics g, ref int x, int y, Color color, string label)
        {
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, x, y + 2, 12, 12);
            }
            x += 16;
            using (var textBrush = new SolidBrush(DimText))
            {
                g.DrawString(label, _hintFont, textBrush, x, y);
                x += (int)g.MeasureString(label, _hintFont).Width + 12;
            }
        }

        private void DrawMetricsRow(Graphics g, int w)
        {
            int y = 258;
            int pad = 10;

            // TTFT
            using (var labelBrush = new SolidBrush(DimText))
            using (var valBrush = new SolidBrush(CyanAccent))
            {
                g.DrawString("TTFT: ", _labelFont, labelBrush, pad, y);
                float labelW = g.MeasureString("TTFT: ", _labelFont).Width;
                string ttftVal = _estimatedTtftMs > 0 ? $"{_estimatedTtftMs:F0}ms" : "--";
                g.DrawString(ttftVal, _labelFont, valBrush, pad + labelW, y);

                // Separator
                float ttftEnd = pad + labelW + g.MeasureString(ttftVal, _labelFont).Width + 10;

                g.DrawString("|", _labelFont, labelBrush, ttftEnd, y);
                float sepW = g.MeasureString("| ", _labelFont).Width;

                // Queue
                float queueX = ttftEnd + sepW;
                g.DrawString("Queue: ", _labelFont, labelBrush, queueX, y);
                float qLabelW = g.MeasureString("Queue: ", _labelFont).Width;
                g.DrawString(_requestsProcessing.ToString(), _labelFont, valBrush, queueX + qLabelW, y);

                float qEnd = queueX + qLabelW + g.MeasureString(_requestsProcessing.ToString(), _labelFont).Width + 10;

                g.DrawString("|", _labelFont, labelBrush, qEnd, y);

                // Batch
                float batchX = qEnd + sepW;
                g.DrawString("Batch: ", _labelFont, labelBrush, batchX, y);
                float bLabelW = g.MeasureString("Batch: ", _labelFont).Width;
                g.DrawString(_batchSize.ToString(), _labelFont, valBrush, batchX + bLabelW, y);
            }
        }

        private void DrawTouchHints(Graphics g, int w, int h)
        {
            int y = 296;
            string hint = "Swipe \u2195 Temp  |  Tap Stop  |  Hold Kill";
            SizeF hintSize = g.MeasureString(hint, _hintFont);
            float hintX = (w - hintSize.Width) / 2;

            // Subtle separator line
            using (var sepPen = new Pen(Color.FromArgb(30, 40, 55), 1))
            {
                g.DrawLine(sepPen, 10, y - 8, w - 10, y - 8);
            }

            using (var hintBrush = new SolidBrush(Color.FromArgb(70, 70, 90)))
            {
                g.DrawString(hint, _hintFont, hintBrush, hintX, y);
            }

            // Stop flash indicator
            if (_stopRequested && (DateTime.UtcNow - _stopFlashTime).TotalSeconds < 1.5)
            {
                using (var stopBrush = new SolidBrush(RedAlert))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString("STOP SENT", _normalFont, stopBrush, new RectangleF(0, y + 16, w, 20), sf);
                }
            }
        }

        private void DrawOverlays(Graphics g, int w, int h)
        {
            // Temperature overlay
            if (_showTempOverlay && (DateTime.UtcNow - _tempOverlayTime).TotalSeconds < 2)
            {
                // Semi-transparent backdrop
                using (var overlayBg = new SolidBrush(Color.FromArgb(180, 10, 10, 18)))
                {
                    g.FillRectangle(overlayBg, w / 2 - 80, h / 2 - 30, 160, 60);
                }
                using (var borderPen = new Pen(CyanAccent, 2))
                {
                    g.DrawRectangle(borderPen, w / 2 - 80, h / 2 - 30, 160, 60);
                }

                string tempText = $"Temp: {_temperature:F2}";
                SizeF tempSize = g.MeasureString(tempText, _overlayFont);
                using (var tempBrush = new SolidBrush(CyanAccent))
                {
                    g.DrawString(tempText, _overlayFont, tempBrush,
                        (w - tempSize.Width) / 2, h / 2 - 18);
                }
            }
            else
            {
                _showTempOverlay = false;
            }
        }

        // === TOUCH HANDLING ===

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                // Single tap = emergency stop
                _stopRequested = true;
                _stopFlashTime = DateTime.UtcNow;

                // Try to send stop request
                Task.Run(async () =>
                {
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

                RedrawAndSignal();
                return;
            }

            if (click_type == ClickType.Long)
            {
                if (!_killArmed)
                {
                    // First long press: arm the kill
                    _killArmed = true;
                    _killArmedTime = DateTime.UtcNow;
                    RedrawAndSignal();
                }
                else if ((DateTime.UtcNow - _killArmedTime).TotalSeconds < 3)
                {
                    // Second long press within 3s: execute kill
                    _killArmed = false;
                    Task.Run(() =>
                    {
                        try
                        {
                            // Attempt to kill llama.cpp server via system command
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

            if (click_type == ClickType.Double)
            {
                // Swipe simulation via double-click: toggle temperature up
                _temperature += 0.1f;
                if (_temperature > 2.0f) _temperature = 0.0f;
                _showTempOverlay = true;
                _tempOverlayTime = DateTime.UtcNow;

                // Try to POST temperature
                Task.Run(async () =>
                {
                    try
                    {
                        string json = $"{{\"temperature\":{_temperature.ToString(CultureInfo.InvariantCulture)}}}";
                        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        await _httpClient.PostAsync("http://127.0.0.1:8081/props", content);
                    }
                    catch { }
                });

                RedrawAndSignal();
                return;
            }
        }

        // WigiDash swipe support: SwipeUp/SwipeDown for temperature
        public void SwipeEvent(int direction)
        {
            // direction: 0=up, 1=down, 2=left, 3=right (common convention)
            if (direction == 0)
            {
                _temperature = Math.Min(2.0f, _temperature + 0.05f);
            }
            else if (direction == 1)
            {
                _temperature = Math.Max(0.0f, _temperature - 0.05f);
            }

            _showTempOverlay = true;
            _tempOverlayTime = DateTime.UtcNow;

            Task.Run(async () =>
            {
                try
                {
                    string json = $"{{\"temperature\":{_temperature.ToString(CultureInfo.InvariantCulture)}}}";
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync("http://127.0.0.1:8081/props", content);
                }
                catch { }
            });

            RedrawAndSignal();
        }

        private void RedrawAndSignal()
        {
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

        private void SignalUpdate()
        {
            if (BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;
                WidgetUpdated?.Invoke(this, e);
            }
        }

        // === LIFECYCLE ===

        public void RequestUpdate()
        {
            if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    DrawFrame();
                    SignalUpdate();
                }
                catch { }
                finally
                {
                    _drawingMutex.ReleaseMutex();
                }
            }
        }

        public void EnterSleep()
        {
            _pausePoll = true;
        }

        public void ExitSleep()
        {
            _pausePoll = false;
        }

        public void Dispose()
        {
            _pausePoll = true;
            _runPoll = false;

            // Don't block UI thread - just signal stop, thread will exit on next cycle
            if (_pollThread != null)
            {
                _pollThread.Join(100);
            }

            // Mutex disposed by GC after thread exits
            BitmapCurrent?.Dispose();
            _titleFont?.Dispose();
            _normalFont?.Dispose();
            _smallFont?.Dispose();
            _valueFont?.Dispose();
            _labelFont?.Dispose();
            _hintFont?.Dispose();
            _overlayFont?.Dispose();
        }

        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
    }
}
