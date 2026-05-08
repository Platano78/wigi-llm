using System;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatsWidget
{
    public partial class LLMStatsWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }

        public virtual UserControl GetSettingsControl() { return null; }

        public event WidgetUpdatedEventHandler WidgetUpdated;

        protected void RaiseWidgetUpdated(WidgetUpdatedEventArgs args)
        {
            if (WidgetUpdated != null)
                WidgetUpdated(this, args);
        }
    }
}
