using System;
using System.Net;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Generic;

namespace ClipboardAgentWidget
{
    public class LLMEndpoint
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public int Port { get; set; }
        public bool IsAvailable { get; set; }
        public string ModelName { get; set; }

        public LLMEndpoint(string name, int port)
        {
            Name = name;
            Port = port;
            BaseUrl = "http://127.0.0.1:" + port;
            IsAvailable = false;
            ModelName = "";
        }
    }

    public class LLMClient
    {
        private readonly int _timeout;
        private List<LLMEndpoint> _endpoints;
        private LLMEndpoint _activeEndpoint;
        private DateTime _lastDiscovery = DateTime.MinValue;
        private const int DISCOVERY_INTERVAL_SECONDS = 30;
        private string _lastError = "";
        private List<string> _discoveryLog = new List<string>();

        // Default endpoints to check
        private static readonly int[] DEFAULT_PORTS = { 8081, 1234, 8080, 5000, 11434 };

        public LLMClient(int timeoutMs)
        {
            _timeout = timeoutMs;
            _endpoints = new List<LLMEndpoint>
            {
                new LLMEndpoint("llama.cpp", 8081),
                new LLMEndpoint("LM Studio", 1234),
                new LLMEndpoint("vLLM", 8000),
                new LLMEndpoint("Ollama", 11434),
                new LLMEndpoint("Text Gen WebUI", 5000)
            };
            _activeEndpoint = null;
        }

        public LLMClient() : this(60000) { } // Increased default timeout to 60s for large models

        public string GetLastError()
        {
            return _lastError;
        }

        public List<string> GetDiscoveryLog()
        {
            return new List<string>(_discoveryLog);
        }

        public string GetActiveEndpointName()
        {
            if (_activeEndpoint != null)
                return _activeEndpoint.Name + " (" + _activeEndpoint.Port + ")";
            return "No server";
        }

        public string GetActiveModelName()
        {
            if (_activeEndpoint != null && !string.IsNullOrEmpty(_activeEndpoint.ModelName))
                return _activeEndpoint.ModelName;
            return "Unknown";
        }

        public void DiscoverEndpoints()
        {
            _discoveryLog.Clear();
            _lastError = "";
            LogDiscovery("Starting endpoint discovery...");
            _activeEndpoint = null;

            foreach (var endpoint in _endpoints)
            {
                try
                {
                    endpoint.IsAvailable = false;
                    endpoint.ModelName = "";

                    string url = endpoint.BaseUrl + "/v1/models";
                    LogDiscovery("Checking " + endpoint.Name + " at " + url);
                    
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Timeout = 3000;

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                string json = reader.ReadToEnd();
                                LogDiscovery("Response from " + endpoint.Name + ": " + json.Substring(0, Math.Min(100, json.Length)) + "...");
                                
                                // Validate it's an LLM API response
                                if (json.Contains("\"data\"") && json.Contains("\"id\""))
                                {
                                    endpoint.IsAvailable = true;
                                    endpoint.ModelName = ParseModelName(json);
                                    LogDiscovery("SUCCESS: Found " + endpoint.Name + " with model: " + endpoint.ModelName);

                                    if (_activeEndpoint == null)
                                    {
                                        _activeEndpoint = endpoint;
                                    }
                                }
                                else
                                {
                                    LogDiscovery("SKIP: " + endpoint.Name + " - Not a valid LLM API (missing data/id fields)");
                                }
                            }
                        }
                    }
                }
                catch (WebException wex)
                {
                    endpoint.IsAvailable = false;
                    string errMsg = wex.Status == WebExceptionStatus.ConnectFailure ? "Connection refused" :
                                   wex.Status == WebExceptionStatus.Timeout ? "Timeout" : wex.Message;
                    LogDiscovery("FAIL: " + endpoint.Name + " - " + errMsg);
                }
                catch (Exception ex)
                {
                    endpoint.IsAvailable = false;
                    LogDiscovery("FAIL: " + endpoint.Name + " - " + ex.Message);
                }
            }

            _lastDiscovery = DateTime.Now;

            if (_activeEndpoint != null)
            {
                Log("Active endpoint: " + _activeEndpoint.Name + " on port " + _activeEndpoint.Port);
            }
            else
            {
                Log("No LLM endpoints found");
            }
        }

        public bool IsServerAvailable()
        {
            // Re-discover if stale
            if ((DateTime.Now - _lastDiscovery).TotalSeconds > DISCOVERY_INTERVAL_SECONDS)
            {
                DiscoverEndpoints();
            }

            return _activeEndpoint != null && _activeEndpoint.IsAvailable;
        }

        public string SendChatCompletion(string userPrompt, string systemPrompt)
        {
            if (string.IsNullOrEmpty(userPrompt))
            {
                throw new ArgumentException("User prompt cannot be null or empty");
            }

            // Ensure we have a valid endpoint
            if (!IsServerAvailable())
            {
                throw new Exception("No LLM server available. Checked ports: 8081, 1234, 8000, 11434, 5000");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                string requestBody = BuildRequestBody(userPrompt, systemPrompt);
                string url = _activeEndpoint.BaseUrl + "/v1/chat/completions";

                Log("Sending request to " + _activeEndpoint.Name + " (" + url + ")");

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = _timeout;

                byte[] data = Encoding.UTF8.GetBytes(requestBody);
                request.ContentLength = data.Length;

                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        string jsonResponse = reader.ReadToEnd();
                        stopwatch.Stop();

                        Log("Request completed in " + stopwatch.ElapsedMilliseconds + "ms");

                        return ParseResponse(jsonResponse);
                    }
                }
            }
            catch (WebException ex)
            {
                stopwatch.Stop();
                Log("Request failed after " + stopwatch.ElapsedMilliseconds + "ms: " + ex.Message);

                // Mark endpoint as unavailable and try to find another
                if (_activeEndpoint != null)
                {
                    _activeEndpoint.IsAvailable = false;
                    _activeEndpoint = null;
                }

                if (ex.Status == WebExceptionStatus.Timeout)
                {
                    throw new Exception("Request timeout after " + _timeout + "ms");
                }

                throw new Exception("Network error: " + ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log("Request failed after " + stopwatch.ElapsedMilliseconds + "ms: " + ex.Message);
                throw;
            }
        }

        private string BuildRequestBody(string userPrompt, string systemPrompt)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\": \"default\", \"messages\": [");

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append("{\"role\": \"system\", \"content\": \"");
                sb.Append(EscapeJsonString(systemPrompt));
                sb.Append("\"}, ");
            }

            sb.Append("{\"role\": \"user\", \"content\": \"");
            sb.Append(EscapeJsonString(userPrompt));
            sb.Append("\"}");

            sb.Append("], \"max_tokens\": 4096, \"stream\": false}");

            return sb.ToString();
        }

        private string ParseResponse(string jsonResponse)
        {
            if (string.IsNullOrEmpty(jsonResponse))
            {
                throw new Exception("Empty response from server");
            }

            // Match content field in JSON response
            Match match = Regex.Match(jsonResponse, @"""content""\s*:\s*""((?:[^""]|\\.)*)""");

            if (match.Success && match.Groups.Count > 1)
            {
                string content = match.Groups[1].Value;
                return UnescapeJsonString(content);
            }

            // Try alternative format (some servers use different structure)
            match = Regex.Match(jsonResponse, @"""text""\s*:\s*""((?:[^""]|\\.)*)""");
            if (match.Success && match.Groups.Count > 1)
            {
                return UnescapeJsonString(match.Groups[1].Value);
            }

            throw new Exception("Could not parse response from server");
        }

        private string ParseModelName(string json)
        {
            Match m = Regex.Match(json, @"""id""\s*:\s*""([^""]*)"" ");
            if (m.Success && m.Groups.Count > 1)
            {
                string model = m.Groups[1].Value;
                // Shorten long model names
                if (model.Length > 20)
                {
                    return model.Substring(0, 17) + "...";
                }
                return model;
            }
            return "default";
        }

        private string EscapeJsonString(string input)
        {
            if (input == null) return "";

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private string UnescapeJsonString(string input)
        {
            if (input == null) return "";

            return input
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private void Log(string message)
        {
            Debug.WriteLine("[ClipboardAgent] " + message);
        }

        private void LogDiscovery(string message)
        {
            _discoveryLog.Add(DateTime.Now.ToString("HH:mm:ss") + " - " + message);
            Log(message);
        }
    }
}