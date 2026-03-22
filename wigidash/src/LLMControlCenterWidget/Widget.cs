using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMControlCenterWidget
{
    public partial class LLMControlCenterWidgetServer : IWidgetObject
    {
        // Identity
        public Guid Guid
        {
            get { return new Guid("57AA91B9-1E54-45CA-A05C-89326A8FBBDD"); }
        }

        public string Name
        {
            get { return "LLM Control Center"; }
        }

        public string Description
        {
            get { return "Unified LLM monitoring and control - model selection, VRAM, port health, tokens/sec"; }
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
                sizes.Add(new WidgetSize(2, 2));
                sizes.Add(new WidgetSize(3, 2));
                sizes.Add(new WidgetSize(3, 3));
                return sizes;
            }
        }

        // Functionality
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(3, 2)); }
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
            LLMControlCenterWidgetInstance widget_instance = new LLMControlCenterWidgetInstance(
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
                using (var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 30)))
                {
                    g.FillRectangle(bgBrush, 0, 0, size.Width, size.Height);
                }

                // Title bar gradient
                using (var titleBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, size.Width, 30),
                    Color.FromArgb(20, 40, 80),
                    Color.FromArgb(15, 25, 50),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(titleBrush, 0, 0, size.Width, 30);
                }

                using (Font titleFont = new Font("Segoe UI", 10, FontStyle.Bold))
                {
                    g.DrawString("LLM Control Center", titleFont, Brushes.White, 10, 7);
                }

                // Model section preview
                int y = 38;
                using (Font normalFont = new Font("Segoe UI", 8))
                using (Font smallFont = new Font("Segoe UI", 7))
                {
                    g.DrawString("Active Model", normalFont, Brushes.Gray, 10, y);
                    y += 18;

                    // Dropdown preview
                    var dropdownRect = new Rectangle(10, y, size.Width / 2 - 20, 22);
                    using (var dropBrush = new SolidBrush(Color.FromArgb(40, 40, 50)))
                    using (var borderPen = new Pen(Color.FromArgb(60, 120, 200), 1))
                    {
                        g.FillRectangle(dropBrush, dropdownRect);
                        g.DrawRectangle(borderPen, dropdownRect);
                    }
                    using (var cyanBrush = new SolidBrush(Color.FromArgb(100, 200, 255)))
                    {
                        g.DrawString("qwen3-14b", smallFont, cyanBrush, dropdownRect.X + 4, dropdownRect.Y + 4);
                    }

                    // VRAM bar preview
                    int barX = size.Width / 2 + 10;
                    int barW = size.Width - barX - 15;
                    g.DrawString("VRAM", normalFont, Brushes.Gray, barX, y - 18);
                    var barRect = new Rectangle(barX, y, barW, 14);
                    using (var barBg = new SolidBrush(Color.FromArgb(40, 40, 50)))
                    {
                        g.FillRectangle(barBg, barRect);
                    }
                    int fillW = (int)(barW * 0.78f);
                    using (var fillBrush = new LinearGradientBrush(
                        new Rectangle(barX, y, fillW, 14),
                        Color.FromArgb(60, 180, 60),
                        Color.FromArgb(40, 140, 40),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(fillBrush, barX, y, fillW, 14);
                    }
                    g.DrawString("12.4/16 GB", smallFont, Brushes.White, barX + 2, y + 1);

                    y += 32;

                    // Status section preview
                    using (var greenBrush = new SolidBrush(Color.FromArgb(80, 200, 80)))
                    {
                        g.FillEllipse(greenBrush, 10, y + 3, 8, 8);
                        g.DrawString("Router: Online", normalFont, Brushes.White, 22, y);
                        g.DrawString("56.6 t/s", normalFont, greenBrush, size.Width - 70, y);
                    }
                    y += 20;

                    // Port indicators
                    g.DrawString("Servers:", smallFont, Brushes.Gray, 10, y);
                    int dotX = 60;
                    int[] ports = { 8081, 8083, 8085 };
                    foreach (int port in ports)
                    {
                        using (var dotBrush = new SolidBrush(port == 8085 ? Color.FromArgb(180, 60, 60) : Color.FromArgb(80, 200, 80)))
                        {
                            g.FillEllipse(dotBrush, dotX, y + 3, 7, 7);
                        }
                        g.DrawString(port.ToString(), smallFont, Brushes.Gray, dotX + 10, y);
                        dotX += 55;
                    }

                    y += 26;

                    // Button bar preview
                    string[] buttons = { "Load", "Unload", "Switch", "Refresh" };
                    int btnPad = 8;
                    int btnW = (size.Width - btnPad * 5) / 4;
                    int btnX = btnPad;
                    using (var btnBrush = new SolidBrush(Color.FromArgb(40, 60, 80)))
                    using (var btnFont = new Font("Segoe UI", 7, FontStyle.Bold))
                    {
                        var textFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        foreach (string btn in buttons)
                        {
                            var btnRect = new Rectangle(btnX, y, btnW, 22);
                            g.FillRectangle(btnBrush, btnRect);
                            g.DrawString(btn, btnFont, Brushes.White, btnRect, textFormat);
                            btnX += btnW + btnPad;
                        }
                    }
                }
            }

            return BitmapPreview;
        }

        public Bitmap WidgetThumbnail => GetWidgetPreview(SupportedSizes[0]);
    }
}
