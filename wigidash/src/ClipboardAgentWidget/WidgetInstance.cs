using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Diagnostics;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClipboardAgentWidget
{
    public partial class ClipboardAgentWidgetInstance : IWidgetInstance
    {
        private Thread _updateThread;
        private volatile bool _isRunning = false;
        private volatile bool _isPaused = false;
        private Mutex _drawingMutex = new Mutex();
        private const int MUTEX_TIMEOUT = 500;

        public Bitmap BitmapCurrent;
        private List<ActionButton> _buttons;
        private LLMClient _llmClient;
        private int _processingButtonIndex = -1;
        private string _statusMessage = "Ready";
        private int _animationFrame = 0;

        // Clipboard state (polled periodically)
        private string _clipboardText = "";
        private string _clipContentType = "Empty";
        private int _clipTokens = 0;
        private string _clipStats = "empty";
        private DateTime _lastClipPoll = DateTime.MinValue;

        // Cycling state for local action buttons
        private int _transformIndex = 0;
        private int _snippetIndex = 0;
        private int _escapeIndex = 0;

        // Colors
        private static readonly Color BgColor = Color.FromArgb(28, 28, 36);
        private static readonly Color HeaderBg = Color.FromArgb(20, 20, 28);
        private static readonly Color DimText = Color.FromArgb(140, 140, 160);
        private static readonly Color BrightText = Color.White;
        private static readonly Color CyanAccent = Color.FromArgb(0, 200, 220);
        private static readonly Color TypeBadgeBg = Color.FromArgb(50, 50, 70);

        public ClipboardAgentWidgetInstance(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            Initialize(parent, widget_size, instance_guid, resourcePath);
            InitializeButtons();
            _llmClient = new LLMClient();
            _llmClient.DiscoverEndpoints();
            PollClipboard();
            DrawFrame();
            StartUpdateThread();
        }

        private void Initialize(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this.WidgetSize = widget_size;
            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format16bppRgb565);
            _buttons = new List<ActionButton>();
            Log("Widget initialized: " + size.Width + "x" + size.Height);
        }

        private void InitializeButtons()
        {
            Size size = WidgetSize.ToSize();
            bool wide = size.Width >= 600; // 4x2 mode

            if (wide)
            {
                // 4x2 layout: 4 columns, 2 rows
                int cols = 4;
                int margin = 6;
                int headerH = 32;
                int statusH = 22;
                int availW = size.Width - (cols + 1) * margin;
                int availH = size.Height - headerH - statusH - 3 * margin;
                int btnW = availW / cols;
                int btnH = availH / 2;

                int y1 = headerH + margin;
                int y2 = y1 + btnH + margin;

                // Row 1: LLM actions
                _buttons.Add(ActionButton.CreateSummarize(new Rectangle(margin, y1, btnW, btnH)));
                _buttons.Add(ActionButton.CreateExplain(new Rectangle(margin + (btnW + margin), y1, btnW, btnH)));
                _buttons.Add(ActionButton.CreateFixBug(new Rectangle(margin + 2 * (btnW + margin), y1, btnW, btnH)));
                _buttons.Add(ActionButton.CreateRefactor(new Rectangle(margin + 3 * (btnW + margin), y1, btnW, btnH)));

                // Row 2: Local actions
                _buttons.Add(ActionButton.CreateFormat(new Rectangle(margin, y2, btnW, btnH)));
                _buttons.Add(ActionButton.CreateTransform(new Rectangle(margin + (btnW + margin), y2, btnW, btnH)));
                _buttons.Add(ActionButton.CreateSnippet(new Rectangle(margin + 2 * (btnW + margin), y2, btnW, btnH)));
                _buttons.Add(ActionButton.CreateEscape(new Rectangle(margin + 3 * (btnW + margin), y2, btnW, btnH)));
            }
            else
            {
                // 2x2 fallback: original 4 buttons
                int btnWidth = (size.Width - 30) / 2;
                int btnHeight = (size.Height - 50) / 2;
                int startY = 35;
                _buttons.Add(ActionButton.CreateSummarize(new Rectangle(10, startY, btnWidth, btnHeight)));
                _buttons.Add(ActionButton.CreateRefactor(new Rectangle(btnWidth + 20, startY, btnWidth, btnHeight)));
                _buttons.Add(ActionButton.CreateExplain(new Rectangle(10, startY + btnHeight + 10, btnWidth, btnHeight)));
                _buttons.Add(ActionButton.CreateFixBug(new Rectangle(btnWidth + 20, startY + btnHeight + 10, btnWidth, btnHeight)));
            }

            Log("Initialized " + _buttons.Count + " buttons (wide=" + (size.Width >= 600) + ")");
        }

        private void StartUpdateThread()
        {
            _isPaused = false;
            _isRunning = true;
            _updateThread = new Thread(UpdateThreadProc);
            _updateThread.IsBackground = true;
            _updateThread.Name = "ClipboardAgentWidget-UpdateThread";
            _updateThread.Start();
        }

        private void UpdateThreadProc()
        {
            int tickCount = 0;
            while (_isRunning)
            {
                if (!_isPaused)
                {
                    _animationFrame++;

                    // Poll clipboard every 2 seconds
                    if (tickCount % 20 == 0)
                        PollClipboard();

                    DrawFrame();
                    tickCount++;
                }
                Thread.Sleep(100);
            }
        }

        private void PollClipboard()
        {
            try
            {
                string text = ClipboardManager.GetText();
                if (text != _clipboardText)
                {
                    _clipboardText = text ?? "";
                    _clipContentType = ContentDetector.DetectType(_clipboardText);
                    _clipTokens = ContentDetector.EstimateTokens(_clipboardText);
                    _clipStats = ContentDetector.GetStats(_clipboardText);
                }
            }
            catch { }
        }

        // === DRAWING ===

        private void DrawFrame()
        {
            if (_drawingMutex.WaitOne(MUTEX_TIMEOUT))
            {
                try
                {
                    using (Graphics g = Graphics.FromImage(BitmapCurrent))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(BgColor);
                        DrawHeader(g);
                        DrawButtons(g);
                        DrawStatusBar(g);
                    }
                    UpdateWidget();
                }
                finally { _drawingMutex.ReleaseMutex(); }
            }
        }

        private void DrawHeader(Graphics g)
        {
            int w = BitmapCurrent.Width;

            // Header background
            using (var headerBrush = new SolidBrush(HeaderBg))
                g.FillRectangle(headerBrush, 0, 0, w, 30);

            // Title
            using (var titleFont = new Font("Arial", 9, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(CyanAccent))
                g.DrawString("CLIPBOARD AGENT", titleFont, titleBrush, 8, 7);

            // Token counter (center-right)
            string tokenStr = "~" + ContentDetector.FormatTokenCount(_clipTokens) + " tok";
            using (var tokenFont = new Font("Arial", 8, FontStyle.Bold))
            using (var tokenBrush = new SolidBrush(_clipTokens > 4000 ? Color.FromArgb(255, 160, 0) :
                                                   _clipTokens > 0 ? Color.FromArgb(180, 220, 180) : DimText))
            {
                SizeF tokenSize = g.MeasureString(tokenStr, tokenFont);
                float tokenX = w - tokenSize.Width - 8;
                g.DrawString(tokenStr, tokenFont, tokenBrush, tokenX, 8);
            }

            // Content type badge
            if (_clipContentType != "Empty")
            {
                using (var badgeFont = new Font("Arial", 7, FontStyle.Bold))
                {
                    string badge = _clipContentType;
                    SizeF badgeSize = g.MeasureString(badge, badgeFont);
                    float badgeX = w - badgeSize.Width - 80;
                    using (var badgeBg = new SolidBrush(TypeBadgeBg))
                        g.FillRectangle(badgeBg, badgeX - 4, 5, badgeSize.Width + 8, 18);
                    using (var badgeBrush = new SolidBrush(GetTypeColor(_clipContentType)))
                        g.DrawString(badge, badgeFont, badgeBrush, badgeX, 7);
                }
            }

            // Server status dot
            bool online = _llmClient.IsServerAvailable();
            using (var dotBrush = new SolidBrush(online ? Color.Lime : Color.FromArgb(255, 60, 60)))
                g.FillEllipse(dotBrush, w - 72, 10, 8, 8);
        }

        private Color GetTypeColor(string type)
        {
            switch (type)
            {
                case "JSON": return Color.FromArgb(255, 200, 50);
                case "XML": return Color.FromArgb(200, 150, 255);
                case "SQL": return Color.FromArgb(100, 200, 255);
                case "C#": return Color.FromArgb(100, 255, 100);
                case "Python": return Color.FromArgb(100, 180, 255);
                case "JS": return Color.FromArgb(255, 220, 100);
                case "URL": return Color.FromArgb(100, 220, 255);
                case "Base64": return Color.FromArgb(255, 150, 150);
                default: return DimText;
            }
        }

        private void DrawButtons(Graphics g)
        {
            for (int i = 0; i < _buttons.Count; i++)
                DrawButton(g, i);
        }

        private void DrawButton(Graphics g, int index)
        {
            ActionButton btn = _buttons[index];
            Color bgColor, borderColor;
            string label = btn.DisplayName;

            // Override label for cycling buttons
            if (btn.Action == ActionType.Transform)
                label = ContentDetector.GetTransformName(_transformIndex);
            else if (btn.Action == ActionType.Snippet)
                label = ContentDetector.GetSnippetName(_snippetIndex);
            else if (btn.Action == ActionType.Escape)
                label = ContentDetector.GetEscapeName(_escapeIndex);

            // State-based colors
            if (_processingButtonIndex == index)
            {
                int pulse = (int)(Math.Sin(_animationFrame * 0.3) * 30 + 100);
                bgColor = Color.FromArgb(pulse, pulse, 0);
                borderColor = Color.Yellow;
            }
            else if (btn.State == ButtonState.Success)
            {
                bgColor = Color.FromArgb(0, 80, 0);
                borderColor = Color.Lime;
            }
            else if (btn.State == ButtonState.Error)
            {
                bgColor = Color.FromArgb(80, 0, 0);
                borderColor = Color.Red;
            }
            else
            {
                bgColor = btn.ActiveColor;
                borderColor = Color.FromArgb(
                    Math.Min(255, btn.ActiveColor.R + 40),
                    Math.Min(255, btn.ActiveColor.G + 40),
                    Math.Min(255, btn.ActiveColor.B + 40));
            }

            // Background with slight gradient effect
            using (var bgBrush = new SolidBrush(bgColor))
                g.FillRectangle(bgBrush, btn.Bounds);

            // Border
            using (var borderPen = new Pen(borderColor, 1))
                g.DrawRectangle(borderPen, btn.Bounds.X, btn.Bounds.Y, btn.Bounds.Width - 1, btn.Bounds.Height - 1);

            // Label
            using (var font = new Font("Arial", 10, FontStyle.Bold))
            using (var textBrush = new SolidBrush(BrightText))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, textBrush,
                    new RectangleF(btn.Bounds.X, btn.Bounds.Y, btn.Bounds.Width, btn.Bounds.Height), sf);
            }

            // Subtle icon/hint for local actions
            if (!btn.IsLLMAction && btn.State == ButtonState.Ready)
            {
                using (var hintFont = new Font("Arial", 6))
                using (var hintBrush = new SolidBrush(Color.FromArgb(180, 180, 200)))
                {
                    string hint = btn.Action == ActionType.Format ? "auto" :
                                  btn.Action == ActionType.Transform ? "tap=cycle" :
                                  btn.Action == ActionType.Snippet ? "tap=paste" :
                                  btn.Action == ActionType.Escape ? "tap=cycle" : "";
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString(hint, hintFont, hintBrush,
                        new RectangleF(btn.Bounds.X, btn.Bounds.Bottom - 14, btn.Bounds.Width, 12), sf);
                }
            }
        }

        private void DrawStatusBar(Graphics g)
        {
            Size size = WidgetSize.ToSize();
            int y = size.Height - 20;

            // Status line background
            using (var statusBg = new SolidBrush(HeaderBg))
                g.FillRectangle(statusBg, 0, y, size.Width, 20);

            using (var statusFont = new Font("Arial", 7))
            {
                // Left: status message
                string status = _processingButtonIndex >= 0 ? "Processing..." : _statusMessage;
                using (var statusBrush = new SolidBrush(DimText))
                    g.DrawString(status, statusFont, statusBrush, 8, y + 3);

                // Center: clipboard stats
                using (var statsBrush = new SolidBrush(Color.FromArgb(120, 120, 140)))
                {
                    SizeF statsSize = g.MeasureString(_clipStats, statusFont);
                    g.DrawString(_clipStats, statusFont, statsBrush, (size.Width - statsSize.Width) / 2, y + 3);
                }

                // Right: endpoint name
                string endpoint = _llmClient.GetActiveEndpointName();
                using (var endBrush = new SolidBrush(_llmClient.IsServerAvailable() ? Color.FromArgb(100, 200, 100) : Color.FromArgb(200, 80, 80)))
                {
                    SizeF endSize = g.MeasureString(endpoint, statusFont);
                    g.DrawString(endpoint, statusFont, endBrush, size.Width - endSize.Width - 8, y + 3);
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
                RaiseWidgetUpdated(e);
            }
        }

        // === INPUT HANDLING ===

        public void RequestUpdate()
        {
            // RequestUpdate must NEVER do I/O — just draw
            DrawFrame();
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type == ClickType.Single && _processingButtonIndex < 0)
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
            ActionButton btn = _buttons[buttonIndex];
            Log("Button clicked: " + btn.DisplayName + " (action=" + btn.Action + ")");

            if (btn.IsLLMAction)
                HandleLLMAction(buttonIndex);
            else
                HandleLocalAction(buttonIndex);
        }

        private void HandleLLMAction(int buttonIndex)
        {
            ActionButton btn = _buttons[buttonIndex];
            _processingButtonIndex = buttonIndex;
            _statusMessage = btn.DisplayName + "...";

            Thread execThread = new Thread(() =>
            {
                try
                {
                    string clipboardText = ClipboardManager.GetText();
                    if (string.IsNullOrEmpty(clipboardText))
                    {
                        _statusMessage = "Clipboard empty!";
                        btn.State = ButtonState.Error;
                        _processingButtonIndex = -1;
                        return;
                    }

                    if (!_llmClient.IsServerAvailable())
                    {
                        _statusMessage = "Server offline!";
                        btn.State = ButtonState.Error;
                        _processingButtonIndex = -1;
                        return;
                    }

                    string result = _llmClient.SendChatCompletion(clipboardText, btn.SystemPrompt);
                    bool success = ClipboardManager.SetText(result);

                    if (success)
                    {
                        _statusMessage = btn.DisplayName + " done!";
                        btn.State = ButtonState.Success;
                        PollClipboard(); // Refresh stats
                    }
                    else
                    {
                        _statusMessage = "Clipboard write failed";
                        btn.State = ButtonState.Error;
                    }
                }
                catch (Exception ex)
                {
                    _statusMessage = "Error: " + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
                    btn.State = ButtonState.Error;
                }
                finally
                {
                    _processingButtonIndex = -1;
                    Thread.Sleep(2500);
                    btn.State = ButtonState.Ready;
                    _statusMessage = "Ready";
                }
            });
            execThread.IsBackground = true;
            execThread.Start();
        }

        private void HandleLocalAction(int buttonIndex)
        {
            ActionButton btn = _buttons[buttonIndex];

            try
            {
                string text = ClipboardManager.GetText();
                if (string.IsNullOrEmpty(text) && btn.Action != ActionType.Snippet)
                {
                    _statusMessage = "Clipboard empty!";
                    btn.State = ButtonState.Error;
                    ResetButtonAfterDelay(btn);
                    return;
                }

                string result = null;

                switch (btn.Action)
                {
                    case ActionType.Format:
                        result = ContentDetector.SmartFormat(text);
                        if (result == text)
                        {
                            _statusMessage = "No format for " + _clipContentType;
                            btn.State = ButtonState.Error;
                            ResetButtonAfterDelay(btn);
                            return;
                        }
                        _statusMessage = "Formatted " + _clipContentType;
                        break;

                    case ActionType.Transform:
                        result = ContentDetector.ApplyTransform(text, _transformIndex);
                        _statusMessage = ContentDetector.GetTransformName(_transformIndex);
                        _transformIndex = (_transformIndex + 1) % ContentDetector.TransformCount;
                        break;

                    case ActionType.Snippet:
                        result = ContentDetector.GetSnippetText(_snippetIndex);
                        _statusMessage = "Snippet: " + ContentDetector.GetSnippetName(_snippetIndex);
                        _snippetIndex = (_snippetIndex + 1) % ContentDetector.SnippetCount;
                        break;

                    case ActionType.Escape:
                        result = ContentDetector.ApplyEscape(text, _escapeIndex);
                        _statusMessage = ContentDetector.GetEscapeName(_escapeIndex);
                        _escapeIndex = (_escapeIndex + 1) % ContentDetector.EscapeCount;
                        break;
                }

                if (result != null)
                {
                    ClipboardManager.SetText(result);
                    btn.State = ButtonState.Success;
                    PollClipboard();
                    ResetButtonAfterDelay(btn);
                }
            }
            catch (Exception ex)
            {
                Log("Local action error: " + ex.Message);
                _statusMessage = "Error: " + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
                btn.State = ButtonState.Error;
                ResetButtonAfterDelay(btn);
            }
        }

        private void ResetButtonAfterDelay(ActionButton btn)
        {
            Thread resetThread = new Thread(() =>
            {
                Thread.Sleep(2000);
                btn.State = ButtonState.Ready;
                _statusMessage = "Ready";
            });
            resetThread.IsBackground = true;
            resetThread.Start();
        }

        private void Log(string message)
        {
            Debug.WriteLine("[ClipboardAgent] " + message);
        }

        public void Dispose()
        {
            _isPaused = true;
            _isRunning = false;
            if (_updateThread != null && _updateThread.IsAlive)
                _updateThread.Join(5000);
            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void EnterSleep() { _isPaused = true; }
        public void ExitSleep() { _isPaused = false; }
    }
}
