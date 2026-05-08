using System;

namespace WigiLlm.Shared
{
    /// <summary>
    /// Helpers for translating WSL POSIX paths into the Windows UNC form
    /// (\\wsl.localhost\&lt;Distro&gt;\...) the WigiDash widgets need to
    /// invoke from the host.
    ///
    /// Distro and user-home are centralized here so widgets don't bake
    /// "Ubuntu" or "/home/&lt;name&gt;" into multiple files. Override via env:
    ///   WSL_DISTRO    — distro name (default: Ubuntu)
    ///   WSL_USER_HOME — full WSL home path, e.g. /home/foo
    ///   WSL_USER      — username only; builds /home/&lt;name&gt; if WSL_USER_HOME unset
    /// </summary>
    public static class WslPaths
    {
        private const string DefaultDistroName = "Ubuntu";
        private const string DistroEnvVar = "WSL_DISTRO";
        private const string UserHomeEnvVar = "WSL_USER_HOME";
        private const string UserEnvVar = "WSL_USER";

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
                    string env = Environment.GetEnvironmentVariable(DistroEnvVar);
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
        /// Resolved WSL user-home path. Resolution order:
        ///   1. WSL_USER_HOME env var (full path, e.g. "/home/foo")
        ///   2. WSL_USER env var (username only — builds "/home/&lt;name&gt;")
        ///   3. Windows Environment.UserName lowercased — builds "/home/&lt;name&gt;"
        ///      (works when WSL username matches Windows username; common case)
        ///   4. Hard fallback "/home/user"
        /// </summary>
        public static string UserHome
        {
            get
            {
                try
                {
                    string envFull = Environment.GetEnvironmentVariable(UserHomeEnvVar);
                    if (!string.IsNullOrEmpty(envFull))
                    {
                        return envFull;
                    }
                    string envUser = Environment.GetEnvironmentVariable(UserEnvVar);
                    if (!string.IsNullOrEmpty(envUser))
                    {
                        return "/home/" + envUser;
                    }
                    string winUser = Environment.UserName;
                    if (!string.IsNullOrEmpty(winUser))
                    {
                        return "/home/" + winUser.ToLowerInvariant();
                    }
                }
                catch { }
                return "/home/user";
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

        /// <summary>
        /// Resolve a path under the WSL user home and return its Windows UNC form.
        /// E.g. UnderHome(".claude/projects") -> "\\wsl.localhost\Ubuntu\home\foo\.claude\projects"
        /// </summary>
        public static string UnderHome(string relPath)
        {
            string home = UserHome;
            if (string.IsNullOrEmpty(relPath))
            {
                return ToWindowsPath(home);
            }
            string rel = relPath.TrimStart('/');
            return ToWindowsPath(home.TrimEnd('/') + "/" + rel);
        }
    }
}
