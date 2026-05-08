using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMLauncherWidget
{
    public partial class LLMLauncherWidgetInstance : IWidgetInstance
    {
        private Thread _updateThread;
        private volatile bool _isRunning = false;
        private volatile bool _isPaused = false;
        private Mutex _drawingMutex = new Mutex();
        private const int MUTEX_TIMEOUT = 500; // Increased from 100ms

        public Bitmap BitmapCurrent;
        private List<LauncherButton> _buttons;
        private Dictionary<int, int> _buttonFlashState;
        private Dictionary<int, int> _buttonFlashCounter;
        private const int FLASH_DURATION = 30;

        // Config and layout
        private WidgetConfig _config;
        private LayoutConfig _layout;
        private string _resourcePath;
        private int _canvasWidth;
        private int _canvasHeight;

        // Icon cache
        private Dictionary<string, Bitmap> _iconCache = new Dictionary<string, Bitmap>();

        // Router status
        // The router-status / kill-button-state heartbeat hits these. They're sensible
        // llama.cpp router defaults; a future revision could pull them from buttons.json
        // root config so users running on non-default ports don't have to recompile.
        private const string ROUTER_API_HOST = "127.0.0.1";
        private const int ROUTER_API_PORT = 8081;
        // Ports the kill button polls to decide if any LLM server is still alive.
        // Default covers llama.cpp router (8081) plus two common standalone server slots.
        private static readonly int[] LLM_SERVER_PORTS = { 8081, 8083, 8085 };

        private string _activeModelName = "";
        private int _routerStatusCheckCounter = 149; // Start near threshold for immediate first check
        private const int ROUTER_STATUS_CHECK_INTERVAL = 150; // Check every ~5 seconds

        // Radio group tracking - stores active button ID per radio group
        private Dictionary<int, string> _activeRadioGroupButton = new Dictionary<int, string>();

        // Tokens/sec readout for the currently loaded model (updated via /metrics).
        // Not marked volatile — C# disallows `volatile double`. CLR aligns 8-byte
        // fields so reads are effectively atomic on x64; the writer updates every
        // ~5s while DrawButton reads at ~30 FPS, so any torn-read window is invisible.
        private double _activeTokensPerSec = 0;

        public LLMLauncherWidgetInstance(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            _resourcePath = resourcePath;
            Initialize(parent, widget_size, instance_guid, resourcePath);
            LoadConfig();
            LoadSettings();
            InitializeButtonsFromConfig();
            DrawFrame();
            StartUpdateThread();
        }

        private void Initialize(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this.WidgetSize = widget_size;
            Size size = widget_size.ToSize();
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;
            BitmapCurrent = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format16bppRgb565);
            _buttons = new List<LauncherButton>();
            _buttonFlashState = new Dictionary<int, int>();
            _buttonFlashCounter = new Dictionary<int, int>();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(_resourcePath, "buttons.json");
            LogDetectionInfo("Looking for config at: " + configPath);

            _config = WidgetConfig.LoadFromFile(configPath);

            if (_config == null || _config.Buttons.Count == 0)
            {
                LogDetectionInfo("No config found or empty, using defaults");
                _config = WidgetConfig.CreateDefault();

                // Save default config for user to edit
                try
                {
                    _config.SaveToFile(configPath);
                    LogDetectionInfo("Saved default config to: " + configPath);
                }
                catch (Exception ex)
                {
                    LogDetectionInfo("Could not save default config: " + ex.Message);
                }
            }
            else
            {
                LogDetectionInfo("Loaded config with " + _config.Buttons.Count + " buttons");
            }

            _layout = _config.Layout;
        }

        private void InitializeButtonsFromConfig()
        {
            _buttons.Clear();
            _buttonFlashState.Clear();
            _buttonFlashCounter.Clear();

            int buttonCount = _config.Buttons.Count;

            for (int i = 0; i < buttonCount; i++)
            {
                ButtonConfig cfg = _config.Buttons[i];
                LauncherButton btn = new LauncherButton();

                btn.DisplayName = cfg.DisplayName;
                btn.ScriptPath = cfg.ScriptPath;
                btn.IconActivePath = cfg.Icons.Active;
                btn.IconInactivePath = cfg.Icons.Inactive;
                btn.AssociatedPort = cfg.Port;
                btn.ExpectedModelName = cfg.ExpectedModel;
                btn.ExpectedServerType = ParseServerType(cfg.ServerType);
                btn.AlternateScriptPath = cfg.AlternateScriptPath;
                btn.AlternateDisplaySuffix = cfg.AlternateDisplaySuffix;
                btn.IsAlternateMode = false;
                btn.StopScriptPath = cfg.StopScriptPath;
                btn.RadioGroup = cfg.RadioGroup;
                btn.PollHost = cfg.PollHost;
                LogDetectionInfo("Button " + cfg.Id + " RadioGroup = " + cfg.RadioGroup);
                btn.Bounds = CalculateButtonBounds(i, buttonCount);

                _buttons.Add(btn);
                _buttonFlashState[i] = 0;
                _buttonFlashCounter[i] = 0;

                LogDetectionInfo("Button " + i + ": " + btn.DisplayName + " at " + btn.Bounds);
            }
        }

        private Rectangle CalculateButtonBounds(int index, int totalButtons)
        {
            int maxCols = _layout.MaxColumns;
            int spacing = _layout.ButtonSpacing;
            int headerHeight = 20; // Reserve space for status header

            // Calculate grid dimensions
            int columns = Math.Min(maxCols, totalButtons);
            int rows = (int)Math.Ceiling((double)totalButtons / columns);

            // Calculate button size (subtract header from available height)
            int availableHeight = _canvasHeight - headerHeight;
            int buttonWidth = (_canvasWidth - (spacing * (columns + 1))) / columns;
            int buttonHeight = (availableHeight - (spacing * (rows + 1))) / rows;

            // Calculate position
            int col = index % columns;
            int row = index / columns;

            return new Rectangle(
                spacing + col * (buttonWidth + spacing),
                headerHeight + spacing + row * (buttonHeight + spacing),
                buttonWidth,
                buttonHeight
            );
        }

        private ServerType ParseServerType(string type)
        {
            switch (type.ToLower())
            {
                case "llamacpp": return ServerType.LlamaCpp;
                case "vllm": return ServerType.VLLM;
                case "ollama": return ServerType.Ollama;
                case "lmstudio": return ServerType.LMStudio;
                case "kill": return ServerType.Unknown; // Special case
                default: return ServerType.Generic;
            }
        }

        private Bitmap LoadIcon(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath))
                return null;

            if (_iconCache.ContainsKey(iconPath))
                return _iconCache[iconPath];

            string fullPath = Path.Combine(_resourcePath, iconPath);
            if (!File.Exists(fullPath))
            {
                LogDetectionInfo("Icon not found: " + fullPath);
                return null;
            }

            try
            {
                Bitmap bitmap = new Bitmap(fullPath);
                _iconCache[iconPath] = bitmap;
                LogDetectionInfo("Loaded icon: " + iconPath);
                return bitmap;
            }
            catch (Exception ex)
            {
                LogDetectionInfo("Failed to load icon " + iconPath + ": " + ex.Message);
                return null;
            }
        }

        private void LoadSettings()
        {
            if (WidgetObject != null && WidgetObject.WidgetManager != null)
            {
                // Load any saved settings
                foreach (var btn in _config.Buttons)
                {
                    string lastState;
                    if (WidgetObject.WidgetManager.LoadSetting(this, "LastLaunched_" + btn.Id, out lastState))
                    {
                        LogDetectionInfo("Loaded last " + btn.Id + " state: " + lastState);
                    }
                }

                // Load radio group active buttons
                for (int group = 1; group <= 10; group++) // Support up to 10 radio groups
                {
                    string activeBtn;
                    if (WidgetObject.WidgetManager.LoadSetting(this, "RadioGroup_" + group, out activeBtn))
                    {
                        if (!string.IsNullOrEmpty(activeBtn))
                        {
                            _activeRadioGroupButton[group] = activeBtn;
                            LogDetectionInfo("Loaded radio group " + group + " active: " + activeBtn);
                        }
                    }
                }
            }
        }

        public void SaveSettings()
        {
            if (WidgetObject != null && WidgetObject.WidgetManager != null)
            {
                for (int i = 0; i < _buttons.Count && i < _config.Buttons.Count; i++)
                {
                    string btnId = _config.Buttons[i].Id;
                    string state = _buttons[i].State == LauncherState.Running ? _buttons[i].ExpectedModelName : "";
                    WidgetObject.WidgetManager.StoreSetting(this, "LastLaunched_" + btnId, state);
                }

                // Save radio group active buttons
                foreach (var kvp in _activeRadioGroupButton)
                {
                    WidgetObject.WidgetManager.StoreSetting(this, "RadioGroup_" + kvp.Key, kvp.Value);
                }

                LogDetectionInfo("Settings saved.");
            }
        }

        private void StartUpdateThread()
        {
            _isPaused = false;
            _isRunning = true;
            _updateThread = new Thread(UpdateThreadProc);
            _updateThread.IsBackground = true;
            _updateThread.Name = "LLMLauncherWidget-UpdateThread";
            _updateThread.Start();
            LogDetectionInfo("Update thread started");
        }

        private int _heartbeatCounter = 0;

        private void UpdateThreadProc()
        {
            while (_isRunning)
            {
                try
                {
                    if (!_isPaused)
                    {
                        UpdateFlashStates();
                        DrawFrame();
                    }

                    // Heartbeat every ~10 seconds
                    _heartbeatCounter++;
                    if (_heartbeatCounter >= 300)
                    {
                        _heartbeatCounter = 0;
                        LogDetectionInfo("Thread heartbeat - alive");
                    }
                }
                catch (Exception ex)
                {
                    LogDetectionInfo("UPDATE ERROR: " + ex.Message + " | Stack: " + ex.StackTrace);
                }
                Thread.Sleep(33); // ~30 FPS
            }
            LogDetectionInfo("UPDATE THREAD EXITED - _isRunning=" + _isRunning);
        }

        private static string _logFile = null;
        private static readonly object _logLock = new object();
        
        private void LogDetectionInfo(string message)
        {
            System.Diagnostics.Debug.WriteLine("[LLMLauncher] " + message);

            // Write to file for debugging - try multiple locations
            try
            {
                if (_logFile == null)
                {
                    // Try multiple locations - Desktop is always writable
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string[] paths = new string[] {
                        Path.Combine(desktopPath, "LLMLauncher_debug.log"),
                        Path.Combine(_resourcePath, "widget_debug.log"),
                        Path.Combine(Path.GetTempPath(), "LLMLauncher_debug.log")
                    };

                    foreach (string path in paths)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);

                            File.AppendAllText(path, "=== LOG INIT " + DateTime.Now + " ===\r\n");
                            File.AppendAllText(path, "Resource path: " + _resourcePath + "\r\n");
                            _logFile = path;
                            break;
                        }
                        catch { continue; }
                    }
                }

                if (_logFile != null)
                {
                    lock (_logLock)
                    {
                        string logLine = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\r\n";
                        File.AppendAllText(_logFile, logLine);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LLMLauncher] LOG ERROR: " + ex.Message);
            }
        }

        private bool _killButtonLogged = false;
        private int _portCheckLogCounter = 0;

        private void UpdateFlashStates()
        {
            // Check router status periodically
            _routerStatusCheckCounter++;
            if (_routerStatusCheckCounter >= ROUTER_STATUS_CHECK_INTERVAL)
            {
                _routerStatusCheckCounter = 0;
                UpdateRouterStatus();
            }

            // Log port status every ~5 seconds (150 frames at 33ms each)
            _portCheckLogCounter++;
            if (_portCheckLogCounter >= 150)
            {
                _portCheckLogCounter = 0;
                // Find kill button and log its state
                int killFlash = -1;
                LauncherState killState = LauncherState.Ready;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    if (_buttons[i].AssociatedPort <= 0)
                    {
                        killFlash = _buttonFlashState[i];
                        killState = _buttons[i].State;
                        break;
                    }
                }
                LogDetectionInfo("Port check | Kill: flash=" + killFlash + ", state=" + killState + " | Active model: " + _activeModelName);
            }

            bool anyServerRunning = false;

            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttonFlashState[i] > 0)
                {
                    _buttonFlashCounter[i]++;
                    if (_buttonFlashCounter[i] >= FLASH_DURATION)
                    {
                        _buttonFlashState[i] = 0;
                        _buttonFlashCounter[i] = 0;
                    }
                }
                else
                {
                    // Skip kill button in first pass
                    if (_buttons[i].AssociatedPort <= 0)
                        continue;

                    // Simple approach: each button checks its own port
                    // If server is running on button's port with expected model, light up
                    bool isRunning = IsExpectedServerRunning(_buttons[i]);
                    _buttons[i].State = isRunning ? LauncherState.Running : LauncherState.Ready;
                    if (isRunning)
                        anyServerRunning = true;
                }
            }

            // Also check LLM ports directly (in case model name doesn't match)
            // Note: Check all known LLM server ports
            if (!anyServerRunning)
            {
                foreach (int port in LLM_SERVER_PORTS)
                {
                    bool portActive = IsPortInUse(port);
                    if (portActive)
                    {
                        anyServerRunning = true;
                        break;
                    }
                }
            }

            // Kill button: lit (green) when all stopped = safe/clear, gray when servers running
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].AssociatedPort <= 0)
                {
                    // Log once on first state change for kill button
                    if (!_killButtonLogged)
                    {
                        _killButtonLogged = true;
                    }

                    // Only update state if not in flash animation
                    if (_buttonFlashState[i] == 0)
                    {
                        LauncherState newState = anyServerRunning ? LauncherState.Ready : LauncherState.Running;
                        if (_buttons[i].State != newState)
                        {
                            LogDetectionInfo("KILL STATE CHANGE: " + _buttons[i].State + " -> " + newState + " (anyServerRunning=" + anyServerRunning + ")");
                        }
                        _buttons[i].State = newState;
                    }
                }
            }
        }

        private ServerInfo GetServerInfo(int port, string host = "127.0.0.1")
        {
            ServerInfo info = new ServerInfo();
            info.Port = port;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    "http://" + host + ":" + port + "/models"
                );
                request.Method = "GET";
                request.Timeout = 3000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string json = reader.ReadToEnd();

                            // Validate this is actually an LLM API response
                            if (IsValidLLMResponse(json))
                            {
                                info.ModelName = ParseModelNameFromJson(json);
                                info.Type = ServerType.LlamaCpp;
                                info.Status = ServerStatus.Online;
                            }
                            else
                            {
                                info.Status = ServerStatus.Offline;
                            }
                        }
                    }
                }
            }
            catch
            {
                info.Status = ServerStatus.Offline;
            }

            return info;
        }

        private bool IsValidLLMResponse(string json)
        {
            return json.Contains("\"data\"") && json.Contains("\"id\"") &&
                   (json.Contains("\"object\"") || json.Contains("model"));
        }

        private string ParseModelNameFromJson(string json)
        {
            // Try 1: Router format - look for status "loaded" (existing logic - KEEP FIRST)
            // This handles llama-swap router responses where models have status fields
            Match m = Regex.Match(json, @"""id""\s*:\s*""([^""]+)""[^}]*""status""\s*:\s*\{\s*""value""\s*:\s*""loaded""");
            if (m.Success)
                return m.Groups[1].Value.ToLower();

            // Try 2: Standalone llama-server format (orchestrator GPU 8083 / CPU 8085)
            // These servers respond with "data" array but NO status field
            // If server is responding at all with model data, it's loaded and running
            Match standaloneMatch = Regex.Match(json, @"""data""\s*:\s*\[\s*\{\s*""id""\s*:\s*""([^""]+)""");
            if (standaloneMatch.Success)
                return standaloneMatch.Groups[1].Value.ToLower();

            return "";
        }

        private bool IsExpectedServerRunning(LauncherButton btn)
        {
            if (btn.AssociatedPort <= 0)
                return false;

            ServerInfo info = GetServerInfo(btn.AssociatedPort, btn.PollHost);

            if (info.Status != ServerStatus.Online)
                return false;

            // Radio group check - if button is in a radio group, check if it's the active one
            if (btn.RadioGroup > 0)
            {
                // Get the button ID for this button
                string btnId = "";
                for (int i = 0; i < _buttons.Count && i < _config.Buttons.Count; i++)
                {
                    if (_buttons[i] == btn)
                    {
                        btnId = _config.Buttons[i].Id;
                        break;
                    }
                }

                // Check if this button is the active one in its radio group
                if (_activeRadioGroupButton.ContainsKey(btn.RadioGroup))
                {
                    return _activeRadioGroupButton[btn.RadioGroup] == btnId;
                }
                else
                {
                    // No active button in this group yet - none should light up
                    return false;
                }
            }

            string modelLower = info.ModelName.ToLower();
            string expected = btn.ExpectedModelName.ToLower();

            // Support pipe-separated model patterns (e.g., "deepseek|coder")
            if (expected.Contains("|"))
            {
                string[] patterns = expected.Split('|');
                foreach (string pattern in patterns)
                {
                    if (modelLower.Contains(pattern.Trim()))
                        return true;
                }
                return false;
            }

            // Empty expected means "any model is OK" - just check if port is online
            if (string.IsNullOrEmpty(expected))
                return true;

            return modelLower.Contains(expected);
        }

        // Track consecutive port check failures for debounce
        private Dictionary<int, int> _portFailureCount = new Dictionary<int, int>();
        private const int PORT_DEBOUNCE_COUNT = 2; // Require 2 consecutive failures

        private bool IsPortInUse(int port, string host = "127.0.0.1")
        {
            bool isConnected = false;
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    // Use async connect with very short timeout for responsive UI
                    IAsyncResult result = client.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(100); // 100ms timeout

                    if (success)
                    {
                        try
                        {
                            client.EndConnect(result);
                            if (client.Connected)
                            {
                                isConnected = true;
                            }
                        }
                        catch
                        {
                            // EndConnect can throw if connection failed
                        }
                    }
                }
            }
            catch
            {
                // Connection failed
            }

            // Debounce logic: require consecutive failures before reporting port closed
            if (isConnected)
            {
                _portFailureCount[port] = 0;
                return true;
            }
            else
            {
                if (!_portFailureCount.ContainsKey(port))
                    _portFailureCount[port] = 0;

                _portFailureCount[port]++;

                // Only report closed after consecutive failures (prevents false negatives during TCP teardown)
                if (_portFailureCount[port] >= PORT_DEBOUNCE_COUNT)
                {
                    return false;
                }
                else
                {
                    // Still in debounce period, report as still open
                    return true;
                }
            }
        }

        private void DrawFrame()
        {
            if (_drawingMutex.WaitOne(MUTEX_TIMEOUT))
            {
                try
                {
                    using (Graphics g = Graphics.FromImage(BitmapCurrent))
                    {
                        g.Clear(Color.FromArgb(32, 32, 32));
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        DrawStatusHeader(g);
                        DrawButtons(g);
                    }
                    UpdateWidget();
                }
                finally { _drawingMutex.ReleaseMutex(); }
            }
        }

        private void DrawStatusHeader(Graphics g)
        {
            // Draw thin header bar at top showing active model
            if (!string.IsNullOrEmpty(_activeModelName))
            {
                Rectangle headerBar = new Rectangle(0, 0, _canvasWidth, 20);
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                {
                    g.FillRectangle(bgBrush, headerBar);
                }

                using (Font font = new Font("Arial", 8, FontStyle.Regular))
                using (Brush textBrush = new SolidBrush(Color.FromArgb(100, 200, 100)))
                {
                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    g.DrawString("Active: " + _activeModelName, font, textBrush, headerBar, format);
                }
            }
        }

        private void DrawButtons(Graphics g)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                DrawButton(g, i);
            }
        }

        private void DrawButton(Graphics g, int index)
        {
            LauncherButton btn = _buttons[index];
            Rectangle bounds = btn.Bounds;

            // Determine state colors
            Color borderColor = Color.Gray;
            int borderWidth = 2;

            if (_buttonFlashState[index] == 1) // Launching
            {
                borderColor = Color.FromArgb(100, 100, 255);
                borderWidth = 3;
            }
            else if (_buttonFlashState[index] == 2) // Success
            {
                borderColor = Color.Lime;
                borderWidth = 3;
            }
            else if (_buttonFlashState[index] == 3) // Error
            {
                borderColor = Color.Red;
                borderWidth = 3;
            }
            else if (btn.State == LauncherState.Running)
            {
                borderColor = Color.Green;
                borderWidth = 3;
            }

            // Get appropriate icon based on state
            string iconPath = btn.State == LauncherState.Running
                ? btn.IconActivePath
                : btn.IconInactivePath;

            Bitmap icon = LoadIcon(iconPath);

            if (icon != null)
            {
                // Draw icon scaled to fill button
                g.DrawImage(icon, bounds);
            }
            else
            {
                // Fallback: solid color background
                Color bgColor = Color.FromArgb(64, 64, 64);

                if (_buttonFlashState[index] == 1)
                {
                    int pulse = (int)(Math.Sin(_buttonFlashCounter[index] * 0.3) * 50 + 100);
                    bgColor = Color.FromArgb(pulse / 2, pulse / 2, pulse);
                }
                else if (_buttonFlashState[index] == 2)
                {
                    int flash = (_buttonFlashCounter[index] % 10) < 5 ? 150 : 50;
                    bgColor = Color.FromArgb(0, flash, 0);
                }
                else if (_buttonFlashState[index] == 3)
                {
                    int flash = (_buttonFlashCounter[index] % 10) < 5 ? 150 : 50;
                    bgColor = Color.FromArgb(flash, 0, 0);
                }
                else if (btn.State == LauncherState.Running)
                {
                    bgColor = Color.FromArgb(0, 80, 0);
                }

                using (Brush bgBrush = new SolidBrush(bgColor))
                {
                    g.FillRectangle(bgBrush, bounds);
                }
            }

            // Draw semi-transparent overlay bar at bottom for text
            int textBarHeight = Math.Max(30, bounds.Height / 4);
            Rectangle textBar = new Rectangle(
                bounds.X,
                bounds.Bottom - textBarHeight,
                bounds.Width,
                textBarHeight
            );

            using (Brush overlay = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                g.FillRectangle(overlay, textBar);
            }

            // Draw button name with mode indicator if applicable
            string displayText = btn.DisplayName;
            if (!string.IsNullOrEmpty(btn.AlternateDisplaySuffix) && btn.IsAlternateMode)
            {
                displayText += " (" + btn.AlternateDisplaySuffix + ")";
            }

            using (Font font = new Font("Arial", 11, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(Color.White))
            {
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(displayText, font, textBrush, textBar, format);
            }

            // Draw state text at bottom of text bar
            string stateText = btn.State == LauncherState.Running ? "RUNNING" : "READY";
            if (_buttonFlashState[index] == 1) stateText = "LAUNCHING";
            else if (_buttonFlashState[index] == 3) stateText = "ERROR";

            // When this button is the active model AND we have a live throughput
            // reading, replace the "RUNNING" label entirely with the readout \u2014
            // the green border + active icon already convey "running", so
            // doubling that wastes the limited bottom-bar real estate.
            // Use bright cyan for high contrast against icon imagery + the
            // 70%-opacity black overlay above.
            Color stateColor = borderColor;
            bool showingTokens = false;
            if (btn.State == LauncherState.Running && _activeTokensPerSec > 0
                && _buttonFlashState[index] == 0
                && MatchesActiveModel(btn.ExpectedModelName, _activeModelName))
            {
                string tokStr;
                if (_activeTokensPerSec >= 10)
                {
                    tokStr = string.Format("{0:F0}", _activeTokensPerSec);
                }
                else
                {
                    tokStr = string.Format("{0:F1}", _activeTokensPerSec);
                }
                stateText = tokStr + " tok/s";
                stateColor = Color.FromArgb(0, 230, 255); // bright cyan
                showingTokens = true;
            }

            float stateFontSize = showingTokens ? 8f : 7f;
            FontStyle stateFontStyle = showingTokens ? FontStyle.Bold : FontStyle.Regular;
            using (Font smallFont = new Font("Arial", stateFontSize, stateFontStyle))
            using (Brush stateBrush = new SolidBrush(stateColor))
            {
                g.DrawString(stateText, smallFont, stateBrush, bounds.X + 5, bounds.Bottom - 15);
            }

            // Draw border (colored based on state)
            using (Pen borderPen = new Pen(borderColor, borderWidth))
            {
                g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }
        }

        private void UpdateWidget()
        {
            if (BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;
                RaiseWidgetUpdated(e);
            }
        }

        /// <summary>
        /// Forces the kill button to update to "Running" (green) state after kill script completes.
        /// Bypasses normal flash state checks to ensure immediate visual feedback.
        /// </summary>
        private void ForceKillButtonUpdate(int buttonIndex)
        {
            try
            {
                if (buttonIndex < 0 || buttonIndex >= _buttons.Count)
                    return;

                // Use raw port check that bypasses debounce logic
                bool anyServerRunning = false;
                System.Text.StringBuilder portStates = new System.Text.StringBuilder();
                foreach (int port in LLM_SERVER_PORTS)
                {
                    bool active = IsPortInUseRaw(port);
                    portStates.Append(port).Append("=").Append(active).Append(", ");
                    if (active) anyServerRunning = true;
                }

                LogDetectionInfo("ForceKillButtonUpdate: " + portStates.ToString() + "anyRunning=" + anyServerRunning);

                if (!anyServerRunning)
                {
                    // All servers stopped after kill — light up kill button (all clear)
                    _buttons[buttonIndex].State = LauncherState.Running;
                    _buttonFlashState[buttonIndex] = 0; // Clear any flash state
                    LogDetectionInfo("ForceKillButtonUpdate: Set kill button to Running (green)");
                }
                else
                {
                    LogDetectionInfo("ForceKillButtonUpdate: Servers still running, keeping Ready state");
                }
            }
            catch (Exception ex)
            {
                LogDetectionInfo("ForceKillButtonUpdate ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Checks if a specific model is loaded on a port by querying the API.
        /// Used for switch commands where port is already in use but we need to verify model loaded.
        /// </summary>
        private bool IsModelLoadedOnPort(int port, string expectedModel, string host = "127.0.0.1")
        {
            if (string.IsNullOrEmpty(expectedModel))
                return IsPortInUseRaw(port, host);

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    "http://" + host + ":" + port + "/models"
                );
                request.Method = "GET";
                request.Timeout = 2000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string json = reader.ReadToEnd();
                            string loadedModel = ParseModelNameFromJson(json);
                            
                            if (string.IsNullOrEmpty(loadedModel))
                                return false; // No model loaded yet
                            
                            // Check if loaded model matches expected
                            return loadedModel.ToLower().Contains(expectedModel.ToLower());
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Raw port check without debounce logic. Used after kill to get immediate accurate status.
        /// </summary>
        private bool IsPortInUseRaw(int port, string host = "127.0.0.1")
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(100);

                    if (success)
                    {
                        try
                        {
                            client.EndConnect(result);
                            return client.Connected;
                        }
                        catch { }
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public void RequestUpdate()
        {
            if (_drawingMutex.WaitOne(MUTEX_TIMEOUT))
            {
                try { DrawFrame(); }
                finally { _drawingMutex.ReleaseMutex(); }
            }
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single)
            {
                for (int i = 0; i < _buttons.Count; i++)
                {
                    if (_buttons[i].Bounds.Contains(x, y))
                    {
                        HandleButtonClick(i);
                        break;
                    }
                }
            }
        }

        private void HandleButtonClick(int buttonIndex)
        {
            LauncherButton btn = _buttons[buttonIndex];
            ButtonConfig cfg = _config.Buttons[buttonIndex];

            _buttonFlashState[buttonIndex] = 1; // Start pulse
            _buttonFlashCounter[buttonIndex] = 0;

            // Execute in background thread
            Thread execThread = new Thread(() =>
            {
                try
                {
                    // Check if this is a kill button or script
                    bool isKillButton = cfg.ServerType.ToLower() == "kill" ||
                                       btn.ScriptPath.ToLower().Contains("kill");

                    if (isKillButton)
                    {
                        // Execute kill command via WSL
                        LogDetectionInfo("Kill command: " + btn.ScriptPath);

                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = "cmd.exe";
                        psi.Arguments = "/c " + btn.ScriptPath;
                        psi.WorkingDirectory = _resourcePath;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.WindowStyle = ProcessWindowStyle.Hidden;

                        Process p = Process.Start(psi);
                        p.WaitForExit(5000);  // 5 second timeout
                        LogDetectionInfo("Kill command completed");

                        // Wait for ports to fully close and reset debounce counters
                        Thread.Sleep(500);
                        foreach (int port in LLM_SERVER_PORTS)
                        {
                            _portFailureCount[port] = PORT_DEBOUNCE_COUNT;
                        }
                        LogDetectionInfo("Post-kill: Reset port debounce counters for " + string.Join(", ", LLM_SERVER_PORTS));

                        _buttonFlashState[buttonIndex] = 2; // Success
                        _buttonFlashCounter[buttonIndex] = 0;

                        // Force immediate state update after flash completes
                        Thread postKillThread = new Thread(() =>
                        {
                            Thread.Sleep(FLASH_DURATION * 35 + 100); // Wait for flash to complete
                            ForceKillButtonUpdate(buttonIndex);
                        });
                        postKillThread.IsBackground = true;
                        postKillThread.Start();
                    }
                    else
                    {
                        // Check if this is a model SWITCH command (router-control.sh switch)
                        // Switch commands should run even if port is in use - they change the model on running router
                        // Radio group buttons are also treated as switches - they load different configs on running router
                        bool isModelSwitch = btn.ScriptPath.Contains(" switch ") || btn.RadioGroup > 0;

                        // Check if this button has a stop script for toggle behavior
                        bool hasStopScript = !string.IsNullOrEmpty(btn.StopScriptPath);
                        bool portInUse = btn.AssociatedPort > 0 && IsPortInUseRaw(btn.AssociatedPort);

                        // Handle toggle behavior: if port is in use and we have a stop script, run it
                        if (!isModelSwitch && portInUse && hasStopScript)
                        {
                            // Port is in use and we have a stop script - execute STOP
                            LogDetectionInfo("Toggle STOP command: " + btn.StopScriptPath);

                            ProcessStartInfo stopPsi = new ProcessStartInfo();
                            stopPsi.FileName = "cmd.exe";
                            stopPsi.Arguments = "/c " + btn.StopScriptPath;
                            stopPsi.WorkingDirectory = _resourcePath;
                            stopPsi.UseShellExecute = false;
                            stopPsi.CreateNoWindow = true;
                            Process stopProcess = Process.Start(stopPsi);
                            stopProcess.WaitForExit(10000); // 10 second timeout for stop

                            // Wait for port to close
                            int stopWaitCount = 0;
                            while (stopWaitCount < 50 && IsPortInUseRaw(btn.AssociatedPort))
                            {
                                Thread.Sleep(100);
                                stopWaitCount++;
                            }

                            if (!IsPortInUseRaw(btn.AssociatedPort))
                            {
                                _buttonFlashState[buttonIndex] = 2; // Success
                                LogDetectionInfo("Toggle STOP successful - port closed");
                            }
                            else
                            {
                                _buttonFlashState[buttonIndex] = 3; // Error - port still in use
                                LogDetectionInfo("Toggle STOP failed - port still in use");
                            }
                            _buttonFlashCounter[buttonIndex] = 0;
                            return;
                        }

                        // Only check port-in-use for START commands without stop scripts
                        // Use IsPortInUseRaw to avoid debounce false positives on fresh start
                        if (!isModelSwitch && portInUse && !hasStopScript)
                        {
                            _buttonFlashState[buttonIndex] = 3; // Error - port in use
                            _buttonFlashCounter[buttonIndex] = 0;
                            return;
                        }

                        // Determine which script to use (primary or alternate)
                        string scriptToUse = btn.ScriptPath;

                        // Check for orchestrator mode toggle
                        if (!string.IsNullOrEmpty(btn.AlternateScriptPath))
                        {
                            // Check current orchestrator mode
                            string currentMode = GetOrchestratorMode();

                            // Toggle mode: if GPU is running, switch to CPU (alternate), and vice versa
                            if (btn.State == LauncherState.Running)
                            {
                                // Switch to alternate mode
                                scriptToUse = btn.IsAlternateMode ? btn.ScriptPath : btn.AlternateScriptPath;
                                btn.IsAlternateMode = !btn.IsAlternateMode;
                            }
                            else
                            {
                                // Not running, check what mode file says or default to GPU
                                if (currentMode == "cpu")
                                {
                                    scriptToUse = btn.AlternateScriptPath;
                                    btn.IsAlternateMode = true;
                                }
                                else
                                {
                                    scriptToUse = btn.ScriptPath;
                                    btn.IsAlternateMode = false;
                                }
                            }
                        }

                        ProcessStartInfo psi = new ProcessStartInfo();
                        LogDetectionInfo("Launch command: " + scriptToUse);

                        // Execute WSL command
                        psi.FileName = "cmd.exe";
                        psi.Arguments = "/c " + scriptToUse;
                        psi.WorkingDirectory = _resourcePath;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        Process p = Process.Start(psi);

                        // Wait for server to start (max 180 seconds - slow D: drive can take 120s+)
                        if (btn.AssociatedPort > 0)
                        {
                            int waitCount = 0;
                            bool success = false;

                            if (btn.RadioGroup > 0)
                            {
                                // Radio group buttons: just wait for script to complete and port to be ready
                                // Don't check for specific model name - radio group tracks which button was clicked
                                LogDetectionInfo("Radio group button - waiting for script completion...");
                                Thread.Sleep(2000); // Give script time to start
                                success = IsPortInUseRaw(btn.AssociatedPort);
                                LogDetectionInfo("Radio group result: " + (success ? "SUCCESS" : "PORT NOT READY"));
                            }
                            else if (isModelSwitch)
                            {
                                // For switch commands, wait for SPECIFIC MODEL to be loaded
                                LogDetectionInfo("Waiting for model '" + btn.ExpectedModelName + "' to load...");
                                while (waitCount < 1800 && !IsModelLoadedOnPort(btn.AssociatedPort, btn.ExpectedModelName, btn.PollHost))
                                {
                                    Thread.Sleep(100);
                                    waitCount++;
                                    if (waitCount % 50 == 0) // Log every 5 seconds
                                        LogDetectionInfo("Still waiting for model... (" + (waitCount / 10) + "s)");
                                }
                                success = IsModelLoadedOnPort(btn.AssociatedPort, btn.ExpectedModelName, btn.PollHost);
                                LogDetectionInfo("Model load result: " + (success ? "SUCCESS" : "TIMEOUT") + " after " + (waitCount / 10) + "s");
                            }
                            else
                            {
                                // For start commands, wait for port to become available
                                while (waitCount < 1800 && !IsPortInUseRaw(btn.AssociatedPort))
                                {
                                    Thread.Sleep(100);
                                    waitCount++;
                                }
                                success = IsPortInUseRaw(btn.AssociatedPort);
                            }

                            if (success)
                            {
                                _buttonFlashState[buttonIndex] = 2; // Success

                                // Update radio group active button on success
                                if (btn.RadioGroup > 0)
                                {
                                    _activeRadioGroupButton[btn.RadioGroup] = cfg.Id;
                                    LogDetectionInfo("Radio group " + btn.RadioGroup + " active button: " + cfg.Id);
                                    SaveSettings(); // Persist radio group state
                                }
                            }
                            else
                            {
                                _buttonFlashState[buttonIndex] = 3; // Error
                            }
                        }
                        else
                        {
                            _buttonFlashState[buttonIndex] = 2; // Success (no port check)

                            // Update radio group active button on success (no port check case)
                            if (btn.RadioGroup > 0)
                            {
                                _activeRadioGroupButton[btn.RadioGroup] = cfg.Id;
                                LogDetectionInfo("Radio group " + btn.RadioGroup + " active button: " + cfg.Id);
                                SaveSettings(); // Persist radio group state
                            }
                        }
                        _buttonFlashCounter[buttonIndex] = 0;
                    }
                }
                catch (Exception ex)
                {
                    LogDetectionInfo("Button click error: " + ex.Message);
                    _buttonFlashState[buttonIndex] = 3; // Error
                    _buttonFlashCounter[buttonIndex] = 0;
                }
            });
            execThread.IsBackground = true;
            execThread.Start();
        }

        /// <summary>
        /// Gets the current orchestrator mode (gpu/cpu) by reading /tmp/orchestrator-mode via WSL.
        /// </summary>
        private string GetOrchestratorMode()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "wsl";
                psi.Arguments = "--exec cat /tmp/orchestrator-mode";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;

                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(1000);

                LogDetectionInfo("Orchestrator mode: " + output);
                return output.ToLower();
            }
            catch (Exception ex)
            {
                LogDetectionInfo("Error reading orchestrator mode: " + ex.Message);
                return "gpu"; // Default to GPU
            }
        }

        /// <summary>
        /// Updates the active model name from the router API (port 8081).
        /// </summary>
        private void UpdateRouterStatus()
        {
            try
            {
                // Use /models endpoint (OpenAI-compatible format)
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    "http://" + ROUTER_API_HOST + ":" + ROUTER_API_PORT + "/models");
                request.Method = "GET";
                request.Timeout = 1000; // 1 second timeout

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string json = reader.ReadToEnd();
                            LogDetectionInfo("Router API response (first 300): " + (json.Length > 300 ? json.Substring(0, 300) : json));

                            // Find loaded model by searching backwards from "loaded" status
                            // This avoids regex issues with braces in the args array
                            string loadedModel = FindLoadedModelInJson(json);
                            if (!string.IsNullOrEmpty(loadedModel))
                            {
                                _activeModelName = loadedModel;
                                SyncRadioGroupFromActiveModel(_activeModelName);
                            }
                            else
                            {
                                _activeModelName = "";
                            }

                            // Fetch tokens/sec from Prometheus metrics endpoint
                            try
                            {
                                string metricsUrl = "http://" + ROUTER_API_HOST + ":" + ROUTER_API_PORT + "/metrics";
                                if (!string.IsNullOrEmpty(_activeModelName))
                                {
                                    metricsUrl = metricsUrl + "?model=" + System.Net.WebUtility.UrlEncode(_activeModelName);
                                }
                                HttpWebRequest metricsRequest = (HttpWebRequest)WebRequest.Create(metricsUrl);
                                metricsRequest.Method = "GET";
                                metricsRequest.Timeout = 1000;

                                using (HttpWebResponse metricsResponse = (HttpWebResponse)metricsRequest.GetResponse())
                                {
                                    if (metricsResponse.StatusCode == HttpStatusCode.OK)
                                    {
                                        using (StreamReader metricsReader = new StreamReader(metricsResponse.GetResponseStream()))
                                        {
                                            string metricsText = metricsReader.ReadToEnd();
                                            double tokensPerSec = ParseTokensPerSecFromMetrics(metricsText);
                                            _activeTokensPerSec = tokensPerSec;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Metrics endpoint unavailable or failed — keep previous value
                                // but cap at 0 if we have no active model
                                if (string.IsNullOrEmpty(_activeModelName))
                                {
                                    _activeTokensPerSec = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Router not available, clear active model
                _activeModelName = "";
                _activeTokensPerSec = 0;
            }
        }

        /// <summary>
        /// Finds the model ID with status "loaded" by searching backwards from the status marker.
        /// Robust against braces in args arrays that break regex-based parsing.
        /// </summary>
        private string FindLoadedModelInJson(string json)
        {
            // Find "value":"loaded" (with flexible whitespace)
            int searchStart = 0;
            while (true)
            {
                int loadedPos = json.IndexOf("\"loaded\"", searchStart, StringComparison.Ordinal);
                if (loadedPos < 0)
                    return null;

                // Verify this is inside a "value" field (not some random string)
                int valueCheck = json.LastIndexOf("\"value\"", loadedPos, StringComparison.Ordinal);
                if (valueCheck < 0 || loadedPos - valueCheck > 30)
                {
                    searchStart = loadedPos + 8;
                    continue;
                }

                // Search backwards from loadedPos to find the nearest "id":"..."
                int idKeyPos = json.LastIndexOf("\"id\"", loadedPos, StringComparison.Ordinal);
                if (idKeyPos < 0)
                {
                    searchStart = loadedPos + 8;
                    continue;
                }

                // Extract the id value: find the quoted string after "id":
                int colonPos = json.IndexOf(':', idKeyPos + 4);
                if (colonPos < 0)
                {
                    searchStart = loadedPos + 8;
                    continue;
                }

                int quoteStart = json.IndexOf('\"', colonPos + 1);
                if (quoteStart < 0)
                {
                    searchStart = loadedPos + 8;
                    continue;
                }

                int quoteEnd = json.IndexOf('\"', quoteStart + 1);
                if (quoteEnd < 0)
                {
                    searchStart = loadedPos + 8;
                    continue;
                }

                string modelId = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                LogDetectionInfo("Found loaded model: " + modelId);
                return modelId;
            }
        }

        /// <summary>
        /// Auto-syncs radio group state from the currently loaded model name.
        /// This ensures radio buttons light up even when models are loaded outside WigiDash.
        /// </summary>
        private void SyncRadioGroupFromActiveModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            string modelLower = modelName.ToLower();

            for (int i = 0; i < _buttons.Count && i < _config.Buttons.Count; i++)
            {
                var btn = _buttons[i];
                if (btn.RadioGroup <= 0)
                    continue;

                string expected = btn.ExpectedModelName.ToLower();
                if (string.IsNullOrEmpty(expected))
                    continue;

                bool matches = false;
                if (expected.Contains("|"))
                {
                    foreach (string pattern in expected.Split('|'))
                    {
                        if (modelLower.Contains(pattern.Trim()))
                        {
                            matches = true;
                            break;
                        }
                    }
                }
                else
                {
                    matches = modelLower.Contains(expected);
                }

                if (matches)
                {
                    string btnId = _config.Buttons[i].Id;
                    if (!_activeRadioGroupButton.ContainsKey(btn.RadioGroup) ||
                        _activeRadioGroupButton[btn.RadioGroup] != btnId)
                    {
                        _activeRadioGroupButton[btn.RadioGroup] = btnId;
                        LogDetectionInfo("Radio group " + btn.RadioGroup + " auto-synced to " + btnId + " (model: " + modelName + ")");
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Checks whether the button's expected model matches the currently active model.
        /// Reuses the same contains/pattern logic as SyncRadioGroupFromActiveModel.
        /// </summary>
        private bool MatchesActiveModel(string expectedModelName, string activeModelName)
        {
            if (string.IsNullOrEmpty(activeModelName))
                return false;
            if (string.IsNullOrEmpty(expectedModelName))
                return false;

            string modelLower = activeModelName.ToLower();
            string expectedLower = expectedModelName.ToLower();

            if (expectedLower.Contains("|"))
            {
                foreach (string pattern in expectedLower.Split('|'))
                {
                    if (modelLower.Contains(pattern.Trim()))
                        return true;
                }
                return false;
            }
            else
            {
                return modelLower.Contains(expectedLower);
            }
        }

        /// <summary>
        /// Parses llamacpp:predicted_tokens_seconds (average generation throughput in tokens/s)
        /// from Prometheus-format metrics text. Returns 0 if not found or malformed.
        /// </summary>
        private double ParseTokensPerSecFromMetrics(string metricsText)
        {
            if (string.IsNullOrEmpty(metricsText))
                return 0;

            // Look for the line: llamacpp:predicted_tokens_seconds <value>
            string metricName = "llamacpp:predicted_tokens_seconds";
            int idx = metricsText.IndexOf(metricName, StringComparison.Ordinal);
            if (idx < 0)
                return 0;

            // Find the value on the same line
            int lineStart = metricsText.LastIndexOf('\n', idx);
            if (lineStart < 0) lineStart = 0;
            else lineStart++;
            int lineEnd = metricsText.IndexOf('\n', idx);
            if (lineEnd < 0) lineEnd = metricsText.Length;

            string line = metricsText.Substring(lineStart, lineEnd - lineStart).Trim();

            // Extract the numeric value (last token on the line)
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return 0;

            double value;
            if (double.TryParse(parts[parts.Length - 1],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out value))
                return value;

            return 0;
        }

        public void Dispose()
        {
            SaveSettings();
            _isPaused = true;
            _isRunning = false;
            if (_updateThread != null && _updateThread.IsAlive)
                _updateThread.Join(5000);

            // Dispose icon cache
            foreach (var kvp in _iconCache)
            {
                if (kvp.Value != null)
                    kvp.Value.Dispose();
            }
            _iconCache.Clear();

            // Dispose mutex
            if (_drawingMutex != null)
                _drawingMutex.Dispose();

            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void EnterSleep() { _isPaused = true; }
        public void ExitSleep() { _isPaused = false; }
    }
}
