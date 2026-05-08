using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace NeuralNexusWidget
{
    public partial class NeuralNexusWidgetServer : IWidgetObject
    {
        // Identity
        public Guid Guid
        {
            get { return new Guid(GetType().Assembly.GetName().Name); }
        }

        public string Name
        {
            get { return "Neural Nexus"; }
        }

        public string Description
        {
            get { return "Orbital network topology monitor — visualizes MCP server health as a living radar interface"; }
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
                return new NeuralNexusWidgetInstance(this, widget_size, instance_guid, ResourcePath);
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
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(10, 10, 18)))
                {
                    g.FillRectangle(bg, 0, 0, size.Width, size.Height);
                }

                // Center node (cyan glow)
                int cx = size.Width / 2;
                int cy = size.Height / 2;
                float r = Math.Min(size.Width, size.Height) / 6f;

                // Glow rings
                for (int ring = 3; ring >= 1; ring--)
                {
                    using (Pen p = new Pen(Color.FromArgb(30 / ring, 0, 180, 220), ring * 2f))
                    {
                        g.DrawEllipse(p, cx - r - ring * 4, cy - r - ring * 4, (r + ring * 4) * 2, (r + ring * 4) * 2);
                    }
                }
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(200, 0, 230, 255)))
                {
                    g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                }

                // Peripheral nodes
                float periphR = Math.Min(size.Width, size.Height) / 3f;
                for (int i = 0; i < 6; i++)
                {
                    float angle = (2f * (float)Math.PI * i / 6f) - (float)Math.PI / 2f;
                    float px = cx + periphR * (float)Math.Cos(angle);
                    float py = cy + periphR * (float)Math.Sin(angle);
                    float pr = 10f;
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(200, 0, 230, 255)))
                    {
                        g.FillEllipse(fill, px - pr, py - pr, pr * 2, pr * 2);
                    }
                }

                // Connection lines
                using (Pen p = new Pen(Color.FromArgb(40, 0, 200, 255), 1f))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = (2f * (float)Math.PI * i / 6f) - (float)Math.PI / 2f;
                        float px = cx + periphR * (float)Math.Cos(angle);
                        float py = cy + periphR * (float)Math.Sin(angle);
                        g.DrawLine(p, cx, cy, px, py);
                    }
                }

                // Title
                using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(200, 200, 220)))
                {
                    g.DrawString("Neural Nexus", titleFont, titleBrush, 10, 8);
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
