using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetServer : IWidgetObject
    {
        // Resources
        public string ResourcePath;
        private Bitmap _icon;

        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Guid Guid
        {
            get { return new Guid(GetType().Assembly.GetName().Name); }
        }

        public string Name
        {
            get { return "Context Monitor"; }
        }

        public string Description
        {
            get { return "Monitor Claude Code token budget and context usage"; }
        }

        public string Author
        {
            get { return "Claude Code"; }
        }

        public string Website
        {
            get { return "https://github.com/Platano78/wigi-llm"; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                return new List<WidgetSize>
                {
                    new WidgetSize(2, 2),
                    new WidgetSize(3, 3)
                };
            }
        }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(2, 2)); }
        }

        public SdkVersion TargetSdk
        {
            get { return WidgetUtility.CurrentSdkVersion; }
        }

        public Bitmap WidgetThumbnail
        {
            get { return GetWidgetPreview(SupportedSizes[0]); }
        }

        public WidgetError Load(string resource_path)
        {
            this.ResourcePath = resource_path;
            _icon = GenerateDefaultIcon();
            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload()
        {
            if (_icon != null) _icon.Dispose();
            return WidgetError.NO_ERROR;
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid)
        {
            return new ContextMonitorWidgetInstance(this, widget_size, instance_guid, ResourcePath);
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Size size = widget_size.ToSize();
            Bitmap bitmap = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Font font = new Font("Segoe UI", 12, FontStyle.Bold))
                {
                    g.DrawString("Context\nMonitor", font, Brushes.White, 10, 10);
                }
            }
            return bitmap;
        }

        private Bitmap GenerateDefaultIcon()
        {
            Bitmap icon = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(icon))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.DeepSkyBlue, 4, 4, 24, 24);
            }
            return icon;
        }
    }
}
