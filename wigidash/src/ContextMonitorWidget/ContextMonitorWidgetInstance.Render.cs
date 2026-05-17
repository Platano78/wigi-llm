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

                MultiSessionData multi = GetSessionsCached();
                UsageCacheData usage = GetUsageCached();

                UsageTier aggTier = ComputeAggregateTier(multi);
                AnimationIntensity eff = EffectiveIntensity(aggTier);
                g.Clear(ComputeBackgroundColor(aggTier, eff));

                int y = DrawHeader(g, usage);
                DrawSessions(g, multi, y);
                DrawFooter(g, multi, usage);
            }
        }

        private static Color QuotaColor(float pct)
        {
            if (pct >= 85) return Color.Red;
            if (pct >= 70) return Color.Orange;
            if (pct >= 50) return Color.Yellow;
            return Color.LimeGreen;
        }

        private int DrawHeader(Graphics g, UsageCacheData usage)
        {
            int W = BitmapCurrent.Width;
            using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            {
                g.DrawString("Context Monitor", titleFont, titleBrush, 6, 14);
            }
            int mascotX = W - MascotDrawW - 4;
            if (usage != null && usage.IsValid)
            {
                using (Font wkFont = new Font("Segoe UI", 9, FontStyle.Bold))
                {
                    string fhStr = "5h " + ((int)Math.Round(usage.FiveHourPct)) + "%";
                    string sdStr = "7d " + ((int)Math.Round(usage.SevenDayPct)) + "%";
                    SizeF szFh = g.MeasureString(fhStr + " ", wkFont);
                    SizeF szSd = g.MeasureString(sdStr, wkFont);
                    float x0 = mascotX - (szFh.Width + szSd.Width) - 6;
                    using (SolidBrush b1 = new SolidBrush(QuotaColor((float)usage.FiveHourPct)))
                        g.DrawString(fhStr, wkFont, b1, x0, 14);
                    using (SolidBrush b2 = new SolidBrush(QuotaColor((float)usage.SevenDayPct)))
                        g.DrawString(sdStr, wkFont, b2, x0 + szFh.Width, 14);
                }
            }
            UsageTier mascotTier = TierFromPercent(GetTopPercent(GetSessionsCached()));
            DrawMascot(g, mascotX, 2, mascotTier);
            return MascotDrawH + 4;
        }

        private float GetTopPercent(MultiSessionData multi)
        {
            if (multi == null || multi.Sessions == null) return 0f;
            float top = 0f;
            foreach (StatusLineData sd in multi.Sessions)
            {
                if (sd != null && sd.IsValid && sd.Session != null && sd.Session.UsagePercentage > top)
                    top = sd.Session.UsagePercentage;
            }
            return top;
        }

        private void DrawFooter(Graphics g, MultiSessionData multi, UsageCacheData usage)
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
                if (usage != null && usage.IsValid)
                {
                    string right = "5h: " + UsageCacheReader.FormatCountdown(usage.FiveHourResetUtc);
                    SizeF sz = g.MeasureString(right, footFont);
                    g.DrawString(right, footFont, dimBrush, W - sz.Width - 6, textY);
                }
            }

            UsageTier bannerTier = TierFromPercent(GetTopPercent(multi));
            AnimationIntensity bannerEff = EffectiveIntensity(bannerTier);
            float pulse = BannerPulseScale(bannerTier, bannerEff);
            int flashA = BannerFlashAlpha(bannerTier, bannerEff);
            int baseSize = 9;
            int scaledSize = (int)Math.Round(baseSize * pulse);
            if (scaledSize < 8) scaledSize = 8;
            using (Font statusFont = new Font("Segoe UI", scaledSize, FontStyle.Bold))
            using (SolidBrush statusBrush = new SolidBrush(Color.FromArgb(flashA, aggColor)))
            {
                SizeF sz = g.MeasureString(aggStatus, statusFont);
                g.DrawString(aggStatus, statusFont, statusBrush, (W - sz.Width) / 2, textY - 1);
            }

            if (usage != null && usage.IsValid)
            {
                int barY = H - 6;
                int barH = 3;
                int barX = 6;
                int barW = W - 12;
                using (Pen border = new Pen(Color.FromArgb(60, 60, 80), 1))
                    g.DrawRectangle(border, barX, barY, barW - 1, barH);
                float fhPct = (float)usage.FiveHourPct;
                int fill = (int)((barW - 2) * Math.Min(fhPct, 100f) / 100f);
                if (fill > 0)
                {
                    using (SolidBrush b = new SolidBrush(QuotaColor(fhPct)))
                        g.FillRectangle(b, barX + 1, barY + 1, fill, barH - 2);
                }
            }
        }
    }
}