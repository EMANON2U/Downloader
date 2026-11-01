using System;
using System.Globalization;

namespace Downloader
{
    public class ProbeData
    {
        public bool Failed, NeedsLogin, Live, Playlist;
        public double Duration;
        public int Height;
        public double? File, Approx, Part0, Part1, Tbr, Abr, Asr;
        public bool HasVideo;
        public string Site, Thumb, ChannelUrl, Channel, Title;

        public const string Template =
            "PREVIEW|%(duration|)s|%(live_status|)s|%(height|)s|%(playlist_index|)s|%(filesize|)s|%(filesize_approx|)s" +
            "|%(requested_formats.0.filesize,requested_formats.0.filesize_approx|)s|%(requested_formats.1.filesize,requested_formats.1.filesize_approx|)s" +
            "|%(tbr|)s|%(abr|)s|%(asr|)s|%(vcodec|)s|%(extractor_key|)s|%(thumbnail|)s|%(channel_url,uploader_url|)s|%(channel,uploader|)s|%(title|)s";

        static double? Num(string s)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 ? d : (double?)null;
        }

        public static ProbeData Parse(string line)
        {
            var c = line.Split(new[] { '|' }, 18);
            if (c.Length != 18 || c[0] != "PREVIEW") return null;
            var p = new ProbeData
            {
                Duration = Num(c[1]) ?? 0,
                Live = c[2] == "is_live" || c[2] == "is_upcoming",
                Height = (int)(Num(c[3]) ?? 0),
                Playlist = c[4].Length > 0 && c[4] != "NA",
                File = Num(c[5]), Approx = Num(c[6]), Part0 = Num(c[7]), Part1 = Num(c[8]),
                Tbr = Num(c[9]), Abr = Num(c[10]), Asr = Num(c[11]),
                HasVideo = c[12].Length > 0 && c[12] != "none" && c[12] != "NA",
                Site = c[13], Thumb = c[14], ChannelUrl = c[15], Channel = c[16], Title = c[17]
            };
            if (p.Site == "Youtube") p.Site = "YouTube";
            return p;
        }
    }

    public class SizeResult
    {
        public double Bytes;
        public bool Approx;
        public bool Ceiling;
        public string Text { get { return (Ceiling ? "até " : "") + (Approx ? "≈ " : "") + Estimate.FormatBytes(Bytes); } }
    }

    public static class Estimate
    {
        public static string TrimRate(int height)
        {
            return height >= 2160 ? "45M" : height >= 1440 ? "24M" : height >= 1080 || height == 0 ? "12M" : height >= 720 ? "6M" : height >= 480 ? "2500k" : "1500k";
        }

        static double RateBits(string rate)
        {
            return double.Parse(rate.Substring(0, rate.Length - 1), CultureInfo.InvariantCulture) * (rate.EndsWith("M") ? 1000000 : 1000);
        }

        public static string FormatBytes(double bytes)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            if (bytes >= 1073741824) return (bytes / 1073741824).ToString(bytes >= 10737418240 ? "0" : "0.0", pt) + " GB";
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0", pt) + " MB";
            return Math.Max(1, bytes / 1024).ToString("0", pt) + " KB";
        }

        static double? Whole(ProbeData p)
        {
            if (p.File != null) return p.File;
            if (p.Approx != null) return p.Approx;
            if (p.Part0 != null) return p.Part0 + (p.Part1 ?? 0);
            if (p.Tbr != null && p.Duration > 0) return p.Tbr * 1000 / 8 * p.Duration;
            return null;
        }

        static double? Audio(ProbeData p, string fmt, double seconds, double? source)
        {
            if (seconds <= 0) return null;
            if (fmt == "mp3") return 320000.0 / 8 * seconds;
            if (fmt == "wav") return (p.Asr ?? 48000) * 2 * 2 * seconds;
            if (p.Abr != null) return p.Abr * 1000 / 8 * seconds;
            return source != null && p.Duration > 0 ? source * seconds / p.Duration : (double?)null;
        }

        public static SizeResult For(ProbeData p, string kind, string fmt, string audioFmt, double? start, double? end, bool exact)
        {
            if (p == null || p.Failed || p.Live || p.Playlist) return null;
            bool trim = start != null || end != null;
            double? seconds = trim ? (end ?? (p.Duration > 0 ? p.Duration : (double?)null)) - (start ?? 0) : p.Duration;
            if (seconds != null && seconds <= 0) return null;
            double? whole = Whole(p);
            double total;

            if (kind == "audio")
            {
                double? audio = !trim && !p.HasVideo && (fmt == "orig" || fmt == "m4a") ? whole : Audio(p, fmt, seconds ?? 0, p.HasVideo ? null : whole);
                if (audio == null) return null;
                return new SizeResult { Bytes = audio.Value, Approx = trim || p.File == null || (fmt != "orig" && fmt != "m4a") };
            }

            if (trim && exact)
            {
                if (seconds == null) return null;
                double video = (RateBits(TrimRate(p.Height)) + 192000) / 8 * seconds.Value;
                if (kind == "split")
                {
                    double? extra = Audio(p, audioFmt, seconds.Value, p.Part1);
                    video = RateBits(TrimRate(p.Height)) / 8 * seconds.Value + (extra ?? 0);
                }
                return new SizeResult { Bytes = video, Approx = true, Ceiling = true };
            }

            if (kind == "split")
            {
                double? video = p.Part0 ?? whole;
                if (video == null) return null;
                double part = trim ? (seconds != null && p.Duration > 0 ? seconds.Value / p.Duration : -1) : 1;
                if (part < 0) return null;
                double? extra = Audio(p, audioFmt, seconds ?? 0, p.Part1);
                total = video.Value * part + (extra ?? 0);
                return new SizeResult { Bytes = total, Approx = true };
            }

            if (whole == null) return null;
            total = whole.Value;
            if (trim)
            {
                if (seconds == null || p.Duration <= 0) return null;
                total = total * seconds.Value / p.Duration;
            }
            return new SizeResult { Bytes = total, Approx = trim || p.File == null };
        }
    }
}
