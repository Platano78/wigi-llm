using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetInstance
    {
        private void DrawSessions(Graphics g, MultiSessionData multi, int yStart)
        {
            int W = BitmapCurrent.Width;
            int H = BitmapCurrent.Height;
            int footerHeight = 30;
            int available = H - yStart - footerHeight;
            if (available < 40) available = 40;

            if (multi == null || multi.Sessions == null || multi.Sessions.Count == 0)
            {
                using (Font noFont = new Font("Segoe UI", 9))
                using (SolidBrush brush = new SolidBrush(Color.Gray))
                {
                    string txt = "No active sessions";
                    SizeF sz = g.MeasureString(txt, noFont);
                    g.DrawString(txt, noFont, brush, (W - sz.Width) / 2, yStart + 14);
                }
                return;
            }

            int maxShow;
            if (H < 220) maxShow = 2;
            else if (H < 300) maxShow = 3;
            else maxShow = 5;

            int total = multi.Sessions.Count;
            int show = Math.Min(maxShow, total);

            int overflowH = total > show ? 14 : 0;
            int rowH = (available - overflowH) / Math.Max(show, 1);
            if (rowH < 36) rowH = 36;

            int y = yStart;
            for (int i = 0; i < show; i++)
            {
                DrawSessionRow(g, multi.Sessions[i], 4, y, W - 8, rowH - 2);
                y += rowH;
            }

            if (total > show)
            {
                using (Font moreFont = new Font("Segoe UI", 8))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(160, 160, 180)))
                {
                    string txt = "+ " + (total - show) + " more";
                    SizeF sz = g.MeasureString(txt, moreFont);
                    g.DrawString(txt, moreFont, brush, (W - sz.Width) / 2, y);
                }
            }
        }

        private void DrawSessionRow(Graphics g, StatusLineData status, int x, int y, int w, int h)
        {
            if (status == null || !status.IsValid || status.Session == null)
            {
                using (Font font = new Font("Segoe UI", 8))
                using (SolidBrush brush = new SolidBrush(Color.Gray))
                {
                    g.DrawString("(no data)", font, brush, x, y);
                }
                return;
            }

            SessionContext s = status.Session;
            float pct = s.UsagePercentage;
            Color tierColor =
                pct > 85 ? Color.Red :
                pct > 70 ? Color.Orange :
                pct > 50 ? Color.Yellow :
                Color.LimeGreen;

            float scale = h / 36.0f;
            if (scale < 1.0f) scale = 1.0f;
            if (scale > 2.4f) scale = 2.4f;

            int line1Pt = (int)(9 * scale);
            int line2Pt = (int)(7 * scale + 0.5f);
            if (line1Pt < 9) line1Pt = 9;
            if (line2Pt < 7) line2Pt = 7;
            int barH = (int)(7 * scale + 0.5f);
            if (barH < 7) barH = 7;

            using (Font line1Font = new Font("Segoe UI", line1Pt, FontStyle.Bold))
            {
                string pctStr = pct.ToString("F0") + "%";
                SizeF pctSz = g.MeasureString(pctStr, line1Font);
                string modelText = s.ModelName != null ? s.ModelName : "Unknown";
                int maxModelW = w - (int)pctSz.Width - 6;
                if (maxModelW < 20) maxModelW = 20;
                string modelTrunc = TruncateToWidth(g, modelText, line1Font, maxModelW);

                using (SolidBrush modelBrush = new SolidBrush(Color.FromArgb(120, 220, 255)))
                    g.DrawString(modelTrunc, line1Font, modelBrush, x, y);
                using (SolidBrush pctBrush = new SolidBrush(tierColor))
                    g.DrawString(pctStr, line1Font, pctBrush, x + w - pctSz.Width, y);
            }

            int line1Px = line1Pt + 5;
            int row2y = y + line1Px;
            using (Font subFont = new Font("Segoe UI", line2Pt))
            using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(180, 180, 200)))
            {
                long ctxK = s.ContextLength / 1000;
                long winK = s.ContextWindowSize / 1000;
                string sid = "";
                if (!string.IsNullOrEmpty(s.SessionId))
                {
                    sid = s.SessionId.Length >= 6 ? s.SessionId.Substring(0, 6) : s.SessionId;
                }
                string winStr = winK >= 1000 ? (winK / 1000) + "M" : (winK + "k");
                string line = ctxK + "k / " + winStr + "  ·  " + sid;
                g.DrawString(line, subFont, subBrush, x, row2y);
            }

            int barY = y + h - barH - 3;
            int minBarY = row2y + line2Pt + 6;
            if (barY < minBarY) barY = minBarY;

            using (Pen border = new Pen(Color.FromArgb(80, 80, 100), 1))
                g.DrawRectangle(border, x, barY, w - 1, barH);

            int innerW = w - 2;
            int fillWidth = (int)(innerW * Math.Min(pct, 100f) / 100f);
            if (fillWidth > 0)
            {
                using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                    new Rectangle(x + 1, barY + 1, Math.Max(fillWidth, 1), barH - 2),
                    tierColor,
                    Color.FromArgb(150, tierColor),
                    90f))
                {
                    g.FillRectangle(fillBrush, x + 1, barY + 1, fillWidth, barH - 2);
                }
                DrawBarOverlay(g, x + 1, barY + 1, fillWidth, barH - 2, TierFromPercent(pct));
            }
        }

        private string TruncateToWidth(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            string ellipsis = "…";
            for (int i = text.Length - 1; i > 0; i--)
            {
                string candidate = text.Substring(0, i).TrimEnd() + ellipsis;
                if (g.MeasureString(candidate, font).Width <= maxWidth) return candidate;
            }
            return ellipsis;
        }
    }
}