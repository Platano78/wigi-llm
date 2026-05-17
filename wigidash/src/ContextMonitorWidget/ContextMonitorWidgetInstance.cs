using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public partial class ContextMonitorWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public Bitmap BitmapCurrent;
        private string _resourcePath;
        private readonly Mutex _drawingMutex = new Mutex();
        private const int MutexTimeout = 500;

        private Thread _updateThread;
        private volatile bool _isRunning = false;

        private StatusLineReader _statusLineReader;
        private UsageCacheReader _usageReader;
        private DateTime _lastUsageRefresh = DateTime.MinValue;
        private UsageCacheData _usageCache;
        private static readonly TimeSpan UsageRefreshInterval = TimeSpan.FromSeconds(30);
        private DateTime _lastSessionRefresh = DateTime.MinValue;
        private MultiSessionData _sessionCache;
        private static readonly TimeSpan SessionRefreshInterval = TimeSpan.FromSeconds(1);

        private string _logFile = @"C:\temp\widget_debug.txt";

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

            try
            {
                LogDebug("Initializing Context Monitor multi-session v5 (animated)");
                _statusLineReader = new StatusLineReader();
                _usageReader = new UsageCacheReader();
                _animIntensity = ResolveAnimationIntensity();
                LogDebug("Init complete (" + w + "x" + h + ") intensity=" + _animIntensity);
            }
            catch (Exception ex)
            {
                LogDebug("Init error: " + ex.Message);
            }

            try { DrawFrame(); } catch (Exception ex) { LogDebug("Initial draw: " + ex.Message); }
            try { StartUpdateLoop(); } catch (Exception ex) { LogDebug("StartLoop: " + ex.Message); }
        }

        private void LogDebug(string message)
        {
            try
            {
                File.AppendAllText(_logFile, "[" + DateTime.Now.ToString("HH:mm:ss") + "] CTX: " + message + "\n");
            }
            catch { }
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
                        try { DrawFrame(); RaiseWidgetUpdated(); }
                        finally { try { _drawingMutex.ReleaseMutex(); } catch { } }
                    }
                }
                catch (Exception ex) { LogDebug("UpdateLoop: " + ex.Message); }
                _frameCount++;
                Thread.Sleep(FrameIntervalMs);
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
        public void UpdateSettings() { RequestUpdate(); }
        public void SaveSettings() { }
        public void LoadSettings() { }

        public System.Windows.Controls.UserControl GetSettingsControl()
        {
            return null;
        }
    }
}