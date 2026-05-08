using System;
using System.Globalization;
using System.IO;
using System.Net;

namespace LLMStatsWidget
{
    /// <summary>
    /// Snapshot of llama.cpp router state. Defaults represent "no data".
    /// </summary>
    public class StatsSnapshot
    {
        public string ModelName;
        public double GenTokensPerSec;     // llamacpp:predicted_tokens_seconds
        public double PromptTokensPerSec;  // llamacpp:prompt_tokens_seconds
        public int RequestsProcessing;     // llamacpp:requests_processing
        public bool Reachable;             // false = router offline / no data

        public StatsSnapshot()
        {
            ModelName = "";
            GenTokensPerSec = 0;
            PromptTokensPerSec = 0;
            RequestsProcessing = 0;
            Reachable = false;
        }
    }

    /// <summary>
    /// Polls the llama.cpp router for the loaded model name + Prometheus metrics.
    /// Stateless — call Fetch() once per refresh tick.
    /// </summary>
    public static class MetricsClient
    {
        private const string RouterHost = "127.0.0.1";
        private const int RouterPort = 8081;
        private const int TimeoutMs = 1500;

        public static StatsSnapshot Fetch()
        {
            StatsSnapshot snap = new StatsSnapshot();
            string modelName = FetchLoadedModelName();
            if (string.IsNullOrEmpty(modelName))
                return snap; // router offline or no model loaded

            snap.ModelName = modelName;

            string metricsText = FetchMetricsText(modelName);
            if (string.IsNullOrEmpty(metricsText))
                return snap; // model name was readable but metrics request failed

            snap.Reachable = true;
            ParseMetrics(metricsText, snap);
            return snap;
        }

        private static string FetchLoadedModelName()
        {
            try
            {
                string url = "http://" + RouterHost + ":" + RouterPort + "/v1/models";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = TimeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return "";
                    using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                    {
                        return ParseLoadedModel(r.ReadToEnd());
                    }
                }
            }
            catch { return ""; }
        }

        private static string FetchMetricsText(string modelName)
        {
            try
            {
                string url = "http://" + RouterHost + ":" + RouterPort + "/metrics?model="
                             + WebUtility.UrlEncode(modelName);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = TimeoutMs;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK) return "";
                    using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                    {
                        return r.ReadToEnd();
                    }
                }
            }
            catch { return ""; }
        }

        private static string ParseLoadedModel(string json)
        {
            int loadedPos = json.IndexOf("\"value\":\"loaded\"", StringComparison.Ordinal);
            if (loadedPos < 0) return "";
            int searchStart = Math.Max(0, loadedPos - 2000);
            int idIdx = json.LastIndexOf("\"id\"", loadedPos, loadedPos - searchStart, StringComparison.Ordinal);
            if (idIdx < 0) return "";
            int colon = json.IndexOf(':', idIdx + 4);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        // Skips Prometheus comment lines (# HELP / # TYPE) which contain the metric
        // name as a substring and would fool a naive IndexOf-based parser.
        private static void ParseMetrics(string text, StatsSnapshot snap)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                double v;
                if (TryReadMetric(line, "llamacpp:predicted_tokens_seconds ", out v))
                    snap.GenTokensPerSec = v;
                else if (TryReadMetric(line, "llamacpp:prompt_tokens_seconds ", out v))
                    snap.PromptTokensPerSec = v;
                else if (TryReadMetric(line, "llamacpp:requests_processing ", out v))
                    snap.RequestsProcessing = (int)v;
            }
        }

        private static bool TryReadMetric(string line, string prefix, out double value)
        {
            value = 0;
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            return double.TryParse(parts[parts.Length - 1],
                                   NumberStyles.Float,
                                   CultureInfo.InvariantCulture,
                                   out value);
        }
    }
}
