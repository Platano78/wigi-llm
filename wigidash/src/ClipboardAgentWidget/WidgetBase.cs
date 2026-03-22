using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClipboardAgentWidget
{
    public partial class ClipboardAgentWidgetServer : IWidgetObject
    {
        public Guid Guid { get { return new Guid(GetType().Assembly.GetName().Name); } }
        public string Name { get { return "Clipboard Agent"; } }
        public string Description { get { return "AI clipboard toolkit: LLM actions, format, transform, snippets, token counter"; } }
        public string Author { get { return "WigiDash"; } }
        public string Website { get { return "https://elmorlabs.com/"; } }
        public Version Version { get { return new Version(1, 0, 0); } }
        public SdkVersion TargetSdk { get { return WidgetUtility.CurrentSdkVersion; } }
        public List<WidgetSize> SupportedSizes { get { return new List<WidgetSize> { new WidgetSize(5, 2), new WidgetSize(4, 2), new WidgetSize(2, 2) }; } }
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }
        public Bitmap PreviewImage { get { return GetWidgetPreview(new WidgetSize(5, 2)); } }
    }
}