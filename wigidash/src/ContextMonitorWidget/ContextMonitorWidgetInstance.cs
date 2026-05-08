using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public class ContextMonitorWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 100;

        private Thread _updateThread;
        private volatile bool _isRunning = false;

        public ContextMonitorWidgetInstance(IWidgetObject parent, WidgetSize widget_size, Guid instance_guid, string resource_path)
        {
            WidgetObject = parent;
            WidgetSize = widget_size;
            Guid = instance_guid;
            _resourcePath = resource_path;
            Size size = widget_size.ToSize();
            int w = size.Width > 0 ? size.Width : 192;
            int h = size.Height > 0 ? size.Height : 192;
            BitmapCurrent = new Bitmap(w, h, PixelFormat.Format16bppRgb565);
            try { DrawFrame(); } catch { }
            try { StartUpdateLoop(); } catch { }
        }

        private void StartUpdateLoop()
        {
            _isRunning = true;
            _updateThread = new Thread(UpdateLoop);
            _updateThread.IsBackground = true;
            _updateThread.Start();
        }

        private void UpdateLoop()
        {
            while (_isRunning)
            {
                try
                {
                    if (_drawingMutex.WaitOne(MutexTimeout))
                    {
                        try
                        {
                            DrawFrame();
                            RaiseWidgetUpdated();
                        }
                        finally
                        {
                            try { _drawingMutex.ReleaseMutex(); } catch { }
                        }
                    }
                }
                catch { }
                Thread.Sleep(2000);
            }
        }

        private void DrawFrame()
        {
            if (BitmapCurrent == null) return;
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(Color.Black);
                using (Font headerFont = new Font("Arial", 8, FontStyle.Bold))
                {
                    g.DrawString("Context Monitor", headerFont, Brushes.White, 2, 2);
                }
                using (Font subFont = new Font("Arial", 7))
                {
                    g.DrawString("Token Usage", subFont, Brushes.Gray, 2, 15);
                }
                int barWidth = (int)(BitmapCurrent.Width * 0.7);
                g.FillRectangle(Brushes.Green, 2, 30, barWidth, 10);
            }
        }

        protected void RaiseWidgetUpdated()
        {
            if (WidgetUpdated != null && BitmapCurrent != null)
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
                e.WaitMax = 1000;
                e.WidgetBitmap = BitmapCurrent;
                WidgetUpdated.Invoke(this, e);
            }
        }

        public void ClickEvent(ClickType click_type, int x, int y) { }

        public void RequestUpdate()
        {
            if (_drawingMutex.WaitOne(MutexTimeout))
            {
                try { DrawFrame(); RaiseWidgetUpdated(); }
                finally { try { _drawingMutex.ReleaseMutex(); } catch { } }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            if (_updateThread != null && _updateThread.IsAlive)
            {
                try { _updateThread.Join(1000); } catch { }
            }
            if (BitmapCurrent != null) BitmapCurrent.Dispose();
        }

        public void EnterSleep() { _isRunning = false; }
        public void ExitSleep() { if (!_isRunning) StartUpdateLoop(); }
        public void UpdateSettings() { }
        public void SaveSettings() { }
        public void LoadSettings() { }

        public System.Windows.Controls.UserControl GetSettingsControl()
        {
            return null;
        }
    }
}