using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WigiDashWidgetFramework.WidgetUtility;
using System.IO;

namespace LLMRouterStatusWidget
{
    public partial class LLMRouterStatusWidgetServer : IWidgetObject
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
            LLMRouterStatusWidgetInstance widget_instance = new LLMRouterStatusWidgetInstance(
                this, widget_size, instance_guid, ResourcePath);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Color BackColor = Color.FromArgb(20, 20, 30);
            Size size = widget_size.ToSize();
            Bitmap BitmapPreview = new Bitmap(size.Width, size.Height);

            using (Graphics g = Graphics.FromImage(BitmapPreview))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Gradient background
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, size.Width, size.Height),
                    Color.FromArgb(30, 30, 50), BackColor, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 0, 0, size.Width, size.Height);
                }

                // Draw preview content
                int padding = 10;
                int centerY = size.Height / 3;

                // Draw a simplified loading indicator
                using (Font titleFont = new Font("Arial", 12, FontStyle.Bold))
                using (Font statusFont = new Font("Arial", 9))
                {
                    string title = "LLM Router";
                    SizeF titleSize = g.MeasureString(title, titleFont);
                    g.DrawString(title, titleFont, Brushes.White,
                        (size.Width - titleSize.Width) / 2, padding);

                    // Draw a sample status indicator
                    int circleSize = 20;
                    int circleX = size.Width / 2 - circleSize / 2;
                    int circleY = centerY;

                    // Blue gradient circle (loading state)
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(circleX, circleY, circleSize, circleSize);
                        using (var gradBrush = new PathGradientBrush(path))
                        {
                            gradBrush.CenterColor = Color.FromArgb(200, 100, 150, 255);
                            gradBrush.SurroundColors = new Color[] { Color.FromArgb(150, 50, 100, 200) };
                            g.FillEllipse(gradBrush, circleX, circleY, circleSize, circleSize);
                        }
                    }

                    // Status text
                    string status = "Loading...";
                    SizeF statusSize = g.MeasureString(status, statusFont);
                    g.DrawString(status, statusFont, Brushes.LightGray,
                        (size.Width - statusSize.Width) / 2, circleY + circleSize + 10);
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
