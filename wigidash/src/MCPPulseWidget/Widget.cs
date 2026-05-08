using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace MCPPulseWidget
{
    public partial class MCPPulseWidgetServer : IWidgetObject
    {
        // Identity
        public Guid Guid
        {
            get { return new Guid(GetType().Assembly.GetName().Name); }
        }

        public string Name
        {
            get { return "MCP Pulse"; }
        }

        public string Description
        {
            get { return "Live MCP server topology with tool-call activity visualization"; }
        }

        public string Author
        {
            get { return "WigiDash"; }
        }

        public string Website
        {
            get { return "https://github.com/Platano78/wigi-llm"; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        public SdkVersion TargetSdk
        {
            get { return WidgetUtility.CurrentSdkVersion; }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                List<WidgetSize> sizes = new List<WidgetSize>();
                sizes.Add(new WidgetSize(3, 3));
                sizes.Add(new WidgetSize(4, 4));
                return sizes;
            }
        }

        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(3, 3)); }
        }

        // Resources
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
                return new MCPPulseWidgetInstance(this, widget_size, instance_guid, ResourcePath);
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
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // Dark background
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(10, 14, 26)))
                {
                    g.FillRectangle(bg, 0, 0, size.Width, size.Height);
                }

                int cx = size.Width / 2;
                int cy = size.Height / 2;

                // Radar grid
                for (int i = 1; i <= 4; i++)
                {
                    float r = i * (float)(Math.Min(size.Width, size.Height) / 10);
                    using (Pen p = new Pen(Color.FromArgb(15, 30, 50, 70), 1f))
                    {
                        g.DrawEllipse(p, cx - r, cy - r, r * 2f, r * 2f);
                    }
                }

                // Center node (cyan glow)
                float centerR = Math.Min(size.Width, size.Height) / 10f;
                for (int ring = 3; ring >= 1; ring--)
                {
                    using (Pen p = new Pen(Color.FromArgb(30 / ring, 0, 180, 220), ring * 2f))
                    {
                        g.DrawEllipse(p, cx - centerR - ring * 4, cy - centerR - ring * 4,
                            (centerR + ring * 4) * 2, (centerR + ring * 4) * 2);
                    }
                }
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(200, 0, 180, 220)))
                {
                    g.FillEllipse(fill, cx - centerR, cy - centerR, centerR * 2, centerR * 2);
                }
                using (Font titleFont = new Font("Segoe UI", 9, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    StringFormat fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString("MCP", titleFont, titleBrush, cx, cy, fmt);
                }

                // Peripheral nodes
                float periphR = Math.Min(size.Width, size.Height) / 3f;
                for (int i = 0; i < 5; i++)
                {
                    float angle = (2f * (float)Math.PI * i / 5f) - (float)Math.PI / 2f;
                    float px = cx + periphR * (float)Math.Cos(angle);
                    float py = cy + periphR * (float)Math.Sin(angle);
                    float pr = 8f;

                    // Connection line
                    using (Pen p = new Pen(Color.FromArgb(30, 0, 180, 220), 1f))
                    {
                        g.DrawLine(p, cx, cy, px, py);
                    }

                    // Node
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(200, 0, 200, 140)))
                    {
                        g.FillEllipse(fill, px - pr, py - pr, pr * 2, pr * 2);
                    }
                }

                // Title
                using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(200, 180, 200, 220)))
                {
                    g.DrawString("MCP Pulse", titleFont, titleBrush, 10, 8);
                }
            }
            return preview;
        }

        public Bitmap WidgetThumbnail
        {
            get { return GetWidgetPreview(new WidgetSize(3, 3)); }
        }
    }
}
