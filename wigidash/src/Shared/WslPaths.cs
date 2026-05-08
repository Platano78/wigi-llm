using System;

namespace WigiLlm.Shared
{
    /// <summary>
    /// Helpers for translating WSL POSIX paths into the Windows UNC form
    /// (\\wsl.localhost\&lt;Distro&gt;\...) the WigiDash widgets need to
    /// invoke from the host.
    ///
    /// The distro name is centralized here so widgets don't bake "Ubuntu"
    /// into multiple files. Override per-call, or set WSL_DISTRO via env.
    /// </summary>
    public static class WslPaths
    {
        private const string DefaultDistroName = "Ubuntu";
        private const string EnvVarName = "WSL_DISTRO";

        /// <summary>
        /// Resolved distro name. Reads the WSL_DISTRO environment variable
        /// if set; falls back to "Ubuntu".
        /// </summary>
        public static string DefaultDistro
        {
            get
            {
                try
                {
                    string env = Environment.GetEnvironmentVariable(EnvVarName);
                    if (!string.IsNullOrEmpty(env))
                    {
                        return env;
                    }
                }
                catch { }
                return DefaultDistroName;
            }
        }

        /// <summary>
        /// Convert an absolute WSL path (e.g. "/home/platano/foo") to its
        /// Windows UNC equivalent ("\\wsl.localhost\Ubuntu\home\platano\foo").
        /// Pass distro=null to use DefaultDistro.
        /// </summary>
        public static string ToWindowsPath(string wslAbsPath, string distro)
        {
            if (string.IsNullOrEmpty(wslAbsPath))
            {
                return wslAbsPath;
            }

            string d = string.IsNullOrEmpty(distro) ? DefaultDistro : distro;

            // Strip leading slash, swap separators.
            string trimmed = wslAbsPath.TrimStart('/');
            string winRel = trimmed.Replace('/', '\\');

            return @"\\wsl.localhost\" + d + @"\" + winRel;
        }

        /// <summary>
        /// Overload using DefaultDistro.
        /// </summary>
        public static string ToWindowsPath(string wslAbsPath)
        {
            return ToWindowsPath(wslAbsPath, null);
        }
    }
}
