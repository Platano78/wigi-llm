using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace LLMLauncherWidget
{
    public partial class LLMLauncherWidgetServer : IWidgetObject
    {
        public Guid Guid { get { return new Guid(GetType().Assembly.GetName().Name); } }
        public string Name { get { return "LLM Launcher"; } }
        public string Description { get { return "Launch LLM servers with visual feedback"; } }
        public string Author { get { return "WigiDash"; } }
        public string Website { get { return "https://github.com/Platano78/wigi-llm"; } }
        public Version Version { get { return new Version(1, 0, 0); } }
        public SdkVersion TargetSdk { get { return WidgetUtility.CurrentSdkVersion; } }
        public List<WidgetSize> SupportedSizes { get { return new List<WidgetSize> { new WidgetSize(2, 2) }; } }
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }
        public Bitmap PreviewImage { get { return GetWidgetPreview(new WidgetSize(2, 2)); } }
    }
}
