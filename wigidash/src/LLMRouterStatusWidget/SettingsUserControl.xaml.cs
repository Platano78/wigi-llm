using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace LLMRouterStatusWidget
{
    public partial class SettingsUserControl : UserControl
    {
        private LLMRouterStatusWidgetInstance _instance;

        public SettingsUserControl(LLMRouterStatusWidgetInstance instance)
        {
            InitializeComponent();
            _instance = instance;

            // Load current values
            if (_instance != null)
            {
                RouterUrlBox.Text = _instance.RouterUrl ?? "http://localhost:8081/v1/models";
                PollingActiveBox.Text = _instance.PollingIntervalActive.ToString();
                PollingIdleBox.Text = _instance.PollingIntervalIdle.ToString();
            }
        }

        private void RouterUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_instance != null)
            {
                _instance.RouterUrl = RouterUrlBox.Text;
            }
        }

        private void PollingActiveBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_instance != null && int.TryParse(PollingActiveBox.Text, out int value))
            {
                _instance.PollingIntervalActive = Math.Max(1000, value); // Min 1 second
            }
        }

        private void PollingIdleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_instance != null && int.TryParse(PollingIdleBox.Text, out int value))
            {
                _instance.PollingIntervalIdle = Math.Max(1000, value); // Min 1 second
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
                    var response = await client.GetAsync(_instance.RouterUrl);

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
