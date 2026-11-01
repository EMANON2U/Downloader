using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace Downloader
{
    public static class Setup
    {
        static readonly string TestHome = Environment.GetEnvironmentVariable("DOWNLOADER_HOME");
        public static readonly string AppId = TestHome == null ? "Downloader" : "Downloader-Teste";
        static string DisplayName { get { return TestHome == null ? "Downloader" : "Downloader (teste)"; } }

        static string LocalBase { get { return TestHome ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string BinDir { get { return Path.Combine(LocalBase, "Downloader", "bin"); } }
        static string InstallDir { get { return Path.Combine(LocalBase, "Programs", "Downloader"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "Downloader.exe"); } }
        static string SelfExe { get { return Assembly.GetExecutingAssembly().Location; } }
        static string UninstallKey { get { return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId; } }

        static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(SelfExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); } }

        public static string InstalledVersion
        {
            get
            {
                try { return File.Exists(InstalledExe) ? FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion : null; }
                catch { return null; }
            }
        }

        public static string InstallAction
        {
            get
            {
                if (RunningInstalled) return null;
                if (!File.Exists(InstalledExe)) return "install";
                try
                {
                    var mine = new System.Version(FileVersionInfo.GetVersionInfo(SelfExe).FileVersion);
                    var theirs = new System.Version(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion);
                    return mine > theirs ? "update" : null;
                }
                catch { return "update"; }
            }
        }

        public static string Install()
        {
            try
            {
                if (Process.GetProcessesByName("Downloader").Any(p => p.Id != Process.GetCurrentProcess().Id && SamePath(p, InstalledExe)))
                    return "o Downloader instalado está aberto. Feche-o e tente de novo.";

                Directory.CreateDirectory(InstallDir);
                string tmp = InstalledExe + ".new";
                File.Copy(SelfExe, tmp, true);
                if (File.Exists(InstalledExe)) File.Replace(tmp, InstalledExe, null);
                else File.Move(tmp, InstalledExe);

                try { File.Delete(Path.Combine(LocalBase, "Downloader", "Downloader.exe")); } catch { }
                try { File.Delete(Path.Combine(LocalBase, "Downloader", "Downloader.old.exe")); } catch { }

                foreach (var lnk in Shortcuts()) CreateShortcut(lnk, InstalledExe);

                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", DisplayName);
                    k.SetValue("DisplayVersion", Version.Number);
                    k.SetValue("Publisher", Version.Company);
                    k.SetValue("DisplayIcon", InstalledExe + ",0");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    long components = Version.Payload.Sum(c => long.Parse(c[2]));
                    k.SetValue("EstimatedSize", (int)((new FileInfo(InstalledExe).Length + components) / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static void Uninstall()
        {
            bool open;
            using (var m = new Mutex(false, @"Local\Downloader-" + AppId))
            {
                try { open = !m.WaitOne(0); if (!open) m.ReleaseMutex(); }
                catch (AbandonedMutexException) { open = false; }
            }
            if (open)
            {
                MessageBox.Show("Feche o Downloader antes de desinstalar.", "Desinstalar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show("Desinstalar o Downloader?\n\nOs vídeos e áudios que você baixou não são apagados.", "Desinstalar",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            bool wipeData = MessageBox.Show("Apagar também suas opções e a biblioteca (histórico de downloads)?", "Desinstalar",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

            foreach (var lnk in Shortcuts()) try { File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            TryDeleteDir(BinDir);
            TryDeleteDir(Path.Combine(Path.GetTempPath(), "Downloader"));
            if (wipeData) TryDeleteDir(Program.DataDir);
            try { Directory.Delete(Path.Combine(LocalBase, "Downloader")); } catch { }

            if (RunningInstalled)
            {
                string sys = Environment.SystemDirectory;
                Process.Start(new ProcessStartInfo(Path.Combine(sys, "cmd.exe"),
                    "/c \"\"" + Path.Combine(sys, "ping.exe") + "\" 127.0.0.1 -n 3 >nul & rd /s /q \"" + InstallDir + "\"\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = sys });
            }
            else TryDeleteDir(InstallDir);

            MessageBox.Show("O Downloader foi desinstalado.", "Desinstalar", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static string EnsureComponents(Action<string> progress)
        {
            try
            {
                Directory.CreateDirectory(BinDir);
                foreach (var c in Version.Payload)
                {
                    string name = c[0], sha = c[1];
                    long size = long.Parse(c[2]);
                    string file = Path.Combine(BinDir, name);
                    bool selfUpdating = name == "yt-dlp.exe";
                    if (File.Exists(file))
                    {
                        if (selfUpdating && new FileInfo(file).Length > 0) continue;
                        if (new FileInfo(file).Length == size && Sha256(file) == sha) continue;
                    }

                    progress("Preparando componentes (só na primeira vez)…  " + Path.GetFileNameWithoutExtension(name));
                    string err = Extract(name, sha);
                    if (err != null) return err;
                }
                WriteNotices();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static bool LooksExecutable(string file)
        {
            try
            {
                using (var f = File.OpenRead(file))
                    return f.ReadByte() == 'M' && f.ReadByte() == 'Z';
            }
            catch { return false; }
        }

        public static string RestoreYtDlp()
        {
            try
            {
                var c = Version.Payload.First(x => x[0] == "yt-dlp.exe");
                return Extract(c[0], c[1]);
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string Extract(string name, string sha)
        {
            string file = Path.Combine(BinDir, name);
            string tmp = file + ".tmp";
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("bin/" + name + ".gz"))
            {
                if (res == null) return "componente " + name + " não está no programa";
                using (var gz = new GZipStream(res, CompressionMode.Decompress))
                using (var outp = File.Create(tmp))
                    gz.CopyTo(outp, 1 << 20);
            }
            if (Sha256(tmp) != sha) { File.Delete(tmp); return name + " corrompido ao extrair (" + sha.Substring(0, 12) + "…)"; }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
            return null;
        }

        public static void WriteNotices()
        {
            Directory.CreateDirectory(BinDir);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("THIRD-PARTY-NOTICES.txt"))
            using (var outp = File.Create(Path.Combine(BinDir, "THIRD-PARTY-NOTICES.txt")))
                res.CopyTo(outp);
        }

        static string[] Shortcuts()
        {
            string name = DisplayName + ".lnk";
            return new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), name) };
        }

        static void CreateShortcut(string lnkPath, string target)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Baixe vídeos e áudios" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        static bool SamePath(Process p, string path)
        {
            try { return string.Equals(p.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
