using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Downloader
{
    public class BrowserInfo
    {
        public string Key, Name, Exe;
        public bool Running;
        public string Label { get { return Name + (Running ? " · aberto" : ""); } }
    }

    public static class Browsers
    {
        static readonly string[][] Known = {
            new[] { "chrome", "Chrome" }, new[] { "msedge", "Edge" }, new[] { "firefox", "Firefox" },
            new[] { "brave", "Brave" }, new[] { "vivaldi", "Vivaldi" } };

        public static List<BrowserInfo> Find()
        {
            var found = new List<BrowserInfo>();
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var path in new[] { @"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet" })
                {
                    try
                    {
                        using (var list = root.OpenSubKey(path))
                        {
                            if (list == null) continue;
                            foreach (var sub in list.GetSubKeyNames())
                                using (var b = list.OpenSubKey(sub))
                                using (var cmd = b == null ? null : b.OpenSubKey(@"shell\open\command"))
                                {
                                    string exe = ExePath(cmd == null ? null : cmd.GetValue("") as string);
                                    if (exe == null || !File.Exists(exe)) continue;
                                    if (found.Any(f => f.Key == sub || string.Equals(f.Exe, exe, StringComparison.OrdinalIgnoreCase))) continue;
                                    string display = (b.GetValue("") as string ?? "").Trim();
                                    string file = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                                    var known = Known.FirstOrDefault(k => k[0] == file);
                                    found.Add(new BrowserInfo { Key = sub, Exe = exe, Name = known != null ? known[1] : display.Length > 0 ? display : file });
                                }
                        }
                    }
                    catch { }
                }
            foreach (var f in found) f.Running = IsRunning(ProcessName(f));
            return found.OrderByDescending(f => f.Running).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string ProcessName(BrowserInfo b)
        {
            string file = Path.GetFileNameWithoutExtension(b.Exe).ToLowerInvariant();
            return file == "launcher" && b.Name.StartsWith("Opera", StringComparison.OrdinalIgnoreCase) ? "opera" : file;
        }

        public static bool IsRunning(string process)
        {
            var all = System.Diagnostics.Process.GetProcessesByName(process);
            foreach (var p in all) p.Dispose();
            return all.Length > 0;
        }

        static string ExePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string c = Environment.ExpandEnvironmentVariables(command.Trim());
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                return end > 1 ? c.Substring(1, end - 1) : null;
            }
            int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? c.Substring(0, exe + 4) : null;
        }

        public static void Open(BrowserInfo b, string url)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(b.Exe, Args.Quote(url)) { UseShellExecute = false }).Dispose();
        }
    }
}
