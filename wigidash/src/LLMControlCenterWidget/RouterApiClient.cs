using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WigiLlm.Shared;

namespace LLMControlCenterWidget
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
        private static readonly HttpClient _pollClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly string _baseUrl;

        public RouterApiClient(string baseUrl = "http://127.0.0.1:8081")
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<RouterStatus> GetStatusAsync()
        {
            try
            {
                var response = await _pollClient.GetAsync($"{_baseUrl}/v1/models");
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
                    TotalVram = "Offline",
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
            catch { return false; }
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
            catch { return false; }
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
                        foreach (var item in data)
                        {
                            var modelData = item as Dictionary<string, object>;
                            if (modelData == null) continue;

                            string modelId = modelData.ContainsKey("id") ? modelData["id"]?.ToString() : null;
                            if (string.IsNullOrEmpty(modelId)) continue;

                            bool isLoaded = false;
                            if (modelData.ContainsKey("status"))
                            {
                                var statusObj = modelData["status"] as Dictionary<string, object>;
                                if (statusObj != null && statusObj.ContainsKey("value"))
                                    isLoaded = statusObj["value"]?.ToString() == "loaded";
                            }

                            var type = modelId.Split('-')[0];
                            string vramEstimate = "";

                            status.Models.Add(new ModelInfo
                            {
                                Name = modelId,
                                IsLoaded = isLoaded,
                                VramEstimate = vramEstimate,
                                Type = type
                            });

                            if (isLoaded) status.LoadedModels.Add(modelId);
                        }
                    }
                }

                status.LoadedCount = status.LoadedModels.Count;
                var vram = GpuInfo.GetLocalVram();
                status.TotalVram = vram.Format();
            }
            catch { }
            return status;
        }

        public void Dispose() { }
    }
}
