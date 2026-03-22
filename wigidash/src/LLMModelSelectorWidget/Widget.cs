using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WigiDashWidgetFramework.WidgetUtility;
using System.IO;

namespace LLMModelSelectorWidget
{
    public partial class LLMModelSelectorWidgetServer : IWidgetObject
    {
        // Resources
        public string ResourcePath;
        private Bitmap icon;

        public WidgetError Load(string resource_path)
        {
            this.ResourcePath = resource_path;

            // Try to load icon
            string iconPath = Path.Combine(ResourcePath, "icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    icon = new Bitmap(iconPath);
                }
                catch
                {
                    icon = null;
                }
            }

            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload()
        {
            icon?.Dispose();
            return WidgetError.NO_ERROR;
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid)
        {
            LLMModelSelectorWidgetInstance widget_instance = new LLMModelSelectorWidgetInstance(
                this, widget_size, instance_guid, ResourcePath);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Color BackColor = Color.FromArgb(30, 30, 30);
            Size size = widget_size.ToSize();
            Bitmap BitmapPreview = new Bitmap(size.Width, size.Height);

            using (Graphics g = Graphics.FromImage(BitmapPreview))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Background
                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillRectangle(brush, 0, 0, size.Width, size.Height);
                }

                // Header
                int padding = 10;
                using (var headerBrush = new SolidBrush(Color.FromArgb(45, 45, 45)))
                {
                    g.FillRectangle(headerBrush, 0, 0, size.Width, 30);
                }

                using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
                {
                    string title = "Model Selector";
                    SizeF titleSize = g.MeasureString(title, titleFont);
                    g.DrawString(title, titleFont, Brushes.White,
                        (size.Width - titleSize.Width) / 2, 8);
                }

                // Dropdown preview
                int y = 40;
                var dropdownRect = new Rectangle(padding, y, size.Width - padding * 2, 30);
                using (var dropdownBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                using (var borderPen = new Pen(Color.FromArgb(0, 122, 204), 1))
                {
                    g.FillRectangle(dropdownBrush, dropdownRect);
                    g.DrawRectangle(borderPen, dropdownRect);

                    using (Font normalFont = new Font("Segoe UI", 8))
                    {
                        g.DrawString("Select Model ▼", normalFont, Brushes.White, dropdownRect.X + 8, dropdownRect.Y + 8);
                    }
                }

                // Button previews
                y += 40;
                int buttonWidth = (size.Width - padding * 5) / 3;
                var loadRect = new Rectangle(padding, y, buttonWidth, 25);
                var unloadRect = new Rectangle(loadRect.Right + padding, y, buttonWidth, 25);
                var statusRect = new Rectangle(unloadRect.Right + padding, y, buttonWidth, 25);

                using (var loadBrush = new SolidBrush(Color.FromArgb(0, 122, 204)))
                using (var unloadBrush = new SolidBrush(Color.FromArgb(204, 50, 50)))
                using (var statusBrush = new SolidBrush(Color.FromArgb(76, 175, 80)))
                using (Font buttonFont = new Font("Segoe UI", 7))
                {
                    g.FillRectangle(loadBrush, loadRect);
                    g.FillRectangle(unloadBrush, unloadRect);
                    g.FillRectangle(statusBrush, statusRect);

                    var textFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("▶ LOAD", buttonFont, Brushes.White, loadRect, textFormat);
                    g.DrawString("■ UNLOAD", buttonFont, Brushes.White, unloadRect, textFormat);
                    g.DrawString("⟳ STATUS", buttonFont, Brushes.White, statusRect, textFormat);
                }

                // Draw icon if available
                if (icon != null)
                {
                    int iconSize = 32;
                    int iconX = (size.Width - iconSize) / 2;
                    int iconY = size.Height - iconSize - 10;
                    g.DrawImage(icon, iconX, iconY, iconSize, iconSize);
                }
            }

            return BitmapPreview;
        }

        public Bitmap WidgetThumbnail => GetWidgetPreview(SupportedSizes[0]);
    }
}
