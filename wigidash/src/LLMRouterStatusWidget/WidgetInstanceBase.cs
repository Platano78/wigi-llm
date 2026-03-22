using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Windows.Controls;

namespace LLMRouterStatusWidget
{
    /// <summary>
    /// Model loading states for the router.
    /// </summary>
    public enum ModelLoadState
    {
        Idle,       // No model loaded, no activity
        Loading,    // Model is being loaded
        Switching,  // Unloading old model and loading new one
        Ready,      // Model loaded and ready
        Error       // Load failed
    }

    /// <summary>
    /// Model information from router API.
    /// </summary>
    public class ModelInfo
    {
        public string Name { get; set; }
        public ModelLoadState State { get; set; }
        public int ProgressPercent { get; set; }
        public long VramUsedMB { get; set; }
        public long VramTotalMB { get; set; }
        public int PendingRequests { get; set; }
        public int LastSwitchSecondsAgo { get; set; }
    }

    /// <summary>
    /// Widget instance base class - properties and settings UI.
    /// This is a partial class - see WidgetInstance.cs for implementation.
    /// </summary>
    public partial class LLMRouterStatusWidgetInstance : IWidgetInstance
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
        public string RouterUrl { get; set; } = "http://localhost:8081/v1/models";
        public int PollingIntervalActive { get; set; } = 2000;  // 2s during activity
        public int PollingIntervalIdle { get; set; } = 10000;   // 10s when idle

        // Current state
        public ModelInfo CurrentModel { get; set; }
    }
}
