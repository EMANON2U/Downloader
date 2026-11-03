using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Downloader
{
    public class Track
    {
        public string Url, Title, Artist, Thumb;
        public double Seconds;
        public string Name { get { return string.IsNullOrEmpty(Artist) ? Title : Artist + " – " + Title; } }
    }

    public class MusicList
    {
        public string Service, Name, Thumb;
        public bool Collection;
        public readonly List<Track> Tracks = new List<Track>();
    }

    public class MusicError : Exception
    {
        public readonly bool Unsupported;
        public MusicError(string message, bool unsupported) : base(message) { Unsupported = unsupported; }
    }

    public static class Music
    {
        public const string Via = "YouTube Music";
        const int TimeoutMs = 20000, MaxBytes = 8 * 1024 * 1024, MaxPages = 20;
        const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
        const string SpotifyLink = @"open\.spotify\.com/(?:intl-[a-z-]+/)?(?:embed/)?(track|album|playlist)/([A-Za-z0-9]{22})";
        const string DeezerLink = @"deezer\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?(track|album|playlist)/(\d+)";
        const string AppleLink = @"music\.apple\.com/(?:([a-z]{2})/)?(album|song|playlist)/(?:[^/?#]+/)?([^/?#]+)";
        static readonly Dictionary<string, MusicList> cache = new Dictionary<string, MusicList>();

        static string Host(string url)
        {
            var m = Regex.Match(url ?? "", @"^https?://(?:www\.|m\.)?([^/:?#]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        }

        public static string Service(string url)
        {
            string h = Host(url);
            if (h == "music.youtube.com") return "YouTube Music";
            if (h == "soundcloud.com" || h.EndsWith(".soundcloud.com")) return "SoundCloud";
            if (h == "spotify.com" || h.EndsWith(".spotify.com") || h == "spotify.link") return "Spotify";
            if (h == "deezer.com" || h.EndsWith(".deezer.com") || h == "deezer.page.link") return "Deezer";
            if (h == "music.apple.com") return "Apple Music";
            return null;
        }

        public static bool IsMusic(string url) { return Service(url) != null; }

        public static bool NeedsSearch(string url)
        {
            string s = Service(url);
            return s == "Spotify" || s == "Deezer" || s == "Apple Music";
        }

        public static string SiteLabel(string service) { return service + " · via " + Via; }

        public static string SearchUrl(Track t)
        {
            string title = Regex.Replace(t.Title ?? "", @"\s*[\(\[][^\)\]]*remaster[^\)\]]*[\)\]]|\s+-\s+[^-]*remaster.*$", "", RegexOptions.IgnoreCase).Trim();
            return "https://music.youtube.com/search?q=" + Uri.EscapeDataString(((t.Artist ?? "") + " " + title).Trim()) + "#songs";
        }

        public static MusicList Resolve(string url)
        {
            lock (cache) { MusicList hit; if (cache.TryGetValue(url, out hit)) return hit; }
            string service = Service(url);
            MusicList list;
            try
            {
                if (service == "Spotify") list = Spotify(Expand(url, SpotifyLink));
                else if (service == "Deezer") list = Deezer(Expand(url, DeezerLink));
                else if (service == "Apple Music") list = Apple(url);
                else throw new MusicError("Este link não é de um serviço de música compatível.", true);
            }
            catch (WebException ex)
            {
                var res = ex.Response as HttpWebResponse;
                if (res != null && (res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.Gone))
                    throw new MusicError("O " + service + " não encontrou este link. Ele pode ter sido removido ou ser privado.", false);
                throw new MusicError("Não consegui falar com o " + service + ": " + ex.Message, false);
            }
            list.Service = service;
            if (list.Tracks.Count == 0) throw new MusicError("Não encontrei músicas neste link do " + service + ".", false);
            lock (cache) cache[url] = list;
            return list;
        }

        static string Expand(string url, string pattern)
        {
            var m = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
            if (m.Success) return url;
            Uri final;
            string body = Get(url, out final);
            if (Regex.IsMatch(final.AbsoluteUri, pattern, RegexOptions.IgnoreCase)) return final.AbsoluteUri;
            m = Regex.Match(body, pattern, RegexOptions.IgnoreCase);
            if (m.Success) return "https://" + (pattern.StartsWith("deezer") ? "www." : "") + m.Value;
            throw new MusicError("Use o link de uma música, álbum ou playlist.", true);
        }

        static MusicList Spotify(string url)
        {
            var m = Regex.Match(url, SpotifyLink, RegexOptions.IgnoreCase);
            string kind = m.Groups[1].Value.ToLowerInvariant(), id = m.Groups[2].Value;
            string html = Get("https://open.spotify.com/embed/" + kind + "/" + id);
            var j = Regex.Match(html, "<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
            if (!j.Success) throw new MusicError("O Spotify não mostrou os dados deste link.", false);
            var page = At(Parse(j.Groups[1].Value), "props", "pageProps");
            var e = At(page, "state", "data", "entity");
            if (e == null && Num(page, "status") == 404) throw new MusicError("O Spotify não encontrou este link. Ele pode ter sido removido ou ser privado.", false);
            if (e == null) throw new MusicError("O Spotify não mostrou os dados deste link.", false);

            var list = new MusicList { Name = Str(e, "name") ?? Str(e, "title"), Thumb = SpotifyImage(e), Collection = kind != "track" };
            if (kind == "track")
            {
                var names = new List<string>();
                foreach (var a in Arr(e, "artists")) { string n = Str(a, "name"); if (!string.IsNullOrEmpty(n)) names.Add(n); }
                list.Tracks.Add(new Track { Url = "https://open.spotify.com/track/" + id, Title = list.Name, Artist = string.Join(", ", names), Seconds = Num(e, "duration") / 1000, Thumb = list.Thumb });
                return list;
            }
            foreach (var t in Arr(e, "trackList"))
            {
                string uri = Str(t, "uri");
                if (uri == null || !uri.StartsWith("spotify:track:")) continue;
                list.Tracks.Add(new Track
                {
                    Url = "https://open.spotify.com/track/" + uri.Substring(14), Title = Str(t, "title"),
                    Artist = (Str(t, "subtitle") ?? "").Replace(' ', ' '), Seconds = Num(t, "duration") / 1000, Thumb = list.Thumb
                });
            }
            return list;
        }

        static string SpotifyImage(object e)
        {
            string best = null;
            double size = -1;
            foreach (var i in Arr(e, "visualIdentity", "image"))
            {
                double w = Num(i, "maxWidth");
                if (Str(i, "url") != null && w > size) { size = w; best = Str(i, "url"); }
            }
            return best;
        }

        static MusicList Deezer(string url)
        {
            var m = Regex.Match(url, DeezerLink, RegexOptions.IgnoreCase);
            string kind = m.Groups[1].Value.ToLowerInvariant(), id = m.Groups[2].Value;
            const string api = "https://api.deezer.com/";
            var root = DeezerGet(api + kind + "/" + id);
            var list = new MusicList { Collection = kind != "track" };
            if (kind == "track")
            {
                list.Thumb = Str(root, "album", "cover_xl");
                list.Tracks.Add(DeezerTrack(root, list.Thumb));
                list.Name = list.Tracks[0].Name;
                return list;
            }
            list.Name = Str(root, "title");
            list.Thumb = Str(root, kind == "album" ? "cover_xl" : "picture_xl");
            string next = api + kind + "/" + id + "/tracks?limit=100";
            for (int page = 0; next != null && page < MaxPages; page++)
            {
                var p = DeezerGet(next);
                foreach (var t in Arr(p, "data")) list.Tracks.Add(DeezerTrack(t, Str(t, "album", "cover_xl") ?? list.Thumb));
                next = Str(p, "next");
            }
            return list;
        }

        static object DeezerGet(string url)
        {
            var o = Parse(Get(url));
            if (At(o, "error") != null) throw new MusicError("O Deezer não liberou este link. Ele pode ter sido removido ou ser privado.", false);
            return o;
        }

        static Track DeezerTrack(object t, string thumb)
        {
            return new Track
            {
                Url = Str(t, "link") ?? "https://www.deezer.com/track/" + Str(t, "id"), Title = Str(t, "title"),
                Artist = Str(t, "artist", "name"), Seconds = Num(t, "duration"), Thumb = thumb
            };
        }

        static MusicList Apple(string url)
        {
            var m = Regex.Match(url, AppleLink, RegexOptions.IgnoreCase);
            if (!m.Success) throw new MusicError("Use o link de uma música ou de um álbum da Apple Music.", true);
            string kind = m.Groups[2].Value.ToLowerInvariant();
            if (kind == "playlist") throw new MusicError("Playlists da Apple Music não são compatíveis. Use o link de uma música ou de um álbum.", true);
            string country = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : "us";
            var song = Regex.Match(url, @"[?&]i=(\d+)");
            bool single = kind == "song" || song.Success;
            string id = song.Success ? song.Groups[1].Value : m.Groups[3].Value;
            if (!Regex.IsMatch(id, @"^\d+$")) throw new MusicError("Use o link de uma música ou de um álbum da Apple Music.", true);

            var root = Parse(Get("https://itunes.apple.com/lookup?country=" + country + "&id=" + id + (single ? "" : "&entity=song&limit=200")));
            var list = new MusicList { Collection = !single };
            foreach (var x in Arr(root, "results"))
            {
                string type = Str(x, "wrapperType");
                if (type == "collection") { list.Name = Str(x, "collectionName"); list.Thumb = AppleArt(x); }
                else if (type == "track" && Str(x, "kind") == "song")
                    list.Tracks.Add(new Track
                    {
                        Url = Regex.Replace(Str(x, "trackViewUrl") ?? url, @"[&?]uo=\d+", ""), Title = Str(x, "trackName"),
                        Artist = Str(x, "artistName"), Seconds = Num(x, "trackTimeMillis") / 1000, Thumb = AppleArt(x)
                    });
            }
            if (single && list.Tracks.Count > 0) { list.Name = list.Tracks[0].Name; list.Thumb = list.Tracks[0].Thumb; }
            return list;
        }

        static string AppleArt(object x)
        {
            string art = Str(x, "artworkUrl100");
            return art == null ? null : art.Replace("100x100bb", "600x600bb");
        }

        static string Get(string url) { Uri final; return Get(url, out final); }

        static string Get(string url, out Uri final)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Agent;
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.Headers[HttpRequestHeader.AcceptLanguage] = "pt-BR,pt;q=0.9,en;q=0.8";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var input = res.GetResponseStream())
            using (var mem = new MemoryStream())
            {
                final = res.ResponseUri;
                var buffer = new byte[65536];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (mem.Length + n > MaxBytes) throw new MusicError("A resposta do serviço de música passou do limite.", false);
                    mem.Write(buffer, 0, n);
                }
                return Encoding.UTF8.GetString(mem.ToArray());
            }
        }

        static object Parse(string json)
        {
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.DeserializeObject(json); }
            catch (ArgumentException) { throw new MusicError("O serviço de música respondeu em um formato inesperado.", false); }
            catch (InvalidOperationException) { throw new MusicError("O serviço de música respondeu em um formato inesperado.", false); }
        }

        static object At(object o, params object[] path)
        {
            foreach (var k in path)
            {
                var d = o as Dictionary<string, object>;
                var a = o as object[];
                if (k is string && d != null) { if (!d.TryGetValue((string)k, out o)) return null; }
                else if (k is int && a != null) { if ((int)k >= a.Length) return null; o = a[(int)k]; }
                else return null;
            }
            return o;
        }

        static string Str(object o, params object[] path)
        {
            var v = At(o, path);
            if (v == null || v is Dictionary<string, object> || v is object[]) return null;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s.Length == 0 ? null : s;
        }

        static double Num(object o, params object[] path)
        {
            double d;
            return double.TryParse(Str(o, path), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        static object[] Arr(object o, params object[] path) { return At(o, path) as object[] ?? new object[0]; }
    }
}
