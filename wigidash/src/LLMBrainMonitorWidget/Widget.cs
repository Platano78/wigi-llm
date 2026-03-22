using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMBrainMonitorWidget
{
    public partial class LLMBrainMonitorWidgetServer : IWidgetObject
    {
        // Identity
        public Guid Guid
        {
            get { return new Guid("7ED895E1-9504-4B9E-A080-E2EB68275A0F"); }
        }

        public string Name
        {
            get { return "LLM Brain Monitor"; }
        }

        public string Description
        {
            get { return "Fullscreen LLM inference oscilloscope with context window visualizer"; }
        }

        public string Author
        {
            get { return "WigiDash"; }
        }

        public string Website
        {
            get { return "https://github.com/"; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        // Capabilities
        public SdkVersion TargetSdk
        {
            get { return WidgetUtility.CurrentSdkVersion; }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                List<WidgetSize> sizes = new List<WidgetSize>();
                sizes.Add(new WidgetSize(5, 4));
                return sizes;
            }
        }

        // Functionality
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(5, 4)); }
        }

        // Resources
        public string ResourcePath;
        private Bitmap icon;

        public WidgetError Load(string resource_path)
        {
            this.ResourcePath = resource_path;

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
            LLMBrainMonitorWidgetInstance widget_instance = new LLMBrainMonitorWidgetInstance(
                this, widget_size, instance_guid, ResourcePath);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            return true;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Size size = widget_size.ToSize();
            Bitmap BitmapPreview = new Bitmap(size.Width, size.Height);

            using (Graphics g = Graphics.FromImage(BitmapPreview))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Background
                using (var bgBrush = new SolidBrush(Color.FromArgb(10, 10, 18)))
                {
                    g.FillRectangle(bgBrush, 0, 0, size.Width, size.Height);
                }

                // Header gradient
                using (var titleBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, size.Width, 35),
                    Color.FromArgb(20, 25, 40),
                    Color.FromArgb(12, 15, 28),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(titleBrush, 0, 0, size.Width, 35);
                }

                using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
                using (Font smallFont = new Font("Segoe UI", 7))
                {
                    g.DrawString("GPT-OSS 20B", titleFont, Brushes.White, 10, 8);

                    using (var cyanBrush = new SolidBrush(Color.FromArgb(0, 200, 255)))
                    {
                        g.DrawString("56.6 t/s", titleFont, cyanBrush, 200, 8);
                    }

                    g.DrawString("VRAM 12.4/16GB", smallFont, Brushes.Gray, 360, 12);
                }

                // Fake waveform preview
                using (var wavePen = new Pen(Color.FromArgb(0, 200, 255), 2))
                {
                    Random rnd = new Random(42);
                    int cy = 110;
                    PointF[] pts = new PointF[48];
                    for (int i = 0; i < 48; i++)
                    {
                        float x = 10 + i * 9.6f;
                        float y = cy + (float)(Math.Sin(i * 0.3) * 30 + rnd.NextDouble() * 10 - 5);
                        pts[i] = new PointF(x, y);
                    }
                    g.DrawCurve(wavePen, pts, 0.4f);
                }

                // KV cache bar preview
                int barY = 195;
                using (var barBg = new SolidBrush(Color.FromArgb(30, 30, 40)))
                {
                    g.FillRectangle(barBg, 10, barY, 460, 22);
                }
                using (var purpleBrush = new SolidBrush(Color.FromArgb(100, 60, 180)))
                {
                    g.FillRectangle(purpleBrush, 10, barY, 46, 22);
                }
                using (var blueBrush = new SolidBrush(Color.FromArgb(60, 140, 200)))
                {
                    g.FillRectangle(blueBrush, 56, barY, 230, 22);
                }
                using (var greenBrush = new SolidBrush(Color.FromArgb(80, 200, 120)))
                {
                    g.FillRectangle(greenBrush, 286, barY, 60, 22);
                }

                using (Font smallFont = new Font("Segoe UI", 7))
                using (var cyanBrush = new SolidBrush(Color.FromArgb(0, 200, 255)))
                {
                    g.DrawString("KV Cache: 2764/4096 (68%)", smallFont, cyanBrush, 10, barY - 14);
                    g.DrawString("TTFT: 120ms | Queue: 0 | Batch: 512", smallFont, Brushes.Gray, 10, 245);
                    g.DrawString("Swipe \u2195 Temp | Tap Stop | Hold Kill", smallFont, Brushes.DimGray, 100, 295);
                }
            }

            return BitmapPreview;
        }

        public Bitmap WidgetThumbnail => GetWidgetPreview(SupportedSizes[0]);
    }
}
