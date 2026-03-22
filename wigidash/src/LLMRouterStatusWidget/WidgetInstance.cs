using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace LLMRouterStatusWidget
{
    /// <summary>
    /// Widget instance implementation - polling, rendering, and lifecycle.
    /// This is a partial class - see WidgetInstanceBase.cs for properties.
    /// </summary>
    public partial class LLMRouterStatusWidgetInstance
    {
        // Threading
        private Thread _taskThread;
        private volatile bool _runTask = false;
        private volatile bool _pauseTask = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;
        private static readonly HttpClient _sharedHttpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(3) };

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;

        // Animation
        private int _animationFrame = 0;
        private bool _isActive = false; // Tracks if we're loading/switching

        public LLMRouterStatusWidgetInstance(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            Initialize(parent, widget_size, instance_guid, resourcePath);
            LoadSettings();

            // Initialize with idle state
            var vram = GpuInfo.GetLocalVram();
            CurrentModel = new ModelInfo
            {
                Name = "No model",
                State = ModelLoadState.Idle,
                ProgressPercent = 0,
                VramUsedMB = 0,
                VramTotalMB = vram.Available ? (long)vram.TotalMB : 0,
                PendingRequests = 0,
                LastSwitchSecondsAgo = 0
            };

            DrawFrame();
            StartTask();
        }

        public void Initialize(IWidgetObject parent, WidgetSize widget_size,
            Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);
        }

        public void StartTask()
        {
            _pauseTask = false;
            _runTask = true;

            _taskThread = new Thread(UpdateTask);
            _taskThread.IsBackground = true;
            _taskThread.Start();
        }

        private void UpdateTask()
        {
            while (_runTask)
            {
                if (!_pauseTask)
                {
                    FetchModelStatus(_sharedHttpClient);

                        if (_drawingMutex.WaitOne(MutexTimeout))
                        {
                            try
                            {
                                DrawFrame();
                                UpdateWidget();
                            }
                            finally
                            {
                                _drawingMutex.ReleaseMutex();
                            }
                        }
                    }

                    // Use active or idle polling interval based on state
                int interval = _isActive ? PollingIntervalActive : PollingIntervalIdle;
                Thread.Sleep(interval);
            }
        }

        private void FetchModelStatus(HttpClient client)
        {
            try
            {
                var response = client.GetAsync(RouterUrl).Result;
                if (response.IsSuccessStatusCode)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    ParseModelStatus(json);
                }
                else
                {
                    // Router not responding
                    CurrentModel.State = ModelLoadState.Error;
                    CurrentModel.Name = "Router offline";
                    _isActive = false;
                }
            }
            catch
            {
                // Network error
                CurrentModel.State = ModelLoadState.Error;
                CurrentModel.Name = "Connection failed";
                _isActive = false;
            }
        }

        private void ParseModelStatus(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var response = serializer.Deserialize<Dictionary<string, object>>(json);

                if (response != null)
                {
                    // Router format: {"data":[{"id":"...","status":{"value":"loaded|unloaded"},"meta":{...}}]}
                    string modelName = null;
                    long modelSize = 0;
                    bool foundLoaded = false;

                    // Find the LOADED model in "data" array (not just data[0]!)
                    if (response.ContainsKey("data"))
                    {
                        var data = response["data"] as System.Collections.ArrayList;
                        if (data != null && data.Count > 0)
                        {
                            // Iterate through all models to find the loaded one
                            foreach (var item in data)
                            {
                                var modelData = item as Dictionary<string, object>;
                                if (modelData == null) continue;

                                // Check status.value == "loaded"
                                if (modelData.ContainsKey("status"))
                                {
                                    var status = modelData["status"] as Dictionary<string, object>;
                                    if (status != null && status.ContainsKey("value"))
                                    {
                                        var statusValue = status["value"]?.ToString();
                                        if (statusValue == "loaded")
                                        {
                                            // Found the loaded model!
                                            foundLoaded = true;

                                            if (modelData.ContainsKey("id"))
                                            {
                                                modelName = modelData["id"]?.ToString();
                                            }

                                            // Get size from meta
                                            if (modelData.ContainsKey("meta"))
                                            {
                                                var meta = modelData["meta"] as Dictionary<string, object>;
                                                if (meta != null && meta.ContainsKey("size"))
                                                {
                                                    modelSize = Convert.ToInt64(meta["size"]);
                                                }
                                            }
                                            break; // Found loaded model, stop searching
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Set model state based on whether we found a LOADED model
                    if (foundLoaded && !string.IsNullOrEmpty(modelName))
                    {
                        // Shorten model name for display
                        if (modelName.StartsWith("CODING-"))
                            modelName = modelName.Substring(7);
                        if (modelName.Length > 25)
                            modelName = modelName.Substring(0, 22) + "...";

                        CurrentModel.Name = modelName;
                        CurrentModel.State = ModelLoadState.Ready;
                        CurrentModel.ProgressPercent = 100;
                        _isActive = false;

                        // Get actual VRAM usage from GPU
                        var vramInfo = GetVramInfo();
                        CurrentModel.VramUsedMB = (long)vramInfo.UsedMB;
                    }
                    else
                    {
                        CurrentModel.Name = "No model";
                        CurrentModel.State = ModelLoadState.Idle;
                        CurrentModel.ProgressPercent = 0;
                        CurrentModel.VramUsedMB = 0;
                        _isActive = false;
                    }

                    // Auto-detect total VRAM
                    var totalVram = GetVramInfo();
                    if (totalVram.Available)
                        CurrentModel.VramTotalMB = (long)totalVram.TotalMB;
                }
            }
            catch
            {
                // JSON parsing failed
                CurrentModel.State = ModelLoadState.Error;
                CurrentModel.Name = "Parse error";
                _isActive = false;
            }
        }

        private VramInfo GetVramInfo()
        {
            try
            {
                Uri routerUri = new Uri(RouterUrl);
                string host = routerUri.Host;

                // If router points to a remote host, query remote GPU VRAM
                if (host != "localhost" && host != "127.0.0.1" && host != "::1")
                {
                    string remoteUrl = $"http://{host}:8089/gpu";
                    return GpuInfo.GetRemoteVramAsync(remoteUrl).Result;
                }
            }
            catch { }

            // Local GPU
            return GpuInfo.GetLocalVram();
        }

        private void DrawFrame()
        {
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Increment animation frame
                _animationFrame = (_animationFrame + 1) % 360;

                // Get state color
                Color stateColor = GetStateColor(CurrentModel.State);
                Color bgColor = Color.FromArgb(20, 20, 30);

                // Background gradient
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, BitmapCurrent.Width, BitmapCurrent.Height),
                    bgColor, Color.Black, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 0, 0, BitmapCurrent.Width, BitmapCurrent.Height);
                }

                // Layout calculations
                int padding = 10;
                int y = padding;

                // Title
                using (Font titleFont = new Font("Arial", 11, FontStyle.Bold))
                {
                    g.DrawString("LLM Router Status", titleFont, Brushes.White, padding, y);
                    y += 25;
                }

                // Separator
                using (Pen pen = new Pen(Color.FromArgb(60, 60, 70), 1))
                {
                    g.DrawLine(pen, padding, y, BitmapCurrent.Width - padding, y);
                    y += 10;
                }

                // Status indicator circle
                int circleSize = 30;
                int circleX = padding;
                DrawStatusCircle(g, circleX, y, circleSize, CurrentModel.State);

                // Model name and status
                using (Font nameFont = new Font("Arial", 10, FontStyle.Bold))
                using (Font statusFont = new Font("Arial", 9))
                {
                    int textX = circleX + circleSize + 10;

                    // Truncate long model names
                    string modelName = CurrentModel.Name;
                    if (modelName.Length > 20)
                    {
                        modelName = modelName.Substring(0, 17) + "...";
                    }

                    g.DrawString(modelName, nameFont, Brushes.White, textX, y);

                    string statusText = GetStateText(CurrentModel.State);
                    if (CurrentModel.State == ModelLoadState.Loading ||
                        CurrentModel.State == ModelLoadState.Switching)
                    {
                        statusText += $" ({CurrentModel.ProgressPercent}%)";
                    }

                    g.DrawString(statusText, statusFont, new SolidBrush(stateColor),
                        textX, y + 15);
                }

                y += circleSize + 15;

                // Progress bar (if loading/switching)
                if (CurrentModel.State == ModelLoadState.Loading ||
                    CurrentModel.State == ModelLoadState.Switching)
                {
                    DrawProgressBar(g, padding, y, BitmapCurrent.Width - padding * 2, 12,
                        CurrentModel.ProgressPercent, stateColor);
                    y += 20;

                    // Estimated time (mock calculation)
                    int estimatedSeconds = (100 - CurrentModel.ProgressPercent) / 5; // ~5% per second
                    using (Font timeFont = new Font("Arial", 8))
                    {
                        g.DrawString($"~{estimatedSeconds}s remaining", timeFont,
                            Brushes.LightGray, padding, y);
                    }
                    y += 20;
                }
                else
                {
                    y += 10;
                }

                // VRAM usage
                DrawVramBar(g, padding, y, BitmapCurrent.Width - padding * 2, 10);
                y += 18;

                // Additional info
                using (Font infoFont = new Font("Arial", 8))
                {
                    string pendingText = $"Pending: {CurrentModel.PendingRequests} request" +
                        (CurrentModel.PendingRequests != 1 ? "s" : "");
                    g.DrawString(pendingText, infoFont, Brushes.LightGray, padding, y);
                    y += 15;

                    if (CurrentModel.LastSwitchSecondsAgo > 0)
                    {
                        string switchText = $"Last switch: {CurrentModel.LastSwitchSecondsAgo}s ago";
                        g.DrawString(switchText, infoFont, Brushes.DarkGray, padding, y);
                    }
                }
            }
        }

        private void DrawStatusCircle(Graphics g, int x, int y, int size, ModelLoadState state)
        {
            Color color = GetStateColor(state);

            // Animated pulsing for loading states
            if (state == ModelLoadState.Loading || state == ModelLoadState.Switching)
            {
                double pulsePhase = _animationFrame * Math.PI / 180.0;
                int pulseAlpha = (int)(Math.Sin(pulsePhase * 2) * 50 + 180);
                color = Color.FromArgb(pulseAlpha, color.R, color.G, color.B);
            }

            // Gradient fill
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x, y, size, size);
                using (var gradBrush = new PathGradientBrush(path))
                {
                    Color centerColor = Color.FromArgb(Math.Min(255, color.A + 40),
                        color.R, color.G, color.B);
                    Color edgeColor = Color.FromArgb(Math.Max(0, color.A - 60),
                        color.R, color.G, color.B);

                    gradBrush.CenterColor = centerColor;
                    gradBrush.SurroundColors = new Color[] { edgeColor };
                    g.FillEllipse(gradBrush, x, y, size, size);
                }
            }

            // Outer ring
            using (var pen = new Pen(color, 2))
            {
                g.DrawEllipse(pen, x - 2, y - 2, size + 4, size + 4);
            }
        }

        private void DrawProgressBar(Graphics g, int x, int y, int width, int height,
            int percent, Color color)
        {
            // Background
            using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 50)))
            {
                g.FillRectangle(bgBrush, x, y, width, height);
            }

            // Progress fill with gradient
            int fillWidth = (int)(width * percent / 100.0);
            if (fillWidth > 0)
            {
                using (var fillBrush = new LinearGradientBrush(
                    new Rectangle(x, y, fillWidth, height),
                    Color.FromArgb(180, color.R, color.G, color.B),
                    Color.FromArgb(120, color.R, color.G, color.B),
                    LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(fillBrush, x, y, fillWidth, height);
                }
            }

            // Border
            using (var pen = new Pen(Color.FromArgb(80, 80, 90), 1))
            {
                g.DrawRectangle(pen, x, y, width - 1, height - 1);
            }
        }

        private void DrawVramBar(Graphics g, int x, int y, int width, int height)
        {
            // Calculate VRAM percentage
            float vramPercent = CurrentModel.VramTotalMB > 0
                ? (float)CurrentModel.VramUsedMB / CurrentModel.VramTotalMB * 100
                : 0;

            // Background
            using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 50)))
            {
                g.FillRectangle(bgBrush, x, y, width, height);
            }

            // VRAM fill
            int fillWidth = (int)(width * vramPercent / 100.0);
            if (fillWidth > 0)
            {
                Color vramColor = vramPercent > 90 ? Color.FromArgb(255, 100, 100) :
                                  vramPercent > 70 ? Color.FromArgb(255, 200, 100) :
                                  Color.FromArgb(100, 200, 255);

                using (var fillBrush = new LinearGradientBrush(
                    new Rectangle(x, y, fillWidth, height),
                    Color.FromArgb(180, vramColor.R, vramColor.G, vramColor.B),
                    Color.FromArgb(120, vramColor.R, vramColor.G, vramColor.B),
                    LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(fillBrush, x, y, fillWidth, height);
                }
            }

            // Border
            using (var pen = new Pen(Color.FromArgb(80, 80, 90), 1))
            {
                g.DrawRectangle(pen, x, y, width - 1, height - 1);
            }

            // VRAM text
            using (Font vramFont = new Font("Arial", 7))
            {
                string vramText = $"VRAM: {CurrentModel.VramUsedMB / 1024.0:F1} / " +
                                  $"{CurrentModel.VramTotalMB / 1024.0:F1} GB";
                SizeF textSize = g.MeasureString(vramText, vramFont);
                g.DrawString(vramText, vramFont, Brushes.White,
                    x + (width - textSize.Width) / 2, y - 12);
            }
        }

        private Color GetStateColor(ModelLoadState state)
        {
            switch (state)
            {
                case ModelLoadState.Idle:
                    return Color.FromArgb(150, 150, 150); // Gray
                case ModelLoadState.Loading:
                    return Color.FromArgb(100, 150, 255); // Blue
                case ModelLoadState.Switching:
                    return Color.FromArgb(255, 200, 100); // Yellow/Amber
                case ModelLoadState.Ready:
                    return Color.FromArgb(100, 255, 150); // Green
                case ModelLoadState.Error:
                    return Color.FromArgb(255, 100, 100); // Red
                default:
                    return Color.Gray;
            }
        }

        private string GetStateText(ModelLoadState state)
        {
            switch (state)
            {
                case ModelLoadState.Idle:
                    return "Idle";
                case ModelLoadState.Loading:
                    return "Loading";
                case ModelLoadState.Switching:
                    return "Switching";
                case ModelLoadState.Ready:
                    return "Ready";
                case ModelLoadState.Error:
                    return "Error";
                default:
                    return "Unknown";
            }
        }

        private void UpdateWidget()
        {
            if (BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;

                WidgetUpdated?.Invoke(this, e);
            }
        }

        // IWidgetInstance interface
        public void RequestUpdate()
        {
            if (_drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    FetchModelStatus(_sharedHttpClient);
                    DrawFrame();
                    UpdateWidget();
                }
                finally
                {
                    _drawingMutex.ReleaseMutex();
                }
            }
        }

        public virtual void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                RequestUpdate();
            }
        }

        public void Dispose()
        {
            _pauseTask = true;
            _runTask = false;

            // Wait for thread to finish
            if (_taskThread != null && _taskThread.IsAlive)
            {
                _taskThread.Join(1000);
            }

            // Dispose mutex
            _drawingMutex?.Dispose();

            BitmapCurrent?.Dispose();
        }

        public void EnterSleep()
        {
            _pauseTask = true;
        }

        public void ExitSleep()
        {
            _pauseTask = false;
        }

        // Settings persistence
        public virtual void UpdateSettings()
        {
            DrawFrame();
            UpdateWidget();
        }

        public virtual void SaveSettings()
        {
            WidgetObject.WidgetManager.StoreSetting(this, "RouterUrl", RouterUrl);
            WidgetObject.WidgetManager.StoreSetting(this, "PollingIntervalActive",
                PollingIntervalActive.ToString());
            WidgetObject.WidgetManager.StoreSetting(this, "PollingIntervalIdle",
                PollingIntervalIdle.ToString());
        }

        public virtual void LoadSettings()
        {
            if (WidgetObject.WidgetManager.LoadSetting(this, "RouterUrl", out string url))
            {
                RouterUrl = url;
            }

            if (WidgetObject.WidgetManager.LoadSetting(this, "PollingIntervalActive",
                out string activeStr))
            {
                if (int.TryParse(activeStr, out int active))
                {
                    PollingIntervalActive = active;
                }
            }

            if (WidgetObject.WidgetManager.LoadSetting(this, "PollingIntervalIdle",
                out string idleStr))
            {
                if (int.TryParse(idleStr, out int idle))
                {
                    PollingIntervalIdle = idle;
                }
            }
        }
    }
}
