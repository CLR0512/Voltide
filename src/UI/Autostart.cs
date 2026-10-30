using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Voltide.UI
{
    /// <summary>
    /// Start-with-Windows via the per-user Run key.
    ///
    /// HKCU is used rather than a scheduled task or the Startup folder: no admin
    /// rights, no extra files, and the entry can be removed cleanly when the switch
    /// is turned off.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Voltide";

        /// <summary>
        /// False when the current process is not Voltide.exe — the in-memory build
        /// (run.cmd) runs under powershell.exe, and writing that path would launch a
        /// PowerShell window at every boot instead of the app.
        /// </summary>
        public static bool IsSupported()
        {
            try
            {
                string exe = Application.ExecutablePath;
                return exe.EndsWith("Voltide.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return false;

                    if (enabled)
                    {
                        // Quoted: the path may contain spaces.
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
