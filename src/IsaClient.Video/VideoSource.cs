using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Music;
using Newtonsoft.Json.Linq;

namespace IsaClient.Video;

/// <summary>One stream yt-dlp found for a video (a picture, a sound, or both in one file).</summary>
public sealed class VideoFormat
{
	public string Id;

	public string Url;

	public Dictionary<string, string> Headers = new Dictionary<string, string>();

	public int Height;

	public double Fps;

	public double Bitrate;

	public long Size;

	public bool HasPicture;

	public bool HasSound;

	/// <summary>A "DRC" sound (dynamic range compressed): used only when the plain one is refused.</summary>
	public bool Drc;

	/// <summary>Language of a sound track ("es-US"), when the video has several (YouTube's dubbed tracks).</summary>
	public string Language;

	/// <summary>"Spanish (US)".</summary>
	public string LanguageName;

	/// <summary>The video's own sound, not a dub.</summary>
	public bool Original;

	public string Label => (Height > 0) ? (Height + "p" + ((Fps > 31.0) ? Math.Round(Fps).ToString(CultureInfo.InvariantCulture) : "")) : "Audio";
}

/// <summary>A video to play: what it is, where it came from, and the streams it can be played from.</summary>
public sealed class VideoInfo
{
	public string Id;

	public string Title;

	public string Channel;

	public double Duration;

	/// <summary>The page it came from (YouTube link...), or the file.</summary>
	public string Source;

	public string Thumbnail;

	/// <summary>A file on this computer (downloaded videos).</summary>
	public string LocalPath;

	public bool IsLive;

	/// <summary>Picture streams by quality, best first: one per height.</summary>
	public List<VideoFormat> Pictures = new List<VideoFormat>();

	public VideoFormat Sound;

	/// <summary>Sound tracks, one per language, the original first (just one for most videos).</summary>
	public List<VideoFormat> Sounds = new List<VideoFormat>();

	public DateTime ResolvedUtc = DateTime.UtcNow;

	/// <summary>
	/// From a site other than YouTube (TikTok, Instagram, X...): it plays from a copy yt-dlp downloads first
	/// (VideoSource.Fetch), kept in LocalPath meanwhile.
	/// </summary>
	public bool Web;

	public bool IsLocal => LocalPath != null;

	public bool IsYouTube => Source != null && MusicPlayer.YouTubeIdPattern.IsMatch(Source);

	/// <summary>The links yt-dlp gives stop working after some hours.</summary>
	public bool Expired => !IsLocal && (DateTime.UtcNow - ResolvedUtc).TotalHours > 5.0;

	public List<int> Heights => Pictures.Select((VideoFormat p) => p.Height).Distinct().OrderByDescending((int h) => h).ToList();

	/// <summary>The best picture not taller than <paramref name="maxHeight"/> (else the smallest one).</summary>
	public VideoFormat PictureFor(int maxHeight)
	{
		return Pictures.Where((VideoFormat p) => p.Height <= maxHeight).OrderByDescending((VideoFormat p) => p.Height).FirstOrDefault() ?? Pictures.OrderBy((VideoFormat p) => p.Height).FirstOrDefault();
	}
}

/// <summary>
/// Finds what to play with yt-dlp: a search ("ytsearch1:"), a link of any site yt-dlp knows, or a file. Only streams
/// Windows can decode are kept: H.264 pictures and AAC sound in MP4 files (on YouTube, up to 1080p).
/// </summary>
internal static class VideoSource
{
	internal static readonly string[] FileExtensions = new string[9] { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".3gp", ".ts" };

	/// <summary>An address without http(s):// in front ("tiktok.com/@someone/video/123", "youtu.be/abc").</summary>
	private static readonly Regex _bareAddress = new Regex("^(?:[a-z0-9-]+\\.)+[a-z]{2,}/\\S*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>Pages that are a video (what a chat link opens in the player): YouTube, TikTok, Instagram reels, Twitch, ...</summary>
	private static readonly Regex _videoPage = new Regex(@"^(?:https?://)?(?:[a-z0-9-]+\.)*(?:"
		+ @"youtube\.com/(?:watch\?|shorts/|live/|embed/)|youtu\.be/"
		+ @"|tiktok\.com/(?:@[^/]+/video/|t/)|vm\.tiktok\.com/|vt\.tiktok\.com/"
		+ @"|instagram\.com/(?:[^/]+/)?(?:reels?|p|tv)/"
		+ @"|twitch\.tv/(?:videos/|[^/]+/clip/)|clips\.twitch\.tv/"
		+ @"|vimeo\.com/\d|dailymotion\.com/video/|dai\.ly/"
		+ @"|facebook\.com/(?:watch|reel/|[^/]+/videos/)|fb\.watch/"
		+ @"|streamable\.com/\w|kick\.com/[^/]+/(?:videos|clips)/|bilibili\.com/video/|rumble\.com/v"
		+ @")|^(?:https?://)?[^?#\s]+\.(?:mp4|webm|mov|m4v)(?:[?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static bool IsVideoPage(string url)
	{
		return !string.IsNullOrEmpty(url) && _videoPage.IsMatch(url.Trim());
	}

	public static bool IsLink(string text)
	{
		text = (text ?? "").Trim();
		return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || text.StartsWith("youtu", StringComparison.OrdinalIgnoreCase) || _bareAddress.IsMatch(text);
	}

	public static VideoInfo FromFile(string path)
	{
		return new VideoInfo
		{
			Id = "file:" + path.ToLowerInvariant(),
			Title = Path.GetFileNameWithoutExtension(path),
			Channel = "On this computer",
			Source = path,
			LocalPath = path
		};
	}

	/// <summary>Runs yt-dlp (slow: call it off the main thread). Throws with a message for the chat.</summary>
	public static VideoInfo Resolve(string query)
	{
		string target = query.Trim();
		if (!IsLink(target))
		{
			target = "ytsearch1:" + target;
		}
		else if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			target = "https://" + target;
		}
		int code = YtDlp.RunYtDlp(new List<string> { "--no-playlist", "-J", target }, 90000, out var output, out var error);
		string json = output.Trim();
		if (code != 0 || json.Length == 0)
		{
			throw new InvalidOperationException(ErrorLine(error) ?? "yt-dlp could not open that.");
		}
		JObject root = JObject.Parse(json);
		if (root["entries"] is JArray entries)
		{
			// A search: the first hit.
			root = entries.OfType<JObject>().FirstOrDefault() ?? throw new InvalidOperationException("Nothing found for \"" + query + "\".");
		}
		return ((string)root["extractor_key"] == "Youtube") ? Parse(root) : ParseWeb(root);
	}

	/// <summary>
	/// Any other site yt-dlp knows: its stream links often only work inside yt-dlp (TikTok answers 403 to anyone else)
	/// or come in pieces (HLS), so it is played from a copy yt-dlp downloads (Fetch).
	/// </summary>
	private static VideoInfo ParseWeb(JObject root)
	{
		string site = (string)root["extractor_key"] ?? "Web";
		return new VideoInfo
		{
			// The site in the id: an Instagram code is as long as a YouTube id.
			Id = site + "-" + ((string)root["id"] ?? Guid.NewGuid().ToString("N")),
			Title = (string)root["title"] ?? (string)root["description"] ?? "Video",
			Channel = (string)root["channel"] ?? (string)root["uploader"] ?? site,
			Duration = (double?)root["duration"] ?? 0.0,
			Source = (string)root["webpage_url"] ?? (string)root["original_url"],
			Thumbnail = (string)root["thumbnail"],
			IsLive = (bool?)root["is_live"] ?? false,
			Web = true
		};
	}

	/// <summary>
	/// Downloads a web video (VideoInfo.Web) with yt-dlp into the cache folder and sets its LocalPath: one file with
	/// picture and sound (no merging needed), H.264 when the site has it, not taller than <paramref name="maxHeight"/>.
	/// <paramref name="progress"/> gets the bytes so far. Slow: call it off the main thread. Throws with a message for
	/// the chat (OperationCanceledException when <paramref name="stop"/> said so).
	/// </summary>
	public static void Fetch(VideoInfo info, int maxHeight, Action<long> progress, Func<bool> stop)
	{
		if (info.IsLive)
		{
			throw new InvalidOperationException("Live streams can't be played here yet.");
		}
		if (info.LocalPath != null && File.Exists(info.LocalPath))
		{
			return;
		}
		Directory.CreateDirectory(VideoPlayer.CacheFolder);
		string name = "web-" + SafeName(info.Id);
		string file = Path.Combine(VideoPlayer.CacheFolder, name + ".mp4");
		string ts = Path.ChangeExtension(file, ".ts");
		foreach (string done in new[] { file, ts })
		{
			if (File.Exists(done) && new FileInfo(done).Length > 0)
			{
				info.LocalPath = done;
				return;
			}
		}
		// Copies of other web videos go: only the one that plays is kept (all of them go when the game starts).
		string playing = VideoPlayer.Current?.LocalPath;
		foreach (string old in Directory.GetFiles(VideoPlayer.CacheFolder, "web-*"))
		{
			if (string.Equals(old, playing, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			try
			{
				File.Delete(old);
			}
			catch
			{
			}
		}
		string h = maxHeight.ToString(CultureInfo.InvariantCulture);
		string formats = "b[height<=" + h + "][vcodec^=avc1]/b[height<=" + h + "][vcodec^=h264]/b[height<=" + h + "][ext=mp4]/b[height<=" + h + "]/b[vcodec^=avc1]/b[vcodec^=h264]/b";
		bool finished = false;
		Task watch = Task.Run(delegate
		{
			while (!finished)
			{
				long bytes = 0L;
				try
				{
					bytes = Directory.GetFiles(VideoPlayer.CacheFolder, name + "*").Sum((string f) => new FileInfo(f).Length);
				}
				catch
				{
				}
				progress?.Invoke(bytes);
				Thread.Sleep(250);
			}
		});
		int code;
		string error;
		try
		{
			code = YtDlp.RunYtDlp(new List<string> { "--no-playlist", "--no-mtime", "-f", formats, "-o", file.Replace("%", "%%"), info.Source }, 3600000, out _, out error, addCommon: true, stop: stop);
		}
		finally
		{
			finished = true;
			watch.Wait(1000);
		}
		if (stop != null && stop())
		{
			throw new OperationCanceledException();
		}
		if (code != 0 || !File.Exists(file))
		{
			throw new InvalidOperationException(ErrorLine(error) ?? "yt-dlp could not download that video.");
		}
		// Without ffmpeg, a video sent in pieces (HLS) is saved as MPEG-TS: Windows plays it by its real extension.
		byte[] head = new byte[1];
		using (FileStream stream = File.OpenRead(file))
		{
			stream.Read(head, 0, 1);
		}
		if (head[0] == 0x47)
		{
			File.Move(file, ts);
			file = ts;
		}
		info.LocalPath = file;
	}

	private static string SafeName(string text)
	{
		char[] chars = (text ?? "x").Select((char c) => char.IsLetterOrDigit(c) ? c : '_').ToArray();
		return new string(chars, 0, Math.Min(chars.Length, 60));
	}

	private static VideoInfo Parse(JObject root)
	{
		VideoInfo info = new VideoInfo
		{
			Id = (string)root["id"] ?? Guid.NewGuid().ToString("N"),
			Title = (string)root["title"] ?? "Video",
			Channel = (string)root["channel"] ?? (string)root["uploader"] ?? (string)root["extractor_key"] ?? "",
			Duration = (double?)root["duration"] ?? 0.0,
			Source = (string)root["webpage_url"] ?? (string)root["original_url"],
			Thumbnail = (string)root["thumbnail"],
			IsLive = (bool?)root["is_live"] ?? false
		};
		if ((string)root["extractor_key"] == "Youtube" && info.Id.Length == 11)
		{
			info.Thumbnail = "https://i.ytimg.com/vi/" + info.Id + "/mqdefault.jpg";
		}
		List<VideoFormat> formats = new List<VideoFormat>();
		foreach (JObject f in (root["formats"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
		{
			string protocol = (string)f["protocol"] ?? "";
			string url = (string)f["url"];
			string id = (string)f["format_id"] ?? "";
			if ((protocol != "https" && protocol != "http") || string.IsNullOrEmpty(url))
			{
				continue;
			}
			string vcodec = (string)f["vcodec"] ?? "none";
			string acodec = (string)f["acodec"] ?? "none";
			string ext = (string)f["ext"] ?? "";
			bool picture = vcodec != "none";
			bool sound = acodec != "none";
			// What Windows decodes without extra codecs: H.264 and AAC, in MP4.
			if ((picture && !(vcodec.StartsWith("avc1") || vcodec.StartsWith("h264"))) || (sound && !acodec.StartsWith("mp4a")) || (ext != "mp4" && ext != "m4a") || (!picture && !sound))
			{
				continue;
			}
			VideoFormat format = new VideoFormat
			{
				Id = id,
				Url = url,
				Height = (int?)f["height"] ?? 0,
				Fps = (double?)f["fps"] ?? 0.0,
				Bitrate = (double?)f["tbr"] ?? 0.0,
				Size = (long?)f["filesize"] ?? (long?)f["filesize_approx"] ?? 0L,
				HasPicture = picture,
				HasSound = sound,
				Drc = id.Contains("drc")
			};
			if (sound && !picture)
			{
				format.Language = (string)f["language"];
				format.Original = ((int?)f["language_preference"] ?? 0) >= 10 || ((string)f["format_note"] ?? "").Contains("original");
				format.LanguageName = TrackName((string)f["format_note"], format.Language);
			}
			if (f["http_headers"] is JObject headers)
			{
				foreach (JProperty header in headers.Properties())
				{
					format.Headers[header.Name] = (string)header.Value;
				}
			}
			formats.Add(format);
		}
		formats = Reachable(formats);
		info.Sounds = (from f in formats
			where f.HasSound && !f.HasPicture
			group f by f.Language ?? "" into g
			select g.OrderByDescending((VideoFormat f) => f.Bitrate).First() into f
			orderby f.Original descending, f.Bitrate descending
			select f).ToList();
		info.Sound = ChooseSound(info.Sounds, VideoPlayer.AudioLanguage);
		// Separate pictures go with the separate sound; files with both are used when there is no separate sound.
		IEnumerable<VideoFormat> pictures = formats.Where((VideoFormat f) => f.HasPicture && (info.Sound != null || f.HasSound));
		info.Pictures = (from f in pictures
			group f by f.Height into g
			select g.OrderBy((VideoFormat f) => f.HasSound ? 1 : 0).ThenByDescending((VideoFormat f) => f.Fps).ThenByDescending((VideoFormat f) => f.Bitrate).First() into f
			orderby f.Height descending
			select f).ToList();
		if (info.Pictures.Count == 0)
		{
			throw new InvalidOperationException(info.IsLive ? "Live streams can't be played here yet." : "This video has no stream Windows can play (it needs an H.264 MP4 version).");
		}
		return info;
	}

	/// <summary>
	/// The streams YouTube really hands out: some videos' links answer 403 (Forbidden) for some formats only, and
	/// playing one of those stalls ("the stream stopped, reconnecting") until Windows gives up on it. Each candidate is
	/// asked for its first KB, all at once and as the player will ask; only a clear refusal (401, 403, 404, 410) drops
	/// it. Anything else (a slow answer, no network) keeps it, and if nothing were left the list stays as it was.
	/// </summary>
	private static List<VideoFormat> Reachable(List<VideoFormat> formats)
	{
		if (formats.Count == 0)
		{
			return formats;
		}
		bool[] refused = new bool[formats.Count];
		Task[] probes = new Task[formats.Count];
		for (int i = 0; i < formats.Count; i++)
		{
			int index = i;
			probes[i] = Task.Run(delegate
			{
				refused[index] = Refuses(formats[index]);
			});
		}
		try
		{
			Task.WaitAll(probes, 10000);
		}
		catch
		{
		}
		List<VideoFormat> kept = formats.Where((VideoFormat f, int i) => !refused[i]).ToList();
		// Playable: a file with both, or a picture and a separate sound.
		bool playable = kept.Any((VideoFormat f) => f.HasPicture && f.HasSound) || (kept.Any((VideoFormat f) => f.HasPicture && !f.HasSound) && kept.Any((VideoFormat f) => f.HasSound && !f.HasPicture));
		List<VideoFormat> result = playable ? kept : formats;
		// DRC sounds only stand in for a plain one that was refused (same language).
		HashSet<string> plain = new HashSet<string>(from f in result
			where f.HasSound && !f.HasPicture && !f.Drc
			select f.Language ?? "");
		return result.Where((VideoFormat f) => !f.Drc || !plain.Contains(f.Language ?? "")).ToList();
	}

	/// <summary>True only when the server clearly refuses this stream's link.</summary>
	private static bool Refuses(VideoFormat format)
	{
		try
		{
			System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
			System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(format.Url);
			request.Timeout = 8000;
			request.ReadWriteTimeout = 8000;
			request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 16);
			foreach (KeyValuePair<string, string> header in format.Headers)
			{
				RangeCache.SetHeader(request, header.Key, header.Value);
			}
			request.AddRange(0, 1023);
			using (System.Net.WebResponse response = request.GetResponse())
			{
				return false;
			}
		}
		catch (System.Net.WebException ex) when (ex.Response is System.Net.HttpWebResponse http)
		{
			int status = (int)http.StatusCode;
			http.Dispose();
			return status == 401 || status == 403 || status == 404 || status == 410;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// The sound track to play: the game's language (or the one picked in the player) when the video has it, else
	/// the original. "original" always takes the original.
	/// </summary>
	public static VideoFormat ChooseSound(List<VideoFormat> sounds, string preference)
	{
		if (sounds.Count == 0)
		{
			return null;
		}
		VideoFormat original = sounds.FirstOrDefault((VideoFormat s) => s.Original) ?? sounds[0];
		if (sounds.Count == 1 || preference == "original")
		{
			return original;
		}
		string wanted = string.IsNullOrEmpty(preference) ? GameLanguage() : preference;
		string primary = Primary(wanted);
		return sounds.Where((VideoFormat s) => Primary(s.Language) == primary).OrderByDescending((VideoFormat s) => s.Original).ThenByDescending((VideoFormat s) => string.Equals(s.Language, wanted, StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? original;
	}

	/// <summary>The game's language ("es-ES").</summary>
	public static string GameLanguage()
	{
		try
		{
			return Terraria.Localization.Language.ActiveCulture.Name;
		}
		catch
		{
			return "en-US";
		}
	}

	private static string Primary(string language)
	{
		language = (language ?? "").ToLowerInvariant();
		int dash = language.IndexOfAny(new char[2] { '-', '_' });
		return (dash > 0) ? language.Substring(0, dash) : language;
	}

	/// <summary>"Spanish (US) original (default), medium" → "Spanish (US)".</summary>
	private static string TrackName(string note, string language)
	{
		string name = (note ?? "").Split(',')[0].Replace("(default)", "").Replace("original", "").Trim();
		return (name.Length > 0) ? name : (language ?? "Default");
	}

	internal static string ErrorLine(string error)
	{
		string line = (error ?? "").Split('\n').Select((string l) => l.Trim()).LastOrDefault((string l) => l.StartsWith("ERROR", StringComparison.Ordinal));
		if (line == null)
		{
			return null;
		}
		line = line.Substring(5).TrimStart(':', ' ');
		int bracket = line.IndexOf("] ", StringComparison.Ordinal);
		if (line.StartsWith("[", StringComparison.Ordinal) && bracket > 0)
		{
			line = line.Substring(bracket + 2);
		}
		return (line.Length > 160) ? (line.Substring(0, 157) + "...") : line;
	}

	/// <summary>YouTube search for the browse page: id, title, channel, length (no streams yet).</summary>
	public static List<VideoInfo> Search(string query, int count)
	{
		List<VideoInfo> results = new List<VideoInfo>();
		YtDlp.RunYtDlp(new List<string> { "--flat-playlist", "--print", "ISAVID|%(id)s|%(duration)s|%(channel)s|%(title)s", "ytsearch" + count + ":" + query }, 90000, out var output, out _);
		foreach (string line in output.Split('\n'))
		{
			string trimmed = line.Trim();
			if (!trimmed.StartsWith("ISAVID|"))
			{
				continue;
			}
			string[] parts = trimmed.Split(new char[1] { '|' }, 5);
			if (parts.Length < 5 || parts[1].Length != 11)
			{
				continue;
			}
			double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
			results.Add(new VideoInfo
			{
				Id = parts[1],
				Title = parts[4],
				Channel = (parts[3] == "NA") ? "" : parts[3],
				Duration = duration,
				Source = "https://www.youtube.com/watch?v=" + parts[1],
				Thumbnail = "https://i.ytimg.com/vi/" + parts[1] + "/mqdefault.jpg"
			});
		}
		return results;
	}
}
