using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatusWidget
{
    /// <summary>
    /// Widget server class - handles metadata and factory operations.
    /// This is a partial class - see Widget.cs for implementation methods.
    /// </summary>
    public partial class LLMStatusWidgetServer : IWidgetObject
    {
        // Identity
        public Guid Guid
        {
            get { return new Guid(GetType().Assembly.GetName().Name); }
        }

        public string Name
        {
            get { return "LLM Monitor"; }
        }

        public string Description
        {
            get { return "Monitors local LLM server status (vLLM, llama.cpp, etc.)"; }
        }

        public string Author
        {
            get { return "Aldwin"; }
        }

        public string Website
        {
            get { return "https://github.com/ElmorLabs-WigiDash"; }
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
                return new List<WidgetSize>()
                {
                    new WidgetSize(1, 1),
                    new WidgetSize(2, 1),
                    new WidgetSize(2, 2)
                };
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
