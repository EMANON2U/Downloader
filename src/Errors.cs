using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Downloader
{
    public class ErrorInfo
    {
        public string Code, Component, Title, Cause, Fix;
        public string Action, ActionLabel;
        public string Incident, Tech, Url;
        public DateTime When;
        public bool Fatal;
    }

    public static class Errors
    {
        public static string ToolVersion = "?";

        static ErrorInfo Make(string code, string comp, string title, string cause, string fix, string action = null, string label = null)
        {
            return new ErrorInfo { Code = code, Component = comp, Title = title, Cause = cause, Fix = fix, Action = action, ActionLabel = label };
        }

        public static string BrowserName(string key)
        {
            switch (key)
            {
                case "firefox": return "Firefox";
                case "chrome": return "Chrome";
                case "edge": return "Edge";
                case "brave": return "Brave";
                case "opera": return "Opera";
                case "vivaldi": return "Vivaldi";
                case "file": return "arquivo de cookies";
                default: return "navegador";
            }
        }

        public static bool BrowserInstalled(string key)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            switch (key)
            {
                case "firefox": return Directory.Exists(Path.Combine(roam, @"Mozilla\Firefox\Profiles"));
                case "chrome": return Directory.Exists(Path.Combine(local, @"Google\Chrome\User Data"));
                case "edge": return Directory.Exists(Path.Combine(local, @"Microsoft\Edge\User Data"));
                case "brave": return Directory.Exists(Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"));
                case "opera": return Directory.Exists(Path.Combine(roam, @"Opera Software\Opera Stable"));
                case "vivaldi": return Directory.Exists(Path.Combine(local, @"Vivaldi\User Data"));
                default: return true;
            }
        }

        public static bool BrowserRunning(string key)
        {
            switch (key)
            {
                case "firefox": case "chrome": case "brave": case "opera": case "vivaldi": return Browsers.IsRunning(key);
                case "edge": return Browsers.IsRunning("msedge");
                default: return false;
            }
        }

        static void AddLoginAlternative(ErrorInfo e, string login)
        {
            if (login != "firefox" && BrowserInstalled("firefox")) { e.Action = "login:firefox"; e.ActionLabel = "Tentar com o Firefox"; }
            else if (login != "file") { e.Action = "login:file"; e.ActionLabel = "Usar arquivo de cookies"; }
        }

        static bool Has(string text, params string[] parts) { return parts.Any(p => text.Contains(p)); }

        public static bool NoVideo(string raw)
        {
            string e = (raw ?? "").ToLowerInvariant();
            return Has(e, "no video", "there is no video", "no media", "unsupported url");
        }

        public static bool Unsupported(string raw) { return (raw ?? "").ToLowerInvariant().Contains("unsupported url"); }

        public static bool NeedsLogin(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string e = raw.ToLowerInvariant();
            return Has(e, "sign in", "log in", "login", "logged in", "use --cookies", "--cookies-from-browser", "authentication",
                       "private video", "video is private", "this account is private", "members-only", "members only",
                       "age-restricted", "confirm your age", "age restricted", "inappropriate", "nsfw", "not a bot", "empty media response");
        }

        public static ErrorInfo Classify(string raw, string login, bool usedCookies, bool hasTrim)
        {
            string e = (raw ?? "").ToLowerInvariant();
            string browser = BrowserName(login);

            if (e.Contains("could not copy") && e.Contains("cookie"))
            {
                var x = Make("AUTH-101", "login-navegador", "O " + browser + " está bloqueando o acesso ao login",
                    "Este conteúdo exige login, e o Windows não deixa ler os dados do " + browser + " enquanto ele está aberto.",
                    "Feche todas as janelas do " + browser + " e tente de novo. Se continuar, use o Firefox ou um arquivo de cookies.");
                AddLoginAlternative(x, login);
                return x;
            }
            if (Has(e, "dpapi", "app-bound", "app bound", "v20 cookies"))
            {
                var x = Make("AUTH-102", "login-navegador", "O " + browser + " não permite ler o login salvo",
                    "Este conteúdo exige login, e o " + browser + " protege esses dados para que outros programas não os leiam. É uma proteção do próprio navegador: fechar e abrir não resolve.",
                    "Faça login no site pelo Firefox e escolha Firefox em “Conteúdo restrito”, ou use um arquivo de cookies. A Ajuda explica como.");
                AddLoginAlternative(x, login);
                return x;
            }
            if (e.Contains("could not find") && e.Contains("cookie"))
            {
                var x = Make("AUTH-103", "login-navegador", "Não encontrei os dados do " + browser,
                    "O " + browser + " parece não estar instalado nesta conta do Windows, ou ainda não foi aberto.",
                    "Abra o " + browser + ", faça login no site e tente de novo, ou escolha outra opção em “Conteúdo restrito”.");
                AddLoginAlternative(x, login);
                return x;
            }
            if (Has(e, "netscape", "cookies file", "invalid cookie", "cookie file"))
                return Make("AUTH-104", "login-arquivo", "O arquivo de cookies não pôde ser usado",
                    "O arquivo não existe mais ou não está no formato de cookies esperado (Netscape).",
                    "Exporte o arquivo de novo, logado no site, e escolha-o em “Conteúdo restrito”.", "login:file", "Escolher outro arquivo");

            if (Has(e, "http error 429", "too many requests", "rate-limit", "rate limit"))
                return Make("SITE-304", "site", "O site limitou os downloads por enquanto",
                    "Muitos pedidos em pouco tempo a partir da sua conexão.",
                    "Espere alguns minutos e tente de novo." + (login == "none" ? " Usar login em “Conteúdo restrito” pode ajudar." : ""));

            if (NeedsLogin(raw))
            {
                if (usedCookies)
                    return Make("AUTH-111", "login", "O site não aceitou o login",
                        "O conteúdo continua restrito mesmo usando o login do " + browser + ". A conta pode não estar logada nesse site ou não ter acesso a este conteúdo.",
                        "Abra o site no " + browser + ", confira se está logado e se consegue ver o conteúdo, e tente de novo.");
                var x = Make("AUTH-110", "login", "Este conteúdo exige login",
                    "O conteúdo é privado, tem restrição de idade, ou o site pediu para entrar em uma conta.",
                    "Faça login no site pelo Firefox e escolha o navegador em “Conteúdo restrito”. Se o conteúdo for público, tente de novo mais tarde.");
                AddLoginAlternative(x, login);
                return x;
            }

            if (e.Contains("requested format is not available") || e.Contains("no formats"))
                return Make("FMT-401", "formato", "Essa qualidade não está disponível para este vídeo",
                    "O site não oferece o vídeo na qualidade ou no formato escolhidos.", "Escolha “Máxima” em Qualidade e tente de novo.");
            if (Has(e, "not available in your country", "available in your country", "geo restrict", "geo-restrict", "not available from your location", "blocked it in your country"))
                return Make("SITE-303", "site", "Conteúdo bloqueado na sua região",
                    "O dono do conteúdo ou o site não libera este vídeo para o seu país.", "Não há como baixar a partir desta conexão.");
            if (e.Contains("ip address is blocked"))
                return Make("SITE-308", "site", "O site bloqueou esta conexão",
                    "O site recusou pedidos vindos do seu endereço de internet.", "Tente mais tarde ou em outra rede.");
            if (e.Contains("unsupported url"))
                return Make("LINK-301", "link", "Este site não é compatível",
                    "O Downloader não reconhece esse endereço como uma página de vídeo.",
                    "Confira se o link abre direto no vídeo. Links de perfil ou de página inicial não funcionam.", "edit", "Corrigir o link");
            if (e.Contains("is not a valid url"))
                return Make("LINK-302", "link", "O link não é válido", "O endereço está incompleto ou foi copiado errado.",
                    "Copie o link de novo.", "edit", "Corrigir o link");
            if (Has(e, "no video could be found", "no media found", "no video formats", "no video in this", "there's no video"))
                return Make("SITE-305", "site", "Não encontrei vídeo neste link",
                    "A publicação pode ter só texto ou foto, ou o vídeo foi removido.", "Abra o link no navegador e confira se o vídeo aparece.", "edit", "Corrigir o link");
            if (Has(e, "live event will begin", "premieres in", "will begin in", "has not started"))
                return Make("SITE-306", "site", "A transmissão ainda não começou",
                    "O vídeo está agendado e ainda não pode ser baixado.", "Tente de novo depois que ela começar.");
            if (Has(e, "video unavailable", "video is unavailable", "has been removed", "not available", "does not exist", "http error 404", "http error 410", "page not found"))
                return Make("SITE-302", "site", "Vídeo indisponível",
                    "O vídeo foi removido, ficou privado ou o link está errado.", "Abra o link no navegador e confira se o vídeo ainda existe.", "edit", "Corrigir o link");
            if (Has(e, "no space left", "errno 28", "winerror 112", "not enough space"))
                return Make("DISK-601", "disco", "Falta espaço no disco", "O disco ficou sem espaço durante o download.",
                    "Libere espaço ou troque a pasta de destino em “Alterar” e tente de novo.");
            if (Has(e, "permission denied", "access is denied", "winerror 5", "errno 13", "acesso negado"))
                return Make("DISK-602", "disco", "Sem permissão para salvar o arquivo",
                    "A pasta está protegida, o arquivo está aberto em outro programa ou o antivírus bloqueou.",
                    "Feche o arquivo se estiver aberto ou troque a pasta de destino em “Alterar”.");
            if (Has(e, "file name too long", "errno 36", "winerror 206"))
                return Make("DISK-603", "disco", "O nome do arquivo ficou longo demais", "A pasta de destino tem um caminho muito comprido.",
                    "Escolha uma pasta de destino com caminho mais curto em “Alterar”.");

            if (hasTrim && e.Contains("ffmpeg"))
                return Make("TRIM-503", "trecho", "Não consegui cortar este vídeo",
                    "Este vídeo ou formato não permitiu baixar só o trecho escolhido.",
                    "Confira os tempos. Se continuar, desmarque “Corte exato” ou baixe o vídeo completo.", "edit", "Ajustar o trecho");
            if (Has(e, "http error 403", "forbidden"))
                return Make("SITE-307", "site", "O site recusou o download",
                    "O site impediu o acesso a este arquivo. Isso costuma ser temporário.", "Tente de novo em alguns minutos.");
            if (Has(e, "getaddrinfo", "failed to resolve", "timed out", "timeout", "connection", "unable to download", "winerror 10060", "winerror 10054", "ssl", "network", "unreachable"))
                return Make("NET-201", "rede", "Falha de conexão",
                    "A internet caiu ou o site demorou demais para responder.", "Confira sua internet e tente de novo. O download continua de onde parou, quando possível.");
            if (e.Contains("ffmpeg") || e.Contains("postprocessing") || e.Contains("conversion failed"))
                return Make("TOOL-702", "conversao", "Não consegui finalizar o arquivo",
                    "O download terminou, mas a etapa de juntar ou converter falhou.", "Tente de novo, ou troque o Formato.");
            if (string.IsNullOrEmpty(raw))
                return Make("TOOL-703", "motor", "O download parou sem explicação",
                    "O componente de download fechou sem informar o motivo. Um antivírus pode ter interrompido.", "Tente de novo. Se repetir, copie os detalhes e envie para o suporte.");
            return Make("APP-900", "motor", "Não foi possível baixar", "O motivo não foi identificado. Os detalhes técnicos estão no relatório.",
                "Tente de novo. Se repetir, copie os detalhes e envie para o suporte.");
        }

        public static ErrorInfo DestUnavailable(string dest)
        {
            return Make("DISK-604", "disco", "Não consegui acessar a pasta de destino",
                Sanitize(dest) + " não está disponível. O disco ou a pasta de rede pode ter sido desconectado.", "Reconecte o disco ou troque a pasta em “Alterar”.");
        }
        public static ErrorInfo MusicLookup(string service, Exception ex)
        {
            var m = ex as MusicError;
            if (m != null && m.Unsupported)
                return Make("LINK-303", "musica", "Este link do " + service + " não é compatível", m.Message,
                    "Copie o link de uma música, álbum ou playlist pelo botão Compartilhar.", "edit", "Corrigir o link");
            return Make("MUSIC-801", "musica", "Não consegui ler a música no " + service,
                m != null ? m.Message : "O " + service + " não respondeu.", "Confira se o link abre no navegador e tente de novo.");
        }
        public static ErrorInfo MusicNotFound()
        {
            return Make("MUSIC-802", "musica", "Não encontrei esta música no YouTube Music",
                "O Spotify, o Deezer e a Apple Music não liberam o áudio, então o programa procura a mesma música no YouTube Music, e ela não apareceu.",
                "Procure a música no YouTube Music e cole o link de lá.");
        }
        public static ErrorInfo FileMissing()
        {
            return Make("DISK-605", "disco", "O arquivo não apareceu na pasta",
                "O download terminou, mas o arquivo não está no destino. Um antivírus pode ter removido.", "Confira a quarentena do antivírus e tente de novo.");
        }
        public static ErrorInfo Components(string detail)
        {
            var e = Make("TOOL-701", "componentes", "Não consegui preparar os componentes de download",
                "Falta espaço em disco, ou um antivírus bloqueou os arquivos.", "Libere espaço, confira a quarentena do antivírus e abra o Downloader de novo.");
            e.Tech = detail; e.Fatal = true;
            return e;
        }
        public static ErrorInfo TrimBeyond(string videoLength)
        {
            return Make("TRIM-501", "trecho", "O trecho passa do fim do vídeo",
                "Este vídeo tem só " + videoLength + ".", "Ajuste os tempos para dentro dessa duração.", "edit", "Ajustar o trecho");
        }
        public static ErrorInfo TrimLive()
        {
            return Make("TRIM-502", "trecho", "Não dá para baixar um trecho de transmissão ao vivo",
                "Enquanto está ao vivo, o vídeo ainda não tem uma duração fechada.", "Baixe o vídeo completo ou tente o trecho quando a transmissão terminar.", "edit", "Ajustar");
        }
        public static ErrorInfo Unexpected(Exception ex, string component, bool fatal)
        {
            var e = Make("APP-901", component, "Ocorreu um erro inesperado no Downloader",
                "É um defeito do programa, não algo que você fez.", "Copie os detalhes e envie para o suporte. Depois, continue usando ou reinicie o programa.");
            e.Tech = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            e.Fatal = fatal;
            return e;
        }

        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = Regex.Replace(text, @"https?://[^\s'""<>]+", m => SanitizeUrl(m.Value));
            s = Regex.Replace(s, @"(?i)[a-z]:\\Users\\[^\\\s""']+", "<usuario>");
            s = Regex.Replace(s, @"(?i)\b(set-cookie|cookies?)\s*:[^\r\n]*", "$1: <removido>");
            s = Regex.Replace(s, @"(?i)\b(authorization|bearer|ct0|senha|sid|[\w-]*(?:token|secret|password|passwd|api[_-]?key|session(?:id)?|cookie)[\w-]*)\b\s*[=:]\s*[^\s;,&]+", "$1=<removido>");
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = Regex.Replace(s, @"(?i)\b" + Regex.Escape(user) + @"\b", "<usuario>");
            s = Regex.Replace(s, @"\b\d{1,3}(\.\d{1,3}){3}\b", "<ip>");
            s = Regex.Replace(s, @"[A-Za-z0-9_\-]{48,}", "<removido>");
            return s;
        }

        public static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            url = Regex.Replace(url, @"^(https?://)[^/@\s]+@", "$1", RegexOptions.IgnoreCase);
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash) + "#…";
            int q = url.IndexOf('?');
            if (q < 0) return url;
            var v = Regex.Match(url, @"[?&]v=([\w-]{6,20})");
            return url.Substring(0, q) + (v.Success ? "?v=" + v.Groups[1].Value : "?…");
        }

        public static string LogDir { get { return Path.Combine(Program.DataDir, "logs"); } }

        public static ErrorInfo Register(ErrorInfo e, string url, string tech)
        {
            e.When = DateTime.Now;
            e.Incident = "D" + e.When.ToString("yyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
            e.Url = SanitizeUrl(url);
            e.Tech = Sanitize(string.IsNullOrEmpty(tech) ? e.Tech : tech);
            Write("ERROR", "id=" + e.Incident + " code=" + e.Code + " comp=" + e.Component + " url=" + e.Url + "\n" + e.Tech);
            return e;
        }

        static readonly object logLock = new object();

        public static void Write(string level, string message)
        {
            try
            {
                lock (logLock)
                {
                Directory.CreateDirectory(LogDir);
                string file = Path.Combine(LogDir, "downloader-" + DateTime.Now.ToString("yyyy-MM") + ".log");
                File.AppendAllText(file, DateTime.Now.ToString("s") + " [" + level + "] v" + Version.Number + " " + Sanitize(message) + "\r\n", Encoding.UTF8);
                foreach (var old in new DirectoryInfo(LogDir).GetFiles("downloader-*.log").OrderByDescending(f => f.Name).Skip(3)) old.Delete();
                }
            }
            catch { }
        }

        public static string BuildReport(ErrorInfo e)
        {
            var b = new StringBuilder();
            b.AppendLine("RELATÓRIO DE ERRO - Downloader");
            b.AppendLine("Código: " + e.Code);
            b.AppendLine("Identificador: " + e.Incident);
            b.AppendLine("Quando: " + e.When.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            b.AppendLine("Componente: " + e.Component);
            b.AppendLine("Problema: " + e.Title);
            b.AppendLine("Possível causa: " + e.Cause);
            b.AppendLine("Link: " + (string.IsNullOrEmpty(e.Url) ? "-" : e.Url));
            b.AppendLine("Programa: " + Version.Number + "  |  yt-dlp: " + ToolVersion);
            b.AppendLine("Windows: " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " x64" : "") + "  |  .NET: " + Environment.Version);
            if (!string.IsNullOrEmpty(e.Tech))
            {
                b.AppendLine();
                b.AppendLine("Detalhes técnicos:");
                b.AppendLine(e.Tech);
            }
            return b.ToString();
        }
    }
}
