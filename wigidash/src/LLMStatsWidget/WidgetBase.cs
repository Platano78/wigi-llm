using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMStatsWidget
{
    public partial class LLMStatsWidgetServer : IWidgetObject
    {
        public Guid Guid { get { return new Guid(GetType().Assembly.GetName().Name); } }
        public string Name { get { return "LLM Stats"; } }
        public string Description { get { return "Live tokens/sec readout for the loaded model"; } }
        public string Author { get { return "WigiDash"; } }
        public string Website { get { return "https://github.com/Platano78/wigi-llm"; } }
        public Version Version { get { return new Version(1, 0, 0); } }
        public SdkVersion TargetSdk { get { return WidgetUtility.CurrentSdkVersion; } }
        public List<WidgetSize> SupportedSizes
        {
            get { return new List<WidgetSize> { new WidgetSize(1, 1), new WidgetSize(2, 1) }; }
        }
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }
        public Bitmap PreviewImage { get { return GetWidgetPreview(new WidgetSize(1, 1)); } }
    }
}
