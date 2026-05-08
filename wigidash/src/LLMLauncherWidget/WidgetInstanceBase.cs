using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Windows.Controls;

namespace LLMLauncherWidget
{
    public partial class LLMLauncherWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }
        private SettingsUserControl _userControl;

        public virtual UserControl GetSettingsControl()
        {
            if (_userControl == null)
            {
                _userControl = new SettingsUserControl(this);
            }
            return _userControl;
        }

        public event WidgetUpdatedEventHandler WidgetUpdated;

        protected void RaiseWidgetUpdated(WidgetUpdatedEventArgs args)
        {
            if (WidgetUpdated != null)
                WidgetUpdated(this, args);
        }
    }
}
