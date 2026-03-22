using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace LLMModelSelectorWidget
{
    public partial class SettingsUserControl : UserControl
    {
        private LLMModelSelectorWidgetInstance _instance;

        public SettingsUserControl(LLMModelSelectorWidgetInstance instance)
        {
            InitializeComponent();
            _instance = instance;

            // Load current values
            if (_instance != null)
            {
                RouterUrlBox.Text = _instance.RouterUrl ?? "http://localhost:8081";
                PollingIntervalBox.Text = _instance.PollingInterval.ToString();
            }
        }

        private void RouterUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_instance != null)
            {
                _instance.RouterUrl = RouterUrlBox.Text;
            }
        }

        private void PollingIntervalBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_instance != null && int.TryParse(PollingIntervalBox.Text, out int value))
            {
                _instance.PollingInterval = Math.Max(1000, value); // Min 1 second
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_instance == null)
            {
                StatusMessage.Text = "Error: Widget instance not found.";
                return;
            }

            StatusMessage.Text = "Testing connection...";
            StatusMessage.Foreground = System.Windows.Media.Brushes.Yellow;

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var response = await client.GetAsync($"{_instance.RouterUrl}/models");

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        StatusMessage.Text = $"✓ Connection successful! Response: {json.Substring(0, Math.Min(100, json.Length))}...";
                        StatusMessage.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }
                    else
                    {
                        StatusMessage.Text = $"✗ Server responded with status: {response.StatusCode}";
                        StatusMessage.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"✗ Connection failed: {ex.Message}";
                StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_instance != null)
            {
                _instance.SaveSettings();
                _instance.UpdateSettings();
                StatusMessage.Text = "✓ Settings saved and widget updated.";
                StatusMessage.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
        }
    }
}
