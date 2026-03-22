using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WigiLlm.Shared;

namespace LLMModelSelectorWidget
{
    public class ModelInfo
    {
        public string Name { get; set; }
        public bool IsLoaded { get; set; }
        public string VramEstimate { get; set; }
        public string Type { get; set; }
    }

    public class RouterStatus
    {
        public List<ModelInfo> Models { get; set; }
        public List<string> LoadedModels { get; set; }
        public string TotalVram { get; set; }
        public int LoadedCount { get; set; }
    }

    public class RouterApiClient : IDisposable
    {
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly string _baseUrl;

        public RouterApiClient(string baseUrl = "http://localhost:8081")
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<RouterStatus> GetStatusAsync()
        {
            try
            {
                // Use /v1/models endpoint (OpenAI-compatible format)
                var response = await _httpClient.GetAsync($"{_baseUrl}/v1/models");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return ParseModelsResponse(content);
            }
            catch (Exception)
            {
                return new RouterStatus
                {
                    Models = new List<ModelInfo>(),
                    LoadedModels = new List<string>(),
                    TotalVram = "Error",
                    LoadedCount = 0
                };
            }
        }

        public async Task<bool> LoadModelAsync(string modelName)
        {
            try
            {
                var json = $"{{\"model\": \"{modelName}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/models/load", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UnloadModelAsync(string modelName)
        {
            try
            {
                var json = $"{{\"model\": \"{modelName}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/models/unload", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private RouterStatus ParseModelsResponse(string jsonResponse)
        {
            var status = new RouterStatus
            {
                Models = new List<ModelInfo>(),
                LoadedModels = new List<string>(),
                LoadedCount = 0,
                TotalVram = "0GB"
            };

            try
            {
                var serializer = new JavaScriptSerializer();
                var response = serializer.Deserialize<Dictionary<string, object>>(jsonResponse);

                if (response != null && response.ContainsKey("data"))
                {
                    var data = response["data"] as System.Collections.ArrayList;
                    if (data != null)
                    {
                        // Parse each model from the data array
                        foreach (var item in data)
                        {
                            var modelData = item as Dictionary<string, object>;
                            if (modelData == null) continue;

                            // Get model id
                            string modelId = null;
                            if (modelData.ContainsKey("id"))
                            {
                                modelId = modelData["id"]?.ToString();
                            }

                            if (string.IsNullOrEmpty(modelId)) continue;

                            // Check if loaded via status.value
                            bool isLoaded = false;
                            if (modelData.ContainsKey("status"))
                            {
                                var statusObj = modelData["status"] as Dictionary<string, object>;
                                if (statusObj != null && statusObj.ContainsKey("value"))
                                {
                                    isLoaded = statusObj["value"]?.ToString() == "loaded";
                                }
                            }

                            // Extract type from model name (agents/coding/fast)
                            var type = modelId.Split('-')[0];

                            // No hardcoded VRAM estimate per model
                            string vramEstimate = "";

                            // Add to models list
                            status.Models.Add(new ModelInfo
                            {
                                Name = modelId,
                                IsLoaded = isLoaded,
                                VramEstimate = vramEstimate,
                                Type = type
                            });

                            // Track loaded models
                            if (isLoaded)
                            {
                                status.LoadedModels.Add(modelId);
                            }
                        }
                    }
                }

                status.LoadedCount = status.LoadedModels.Count;

                // Get real VRAM usage from GPU
                var vram = GpuInfo.GetLocalVram();
                status.TotalVram = vram.Format();
            }
            catch
            {
                // JSON parsing failed - return empty status
            }

            return status;
        }

        public void Dispose()
        {
            // HttpClient is static and shared, don't dispose
        }
    }
}
