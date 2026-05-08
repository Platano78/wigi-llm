using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ControlPanel
{
    public partial class ControlPanelWidgetInstance
    {
        // Rendering
        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private readonly object _drawLock = new object();

        // UI Constants
        private const int BUTTON_MARGIN = 8;
        private const int BUTTON_HEIGHT = 32;
        private Dictionary<string, Rectangle> Buttons;

        public ControlPanelWidgetInstance(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resourcePath)
        {
            this.WidgetObject = parent;
            this.Guid = instance_guid;
            this._resourcePath = resourcePath;
            this.WidgetSize = widget_size;

            Size size = widget_size.ToSize();
            BitmapCurrent = new Bitmap(size.Width, size.Height, PixelFormat.Format16bppRgb565);

            InitializeButtons(size.Width, size.Height);
            DrawFrame();
        }

        private void InitializeButtons(int width, int height)
        {
            Buttons = new Dictionary<string, Rectangle>();
            int y = height - 10;
            int buttonWidth = width - BUTTON_MARGIN * 2;

            // Six buttons stacked vertically
            string[] buttonIds = new string[] { "settings", "deploy", "test", "build", "pr", "commit" };
            foreach (string id in buttonIds)
            {
                y -= BUTTON_HEIGHT;
                Buttons[id] = new Rectangle(BUTTON_MARGIN, y, buttonWidth, BUTTON_HEIGHT);
                y -= BUTTON_MARGIN;
            }
        }

        private void DrawFrame()
        {
            if (BitmapCurrent == null) return;

            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Background
                g.Clear(Color.FromArgb(18, 18, 28));

                int y = 8;

                // Header
                using (Font headerFont = new Font("Segoe UI", 16, FontStyle.Bold))
                {
                    DrawCenteredText(g, "Control Panel", headerFont, Color.White, 0, y, BitmapCurrent.Width, 24);
                }
                y += 26;

                // Subtitle
                using (Font subtitleFont = new Font("Segoe UI", 9))
                {
                    DrawCenteredText(g, "Quick Actions", subtitleFont, Color.Purple, 0, y, BitmapCurrent.Width, 14);
                }
                y += 18;

                // Draw buttons
                DrawButton(g, "commit", "Git Commit", Color.Purple);
                DrawButton(g, "pr", "Pull Request", Color.MediumPurple);
                DrawButton(g, "build", "Build", Color.Orchid);
                DrawButton(g, "test", "Test", Color.Purple);
                DrawButton(g, "deploy", "Deploy", Color.MediumPurple);
                DrawButton(g, "settings", "Settings", Color.Orchid);
            }
        }

        private void DrawButton(Graphics g, string id, string text, Color color)
        {
            if (!Buttons.ContainsKey(id)) return;
            Rectangle rect = Buttons[id];

            using (LinearGradientBrush bgBrush = new LinearGradientBrush(rect, Color.FromArgb(50, color), Color.FromArgb(20, color), 90f))
            using (Pen borderPen = new Pen(color, 2))
            using (SolidBrush textBrush = new SolidBrush(color))
            using (Font font = new Font("Segoe UI", 11, FontStyle.Bold))
            {
                FillRoundedRect(g, bgBrush, rect, 6);
                DrawRoundedRect(g, borderPen, rect, 6);

                SizeF textSize = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush,
                    rect.X + (rect.Width - textSize.Width) / 2,
                    rect.Y + (rect.Height - textSize.Height) / 2);
            }
        }

        private void DrawCenteredText(Graphics g, string text, Font font, Color color, int x, int y, int width, int height)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, x + (width - size.Width) / 2, y + (height - size.Height) / 2);
            }
        }

        private void FillRoundedRect(Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            using (GraphicsPath path = GetRoundedPath(bounds, radius))
            {
                g.FillPath(brush, path);
            }
        }

        private void DrawRoundedRect(Graphics g, Pen pen, Rectangle bounds, int radius)
        {
            using (GraphicsPath path = GetRoundedPath(bounds, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath GetRoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type != ClickType.Single) return;

            foreach (KeyValuePair<string, Rectangle> kvp in Buttons)
            {
                if (kvp.Value.Contains(x, y))
                {
                    // TODO: Implement button actions
                    break;
                }
            }
        }

        public void RequestUpdate()
        {
            lock (_drawLock)
            {
                DrawFrame();
                RaiseWidgetUpdated();
            }
        }

        public void Dispose()
        {
            if (BitmapCurrent != null)
            {
                BitmapCurrent.Dispose();
            }
        }

        public void EnterSleep() { }
        public void ExitSleep() { }
        public void SaveSettings() { }
        public void LoadSettings() { }
        public void UpdateSettings()
        {
            lock (_drawLock)
            {
                DrawFrame();
                RaiseWidgetUpdated();
            }
        }
    }
}
