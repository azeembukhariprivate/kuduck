using System;
using Microsoft.Win32;

namespace Kuduck.Services
{
    /// <summary>"Start with Windows" for the current user only (no admin needed).</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KUDUCK";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                        return key?.GetValue(ValueName) != null;
                }
                catch { return false; }
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    if (key == null) return false;
                    if (enabled)
                        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --startup");
                    else if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
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
