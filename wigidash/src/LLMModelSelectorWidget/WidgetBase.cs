using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMModelSelectorWidget
{
    /// <summary>
    /// LLM Model Selector Widget - manual model selection with load/unload controls
    /// </summary>
    public partial class LLMModelSelectorWidgetServer : IWidgetObject
    {
        // Identity - GUID: B2C3D4E5-F6A7-8901-2345-678901BCDEF0
        public Guid Guid
        {
            get { return new Guid("B2C3D4E5-F6A7-8901-2345-678901BCDEF0"); }
        }

        public string Name
        {
            get { return "LLM Model Selector"; }
        }

        public string Description
        {
            get { return "Dropdown for manual model selection with load/unload controls"; }
        }

        public string Author
        {
            get { return "WigiDash"; }
        }

        public string Website
        {
            get { return "https://github.com/"; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        // Capabilities
        public SdkVersion TargetSdk
        {
            get { return WidgetUtility.CurrentSdkVersion; }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                List<WidgetSize> widget_size_list = new List<WidgetSize>();
                // Support 1x1 through 3x3
                for (int y = 1; y <= 3; y++)
                {
                    for (int x = 1; x <= 3; x++)
                    {
                        widget_size_list.Add(new WidgetSize(x, y));
                    }
                }
                return widget_size_list;
            }
        }

        // Functionality
        public IWidgetManager WidgetManager { get; set; }

        // Error handling
        public string LastErrorMessage { get; set; }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(1, 1)); }
        }
    }
}
