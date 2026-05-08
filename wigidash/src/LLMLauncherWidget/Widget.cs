using WigiDashWidgetFramework;
using System;
using System.Drawing;
using WigiDashWidgetFramework.WidgetUtility;
using System.IO;

namespace LLMLauncherWidget
{
    public partial class LLMLauncherWidgetServer : IWidgetObject
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
                {
                    _icon = new Bitmap(iconPath);
                }
                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = "Failed to load widget: " + ex.Message;
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
                LLMLauncherWidgetInstance instance = new LLMLauncherWidgetInstance(this, widget_size, instance_guid, ResourcePath);
                return instance;
            }
            catch (Exception ex)
            {
                LastErrorMessage = "Failed to create widget instance: " + ex.Message;
                return null;
            }
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Color BackColor = Color.FromArgb(48, 48, 48);
            Size size = widget_size.ToSize();
            Bitmap preview = new Bitmap(size.Width, size.Height);

            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(BackColor);
                if (_icon != null)
                {
                    int x = (size.Width - _icon.Width) / 2;
                    int y = (size.Height - _icon.Height) / 2;
                    g.DrawImage(_icon, x, y);
                }
                using (Font font = new Font("Arial", 10, FontStyle.Bold))
                using (Brush brush = new SolidBrush(Color.Gray))
                {
                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Far;
                    g.DrawString("LLM LAUNCHER", font, brush, new RectangleF(0, size.Height - 30, size.Width, 25), format);
                }
            }
            return preview;
        }

        public Bitmap WidgetThumbnail
        {
            get { return GetWidgetPreview(SupportedSizes[0]); }
        }
    }
}
