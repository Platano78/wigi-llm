using System.Windows.Controls;
using System.Net.Sockets;

namespace LLMLauncherWidget
{
    public partial class SettingsUserControl : UserControl
    {
        private LLMLauncherWidgetInstance _instance;

        public SettingsUserControl(LLMLauncherWidgetInstance instance)
        {
            InitializeComponent();
            _instance = instance;
            UpdateStatus();
        }

        private void OnRefreshStatus(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            bool portInUse = IsPortInUse(8081);
            StatusText.Text = portInUse ? "Online" : "Offline";
            StatusText.Foreground = portInUse ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.Red;
        }

        private bool IsPortInUse(int port)
        {
            try
            {
                TcpClient client = new TcpClient();
                client.Connect("127.0.0.1", port);
                client.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnResetDefaults(object sender, System.Windows.RoutedEventArgs e)
        {
            // Reset to default configuration
            if (_instance != null)
            {
                _instance.SaveSettings();
            }
        }
    }
}
