using System;
using System.Drawing;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Three-zone VRAM composition bar:
    ///   [model weights | KV cache | free space]
    /// Color-coded so the user can see KV cache growth eating into headroom.
    /// </summary>
    public static class VramBar
    {
        public static void Draw(Graphics g, Rectangle bounds,
                                double modelWeightsGb, double kvCacheGb, double totalGb,
                                string label)
        {
            // Label above the bar
            using (Font lFont = new Font("Arial", 8, FontStyle.Bold))
            using (Brush lBrush = new SolidBrush(Color.FromArgb(200, 210, 230)))
            {
                g.DrawString(label, lFont, lBrush, bounds.X, bounds.Y);
            }

            int barTop = bounds.Y + 14;
            int barH = 14;
            Rectangle barRect = new Rectangle(bounds.X, barTop, bounds.Width, barH);

            // Background (free)
            using (Brush bg = new SolidBrush(Color.FromArgb(30, 40, 55)))
            {
                g.FillRectangle(bg, barRect);
            }

            if (totalGb > 0)
            {
                double weightsFrac = Math.Max(0, Math.Min(1, modelWeightsGb / totalGb));
                double kvFrac = Math.Max(0, Math.Min(1 - weightsFrac, kvCacheGb / totalGb));

                int weightsW = (int)(bounds.Width * weightsFrac);
                int kvW = (int)(bounds.Width * kvFrac);

                // Weights — blue
                if (weightsW > 0)
                {
                    using (Brush b = new SolidBrush(Color.FromArgb(90, 140, 220)))
                    {
                        g.FillRectangle(b, bounds.X, barTop, weightsW, barH);
                    }
                }
                // KV cache — orange (warns of growth)
                if (kvW > 0)
                {
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 160, 60)))
                    {
                        g.FillRectangle(b, bounds.X + weightsW, barTop, kvW, barH);
                    }
                }
            }

            // Outline
            using (Pen p = new Pen(Color.FromArgb(70, 80, 100), 1))
            {
                g.DrawRectangle(p, barRect);
            }

            // Numeric readout below
            string txt;
            if (totalGb > 0)
            {
                double used = modelWeightsGb + kvCacheGb;
                txt = used.ToString("F1") + " / " + totalGb.ToString("F1") + " GB";
            }
            else
            {
                txt = "-- / -- GB";
            }
            using (Font tFont = new Font("Arial", 8, FontStyle.Regular))
            using (Brush tBrush = new SolidBrush(Color.FromArgb(180, 190, 210)))
            {
                g.DrawString(txt, tFont, tBrush, bounds.X, barTop + barH + 1);
            }

            // Tiny legend (only if KV cache visible)
            if (kvCacheGb > 0.05)
            {
                int legendX = bounds.X + bounds.Width - 80;
                int legendY = barTop + barH + 1;
                using (Brush bw = new SolidBrush(Color.FromArgb(90, 140, 220)))
                {
                    g.FillRectangle(bw, legendX, legendY + 3, 6, 6);
                }
                using (Brush bk = new SolidBrush(Color.FromArgb(255, 160, 60)))
                {
                    g.FillRectangle(bk, legendX + 38, legendY + 3, 6, 6);
                }
                using (Font lf = new Font("Arial", 7, FontStyle.Regular))
                using (Brush lb = new SolidBrush(Color.FromArgb(150, 160, 180)))
                {
                    g.DrawString("wts", lf, lb, legendX + 9, legendY);
                    g.DrawString("kv", lf, lb, legendX + 47, legendY);
                }
            }
        }
    }
}
