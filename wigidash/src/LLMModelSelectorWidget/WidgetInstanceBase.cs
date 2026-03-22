using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Windows.Controls;
using System.Collections.Generic;

namespace LLMModelSelectorWidget
{
    /// <summary>
    /// Widget instance base class - properties and settings UI.
    /// This is a partial class - see WidgetInstance.cs for implementation.
    /// </summary>
    public partial class LLMModelSelectorWidgetInstance : IWidgetInstance
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

        // Events
        public event WidgetUpdatedEventHandler WidgetUpdated;

        // Settings properties
        public string RouterUrl { get; set; } = "http://localhost:8081";
        public int PollingInterval { get; set; } = 3000;  // 3s update interval

        // Current state
        public RouterStatus CurrentStatus { get; set; }
        public bool IsLoading { get; set; } = false;
        public string LoadingMessage { get; set; } = "";
        public bool ShowDropdown { get; set; } = false;
        public int SelectedIndex { get; set; } = -1;
        public string SelectedModel { get; set; } = "";
    }
}
