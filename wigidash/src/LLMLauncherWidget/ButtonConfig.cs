using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LLMLauncherWidget
{
    public class IconConfig
    {
        public string Active { get; set; }
        public string Inactive { get; set; }

        public IconConfig()
        {
            Active = "";
            Inactive = "";
        }
    }

    public class ButtonConfig
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string ScriptPath { get; set; }
        public int Port { get; set; }
        public string ExpectedModel { get; set; }
        public string ServerType { get; set; }
        public IconConfig Icons { get; set; }
        public string AlternateScriptPath { get; set; } // For orchestrator CPU mode
        public string AlternateDisplaySuffix { get; set; } // Display suffix for alternate mode (e.g., "CPU")
        public string StopScriptPath { get; set; } // For toggle behavior (stop when running)
        public int RadioGroup { get; set; } // Radio group ID (0 = none, 1+ = mutually exclusive group)
        public string PollHost { get; set; } // Remote host for health checks (default: 127.0.0.1)

        public ButtonConfig()
        {
            Id = "";
            DisplayName = "";
            ScriptPath = "";
            Port = 8081;
            ExpectedModel = "";
            ServerType = "LlamaCpp";
            Icons = new IconConfig();
            AlternateScriptPath = "";
            AlternateDisplaySuffix = "";
            StopScriptPath = "";
            RadioGroup = 0;
            PollHost = "127.0.0.1";
        }
    }

    public class LayoutConfig
    {
        public string Type { get; set; }
        public int MaxColumns { get; set; }
        public int ButtonSpacing { get; set; }
        public int ButtonPadding { get; set; }

        public LayoutConfig()
        {
            Type = "dynamic";
            MaxColumns = 3;
            ButtonSpacing = 10;
            ButtonPadding = 5;
        }
    }

    public class WidgetConfig
    {
        public int Version { get; set; }
        public LayoutConfig Layout { get; set; }
        public List<ButtonConfig> Buttons { get; set; }

        public WidgetConfig()
        {
            Version = 1;
            Layout = new LayoutConfig();
            Buttons = new List<ButtonConfig>();
        }

        // Simple JSON parser without external dependencies
        public static WidgetConfig LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            string json = File.ReadAllText(filePath);
            return ParseJson(json);
        }

        private static WidgetConfig ParseJson(string json)
        {
            WidgetConfig config = new WidgetConfig();

            // Parse version
            Match versionMatch = Regex.Match(json, @"""version""\s*:\s*(\d+)");
            if (versionMatch.Success)
                config.Version = int.Parse(versionMatch.Groups[1].Value);

            // Parse layout
            Match layoutMatch = Regex.Match(json, @"""layout""\s*:\s*\{([^}]+)\}", RegexOptions.Singleline);
            if (layoutMatch.Success)
            {
                string layoutJson = layoutMatch.Groups[1].Value;
                config.Layout = ParseLayout(layoutJson);
            }

            // Parse buttons array
            Match buttonsMatch = Regex.Match(json, @"""buttons""\s*:\s*\[([^\]]+)\]", RegexOptions.Singleline);
            if (buttonsMatch.Success)
            {
                string buttonsJson = buttonsMatch.Groups[1].Value;
                config.Buttons = ParseButtons(buttonsJson);
            }

            return config;
        }

        private static LayoutConfig ParseLayout(string json)
        {
            LayoutConfig layout = new LayoutConfig();

            Match typeMatch = Regex.Match(json, @"""type""\s*:\s*""([^""]+)""");
            if (typeMatch.Success)
                layout.Type = typeMatch.Groups[1].Value;

            Match colMatch = Regex.Match(json, @"""maxColumns""\s*:\s*(\d+)");
            if (colMatch.Success)
                layout.MaxColumns = int.Parse(colMatch.Groups[1].Value);

            Match spacingMatch = Regex.Match(json, @"""buttonSpacing""\s*:\s*(\d+)");
            if (spacingMatch.Success)
                layout.ButtonSpacing = int.Parse(spacingMatch.Groups[1].Value);

            Match paddingMatch = Regex.Match(json, @"""buttonPadding""\s*:\s*(\d+)");
            if (paddingMatch.Success)
                layout.ButtonPadding = int.Parse(paddingMatch.Groups[1].Value);

            return layout;
        }

        private static List<ButtonConfig> ParseButtons(string json)
        {
            List<ButtonConfig> buttons = new List<ButtonConfig>();

            // Match each button object
            MatchCollection buttonMatches = Regex.Matches(json, @"\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);

            foreach (Match buttonMatch in buttonMatches)
            {
                string buttonJson = buttonMatch.Groups[1].Value;
                ButtonConfig btn = new ButtonConfig();

                Match idMatch = Regex.Match(buttonJson, @"""id""\s*:\s*""([^""]+)""");
                if (idMatch.Success)
                    btn.Id = idMatch.Groups[1].Value;

                Match nameMatch = Regex.Match(buttonJson, @"""displayName""\s*:\s*""([^""]+)""");
                if (nameMatch.Success)
                    btn.DisplayName = nameMatch.Groups[1].Value;

                Match scriptMatch = Regex.Match(buttonJson, @"""scriptPath""\s*:\s*""([^""]+)""");
                if (scriptMatch.Success)
                    btn.ScriptPath = scriptMatch.Groups[1].Value;

                Match portMatch = Regex.Match(buttonJson, @"""port""\s*:\s*(-?\d+)");
                if (portMatch.Success)
                    btn.Port = int.Parse(portMatch.Groups[1].Value);

                Match modelMatch = Regex.Match(buttonJson, @"""expectedModel""\s*:\s*""([^""]*)""");
                if (modelMatch.Success)
                    btn.ExpectedModel = modelMatch.Groups[1].Value;

                Match serverMatch = Regex.Match(buttonJson, @"""serverType""\s*:\s*""([^""]+)""");
                if (serverMatch.Success)
                    btn.ServerType = serverMatch.Groups[1].Value;

                Match altScriptMatch = Regex.Match(buttonJson, @"""alternateScriptPath""\s*:\s*""([^""]*)""");
                if (altScriptMatch.Success)
                    btn.AlternateScriptPath = altScriptMatch.Groups[1].Value;

                Match altSuffixMatch = Regex.Match(buttonJson, @"""alternateDisplaySuffix""\s*:\s*""([^""]*)""");
                if (altSuffixMatch.Success)
                    btn.AlternateDisplaySuffix = altSuffixMatch.Groups[1].Value;

                Match stopScriptMatch = Regex.Match(buttonJson, @"""stopScriptPath""\s*:\s*""([^""]*)""");
                if (stopScriptMatch.Success)
                    btn.StopScriptPath = stopScriptMatch.Groups[1].Value;

                Match radioGroupMatch = Regex.Match(buttonJson, @"""radioGroup""\s*:\s*(\d+)");
                if (radioGroupMatch.Success)
                    btn.RadioGroup = int.Parse(radioGroupMatch.Groups[1].Value);

                Match pollHostMatch = Regex.Match(buttonJson, @"""pollHost""\s*:\s*""([^""]+)""");
                if (pollHostMatch.Success)
                    btn.PollHost = pollHostMatch.Groups[1].Value;

                // Parse icons
                Match iconsMatch = Regex.Match(buttonJson, @"""icons""\s*:\s*\{([^}]+)\}");
                if (iconsMatch.Success)
                {
                    string iconsJson = iconsMatch.Groups[1].Value;
                    Match activeMatch = Regex.Match(iconsJson, @"""active""\s*:\s*""([^""]+)""");
                    if (activeMatch.Success)
                        btn.Icons.Active = activeMatch.Groups[1].Value;

                    Match inactiveMatch = Regex.Match(iconsJson, @"""inactive""\s*:\s*""([^""]+)""");
                    if (inactiveMatch.Success)
                        btn.Icons.Inactive = inactiveMatch.Groups[1].Value;
                }

                if (!string.IsNullOrEmpty(btn.Id))
                    buttons.Add(btn);
            }

            return buttons;
        }

        // Save config to file
        public void SaveToFile(string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("{");
                writer.WriteLine("  \"version\": " + Version + ",");
                writer.WriteLine("  \"layout\": {");
                writer.WriteLine("    \"type\": \"" + Layout.Type + "\",");
                writer.WriteLine("    \"maxColumns\": " + Layout.MaxColumns + ",");
                writer.WriteLine("    \"buttonSpacing\": " + Layout.ButtonSpacing + ",");
                writer.WriteLine("    \"buttonPadding\": " + Layout.ButtonPadding);
                writer.WriteLine("  },");
                writer.WriteLine("  \"buttons\": [");

                for (int i = 0; i < Buttons.Count; i++)
                {
                    var btn = Buttons[i];
                    writer.WriteLine("    {");
                    writer.WriteLine("      \"id\": \"" + btn.Id + "\",");
                    writer.WriteLine("      \"displayName\": \"" + btn.DisplayName + "\",");
                    writer.WriteLine("      \"scriptPath\": \"" + btn.ScriptPath + "\",");
                    writer.WriteLine("      \"port\": " + btn.Port + ",");
                    writer.WriteLine("      \"expectedModel\": \"" + btn.ExpectedModel + "\",");
                    writer.WriteLine("      \"serverType\": \"" + btn.ServerType + "\",");
                    if (!string.IsNullOrEmpty(btn.AlternateScriptPath))
                    {
                        writer.WriteLine("      \"alternateScriptPath\": \"" + btn.AlternateScriptPath + "\",");
                        writer.WriteLine("      \"alternateDisplaySuffix\": \"" + btn.AlternateDisplaySuffix + "\",");
                    }
                    if (!string.IsNullOrEmpty(btn.StopScriptPath))
                    {
                        writer.WriteLine("      \"stopScriptPath\": \"" + btn.StopScriptPath + "\",");
                    }
                    writer.WriteLine("      \"icons\": {");
                    writer.WriteLine("        \"active\": \"" + btn.Icons.Active + "\",");
                    writer.WriteLine("        \"inactive\": \"" + btn.Icons.Inactive + "\"");
                    writer.WriteLine("      }");
                    writer.Write("    }");
                    if (i < Buttons.Count - 1)
                        writer.WriteLine(",");
                    else
                        writer.WriteLine();
                }

                writer.WriteLine("  ]");
                writer.WriteLine("}");
            }
        }

        // Create default config — minimal example matching wigi-llm's buttons-starter.json schema.
        // Users should drop their own buttons.json into the widget directory; this fallback
        // exists so the widget doesn't render empty on first load.
        public static WidgetConfig CreateDefault()
        {
            WidgetConfig config = new WidgetConfig();
            config.Version = 2;
            config.Layout = new LayoutConfig { Type = "dynamic", MaxColumns = 2, ButtonSpacing = 8, ButtonPadding = 4 };

            config.Buttons.Add(new ButtonConfig
            {
                Id = "kill",
                DisplayName = "KILL ALL",
                ScriptPath = "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh kill",
                Port = -1,
                ExpectedModel = "",
                ServerType = "Kill",
                Icons = new IconConfig { Active = "icon_kill_active.png", Inactive = "icon_kill_off.png" }
            });

            config.Buttons.Add(new ButtonConfig
            {
                Id = "router",
                DisplayName = "Router ON",
                ScriptPath = "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh start",
                Port = 8081,
                ExpectedModel = "",
                ServerType = "LlamaCpp",
                Icons = new IconConfig { Active = "icon_router_active.png", Inactive = "icon_router_off.png" }
            });

            return config;
        }
    }
}
