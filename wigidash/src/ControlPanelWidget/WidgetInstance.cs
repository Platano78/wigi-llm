using System;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ControlPanel
{
    public partial class ControlPanelWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

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

        public System.Windows.Controls.UserControl GetSettingsControl()
        {
            return null;
        }
    }
}
