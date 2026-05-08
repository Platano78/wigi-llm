using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Drawing;
using System.Windows.Controls;

namespace LLMStatusWidget
{
    /// <summary>
    /// Server status states for LLM monitoring.
    /// </summary>
    public enum ServerStatus
    {
        Offline,  // Server not responding
        Loading,  // Server responding but no model loaded
        Online    // Server responding with model loaded
    }

    /// <summary>
    /// Widget instance base class - properties and settings UI.
    /// This is a partial class - see WidgetInstance.cs for implementation.
    /// </summary>
    public partial class LLMStatusWidgetInstance : IWidgetInstance
    {
        public IWidgetObject WidgetObject { get; set; }
        public Guid Guid { get; set; }
        public WidgetSize WidgetSize { get; set; }

        public virtual UserControl GetSettingsControl()
        {
            return null;
        }

        // Events
        public event WidgetUpdatedEventHandler WidgetUpdated;

        // Settings properties
        public string ServerUrl { get; set; }
        public string OnlineImagePath { get; set; }
        public string OfflineImagePath { get; set; }
        public int PollingIntervalMs { get; set; }

        // Multi-port monitoring (dynamic - easily add/remove ports)
        // 8083=OrchGPU, 8085=OrchCPU (CPU orchestrator for VRAM efficiency)
        public int[] MonitoredPorts { get; set; }
        public string[] PortLabels { get; set; }
        public ServerStatus[] PortStatus { get; set; }

        // Background image path
        public string BackgroundImagePath { get; set; }

        protected void InitializeDefaults()
        {
            ServerUrl = "http://localhost:8080/health";
            OnlineImagePath = "";
            OfflineImagePath = "";
            PollingIntervalMs = 5000;
            MonitoredPorts = new int[] { 8080, 8081, 8083, 8085, 1234, 8765, 3457 };
            PortLabels = new string[] { "Genesis", "LLM", "OrchGPU", "OrchCPU", "LMStudio", "REST", "Gateway" };
            PortStatus = new ServerStatus[7];
            BackgroundImagePath = "";
        }
    }
}
