using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ControlPanel
{
    /// <summary>
    /// Widget server implementation for Control Panel - lifecycle and factory methods.
    /// This is a partial class - see WidgetBase.cs for properties.
    /// </summary>
    public partial class ControlPanelWidgetServer : IWidgetObject
    {
        // Resources
        public string ResourcePath;
        private Bitmap _icon;

        /// <summary>
        /// Loads the widget server with the given resource path.
        /// </summary>
        /// <param name="resource_path">Path to the resources directory.</param>
        /// <returns>WidgetError indicating success or failure.</returns>
        public WidgetError Load(string resource_path)
        {
            try
            {
                this.ResourcePath = resource_path;
                _icon = GenerateDefaultIcon();
                return WidgetError.NO_ERROR;
            }
            catch (Exception)
            {
                return WidgetError.NO_ERROR;
            }
        }

        /// <summary>
        /// Unloads the widget server and disposes resources.
        /// </summary>
        /// <returns>WidgetError indicating success or failure.</returns>
        public WidgetError Unload()
        {
            try
            {
                if (_icon != null) { _icon.Dispose(); };
                return WidgetError.NO_ERROR;
            }
            catch (Exception)
            {
                return WidgetError.NO_ERROR;
            }
        }

        /// <summary>
        /// Creates a new widget instance with the given parameters.
        /// </summary>
        /// <param name="widget_size">The requested size of the widget.</param>
        /// <param name="instance_guid">Unique identifier for this instance.</param>
        /// <returns>A new IWidgetInstance object.</returns>
        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid)
        {
            var widget_instance = new ControlPanelWidgetInstance(this, widget_size, instance_guid, ResourcePath);
            return widget_instance;
        }

        /// <summary>
        /// Removes a widget instance by its GUID.
        /// </summary>
        /// <param name="instance_guid">The unique identifier of the instance to remove.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public bool RemoveWidgetInstance(Guid instance_guid)
        {
            // Implementation would handle cleanup if needed
            return true;
        }

        /// <summary>
        /// Generates a preview bitmap for the widget at the specified size.
        /// </summary>
        /// <param name="widget_size">The size to render the preview at.</param>
        /// <returns>A bitmap representing the widget preview.</returns>
        public Bitmap GetWidgetPreview(WidgetSize widget_size)
        {
            Color BackColor = Color.FromArgb(20, 20, 30);
            Color AccentColor = Color.Purple;
            Size size = widget_size.ToSize();
            Bitmap BitmapPreview = new Bitmap(size.Width, size.Height);
            
            using (Graphics g = Graphics.FromImage(BitmapPreview))
            {
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw title and subtitle
                using (Font titleFont = new Font("Segoe UI", 14, FontStyle.Bold))
                using (Font subFont = new Font("Segoe UI", 10))
                {
                    g.DrawString("Control Panel", titleFont, Brushes.White, 10, 10);
                    g.DrawString("Quick Actions", subFont, new SolidBrush(AccentColor), 10, 35);
                }

                // Draw grid icon
                using (Pen p = new Pen(AccentColor, 2))
                {
                    int iconSize = 40;
                    int x = size.Width - iconSize - 10;
                    int y = 10;
                    
                    // Draw rectangle border
                    g.DrawRectangle(p, x, y, iconSize, iconSize);
                    
                    // Draw internal grid lines
                    g.DrawLine(p, x + iconSize/3, y, x + iconSize/3, y + iconSize);
                    g.DrawLine(p, x + 2*iconSize/3, y, x + 2*iconSize/3, y + iconSize);
                    g.DrawLine(p, x, y + iconSize/3, x + iconSize, y + iconSize/3);
                    g.DrawLine(p, x, y + 2*iconSize/3, x + iconSize, y + 2*iconSize/3);
                }
            }
            
            return BitmapPreview;
        }

        /// <summary>
        /// Gets the thumbnail representation of the widget.
        /// </summary>
        public Bitmap WidgetThumbnail
        {
            get { return GetWidgetPreview(SupportedSizes[0]); }
        }

        /// <summary>
        /// Generates the default icon for the widget.
        /// </summary>
        /// <returns>A 100x100 bitmap with grid pattern.</returns>
        private Bitmap GenerateDefaultIcon()
        {
            Bitmap bmp = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Color BackColor = Color.FromArgb(20, 20, 30);
                Color AccentColor = Color.Purple;
                
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw grid pattern
                using (Pen p = new Pen(AccentColor, 2))
                {
                    int margin = 15;
                    int cellSize = 20;
                    
                    // Draw outer rectangle
                    g.DrawRectangle(p, margin, margin, 70, 70);
                    
                    // Draw internal grid lines
                    for (int i = 1; i < 3; i++)
                    {
                        // Vertical lines
                        g.DrawLine(p, margin + i * cellSize, margin, margin + i * cellSize, margin + 70);
                        // Horizontal lines
                        g.DrawLine(p, margin, margin + i * cellSize, margin + 70, margin + i * cellSize);
                    }
                }

                // Draw text
                using (Font f = new Font("Arial", 8, FontStyle.Bold))
                {
                    g.DrawString("CTRL", f, Brushes.White, 30, 20);
                    g.DrawString("PANEL", f, new SolidBrush(AccentColor), 25, 60);
                }
            }
            return bmp;
        }
    }
}