using System;
using System.Drawing;
using System.IO;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatsWidget
{
    public partial class LLMStatsWidgetServer : IWidgetObject
    {
        public string ResourcePath;
        private Bitmap _icon;

        public WidgetError Load(string resource_path)
        {
            try
            {
                this.ResourcePath = resource_path;
                string iconPath = Path.Combine(ResourcePath, "icon.png");
                if (File.Exists(iconPath))
                    _icon = new Bitmap(iconPath);
                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = "Failed to load: " + ex.Message;
                return WidgetError.MANAGER_LOAD_FAIL;
            }
        }

        public WidgetError Unload()
        {
            if (_icon != null) _icon.Dispose();
            return WidgetError.NO_ERROR;
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid)
        {
            try
            {
                return new LLMStatsWidgetInstance(this, widget_size, instance_guid, ResourcePath);
            }
            catch (Exception ex)
            {
                LastErrorMessage = "Failed to create instance: " + ex.Message;
                return null;
            }
        }

        public bool RemoveWidgetInstance(Guid instance_guid) { return true; }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Size size = widget_size.ToSize();
            Bitmap preview = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(Color.FromArgb(20, 20, 28));
                using (Font numFont = new Font("Arial", 22, FontStyle.Bold))
                using (Brush numBrush = new SolidBrush(Color.FromArgb(0, 230, 255)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString("--", numFont, numBrush,
                                 new RectangleF(0, 0, size.Width, size.Height * 0.6f), fmt);
                }
                using (Font lblFont = new Font("Arial", 9, FontStyle.Regular))
                using (Brush lblBrush = new SolidBrush(Color.FromArgb(160, 160, 180)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    g.DrawString("tok/s", lblFont, lblBrush,
                                 new RectangleF(0, size.Height * 0.55f, size.Width, 20), fmt);
                    g.DrawString("LLM STATS", lblFont, lblBrush,
                                 new RectangleF(0, size.Height - 18, size.Width, 16), fmt);
                }
            }
            return preview;
        }

        public Bitmap WidgetThumbnail
        {
            get { return GetWidgetPreview(new WidgetSize(1, 1)); }
        }
    }
}
