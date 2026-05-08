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

namespace LLMStatusWidget
{
    /// <summary>
    /// Widget instance implementation - polling, rendering, and lifecycle.
    /// This is a partial class - see WidgetInstanceBase.cs for properties.
    /// </summary>
    public partial class LLMStatusWidgetInstance
    {
        // Threading
        private Thread _taskThread;
        private volatile bool _runTask = false;
        private volatile bool _pauseTask = false;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private bool _lastKnownStatus = false;

        // Animation
        private int _animationFrame = 0;
        private Bitmap _backgroundImage = null;

        public LLMStatusWidgetInstance(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            InitializeDefaults();
            Initialize(parent, widget_size, instance_guid, resourcePath);
            LoadSettings();
            DrawFrame(_lastKnownStatus);
            StartTask();
        }

        public void Initialize(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);

            // Load background image
            LoadBackgroundImage();
        }

        private void LoadBackgroundImage()
        {
            // Try resource folder first, then hardcoded path
            string[] paths = new string[]
            {
                Path.Combine(_resourcePath, "background.png"),
                Path.Combine(_resourcePath, "background.jpg"),
                System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Pictures", "brighterneon.png")
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        _backgroundImage = new Bitmap(path);
                        break;
                    }
                    catch { }
                }
            }
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
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(3);

                while (_runTask)
                {
                    if (!_pauseTask)
                    {
                        bool isOnline = CheckServerStatus(client);
                        
                        if (_drawingMutex.WaitOne(MutexTimeout))
                        {
                            try
                            {
                                DrawFrame(isOnline);
                                UpdateWidget();
                            }
                            finally
                            {
                                _drawingMutex.ReleaseMutex();
                            }
                        }
                    }

                    Thread.Sleep(PollingIntervalMs);
                }
            }
        }

        private bool CheckServerStatus(HttpClient client)
        {
            bool anyOnline = false;

            // Check all monitored ports
            for (int i = 0; i < MonitoredPorts.Length; i++)
            {
                PortStatus[i] = CheckSingleServerStatus(client, MonitoredPorts[i]);
                if (PortStatus[i] == ServerStatus.Online) anyOnline = true;
            }

            _lastKnownStatus = anyOnline;
            return anyOnline;
        }

        private ServerStatus CheckSingleServerStatus(HttpClient client, int port)
        {
            try
            {
                // Step 1: Check if server is responding via /health OR /v1/models
                // (LM Studio doesn't have /health, but has /v1/models)
                bool serverResponding = false;
                bool isLLMServer = false;
                
                // Try /health first
                try
                {
                    string healthUrl = "http://localhost:" + port + "/health";
                    var healthResponse = client.GetAsync(healthUrl).Result;
                    if (healthResponse.IsSuccessStatusCode)
                    {
                        serverResponding = true;
                    }
                }
                catch { }
                
                // Try /v1/models to check if it's an LLM server
                try
                {
                    string modelsUrl = "http://localhost:" + port + "/v1/models";
                    var modelsResponse = client.GetAsync(modelsUrl).Result;
                    if (modelsResponse.IsSuccessStatusCode)
                    {
                        serverResponding = true;
                        isLLMServer = true;
                    }
                    else if (modelsResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Server responds but no /v1/models - not an LLM server
                        isLLMServer = false;
                    }
                }
                catch { }
                
                if (!serverResponding)
                {
                    return ServerStatus.Offline;
                }
                
                // If not an LLM server, just return Online (health check passed)
                if (!isLLMServer)
                {
                    return ServerStatus.Online;
                }
                
                // Step 2: For LLM servers, test if a model is actually loaded
                // by sending a minimal completion request
                try
                {
                    return TestModelLoaded(client, port) ? ServerStatus.Online : ServerStatus.Loading;
                }
                catch
                {
                    return ServerStatus.Loading;
                }
            }
            catch
            {
                return ServerStatus.Offline;
            }
        }
        
        private bool TestModelLoaded(HttpClient client, int port)
        {
            try
            {
                // Get models list to check if any model is loaded
                // This avoids the hardcoded "test" model name issue with llama-swap router
                string modelsUrl = "http://localhost:" + port + "/v1/models";
                var modelsResponse = client.GetAsync(modelsUrl).Result;

                if (!modelsResponse.IsSuccessStatusCode)
                    return false;

                string modelsJson = modelsResponse.Content.ReadAsStringAsync().Result;

                // Check if any model has status "loaded" (router format)
                // Router returns: {"data":[{"id":"model-name",...,"status":{"value":"loaded"}}]}
                if (modelsJson.Contains("\"value\":\"loaded\""))
                    return true;

                // For standalone llama-server (orchestrator), presence of "data" array means loaded
                // Standalone returns: {"models":[...],"data":[{"id":"model-name",...}]}
                if (modelsJson.Contains("\"data\":[{\"id\":"))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HasLoadedModels(string json)
        {
            try
            {
                // Parse JSON to check if models exist
                // LM Studio format: {"data": [{model1}, ...]}
                // llama.cpp format: {"models": [...], "data": [...]}
                var serializer = new JavaScriptSerializer();
                var response = serializer.Deserialize<Dictionary<string, object>>(json);
                
                if (response != null)
                {
                    // Check "data" array (OpenAI/LM Studio format)
                    if (response.ContainsKey("data"))
                    {
                        var data = response["data"] as System.Collections.ArrayList;
                        if (data != null && data.Count > 0)
                        {
                            return true;
                        }
                    }
                    
                    // Check "models" array (llama.cpp format)
                    if (response.ContainsKey("models"))
                    {
                        var models = response["models"] as System.Collections.ArrayList;
                        if (models != null && models.Count > 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                // If JSON parsing fails, assume no models
                return false;
            }
        }

        private void DrawFrame(bool isOnline)
        {
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                string imagePath = isOnline ? OnlineImagePath : OfflineImagePath;

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    try
                    {
                        using (var img = Image.FromFile(imagePath))
                        {
                            // Scale to fit widget
                            float scale = Math.Min(
                                (float)BitmapCurrent.Width / img.Width,
                                (float)BitmapCurrent.Height / img.Height);

                            int newWidth = (int)(img.Width * scale);
                            int newHeight = (int)(img.Height * scale);
                            int x = (BitmapCurrent.Width - newWidth) / 2;
                            int y = (BitmapCurrent.Height - newHeight) / 2;

                            g.DrawImage(img, x, y, newWidth, newHeight);
                        }
                    }
                    catch
                    {
                        DrawFallback(g, isOnline);
                    }
                }
                else
                {
                    DrawFallback(g, isOnline);
                }
            }
        }

        private void DrawFallback(Graphics g, bool isOnline)
        {
            // Increment animation frame
            _animationFrame = (_animationFrame + 1) % 360;

            // Draw background image or fallback gradient
            if (_backgroundImage != null)
            {
                // Draw bright neon background with light overlay for readability
                g.DrawImage(_backgroundImage, 0, 0, BitmapCurrent.Width, BitmapCurrent.Height);

                // Light overlay - keeps neon visible but improves text readability
                using (var overlay = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                {
                    g.FillRectangle(overlay, 0, 0, BitmapCurrent.Width, BitmapCurrent.Height);
                }
            }
            else
            {
                // Fallback gradient
                Color bgColor = isOnline ? Color.FromArgb(0, 20, 30) : Color.FromArgb(20, 10, 30);
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, BitmapCurrent.Width, BitmapCurrent.Height),
                    bgColor, Color.Black, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 0, 0, BitmapCurrent.Width, BitmapCurrent.Height);
                }
            }

            // Dynamic grid layout based on port count
            int portCount = MonitoredPorts.Length;
            int cols = portCount <= 4 ? 2 : 3;
            int rows = (int)Math.Ceiling((double)portCount / cols);

            int padding = 8;
            int cellWidth = (BitmapCurrent.Width - padding * (cols + 1)) / cols;
            int cellHeight = (BitmapCurrent.Height - padding * (rows + 1)) / rows;

            for (int i = 0; i < portCount; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int x = padding + col * (cellWidth + padding);
                int y = padding + row * (cellHeight + padding);

                // Get the three-state status for this port
                ServerStatus status = i < PortStatus.Length ? PortStatus[i] : ServerStatus.Offline;

                // Calculate pulsing glow intensity
                double pulsePhase = (_animationFrame + i * 45) * Math.PI / 180.0;
                int pulseIntensity = status != ServerStatus.Offline ? (int)(Math.Sin(pulsePhase) * 25 + 50) : 0;

                // Glow effect based on status
                if (status != ServerStatus.Offline && pulseIntensity > 0)
                {
                    int glowSize = 3;
                    Color glowColor = status == ServerStatus.Online 
                        ? Color.FromArgb(pulseIntensity / 2, 150, 255, 150)   // Green glow
                        : Color.FromArgb(pulseIntensity / 2, 255, 200, 100);  // Amber glow for loading
                    using (var glowBrush = new SolidBrush(glowColor))
                    {
                        g.FillRectangle(glowBrush, x - glowSize, y - glowSize,
                            cellWidth + glowSize * 2, cellHeight + glowSize * 2);
                    }
                }

                // Cell background - darker semi-transparent for contrast against bright bg
                using (var brush = new SolidBrush(Color.FromArgb(180, 20, 15, 30)))
                {
                    g.FillRectangle(brush, x, y, cellWidth, cellHeight);
                }

                // Status indicator circle with gradient
                int circleSize = Math.Min(cellWidth, cellHeight) / 3;
                int circleX = x + (cellWidth - circleSize) / 2;
                int circleY = y + cellHeight / 5;

                if (status == ServerStatus.Online)
                {
                    // Bright cyan/teal gradient - model loaded and ready
                    int glowAlpha = (int)(Math.Sin(pulsePhase) * 40 + 215);
                    Color centerColor = Color.FromArgb(glowAlpha, 100, 255, 220);  // Bright cyan center
                    Color edgeColor = Color.FromArgb(glowAlpha - 40, 0, 200, 150); // Teal edge

                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(circleX, circleY, circleSize, circleSize);
                        using (var gradBrush = new PathGradientBrush(path))
                        {
                            gradBrush.CenterColor = centerColor;
                            gradBrush.SurroundColors = new Color[] { edgeColor };
                            g.FillEllipse(gradBrush, circleX, circleY, circleSize, circleSize);
                        }
                    }

                    // Bright outer glow ring
                    using (var pen = new Pen(Color.FromArgb(pulseIntensity + 60, 0, 255, 180), 2))
                    {
                        g.DrawEllipse(pen, circleX - 3, circleY - 3, circleSize + 6, circleSize + 6);
                    }
                }
                else if (status == ServerStatus.Loading)
                {
                    // Amber/yellow pulsing - server up but no model loaded
                    int glowAlpha = (int)(Math.Sin(pulsePhase * 2) * 50 + 180); // Faster pulse for loading
                    Color centerColor = Color.FromArgb(glowAlpha, 255, 200, 80);   // Bright amber center
                    Color edgeColor = Color.FromArgb(glowAlpha - 40, 200, 140, 30); // Orange edge

                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(circleX, circleY, circleSize, circleSize);
                        using (var gradBrush = new PathGradientBrush(path))
                        {
                            gradBrush.CenterColor = centerColor;
                            gradBrush.SurroundColors = new Color[] { edgeColor };
                            g.FillEllipse(gradBrush, circleX, circleY, circleSize, circleSize);
                        }
                    }

                    // Amber outer glow ring
                    using (var pen = new Pen(Color.FromArgb(pulseIntensity + 40, 255, 180, 50), 2))
                    {
                        g.DrawEllipse(pen, circleX - 3, circleY - 3, circleSize + 6, circleSize + 6);
                    }
                }
                else
                {
                    // Offline - subtle dark with slight red tint
                    using (var brush = new SolidBrush(Color.FromArgb(50, 40, 40)))
                    {
                        g.FillEllipse(brush, circleX, circleY, circleSize, circleSize);
                    }
                    using (var pen = new Pen(Color.FromArgb(40, 80, 50, 50), 1))
                    {
                        g.DrawEllipse(pen, circleX, circleY, circleSize, circleSize);
                    }
                }

                // Port label
                string label = i < PortLabels.Length ? PortLabels[i] : "Port";
                using (Font f = new Font("Arial", Math.Max(9, cellWidth / 7), FontStyle.Bold))
                {
                    SizeF textSize = g.MeasureString(label, f);
                    float textX = x + (cellWidth - textSize.Width) / 2;
                    float textY = y + cellHeight * 0.52f;

                    // Text shadow
                    g.DrawString(label, f, Brushes.Black, textX + 1, textY + 1);
                    // Text color based on status: white=online, gold=loading, gray=offline
                    Brush textBrush = status == ServerStatus.Online ? Brushes.White 
                        : status == ServerStatus.Loading ? Brushes.Gold : Brushes.Gray;
                    g.DrawString(label, f, textBrush, textX, textY);
                }

                // Port number
                string portNum = MonitoredPorts[i].ToString();
                using (Font f = new Font("Arial", Math.Max(8, cellWidth / 9)))
                {
                    SizeF textSize = g.MeasureString(portNum, f);
                    float textX = x + (cellWidth - textSize.Width) / 2;
                    float textY = y + cellHeight * 0.75f;
                    Brush portBrush = status == ServerStatus.Online ? Brushes.LightGray 
                        : status == ServerStatus.Loading ? Brushes.Khaki : Brushes.DimGray;
                    g.DrawString(portNum, f, portBrush, textX, textY);
                }

                // Border - subtle, color based on status
                Color borderColor = status == ServerStatus.Online
                    ? Color.FromArgb(60 + pulseIntensity, 0, 150, 70)   // Green
                    : status == ServerStatus.Loading
                        ? Color.FromArgb(60 + pulseIntensity, 200, 150, 30) // Amber
                        : Color.FromArgb(30, 50, 50, 50);  // Gray
                using (var pen = new Pen(borderColor, 1))
                {
                    g.DrawRectangle(pen, x, y, cellWidth - 1, cellHeight - 1);
                }
            }
        }

        private void UpdateWidget()
        {
            if (BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;

                if (WidgetUpdated != null) WidgetUpdated.Invoke(this, e);
            }
        }

        // IWidgetInstance interface
        public void RequestUpdate()
        {
            if (_drawingMutex.WaitOne(MutexTimeout))
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(2);
                        bool isOnline = CheckServerStatus(client);
                        DrawFrame(isOnline);
                        UpdateWidget();
                    }
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

            if (BitmapCurrent != null) BitmapCurrent.Dispose();
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
            DrawFrame(_lastKnownStatus);
            UpdateWidget();
        }

        public virtual void SaveSettings()
        {
            WidgetObject.WidgetManager.StoreSetting(this, "ServerUrl", ServerUrl);
            WidgetObject.WidgetManager.StoreSetting(this, "PollingIntervalMs", PollingIntervalMs.ToString());
            
            // Store image paths if set
            if (!string.IsNullOrEmpty(OnlineImagePath))
                WidgetObject.WidgetManager.StoreSetting(this, "OnlineImagePath", OnlineImagePath);
            if (!string.IsNullOrEmpty(OfflineImagePath))
                WidgetObject.WidgetManager.StoreSetting(this, "OfflineImagePath", OfflineImagePath);
        }

        public virtual void LoadSettings()
        {
            string url;
            if (WidgetObject.WidgetManager.LoadSetting(this, "ServerUrl", out url))
            {
                ServerUrl = url;
            }

            string intervalStr;
            if (WidgetObject.WidgetManager.LoadSetting(this, "PollingIntervalMs", out intervalStr))
            {
                int interval;
                if (int.TryParse(intervalStr, out interval))
                {
                    PollingIntervalMs = interval;
                }
            }

            string onlinePath;
            if (WidgetObject.WidgetManager.LoadSetting(this, "OnlineImagePath", out onlinePath))
            {
                OnlineImagePath = onlinePath;
            }

            string offlinePath;
            if (WidgetObject.WidgetManager.LoadSetting(this, "OfflineImagePath", out offlinePath))
            {
                OfflineImagePath = offlinePath;
            }
        }
    }
}
