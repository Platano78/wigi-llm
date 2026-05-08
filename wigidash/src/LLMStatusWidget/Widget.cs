using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatusWidget
{
    /// <summary>
    /// Widget server implementation - lifecycle and factory methods.
    /// This is a partial class - see WidgetBase.cs for properties.
    /// </summary>
    public partial class LLMStatusWidgetServer : IWidgetObject
    {
        // Resources
        public string ResourcePath;
        private Bitmap _icon;

        public WidgetError Load(string resource_path)
        {
            this.ResourcePath = resource_path;

            // Try to load icon, fallback to generated if not found
            string iconPath = Path.Combine(ResourcePath, "icon.png");
            if (File.Exists(iconPath))
            {
                _icon = new Bitmap(iconPath);
            }
            else
            {
                _icon = GenerateDefaultIcon();
            }

            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload()
        {
            if (_icon != null) _icon.Dispose();
            return WidgetError.NO_ERROR;
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid)
        {
            var widget_instance = new LLMStatusWidgetInstance(this, widget_size, instance_guid, ResourcePath);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            // The framework handles cleanup via IWidgetInstance.Dispose()
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Color BackColor = Color.FromArgb(30, 30, 30);
            Size size = widget_size.ToSize();
            Bitmap BitmapPreview = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(BitmapPreview))
            {
                g.Clear(BackColor);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                
                if (_icon != null)
                {
                    // Center the icon
                    int x = (size.Width - _icon.Width) / 2;
                    int y = (size.Height - _icon.Height) / 2;
                    g.DrawImage(_icon, x, y, _icon.Width, _icon.Height);
                }
                else
                {
                    // Fallback text
                    using (Font f = new Font("Arial", 14, FontStyle.Bold))
                    {
                        string text = "LLM";
                        SizeF textSize = g.MeasureString(text, f);
                        g.DrawString(text, f, Brushes.Lime,
                            (size.Width - textSize.Width) / 2,
                            (size.Height - textSize.Height) / 2);
                    }
                }
            }
            return BitmapPreview;
        }

        public Bitmap WidgetThumbnail { get { return GetWidgetPreview(SupportedSizes[0]); } }

        private Bitmap GenerateDefaultIcon()
        {
            Bitmap bmp = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 30, 30));
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw a brain/AI icon
                using (Pen p = new Pen(Color.Cyan, 3))
                {
                    g.DrawEllipse(p, 20, 20, 60, 60);
                    g.DrawLine(p, 50, 30, 50, 70);
                    g.DrawLine(p, 30, 50, 70, 50);
                }

                using (Font f = new Font("Arial", 10, FontStyle.Bold))
                {
                    g.DrawString("LLM", f, Brushes.White, 35, 80);
                }
            }
            return bmp;
        }
    }
}
