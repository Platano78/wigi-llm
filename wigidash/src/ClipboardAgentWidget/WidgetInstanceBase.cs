using System;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClipboardAgentWidget
{
    public partial class ClipboardAgentWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }

        public event WidgetUpdatedEventHandler WidgetUpdated;

        public void RaiseWidgetUpdated(WidgetUpdatedEventArgs e)
        {
            if (WidgetUpdated != null)
            {
                WidgetUpdated(this, e);
            }
        }

        public System.Windows.Controls.UserControl GetSettingsControl()
        {
            return null;
        }
    }
}