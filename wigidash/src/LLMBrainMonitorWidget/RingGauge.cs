using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Throughput dial — circular ring with a colored arc proportional to value/max.
    /// Renders centered in a square bounding box. Number drawn in the middle.
    /// </summary>
    public static class RingGauge
    {
        public static void Draw(Graphics g, Rectangle bounds, double value, double max, string label, string units)
        {
            int side = Math.Min(bounds.Width, bounds.Height);
            int cx = bounds.X + bounds.Width / 2;
            int cy = bounds.Y + bounds.Height / 2;
            int outerR = side / 2 - 4;
            int innerR = outerR - 8;

            Rectangle ringBox = new Rectangle(cx - outerR, cy - outerR, outerR * 2, outerR * 2);

            // Background ring
            using (Pen bgPen = new Pen(Color.FromArgb(40, 60, 90), 6))
            {
                bgPen.StartCap = LineCap.Round;
                bgPen.EndCap = LineCap.Round;
                g.DrawArc(bgPen, ringBox, -210, 240);
            }

            // Value arc — clamp 0..1, sweep 240 degrees
            double pct = Math.Max(0, Math.Min(1, value / max));
            float sweep = (float)(240.0 * pct);

            Color arcColor = ColorForFraction(pct);
            using (Pen arcPen = new Pen(arcColor, 6))
            {
                arcPen.StartCap = LineCap.Round;
                arcPen.EndCap = LineCap.Round;
                if (sweep > 0)
                    g.DrawArc(arcPen, ringBox, -210, sweep);
            }

            // Center text — number
            string numText = FormatValue(value);
            using (Font numFont = new Font("Arial", side >= 100 ? 22 : 16, FontStyle.Bold))
            using (Brush numBrush = new SolidBrush(arcColor))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                Rectangle txtBox = new Rectangle(cx - innerR, cy - innerR + 2, innerR * 2, innerR);
                g.DrawString(numText, numFont, numBrush, txtBox, fmt);
            }

            // Units below number
            using (Font unitFont = new Font("Arial", 8, FontStyle.Regular))
            using (Brush unitBrush = new SolidBrush(Color.FromArgb(160, 170, 190)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                Rectangle txtBox = new Rectangle(cx - innerR, cy + 2, innerR * 2, innerR - 2);
                g.DrawString(units, unitFont, unitBrush, txtBox, fmt);
            }

            // Label above ring
            if (!string.IsNullOrEmpty(label))
            {
                using (Font lFont = new Font("Arial", 8, FontStyle.Bold))
                using (Brush lBrush = new SolidBrush(Color.FromArgb(200, 210, 230)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    g.DrawString(label, lFont, lBrush, new Rectangle(bounds.X, bounds.Y - 2, bounds.Width, 14), fmt);
                }
            }
        }

        private static string FormatValue(double v)
        {
            if (v <= 0) return "--";
            if (v >= 100) return ((int)v).ToString();
            if (v >= 10) return ((int)v).ToString();
            return v.ToString("F1");
        }

        // Bands tuned for tokens/sec on a consumer GPU
        private static Color ColorForFraction(double pct)
        {
            if (pct <= 0) return Color.FromArgb(120, 120, 140);
            if (pct < 0.25) return Color.FromArgb(255, 200, 60);   // yellow — slow
            if (pct < 0.6) return Color.FromArgb(0, 230, 255);     // cyan — good
            return Color.FromArgb(80, 255, 140);                   // green — great
        }
    }
}
