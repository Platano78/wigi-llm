using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WigiLlm.Shared
{
    /// <summary>
    /// GPU VRAM info returned by GpuInfo queries.
    /// </summary>
    public class VramInfo
    {
        public float UsedMB { get; set; }
        public float TotalMB { get; set; }
        public float UsedGB { get { return UsedMB / 1024f; } }
        public float TotalGB { get { return TotalMB / 1024f; } }
        public float Percent { get { return TotalMB > 0 ? UsedMB / TotalMB : 0f; } }
        public bool Available { get { return TotalMB > 0; } }

        public string Format()
        {
            if (!Available) return "N/A";
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}GB / {1:F1}GB", UsedGB, TotalGB);
        }

        public string FormatWithPercent()
        {
            if (!Available) return "N/A";
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}/{1:F0} GB ({2:F0}%)", UsedGB, TotalGB, Percent * 100f);
        }
    }

    /// <summary>
    /// Auto-detects GPU VRAM via nvidia-smi (local) or HTTP endpoint (remote).
    /// Thread-safe with configurable cache interval.
    /// </summary>
    public static class GpuInfo
    {
        public static TimeSpan CacheInterval = TimeSpan.FromSeconds(5);
        public static string NvidiaSmiPath = @"C:\Windows\System32\nvidia-smi.exe";

        private static VramInfo _cachedLocal = new VramInfo();
        private static DateTime _lastQuery = DateTime.MinValue;
        private static readonly object _lock = new object();

        /// <summary>
        /// Get local GPU VRAM via nvidia-smi.exe. Thread-safe, cached.
        /// Returns VramInfo with Available=false if detection fails.
        /// </summary>
        public static VramInfo GetLocalVram()
        {
            lock (_lock)
            {
                if (_cachedLocal.Available && (DateTime.Now - _lastQuery) < CacheInterval)
                    return _cachedLocal;

                try
                {
                    if (!File.Exists(NvidiaSmiPath))
                        return _cachedLocal;

                    var psi = new ProcessStartInfo
                    {
                        FileName = NvidiaSmiPath,
                        Arguments = "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(3000);

                        if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            string[] parts = output.Trim().Split(',');
                            if (parts.Length >= 2)
                            {
                                float used, total;
                                if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out used) &&
                                    float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out total))
                                {
                                    _cachedLocal = new VramInfo { UsedMB = used, TotalMB = total };
                                    _lastQuery = DateTime.Now;
                                }
                            }
                        }
                    }
                }
                catch { }

                return _cachedLocal;
            }
        }

        /// <summary>
        /// Get remote GPU VRAM from HTTP endpoint.
        /// Expects JSON: {"vram_used_mb": N, "vram_total_mb": N}
        /// </summary>
        public static async Task<VramInfo> GetRemoteVramAsync(string url)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(3);
                    string json = await client.GetStringAsync(url);
                    var serializer = new JavaScriptSerializer();
                    var data = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (data != null && data.ContainsKey("vram_used_mb") && data.ContainsKey("vram_total_mb"))
                    {
                        float used = 0, total = 0;
                        float.TryParse(data["vram_used_mb"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out used);
                        float.TryParse(data["vram_total_mb"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out total);
                        return new VramInfo { UsedMB = used, TotalMB = total };
                    }
                }
            }
            catch { }

            return new VramInfo();
        }

        /// <summary>
        /// Load optional per-model VRAM estimates from JSON file.
        /// Format: {"model-name": "~13GB", ...}
        /// Returns empty dict if file doesn't exist.
        /// </summary>
        public static Dictionary<string, string> LoadVramEstimates(string jsonPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
                    return result;

                string json = File.ReadAllText(jsonPath);
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                        result[kvp.Key] = kvp.Value;
                }
            }
            catch { }

            return result;
        }
    }
}
