using System;
using System.Drawing;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetInstance
    {
        private void DrawBarOverlay(Graphics g, int x, int y, int w, int h, UsageTier tier)
        {
            if (w <= 0 || h <= 0) return;

            int sx = x + ShimmerOffsetPx(w);
            if (sx >= x && sx < x + w)
            {
                int sw = Math.Min(3, x + w - sx);
                using (SolidBrush shimmer = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    g.FillRectangle(shimmer, sx, y, sw, h);
            }

            if (tier == UsageTier.Alarm)
            {
                int off = StripeOffsetPx();
                int spacing = 12;
                using (Pen stripe = new Pen(Color.FromArgb(90, 255, 220, 120), 1))
                {
                    int line = x - off;
                    while (line < x + w + h)
                    {
                        int x1 = line, y1 = y;
                        int x2 = line + h, y2 = y + h;
                        if (x2 < x) { line += spacing; continue; }
                        if (x1 > x + w) break;
                        g.DrawLine(stripe, x1, y1, x2, y2);
                        line += spacing;
                    }
                }
            }
        }
    }
}