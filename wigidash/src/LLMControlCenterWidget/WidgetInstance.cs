using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace LLMControlCenterWidget
{
    public class LLMControlCenterWidgetInstance : IWidgetInstance
    {
        // IWidgetInstance properties
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        // No settings UI for this widget
        public UserControl GetSettingsControl() { return null; }

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;

        // API client
        private RouterApiClient _apiClient;

        // Threading
        private Thread _pollThread;
        private volatile bool _runPoll = false;
        private volatile bool _pausePoll = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;
        private int _pollIntervalMs = 5000;

        // State - Router
        private RouterStatus _routerStatus;
        private bool _routerOnline = false;
        private float _tokensPerSec = 0f;

        // State - Port health
        private static readonly int[] MonitoredPorts = { 8081, 8083, 8085 };
        private Dictionary<int, bool> _portStatus = new Dictionary<int, bool>();

        // State - Model selection
        private string _selectedModel = "";
        private int _selectedIndex = -1;
        private bool _showDropdown = false;
        private bool _isLoading = false;
        private string _loadingMessage = "";

        // Button hit rectangles (computed during draw)
        private Rectangle _btnLoad;
        private Rectangle _btnUnload;
        private Rectangle _btnSwitch;
        private Rectangle _btnRefresh;
        private Rectangle _modelAreaRect;
        private Rectangle _dropdownRect;

        // Fonts (disposed in Dispose)
        private readonly Font _titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font _normalFont = new Font("Segoe UI", 8f);
        private readonly Font _smallFont = new Font("Segoe UI", 7f);
        private readonly Font _valueFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font _btnFont = new Font("Segoe UI", 7f, FontStyle.Bold);

        // Colors - dark sci-fi theme
        private static readonly Color BgColor = Color.FromArgb(20, 20, 30);
        private static readonly Color TitleGradientTop = Color.FromArgb(20, 40, 80);
        private static readonly Color TitleGradientBottom = Color.FromArgb(15, 25, 50);
        private static readonly Color OnlineColor = Color.FromArgb(80, 200, 80);
        private static readonly Color OfflineColor = Color.FromArgb(180, 60, 60);
        private static readonly Color CyanValue = Color.FromArgb(100, 200, 255);
        private static readonly Color ButtonBg = Color.FromArgb(40, 60, 80);
        private static readonly Color ButtonBgHover = Color.FromArgb(50, 80, 110);
        private static readonly Color DimText = Color.FromArgb(140, 140, 160);
        private static readonly Color SectionBorder = Color.FromArgb(40, 50, 70);
        private static readonly Color DropdownBg = Color.FromArgb(30, 30, 45);
        private static readonly Color DropdownSelected = Color.FromArgb(50, 60, 90);
        private static readonly Color VramGreen = Color.FromArgb(60, 180, 60);
        private static readonly Color VramGreenDark = Color.FromArgb(40, 140, 40);
        private static readonly Color VramAmber = Color.FromArgb(220, 180, 40);
        private static readonly Color VramAmberDark = Color.FromArgb(180, 140, 20);
        private static readonly Color VramRed = Color.FromArgb(220, 60, 60);
        private static readonly Color VramRedDark = Color.FromArgb(180, 40, 40);
        private static readonly Color VramBarBg = Color.FromArgb(40, 40, 50);

        // Health check client with short timeout
        private static readonly HttpClient _healthClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        public LLMControlCenterWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);

            // Initialize API client
            _apiClient = new RouterApiClient("http://127.0.0.1:8081");

            // Initialize port status
            foreach (int port in MonitoredPorts)
            {
                _portStatus[port] = false;
            }

            // Initialize empty router status
            _routerStatus = new RouterStatus
            {
                Models = new List<ModelInfo>(),
                LoadedModels = new List<string>(),
                TotalVram = "Detecting...",
                LoadedCount = 0
            };

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
                if (!_pausePoll && !_isLoading)
                {
                    try
                    {
                        FetchAllDataAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch { }

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

                Thread.Sleep(_pollIntervalMs);
            }
        }

        private async Task FetchAllDataAsync()
        {
            // Fetch router status (model list, VRAM, loaded state)
            _routerStatus = await _apiClient.GetStatusAsync();
            _routerOnline = _routerStatus.Models.Count > 0 || _routerStatus.TotalVram != "Offline";

            // Set default selected model
            if (string.IsNullOrEmpty(_selectedModel))
            {
                if (_routerStatus.LoadedModels.Count > 0)
                    _selectedModel = _routerStatus.LoadedModels[0];
                else if (_routerStatus.Models.Count > 0)
                    _selectedModel = _routerStatus.Models[0].Name;
            }

            // Check port health in parallel
            var portTasks = new List<Task>();
            foreach (int port in MonitoredPorts)
            {
                int p = port;
                portTasks.Add(Task.Run(async () =>
                {
                    _portStatus[p] = await CheckPortHealthAsync(p);
                }));
            }
            await Task.WhenAll(portTasks);

            // Try to get tokens/sec from metrics endpoint
            _tokensPerSec = await FetchTokensPerSecAsync();
        }

        private async Task<bool> CheckPortHealthAsync(int port)
        {
            try
            {
                var response = await _healthClient.GetAsync($"http://127.0.0.1:{port}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<float> FetchTokensPerSecAsync()
        {
            try
            {
                var response = await _healthClient.GetAsync("http://127.0.0.1:8081/metrics");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    // Parse tokens_per_second from metrics (Prometheus format or JSON)
                    // Look for pattern like: tokens_per_second 56.6
                    foreach (string line in content.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("tokens_per_second") || trimmed.StartsWith("tps"))
                        {
                            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && float.TryParse(parts[parts.Length - 1],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float tps))
                            {
                                return tps;
                            }
                        }
                    }
                }
            }
            catch { }
            return _tokensPerSec; // Keep previous value on failure
        }

        private void DrawFrame()
        {
            int w = BitmapCurrent.Width;
            int h = BitmapCurrent.Height;

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // Clear background
                using (var bgBrush = new SolidBrush(BgColor))
                {
                    g.FillRectangle(bgBrush, 0, 0, w, h);
                }

                int titleH = 30;
                int pad = 8;

                // === TITLE BAR (0-30) ===
                DrawTitleBar(g, w, titleH);

                // === MODEL SECTION (30-120) ===
                int modelTop = titleH + 2;
                int modelBottom = 120;
                DrawModelSection(g, w, modelTop, modelBottom, pad);

                // === STATUS SECTION (120-200) ===
                int statusTop = modelBottom + 2;
                int statusBottom = h - 70;
                DrawStatusSection(g, w, statusTop, statusBottom, pad);

                // === BUTTON BAR (bottom 70px) ===
                int buttonTop = statusBottom + 2;
                DrawButtonBar(g, w, h, buttonTop, pad);

                // Section dividers
                using (var divPen = new Pen(SectionBorder, 1))
                {
                    g.DrawLine(divPen, 0, modelBottom, w, modelBottom);
                    g.DrawLine(divPen, 0, statusBottom, w, statusBottom);
                }

                // Dropdown overlay (drawn on top of everything)
                if (_showDropdown)
                {
                    DrawDropdownOverlay(g, w, h);
                }
            }
        }

        private void DrawTitleBar(Graphics g, int w, int titleH)
        {
            using (var titleBrush = new LinearGradientBrush(
                new Rectangle(0, 0, w, titleH),
                TitleGradientTop, TitleGradientBottom,
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(titleBrush, 0, 0, w, titleH);
            }

            // Bottom accent line
            using (var accentPen = new Pen(Color.FromArgb(60, 120, 200), 1))
            {
                g.DrawLine(accentPen, 0, titleH - 1, w, titleH - 1);
            }

            g.DrawString("LLM Control Center", _titleFont, Brushes.White, 10, 7);

            // Online/offline indicator in title
            var indicatorColor = _routerOnline ? OnlineColor : OfflineColor;
            using (var dotBrush = new SolidBrush(indicatorColor))
            {
                g.FillEllipse(dotBrush, w - 22, 11, 10, 10);
            }
            // Glow effect for online
            if (_routerOnline)
            {
                using (var glowBrush = new SolidBrush(Color.FromArgb(40, 80, 200, 80)))
                {
                    g.FillEllipse(glowBrush, w - 25, 8, 16, 16);
                }
            }
        }

        private void DrawModelSection(Graphics g, int w, int top, int bottom, int pad)
        {
            int sectionH = bottom - top;
            int midX = w / 2;

            // Left side: Active Model + Dropdown
            using (var dimBrush = new SolidBrush(DimText))
            {
                g.DrawString("Active Model", _smallFont, dimBrush, pad, top + 4);
            }

            // Active model name
            string activeModel = _routerStatus.LoadedModels.Count > 0
                ? _routerStatus.LoadedModels[0]
                : "None";

            using (var cyanBrush = new SolidBrush(CyanValue))
            {
                // Truncate if needed
                string displayName = activeModel;
                SizeF nameSize = g.MeasureString(displayName, _valueFont);
                if (nameSize.Width > midX - pad * 2 - 20)
                {
                    while (displayName.Length > 3 && g.MeasureString(displayName + "...", _valueFont).Width > midX - pad * 2 - 20)
                        displayName = displayName.Substring(0, displayName.Length - 1);
                    displayName += "...";
                }
                g.DrawString(displayName, _valueFont, cyanBrush, pad, top + 20);
            }

            // Dropdown selector box
            _dropdownRect = new Rectangle(pad, top + 40, midX - pad * 2, 24);
            _modelAreaRect = new Rectangle(pad, top + 4, midX - pad, sectionH - 8);
            using (var dropBg = new SolidBrush(DropdownBg))
            using (var borderPen = new Pen(Color.FromArgb(60, 120, 200), 1))
            using (var textBrush = new SolidBrush(Color.White))
            {
                g.FillRectangle(dropBg, _dropdownRect);
                g.DrawRectangle(borderPen, _dropdownRect);

                string dropText = string.IsNullOrEmpty(_selectedModel) ? "Select Model" : _selectedModel;
                // Truncate
                SizeF dropSize = g.MeasureString(dropText, _smallFont);
                if (dropSize.Width > _dropdownRect.Width - 20)
                {
                    while (dropText.Length > 3 && g.MeasureString(dropText + "...", _smallFont).Width > _dropdownRect.Width - 20)
                        dropText = dropText.Substring(0, dropText.Length - 1);
                    dropText += "...";
                }

                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(dropText, _smallFont, textBrush,
                    new RectangleF(_dropdownRect.X + 4, _dropdownRect.Y, _dropdownRect.Width - 20, _dropdownRect.Height), sf);

                // Arrow
                using (var dimBrush = new SolidBrush(DimText))
                {
                    g.DrawString(_showDropdown ? "^" : "v", _smallFont, dimBrush,
                        _dropdownRect.Right - 14, _dropdownRect.Y + 5);
                }
            }

            // Model count
            using (var dimBrush = new SolidBrush(DimText))
            {
                string countText = $"{_routerStatus.LoadedCount} loaded / {_routerStatus.Models.Count} available";
                g.DrawString(countText, _smallFont, dimBrush, pad, top + 68);
            }

            // Right side: VRAM bar gauge
            int vramX = midX + pad;
            int vramW = w - vramX - pad;

            using (var dimBrush = new SolidBrush(DimText))
            {
                g.DrawString("VRAM Usage", _smallFont, dimBrush, vramX, top + 4);
            }

            // Get real VRAM values from GPU
            var vram = GpuInfo.GetLocalVram();
            float usedVram = vram.UsedGB;
            float totalVram = vram.TotalGB;
            float vramPercent = vram.Percent;

            // VRAM text
            using (var cyanBrush = new SolidBrush(CyanValue))
            {
                string vramText = $"{usedVram:F1}/{totalVram:F0} GB ({(vramPercent * 100):F0}%)";
                g.DrawString(vramText, _valueFont, cyanBrush, vramX, top + 20);
            }

            // VRAM bar
            int barY = top + 42;
            int barH = 18;
            var barRect = new Rectangle(vramX, barY, vramW, barH);

            using (var barBgBrush = new SolidBrush(VramBarBg))
            {
                g.FillRectangle(barBgBrush, barRect);
            }

            int fillW = Math.Max(0, Math.Min((int)(vramW * vramPercent), vramW));
            if (fillW > 2)
            {
                Color gradTop, gradBottom;
                if (vramPercent < 0.80f)
                {
                    gradTop = VramGreen;
                    gradBottom = VramGreenDark;
                }
                else if (vramPercent < 0.95f)
                {
                    gradTop = VramAmber;
                    gradBottom = VramAmberDark;
                }
                else
                {
                    gradTop = VramRed;
                    gradBottom = VramRedDark;
                }

                var fillRect = new Rectangle(vramX, barY, fillW, barH);
                using (var fillBrush = new LinearGradientBrush(fillRect, gradTop, gradBottom, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(fillBrush, fillRect);
                }
            }

            // Bar border
            using (var barBorderPen = new Pen(SectionBorder, 1))
            {
                g.DrawRectangle(barBorderPen, barRect);
            }

            // Loaded model list (small, under bar)
            int listY = barY + barH + 4;
            using (var dimBrush = new SolidBrush(DimText))
            {
                if (_routerStatus.LoadedModels.Count > 0)
                {
                    for (int i = 0; i < Math.Min(_routerStatus.LoadedModels.Count, 2); i++)
                    {
                        string name = _routerStatus.LoadedModels[i];
                        // Truncate
                        if (g.MeasureString(name, _smallFont).Width > vramW)
                        {
                            while (name.Length > 3 && g.MeasureString(name + "..", _smallFont).Width > vramW)
                                name = name.Substring(0, name.Length - 1);
                            name += "..";
                        }
                        using (var greenBrush = new SolidBrush(OnlineColor))
                        {
                            g.FillEllipse(greenBrush, vramX, listY + 3, 5, 5);
                        }
                        g.DrawString(name, _smallFont, dimBrush, vramX + 8, listY);
                        listY += 14;
                    }
                }
                else
                {
                    g.DrawString("No models loaded", _smallFont, dimBrush, vramX, listY);
                }
            }
        }

        private void DrawStatusSection(Graphics g, int w, int top, int bottom, int pad)
        {
            int y = top + 6;

            // Router status line
            string routerLabel = "Router:";
            g.DrawString(routerLabel, _normalFont, Brushes.White, pad, y);

            float labelW = g.MeasureString(routerLabel, _normalFont).Width;
            int dotX = pad + (int)labelW + 4;

            // Status dot with glow
            var statusColor = _routerOnline ? OnlineColor : OfflineColor;
            using (var dotBrush = new SolidBrush(statusColor))
            {
                g.FillEllipse(dotBrush, dotX, y + 4, 9, 9);
            }
            if (_routerOnline)
            {
                using (var glowBrush = new SolidBrush(Color.FromArgb(30, 80, 200, 80)))
                {
                    g.FillEllipse(glowBrush, dotX - 2, y + 2, 13, 13);
                }
            }

            string statusText = _routerOnline ? "Online" : "Offline";
            using (var statusBrush = new SolidBrush(statusColor))
            {
                g.DrawString(statusText, _normalFont, statusBrush, dotX + 14, y);
            }

            // Tokens per second (right-aligned)
            if (_tokensPerSec > 0)
            {
                string tpsText = $"{_tokensPerSec:F1} t/s";
                SizeF tpsSize = g.MeasureString(tpsText, _valueFont);
                using (var tpsBrush = new SolidBrush(CyanValue))
                {
                    g.DrawString(tpsText, _valueFont, tpsBrush, w - pad - tpsSize.Width, y);
                }
            }
            else if (_routerOnline)
            {
                using (var dimBrush = new SolidBrush(DimText))
                {
                    SizeF metricsSize = g.MeasureString("-- t/s", _normalFont);
                    g.DrawString("-- t/s", _normalFont, dimBrush, w - pad - metricsSize.Width, y);
                }
            }

            y += 24;

            // Server port indicators
            g.DrawString("Servers:", _normalFont, Brushes.White, pad, y);
            float serversLabelW = g.MeasureString("Servers:", _normalFont).Width;
            int portX = pad + (int)serversLabelW + 8;

            foreach (int port in MonitoredPorts)
            {
                bool online = _portStatus.ContainsKey(port) && _portStatus[port];
                var portColor = online ? OnlineColor : OfflineColor;

                // Port dot
                using (var portDotBrush = new SolidBrush(portColor))
                {
                    g.FillEllipse(portDotBrush, portX, y + 4, 8, 8);
                }

                // Port number label
                string portLabel = port.ToString();
                using (var portTextBrush = new SolidBrush(online ? Color.White : DimText))
                {
                    g.DrawString(portLabel, _smallFont, portTextBrush, portX + 11, y + 1);
                }

                portX += (int)g.MeasureString(portLabel, _smallFont).Width + 22;
            }

            // Loading message
            if (_isLoading && !string.IsNullOrEmpty(_loadingMessage))
            {
                y += 22;
                using (var loadBrush = new SolidBrush(CyanValue))
                {
                    g.DrawString(_loadingMessage, _smallFont, loadBrush, pad, y);
                }
            }
        }

        private void DrawButtonBar(Graphics g, int w, int h, int top, int pad)
        {
            int btnH = 28;
            int btnPad = 6;
            int totalPad = btnPad * 5; // 4 gaps + 2 edges
            int btnW = (w - totalPad) / 4;
            int btnY = top + (h - top - btnH) / 2;

            int x = btnPad;

            // Load button
            _btnLoad = new Rectangle(x, btnY, btnW, btnH);
            DrawButton(g, _btnLoad, "Load", _isLoading);
            x += btnW + btnPad;

            // Unload button
            _btnUnload = new Rectangle(x, btnY, btnW, btnH);
            DrawButton(g, _btnUnload, "Unload", _isLoading);
            x += btnW + btnPad;

            // Switch button
            _btnSwitch = new Rectangle(x, btnY, btnW, btnH);
            DrawButton(g, _btnSwitch, "Switch", _isLoading);
            x += btnW + btnPad;

            // Refresh button
            _btnRefresh = new Rectangle(x, btnY, btnW, btnH);
            DrawButton(g, _btnRefresh, "Refresh", false);
        }

        private void DrawButton(Graphics g, Rectangle rect, string label, bool disabled)
        {
            Color btnColor = disabled ? Color.FromArgb(30, 35, 45) : ButtonBg;

            // Button background with subtle gradient
            using (var btnBrush = new LinearGradientBrush(rect,
                Color.FromArgb(btnColor.R + 10, btnColor.G + 10, btnColor.B + 10),
                btnColor, LinearGradientMode.Vertical))
            {
                g.FillRectangle(btnBrush, rect);
            }

            // Border
            using (var borderPen = new Pen(disabled ? Color.FromArgb(50, 50, 60) : Color.FromArgb(60, 100, 140), 1))
            {
                g.DrawRectangle(borderPen, rect);
            }

            // Text
            var textColor = disabled ? DimText : Color.White;
            using (var textBrush = new SolidBrush(textColor))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(label, _btnFont, textBrush, rect, sf);
            }
        }

        private void DrawDropdownOverlay(Graphics g, int w, int h)
        {
            if (_routerStatus.Models.Count == 0) return;

            int itemH = 20;
            int menuH = Math.Min(_routerStatus.Models.Count * itemH, 180);
            var menuRect = new Rectangle(_dropdownRect.X, _dropdownRect.Bottom + 1, _dropdownRect.Width, menuH);

            // Semi-transparent background behind menu
            using (var overlayBrush = new SolidBrush(Color.FromArgb(180, 10, 10, 20)))
            {
                g.FillRectangle(overlayBrush, 0, menuRect.Y, w, h - menuRect.Y);
            }

            // Menu background
            using (var menuBg = new SolidBrush(DropdownBg))
            using (var borderPen = new Pen(Color.FromArgb(60, 120, 200), 1))
            {
                g.FillRectangle(menuBg, menuRect);
                g.DrawRectangle(borderPen, menuRect);
            }

            int itemY = menuRect.Y;
            for (int i = 0; i < _routerStatus.Models.Count && itemY + itemH <= menuRect.Bottom; i++)
            {
                var model = _routerStatus.Models[i];
                var itemRect = new Rectangle(menuRect.X, itemY, menuRect.Width, itemH);

                // Highlight selected
                if (model.Name == _selectedModel || i == _selectedIndex)
                {
                    using (var selBrush = new SolidBrush(DropdownSelected))
                    {
                        g.FillRectangle(selBrush, itemRect);
                    }
                }

                // Loaded indicator
                var dotColor = model.IsLoaded ? OnlineColor : Color.FromArgb(60, 60, 70);
                using (var dotBrush = new SolidBrush(dotColor))
                {
                    g.FillEllipse(dotBrush, itemRect.X + 4, itemRect.Y + 6, 7, 7);
                }

                // Model name
                string displayText = $"{model.Name} ({model.VramEstimate})";
                var textColor = model.IsLoaded ? Color.White : DimText;
                using (var textBrush = new SolidBrush(textColor))
                {
                    g.DrawString(displayText, _smallFont, textBrush, itemRect.X + 16, itemRect.Y + 3);
                }

                itemY += itemH;
            }
        }

        private void ParseVramValues(string vramString, out float used, out float total)
        {
            used = 0f;
            total = 0f;
            if (string.IsNullOrEmpty(vramString) || vramString == "Offline") return;

            try
            {
                // Expected format: "12.0GB / 16GB"
                string cleaned = vramString.Replace("GB", "").Replace("gb", "");
                string[] parts = cleaned.Split('/');
                if (parts.Length == 2)
                {
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out used);
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out total);
                }
            }
            catch { }
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

        // === TOUCH HANDLING ===

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            var point = new Point(x, y);

            if (click_type == ClickType.Double)
            {
                // Force refresh
                Task.Run(async () =>
                {
                    await FetchAllDataAsync();
                    RedrawAndSignal();
                });
                return;
            }

            if (click_type == ClickType.Long)
            {
                // Long press on model area toggles dropdown
                if (_modelAreaRect.Contains(point) || _dropdownRect.Contains(point))
                {
                    _showDropdown = !_showDropdown;
                    RedrawAndSignal();
                }
                return;
            }

            if (click_type == ClickType.Single)
            {
                // If dropdown is open, handle selection
                if (_showDropdown)
                {
                    HandleDropdownClick(point);
                    return;
                }

                // Dropdown toggle
                if (_dropdownRect.Contains(point))
                {
                    _showDropdown = !_showDropdown;
                    RedrawAndSignal();
                    return;
                }

                // Model area tap - cycle to next model
                if (_modelAreaRect.Contains(point) && !_dropdownRect.Contains(point))
                {
                    CycleSelectedModel();
                    RedrawAndSignal();
                    return;
                }

                // Button clicks
                if (!_isLoading)
                {
                    if (_btnLoad.Contains(point) && !string.IsNullOrEmpty(_selectedModel))
                    {
                        Task.Run(async () => await ExecuteLoadModel(_selectedModel));
                        return;
                    }

                    if (_btnUnload.Contains(point))
                    {
                        string modelToUnload = _routerStatus.LoadedModels.Count > 0
                            ? _routerStatus.LoadedModels[0]
                            : _selectedModel;
                        if (!string.IsNullOrEmpty(modelToUnload))
                        {
                            Task.Run(async () => await ExecuteUnloadModel(modelToUnload));
                        }
                        return;
                    }

                    if (_btnSwitch.Contains(point))
                    {
                        Task.Run(async () => await ExecuteSwitchModel());
                        return;
                    }
                }

                if (_btnRefresh.Contains(point))
                {
                    Task.Run(async () =>
                    {
                        await FetchAllDataAsync();
                        RedrawAndSignal();
                    });
                    return;
                }
            }
        }

        private void HandleDropdownClick(Point point)
        {
            int itemH = 20;
            int menuH = Math.Min(_routerStatus.Models.Count * itemH, 180);
            var menuRect = new Rectangle(_dropdownRect.X, _dropdownRect.Bottom + 1, _dropdownRect.Width, menuH);

            if (menuRect.Contains(point))
            {
                int itemIndex = (point.Y - menuRect.Y) / itemH;
                if (itemIndex >= 0 && itemIndex < _routerStatus.Models.Count)
                {
                    _selectedModel = _routerStatus.Models[itemIndex].Name;
                    _selectedIndex = itemIndex;
                }
            }

            _showDropdown = false;
            RedrawAndSignal();
        }

        private void CycleSelectedModel()
        {
            if (_routerStatus.Models.Count == 0) return;

            int currentIdx = -1;
            for (int i = 0; i < _routerStatus.Models.Count; i++)
            {
                if (_routerStatus.Models[i].Name == _selectedModel)
                {
                    currentIdx = i;
                    break;
                }
            }

            int nextIdx = (currentIdx + 1) % _routerStatus.Models.Count;
            _selectedModel = _routerStatus.Models[nextIdx].Name;
            _selectedIndex = nextIdx;
        }

        private async Task ExecuteLoadModel(string modelName)
        {
            _isLoading = true;
            _loadingMessage = $"Loading {modelName}...";
            RedrawAndSignal();

            try
            {
                bool success = await _apiClient.LoadModelAsync(modelName);
                _loadingMessage = success
                    ? $"Loaded {modelName}"
                    : $"Failed: {modelName}";
                RedrawAndSignal();

                await Task.Delay(2000);
                await FetchAllDataAsync();
            }
            finally
            {
                _isLoading = false;
                _loadingMessage = "";
                RedrawAndSignal();
            }
        }

        private async Task ExecuteUnloadModel(string modelName)
        {
            _isLoading = true;
            _loadingMessage = $"Unloading {modelName}...";
            RedrawAndSignal();

            try
            {
                bool success = await _apiClient.UnloadModelAsync(modelName);
                _loadingMessage = success
                    ? $"Unloaded {modelName}"
                    : $"Failed: {modelName}";
                RedrawAndSignal();

                await Task.Delay(2000);
                await FetchAllDataAsync();
            }
            finally
            {
                _isLoading = false;
                _loadingMessage = "";
                RedrawAndSignal();
            }
        }

        private async Task ExecuteSwitchModel()
        {
            // Find next available model that is not currently loaded
            string nextModel = null;
            for (int i = 0; i < _routerStatus.Models.Count; i++)
            {
                if (!_routerStatus.Models[i].IsLoaded)
                {
                    nextModel = _routerStatus.Models[i].Name;
                    break;
                }
            }

            if (nextModel == null && _routerStatus.Models.Count > 0)
            {
                // All loaded - cycle through anyway
                CycleSelectedModel();
                nextModel = _selectedModel;
            }

            if (string.IsNullOrEmpty(nextModel)) return;

            _isLoading = true;
            _loadingMessage = $"Switching to {nextModel}...";
            RedrawAndSignal();

            try
            {
                // Unload current if loaded
                if (_routerStatus.LoadedModels.Count > 0)
                {
                    string current = _routerStatus.LoadedModels[0];
                    _loadingMessage = $"Unloading {current}...";
                    RedrawAndSignal();
                    await _apiClient.UnloadModelAsync(current);
                    await Task.Delay(1000);
                }

                // Load new model
                _loadingMessage = $"Loading {nextModel}...";
                RedrawAndSignal();
                bool success = await _apiClient.LoadModelAsync(nextModel);

                _loadingMessage = success
                    ? $"Switched to {nextModel}"
                    : $"Failed: {nextModel}";
                _selectedModel = nextModel;
                RedrawAndSignal();

                await Task.Delay(2000);
                await FetchAllDataAsync();
            }
            finally
            {
                _isLoading = false;
                _loadingMessage = "";
                RedrawAndSignal();
            }
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

            if (_pollThread != null && _pollThread.IsAlive)
            {
                _pollThread.Join(100);
            }

            // Mutex disposed by GC after thread exits
            _apiClient?.Dispose();
            BitmapCurrent?.Dispose();
            _titleFont?.Dispose();
            _normalFont?.Dispose();
            _smallFont?.Dispose();
            _valueFont?.Dispose();
            _btnFont?.Dispose();
        }

        // No settings persistence needed for this unified widget
        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
    }
}
