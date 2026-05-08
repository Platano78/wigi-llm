using System;
using System.Collections.Generic;
using System.Drawing;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace ClaudeCodeWidgets.ControlPanel
{
    public partial class ControlPanelWidgetServer
    {
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Guid Guid
        {
            get { return new Guid("CC02BB22-CC33-4D44-EE55-FF6677889900"); }
        }

        public string Name
        {
            get { return "Control Panel"; }
        }

        public string Description
        {
            get { return "Quick actions for Git, build, test, and deploy"; }
        }

        public string Author
        {
            get { return "Claude Code"; }
        }

        public string Website
        {
            get { return "https://github.com/ElmorLabs-WigiDash"; }
        }

        public Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                return new List<WidgetSize>
                {
                    new WidgetSize(2, 2),
                    new WidgetSize(3, 3)
                };
            }
        }

        public Bitmap PreviewImage
        {
            get { return GetWidgetPreview(new WidgetSize(2, 2)); }
        }

        public SdkVersion TargetSdk
        {
            get { return WidgetUtility.CurrentSdkVersion; }
        }
    }
}
