using System;
using System.Drawing;

namespace LLMLauncherWidget
{
    public enum LauncherState
    {
        Ready,
        Launching,
        Running,
        Stopping,
        Error
    }

    public class LauncherButton
    {
        public string DisplayName { get; set; }
        public string ScriptPath { get; set; }
        public string IconActivePath { get; set; }
        public string IconInactivePath { get; set; }
        public int AssociatedPort { get; set; }
        public LauncherState State { get; set; }
        public Rectangle Bounds { get; set; }
        public string LastError { get; set; }
        public string ExpectedModelName { get; set; }
        public ServerType ExpectedServerType { get; set; }
        public string AlternateScriptPath { get; set; }
        public string AlternateDisplaySuffix { get; set; }
        public bool IsAlternateMode { get; set; } // Tracks if button is in alternate mode (e.g., CPU)
        public string StopScriptPath { get; set; } // For toggle behavior (stop when running)
        public int RadioGroup { get; set; } // Radio group ID (0 = none, 1+ = mutually exclusive group)
        public string PollHost { get; set; } // Remote host for health checks (default: 127.0.0.1)

        public LauncherButton()
        {
            State = LauncherState.Ready;
            LastError = "";
            ExpectedModelName = "";
            ExpectedServerType = ServerType.Unknown;
            AlternateScriptPath = "";
            AlternateDisplaySuffix = "";
            IsAlternateMode = false;
            StopScriptPath = "";
            RadioGroup = 0;
            PollHost = "127.0.0.1";
        }
    }
}
