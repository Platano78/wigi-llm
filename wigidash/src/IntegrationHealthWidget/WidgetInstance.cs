using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Web.Script.Serialization;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiLlm.Shared;

namespace IntegrationHealthWidget
{
    public enum ServiceStatus { Unknown, Running, Stopped, Error }

    public class ServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public ServiceStatus Status { get; set; }
        public string JsonKey { get; set; }
    }

    public class IntegrationHealthWidgetInstance : IWidgetInstance
    {
        // Interface properties
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;

        // Threading
        private Thread _pollThread;
        private volatile bool _runPoll = false;
        private const int PollIntervalMs = 5000;

        // Services
        private List<ServiceInfo> _services;
        private string _wslIp = "--";
        private string _publicUrl = "--";
        private bool _isExecutingCommand = false;
        private string _lastAction = "";

        // Script path under the WSL user home — matches a typical layout where
        // the rest-bridge project lives at $HOME/project/rest-bridge-2.0-full.
        // Override the user portion via WSL_USER_HOME env if your setup differs.
        private static readonly string ScriptPath = WslPaths.UnderHome(
            "project/rest-bridge-2.0-full/scripts/rest-bridge-full-start.ps1");

        // Button rectangles
        private Rectangle _startAllButton;
        private Rectangle _stopAllButton;
        private Rectangle _restartAllButton;
        private Rectangle _refreshButton;
        private Rectangle[] _serviceActionButtons;

        // Layout constants
        private const int TitleBarHeight = 30;
        private const int ServiceRowHeight = 40;
        private const int InfoBarHeight = 38;
        private const int ButtonBarHeight = 40;
        private const int Padding = 8;

        // Colors
        private static readonly Color BgColor = Color.FromArgb(20, 20, 30);
        private static readonly Color TitleGradientTop = Color.FromArgb(40, 60, 80);
        private static readonly Color TitleGradientBottom = Color.FromArgb(30, 45, 60);
        private static readonly Color RunningColor = Color.FromArgb(80, 200, 80);
        private static readonly Color StoppedColor = Color.FromArgb(180, 60, 60);
        private static readonly Color UnknownColor = Color.FromArgb(100, 100, 100);
        private static readonly Color ButtonBg = Color.FromArgb(40, 60, 80);
        private static readonly Color ButtonBorder = Color.FromArgb(60, 80, 100);
        private static readonly Color ActiveHighlight = Color.FromArgb(200, 200, 100);

        public IntegrationHealthWidgetInstance(IWidgetObject parent, WidgetSize widgetSize, Guid instanceGuid, string resourcePath)
        {
            WidgetObject = parent;
            WidgetSize = widgetSize;
            Guid = instanceGuid;
            _resourcePath = resourcePath;

            Size size = widgetSize.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);

            InitializeServices();
            CalculateButtonLayout(size);
            DrawFrame();
            StartPolling();
        }

        private void InitializeServices()
        {
            _services = new List<ServiceInfo>
            {
                new ServiceInfo { Name = "mcp_gateway", DisplayName = "MCP Gateway", JsonKey = "mcp_gateway", Status = ServiceStatus.Unknown },
                new ServiceInfo { Name = "rest_bridge", DisplayName = "REST Bridge", JsonKey = "rest_bridge", Status = ServiceStatus.Unknown },
                new ServiceInfo { Name = "director", DisplayName = "Director", JsonKey = "director", Status = ServiceStatus.Unknown },
                new ServiceInfo { Name = "tunnel", DisplayName = "CF Tunnel", JsonKey = "tunnel", Status = ServiceStatus.Unknown }
            };
        }

        private void CalculateButtonLayout(Size size)
        {
            int width = size.Width;

            // Refresh button in title bar (right side)
            _refreshButton = new Rectangle(width - 32, 4, 24, 22);

            // Per-service action buttons (right side of each service row)
            _serviceActionButtons = new Rectangle[_services.Count];
            int serviceStartY = TitleBarHeight;
            for (int i = 0; i < _services.Count; i++)
            {
                int rowY = serviceStartY + i * ServiceRowHeight;
                _serviceActionButtons[i] = new Rectangle(width - 44, rowY + 8, 32, 24);
            }

            // Bottom button bar
            int buttonBarY = size.Height - ButtonBarHeight - 2;
            int btnWidth = (width - Padding * 4) / 3;
            int btnHeight = ButtonBarHeight - 8;
            _startAllButton = new Rectangle(Padding, buttonBarY + 4, btnWidth, btnHeight);
            _stopAllButton = new Rectangle(Padding * 2 + btnWidth, buttonBarY + 4, btnWidth, btnHeight);
            _restartAllButton = new Rectangle(Padding * 3 + btnWidth * 2, buttonBarY + 4, btnWidth, btnHeight);
        }

        private void StartPolling()
        {
            _runPoll = true;
            _pollThread = new Thread(PollHealth);
            _pollThread.IsBackground = true;
            _pollThread.Start();
        }

        private void PollHealth()
        {
            while (_runPoll)
            {
                try
                {
                    FetchInfo();

                    if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
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
                catch { }

                Thread.Sleep(PollIntervalMs);
            }
        }

        private void FetchInfo()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-ExecutionPolicy Bypass -File \"" + ScriptPath + "\" health",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    if (!string.IsNullOrEmpty(output))
                    {
                        ParseHealthJson(output);
                    }
                    else
                    {
                        SetAllServicesStatus(ServiceStatus.Unknown);
                    }
                }
            }
            catch
            {
                SetAllServicesStatus(ServiceStatus.Unknown);
            }
        }

        private void ParseHealthJson(string output)
        {
            try
            {
                // Extract JSON from output (script may have header text before JSON)
                string json = output;
                int jsonStart = output.IndexOf('{');
                int jsonEnd = output.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    json = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }
                else
                {
                    SetAllServicesStatus(ServiceStatus.Unknown);
                    return;
                }

                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);

                if (data != null)
                {
                    foreach (var service in _services)
                    {
                        if (data.ContainsKey(service.JsonKey))
                        {
                            string statusStr = data[service.JsonKey].ToString().ToLower();
                            service.Status = ParseServiceStatus(statusStr);
                        }
                        else
                        {
                            service.Status = ServiceStatus.Unknown;
                        }
                    }

                    if (data.ContainsKey("wsl_ip"))
                        _wslIp = data["wsl_ip"].ToString();
                    if (data.ContainsKey("domain"))
                        _publicUrl = data["domain"].ToString();
                }
                else
                {
                    SetAllServicesStatus(ServiceStatus.Unknown);
                }
            }
            catch
            {
                SetAllServicesStatus(ServiceStatus.Unknown);
            }
        }

        private ServiceStatus ParseServiceStatus(string status)
        {
            switch (status)
            {
                case "running":
                    return ServiceStatus.Running;
                case "stopped":
                    return ServiceStatus.Stopped;
                case "error":
                    return ServiceStatus.Error;
                default:
                    return ServiceStatus.Unknown;
            }
        }

        private void SetAllServicesStatus(ServiceStatus status)
        {
            foreach (var service in _services)
            {
                service.Status = status;
            }
        }

        private void ExecuteCommand(string action)
        {
            if (_isExecutingCommand) return;

            _isExecutingCommand = true;
            _lastAction = action;

            // Immediate visual feedback
            if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
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

            Thread cmdThread = new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-ExecutionPolicy Bypass -File \"" + ScriptPath + "\" " + action,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (var process = Process.Start(psi))
                    {
                        process.WaitForExit(60000);
                    }

                    // Wait for services to stabilize then refresh
                    Thread.Sleep(3000);
                    FetchInfo();
                }
                catch { }
                finally
                {
                    _isExecutingCommand = false;

                    if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
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
            });
            cmdThread.IsBackground = true;
            cmdThread.Start();
        }

        private void DrawFrame()
        {
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(BgColor);

                int width = BitmapCurrent.Width;
                int height = BitmapCurrent.Height;

                DrawTitleBar(g, width);
                DrawServiceGrid(g, width);
                DrawInfoBar(g, width, height);
                DrawButtonBar(g, width, height);
                DrawBorder(g, width, height);
            }
        }

        private void DrawTitleBar(Graphics g, int width)
        {
            // Title gradient background
            using (var titleBrush = new LinearGradientBrush(
                new Rectangle(0, 0, width, TitleBarHeight),
                TitleGradientTop,
                TitleGradientBottom,
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(titleBrush, 0, 0, width, TitleBarHeight);
            }

            // Bottom separator line
            using (var pen = new Pen(Color.FromArgb(60, 80, 100), 1))
            {
                g.DrawLine(pen, 0, TitleBarHeight, width, TitleBarHeight);
            }

            // Title text
            using (Font titleFont = new Font("Arial", 10, FontStyle.Bold))
            {
                g.DrawString("Integration Health", titleFont, Brushes.White, Padding, 6);
            }

            // Refresh button
            DrawSmallButton(g, _refreshButton, "\u21BB", _isExecutingCommand);
        }

        private void DrawServiceGrid(Graphics g, int width)
        {
            int startY = TitleBarHeight;

            for (int i = 0; i < _services.Count; i++)
            {
                var service = _services[i];
                int rowY = startY + i * ServiceRowHeight;

                // Alternating row background for readability
                if (i % 2 == 1)
                {
                    using (var rowBrush = new SolidBrush(Color.FromArgb(25, 25, 38)))
                    {
                        g.FillRectangle(rowBrush, 0, rowY, width, ServiceRowHeight);
                    }
                }

                // Row separator
                using (var pen = new Pen(Color.FromArgb(35, 40, 55), 1))
                {
                    g.DrawLine(pen, Padding, rowY + ServiceRowHeight - 1, width - Padding, rowY + ServiceRowHeight - 1);
                }

                // Status indicator dot
                Color dotColor = GetStatusColor(service.Status);
                int dotX = Padding + 4;
                int dotY = rowY + (ServiceRowHeight - 10) / 2;

                // Glow effect for running services
                if (service.Status == ServiceStatus.Running)
                {
                    using (var glowBrush = new SolidBrush(Color.FromArgb(30, RunningColor.R, RunningColor.G, RunningColor.B)))
                    {
                        g.FillEllipse(glowBrush, dotX - 4, dotY - 4, 18, 18);
                    }
                    using (var glowBrush2 = new SolidBrush(Color.FromArgb(15, RunningColor.R, RunningColor.G, RunningColor.B)))
                    {
                        g.FillEllipse(glowBrush2, dotX - 7, dotY - 7, 24, 24);
                    }
                }

                using (var dotBrush = new SolidBrush(dotColor))
                {
                    g.FillEllipse(dotBrush, dotX, dotY, 10, 10);
                }

                // Service name
                using (Font nameFont = new Font("Arial", 9, FontStyle.Bold))
                {
                    g.DrawString(service.DisplayName, nameFont, Brushes.White, dotX + 18, rowY + 4);
                }

                // Status text
                string statusText = GetStatusText(service.Status);
                Color statusTextColor = GetStatusColor(service.Status);
                using (Font statusFont = new Font("Arial", 8))
                using (var statusBrush = new SolidBrush(statusTextColor))
                {
                    g.DrawString(statusText, statusFont, statusBrush, dotX + 18, rowY + 22);
                }

                // Per-service action button
                string btnText = service.Status == ServiceStatus.Running ? "\u21BB" : "\u25B6";
                bool btnActive = _isExecutingCommand && _lastAction == ("restart-" + service.Name);
                DrawSmallButton(g, _serviceActionButtons[i], btnText, btnActive);
            }
        }

        private void DrawInfoBar(Graphics g, int width, int height)
        {
            int infoY = TitleBarHeight + _services.Count * ServiceRowHeight;

            // Info bar background
            using (var infoBrush = new SolidBrush(Color.FromArgb(18, 18, 28)))
            {
                g.FillRectangle(infoBrush, 0, infoY, width, InfoBarHeight);
            }

            // Top separator
            using (var pen = new Pen(Color.FromArgb(40, 55, 70), 1))
            {
                g.DrawLine(pen, 0, infoY, width, infoY);
            }

            using (Font labelFont = new Font("Arial", 8, FontStyle.Bold))
            using (Font valueFont = new Font("Arial", 8))
            {
                // WSL IP
                g.DrawString("WSL:", labelFont, Brushes.Gray, Padding, infoY + 4);
                using (var cyanBrush = new SolidBrush(Color.FromArgb(100, 200, 220)))
                {
                    g.DrawString(_wslIp, valueFont, cyanBrush, Padding + 32, infoY + 4);
                }

                // Public URL
                g.DrawString("URL:", labelFont, Brushes.Gray, Padding, infoY + 20);
                using (var cyanBrush = new SolidBrush(Color.Cyan))
                {
                    g.DrawString(_publicUrl, valueFont, cyanBrush, Padding + 32, infoY + 20);
                }
            }
        }

        private void DrawButtonBar(Graphics g, int width, int height)
        {
            int barY = height - ButtonBarHeight - 2;

            // Bar separator
            using (var pen = new Pen(Color.FromArgb(40, 55, 70), 1))
            {
                g.DrawLine(pen, 0, barY, width, barY);
            }

            DrawActionButton(g, _startAllButton, "Start All", Color.FromArgb(40, 120, 60),
                _isExecutingCommand && _lastAction == "start");
            DrawActionButton(g, _stopAllButton, "Stop All", Color.FromArgb(140, 50, 50),
                _isExecutingCommand && _lastAction == "stop");
            DrawActionButton(g, _restartAllButton, "Restart", Color.FromArgb(140, 120, 40),
                _isExecutingCommand && _lastAction == "restart");
        }

        private void DrawBorder(Graphics g, int width, int height)
        {
            using (var pen = new Pen(Color.FromArgb(60, 80, 100), 1))
            {
                g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
            }
        }

        private void DrawSmallButton(Graphics g, Rectangle rect, string text, bool isActive)
        {
            Color bgColor = isActive ? ActiveHighlight : ButtonBg;

            using (var brush = new SolidBrush(bgColor))
            {
                g.FillRectangle(brush, rect);
            }

            using (var pen = new Pen(ButtonBorder, 1))
            {
                g.DrawRectangle(pen, rect);
            }

            using (Font f = new Font("Arial", 9, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(text, f);
                float textX = rect.X + (rect.Width - textSize.Width) / 2;
                float textY = rect.Y + (rect.Height - textSize.Height) / 2;
                Brush textBrush = isActive ? Brushes.Black : Brushes.White;
                g.DrawString(text, f, textBrush, textX, textY);
            }
        }

        private void DrawActionButton(Graphics g, Rectangle rect, string text, Color baseColor, bool isActive)
        {
            Color topColor = isActive ? ActiveHighlight : baseColor;
            Color bottomColor = Color.FromArgb(
                Math.Max(0, (int)(topColor.R * 0.6)),
                Math.Max(0, (int)(topColor.G * 0.6)),
                Math.Max(0, (int)(topColor.B * 0.6)));

            using (var brush = new LinearGradientBrush(rect, topColor, bottomColor, LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, rect);
            }

            using (var pen = new Pen(Color.FromArgb(100, 255, 255, 255), 1))
            {
                g.DrawRectangle(pen, rect);
            }

            using (Font btnFont = new Font("Arial", 8, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(text, btnFont);
                float textX = rect.X + (rect.Width - textSize.Width) / 2;
                float textY = rect.Y + (rect.Height - textSize.Height) / 2;

                // Shadow
                g.DrawString(text, btnFont, Brushes.Black, textX + 1, textY + 1);
                // Text
                Brush textBrush = isActive ? Brushes.Black : Brushes.White;
                g.DrawString(text, btnFont, textBrush, textX, textY);
            }
        }

        private Color GetStatusColor(ServiceStatus status)
        {
            switch (status)
            {
                case ServiceStatus.Running: return RunningColor;
                case ServiceStatus.Stopped: return StoppedColor;
                case ServiceStatus.Error: return Color.FromArgb(200, 80, 80);
                default: return UnknownColor;
            }
        }

        private string GetStatusText(ServiceStatus status)
        {
            switch (status)
            {
                case ServiceStatus.Running: return "Online";
                case ServiceStatus.Stopped: return "Offline";
                case ServiceStatus.Error: return "Error";
                default: return "Unknown";
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

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Double)
            {
                // Double-click anywhere = force refresh
                ForceRefresh();
                return;
            }

            if (click_type == ClickType.Single)
            {
                // Title refresh button
                if (_refreshButton.Contains(x, y))
                {
                    ForceRefresh();
                    return;
                }

                // Bottom action buttons
                if (_startAllButton.Contains(x, y))
                {
                    ExecuteCommand("start");
                    return;
                }
                if (_stopAllButton.Contains(x, y))
                {
                    ExecuteCommand("stop");
                    return;
                }
                if (_restartAllButton.Contains(x, y))
                {
                    ExecuteCommand("restart");
                    return;
                }

                // Per-service action buttons
                for (int i = 0; i < _serviceActionButtons.Length; i++)
                {
                    if (_serviceActionButtons[i].Contains(x, y))
                    {
                        ExecuteCommand("restart-" + _services[i].Name);
                        return;
                    }
                }
            }
        }

        private void ForceRefresh()
        {
            new Thread(() =>
            {
                FetchInfo();
                if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
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
            }) { IsBackground = true }.Start();
        }

        public void RequestUpdate()
        {
            FetchInfo();
            if (_runPoll && _drawingMutex.WaitOne(MutexTimeout))
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

        public System.Windows.Controls.UserControl GetSettingsControl()
        {
            return null;
        }

        public void Dispose()
        {
            _runPoll = false;
            if (_pollThread != null && _pollThread.IsAlive)
            {
                _pollThread.Join(100);
            }
            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void EnterSleep() { _runPoll = false; }
        public void ExitSleep() { StartPolling(); }
        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
    }
}
