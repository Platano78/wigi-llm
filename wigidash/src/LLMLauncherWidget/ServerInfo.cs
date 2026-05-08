using System;

namespace LLMLauncherWidget
{
    public enum ServerType { Unknown, LlamaCpp, VLLM, Ollama, LMStudio, AgentGenesis, Generic }
    public enum ServerStatus { Offline, Online, Loading, HighLatency, Error }

    public class ServerInfo
    {
        public int Port { get; set; }
        public ServerType Type { get; set; }
        public string DisplayName { get; set; }
        public string ModelName { get; set; }
        public ServerStatus Status { get; set; }
        public int LatencyMs { get; set; }
        public DateTime LastChecked { get; set; }
        public string HealthEndpoint { get; set; }

        public ServerInfo()
        {
            Status = ServerStatus.Offline;
            LatencyMs = 0;
            LastChecked = DateTime.Now;
            Type = ServerType.Unknown;
            DisplayName = "Unknown Server";
        }

        public override string ToString()
        {
            return DisplayName + ":" + Port + " (" + Type + ") - " + Status;
        }
    }
}
