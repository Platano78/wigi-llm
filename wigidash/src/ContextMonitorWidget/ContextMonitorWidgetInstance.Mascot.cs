using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetInstance
    {
        private const int MascotW = 24;
        private const int MascotH = 22;
        private const int MascotScale = 2;
        private const int MascotDrawW = MascotW * MascotScale;
        private const int MascotDrawH = MascotH * MascotScale;

        private void DrawMascot(Graphics g, int x, int y, UsageTier tier)
        {
            using (Bitmap small = new Bitmap(MascotW, MascotH, PixelFormat.Format32bppArgb))
            {
                using (Graphics sg = Graphics.FromImage(small))
                {
                    sg.Clear(Color.Transparent);
                    DrawMascotNative(sg, 0, 0, tier);
                }
                InterpolationMode oldInterp = g.InterpolationMode;
                PixelOffsetMode oldPxOff = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, x, y, MascotDrawW, MascotDrawH);
                g.InterpolationMode = oldInterp;
                g.PixelOffsetMode = oldPxOff;
            }
        }

        private void DrawMascotNative(Graphics g, int x, int y, UsageTier tier)
        {
            int f = _frameCount;
            double phase = f / (double)FramesPerSecond * Math.PI * 2;
            int jx = 0, jy = 0;
            if (tier == UsageTier.Alarm)
            {
                jx = (f % 3 == 0) ? -2 : ((f % 3 == 1) ? 2 : 0);
                jy = ((f / 2) % 3 == 0) ? -2 : (((f / 2) % 3 == 1) ? 1 : 0);
            }
            int yOff;
            if (tier == UsageTier.Alarm)
                yOff = (int)Math.Round(Math.Sin(phase * 4) * 2);
            else if (tier == UsageTier.Busy)
                yOff = (int)Math.Round(Math.Sin(phase * 2) * 3);
            else
                yOff = (int)Math.Round(Math.Sin(phase * 0.7) * 2);
            int bx = x + jx;
            int by = y + yOff + jy;
            int antSway;
            if (tier == UsageTier.Alarm)
                antSway = (int)Math.Round(Math.Sin(phase * 4) * 2);
            else if (tier == UsageTier.Busy)
                antSway = (int)Math.Round(Math.Sin(phase * 2) * 2);
            else
                antSway = (int)Math.Round(Math.Sin(phase * 0.8) * 1.5);

            Color body = tier == UsageTier.Alarm ? Color.FromArgb(255, 90, 90)
                       : tier == UsageTier.Busy ? Color.FromArgb(255, 200, 60)
                       : Color.FromArgb(120, 210, 255);
            Color dark = Color.FromArgb(20, 20, 30);

            using (SolidBrush b = new SolidBrush(body))
            {
                g.FillRectangle(b, bx + 2, by + 4, MascotW - 4, MascotH - 6);
                g.FillRectangle(b, bx + 1, by + 6, MascotW - 2, MascotH - 10);
                g.FillRectangle(b, bx + 3, by + 2, MascotW - 6, 3);
            }
            int antBaseX = bx + MascotW / 2;
            int antTipX = antBaseX + antSway;
            using (Pen ant = new Pen(body, 1))
                g.DrawLine(ant, antBaseX, by + 2, antTipX, by);
            using (SolidBrush antTip = new SolidBrush(Color.White))
                g.FillRectangle(antTip, antTipX - 1, by - 1, 2, 2);

            bool blink = tier == UsageTier.Calm && (f / 30) % 4 == 0 && (f % 30) < 5;
            int eyeY = by + 8;
            if (blink)
            {
                using (Pen p = new Pen(dark, 1))
                {
                    g.DrawLine(p, bx + 6, eyeY + 1, bx + 9, eyeY + 1);
                    g.DrawLine(p, bx + MascotW - 10, eyeY + 1, bx + MascotW - 7, eyeY + 1);
                }
            }
            else
            {
                using (SolidBrush w = new SolidBrush(Color.White))
                {
                    g.FillRectangle(w, bx + 6, eyeY, 3, 3);
                    g.FillRectangle(w, bx + MascotW - 9, eyeY, 3, 3);
                }
                using (SolidBrush p = new SolidBrush(dark))
                {
                    int px = tier == UsageTier.Alarm ? 0 : (f / 8) % 3 - 1;
                    g.FillRectangle(p, bx + 7 + px, eyeY + 1, 1, 2);
                    g.FillRectangle(p, bx + MascotW - 8 + px, eyeY + 1, 1, 2);
                }
            }

            using (SolidBrush m = new SolidBrush(dark))
            {
                int mx = bx + MascotW / 2 - 2;
                int my = by + 14;
                if (tier == UsageTier.Calm)
                {
                    g.FillRectangle(m, mx, my, 4, 1);
                    g.FillRectangle(m, mx - 1, my - 1, 1, 1);
                    g.FillRectangle(m, mx + 4, my - 1, 1, 1);
                }
                else if (tier == UsageTier.Busy)
                {
                    g.FillRectangle(m, mx + 1, my, 2, 3);
                }
                else
                {
                    g.FillRectangle(m, mx, my, 1, 1);
                    g.FillRectangle(m, mx + 1, my + 1, 1, 1);
                    g.FillRectangle(m, mx + 2, my, 1, 1);
                    g.FillRectangle(m, mx + 3, my + 1, 1, 1);
                }
            }

            if (tier == UsageTier.Alarm && (f % 16) < 12)
            {
                using (SolidBrush ex = new SolidBrush(Color.Yellow))
                {
                    int exX = bx + MascotW + 2;
                    int exY = by + 1;
                    g.FillRectangle(ex, exX, exY, 2, 6);
                    g.FillRectangle(ex, exX, exY + 8, 2, 2);
                }
            }
        }
    }
}