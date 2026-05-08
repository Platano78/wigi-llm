using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatsWidget
{
    public partial class LLMStatsWidgetInstance : IWidgetInstance
    {
        private const int POLL_INTERVAL_MS = 2000;

        private readonly object _drawLock = new object();
        private Thread _pollThread;
        private volatile bool _isRunning;
        private volatile bool _isPaused;

        private Bitmap _bitmap;
        private int _w;
        private int _h;

        // Snapshot fields are updated by the poll thread and read by the draw thread.
        // Reads of reference / int / double are atomic on x64-aligned fields, and
        // the entire snapshot is replaced wholesale every poll, so we can read each
        // field independently without locking — torn reads at most cost one stale frame.
        private string _modelName = "";
        private double _genTps = 0;
        private double _promptTps = 0;
        private int _requestsProcessing = 0;
        private bool _reachable = false;

        public LLMStatsWidgetInstance(IWidgetObject parent, WidgetSize size, Guid guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = guid;
            this.WidgetSize = size;
            Size px = size.ToSize();
            _w = px.Width;
            _h = px.Height;
            _bitmap = new Bitmap(_w, _h, System.Drawing.Imaging.PixelFormat.Format16bppRgb565);

            DrawFrame();   // initial paint so the tile isn't blank during first poll
            StartPollThread();
        }

        private void StartPollThread()
        {
            _isRunning = true;
            _isPaused = false;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Name = "LLMStatsWidget-Poll";
            _pollThread.Start();
        }

        private void PollLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (!_isPaused)
                    {
                        StatsSnapshot snap = MetricsClient.Fetch();
                        _modelName = snap.ModelName;
                        _genTps = snap.GenTokensPerSec;
                        _promptTps = snap.PromptTokensPerSec;
                        _requestsProcessing = snap.RequestsProcessing;
                        _reachable = snap.Reachable;
                        DrawFrame();
                    }
                }
                catch
                {
                    // Swallow — next tick will retry. Snapshot fields keep their last value.
                }
                Thread.Sleep(POLL_INTERVAL_MS);
            }
        }

        private void DrawFrame()
        {
            lock (_drawLock)
            {
                using (Graphics g = Graphics.FromImage(_bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // Background — slight blue tint, distinct from the launcher's flat dark gray
                    using (LinearGradientBrush bg = new LinearGradientBrush(
                        new Rectangle(0, 0, _w, _h),
                        Color.FromArgb(18, 20, 30),
                        Color.FromArgb(10, 12, 20),
                        90f))
                    {
                        g.FillRectangle(bg, 0, 0, _w, _h);
                    }

                    if (!_reachable)
                    {
                        DrawOfflineState(g);
                    }
                    else
                    {
                        DrawLiveState(g);
                    }

                    DrawAccentLine(g);
                }
                Push();
            }
        }

        private void DrawOfflineState(Graphics g)
        {
            Color dim = Color.FromArgb(120, 120, 140);
            using (Font f = new Font("Arial", 11, FontStyle.Bold))
            using (Brush b = new SolidBrush(dim))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString("OFFLINE", f, b, new RectangleF(0, 0, _w, _h * 0.55f), fmt);
            }
            using (Font f = new Font("Arial", 8, FontStyle.Regular))
            using (Brush b = new SolidBrush(Color.FromArgb(90, 90, 110)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                g.DrawString("router :8081", f, b, new RectangleF(0, _h * 0.55f, _w, 18), fmt);
            }
        }

        private void DrawLiveState(Graphics g)
        {
            // Top: model name, truncated to fit
            string name = string.IsNullOrEmpty(_modelName) ? "(no model)" : _modelName;
            using (Font nameFont = new Font("Arial", 8, FontStyle.Bold))
            using (Brush nameBrush = new SolidBrush(Color.FromArgb(170, 180, 200)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Near;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(name, nameFont, nameBrush,
                             new RectangleF(4, 4, _w - 8, 14), fmt);
            }

            // Center: big tokens/sec number — color-coded by throughput band
            Color numColor = ColorForThroughput(_genTps);
            string numText;
            if (_genTps >= 100) numText = string.Format("{0:F0}", _genTps);
            else if (_genTps >= 10) numText = string.Format("{0:F0}", _genTps);
            else if (_genTps > 0) numText = string.Format("{0:F1}", _genTps);
            else numText = "--";

            float numFontSize = NumberFontSize(numText);
            using (Font numFont = new Font("Arial", numFontSize, FontStyle.Bold))
            using (Brush numBrush = new SolidBrush(numColor))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(numText, numFont, numBrush,
                             new RectangleF(0, _h * 0.18f, _w, _h * 0.55f), fmt);
            }

            // Below number: "tok/s" label
            using (Font lblFont = new Font("Arial", 8, FontStyle.Regular))
            using (Brush lblBrush = new SolidBrush(Color.FromArgb(150, 160, 180)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                g.DrawString("tok/s gen", lblFont, lblBrush,
                             new RectangleF(0, _h * 0.65f, _w, 14), fmt);
            }

            // Bottom: secondary line — prompt throughput when meaningful
            string secondary = "";
            if (_requestsProcessing > 0) secondary = "active";
            else if (_promptTps > 0) secondary = string.Format("{0:F0} prompt/s", _promptTps);

            if (!string.IsNullOrEmpty(secondary))
            {
                using (Font sFont = new Font("Arial", 7, FontStyle.Regular))
                using (Brush sBrush = new SolidBrush(Color.FromArgb(110, 120, 140)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    g.DrawString(secondary, sFont, sBrush,
                                 new RectangleF(0, _h - 14, _w, 12), fmt);
                }
            }
        }

        private void DrawAccentLine(Graphics g)
        {
            // Thin colored bar at the bottom — visual cue for state
            Color accent;
            if (!_reachable) accent = Color.FromArgb(80, 80, 95);
            else if (_genTps <= 0) accent = Color.FromArgb(100, 110, 130);
            else accent = ColorForThroughput(_genTps);

            using (Brush b = new SolidBrush(accent))
            {
                g.FillRectangle(b, 0, _h - 2, _w, 2);
            }
        }

        // Color mapping: under 15 = warm yellow (slow), 15-40 = cyan (good), >40 = bright green (great)
        private static Color ColorForThroughput(double tps)
        {
            if (tps <= 0) return Color.FromArgb(120, 120, 140);
            if (tps < 15) return Color.FromArgb(255, 200, 60);
            if (tps < 40) return Color.FromArgb(0, 230, 255);
            return Color.FromArgb(80, 255, 140);
        }

        // Scale font down for longer numbers so 3+ digits still fit in a 1x1 tile.
        private float NumberFontSize(string numText)
        {
            int len = numText.Length;
            if (_w <= 110)
            {
                if (len <= 2) return 36f;
                if (len <= 3) return 30f;
                if (len <= 4) return 24f;
                return 20f;
            }
            // 2x1 or larger — go bigger
            if (len <= 2) return 48f;
            if (len <= 3) return 40f;
            if (len <= 4) return 32f;
            return 26f;
        }

        private void Push()
        {
            WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
            e.WaitMax = 1000;
            e.WidgetBitmap = _bitmap;
            RaiseWidgetUpdated(e);
        }

        public Bitmap BitmapCurrent { get { return _bitmap; } }

        public void RequestUpdate()
        {
            DrawFrame();
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            // Tap to force an immediate refresh
            if (click_type == ClickType.Single)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        StatsSnapshot snap = MetricsClient.Fetch();
                        _modelName = snap.ModelName;
                        _genTps = snap.GenTokensPerSec;
                        _promptTps = snap.PromptTokensPerSec;
                        _requestsProcessing = snap.RequestsProcessing;
                        _reachable = snap.Reachable;
                        DrawFrame();
                    }
                    catch { }
                });
            }
        }

        public void EnterSleep() { _isPaused = true; }
        public void ExitSleep() { _isPaused = false; }

        public void Dispose()
        {
            _isPaused = true;
            _isRunning = false;
            if (_pollThread != null && _pollThread.IsAlive)
                _pollThread.Join(3000);
            if (_bitmap != null)
                _bitmap.Dispose();
        }
    }
}
