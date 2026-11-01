using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Shell;

[assembly: AssemblyTitle("Downloader - baixe vídeos e áudios")]
[assembly: AssemblyProduct(Downloader.Version.Product)]
[assembly: AssemblyCompany(Downloader.Version.Company)]
[assembly: AssemblyCopyright("© " + Downloader.Version.Year + " " + Downloader.Version.Company)]
[assembly: AssemblyVersion(Downloader.Version.Number + ".0")]
[assembly: AssemblyFileVersion(Downloader.Version.Number + ".0")]
[assembly: AssemblyInformationalVersion(Downloader.Version.Number)]

namespace Downloader
{
    public static class Options
    {
        public static readonly string[][] Kinds = {
            new[] { "video", "Vídeo", "Vídeo" },
            new[] { "split", "Vídeo + áudio separados", "Vídeo + áudio separados (2 arquivos)" },
            new[] { "audio", "Só áudio", "Só áudio" } };
        public static readonly string[][] Qualities = {
            new[] { "max", "Máxima", "Máxima disponível" },
            new[] { "2160", "4K", "4K · 2160p" },
            new[] { "1440", "1440p", "1440p" },
            new[] { "1080", "1080p", "Full HD · 1080p" },
            new[] { "720", "720p", "HD · 720p" },
            new[] { "480", "480p", "480p" },
            new[] { "360", "360p", "360p" } };
        public static readonly string[][] VideoFmts = {
            new[] { "mkv", "MKV", "MKV · máxima qualidade" },
            new[] { "mp4", "MP4", "MP4 · compatível (Premiere, celular)" } };
        public static readonly string[][] AudioFmts = {
            new[] { "mp3", "MP3", "MP3 · 320 kbps" },
            new[] { "wav", "WAV", "WAV · sem perdas, para edição" },
            new[] { "m4a", "M4A", "M4A · AAC, compatível" },
            new[] { "orig", "Original", "Original · sem recompressão" } };
        public static readonly string[][] Logins = {
            new[] { "none", "Sem login", "Sem login · para conteúdo público" },
            new[] { "firefox", "Firefox", "Firefox · recomendado" },
            new[] { "chrome", "Chrome", "Chrome · pode ser bloqueado pelo navegador" },
            new[] { "edge", "Edge", "Edge · pode ser bloqueado pelo navegador" },
            new[] { "file", "Arquivo de cookies", "Arquivo de cookies (.txt)…" } };
        public static readonly string[][] Trims = {
            new[] { "full", "Vídeo completo", "Vídeo completo" },
            new[] { "part", "Só um trecho", "Só um trecho (escolher início e fim)" } };

        public static string Label(string[][] table, string key)
        {
            foreach (var o in table) if (o[0] == key) return o[1];
            return key;
        }
        public static bool Has(string[][] table, string key) { return table.Any(o => o[0] == key); }
    }

    public class Item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(params string[] names)
        {
            var h = PropertyChanged;
            if (h != null) foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }

        public string Url;
        public string LastError;
        public string Height;
        public string Channel, ChannelUrl;
        public string ThumbUrl;
        public DateTime Started;
        public Process Proc;
        public bool Cancelled;
        public bool Paused;
        public bool Force;
        public bool AlreadyThere;
        public bool UseId;
        public string DestDir;
        public readonly List<string> Files = new List<string>();

        public double? TrimStart, TrimEnd;
        public bool TrimExact = true;
        public double ProbeDuration;
        public string ProbeLive, ProbeHeight;
        public string WorkDir;
        public bool UseCookies;
        public ErrorInfo Preflight;
        public int Streams = 1, StreamIdx;
        public readonly Queue<string> Tail = new Queue<string>();

        ErrorInfo err;
        bool busy;
        string title, site, status, state, thumb, phase = "";
        string kind = "video", quality = "max", fmt = "mkv", login = "none";
        string audioFmt = "orig";
        double progress;

        public string Title { get { return title; } set { title = value; Changed("Title"); } }
        public string Site { get { return site; } set { site = value; Changed("Site"); } }
        public string Status { get { return status; } set { status = value; Changed("Status"); } }
        public string Thumb { get { return thumb; } set { thumb = value; Changed("Thumb"); } }
        public string Phase { get { return phase; } set { phase = value; } }
        public double Progress { get { return progress; } set { progress = value; Changed("Progress"); } }
        public string State { get { return state; } set { state = value; Changed("State", "Editable", "CanPause", "CancelTip"); } }
        public bool Editable { get { return state != "baixando"; } }
        public bool Busy { get { return busy; } set { busy = value; Changed("Busy", "CanPause"); } }
        public bool CanPause { get { return state == "baixando" && !busy; } }
        public string CancelTip { get { return state == "baixando" ? "Cancelar download" : "Remover da lista"; } }
        public ErrorInfo Err
        {
            get { return err; }
            set { err = value; Changed("Err", "ErrFix", "ErrAction", "HasErrAction", "ErrCode"); }
        }
        public string ErrFix { get { return err == null ? "" : err.Fix; } }
        public string ErrCode { get { return err == null ? "" : err.Code; } }
        public string ErrAction { get { return err == null ? null : err.ActionLabel; } }
        public bool HasErrAction { get { return err != null && err.ActionLabel != null; } }
        public bool HasTrim { get { return TrimStart != null || TrimEnd != null; } }
        public string TrimLabel { get { return !HasTrim ? "" : "Trecho " + TimeText.Format(TrimStart ?? 0) + " – " + (TrimEnd == null ? "fim" : TimeText.Format(TrimEnd.Value)); } }

        public string Kind
        {
            get { return kind; }
            set
            {
                kind = value;
                if (kind == "audio" && !Options.Has(Options.AudioFmts, fmt)) fmt = "mp3";
                if (kind != "audio" && !Options.Has(Options.VideoFmts, fmt)) fmt = "mkv";
                Changed("Kind", "KindLabel", "Fmt", "FmtLabel");
            }
        }
        public string Quality { get { return quality; } set { quality = value; Changed("Quality", "QualityLabel"); } }
        public string Fmt { get { return fmt; } set { fmt = value; Changed("Fmt", "FmtLabel"); } }
        public string Login { get { return login; } set { login = value; Changed("Login", "LoginLabel"); } }
        public string AudioFmt { get { return audioFmt; } set { audioFmt = value; Changed("AudioFmt", "AudioFmtLabel", "SplitAudioLabel"); } }

        public string KindLabel { get { return Options.Label(Options.Kinds, kind); } }
        public string QualityLabel { get { return Options.Label(Options.Qualities, quality); } }
        public string FmtLabel { get { return Options.Label(kind == "audio" ? Options.AudioFmts : Options.VideoFmts, fmt); } }
        public string LoginLabel { get { return Options.Label(Options.Logins, login); } }
        public string AudioFmtLabel { get { return Options.Label(Options.AudioFmts, audioFmt); } }
        public string SplitAudioLabel { get { return "Áudio · " + AudioFmtLabel; } }
        string trimMode = "full";
        public string TrimMode { get { return trimMode; } set { trimMode = value; Changed("TrimMode", "TrimModeLabel"); } }
        public string TrimModeLabel { get { return Options.Label(Options.Trims, trimMode); } }

        public void CopyOptions(Item from) { Kind = from.Kind; Quality = from.Quality; Fmt = from.Fmt; Login = from.Login; AudioFmt = from.AudioFmt; }
    }

    public static class TimeText
    {
        public static double? Parse(string text)
        {
            string s = (text ?? "").Trim();
            if (!Regex.IsMatch(s, @"^\d{1,6}(:\d{1,2}){0,2}$")) return null;
            double total = 0;
            var parts = s.Split(':');
            for (int i = 0; i < parts.Length; i++)
            {
                int v = int.Parse(parts[i]);
                if (i > 0 && v >= 60) return null;
                total = total * 60 + v;
            }
            return total;
        }

        public static string Format(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + t.ToString(@"\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        public static string Long(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            var parts = new List<string>();
            if (t.TotalHours >= 1) parts.Add((int)t.TotalHours + " h");
            if (t.Minutes > 0) parts.Add(t.Minutes + " min");
            if (t.Seconds > 0 || parts.Count == 0) parts.Add(t.Seconds + " s");
            return string.Join(" ", parts);
        }
    }

    public class Entry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(params string[] names)
        {
            var h = PropertyChanged;
            if (h != null) foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }

        public string Url, Channel, ChannelUrl, Kind, Quality, Fmt, AudioFmt, Height;
        public double? TrimStart, TrimEnd;
        public bool TrimExact = true;
        public DateTime Date;
        public readonly List<string> Files = new List<string>();

        string thumb;
        bool exists = true;
        public string Title { get; set; }
        public string Site { get; set; }
        public string Thumb { get { return thumb; } set { thumb = value; Changed("Thumb"); } }
        public bool Exists { get { return exists; } set { exists = value; Changed("Exists", "Meta"); } }

        public string Meta
        {
            get
            {
                if (!exists) return "O arquivo não está mais na pasta. Use “Baixar de novo”.";
                var parts = new List<string> { Date.ToString("dd/MM/yyyy HH:mm"), Options.Label(Options.Kinds, Kind) };
                if (TrimStart != null || TrimEnd != null) parts.Add("Trecho " + TimeText.Format(TrimStart ?? 0) + " – " + (TrimEnd == null ? "fim" : TimeText.Format(TrimEnd.Value)));
                if (Kind != "audio" && !string.IsNullOrEmpty(Height)) parts.Add(Height + "p");
                parts.Add(Options.Label(Kind == "audio" ? Options.AudioFmts : Options.VideoFmts, Fmt) +
                          (Kind == "split" ? " + " + Options.Label(Options.AudioFmts, AudioFmt) : ""));
                return string.Join("  ·  ", parts);
            }
        }
    }

    public class Suggestion : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
        bool queued;
        string thumb;
        public string Url, ThumbUrl;
        public string Title { get; set; }
        public string Info { get; set; }
        public string Thumb { get { return thumb; } set { thumb = value; Changed("Thumb"); } }
        public bool Queued { get { return queued; } set { queued = value; Changed("Queued"); } }
    }

    public static class Args
    {
        public static string Quote(string value)
        {
            var b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); slashes = 0; b.Append('"'); continue; }
                b.Append('\\', slashes);
                slashes = 0;
                b.Append(c);
            }
            b.Append('\\', slashes * 2);
            return b.Append('"').ToString();
        }
    }

    public class ThumbConverter : IValueConverter
    {
        const int MaxSide = 8000, DecodeWidth = 416;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string file = value as string;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            try
            {
                using (var st = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var head = System.Windows.Media.Imaging.BitmapFrame.Create(st, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
                    if (head.PixelWidth > MaxSide || head.PixelHeight > MaxSide) return null;
                    st.Position = 0;
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                    img.DecodePixelWidth = Math.Min(DecodeWidth, head.PixelWidth);
                    img.StreamSource = st;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    public class Preview : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
        string title, site, thumb, sizeText, duration;
        bool busy;
        public string Url, ThumbUrl, ChannelUrl, Channel;
        public SizeResult Size;
        public string Title { get { return title; } set { title = value; Changed("Title"); } }
        public string Site { get { return site; } set { site = value; Changed("Site"); } }
        public string Thumb { get { return thumb; } set { thumb = value; Changed("Thumb"); } }
        public string SizeText { get { return sizeText; } set { sizeText = value; Changed("SizeText"); } }
        public string Duration { get { return duration; } set { duration = value; Changed("Duration"); } }
        public bool Busy { get { return busy; } set { busy = value; Changed("Busy"); } }
    }

    public class Program
    {
        const string NameTemplate = "%(title).150B";
        const int MaxPreviews = 8;
        const int UpdateTimeoutMs = 45000, ToolTimeoutMs = 30000, PreviewTimeoutMs = 60000, SuggestTimeoutMs = 60000, KillWaitMs = 10000;
        const int ThumbTimeoutMs = 20000, ThumbMaxBytes = 10 * 1024 * 1024, ThumbCacheDays = 30;

        static readonly string[][] ThemeColors = {
            new[] { "Canvas", "#FFFFFF", "#1C1C1E" },
            new[] { "Sidebar", "#F5F5F7", "#242426" },
            new[] { "Elevated", "#FFFFFF", "#2C2C2E" },
            new[] { "FieldBg", "#F2F2F5", "#333336" },
            new[] { "FieldHover", "#EBEBEF", "#3A3A3C" },
            new[] { "ChipBg", "#F2F2F5", "#2C2C2E" },
            new[] { "ChipHover", "#E6E6EB", "#3A3A3C" },
            new[] { "Hover", "#EBEBEF", "#343436" },
            new[] { "NavSelected", "#E3E3E8", "#3A3A3C" },
            new[] { "Line", "#E5E5EA", "#38383A" },
            new[] { "Text", "#1D1D1F", "#F5F5F7" },
            new[] { "Muted", "#6E6E73", "#A1A1A6" },
            new[] { "Faint", "#8E8E93", "#8E8E93" },
            new[] { "Accent", "#0071E3", "#0A84FF" },
            new[] { "LinkText", "#0066CC", "#4DA3FF" },
            new[] { "AccentSoft", "#E8F1FC", "#12355C" },
            new[] { "AccentSoftText", "#0058B0", "#9CCBFF" },
            new[] { "Primary", "#0071E3", "#0071E3" },
            new[] { "PrimaryHover", "#0077ED", "#0A84FF" },
            new[] { "PrimaryPress", "#0062C4", "#0062C4" },
            new[] { "OnAccent", "#FFFFFF", "#FFFFFF" },
            new[] { "Disabled", "#E5E5EA", "#3A3A3C" },
            new[] { "DisabledText", "#8E8E93", "#8E8E93" },
            new[] { "Warn", "#B25000", "#FF9F0A" },
            new[] { "WarnSoft", "#FFF4E5", "#3D2E14" },
            new[] { "Danger", "#C4001A", "#FF6961" },
            new[] { "Good", "#248A3D", "#30D158" },
            new[] { "ThumbBg", "#E8E8ED", "#3A3A3C" },
            new[] { "ThumbIcon", "#AEAEB2", "#6E6E73" },
            new[] { "ScrollThumb", "#C7C7CC", "#55555A" },
            new[] { "SwitchOff", "#D1D1D6", "#48484A" },
            new[] { "Scrim", "#40000000", "#80000000" } };

        static string themeMode = "system";
        static double savedWidth, savedHeight;
        static bool savedMaximized;
        static bool compactLayout, layoutApplied;

        static Window win;
        static TextBox linksBox, searchBox, trimStartBox, trimEndBox;
        static TextBlock footer, libEmpty, suggestTitle, linkHint, trimHint, loginHint;
        static FrameworkElement suggestPanel, emptyHint;
        static CheckBox trimExactBox;
        static Button downloadBtn;
        static string cookieFile;
        static string seenVersion;
        static ErrorInfo sheetError;
        static RadioButton tabDownloads, tabLibrary, tabHelp, tabNews, tabLegal;
        static bool libraryOpen;
        static string pendingSection;
        static TextBlock totalText;
        static readonly ObservableCollection<Preview> previews = new ObservableCollection<Preview>();
        static readonly Dictionary<string, ProbeData> probeCache = new Dictionary<string, ProbeData>();
        static System.Windows.Threading.DispatcherTimer previewTimer;
        static bool previewBusy, previewKilled;
        static string previewKey;
        static Process previewProc;
        static ICollectionView libView;
        static string firstRunPath;
        static readonly Item defaults = new Item();
        static readonly ObservableCollection<Item> items = new ObservableCollection<Item>();
        static readonly ObservableCollection<Entry> history = new ObservableCollection<Entry>();
        static readonly ObservableCollection<Suggestion> suggestions = new ObservableCollection<Suggestion>();
        static bool suggestBusy;
        static string suggestKey;
        static readonly Queue<Item> queue = new Queue<Item>();
        static Item current;
        static bool updating;
        static bool queuePaused;
        static Button pauseAllBtn;
        static ContextMenu openMenu;
        static Mutex singleInstance;

        static string Bin { get { return Setup.BinDir; } }
        static string TempDir { get { return Path.Combine(Path.GetTempPath(), "Downloader"); } }
        internal static string DataDir
        {
            get
            {
                return Environment.GetEnvironmentVariable("DOWNLOADER_DATA")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader");
            }
        }
        static string ConfigFile { get { return Path.Combine(DataDir, "config.ini"); } }
        static string HistoryFile { get { return Path.Combine(DataDir, "history.tsv"); } }
        static string ThumbsDir { get { return Path.Combine(DataDir, "thumbs"); } }
        static string DefaultDest { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Downloader"); } }
        static string customDest;
        static string Dest { get { return customDest ?? DefaultDest; } }

        [STAThread]
        public static void Main(string[] args)
        {
            SetDllDirectory("");
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            var app = new Application();
            app.DispatcherUnhandledException += (s, e) =>
            {
                ShowFatal(e.Exception, "interface");
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception("Erro desconhecido");
                var err = Errors.Register(Errors.Unexpected(ex, "segundo-plano", true), null, null);
                MessageBox.Show("O Downloader encontrou um erro e precisa fechar.\n\nCódigo: " + err.Code + "\nIdentificador: " + err.Incident +
                    "\n\nOs detalhes estão em:\n" + Errors.LogDir, "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))) { Setup.Uninstall(); return; }
            bool created;
            singleInstance = new Mutex(true, @"Local\Downloader-" + Setup.AppId, out created);
            if (!created && args.Contains("--wait"))
            {
                try { created = singleInstance.WaitOne(15000); } catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                MessageBox.Show("O Downloader já está aberto.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadConfig();
            app.Resources["NavLabel"] = Visibility.Visible;
            app.Resources["NavIcon"] = Visibility.Collapsed;
            ApplyTheme();

            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                win = (Window)XamlReader.Load(st);
            app.Resources[typeof(ToolTip)] = win.Resources[typeof(ToolTip)];
            RestoreWindowSize();

            linksBox = (TextBox)win.FindName("LinksBox");
            totalText = (TextBlock)win.FindName("TotalText");
            footer = (TextBlock)win.FindName("FooterText");
            emptyHint = (FrameworkElement)win.FindName("EmptyHint");
            linkHint = (TextBlock)win.FindName("LinkHint");
            trimHint = (TextBlock)win.FindName("TrimHint");
            loginHint = (TextBlock)win.FindName("LoginHint");
            trimStartBox = (TextBox)win.FindName("TrimStartBox");
            trimEndBox = (TextBox)win.FindName("TrimEndBox");
            trimExactBox = (CheckBox)win.FindName("TrimExactBox");
            downloadBtn = (Button)win.FindName("DownloadBtn");
            libEmpty = (TextBlock)win.FindName("LibEmpty");
            suggestTitle = (TextBlock)win.FindName("SuggestTitle");
            suggestPanel = (FrameworkElement)win.FindName("SuggestPanel");
            searchBox = (TextBox)win.FindName("SearchBox");
            UpdateDestText();
            ((FrameworkElement)win.FindName("InputCard")).DataContext = defaults;
            ((ItemsControl)win.FindName("ItemsList")).ItemsSource = items;
            ((ItemsControl)win.FindName("LibList")).ItemsSource = history;
            ((ItemsControl)win.FindName("SuggestList")).ItemsSource = suggestions;
            ((ItemsControl)win.FindName("PreviewList")).ItemsSource = previews;
            previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            previewTimer.Tick += (s, e) => SyncPreviews();
            libView = CollectionViewSource.GetDefaultView(history);
            items.CollectionChanged += (s, e) => UpdateEmpty();
            history.CollectionChanged += (s, e) => UpdateEmpty();
            suggestions.CollectionChanged += (s, e) => UpdateEmpty();
            previews.CollectionChanged += (s, e) => UpdateEmpty();
            searchBox.TextChanged += (s, e) => FilterLibrary();

            tabDownloads = (RadioButton)win.FindName("TabDownloads");
            tabLibrary = (RadioButton)win.FindName("TabLibrary");
            tabHelp = (RadioButton)win.FindName("TabHelp");
            tabNews = (RadioButton)win.FindName("TabNews");
            tabDownloads.Checked += (s, e) => ShowTab(false);
            tabLibrary.Checked += (s, e) => ShowTab(true);
            tabHelp.Checked += (s, e) => RenderPage("help");
            tabNews.Checked += (s, e) => RenderPage("news");
            tabLegal = (RadioButton)win.FindName("TabLegal");
            tabLegal.Checked += (s, e) => RenderPage("legal");
            ((Button)win.FindName("CopyrightLink")).Click += (s, e) => ShowPage("legal", "Diretrizes de Uso");
            foreach (var pair in new[] { new[] { "ThemeSystem", "system" }, new[] { "ThemeLight", "light" }, new[] { "ThemeDark", "dark" } })
            {
                var radio = (RadioButton)win.FindName(pair[0]);
                string mode = pair[1];
                radio.IsChecked = themeMode == mode;
                radio.Checked += (s, e) => { themeMode = mode; ApplyTheme(); SaveConfig(); };
            }
            ((Button)win.FindName("MinBtn")).Click += (s, e) => SystemCommands.MinimizeWindow(win);
            ((Button)win.FindName("MaxBtn")).Click += (s, e) =>
            {
                if (win.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(win);
                else SystemCommands.MaximizeWindow(win);
            };
            ((Button)win.FindName("CloseBtn")).Click += (s, e) => win.Close();
            win.StateChanged += (s, e) => UpdateWindowState();
            ((Button)win.FindName("FirstRunPick")).Click += (s, e) =>
            {
                string p = PickFolder(firstRunPath);
                if (p != null) { firstRunPath = p; ((TextBlock)win.FindName("FirstRunPath")).Text = p; }
            };
            ((Button)win.FindName("FirstRunOk")).Click += (s, e) => FinishFirstRun();
            ((Button)win.FindName("InstallOk")).Click += (s, e) => DoInstall();
            ((Button)win.FindName("InstallSkip")).Click += (s, e) => { HideSheet("InstallSheet"); AfterInstallSheet(); };
            ((Button)win.FindName("EmptyHelpBtn")).Click += (s, e) => ShowPage("help", null);
            ((Button)win.FindName("PageBack")).Click += (s, e) => ClosePage();
            ((Button)win.FindName("LogsBtn")).Click += (s, e) =>
            {
                try { Directory.CreateDirectory(Errors.LogDir); Process.Start(Explorer, Args.Quote(Errors.LogDir)); }
                catch (Exception ex) { ShowError(Errors.Register(Errors.Unexpected(ex, "ajuda", false), null, null)); }
            };
            ((Button)win.FindName("LicensesBtn")).Click += (s, e) => OpenLicenses();
            ((Button)win.FindName("PasteBtn")).Click += (s, e) => PasteLinks();
            ((Button)win.FindName("ErrCopy")).Click += (s, e) => CopyReport();
            ((Button)win.FindName("ErrReport")).Click += (s, e) => SendOrSaveReport();
            ((Button)win.FindName("ErrClose")).Click += (s, e) => HideSheet("ErrorSheet");
            linksBox.TextChanged += (s, e) => UpdateInputState();
            trimStartBox.TextChanged += (s, e) => UpdateInputState();
            trimEndBox.TextChanged += (s, e) => UpdateInputState();
            trimExactBox.Checked += (s, e) => UpdateInputState();
            trimExactBox.Unchecked += (s, e) => UpdateInputState();
            defaults.PropertyChanged += (s, e) => UpdateInputState();

            downloadBtn.Click += (s, e) => AddLinks();
            ((Button)win.FindName("FolderBtn")).Click += (s, e) => OpenFolder(null);
            ((Button)win.FindName("ChangeDestBtn")).Click += (s, e) => PickDest();
            pauseAllBtn = (Button)win.FindName("PauseAllBtn");
            pauseAllBtn.Click += (s, e) => TogglePauseAll();
            ((Button)win.FindName("ClearBtn")).Click += (s, e) =>
            {
                foreach (var it in items.Where(i => i.State == "ok" || i.State == "erro" || i.State == "cancelado").ToList()) { CleanWork(it); items.Remove(it); }
            };
            win.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { if (downloadBtn.IsEnabled) AddLinks(); e.Handled = true; }
                else if (e.Key == Key.Escape)
                {
                    var err = (FrameworkElement)win.FindName("ErrorSheet");
                    var page = (FrameworkElement)win.FindName("PagePanel");
                    if (err.Visibility == Visibility.Visible) err.Visibility = Visibility.Collapsed;
                    else if (page.Visibility == Visibility.Visible) ClosePage();
                }
            };
            win.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnTaggedButton));

            win.TaskbarItemInfo = new TaskbarItemInfo();
            win.SourceInitialized += (s, e) => StyleTitleBar();
            win.Loaded += (s, e) =>
            {
                LoadHistory();
                UpdateDestText();
                UpdateEmpty();
                UpdateInputState();
                UpdateLayoutMode();
                UpdateWindowState();
                footer.Text = "Downloader " + Version.Number;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s2, e2) => { if (themeMode == "system" && e2.Category == Microsoft.Win32.UserPreferenceCategory.General) Ui(ApplyTheme); };
                ((FrameworkElement)win.FindName("NewsDot")).Visibility = seenVersion != Version.Number && seenVersion != null ? Visibility.Visible : Visibility.Collapsed;
                if (seenVersion == null) { seenVersion = Version.Number; SaveConfig(); }
                if (Setup.InstallAction != null) ShowInstallSheet();
                else AfterInstallSheet();
            };
            win.Closing += OnClosing;
            win.Closing += (s, e) =>
            {
                savedMaximized = win.WindowState == WindowState.Maximized;
                var size = win.RestoreBounds;
                if (!size.IsEmpty) { savedWidth = size.Width; savedHeight = size.Height; }
            };
            win.Closed += (s, e) => { KillPreview(); SaveConfig(); };
            win.Deactivated += (s, e) => CloseMenu();
            win.LocationChanged += (s, e) => CloseMenu();
            win.SizeChanged += (s, e) => { CloseMenu(); UpdateLayoutMode(); };

            app.Run(win);
        }

        static void AfterInstallSheet()
        {
            if (customDest == null) ShowFirstRun();
            Startup();
        }

        static void ShowInstallSheet()
        {
            bool update = Setup.InstallAction == "update";
            ((TextBlock)win.FindName("InstallTitle")).Text = update ? "Atualizar o Downloader?" : "Instalar o Downloader?";
            ((TextBlock)win.FindName("InstallText")).Text = update
                ? "Você já tem a versão " + Setup.InstalledVersion + " instalada. Atualizar para a " + Version.Number + "? Sua pasta, opções e biblioteca continuam."
                : "Ele fica no menu Iniciar e na área de trabalho, e aparece em Configurações > Aplicativos para desinstalar. Não precisa de administrador.";
            ((Button)win.FindName("InstallOk")).Content = update ? "Atualizar" : "Instalar";
            ((FrameworkElement)win.FindName("InstallSheet")).Visibility = Visibility.Visible;
        }

        static void HideSheet(string name) { ((FrameworkElement)win.FindName(name)).Visibility = Visibility.Collapsed; }

        static void DoInstall()
        {
            string err = Setup.Install();
            if (err != null)
            {
                MessageBox.Show(win, "Não consegui instalar: " + err, "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            singleInstance.ReleaseMutex();
            singleInstance.Dispose();
            try { Process.Start(Setup.InstalledExe, "--wait"); } catch { }
            win.Closing -= OnClosing;
            Application.Current.Shutdown();
        }

        static void OpenLicenses()
        {
            try
            {
                Setup.WriteNotices();
                Process.Start(Path.Combine(Bin, "THIRD-PARTY-NOTICES.txt"));
            }
            catch (Exception ex) { ShowError(Errors.Register(Errors.Unexpected(ex, "ajuda", false), null, null)); }
        }

        static void Startup()
        {
            CleanOldTemp();
            updating = true;
            footer.Text = "Preparando componentes…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = Setup.EnsureComponents(msg => Ui(() => footer.Text = msg));
                if (err != null)
                {
                    var info = Errors.Register(Errors.Components(err), null, err);
                    Ui(() =>
                    {
                        footer.Text = "Downloads indisponíveis: componentes com problema (" + info.Code + ")";
                        ShowError(info);
                    });
                    return;
                }
                if (!ToolWorks())
                {
                    Errors.Write("WARN", "yt-dlp nao executa; restaurando a copia embutida");
                    Ui(() => footer.Text = "Restaurando o motor de download…");
                    string restore = Setup.RestoreYtDlp();
                    if (restore != null || !ToolWorks())
                    {
                        var bad = Errors.Register(Errors.Components(restore ?? "yt-dlp nao executa"), null, restore ?? "yt-dlp nao executa");
                        Ui(() =>
                        {
                            footer.Text = "Downloads indisponíveis: componentes com problema (" + bad.Code + ")";
                            ShowError(bad);
                        });
                        return;
                    }
                }
                string msg2 = "Não deu para verificar atualizações do motor de download";
                Ui(() => footer.Text = "Verificando atualizações do motor de download…");
                var update = new StringBuilder();
                int updateCode = RunTool("-U", UpdateTimeoutMs, l => update.AppendLine(l));
                if (updateCode == 0)
                {
                    bool updated = update.ToString().Contains("Updated");
                    msg2 = updated ? "Motor de download atualizado agora" : "Motor de download em dia";
                    if (updated && !ToolWorks())
                    {
                        Errors.Write("WARN", "yt-dlp atualizado nao executa; restaurando a copia embutida");
                        Setup.RestoreYtDlp();
                        ToolWorks();
                    }
                }
                else Errors.Write("WARN", "yt-dlp -U terminou com codigo " + updateCode);
                Ui(() =>
                {
                    updating = false;
                    footer.Text = "Downloader " + Version.Number;
                    footer.ToolTip = msg2;
                    StartNext();
                    SyncPreviews();
                    RefreshSuggestions(null);
                });
            });
        }

        static bool ToolWorks()
        {
            if (!Setup.LooksExecutable(Path.Combine(Bin, "yt-dlp.exe"))) return false;
            string version = null;
            int code = RunTool("--version", ToolTimeoutMs, l => { if (version == null) version = l.Trim(); });
            if (code != 0 || string.IsNullOrEmpty(version)) return false;
            Errors.ToolVersion = version;
            return true;
        }

        static int RunTool(string args, int timeoutMs, Action<string> onLine)
        {
            try
            {
                using (var p = Run(Path.Combine(Bin, "yt-dlp.exe"), args))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (p.WaitForExit(timeoutMs))
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                    KillTree(p);
                    p.WaitForExit(KillWaitMs);
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Errors.Write("WARN", "yt-dlp " + args + ": " + ex.Message);
                return -1;
            }
        }

        static bool IsLink(string token)
        {
            Uri u;
            return Uri.TryCreate(token, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https") && u.Host.Contains(".");
        }

        static string[] Tokens(string text) { return text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); }

        static void SetHint(TextBlock t, string text, bool problem)
        {
            t.Text = text;
            t.SetResourceReference(TextBlock.ForegroundProperty, problem ? "Warn" : "Muted");
            t.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        static string ReadTrim(out double? start, out double? end)
        {
            start = end = null;
            string a = trimStartBox.Text.Trim(), b = trimEndBox.Text.Trim();
            if (a.Length == 0 && b.Length == 0) return "Informe o início, o fim ou os dois. Exemplo: 03:15 e 05:40.";
            if (a.Length > 0) { start = TimeText.Parse(a); if (start == null) return "Início inválido. Use minutos:segundos, como 03:15."; }
            if (b.Length > 0) { end = TimeText.Parse(b); if (end == null) return "Fim inválido. Use minutos:segundos, como 05:40."; }
            if (end != null && (start ?? 0) >= end) return "O fim precisa ser depois do início.";
            return null;
        }

        static void UpdateInputState()
        {
            if (win == null || downloadBtn == null) return;
            var tokens = Tokens(linksBox.Text);
            var links = tokens.Where(IsLink).ToList();
            int bad = tokens.Length - links.Count;

            SetHint(linkHint, bad > 0 ? "Há texto que não parece link: ele não será baixado e fica na caixa." : "", bad > 0);

            bool part = defaults.TrimMode == "part";
            string trimProblem = null;
            if (part)
            {
                double? s, e;
                trimProblem = ReadTrim(out s, out e);
                if (trimProblem == null && links.Count > 1) trimProblem = "O trecho vale para um link por vez. Deixe só um link ou escolha “Vídeo completo”.";
                if (trimProblem != null) SetHint(trimHint, trimProblem, true);
                else
                {
                    string range = "Vai baixar de " + TimeText.Format(s ?? 0) + " até " + (e == null ? "o fim do vídeo" : TimeText.Format(e.Value)) +
                                   (e == null ? "" : " (" + TimeText.Long(e.Value - (s ?? 0)) + ")");
                    bool audio = defaults.Kind == "audio";
                    SetHint(trimHint, range + (audio ? "." : trimExactBox.IsChecked == true
                        ? ".  Corte exato: o vídeo é convertido para H.264."
                        : ".  Corte rápido: mantém a qualidade original, mas pode começar alguns segundos antes."), false);
                }
            }
            ((FrameworkElement)win.FindName("TrimExactBox")).Visibility = defaults.Kind == "audio" ? Visibility.Collapsed : Visibility.Visible;

            string login = defaults.Login;
            if (login == "none") SetHint(loginHint, "", false);
            else if (login == "file") SetHint(loginHint, cookieFile == null ? "Escolha o arquivo de cookies." : "Arquivo: " + Path.GetFileName(cookieFile) + ". Só é usado se o site pedir. Trate esse arquivo como uma senha.", cookieFile == null);
            else SetHint(loginHint, "O login do " + Errors.BrowserName(login) + " só é usado se o site pedir.", false);

            downloadBtn.IsEnabled = links.Count > 0 && trimProblem == null;
            downloadBtn.ToolTip = links.Count == 0 ? "Cole um link primeiro" : trimProblem ?? "Ctrl+Enter";

            RefreshPreviewSizes();
            if (links.Count == 0) SyncPreviews();
            else { previewTimer.Stop(); previewTimer.Start(); }
        }

        static string PreviewArgs()
        {
            string sel = SelectArgs(defaults);
            if (defaults.Kind == "split") sel = sel.Replace("\"bv/bv*/b\"", "\"bv+ba/b\"");
            if (defaults.Kind == "audio" && defaults.Fmt == "m4a") sel += "-S \"acodec:aac\" ";
            return sel;
        }

        static string PreviewKey(string url) { return url + "|" + PreviewArgs(); }

        static void SyncPreviews()
        {
            if (previewTimer != null) previewTimer.Stop();
            var links = Tokens(linksBox.Text).Where(IsLink).Distinct().ToList();
            var wanted = links.Take(MaxPreviews).ToList();
            foreach (var old in previews.Where(p => !wanted.Contains(p.Url)).ToList()) previews.Remove(old);
            for (int i = 0; i < wanted.Count; i++)
            {
                string url = wanted[i];
                var p = previews.FirstOrDefault(x => x.Url == url);
                if (p == null) previews.Insert(i, new Preview { Url = url, Title = url, Site = SiteName(url), Busy = true });
                else if (previews.IndexOf(p) != i) previews.Move(previews.IndexOf(p), i);
            }
            foreach (var key in probeCache.Where(kv => kv.Value.Failed && !wanted.Any(u => kv.Key.StartsWith(u + "|"))).Select(kv => kv.Key).ToList())
                probeCache.Remove(key);
            RefreshPreviewSizes();
            PumpPreviews();
        }

        static void PumpPreviews()
        {
            if (updating) return;
            var keys = previews.Select(p => PreviewKey(p.Url)).ToList();
            if (previewBusy)
            {
                if (!keys.Contains(previewKey)) KillPreview();
                return;
            }
            var next = previews.FirstOrDefault(p => !probeCache.ContainsKey(PreviewKey(p.Url)));
            if (next == null) return;

            previewBusy = true;
            previewKilled = false;
            previewKey = PreviewKey(next.Url);
            string key = previewKey;
            string args = "--simulate --no-warnings --playlist-items 1 " + PreviewArgs() + ToolArgs(new Item()) +
                          "--print " + Args.Quote(ProbeData.Template) + " " + Args.Quote(next.Url);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ProbeData data = null;
                string error = null;
                try
                {
                    var p = Run(Path.Combine(Bin, "yt-dlp.exe"), args);
                    previewProc = p;
                    p.OutputDataReceived += (s, e) => { if (e.Data != null && e.Data.StartsWith("PREVIEW|")) data = ProbeData.Parse(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null && e.Data.StartsWith("ERROR:")) error = e.Data.Substring(6).Trim(); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(PreviewTimeoutMs)) { KillTree(p); p.WaitForExit(KillWaitMs); }
                    else p.WaitForExit();
                }
                catch (Exception ex) { error = ex.Message; }
                Ui(() =>
                {
                    previewBusy = false;
                    previewProc = null;
                    if (!previewKilled)
                    {
                        if (data == null) data = new ProbeData { Failed = true, NeedsLogin = error != null && Errors.NeedsLogin(error) };
                        probeCache[key] = data;
                        RefreshPreviewSizes();
                    }
                    PumpPreviews();
                });
            });
        }

        static void KillPreview()
        {
            var p = previewProc;
            previewKilled = true;
            try { if (p != null && !p.HasExited) KillTree(p); } catch { }
        }

        static void RefreshPreviewSizes()
        {
            double? start = null, end = null;
            bool part = defaults.TrimMode == "part" && ReadTrim(out start, out end) == null;
            bool exact = trimExactBox.IsChecked == true;
            int links = Tokens(linksBox.Text).Where(IsLink).Distinct().Count();
            double total = 0;
            bool anyUnknown = links > previews.Count, anyBusy = false, anyApprox = false;

            foreach (var p in previews)
            {
                ProbeData d;
                p.Size = null;
                if (!probeCache.TryGetValue(PreviewKey(p.Url), out d))
                {
                    p.Busy = true;
                    p.SizeText = "Calculando…";
                    anyBusy = true;
                    continue;
                }
                p.Busy = false;
                if (!d.Failed)
                {
                    if (!string.IsNullOrEmpty(d.Title) && d.Title != "NA") p.Title = d.Title;
                    if (!string.IsNullOrEmpty(d.Site) && d.Site != "NA") p.Site = d.Site;
                    if (d.ChannelUrl != null && d.ChannelUrl.StartsWith("http")) p.ChannelUrl = d.ChannelUrl;
                    if (!string.IsNullOrEmpty(d.Channel) && d.Channel != "NA") p.Channel = d.Channel;
                    p.Duration = d.Duration > 0 && !d.Playlist ? TimeText.Format(d.Duration) : "";
                    if (d.Thumb != null && d.Thumb.StartsWith("http") && d.Thumb != p.ThumbUrl)
                    {
                        var target = p;
                        target.ThumbUrl = d.Thumb;
                        FetchThumb(d.Thumb, f => target.Thumb = f);
                    }
                }
                p.Size = Estimate.For(d, defaults.Kind, defaults.Fmt, defaults.AudioFmt, part ? start : null, part ? end : null, exact);
                if (p.Size != null)
                {
                    p.SizeText = p.Size.Text;
                    total += p.Size.Bytes;
                    anyApprox |= p.Size.Approx;
                }
                else
                {
                    anyUnknown = true;
                    p.SizeText = d.NeedsLogin ? "Exige login" : d.Live ? "Ao vivo" : d.Playlist ? "Vários vídeos" : "Tamanho desconhecido";
                }
            }

            if (links < 2 || anyBusy || total <= 0) totalText.Text = "";
            else totalText.Text = (anyUnknown ? "Pelo menos " : "Total ") + (anyApprox || anyUnknown ? "≈ " : "") + Estimate.FormatBytes(total);
            ((FrameworkElement)win.FindName("PreviewPanel")).Visibility = previews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var more = (TextBlock)win.FindName("PreviewMore");
            more.Text = links > previews.Count ? "E mais " + (links - previews.Count) + " links. O tamanho deles aparece ao baixar." : "";
            more.Visibility = more.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static void OnPreviewButton(Preview p, string tag)
        {
            if (tag == "p:link") OpenLink(p.Url);
            else if (tag == "p:remove")
                linksBox.Text = string.Join("\r\n", Tokens(linksBox.Text).Where(t => t != p.Url));
        }

        static void OpenLink(string url)
        {
            if (!IsLink(url)) return;
            try { Process.Start(url); } catch { }
        }

        static void PasteLinks()
        {
            try
            {
                string clip = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clip)) return;
                string cur = linksBox.Text.TrimEnd();
                linksBox.Text = (cur.Length > 0 ? cur + "\r\n" : "") + clip.Trim();
                linksBox.Focus();
                linksBox.CaretIndex = linksBox.Text.Length;
            }
            catch { }
        }

        static void AddLinks()
        {
            var tokens = Tokens(linksBox.Text);
            var urls = tokens.Where(IsLink).Distinct().ToList();
            if (urls.Count == 0) { linksBox.Focus(); return; }

            double? start = null, end = null;
            if (defaults.TrimMode == "part" && (urls.Count > 1 || ReadTrim(out start, out end) != null)) return;

            int skipped = 0;
            var active = new HashSet<string>(items.Where(i => (i.State == "fila" || i.State == "baixando") && i.TrimStart == start && i.TrimEnd == end).Select(i => VideoKey(i.Url)));
            foreach (var u in urls)
            {
                if (!active.Add(VideoKey(u))) { skipped++; continue; }
                var it = new Item { Url = u, Title = u, Site = SiteName(u), Status = "Na fila", State = "fila", TrimStart = start, TrimEnd = end };
                it.TrimExact = trimExactBox.IsChecked == true;
                it.CopyOptions(defaults);
                var seen = previews.FirstOrDefault(p => p.Url == u);
                if (seen != null)
                {
                    it.Title = seen.Title; it.Site = seen.Site; it.Thumb = seen.Thumb; it.ThumbUrl = seen.ThumbUrl;
                    it.Channel = seen.Channel; it.ChannelUrl = seen.ChannelUrl;
                    if (seen.Size != null) it.Status = "Na fila  ·  " + seen.Size.Text;
                }
                items.Add(it);
                queue.Enqueue(it);
            }
            linksBox.Text = string.Join("\r\n", tokens.Where(t => !IsLink(t)));
            if (defaults.TrimMode == "part")
            {
                defaults.TrimMode = "full";
                trimStartBox.Clear(); trimEndBox.Clear();
            }
            UpdateInputState();
            if (skipped > 0) SetHint(linkHint, skipped == 1 ? "Esse vídeo já está na fila." : skipped + " vídeos já estavam na fila.", true);
            StartNext();
        }

        static void ResetForRun(Item it)
        {
            it.Files.Clear(); it.Height = null; it.LastError = null; it.Cancelled = false; it.Paused = false;
            it.Err = null; it.Preflight = null; it.Progress = 0; it.Busy = false; it.StreamIdx = 0; it.Tail.Clear(); it.AlreadyThere = false;
            it.State = "fila"; it.Status = "Na fila";
        }

        static void Retry(Item it)
        {
            if (it.State == "fila" || it.State == "baixando") return;
            if (it.State == "ok") it.Force = true;
            ResetForRun(it);
            queue.Enqueue(it);
            StartNext();
        }

        static void RunErrorAction(Item it)
        {
            string action = it.Err == null ? null : it.Err.Action;
            if (action == "edit")
            {
                string cur = linksBox.Text.TrimEnd();
                linksBox.Text = (cur.Length > 0 ? cur + "\r\n" : "") + it.Url;
                defaults.CopyOptions(it);
                defaults.TrimMode = it.HasTrim ? "part" : "full";
                trimStartBox.Text = it.TrimStart == null ? "" : TimeText.Format(it.TrimStart.Value);
                trimEndBox.Text = it.TrimEnd == null ? "" : TimeText.Format(it.TrimEnd.Value);
                trimExactBox.IsChecked = it.TrimExact;
                CleanWork(it);
                items.Remove(it);
                tabDownloads.IsChecked = true;
                linksBox.Focus();
            }
            else if (action == "login:firefox") { it.Login = "firefox"; Retry(it); }
            else if (action == "login:file")
            {
                if (!PickCookieFile()) return;
                it.Login = "file";
                Retry(it);
            }
        }

        static bool PickCookieFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Escolha o arquivo de cookies (formato Netscape)", Filter = "Arquivo de cookies (*.txt)|*.txt|Todos os arquivos (*.*)|*.*" };
            if (dlg.ShowDialog(win) != true) return false;
            cookieFile = dlg.FileName;
            SaveConfig();
            UpdateInputState();
            return true;
        }

        static void Pause(Item it)
        {
            if (!it.CanPause || it.Paused) return;
            it.Paused = true;
            it.Status = "Pausando…";
            if (it.Proc != null) KillTree(it.Proc);
        }

        static void Resume(Item it)
        {
            ResetForRun(it);
            it.Force = false;
            var rest = queue.Where(q => q != it).ToList();
            queue.Clear();
            queue.Enqueue(it);
            foreach (var q in rest) queue.Enqueue(q);
            if (queuePaused) SetQueuePaused(false);
            StartNext();
        }

        static void TogglePauseAll()
        {
            if (!queuePaused)
            {
                SetQueuePaused(true);
                if (current != null) Pause(current);
            }
            else
            {
                SetQueuePaused(false);
                var paused = items.Where(i => i.State == "pausado").ToList();
                var rest = queue.Where(q => !paused.Contains(q)).ToList();
                queue.Clear();
                foreach (var it in paused)
                {
                    ResetForRun(it);
                    it.Force = false;
                    queue.Enqueue(it);
                }
                foreach (var q in rest) queue.Enqueue(q);
                StartNext();
            }
        }

        static void SetQueuePaused(bool value)
        {
            queuePaused = value;
            pauseAllBtn.Content = value ? "Retomar tudo" : "Pausar tudo";
            UpdateTaskbar();
        }

        static void StartNext()
        {
            if (updating || queuePaused || current != null || queue.Count == 0) { UpdateTaskbar(); return; }
            var it = queue.Dequeue();
            current = it;
            it.State = "baixando"; it.Status = it.HasTrim ? "Verificando o vídeo e o trecho…" : "Obtendo informações do vídeo…";
            it.Busy = true; it.Progress = 0; it.Started = DateTime.Now;
            if (it.WorkDir == null) it.WorkDir = Path.Combine(TempDir, "job-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            UpdateTaskbar();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int code = -1;
                try { code = RunItem(it); }
                catch (Exception ex)
                {
                    it.LastError = ex.Message;
                    if (ex is System.ComponentModel.Win32Exception) it.Preflight = Errors.Components(ex.Message);
                }
                try { File.Delete(Path.Combine(it.WorkDir, "cookies.txt")); } catch { }
                Ui(() => Finish(it, code));
            });
        }

        static int RunItem(Item it)
        {
            Directory.CreateDirectory(it.WorkDir);
            it.DestDir = Dest;
            try { Directory.CreateDirectory(it.DestDir); } catch { }
            if (!Directory.Exists(it.DestDir)) { it.Preflight = Errors.DestUnavailable(it.DestDir); return -1; }

            int code = -1;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                it.UseCookies = attempt == 1;
                it.LastError = null;
                if (it.UseCookies && it.Login == "file" && (cookieFile == null || !File.Exists(cookieFile)))
                {
                    it.LastError = "cookie file missing";
                    return -1;
                }
                code = it.HasTrim ? Probe(it) : 0;
                if (code == 0 && it.Preflight == null) code = RunSteps(it);
                if (code == 0 || it.Preflight != null || it.Cancelled || it.Paused) break;
                if (attempt == 1 || it.Login == "none" || !Errors.NeedsLogin(it.LastError)) break;
                string who = Errors.BrowserName(it.Login);
                Ui(() => { it.Busy = true; it.Status = "Este conteúdo exige login. Usando " + (it.Login == "file" ? "o arquivo de cookies" : "o login do " + who) + "…"; });
            }
            return code;
        }

        static int Probe(Item it)
        {
            string args = "--simulate --no-warnings " + SelectArgs(it) + ToolArgs(it) +
                "--print \"video:INFO|%(extractor_key)s|%(thumbnail)s|%(channel_url,uploader_url|)s|%(channel,uploader|)s|%(title)s\" " +
                "--print \"PROBE|%(duration|)s|%(live_status|)s|%(height|)s\" " + Args.Quote(it.Url);
            it.ProbeDuration = 0; it.ProbeLive = null; it.ProbeHeight = null;
            int code = Pump(it, args);
            if (code != 0 || it.Cancelled || it.Paused) return code;

            if (it.ProbeLive == "is_live" || it.ProbeLive == "is_upcoming") { it.Preflight = Errors.TrimLive(); return -1; }
            double d = it.ProbeDuration;
            if (d > 0 && ((it.TrimStart ?? 0) >= d || (it.TrimEnd != null && it.TrimEnd > d + 1)))
            {
                it.Preflight = Errors.TrimBeyond(TimeText.Format(d));
                return -1;
            }
            return 0;
        }

        static int RunSteps(Item it)
        {
            int code = -1;
            foreach (var step in BuildSteps(it))
            {
                if (it.Cancelled || it.Paused) break;
                it.Phase = step[0];
                it.StreamIdx = 0; it.Streams = 1;
                code = Pump(it, step[1]);
                if (code != 0) break;
            }
            return code;
        }

        static int Pump(Item it, string args)
        {
            var p = Run(Path.Combine(Bin, "yt-dlp.exe"), args, it.DestDir);
            it.Proc = p;
            if (it.Cancelled || it.Paused) KillTree(p);
            p.OutputDataReceived += (s, e) => { if (e.Data != null) { var l = e.Data; Ui(() => OnLine(it, l)); } };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) { var l = e.Data; Ui(() => OnLine(it, l)); } };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            win.Dispatcher.Invoke(new Action(() => { }));
            return p.ExitCode;
        }

        static string SelectArgs(Item it)
        {
            if (it.Kind == "audio") return "-f \"ba/b\" ";
            bool mp4 = it.Fmt == "mp4";
            string res = it.Quality == "max" ? "res" : "res:" + it.Quality;
            string sort = mp4 ? "-S \"vcodec:h264," + res + ",fps,acodec:aac\" "
                              : (it.Quality == "max" ? "" : "-S \"" + res + "\" ");
            return (it.Kind == "split" ? "-f \"bv/bv*/b\" " : "-f \"bv*+ba/b\" ") + sort;
        }

        static List<string[]> BuildSteps(Item it)
        {
            var steps = new List<string[]>();
            bool mp4 = it.Fmt == "mp4";
            string name = NameTemplate + TrimSuffix(it) + (it.UseId ? " [%(id)s]" : "");

            if (it.Kind == "video")
            {
                steps.Add(new[] { "", SelectArgs(it) +
                    (mp4 ? "--merge-output-format mp4 --remux-video mp4 " : "--merge-output-format mkv --remux-video mkv ") +
                    TrimArgs(it, true) + Common(it, name) });
            }
            else if (it.Kind == "split")
            {
                steps.Add(new[] { "Vídeo", SelectArgs(it) + "--remux-video " + (mp4 ? "mp4 " : "mkv ") +
                    TrimArgs(it, true) + Common(it, name + " (vídeo)") });
                steps.Add(new[] { "Áudio", "-f \"ba/b\" " + AudioArgs(it.AudioFmt) + TrimArgs(it, false) + Common(it, name + " (áudio)") });
            }
            else
            {
                steps.Add(new[] { "", SelectArgs(it) + AudioArgs(it.Fmt) + TrimArgs(it, false) + Common(it, name) });
            }
            return steps;
        }

        static string TrimSuffix(Item it)
        {
            if (!it.HasTrim) return "";
            Func<double, string> f = v => { var t = TimeSpan.FromSeconds(v); return (t.TotalHours >= 1 ? (int)t.TotalHours + "h" : "") + t.ToString(@"mm\mss\s"); };
            return " (trecho " + f(it.TrimStart ?? 0) + "-" + (it.TrimEnd == null ? "fim" : f(it.TrimEnd.Value)) + ")";
        }

        static string TrimArgs(Item it, bool video)
        {
            if (!it.HasTrim) return "";
            string range = "*" + (it.TrimStart ?? 0).ToString(CultureInfo.InvariantCulture) + "-" +
                           (it.TrimEnd == null ? "inf" : it.TrimEnd.Value.ToString(CultureInfo.InvariantCulture));
            string a = "--download-sections \"" + range + "\" ";
            if (video && it.TrimExact)
            {
                int h;
                int.TryParse(it.ProbeHeight, out h);
                string rate = Estimate.TrimRate(h);
                a +="--force-keyframes-at-cuts --downloader-args \"ffmpeg_o:-c:v libopenh264 -b:v " + rate + " -c:a aac -b:a 192k\" ";
            }
            return a;
        }

        static string AudioArgs(string fmt)
        {
            switch (fmt)
            {
                case "mp3": return "-x --audio-format mp3 --audio-quality 320K ";
                case "wav": return "-x --audio-format wav ";
                case "m4a": return "-S \"acodec:aac\" -x --audio-format m4a ";
                default: return "-x ";
            }
        }

        static string ToolArgs(Item it)
        {
            var a = new StringBuilder();
            if (it.UseCookies)
            {
                if (it.Login == "file")
                {
                    string copy = Path.Combine(it.WorkDir, "cookies.txt");
                    File.Copy(cookieFile, copy, true);
                    a.Append("--cookies " + Args.Quote(copy) + " ");
                }
                else if (it.Login != "none") a.Append("--cookies-from-browser " + it.Login + " ");
            }
            a.Append("--encoding utf-8 --no-playlist --no-colors --newline ");
            a.Append("--js-runtimes " + Args.Quote("deno:" + Path.Combine(Bin, "deno.exe")) + " ");
            return a.ToString();
        }

        static string Common(Item it, string name)
        {
            var a = new StringBuilder(ToolArgs(it));
            if (it.Force) a.Append("--force-overwrites ");
            if (!it.HasTrim) a.Append("--live-from-start ");
            a.Append("--no-quiet --progress ");
            a.Append("--ffmpeg-location " + Args.Quote(Bin) + " ");
            a.Append("-P " + Args.Quote("temp:" + it.WorkDir) + " ");
            a.Append("-o " + Args.Quote(name + ".%(ext)s") + " ");
            a.Append("--print \"video:INFO|%(extractor_key)s|%(thumbnail)s|%(channel_url,uploader_url|)s|%(channel,uploader|)s|%(title)s\" ");
            a.Append("--print \"after_move:OK|%(height|)s|%(filepath)s\" ");
            a.Append("--progress-template \"download:PCT|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s\" ");
            a.Append(Args.Quote(it.Url));
            return a.ToString();
        }

        static string DownloadLabel(Item it)
        {
            if (it.HasTrim) return "Baixando o trecho";
            if (it.Phase == "Vídeo") return it.Streams > 1 ? "Baixando vídeo" : "Baixando vídeo (arquivo 1 de 2)";
            if (it.Phase == "Áudio") return "Baixando áudio (arquivo 2 de 2)";
            if (it.Kind == "audio") return "Baixando áudio";
            if (it.Streams > 1) return it.StreamIdx <= 1 ? "Baixando vídeo (parte 1 de 2)" : "Baixando áudio (parte 2 de 2)";
            return "Baixando";
        }

        static void Processing(Item it, string text)
        {
            it.Busy = true;
            it.Status = text;
            UpdateTaskbar();
        }

        static void OnLine(Item it, string line)
        {
            if (it.Paused && it.Status == "Pausando…") { if (line.StartsWith("ERROR:")) it.LastError = line.Substring(6).Trim(); return; }

            if (line.StartsWith("PCT|"))
            {
                if (it.HasTrim) return;
                var c = line.Split('|');
                var m = Regex.Match(c[1], @"([\d.]+)%");
                if (m.Success)
                {
                    double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    it.Busy = false;
                    it.Progress = v;
                    string speed = c[2].Trim(), eta = c.Length > 3 ? c[3].Trim() : "";
                    it.Status = DownloadLabel(it) + string.Format(CultureInfo.CurrentCulture, "  ·  {0:0}%", v) +
                        (speed.Length > 0 && !speed.StartsWith("Unknown") && speed != "NA" ? "  ·  " + speed.Replace("/s", "/s").Replace("MiB", "MB").Replace("KiB", "KB").Replace("GiB", "GB") : "") +
                        (eta.Length > 0 && eta != "Unknown" && eta != "NA" ? "  ·  faltam " + eta : "");
                    UpdateTaskbar();
                }
                return;
            }

            if (line.StartsWith("frame=") || line.StartsWith("size=") || (it.HasTrim && line.Contains(" time=")))
            {
                var t = Regex.Match(line, @"time=(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (it.HasTrim && t.Success)
                {
                    double done = int.Parse(t.Groups[1].Value) * 3600 + int.Parse(t.Groups[2].Value) * 60 + double.Parse(t.Groups[3].Value, CultureInfo.InvariantCulture);
                    double total = (it.TrimEnd ?? it.ProbeDuration) - (it.TrimStart ?? 0);
                    if (total > 0)
                    {
                        double pct = Math.Min(100, done * 100 / total);
                        it.Busy = false;
                        it.Progress = pct;
                        var sp = Regex.Match(line, @"speed=\s*([\d.]+)x");
                        double speed = sp.Success ? double.Parse(sp.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                        string left = speed > 0.01 && total > done ? "  ·  faltam " + TimeText.Long((total - done) / speed) : "";
                        it.Status = (it.Phase.Length > 0 ? it.Phase + ": " : "") + "Baixando o trecho" + string.Format("  ·  {0:0}%", pct) + left;
                        UpdateTaskbar();
                    }
                }
                return;
            }

            if (line.StartsWith("[") || line.StartsWith("ERROR") || line.StartsWith("WARNING"))
            {
                it.Tail.Enqueue(line);
                while (it.Tail.Count > 40) it.Tail.Dequeue();
            }

            if (line.StartsWith("PROBE|"))
            {
                var c = line.Split('|');
                double d;
                if (c.Length > 3)
                {
                    if (double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) it.ProbeDuration = d;
                    it.ProbeLive = c[2];
                    it.ProbeHeight = c[3];
                }
            }
            else if (Regex.IsMatch(line, @"^\[info\] .*Downloading \d+ format\(s\):"))
            {
                it.Streams = line.Substring(line.LastIndexOf(':')).Count(ch => ch == '+') + 1;
                it.Status = it.HasTrim ? "Conectando para baixar o trecho…" : "Iniciando o download…";
            }
            else if (line.StartsWith("[download] Destination:")) it.StreamIdx++;
            else if (line.StartsWith("[download]") && line.EndsWith("has already been downloaded")) it.AlreadyThere = true;
            else if (line.StartsWith("[Merger]")) Processing(it, "Juntando vídeo e áudio…");
            else if (line.StartsWith("[ExtractAudio]")) Processing(it, "Convertendo o áudio…");
            else if (line.StartsWith("[VideoRemuxer]") || line.StartsWith("[VideoConvertor]")) Processing(it, "Preparando o arquivo final…");
            else if (line.StartsWith("[Fixup")) Processing(it, "Ajustando o arquivo…");
            else if (line.StartsWith("[MoveFiles]")) Processing(it, "Salvando na pasta…");
            else if (line.StartsWith("INFO|"))
            {
                var c = line.Split(new[] { '|' }, 6);
                if (c.Length == 6)
                {
                    it.Site = c[1] == "Youtube" ? "YouTube" : c[1];
                    if (c[2].StartsWith("http") && c[2] != it.ThumbUrl)
                    {
                        it.ThumbUrl = c[2];
                        FetchThumb(c[2], f => it.Thumb = f);
                    }
                    if (c[3].StartsWith("http")) it.ChannelUrl = c[3];
                    if (c[4].Length > 0 && c[4] != "NA") it.Channel = c[4];
                    it.Title = c[5];
                }
            }
            else if (line.StartsWith("OK|"))
            {
                var c = line.Split(new[] { '|' }, 3);
                if (c.Length == 3)
                {
                    if (c[1].Trim().Length > 0 && c[1].Trim() != "NA") it.Height = c[1].Trim();
                    it.Files.Add(Path.GetFullPath(Path.Combine(it.DestDir ?? Dest, c[2].Trim())));
                }
            }
            else if (line.StartsWith("ERROR:"))
            {
                it.LastError = line.Substring(6).Trim();
            }
        }

        static void Finish(Item it, int code)
        {
            current = null;
            it.Proc = null;
            it.Busy = false;
            if (it.Cancelled)
            {
                it.Cancelled = false;
                CleanWork(it);
                it.State = "cancelado";
                it.Status = it.Files.Count > 0 ? "Cancelado  ·  o arquivo já concluído continua na pasta" : "Cancelado  ·  nada foi salvo";
                it.Progress = 0;
                Errors.Write("INFO", "cancelado url=" + Errors.SanitizeUrl(it.Url));
            }
            else if (it.Paused)
            {
                it.Paused = false;
                it.State = "pausado";
                it.Status = it.HasTrim
                    ? "Pausado  ·  ao continuar, o trecho recomeça do início"
                    : string.Format("Pausado em {0:0}%  ·  mude as opções se quiser e clique em continuar", it.Progress);
            }
            else if (code == 0 && it.Files.Count > 0 && it.Files.All(File.Exists) && NameTakenByOther(it))
            {
                it.UseId = true;
                it.Files.Clear();
                ResetForRun(it);
                var rest = queue.ToList();
                queue.Clear();
                queue.Enqueue(it);
                foreach (var q in rest) queue.Enqueue(q);
            }
            else if (code == 0 && it.Files.Count > 0 && it.Files.All(File.Exists))
            {
                it.State = "ok"; it.Progress = 100;
                CleanWork(it);
                bool existed = !it.Force && it.AlreadyThere;
                string what = it.Files.Count > 1 ? it.Files.Count + " arquivos  ·  " : "";
                string res = it.Height != null && it.Kind != "audio" ? it.Height + "p  ·  " : "";
                it.Status = (existed ? "Já estava na pasta  ·  " : "Salvo  ·  ") + res + what + Estimate.FormatBytes(it.Files.Sum(f => new FileInfo(f).Length));
                it.Force = false;
                AddHistory(it);
            }
            else
            {
                var err = it.Preflight ?? (code == 0 ? Errors.FileMissing() : Errors.Classify(it.LastError, it.Login, it.UseCookies, it.HasTrim));
                string tech = "Última mensagem: " + (it.LastError ?? "-") + "\nTipo: " + it.Kind + " | Qualidade: " + it.Quality + " | Formato: " + it.Fmt +
                              " | Login: " + it.Login + (it.UseCookies ? " (usado)" : " (não usado)") + " | " + (it.HasTrim ? it.TrimLabel + (it.TrimExact ? " exato" : " rápido") : "vídeo completo") +
                              "\nCódigo de saída: " + code + "\n" + string.Join("\n", it.Tail);
                it.Err = Errors.Register(err, it.Url, tech);
                it.State = "erro";
                it.Status = err.Title;
                if (it.HasTrim) CleanWork(it);
            }
            StartNext();
        }

        static bool NameTakenByOther(Item it)
        {
            if (!it.AlreadyThere || it.UseId || it.Force) return false;
            string key = VideoKey(it.Url);
            return !history.Any(h => VideoKey(h.Url) == key && h.Files.Intersect(it.Files, StringComparer.OrdinalIgnoreCase).Any());
        }

        static void CleanWork(Item it)
        {
            string dir = it.WorkDir;
            it.WorkDir = null;
            if (dir == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int i = 0; i < 5 && Directory.Exists(dir); i++)
                {
                    try { Directory.Delete(dir, true); }
                    catch { Thread.Sleep(600); }
                }
            });
        }

        static void CleanOldTemp()
        {
            try
            {
                Directory.CreateDirectory(TempDir);
                foreach (var d in Directory.GetDirectories(TempDir, "job-*")) try { Directory.Delete(d, true); } catch { }
                foreach (var f in Directory.GetFiles(TempDir)) try { File.Delete(f); } catch { }
                string cache = Path.Combine(TempDir, "thumbs");
                if (Directory.Exists(cache))
                    foreach (var f in new DirectoryInfo(cache).GetFiles().Where(x => x.LastWriteTime < DateTime.Now.AddDays(-ThumbCacheDays)))
                        try { f.Delete(); } catch { }
            }
            catch { }
        }

        static void ShowError(ErrorInfo e)
        {
            sheetError = e;
            ((TextBlock)win.FindName("ErrTitle")).Text = e.Title;
            ((TextBlock)win.FindName("ErrHeader")).Text = e.Fatal ? "O Downloader encontrou um problema" : "Detalhes do erro";
            ((TextBlock)win.FindName("ErrCause")).Text = e.Cause;
            ((TextBlock)win.FindName("ErrFixText")).Text = e.Fix;
            ((TextBlock)win.FindName("ErrMeta")).Text = "Código " + e.Code + "   ·   Identificador " + e.Incident + "   ·   " + e.When.ToString("dd/MM/yyyy HH:mm:ss");
            ((TextBox)win.FindName("ErrReportText")).Text = Errors.BuildReport(e);
            ((TextBlock)win.FindName("ErrStatus")).Text = "";
            bool canSend = !string.IsNullOrEmpty(Version.ReportUrl);
            ((Button)win.FindName("ErrReport")).Content = canSend ? "Enviar relatório" : "Salvar relatório…";
            string privacy = "O relatório acima é tudo que sai daqui: não tem cookies, senhas nem o seu nome de usuário.";
            if (canSend) privacy += " Enviar manda esse texto para " + new Uri(Version.ReportUrl).Host + ".";
            else if (!string.IsNullOrEmpty(Version.SupportContact)) privacy += " Envie para " + Version.SupportContact + ".";
            else privacy += " Nada é enviado automaticamente.";
            ((TextBlock)win.FindName("ErrPrivacy")).Text = privacy;
            ((FrameworkElement)win.FindName("ErrorSheet")).Visibility = Visibility.Visible;
        }

        static void ShowFatal(Exception ex, string component)
        {
            var e = Errors.Register(Errors.Unexpected(ex, component, true), null, null);
            if (win != null && win.IsLoaded) { ShowError(e); return; }
            MessageBox.Show("O Downloader não conseguiu abrir.\n\nCódigo: " + e.Code + "\nIdentificador: " + e.Incident + "\n\nOs detalhes estão em:\n" + Errors.LogDir,
                "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        static void CopyReport()
        {
            var status = (TextBlock)win.FindName("ErrStatus");
            try { Clipboard.SetText(Errors.BuildReport(sheetError)); status.Text = "Copiado."; }
            catch { status.Text = "Não consegui copiar. Selecione o texto e use Ctrl+C."; }
        }

        static void SendOrSaveReport()
        {
            var status = (TextBlock)win.FindName("ErrStatus");
            string report = Errors.BuildReport(sheetError);
            if (string.IsNullOrEmpty(Version.ReportUrl))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Salvar relatório de erro", Filter = "Texto (*.txt)|*.txt",
                    FileName = "Downloader-erro-" + sheetError.Incident + ".txt",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                if (dlg.ShowDialog(win) != true) return;
                try { File.WriteAllText(dlg.FileName, report, Encoding.UTF8); status.Text = "Salvo em " + Path.GetFileName(dlg.FileName) + "."; }
                catch (Exception ex) { status.Text = "Não consegui salvar: " + ex.Message; }
                return;
            }
            status.Text = "Enviando…";
            string id = sheetError.Incident;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string result;
                try
                {
                    using (var wc = new System.Net.WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[System.Net.HttpRequestHeader.ContentType] = "text/plain; charset=utf-8";
                        wc.UploadString(Version.ReportUrl, report);
                    }
                    result = "Enviado. Informe o identificador " + id + " se precisar falar com o suporte.";
                    Errors.Write("INFO", "relatorio enviado id=" + id);
                }
                catch (Exception ex)
                {
                    result = "Não consegui enviar. Use “Copiar detalhes”.";
                    Errors.Write("WARN", "falha ao enviar relatorio id=" + id + ": " + ex.Message);
                }
                Ui(() => status.Text = result);
            });
        }

        static void ShowPage(string page, string section)
        {
            pendingSection = section;
            var tab = page == "help" ? tabHelp : page == "legal" ? tabLegal : tabNews;
            if (tab.IsChecked == true) RenderPage(page);
            else tab.IsChecked = true;
        }

        static void ClosePage()
        {
            if (libraryOpen) tabLibrary.IsChecked = true;
            else tabDownloads.IsChecked = true;
        }

        static void RenderPage(string page)
        {
            string section = pendingSection;
            pendingSection = null;
            bool help = page == "help";
            bool legal = page == "legal";
            CloseMenu();
            ((TextBlock)win.FindName("PageTitle")).Text = help ? "Ajuda" : legal ? "Termos e políticas" : "Novidades";
            ((FrameworkElement)win.FindName("LogsBtn")).Visibility = help ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)win.FindName("LicensesBtn")).Visibility = page == "news" ? Visibility.Collapsed : Visibility.Visible;
            string text = ReadResource(help ? "ajuda.md" : legal ? "legal.md" : "CHANGELOG.md");
            OpenDoc(ParseDoc(FillLegalFields(text)), section);
            ((FrameworkElement)win.FindName("PagePanel")).Visibility = Visibility.Visible;
            if (page == "news")
            {
                seenVersion = Version.Number;
                SaveConfig();
                ((FrameworkElement)win.FindName("NewsDot")).Visibility = Visibility.Collapsed;
            }
        }

        static string FillLegalFields(string text)
        {
            string contact = string.IsNullOrEmpty(Version.SupportContact)
                ? "Esta versão não tem um canal de contato definido."
                : "Fale com o publicador em " + Version.SupportContact + ".";
            string report = string.IsNullOrEmpty(Version.ReportUrl)
                ? "Esta versão não envia relatórios: você pode copiar ou salvar o texto e decidir com quem compartilhar."
                : "Esta versão também pode enviar o relatório para " + new Uri(Version.ReportUrl).Host + ", somente quando você clica em Enviar relatório.";
            return text.Replace("{{CONTACT}}", contact).Replace("{{REPORT}}", report).Replace("{{VERSION}}", Version.Number);
        }

        static string ReadResource(string name)
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(st, Encoding.UTF8))
                return r.ReadToEnd();
        }

        class DocSection
        {
            public string Group, Title, Detail;
            public readonly List<string> Lines = new List<string>();
        }

        static List<DocSection> ParseDoc(string text)
        {
            var list = new List<DocSection>();
            string group = null;
            DocSection cur = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("# ")) { group = line.Substring(2); continue; }
                if (line.StartsWith("## "))
                {
                    var parts = line.Substring(3).Split(new[] { " | " }, 2, StringSplitOptions.None);
                    cur = new DocSection { Group = group, Title = parts[0], Detail = parts.Length > 1 ? parts[1] : null };
                    list.Add(cur);
                    continue;
                }
                if (cur != null) cur.Lines.Add(line);
            }
            return list;
        }

        static void OpenDoc(List<DocSection> sections, string wanted)
        {
            var index = (StackPanel)win.FindName("PageIndex");
            index.Children.Clear();
            string group = null;
            RadioButton first = null, target = null;
            foreach (var sec in sections)
            {
                if (sec.Group != null && sec.Group != group)
                {
                    group = sec.Group;
                    index.Children.Add(new TextBlock { Text = group, Style = (Style)win.FindResource("DocIndexGroup") });
                }
                var item = new RadioButton { Content = sec.Title, Style = (Style)win.FindResource("DocIndexItem") };
                var chosen = sec;
                item.Checked += (s, e) => RenderSection(chosen);
                index.Children.Add(item);
                if (first == null) first = item;
                if (sec.Title == wanted) target = item;
            }
            var open = target ?? first;
            if (open != null) open.IsChecked = true;
        }

        static void RenderSection(DocSection sec)
        {
            var host = (StackPanel)win.FindName("PageBody");
            host.Children.Clear();
            ((ScrollViewer)win.FindName("PageScroll")).ScrollToTop();

            host.Children.Add(new TextBlock { Text = sec.Title, Style = (Style)win.FindResource("DocH2") });
            DateTime date;
            string detail = sec.Detail;
            if (detail != null && DateTime.TryParse(detail, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) detail = date.ToString("dd/MM/yyyy");
            host.Children.Add(new TextBlock { Text = detail ?? "", Style = (Style)win.FindResource("DocLead"), Margin = new Thickness(0, 2, 0, 14), Visibility = detail == null ? Visibility.Collapsed : Visibility.Visible });

            StackPanel steps = null;
            foreach (var line in sec.Lines)
            {
                var number = Regex.Match(line, @"^(\d+)\. (.*)$");
                if (number.Success)
                {
                    if (steps == null)
                    {
                        steps = new StackPanel();
                        host.Children.Add(new Border { Style = (Style)win.FindResource("DocPanel"), Child = steps });
                    }
                    steps.Children.Add(StepRow(number.Groups[1].Value, number.Groups[2].Value));
                    continue;
                }
                steps = null;

                if (line.StartsWith("### ")) host.Children.Add(new TextBlock { Text = line.Substring(4), Style = (Style)win.FindResource("DocH3") });
                else if (line.StartsWith("> ")) host.Children.Add(Callout(line.Substring(2)));
                else if (line.StartsWith("- ")) host.Children.Add(BulletRow(line.Substring(2)));
                else host.Children.Add(DocParagraph(line));
            }
        }

        static TextBlock DocParagraph(string text)
        {
            var t = new TextBlock { Style = (Style)win.FindResource("DocText") };
            AddBold(t, text);
            return t;
        }

        static FrameworkElement StepRow(string number, string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var label = new TextBlock { Text = number, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentSoftText");
            var badge = new Border { Style = (Style)win.FindResource("DocStepNumber"), Child = label };
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            var body = DocParagraph(text);
            body.Margin = new Thickness(0, 1, 0, 0);
            row.Children.Add(body);
            return row;
        }

        static FrameworkElement BulletRow(string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var mark = new TextBlock { Text = "•", Width = 16, FontSize = 14 };
            mark.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            var body = DocParagraph(text);
            body.Margin = new Thickness(0);
            row.Children.Add(body);
            return row;
        }

        static FrameworkElement Callout(string text)
        {
            var body = DocParagraph(text);
            body.Margin = new Thickness(0);
            return new Border { Style = (Style)win.FindResource("DocCallout"), Child = body };
        }

        static void AddBold(TextBlock t, string text)
        {
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var run = new System.Windows.Documents.Run(parts[i]);
                if (i % 2 == 1) run.FontWeight = FontWeights.SemiBold;
                t.Inlines.Add(run);
            }
        }

        static void ShowTab(bool library)
        {
            var show = library ? Visibility.Collapsed : Visibility.Visible;
            var hide = library ? Visibility.Visible : Visibility.Collapsed;
            libraryOpen = library;
            ((TextBlock)win.FindName("ViewTitle")).Text = library ? "Biblioteca" : "Downloads";
            ((FrameworkElement)win.FindName("PagePanel")).Visibility = Visibility.Collapsed;
            ((FrameworkElement)win.FindName("DownloadsView")).Visibility = show;
            ((FrameworkElement)win.FindName("CopyrightBar")).Visibility = show;
            ((FrameworkElement)win.FindName("QueueActions")).Visibility = show;
            ((FrameworkElement)win.FindName("InputCard")).Visibility = show;
            ((FrameworkElement)win.FindName("LibraryView")).Visibility = hide;
            searchBox.Visibility = hide;
            CloseMenu();
            if (library) CheckLibraryFiles();
        }

        static void UpdateEmpty()
        {
            emptyHint.Visibility = items.Count == 0 && suggestions.Count == 0 && previews.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            suggestPanel.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            libEmpty.Text = history.Count == 0 ? "Nada na biblioteca ainda. Tudo que você baixar aparece aqui." : "Nada encontrado.";
            libEmpty.Visibility = libView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        static void FilterLibrary()
        {
            string q = searchBox.Text.Trim();
            if (q.Length == 0) libView.Filter = null;
            else libView.Filter = o =>
            {
                var en = (Entry)o;
                return new[] { en.Title, en.Channel, en.Site }.Any(s => s != null && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            };
            UpdateEmpty();
        }

        static void AddHistory(Item it)
        {
            var replaced = history.Where(h => h.Url == it.Url && h.Kind == it.Kind && h.TrimStart == it.TrimStart && h.TrimEnd == it.TrimEnd).ToList();
            var en = new Entry
            {
                Url = it.Url, Title = it.Title, Site = it.Site, Thumb = it.Thumb ?? it.ThumbUrl, Channel = it.Channel, ChannelUrl = it.ChannelUrl,
                Kind = it.Kind, Quality = it.Quality, Fmt = it.Fmt, AudioFmt = it.AudioFmt, Height = it.Height, Date = DateTime.Now,
                TrimStart = it.TrimStart, TrimEnd = it.TrimEnd, TrimExact = it.TrimExact
            };
            en.Files.AddRange(it.Files);
            history.Insert(0, en);
            foreach (var old in replaced) RemoveHistory(old);
            SaveHistory();
            CacheThumb(en);
            RefreshSuggestions(it.ChannelUrl);
        }

        static void RemoveHistory(Entry en)
        {
            history.Remove(en);
            if (en.Thumb != null && en.Thumb.StartsWith(ThumbsDir, StringComparison.OrdinalIgnoreCase) && !history.Any(h => h.Thumb == en.Thumb))
                try { File.Delete(en.Thumb); } catch { }
        }

        static void CacheThumb(Entry en)
        {
            string src = en.Thumb;
            if (src == null || src.StartsWith(ThumbsDir, StringComparison.OrdinalIgnoreCase)) return;
            if (src.StartsWith("http")) { FetchThumb(src, f => KeepThumb(en, f)); return; }
            KeepThumb(en, src);
        }

        static void KeepThumb(Entry en, string file)
        {
            try
            {
                Directory.CreateDirectory(ThumbsDir);
                string dst = Path.Combine(ThumbsDir, Path.GetFileName(file));
                if (!File.Exists(dst)) File.Copy(file, dst);
                en.Thumb = dst;
                SaveHistory();
            }
            catch { }
        }

        static void FetchThumb(string url, Action<string> done)
        {
            if (url == null || !IsLink(url)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string dir = Path.Combine(TempDir, "thumbs");
                    Directory.CreateDirectory(dir);
                    string name;
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                        name = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "");
                    string file = Path.Combine(dir, name + ".jpg");
                    if (!File.Exists(file))
                    {
                        DownloadThumb(url, file + ".tmp");
                        File.Move(file + ".tmp", file);
                    }
                    Ui(() => done(file));
                }
                catch { }
            });
        }

        static void DownloadThumb(string url, string file)
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Timeout = ThumbTimeoutMs;
            req.ReadWriteTimeout = ThumbTimeoutMs;
            bool tooBig = false;
            try
            {
                using (var res = req.GetResponse())
                using (var input = res.GetResponseStream())
                using (var output = File.Create(file))
                {
                    var buffer = new byte[65536];
                    long total = 0;
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > ThumbMaxBytes) { tooBig = true; break; }
                        output.Write(buffer, 0, n);
                    }
                }
            }
            catch { try { File.Delete(file); } catch { } throw; }
            if (tooBig)
            {
                File.Delete(file);
                throw new InvalidDataException("miniatura maior que o limite");
            }
        }

        static void CheckLibraryFiles()
        {
            var list = history.ToList();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var en in list)
                {
                    var e = en;
                    bool ok = e.Files.Count > 0 && e.Files.All(File.Exists);
                    if (ok != e.Exists) Ui(() => e.Exists = ok);
                }
            });
        }

        static void OnLibraryButton(Entry en, string tag)
        {
            string file = en.Files.FirstOrDefault(File.Exists);
            if (tag == "h:link") OpenLink(en.Url);
            else if (tag == "h:open")
            {
                if (file == null) { en.Exists = false; return; }
                try { Process.Start(file); } catch (Exception ex) { MessageBox.Show(win, ex.Message, "Downloader"); }
            }
            else if (tag == "h:show") OpenFolder(file);
            else if (tag == "h:remove") { RemoveHistory(en); SaveHistory(); }
            else if (tag == "h:again") DownloadAgain(en);
        }

        static void DownloadAgain(Entry en)
        {
            if (!IsLink(en.Url)) { Errors.Write("WARN", "biblioteca com link invalido ignorado"); return; }
            var it = new Item { Url = en.Url, Title = en.Title, Site = en.Site, Thumb = en.Thumb, Status = "Na fila", State = "fila", Force = true,
                                TrimStart = en.TrimStart, TrimEnd = en.TrimEnd, TrimExact = en.TrimExact };
            it.CopyOptions(defaults);
            if (Options.Has(Options.Kinds, en.Kind)) it.Kind = en.Kind;
            if (Options.Has(Options.Qualities, en.Quality)) it.Quality = en.Quality;
            if (Options.Has(it.Kind == "audio" ? Options.AudioFmts : Options.VideoFmts, en.Fmt)) it.Fmt = en.Fmt;
            if (Options.Has(Options.AudioFmts, en.AudioFmt)) it.AudioFmt = en.AudioFmt;
            items.Add(it);
            queue.Enqueue(it);
            tabDownloads.IsChecked = true;
            StartNext();
        }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
        static string TrimText(double? v) { return v == null ? "" : v.Value.ToString(CultureInfo.InvariantCulture); }
        static double? TrimValue(string s)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }

        static void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                WriteAtomic(HistoryFile, history.Take(2000).Select(e => string.Join("\t", new[] {
                    e.Date.ToString("o"), e.Url, e.Title, e.Site, e.Thumb, e.Channel, e.ChannelUrl,
                    e.Kind, e.Quality, e.Fmt, e.AudioFmt, e.Height, string.Join("|", e.Files),
                    TrimText(e.TrimStart), TrimText(e.TrimEnd), e.TrimExact ? "1" : "0" }.Select(Clean))).ToArray());
            }
            catch { }
        }

        static void WriteAtomic(string file, string[] lines)
        {
            string tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }

        static void LoadHistory()
        {
            string[] lines;
            try
            {
                if (!File.Exists(HistoryFile)) return;
                lines = File.ReadAllLines(HistoryFile, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Errors.Write("WARN", "biblioteca nao pode ser lida: " + ex.Message);
                return;
            }
            int bad = 0;
            foreach (var line in lines)
            {
                try
                {
                    var c = line.Split('\t');
                    if (c.Length < 13) { bad++; continue; }
                    var en = new Entry
                    {
                        Date = DateTime.Parse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        Url = c[1], Title = c[2], Site = c[3], Thumb = NullIfEmpty(c[4]), Channel = c[5], ChannelUrl = c[6],
                        Kind = c[7], Quality = c[8], Fmt = c[9], AudioFmt = c[10], Height = NullIfEmpty(c[11])
                    };
                    en.Files.AddRange(c[12].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                    if (c.Length >= 16)
                    {
                        en.TrimStart = TrimValue(c[13]); en.TrimEnd = TrimValue(c[14]); en.TrimExact = c[15] != "0";
                    }
                    history.Add(en);
                    if (en.Thumb != null && en.Thumb.StartsWith("http")) CacheThumb(en);
                }
                catch { bad++; }
            }
            if (bad > 0)
            {
                Errors.Write("WARN", "biblioteca com " + bad + " linha(s) ignorada(s)");
                try { File.Copy(HistoryFile, HistoryFile + ".corrompido", true); } catch { }
            }
        }

        static void RefreshSuggestions(string firstChannel)
        {
            var top = history.Where(h => IsLink(h.ChannelUrl)).GroupBy(h => h.ChannelUrl)
                             .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(h => h.Date))
                             .Select(g => g.First()).ToList();
            var chosen = top.Where(h => h.ChannelUrl == firstChannel).Concat(top).Distinct().Take(3).ToList();
            if (chosen.Count == 0) return;
            string key = string.Join(" ", chosen.Select(h => h.ChannelUrl));
            if (suggestBusy || key == suggestKey) return;
            suggestBusy = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var lists = new List<List<Suggestion>>();
                foreach (var ch in chosen)
                {
                    var found = new List<Suggestion>();
                    RunTool("--encoding utf-8 --flat-playlist --playlist-end 8 --ignore-errors --no-warnings --no-colors " +
                        "--print \"%(url)s|%(thumbnails.-1.url|)s|%(duration|)s|%(title)s\" " + Args.Quote(ch.ChannelUrl), SuggestTimeoutMs, line =>
                    {
                        var c = line.Split(new[] { '|' }, 4);
                        if (c.Length == 4 && IsLink(c[0]))
                            lock (found) found.Add(new Suggestion { Url = c[0], ThumbUrl = c[1], Title = c[3], Info = (ch.Channel ?? ch.Site) + Duration(c[2]) });
                    });
                    lists.Add(found);
                }
                Ui(() =>
                {
                    suggestBusy = false;
                    suggestKey = key;
                    var have = new HashSet<string>(history.Select(h => VideoKey(h.Url)).Concat(items.Select(i => VideoKey(i.Url))));
                    suggestions.Clear();
                    for (int i = 0; suggestions.Count < 8 && lists.Any(l => i < l.Count); i++)
                        foreach (var l in lists)
                            if (i < l.Count && suggestions.Count < 8 && have.Add(VideoKey(l[i].Url)))
                            {
                                var sg = l[i];
                                suggestions.Add(sg);
                                FetchThumb(sg.ThumbUrl, f => sg.Thumb = f);
                            }
                    suggestTitle.Text = chosen.Count == 1 ? "Mais de " + (chosen[0].Channel ?? chosen[0].Site) : "Sugestões dos canais que você mais baixa";
                });
            });
        }

        static void OnSuggestionButton(Suggestion sg, string tag)
        {
            if (tag == "s:open") OpenLink(sg.Url);
            else if (tag == "s:get" && !sg.Queued)
            {
                sg.Queued = true;
                var it = new Item { Url = sg.Url, Title = sg.Title, Site = SiteName(sg.Url), Thumb = sg.Thumb, ThumbUrl = sg.ThumbUrl, Status = "Na fila", State = "fila" };
                it.CopyOptions(defaults);
                items.Add(it);
                queue.Enqueue(it);
                StartNext();
            }
        }

        static string Duration(string s)
        {
            double d;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d <= 0) return "";
            var t = TimeSpan.FromSeconds(d);
            return "  ·  " + (t.TotalHours >= 1 ? (int)t.TotalHours + t.ToString(@"\:mm\:ss") : t.ToString(@"m\:ss"));
        }

        static string VideoKey(string url)
        {
            var m = Regex.Match(url ?? "", @"(?:[?&]v=|youtu\.be/|/live/|/shorts/)([\w-]{11})");
            return m.Success ? "yt:" + m.Groups[1].Value : (url ?? "").Trim().TrimEnd('/').ToLowerInvariant();
        }

        static void OnTaggedButton(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            if (b == null) return;
            string tag = b.Tag as string;
            if (tag == null) return;
            var en = b.DataContext as Entry;
            if (en != null) { OnLibraryButton(en, tag); return; }
            var sg = b.DataContext as Suggestion;
            if (sg != null) { OnSuggestionButton(sg, tag); return; }
            var pv = b.DataContext as Preview;
            if (pv != null) { OnPreviewButton(pv, tag); return; }
            var it = b.DataContext as Item;
            if (it == null) return;

            if (tag.StartsWith("chip:")) ShowMenu(b, it, tag.Substring(5));
            else if (tag == "link") OpenLink(it.Url);
            else if (tag == "show") OpenFolder(it.Files.FirstOrDefault());
            else if (tag == "open")
            {
                string file = it.Files.FirstOrDefault(File.Exists);
                if (file != null) try { Process.Start(file); } catch { OpenFolder(file); }
            }
            else if (tag == "retry") Retry(it);
            else if (tag == "pause") Pause(it);
            else if (tag == "resume") Resume(it);
            else if (tag == "err:action") RunErrorAction(it);
            else if (tag == "err:details" && it.Err != null) ShowError(it.Err);
            else if (tag == "err:help") ShowPage("help", "Conteúdo restrito e login");
            else if (tag == "remove")
            {
                if (it.State == "baixando")
                {
                    it.Cancelled = true;
                    it.Status = "Cancelando…";
                    if (it.Proc != null) KillTree(it.Proc);
                }
                else
                {
                    if (it.State == "fila") RemoveFromQueue(it);
                    CleanWork(it);
                    items.Remove(it);
                }
            }
        }

        static void ShowMenu(Button b, Item it, string field)
        {
            string[][] opts; string cur;
            switch (field)
            {
                case "kind": opts = Options.Kinds; cur = it.Kind; break;
                case "quality": opts = Options.Qualities; cur = it.Quality; break;
                case "fmt": opts = it.Kind == "audio" ? Options.AudioFmts : Options.VideoFmts; cur = it.Fmt; break;
                case "login": opts = Options.Logins; cur = it.Login; break;
                case "afmt": opts = Options.AudioFmts; cur = it.AudioFmt; break;
                case "trim": opts = Options.Trims; cur = it.TrimMode; break;
                default: return;
            }

            CloseMenu();
            var menu = new ContextMenu
            {
                Style = (Style)win.FindResource("Menu"),
                PlacementTarget = b,
                Placement = PlacementMode.Bottom,
                VerticalOffset = -6,
                HorizontalOffset = -10,
                StaysOpen = false
            };
            menu.Closed += (s, e) => { if (openMenu == menu) openMenu = null; };
            openMenu = menu;
            var entry = (Style)win.FindResource("MenuEntry");
            foreach (var o in opts)
            {
                string key = o[0];
                var mi = new MenuItem { Header = o[2], IsChecked = key == cur, Style = entry };
                if (field == "login" && !Errors.BrowserInstalled(key))
                {
                    mi.Header = Options.Label(Options.Logins, key) + " · não encontrado neste PC";
                    mi.IsEnabled = false;
                }
                mi.Click += (s, e) =>
                {
                    if (field == "kind") it.Kind = key;
                    else if (field == "quality") it.Quality = key;
                    else if (field == "fmt") it.Fmt = key;
                    else if (field == "afmt") it.AudioFmt = key;
                    else if (field == "trim")
                    {
                        it.TrimMode = key;
                        if (key == "part") trimStartBox.Focus();
                    }
                    else
                    {
                        if (key == "file" && !PickCookieFile()) return;
                        it.Login = key;
                    }
                    if (it == defaults) SaveConfig();
                };
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        static void CloseMenu()
        {
            if (openMenu != null) { openMenu.IsOpen = false; openMenu = null; }
        }

        static void RemoveFromQueue(Item it)
        {
            var rest = queue.Where(q => q != it).ToList();
            queue.Clear();
            foreach (var q in rest) queue.Enqueue(q);
            UpdateTaskbar();
        }

        static void OpenFolder(string file)
        {
            try
            {
                if (file != null && File.Exists(file)) Process.Start(Explorer, "/select," + Args.Quote(file));
                else Process.Start(Explorer, Args.Quote(Dest));
            }
            catch (Exception ex) { ShowError(Errors.Register(Errors.Unexpected(ex, "abrir-pasta", false), null, null)); }
        }

        static void OnClosing(object sender, CancelEventArgs e)
        {
            if (current == null && queue.Count == 0) return;
            var r = MessageBox.Show(win, "Ainda há downloads em andamento. Se fechar agora, eles serão cancelados.\n\nFechar mesmo assim?", "Downloader", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            queue.Clear();
            if (current != null) { current.Cancelled = true; if (current.Proc != null) KillTree(current.Proc); }
        }

        static string Explorer { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); } }

        static Process Run(string exe, string args, string workDir = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (workDir != null) psi.WorkingDirectory = workDir;
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            var p = Process.Start(psi);
            JobGuard.Add(p);
            return p;
        }

        static void KillTree(Process p)
        {
            try
            {
                var k = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(k);
            }
            catch { }
        }

        static void Ui(Action a) { win.Dispatcher.BeginInvoke(a); }

        static void UpdateTaskbar()
        {
            var tb = win.TaskbarItemInfo;
            if (current == null)
            {
                var paused = items.FirstOrDefault(i => i.State == "pausado");
                tb.ProgressState = paused != null ? TaskbarItemProgressState.Paused : TaskbarItemProgressState.None;
                if (paused != null) tb.ProgressValue = paused.Progress / 100.0;
                return;
            }
            tb.ProgressState = current.Busy ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
            tb.ProgressValue = current.Progress / 100.0;
        }

        static string SiteName(string url)
        {
            var m = Regex.Match(url, @"^https?://(?:www\.|m\.)?([^/:]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return "Link";
            string h = m.Groups[1].Value.ToLowerInvariant();
            if (h.Contains("youtu")) return "YouTube";
            if (h.Contains("instagram")) return "Instagram";
            if (h.Contains("tiktok")) return "TikTok";
            if (h == "x.com" || h.Contains("twitter")) return "X";
            if (h.Contains("facebook") || h == "fb.watch") return "Facebook";
            return h;
        }

        static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigFile)) return;
                var cfg = new Dictionary<string, string>();
                foreach (var l in File.ReadAllLines(ConfigFile).Where(x => x.Contains("=")))
                    cfg[l.Substring(0, l.IndexOf('='))] = l.Substring(l.IndexOf('=') + 1);
                string v;
                if (cfg.TryGetValue("kind", out v) && Options.Has(Options.Kinds, v)) defaults.Kind = v;
                if (cfg.TryGetValue("quality", out v) && Options.Has(Options.Qualities, v)) defaults.Quality = v;
                if (cfg.TryGetValue("fmt", out v) && Options.Has(defaults.Kind == "audio" ? Options.AudioFmts : Options.VideoFmts, v)) defaults.Fmt = v;
                if (cfg.TryGetValue("trim", out v) && v == "part") defaults.TrimMode = v;
                if (cfg.TryGetValue("cookies", out v) && v.Length > 0) cookieFile = v;
                if (cfg.TryGetValue("seen", out v) && v.Length > 0) seenVersion = v;
                else seenVersion = "antiga";
                if (cfg.TryGetValue("login", out v) && Options.Has(Options.Logins, v) && (v != "file" || cookieFile != null)) defaults.Login = v;
                if (cfg.TryGetValue("afmt", out v) && Options.Has(Options.AudioFmts, v)) defaults.AudioFmt = v;
                if (cfg.TryGetValue("dest", out v) && v.Length > 0) customDest = v;
                if (cfg.TryGetValue("theme", out v) && (v == "light" || v == "dark" || v == "system")) themeMode = v;
                if (cfg.TryGetValue("width", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out savedWidth);
                if (cfg.TryGetValue("height", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out savedHeight);
                savedMaximized = cfg.TryGetValue("maximized", out v) && v == "1";
            }
            catch { }
        }

        static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
                WriteAtomic(ConfigFile, new[] {
                    "kind=" + defaults.Kind, "quality=" + defaults.Quality, "fmt=" + defaults.Fmt, "login=" + defaults.Login,
                    "afmt=" + defaults.AudioFmt,
                    "dest=" + (customDest ?? ""),
                    "cookies=" + (cookieFile ?? ""),
                    "seen=" + (seenVersion ?? ""),
                    "theme=" + themeMode,
                    "width=" + savedWidth.ToString(CultureInfo.InvariantCulture),
                    "height=" + savedHeight.ToString(CultureInfo.InvariantCulture),
                    "maximized=" + (savedMaximized ? "1" : "0") });
            }
            catch { }
        }

        static bool IsDark()
        {
            if (themeMode != "system") return themeMode == "dark";
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        static void ApplyTheme()
        {
            int column = IsDark() ? 2 : 1;
            foreach (var c in ThemeColors)
            {
                var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c[column]));
                brush.Freeze();
                Application.Current.Resources[c[0]] = brush;
            }
            if (win != null) StyleTitleBar();
        }

        static void RestoreWindowSize()
        {
            var area = SystemParameters.WorkArea;
            win.Width = Math.Min(savedWidth >= win.MinWidth ? savedWidth : win.Width, area.Width);
            win.Height = Math.Min(savedHeight >= win.MinHeight ? savedHeight : win.Height, area.Height);
            if (savedMaximized) win.WindowState = WindowState.Maximized;
        }

        static void UpdateLayoutMode()
        {
            bool compact = win.ActualWidth < 920;
            double gutter = win.ActualWidth >= 1180 ? 44 : 28;
            ((FrameworkElement)win.FindName("MainContent")).Margin = new Thickness(gutter, 0, gutter, 14);
            ((FrameworkElement)win.FindName("PageContent")).Margin = new Thickness(gutter, 0, gutter, 14);
            if (layoutApplied && compact == compactLayout) return;
            layoutApplied = true;
            compactLayout = compact;
            ((ColumnDefinition)win.FindName("SidebarColumn")).Width = new GridLength(compact ? 68 : 232);
            ((StackPanel)win.FindName("ThemePanel")).Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
            var change = (Button)win.FindName("ChangeDestBtn");
            DockPanel.SetDock(change, compact ? Dock.Bottom : Dock.Right);
            change.Margin = compact ? new Thickness(0, 0, 0, 7) : new Thickness(4, 0, 10, 0);
            change.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            Application.Current.Resources["NavLabel"] = compact ? Visibility.Collapsed : Visibility.Visible;
            Application.Current.Resources["NavIcon"] = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        static void UpdateWindowState()
        {
            bool max = win.WindowState == WindowState.Maximized;
            var frame = SystemParameters.WindowResizeBorderThickness;
            ((FrameworkElement)win.FindName("RootGrid")).Margin = max
                ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
            ((FrameworkElement)win.FindName("MaxIcon")).Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)win.FindName("RestoreIcon")).Visibility = max ? Visibility.Visible : Visibility.Collapsed;
            ((Button)win.FindName("MaxBtn")).ToolTip = max ? "Restaurar" : "Maximizar";
        }

        static void UpdateDestText()
        {
            string name = Path.GetFileName(Dest.TrimEnd('\\'));
            ((TextBlock)win.FindName("DestText")).Text = string.IsNullOrEmpty(name) ? Dest : name;
            string parent = Path.GetDirectoryName(Dest.TrimEnd('\\'));
            var path = (TextBlock)win.FindName("DestPath");
            path.Text = parent ?? "";
            path.Visibility = string.IsNullOrEmpty(parent) ? Visibility.Collapsed : Visibility.Visible;
            ((Button)win.FindName("FolderBtn")).ToolTip = "Abrir " + Dest;
        }

        static void PickDest()
        {
            string path = PickFolder(Dest);
            if (path == null) return;
            customDest = path;
            SaveConfig();
            UpdateDestText();
        }

        static string PickFolder(string startDir)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogCom();
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | 0x20 | 0x40);
                dlg.SetTitle("Escolha onde salvar os downloads");
                string dir = startDir;
                while (dir != null && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
                IShellItem start;
                if (dir != null && SHCreateItemFromParsingName(dir, IntPtr.Zero, typeof(IShellItem).GUID, out start) == 0)
                    dlg.SetFolder(start);
                if (dlg.Show(new WindowInteropHelper(win).Handle) != 0) return null;
                IShellItem res;
                dlg.GetResult(out res);
                string path;
                res.GetDisplayName(0x80058000, out path);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception ex) { MessageBox.Show(win, ex.Message, "Downloader"); return null; }
        }

        static void ShowFirstRun()
        {
            firstRunPath = DefaultDest;
            ((TextBlock)win.FindName("FirstRunPath")).Text = firstRunPath;
            ((FrameworkElement)win.FindName("FirstRun")).Visibility = Visibility.Visible;
        }

        static void FinishFirstRun()
        {
            try { Directory.CreateDirectory(firstRunPath); }
            catch (Exception ex) { MessageBox.Show(win, "Não consegui usar essa pasta: " + ex.Message, "Downloader"); return; }
            customDest = firstRunPath;
            SaveConfig();
            UpdateDestText();
            ((FrameworkElement)win.FindName("FirstRun")).Visibility = Visibility.Collapsed;
            linksBox.Focus();
            StartNext();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern bool SetDllDirectory(string path);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [DllImport("dwmapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static void StyleTitleBar()
        {
            try
            {
                var h = new WindowInteropHelper(win).Handle;
                if (h == IntPtr.Zero) return;
                int dark = IsDark() ? 1 : 0;
                DwmSetWindowAttribute(h, 20, ref dark, 4);
            }
            catch { }
        }
    }

    static class JobGuard
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimit
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimit
        {
            public BasicLimit Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        const uint KillOnClose = 0x2000;
        const int ExtendedInfoClass = 9;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimit info, int size);
        [DllImport("kernel32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        static readonly IntPtr Job = Create();

        static IntPtr Create()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return job;
            var info = new ExtendedLimit();
            info.Basic.LimitFlags = KillOnClose;
            SetInformationJobObject(job, ExtendedInfoClass, ref info, Marshal.SizeOf(typeof(ExtendedLimit)));
            return job;
        }

        public static void Add(Process p)
        {
            if (Job == IntPtr.Zero) return;
            try { AssignProcessToJobObject(Job, p.Handle); }
            catch (Exception ex) { Errors.Write("WARN", "job: " + ex.Message); }
        }
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialogCom { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    }
}
