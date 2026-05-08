using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Top-level HUD layout — draws the 4 corner clusters of the brain monitor.
    ///
    ///   +--------------------------------------------+
    ///   | TopLeft: model identity   TopRight: ring   |
    ///   |                                            |
    ///   |             (particle field — drawn        |
    ///   |              separately by caller)         |
    ///   |                                            |
    ///   | BotLeft: VRAM bar         BotRight: queue  |
    ///   +--------------------------------------------+
    ///   |               sparkline strip              |
    ///   +--------------------------------------------+
    ///
    /// Each panel has a glass-pane background so HUD elements remain readable
    /// over the active particle stream.
    /// </summary>
    public static class HudPanel
    {
        // Glass pane behind a HUD cluster — semi-transparent gradient
        private static void GlassPane(Graphics g, Rectangle r)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                r, Color.FromArgb(150, 18, 24, 36), Color.FromArgb(110, 10, 14, 22),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(b, r);
            }
            using (Pen p = new Pen(Color.FromArgb(60, 100, 140, 180), 1))
            {
                g.DrawRectangle(p, r);
            }
        }

        // ---------- Top-left: model identity ----------
        public static void DrawTopLeft(Graphics g, Rectangle bounds, string modelName,
                                       int contextWindow, int slots, string serverStatus)
        {
            GlassPane(g, bounds);
            int pad = 8;
            int x = bounds.X + pad;
            int y = bounds.Y + pad;

            // Status indicator dot
            Color dotColor = serverStatus == "online" ? Color.FromArgb(80, 255, 140)
                           : serverStatus == "offline" ? Color.FromArgb(255, 80, 80)
                           : Color.FromArgb(255, 200, 60);
            using (Brush dot = new SolidBrush(dotColor))
            {
                g.FillEllipse(dot, x, y + 5, 7, 7);
            }

            // Model name — large, bold
            string display = string.IsNullOrEmpty(modelName) ? "(no model)" : modelName;
            using (Font nameFont = new Font("Arial", 13, FontStyle.Bold))
            using (Brush nameBrush = new SolidBrush(Color.FromArgb(220, 230, 250)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                Rectangle nameBox = new Rectangle(x + 12, y, bounds.Width - pad * 2 - 12, 18);
                g.DrawString(display, nameFont, nameBrush, nameBox, fmt);
            }

            // Sub-line: ctx + slots
            string sub;
            if (contextWindow > 0 || slots > 0)
            {
                string ctxStr = contextWindow > 0
                    ? FormatTokens(contextWindow) + " ctx"
                    : "ctx --";
                string slotStr = slots > 0 ? slots + " slot" + (slots == 1 ? "" : "s") : "";
                sub = string.IsNullOrEmpty(slotStr) ? ctxStr : ctxStr + " · " + slotStr;
            }
            else
            {
                sub = "router :8081";
            }
            using (Font subFont = new Font("Arial", 9, FontStyle.Regular))
            using (Brush subBrush = new SolidBrush(Color.FromArgb(150, 165, 195)))
            {
                g.DrawString(sub, subFont, subBrush, x, y + 22);
            }

            // Mini-label
            using (Font lFont = new Font("Arial", 7, FontStyle.Regular))
            using (Brush lBrush = new SolidBrush(Color.FromArgb(110, 130, 160)))
            {
                g.DrawString("LOADED MODEL", lFont, lBrush, x, bounds.Bottom - 14);
            }
        }

        // ---------- Top-right: throughput ring gauge ----------
        public static void DrawTopRight(Graphics g, Rectangle bounds,
                                        double genTps, double genTpsMax)
        {
            GlassPane(g, bounds);
            int pad = 6;
            // Ring fills the panel; max scale tunable by caller (passed in)
            Rectangle ring = new Rectangle(bounds.X + pad, bounds.Y + pad + 4,
                                           bounds.Width - pad * 2, bounds.Height - pad * 2 - 4);
            RingGauge.Draw(g, ring, genTps, genTpsMax, "GENERATION", "tok/s");
        }

        // ---------- Bottom-left: VRAM composition ----------
        public static void DrawBottomLeft(Graphics g, Rectangle bounds,
                                          double weightsGb, double kvCacheGb, double totalGb)
        {
            GlassPane(g, bounds);
            int pad = 8;
            Rectangle inner = new Rectangle(bounds.X + pad, bounds.Y + pad,
                                            bounds.Width - pad * 2, bounds.Height - pad * 2);
            VramBar.Draw(g, inner, weightsGb, kvCacheGb, totalGb, "VRAM");
        }

        // ---------- Bottom-right: request queue + prompt rate ----------
        public static void DrawBottomRight(Graphics g, Rectangle bounds,
                                           int requestsProcessing, int requestsDeferred,
                                           double promptTps)
        {
            GlassPane(g, bounds);
            int pad = 8;
            int x = bounds.X + pad;
            int y = bounds.Y + pad;

            // Top: queue label + numbers
            using (Font lFont = new Font("Arial", 8, FontStyle.Bold))
            using (Brush lBrush = new SolidBrush(Color.FromArgb(200, 210, 230)))
            {
                g.DrawString("REQUESTS", lFont, lBrush, x, y);
            }

            // Big number: active requests
            Color actColor = requestsProcessing > 0 ? Color.FromArgb(0, 230, 255)
                                                    : Color.FromArgb(140, 150, 170);
            using (Font n = new Font("Arial", 22, FontStyle.Bold))
            using (Brush b = new SolidBrush(actColor))
            {
                g.DrawString(requestsProcessing.ToString(), n, b, x, y + 12);
            }
            using (Font lf = new Font("Arial", 8, FontStyle.Regular))
            using (Brush lb = new SolidBrush(Color.FromArgb(150, 160, 180)))
            {
                g.DrawString("active", lf, lb, x + 30, y + 26);
            }

            // Deferred badge if any
            if (requestsDeferred > 0)
            {
                using (Font df = new Font("Arial", 9, FontStyle.Bold))
                using (Brush db = new SolidBrush(Color.FromArgb(255, 100, 100)))
                {
                    g.DrawString("+" + requestsDeferred + " queued", df, db,
                                 x + 80, y + 14);
                }
            }

            // Bottom: prompt tok/s
            string promptText;
            if (promptTps > 0) promptText = ((int)promptTps) + " prompt/s";
            else promptText = "-- prompt/s";
            using (Font pf = new Font("Arial", 9, FontStyle.Regular))
            using (Brush pb = new SolidBrush(Color.FromArgb(180, 190, 210)))
            {
                g.DrawString(promptText, pf, pb, x, bounds.Bottom - 18);
            }
        }

        // ---------- Helpers ----------
        private static string FormatTokens(int n)
        {
            if (n >= 1024 * 1024) return ((double)n / (1024 * 1024)).ToString("F1") + "M";
            if (n >= 1024) return ((double)n / 1024).ToString("F0") + "K";
            return n.ToString();
        }
    }
}
