using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetInstance
    {
        private void DrawFrame()
        {
            if (BitmapCurrent == null) return;

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(18, 18, 28));

                MultiSessionData multi = null;
                try
                {
                    if (_statusLineReader != null) multi = _statusLineReader.ReadAllSessions();
                }
                catch (Exception ex) { LogDebug("ReadAll: " + ex.Message); }

                WeeklyUsage weekly = GetWeeklyCached();

                int y = DrawHeader(g, weekly);
                DrawSessions(g, multi, y);
                DrawFooter(g, multi, weekly);
            }
        }

        private WeeklyUsage GetWeeklyCached()
        {
            try
            {
                if (_weeklyAggregator == null) return null;
                if (_weeklyCache == null || DateTime.Now - _lastWeeklyRefresh > WeeklyRefreshInterval)
                {
                    _weeklyCache = _weeklyAggregator.GetWeeklyUsage();
                    _lastWeeklyRefresh = DateTime.Now;
                }
                return _weeklyCache;
            }
            catch (Exception ex) { LogDebug("Weekly: " + ex.Message); return _weeklyCache; }
        }

        private int DrawHeader(Graphics g, WeeklyUsage weekly)
        {
            int W = BitmapCurrent.Width;
            using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            {
                g.DrawString("Context Monitor", titleFont, titleBrush, 6, 4);
            }
            if (weekly != null)
            {
                using (Font wkFont = new Font("Segoe UI", 9, FontStyle.Bold))
                {
                    string costStr = "$" + weekly.TotalSpent.ToString("F2");
                    SizeF sz = g.MeasureString(costStr, wkFont);
                    Color wkColor = weekly.Percentage > 90 ? Color.Red : (weekly.Percentage > 70 ? Color.Orange : Color.LimeGreen);
                    using (SolidBrush wkBrush = new SolidBrush(wkColor))
                        g.DrawString(costStr, wkFont, wkBrush, W - sz.Width - 6, 4);
                }
            }
            return 22;
        }

        private void DrawFooter(Graphics g, MultiSessionData multi, WeeklyUsage weekly)
        {
            int W = BitmapCurrent.Width;
            int H = BitmapCurrent.Height;
            int footerTop = H - 28;

            using (Pen sep = new Pen(Color.FromArgb(50, 50, 70), 1))
                g.DrawLine(sep, 6, footerTop, W - 6, footerTop);

            int sessionCount = multi != null && multi.Sessions != null ? multi.Sessions.Count : 0;

            string aggStatus = "Safe";
            Color aggColor = Color.LimeGreen;
            if (multi != null && multi.Sessions != null && multi.Sessions.Count > 0)
            {
                float topPct = 0;
                foreach (StatusLineData sd in multi.Sessions)
                {
                    if (sd != null && sd.IsValid && sd.Session != null && sd.Session.UsagePercentage > topPct)
                        topPct = sd.Session.UsagePercentage;
                }
                if (topPct >= 85) { aggStatus = "HANDOFF"; aggColor = Color.Red; }
                else if (topPct >= 70) { aggStatus = "Prepare"; aggColor = Color.Orange; }
                else if (topPct >= 50) { aggStatus = "Monitor"; aggColor = Color.Yellow; }
            }

            int textY = footerTop + 4;

            using (Font footFont = new Font("Segoe UI", 8))
            using (SolidBrush dimBrush = new SolidBrush(Color.FromArgb(160, 160, 180)))
            {
                string left = sessionCount + " session" + (sessionCount == 1 ? "" : "s");
                g.DrawString(left, footFont, dimBrush, 6, textY);
                if (weekly != null)
                {
                    string right = weekly.Percentage.ToString("F0") + "% wk";
                    SizeF sz = g.MeasureString(right, footFont);
                    g.DrawString(right, footFont, dimBrush, W - sz.Width - 6, textY);
                }
            }

            using (Font statusFont = new Font("Segoe UI", 9, FontStyle.Bold))
            using (SolidBrush statusBrush = new SolidBrush(aggColor))
            {
                SizeF sz = g.MeasureString(aggStatus, statusFont);
                g.DrawString(aggStatus, statusFont, statusBrush, (W - sz.Width) / 2, textY - 1);
            }

            if (weekly != null)
            {
                int barY = H - 6;
                int barH = 3;
                int barX = 6;
                int barW = W - 12;
                using (Pen border = new Pen(Color.FromArgb(60, 60, 80), 1))
                    g.DrawRectangle(border, barX, barY, barW - 1, barH);
                int fill = (int)((barW - 2) * Math.Min(weekly.Percentage, 100f) / 100f);
                if (fill > 0)
                {
                    Color barColor = weekly.Percentage > 90 ? Color.Red : (weekly.Percentage > 70 ? Color.Orange : Color.LimeGreen);
                    using (SolidBrush b = new SolidBrush(barColor))
                        g.FillRectangle(b, barX + 1, barY + 1, fill, barH - 2);
                }
            }
        }
    }
}