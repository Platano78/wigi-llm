using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WigiDashWidgetFramework.WidgetUtility;
using System.IO;
using System.Collections.Generic;

namespace IntegrationHealthWidget
{
    public class IntegrationHealthWidgetServer : IWidgetObject
    {
        public Guid Guid { get { return new Guid(GetType().Assembly.GetName().Name); } }
        public string Name { get { return "Integration Health"; } }
        public string Description { get { return "Unified service health monitoring and control"; } }
        public string Author { get { return ""; } }
        public string Website { get { return ""; } }
        public Version Version { get { return new Version(1, 0, 0); } }
        public List<WidgetSize> SupportedSizes
        {
            get
            {
                return new List<WidgetSize>
                {
                    new WidgetSize(2, 2),
                    new WidgetSize(3, 2)
                };
            }
        }
        public Bitmap WidgetThumbnail { get { return GetWidgetPreview(SupportedSizes[0]); } }
        public IWidgetManager WidgetManager { get; set; }
        public SdkVersion TargetSdk { get { return WidgetUtility.CurrentSdkVersion; } }
        public string LastErrorMessage { get; set; }
        public Bitmap PreviewImage { get { return GetWidgetPreview(new WidgetSize(2, 2)); } }

        public string ResourcePath;
        private Bitmap _icon;

        public WidgetError Load(string resource_path)
        {
            this.ResourcePath = resource_path;

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
            return new IntegrationHealthWidgetInstance(this, widget_size, instance_guid, ResourcePath);
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Size size = widget_size.ToSize();
            Bitmap preview = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(20, 20, 30));

                if (_icon != null)
                {
                    int x = (size.Width - _icon.Width) / 2;
                    int y = (size.Height - _icon.Height) / 2;
                    g.DrawImage(_icon, x, y, _icon.Width, _icon.Height);
                }
                else
                {
                    // Draw a preview that hints at the service grid layout
                    using (var titleBrush = new LinearGradientBrush(
                        new Rectangle(0, 0, size.Width, 28),
                        Color.FromArgb(40, 60, 80),
                        Color.FromArgb(30, 45, 60),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(titleBrush, 0, 0, size.Width, 28);
                    }

                    using (Font f = new Font("Arial", 9, FontStyle.Bold))
                    {
                        g.DrawString("Integration Health", f, Brushes.White, 8, 5);
                    }

                    // Draw 4 placeholder service rows
                    string[] labels = { "MCP Gateway", "REST Bridge", "Director", "CF Tunnel" };
                    using (Font f = new Font("Arial", 8))
                    {
                        for (int i = 0; i < labels.Length; i++)
                        {
                            int y = 35 + i * 22;
                            using (var dot = new SolidBrush(i < 3 ? Color.FromArgb(80, 200, 80) : Color.FromArgb(180, 60, 60)))
                            {
                                g.FillEllipse(dot, 12, y + 2, 8, 8);
                            }
                            g.DrawString(labels[i], f, Brushes.LightGray, 26, y);
                        }
                    }

                    using (var pen = new Pen(Color.FromArgb(60, 80, 100), 1))
                    {
                        g.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
                    }
                }
            }
            return preview;
        }

        private Bitmap GenerateDefaultIcon()
        {
            Bitmap bmp = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Shield/health icon
                using (Pen p = new Pen(Color.Cyan, 3))
                {
                    // Shield shape
                    g.DrawArc(p, 25, 15, 50, 40, 180, 180);
                    g.DrawLine(p, 25, 35, 25, 60);
                    g.DrawLine(p, 75, 35, 75, 60);
                    g.DrawLine(p, 25, 60, 50, 78);
                    g.DrawLine(p, 75, 60, 50, 78);
                }

                // Health cross
                using (Pen p = new Pen(Color.FromArgb(80, 200, 80), 3))
                {
                    g.DrawLine(p, 50, 30, 50, 55);
                    g.DrawLine(p, 38, 42, 62, 42);
                }

                using (Font f = new Font("Arial", 7, FontStyle.Bold))
                {
                    g.DrawString("HEALTH", f, Brushes.White, 28, 82);
                }
            }
            return bmp;
        }
    }
}
