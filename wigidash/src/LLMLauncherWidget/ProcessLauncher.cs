using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LLMLauncherWidget
{
    public static class ProcessLauncher
    {
        private const int DefaultTimeoutMs = 30000;

        public static async Task<Process> LaunchBatchScriptAsync(string scriptPath, bool hidden = true)
        {
            try
            {
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("Script not found: " + scriptPath);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = scriptPath;
                startInfo.UseShellExecute = true;
                startInfo.CreateNoWindow = hidden;
                if (hidden)
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                Process process = Process.Start(startInfo);
                return await Task.FromResult(process);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to launch batch script: " + ex.Message);
            }
        }

        public static async Task<Process> LaunchKillSwitchAsync(string pythonPath = "python")
        {
            try
            {
                string scriptPath = FindKillSwitchScript();
                if (string.IsNullOrEmpty(scriptPath))
                    throw new FileNotFoundException("kill_switch.py not found");

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = pythonPath;
                startInfo.Arguments = "\"" + scriptPath + "\"";
                startInfo.UseShellExecute = true;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                Process process = Process.Start(startInfo);
                return await Task.FromResult(process);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to launch kill switch: " + ex.Message);
            }
        }

        public static bool IsProcessRunning(Process process)
        {
            if (process == null) return false;
            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static string FindKillSwitchScript()
        {
            string[] possiblePaths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kill_switch.py"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\assets\\scripts\\kill_switch.py"),
                "kill_switch.py"
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return null;
        }
    }
}
