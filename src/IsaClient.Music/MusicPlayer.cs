using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria.IO;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Music;

/// <summary>
/// Music module: .play / .firstplay / .skip / .autoplay... plays songs from YouTube (or any site yt-dlp
/// supports, or local files) while Terraria's own music fades out. yt-dlp is downloaded the first time the module is
/// turned on. Everything lives in Terraria/IsaClient/Music: IsaMusic.json, Songs/ (downloaded audio saved with its real
/// title, see IsaMusicLibrary), library.json / playlists.json and bin/ (yt-dlp.exe, optional deno.exe).
/// The music window and the floating mini player (IsaMusicUI) drive it through the "Player API" region.
/// </summary>
public static class MusicPlayer
{
	public sealed class Track
	{
		public string Id;

		public string Title;

		public double Duration;

		public string Source;

		public string FilePath;

		public bool Autoplay;

		public string RequestedBy;

		public string Display => (string.IsNullOrEmpty(Title) ? Source : Title) + ((Duration > 0.0) ? (" (" + FormatTime(TimeSpan.FromSeconds(Duration)) + ")") : "");
	}

	public enum LoopMode
	{
		Off,
		Song,
		Queue
	}

	public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

	public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

	internal static readonly string[] CommandNames = new string[40]
	{
		"play", "p", "firstplay", "playfirst", "pf", "playnext", "playnow", "skip", "next", "stop",
		"pause", "resume", "unpause", "queue", "q", "np", "nowplaying", "volume", "vol", "autoplay",
		"loop", "shuffle", "qremove", "qclear", "music", "loadcookies", "cookies", "ytdlp", "seek", "songs",
		"musicfolder", "musicui", "player", "mplayer", "miniplayer", "mini", "prev", "fav", "background", "bgplay"
	};

	private static readonly Color _musicColor = new Color(255, 170, 225);

	internal static readonly Regex YouTubeIdPattern = new Regex("(?:youtu\\.be/|v=|/shorts/|/live/|/embed/)([A-Za-z0-9_-]{11})", RegexOptions.Compiled);

	// Settings (IsaClient/Music/IsaMusic.json)
	public static bool Enabled;

	public static float Volume = 0.6f;

	public static bool AutoplayEnabled;

	/// <summary>Songs keep playing while the game is in the background or minimized (only the music module's songs).</summary>
	public static bool PlayInBackground = true;

	public static bool MuteGameMusic = true;

	public static bool AnnounceSongs = true;

	public static string CookiesFile = "";

	public static string CookiesFromBrowser = "";

	public static string SearchPrefix = "ytsearch1:";

	public static string AudioFormat = "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best";

	public static string ExtraYtDlpArguments = "";

	public static int MaxCacheMB = 1024;

	public static int MaxSongMinutes = 30;

	/// <summary>How many queued songs are downloaded ahead while the current one plays.</summary>
	public static int PrefetchCount = 3;

	public static LoopMode Loop;

	private static Preferences _config;

	internal static readonly object Sync = new object();

	internal static readonly List<Track> TrackQueue = new List<Track>();

	private static readonly List<string> _recentIds = new List<string>();

	internal static Track CurrentTrack;

	private static AudioPlayer _player;

	private static bool _workerRunning;

	internal static volatile bool Installing;

	private static bool _autoplayFetching;

	private static int _generation;

	private static bool _focusPaused;

	private static bool _advancePending;

	private static bool _listening;

	/// <summary>The song was paused because a video started: it comes back when the video closes.</summary>
	private static bool _pausedForVideo;

	public static string MusicFolder => Path.Combine(ClientSettings.IsaClientFolder, "Music");

	public static string SongsFolder => Path.Combine(MusicFolder, "Songs");

	public static string BinFolder => Path.Combine(MusicFolder, "bin");

	public static string YtDlpPath => Path.Combine(BinFolder, "yt-dlp.exe");

	public static string DenoPath => Path.Combine(BinFolder, "deno.exe");

	private static string ConfigPath => Path.Combine(MusicFolder, "IsaMusic.json");

	public static Track Current => CurrentTrack;

	public static bool IsPlaying
	{
		get
		{
			if (_player != null && _player.IsAlive)
			{
				return !_player.IsPaused;
			}
			return false;
		}
	}

	public static bool IsPaused => _player != null && _player.IsAlive && _player.IsPaused && !_focusPaused;

	/// <summary>
	/// Read by Main.UpdateAudio: fades the vanilla music out while a song plays. Paused by you, the game's music comes
	/// back (paused because the window lost focus, it stays out: the game's music is paused then too).
	/// </summary>
	public static bool SilencesGameMusic
	{
		get
		{
			if (MuteGameMusic && _player != null)
			{
				return _player.IsAlive && (!_player.IsPaused || _focusPaused);
			}
			return false;
		}
	}

	public static int QueueCount
	{
		get
		{
			lock (Sync)
			{
				return TrackQueue.Count;
			}
		}
	}

	#region Settings

	public static void Load()
	{
		if (!_listening)
		{
			_listening = true;
			MediaFocus.Claimed += OnMediaClaimed;
			MediaFocus.Released += OnMediaReleased;
		}
		try
		{
			Directory.CreateDirectory(MusicFolder);
			_config = new Preferences(ConfigPath);
			if (!_config.Load())
			{
				Save();
				return;
			}
			Enabled = _config.Get("Enabled", false);
			Volume = MathHelper.Clamp(_config.Get("Volume", 0.6f), 0f, 1f);
			AutoplayEnabled = _config.Get("Autoplay", false);
			PlayInBackground = _config.Get("PlayInBackground", true);
			MuteGameMusic = _config.Get("MuteTerrariaMusic", true);
			AnnounceSongs = _config.Get("AnnounceSongs", true);
			CookiesFile = _config.Get("CookiesFile", "") ?? "";
			CookiesFromBrowser = _config.Get("CookiesFromBrowser", "") ?? "";
			SearchPrefix = _config.Get("SearchPrefix", "ytsearch1:") ?? "ytsearch1:";
			AudioFormat = _config.Get("AudioFormat", AudioFormat) ?? AudioFormat;
			ExtraYtDlpArguments = _config.Get("ExtraYtDlpArguments", "") ?? "";
			MaxCacheMB = Utils.Clamp(_config.Get("MaxCacheMB", 1024), 50, 100000);
			MaxSongMinutes = Utils.Clamp(_config.Get("MaxSongMinutes", 30), 1, 600);
			PrefetchCount = Utils.Clamp(_config.Get("PrefetchSongs", 3), 1, 20);
			Enum.TryParse<LoopMode>(_config.Get("Loop", "Off"), ignoreCase: true, out Loop);
			Shuffle = _config.Get("Shuffle", false);
			MusicPlayerWindow.LoadSettings(_config);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Save()
	{
		try
		{
			Directory.CreateDirectory(MusicFolder);
			if (_config == null)
			{
				_config = new Preferences(ConfigPath);
			}
			_config.Clear();
			_config.Put("Enabled", Enabled);
			_config.Put("Volume", Volume);
			_config.Put("Autoplay", AutoplayEnabled);
			_config.Put("PlayInBackground", PlayInBackground);
			_config.Put("MuteTerrariaMusic", MuteGameMusic);
			_config.Put("AnnounceSongs", AnnounceSongs);
			_config.Put("CookiesFile", CookiesFile);
			_config.Put("CookiesFromBrowser", CookiesFromBrowser);
			_config.Put("SearchPrefix", SearchPrefix);
			_config.Put("AudioFormat", AudioFormat);
			_config.Put("ExtraYtDlpArguments", ExtraYtDlpArguments);
			_config.Put("MaxCacheMB", MaxCacheMB);
			_config.Put("MaxSongMinutes", MaxSongMinutes);
			_config.Put("PrefetchSongs", PrefetchCount);
			_config.Put("Loop", Loop.ToString());
			_config.Put("Shuffle", Shuffle);
			MusicPlayerWindow.SaveSettings(_config);
			_config.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region Helpers

	public static string FormatTime(TimeSpan time)
	{
		if (!(time.TotalHours >= 1.0))
		{
			return (int)time.TotalMinutes + ":" + time.Seconds.ToString("00");
		}
		return (int)time.TotalHours + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00");
	}

	/// <summary>Which music messages go to the chat.</summary>
	public enum ChatMessageMode
	{
		/// <summary>None.</summary>
		Off,
		/// <summary>Answers to the music commands you type, and errors. Buttons (window, mini player, panel) stay quiet.</summary>
		Commands,
		/// <summary>Everything, also what buttons do.</summary>
		All
	}

	/// <summary>How long after a typed music command its answers still show in <see cref="ChatMessageMode.Commands"/> mode
	/// (searches and downloads answer later, from another thread).</summary>
	private const int TypedCommandWindowMs = 20000;

	[Setting("MusicChatMessages")]
	public static ChatMessageMode ChatMessages = ChatMessageMode.Commands;

	private static volatile int _typedCommandTick;

	private static volatile bool _typedCommandSeen;

	/// <summary>A music command was typed in the chat: its answers show for a while.</summary>
	internal static void MarkTypedCommand()
	{
		_typedCommandTick = Environment.TickCount;
		_typedCommandSeen = true;
	}

	private static bool ShouldSay(bool error)
	{
		switch (ChatMessages)
		{
		case ChatMessageMode.Off:
			return false;
		case ChatMessageMode.All:
			return true;
		default:
			return error || (_typedCommandSeen && unchecked(Environment.TickCount - _typedCommandTick) < TypedCommandWindowMs);
		}
	}

	internal static void Say(string text, Color? color = null)
	{
		if (!ShouldSay(color.HasValue && color.Value == ClientChat.ErrorColor))
		{
			return;
		}
		Color c = color ?? _musicColor;
		Main.QueueMainThreadAction(delegate
		{
			Main.NewText(IsaClient.Languages.L10n.T("[Music] " + text), c.R, c.G, c.B);
		});
	}

	internal static void SayError(string text)
	{
		Say(text, ClientChat.ErrorColor);
	}

	public static string NowPlayingText
	{
		get
		{
			Track current = CurrentTrack;
			AudioPlayer player = _player;
			if (current == null || player == null)
			{
				if (Installing)
				{
					return "Installing yt-dlp...";
				}
				return (QueueCount > 0) ? "Loading next song..." : "Nothing playing";
			}
			string title = (string.IsNullOrEmpty(current.Title) ? current.Source : current.Title);
			if (title.Length > 38)
			{
				title = title.Substring(0, 36) + "...";
			}
			TimeSpan duration = ((player.Duration.Ticks > 0) ? player.Duration : TimeSpan.FromSeconds(current.Duration));
			return (player.IsPaused ? "Paused: " : "Playing: ") + title + "  " + FormatTime(player.Position) + " / " + FormatTime(duration);
		}
	}
	#endregion
	#region Commands

	#endregion

	#region Enable / install

	public static void SetEnabled(bool enabled)
	{
		Enabled = enabled;
		Save();
		if (!enabled)
		{
			Stop(clearQueue: true);
			Say("Music module off.");
			return;
		}
		Say("Music module on. Use .play <song or link>.");
		YtDlp.EnsureYtDlp(null);
	}

	#endregion

	#region Queue and playback

	internal enum QueuePosition
	{
		End,
		Next,
		Now
	}

	internal static void Request(string query, QueuePosition position)
	{
		if (!Enabled)
		{
			Enabled = true;
			Save();
			Say("Music module on.");
		}
		query = MusicLinks.NormalizeQuery(query);
		Track track = new Track
		{
			Source = query
		};
		string local = FindLocalFile(query);
		if (local != null)
		{
			track.FilePath = local;
			MusicLibrary.Song known = MusicLibrary.All().FirstOrDefault((MusicLibrary.Song s) => s.File != null && string.Equals(Path.GetFullPath(s.File), local, StringComparison.OrdinalIgnoreCase));
			track.Title = known?.Title ?? Path.GetFileNameWithoutExtension(local);
			track.Id = known?.Key ?? ("file:" + local);
			track.Duration = known?.Duration ?? 0.0;
		}
		lock (Sync)
		{
			switch (position)
			{
			case QueuePosition.End:
			{
				int firstAuto = TrackQueue.FindIndex((Track t) => t.Autoplay);
				TrackQueue.Insert((firstAuto < 0) ? TrackQueue.Count : firstAuto, track);
				break;
			}
			default:
				TrackQueue.Insert(0, track);
				break;
			}
		}
		bool idle = CurrentTrack == null;
		if (position == QueuePosition.Now && !idle)
		{
			Say("Playing now: " + query);
			Advance(skipCurrent: true);
		}
		else if (idle)
		{
			Say("Searching: " + query);
		}
		else
		{
			Say("Queued" + ((position == QueuePosition.Next) ? " first" : "") + ": " + query + "  (#" + ((position == QueuePosition.End) ? QueueCount : 1) + ")");
		}
		if (track.FilePath != null)
		{
			EnsureWorker();
		}
		else
		{
			YtDlp.EnsureYtDlp(EnsureWorker);
		}
	}

	/// <summary>A path to an audio file, or the name of a song already in Songs/ (".play my song").</summary>
	private static string FindLocalFile(string query)
	{
		try
		{
			if (query.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(query))
			{
				return Path.GetFullPath(query);
			}
			if (query.StartsWith("http", StringComparison.OrdinalIgnoreCase))
			{
				Match match = YouTubeIdPattern.Match(query);
				if (match.Success && MusicLibrary.TryGetFile(match.Groups[1].Value, out var cached))
				{
					return cached;
				}
				MusicLibrary.Song byLink = MusicLibrary.All().FirstOrDefault((MusicLibrary.Song s) => s.Downloaded && string.Equals(s.Url, query, StringComparison.OrdinalIgnoreCase));
				if (byLink != null)
				{
					return byLink.File;
				}
			}
			else
			{
				// ".play My Song": a saved song with that title plays from disk.
				MusicLibrary.Song saved = MusicLibrary.All().FirstOrDefault((MusicLibrary.Song s) => s.Downloaded && string.Equals(s.Title, query, StringComparison.OrdinalIgnoreCase));
				if (saved != null)
				{
					return saved.File;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void EnsureWorker()
	{
		lock (Sync)
		{
			if (_workerRunning)
			{
				Monitor.PulseAll(Sync);
				return;
			}
			_workerRunning = true;
		}
		Thread thread = new Thread(WorkerLoop)
		{
			IsBackground = true,
			Name = "IsaClient Music Worker"
		};
		thread.Start();
	}

	/// <summary>
	/// Background worker: downloads the first two songs of the queue (current + next, so the next one starts
	/// instantly), starts playback when idle and finds related songs for autoplay.
	/// </summary>
	private static void WorkerLoop()
	{
		try
		{
			while (Enabled)
			{
				Track toDownload = null;
				bool startNext = false;
				bool fetchAutoplay = false;
				lock (Sync)
				{
					for (int i = 0; i < Math.Min(PrefetchCount, TrackQueue.Count); i++)
					{
						if (TrackQueue[i].FilePath == null)
						{
							toDownload = TrackQueue[i];
							break;
						}
					}
					startNext = !_advancePending && CurrentTrack == null && TrackQueue.Count > 0 && TrackQueue[0].FilePath != null;
					if (startNext)
					{
						_advancePending = true;
					}
					fetchAutoplay = AutoplayEnabled && TrackQueue.Count == 0 && !_autoplayFetching && _recentIds.Count > 0;
				}
				if (startNext)
				{
					Main.QueueMainThreadAction(delegate
					{
						Advance(skipCurrent: false);
					});
				}
				if (toDownload != null)
				{
					Resolve(toDownload);
					continue;
				}
				if (fetchAutoplay)
				{
					FetchAutoplay();
					continue;
				}
				lock (Sync)
				{
					Monitor.Wait(Sync, 1000);
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			lock (Sync)
			{
				_workerRunning = false;
			}
		}
	}

	private static void Resolve(Track track)
	{
		if (!File.Exists(YtDlpPath))
		{
			lock (Sync)
			{
				TrackQueue.Remove(track);
			}
			return;
		}
		bool isUrl = track.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
		string target = (isUrl ? track.Source : (SearchPrefix + track.Source));
		if (isUrl && MusicLinks.IsShortLink(track.Source))
		{
			track.Source = MusicLinks.FollowRedirects(track.Source);
			target = track.Source;
		}
		if (isUrl && MusicLinks.IsPlaylistLink(track.Source))
		{
			ExpandPlaylist(track);
			return;
		}
		if (isUrl && MusicLinks.IsLookupService(track.Source))
		{
			// Spotify, Apple Music, Deezer... cannot be downloaded: the same song is looked up by its name.
			string name = MusicLinks.SongNameFromPage(track.Source);
			if (string.IsNullOrEmpty(name))
			{
				lock (Sync)
				{
					TrackQueue.Remove(track);
				}
				SayError("Could not read the song name from " + track.Source);
				return;
			}
			target = SearchPrefix + name;
		}
		int code = DownloadAudio(target, track, out var file, out var output, out var error);
		if ((file == null || !File.Exists(file)) && isUrl && !MusicLinks.IsLookupService(track.Source) && (error + output).IndexOf("Unsupported URL", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			// A page yt-dlp does not know: its title is searched instead.
			string name = MusicLinks.SongNameFromPage(track.Source);
			if (!string.IsNullOrEmpty(name))
			{
				code = DownloadAudio(SearchPrefix + name, track, out file, out output, out error);
			}
		}
		if (file == null || !File.Exists(file))
		{
			lock (Sync)
			{
				TrackQueue.Remove(track);
			}
			SayError("Could not get \"" + track.Source + "\": " + DescribeError(code, output, error));
			return;
		}
		// Saved with its real title as file name; the YouTube id goes into the library and the file's metadata.
		track.FilePath = MusicLibrary.Register(track.Id, track.Title, track.Duration, file, isUrl ? track.Source : null);
		TrimCache();
	}

	/// <summary>Downloads one song with yt-dlp; fills the track's id, title and duration. Returns the exit code.</summary>
	private static int DownloadAudio(string target, Track track, out string file, out string output, out string error)
	{
		Directory.CreateDirectory(SongsFolder);
		int code = DownloadAudioTo(SongsFolder, target, track, out file, out output, out error);
		if ((file == null || !File.Exists(file)) && CouldNotWrite(error + "\n" + output))
		{
			// Some PCs don't let yt-dlp write into Documents (Windows' ransomware protection, or the folder somewhere
			// it can't reach): it downloads into a temporary folder and the game moves the song where it goes.
			string temp = Path.Combine(Path.GetTempPath(), "IsaClient-songs");
			try
			{
				Directory.CreateDirectory(temp);
				code = DownloadAudioTo(temp, target, track, out string tempFile, out output, out error);
				if (tempFile != null && File.Exists(tempFile))
				{
					string moved = Path.Combine(SongsFolder, Path.GetFileName(tempFile));
					if (File.Exists(moved))
					{
						File.Delete(moved);
					}
					File.Move(tempFile, moved);
					file = moved;
				}
			}
			catch (Exception ex)
			{
				error = error + "\n" + ex.Message;
			}
		}
		return code;
	}

	private static bool CouldNotWrite(string text)
	{
		return text.IndexOf("unable to open for writing", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("No such file or directory", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>One yt-dlp download of a song into <paramref name="folder"/>.</summary>
	private static int DownloadAudioTo(string folder, string target, Track track, out string file, out string output, out string error)
	{
		List<string> arguments = new List<string>
		{
			"--no-playlist", "-f", AudioFormat, "-o", Path.Combine(folder, "%(id)s.%(ext)s"), "--no-simulate", "--no-mtime", "--match-filters", "duration <? " + MaxSongMinutes * 60,
			"--print", "before_dl:ISAMETA|%(id)s|%(duration)s|%(extractor_key)s|%(title)s", "--print", "after_move:ISAFILE|%(filepath)s", target
		};
		int code = YtDlp.RunYtDlp(arguments, 300000, out output, out error);
		file = null;
		string rawId = null;
		foreach (string line in output.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith("ISAMETA|"))
			{
				string[] parts = trimmed.Split(new char[1] { '|' }, 5);
				rawId = parts[1];
				string extractor = (parts.Length > 3) ? parts[3] : "Youtube";
				// YouTube songs keep their bare id (11 chars); other sites get a prefix so ids never collide.
				track.Id = (extractor.StartsWith("Youtube", StringComparison.OrdinalIgnoreCase) || extractor == "NA") ? rawId : (extractor.ToLowerInvariant() + ":" + rawId);
				double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out track.Duration);
				track.Title = ((parts.Length > 4) ? parts[4] : track.Source);
			}
			else if (trimmed.StartsWith("ISAFILE|"))
			{
				file = trimmed.Substring(8);
			}
		}
		if ((file == null || !File.Exists(file)) && rawId != null)
		{
			try
			{
				file = Directory.GetFiles(folder, rawId + ".*").FirstOrDefault((string f) => !f.EndsWith(".part") && !f.EndsWith(".ytdl"));
			}
			catch
			{
				file = null;
			}
		}
		return code;
	}
	#region Links

	#endregion

	private static string DescribeError(int code, string output, string error)
	{
		string text = error + "\n" + output;
		if (text.Contains("Sign in to confirm") || text.Contains("cookies"))
		{
			return "YouTube asks for a login. Use .loadcookies (cookies.txt) or .loadcookies firefox.";
		}
		if (text.Contains("JavaScript") || text.Contains("js-runtime") || text.Contains("n challenge"))
		{
			return "yt-dlp needs a JavaScript runtime for this video. Run .ytdlp deno once.";
		}
		if (text.Contains("does not pass filter"))
		{
			return "longer than " + MaxSongMinutes + " minutes (MaxSongMinutes in IsaMusic.json).";
		}
		if (code == -1)
		{
			return "timed out.";
		}
		string last = error.Split('\n').Select((string l) => l.Trim()).LastOrDefault((string l) => l.StartsWith("ERROR")) ?? error.Split('\n').Select((string l) => l.Trim()).LastOrDefault((string l) => l.Length > 0) ?? ("exit code " + code);
		if (last.Length > 200)
		{
			last = last.Substring(0, 200);
		}
		return last;
	}

	private static void ExpandPlaylist(Track playlist)
	{
		string output = "";
		string error = "";
		List<Track> tracks = new List<Track>();
		if (MusicLinks.IsLookupService(playlist.Source))
		{
			try
			{
				tracks = MusicLinks.ReadServiceCollection(playlist.Source);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
		}
		else
		{
			List<string> arguments = new List<string> { "--flat-playlist", "--print", "ISAITEM|%(id)s|%(duration)s|%(ie_key)s|%(url)s|%(title)s", playlist.Source };
			YtDlp.RunYtDlp(arguments, 120000, out output, out error);
		}
		foreach (string line in output.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith("ISAITEM|"))
			{
				string[] parts = trimmed.Split(new char[1] { '|' }, 6);
				if (parts.Length < 6)
				{
					continue;
				}
				double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
				bool youtube = parts[3].StartsWith("Youtube", StringComparison.OrdinalIgnoreCase) || (parts[3] == "NA" && parts[1].Length == 11);
				Track track = new Track
				{
					Id = (youtube ? parts[1] : (parts[3].ToLowerInvariant() + ":" + parts[1])),
					Title = parts[5],
					Duration = duration,
					Source = (youtube ? ("https://www.youtube.com/watch?v=" + parts[1]) : (parts[4].StartsWith("http") ? parts[4] : playlist.Source))
				};
				if (MusicLibrary.TryGetFile(track.Id, out var cached))
				{
					track.FilePath = cached;
				}
				tracks.Add(track);
			}
		}
		lock (Sync)
		{
			int index = TrackQueue.IndexOf(playlist);
			TrackQueue.Remove(playlist);
			if (index < 0)
			{
				index = TrackQueue.Count;
			}
			TrackQueue.InsertRange(Math.Min(index, TrackQueue.Count), tracks);
		}
		if (tracks.Count == 0)
		{
			SayError("The playlist is empty or could not be read: " + DescribeError(0, output, error));
		}
		else
		{
			Say("Queued " + tracks.Count + " songs from the playlist.");
		}
	}

	/// <summary>Autoplay: YouTube's "mix" of the last song gives related songs; the first one not played recently is queued.</summary>
	private static void FetchAutoplay()
	{
		string seed;
		lock (Sync)
		{
			_autoplayFetching = true;
			seed = _recentIds.LastOrDefault((string id) => !id.StartsWith("file:"));
		}
		try
		{
			if (seed == null || !File.Exists(YtDlpPath))
			{
				return;
			}
			List<string> arguments = new List<string>
			{
				"--flat-playlist", "--playlist-items", "2:15", "--print", "ISAITEM|%(id)s|%(duration)s|%(title)s", "https://www.youtube.com/watch?v=" + seed + "&list=RD" + seed
			};
			YtDlp.RunYtDlp(arguments, 90000, out var output, out _);
			foreach (string line in output.Split('\n'))
			{
				string trimmed = line.Trim();
				if (!trimmed.StartsWith("ISAITEM|"))
				{
					continue;
				}
				string[] parts = trimmed.Split(new char[1] { '|' }, 4);
				double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
				lock (Sync)
				{
					if (_recentIds.Contains(parts[1]) || (duration > 0.0 && duration > (double)(MaxSongMinutes * 60)) || !AutoplayEnabled || TrackQueue.Count > 0)
					{
						continue;
					}
					Track track = new Track
					{
						Id = parts[1],
						Title = ((parts.Length > 3) ? parts[3] : parts[1]),
						Duration = duration,
						Source = "https://www.youtube.com/watch?v=" + parts[1],
						Autoplay = true
					};
					if (MusicLibrary.TryGetFile(track.Id, out var cached))
					{
						track.FilePath = cached;
					}
					TrackQueue.Add(track);
				}
				break;
			}
		}
		finally
		{
			lock (Sync)
			{
				_autoplayFetching = false;
				if (TrackQueue.Count == 0)
				{
					// Nothing new found: do not retry until the next song starts.
					_recentIds.Clear();
				}
			}
		}
	}

	/// <summary>Moves to the next song of the queue (main thread).</summary>
	private static void Advance(bool skipCurrent)
	{
		lock (Sync)
		{
			_advancePending = false;
		}
		Track finished = CurrentTrack;
		StopPlayer();
		CurrentTrack = null;
		Track next = null;
		lock (Sync)
		{
			if (finished != null && !skipCurrent && Loop == LoopMode.Song)
			{
				next = finished;
			}
			else
			{
				if (finished != null && !_goingBack)
				{
					_history.Add(finished);
					if (_history.Count > 100)
					{
						_history.RemoveAt(0);
					}
				}
				if (finished != null && Loop == LoopMode.Queue && !finished.Autoplay && !_goingBack)
				{
					TrackQueue.Add(finished);
				}
				if (TrackQueue.Count > 0 && TrackQueue[0].FilePath != null)
				{
					next = TrackQueue[0];
					TrackQueue.RemoveAt(0);
				}
			}
			Monitor.PulseAll(Sync);
		}
		if (next == null)
		{
			if (QueueCount == 0 && finished != null && !AutoplayEnabled && !skipCurrent)
			{
				Say("Queue finished.");
			}
			EnsureWorker();
			return;
		}
		Play(next);
	}

	private static void Play(Track track)
	{
		int generation = ++_generation;
		CurrentTrack = track;
		AudioPlayer player = new AudioPlayer(track.FilePath, Volume);
		player.Finished += delegate
		{
			Main.QueueMainThreadAction(delegate
			{
				if (generation == _generation)
				{
					Advance(skipCurrent: false);
				}
			});
		};
		player.Failed += delegate(string message)
		{
			SayError("Could not play " + track.Display + ": " + message);
			Main.QueueMainThreadAction(delegate
			{
				if (generation == _generation)
				{
					Advance(skipCurrent: true);
				}
			});
		};
		_player = player;
		player.Start();
		_pausedForVideo = false;
		MediaFocus.Claim(MediaFocus.MusicOwner);
		try
		{
			File.SetLastAccessTimeUtc(track.FilePath, DateTime.UtcNow);
		}
		catch
		{
		}
		lock (Sync)
		{
			if (track.Id != null)
			{
				_recentIds.Remove(track.Id);
				_recentIds.Add(track.Id);
				if (_recentIds.Count > 50)
				{
					_recentIds.RemoveAt(0);
				}
			}
			Monitor.PulseAll(Sync);
		}
		if (AnnounceSongs)
		{
			Say((track.Autoplay ? "Autoplay: " : "Now playing: ") + track.Display);
		}
		if (track.Id != null)
		{
			string key = track.Id;
			Task.Run(delegate
			{
				MusicLibrary.NotePlayed(key);
			});
		}
		MusicPlayerWindow.OnSongStarted();
		EnsureWorker();
	}

	private static void StopPlayer()
	{
		_generation++;
		_player?.Stop();
		_player = null;
		_focusPaused = false;
	}

	public static void Stop(bool clearQueue)
	{
		StopPlayer();
		CurrentTrack = null;
		if (clearQueue)
		{
			lock (Sync)
			{
				TrackQueue.Clear();
				_recentIds.Clear();
				Monitor.PulseAll(Sync);
			}
		}
	}

	public static void Skip(int count = 1)
	{
		lock (Sync)
		{
			for (int i = 1; i < count && TrackQueue.Count > 0; i++)
			{
				TrackQueue.RemoveAt(0);
			}
		}
		if (CurrentTrack == null && QueueCount == 0)
		{
			Say("Nothing to skip.");
			return;
		}
		Say("Skipped" + ((CurrentTrack != null) ? (": " + CurrentTrack.Display) : "."));
		Advance(skipCurrent: true);
	}

	public static void Pause()
	{
		if (_player == null)
		{
			Say("Nothing playing.");
			return;
		}
		_player.Pause();
		_focusPaused = false;
		_pausedForVideo = false;
		Say("Paused.");
	}

	public static void Resume()
	{
		if (_player == null)
		{
			Say("Nothing playing.");
			return;
		}
		_player.Resume();
		_focusPaused = false;
		_pausedForVideo = false;
		MediaFocus.Claim(MediaFocus.MusicOwner);
		Say("Resumed.");
	}

	public static void TogglePause()
	{
		if (IsPaused)
		{
			Resume();
		}
		else
		{
			Pause();
		}
	}

	public static void SetVolume(float volume)
	{
		Volume = MathHelper.Clamp(volume, 0f, 1f);
		if (_player != null)
		{
			_player.Volume = Volume;
		}
	}

	internal static void Seek(string args)
	{
		if (_player == null)
		{
			Say("Nothing playing.");
			return;
		}
		string[] parts = args.Split(':');
		double seconds = 0.0;
		foreach (string part in parts)
		{
			if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			{
				SayError("Usage: .seek 1:30  (or .seek 90)");
				return;
			}
			seconds = seconds * 60.0 + value;
		}
		_player.Seek(TimeSpan.FromSeconds(seconds));
		Say("Seek to " + FormatTime(TimeSpan.FromSeconds(seconds)) + ".");
	}

	internal static void ShowQueue()
	{
		List<Track> list;
		lock (Sync)
		{
			list = TrackQueue.ToList();
		}
		Say(NowPlayingText + ((Loop != LoopMode.Off) ? ("  [loop " + Loop.ToString().ToLower() + "]") : ""));
		if (list.Count == 0)
		{
			Say("The queue is empty." + (AutoplayEnabled ? " Autoplay will pick related songs." : ""), ClientChat.InfoColor);
			return;
		}
		for (int i = 0; i < Math.Min(10, list.Count); i++)
		{
			Say(i + 1 + ". " + list[i].Display + (list[i].Autoplay ? "  (autoplay)" : "") + ((list[i].FilePath == null) ? "  ..." : ""), ClientChat.InfoColor);
		}
		if (list.Count > 10)
		{
			Say("... and " + (list.Count - 10) + " more.", ClientChat.InfoColor);
		}
	}

	internal static void ShowLibrary(string filter)
	{
		List<MusicLibrary.Song> songs = MusicLibrary.Search(filter).Where((MusicLibrary.Song s) => s.Downloaded).OrderBy((MusicLibrary.Song s) => s.Title).ToList();
		if (songs.Count == 0)
		{
			Say("No downloaded songs" + ((filter.Length > 0) ? (" match '" + filter + "'") : "") + ".");
			return;
		}
		Say(songs.Count + " downloaded song(s) (.play <title> plays the saved file, .musicui shows them all):");
		foreach (MusicLibrary.Song song in songs.Take(12))
		{
			Say(song.Display, ClientChat.InfoColor);
		}
	}

	/// <summary>Keeps Songs/ under MaxCacheMB by deleting the songs played longest ago.</summary>
	private static void TrimCache()
	{
		try
		{
			FileInfo[] files = new DirectoryInfo(SongsFolder).GetFiles();
			long limit = (long)MaxCacheMB * 1024L * 1024L;
			long total = files.Sum((FileInfo f) => f.Length);
			if (total <= limit)
			{
				return;
			}
			string current = CurrentTrack?.FilePath;
			// Favorites and playlist songs are never deleted to make room.
			HashSet<string> kept = MusicLibrary.ProtectedFiles();
			HashSet<string> queued;
			lock (Sync)
			{
				queued = new HashSet<string>(from t in TrackQueue
					where t.FilePath != null
					select t.FilePath, StringComparer.OrdinalIgnoreCase);
			}
			foreach (FileInfo file in files.OrderBy((FileInfo f) => f.LastAccessTimeUtc))
			{
				if (total <= limit)
				{
					break;
				}
				if (!string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase) && !queued.Contains(file.FullName) && !kept.Contains(file.FullName))
				{
					total -= file.Length;
					file.Delete();
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region Player API (music window, mini player)

	public sealed class SearchResult
	{
		public string Id;

		public string Title;

		public double Duration;

		public string Channel;
	}

	public static bool Shuffle;

	private static readonly List<Track> _history = new List<Track>();

	private static bool _goingBack;

	private static List<SearchResult> _searchResults = new List<SearchResult>();

	public static bool Searching { get; private set; }

	public static string SearchedFor { get; private set; } = "";

	public static List<SearchResult> SearchResults
	{
		get
		{
			lock (Sync)
			{
				return _searchResults.ToList();
			}
		}
	}

	public static string CurrentKey => CurrentTrack?.Id;

	public static bool HasSong => _player != null && CurrentTrack != null;

	public static TimeSpan Position => _player?.Position ?? TimeSpan.Zero;

	public static TimeSpan Length
	{
		get
		{
			AudioPlayer player = _player;
			Track current = CurrentTrack;
			if (player != null && player.Duration.Ticks > 0)
			{
				return player.Duration;
			}
			return (current != null && current.Duration > 0.0) ? TimeSpan.FromSeconds(current.Duration) : TimeSpan.Zero;
		}
	}

	/// <summary>A song is being downloaded / about to start.</summary>
	public static bool Loading => CurrentTrack == null && (QueueCount > 0 || Installing);

	public static List<Track> QueueSnapshot()
	{
		lock (Sync)
		{
			return TrackQueue.ToList();
		}
	}

	public static int HistoryCount
	{
		get
		{
			lock (Sync)
			{
				return _history.Count;
			}
		}
	}

	public static Track TrackFor(MusicLibrary.Song song)
	{
		return new Track
		{
			Id = song.Key,
			Title = song.Title,
			Duration = song.Duration,
			Source = song.Source,
			FilePath = (song.Downloaded ? song.File : null)
		};
	}

	public static Track TrackFor(SearchResult result)
	{
		string file;
		return new Track
		{
			Id = result.Id,
			Title = result.Title,
			Duration = result.Duration,
			Source = "https://www.youtube.com/watch?v=" + result.Id,
			FilePath = (MusicLibrary.TryGetFile(result.Id, out file) ? file : null)
		};
	}

	private static void EnableQuietly()
	{
		if (!Enabled)
		{
			Enabled = true;
			Save();
		}
	}

	private static void Kick(Track track)
	{
		if (track != null && track.FilePath != null)
		{
			EnsureWorker();
		}
		else
		{
			YtDlp.EnsureYtDlp(EnsureWorker);
		}
	}

	/// <summary>Plays a list (a playlist, the favorites, the library...) from <paramref name="start" />: it replaces the queue.</summary>
	public static void PlayList(IList<Track> tracks, int start, string label = null)
	{
		if (tracks == null || tracks.Count == 0)
		{
			return;
		}
		EnableQuietly();
		start = Utils.Clamp(start, 0, tracks.Count - 1);
		List<Track> order = tracks.Skip(start).Concat(tracks.Take(start)).ToList();
		if (Shuffle && order.Count > 2)
		{
			Track first = order[0];
			order = new List<Track> { first }.Concat(order.Skip(1).OrderBy((Track _) => Main.rand.Next())).ToList();
		}
		lock (Sync)
		{
			TrackQueue.Clear();
			TrackQueue.AddRange(order);
			Monitor.PulseAll(Sync);
		}
		Say("Playing " + (label ?? order[0].Display) + ((order.Count > 1) ? (" (" + order.Count + " songs)") : ""));
		if (CurrentTrack != null)
		{
			Advance(skipCurrent: true);
		}
		Kick(order[0]);
	}

	/// <summary>Plays one song right away; the queue stays after it.</summary>
	public static void PlayNow(Track track)
	{
		if (track == null)
		{
			return;
		}
		EnableQuietly();
		lock (Sync)
		{
			TrackQueue.Insert(0, track);
			Monitor.PulseAll(Sync);
		}
		if (CurrentTrack != null)
		{
			Advance(skipCurrent: true);
		}
		Kick(track);
	}

	/// <summary>Adds songs to the queue: at the end (before autoplay picks) or right after the current song.</summary>
	public static void Enqueue(IList<Track> tracks, bool next = false)
	{
		if (tracks == null || tracks.Count == 0)
		{
			return;
		}
		EnableQuietly();
		lock (Sync)
		{
			if (next)
			{
				TrackQueue.InsertRange(0, tracks);
			}
			else
			{
				int firstAuto = TrackQueue.FindIndex((Track t) => t.Autoplay);
				TrackQueue.InsertRange((firstAuto < 0) ? TrackQueue.Count : firstAuto, tracks);
			}
			Monitor.PulseAll(Sync);
		}
		Say(((tracks.Count == 1) ? ("Queued" + (next ? " next" : "") + ": " + tracks[0].Display) : ("Queued " + tracks.Count + " songs" + (next ? " next" : "") + ".")));
		Kick(tracks[0]);
	}

	public static void RemoveQueued(int index)
	{
		lock (Sync)
		{
			if (index >= 0 && index < TrackQueue.Count)
			{
				TrackQueue.RemoveAt(index);
				Monitor.PulseAll(Sync);
			}
		}
	}

	public static void MoveQueued(int from, int to)
	{
		lock (Sync)
		{
			if (from < 0 || from >= TrackQueue.Count || to < 0 || to >= TrackQueue.Count)
			{
				return;
			}
			Track track = TrackQueue[from];
			TrackQueue.RemoveAt(from);
			TrackQueue.Insert(to, track);
			Monitor.PulseAll(Sync);
		}
	}

	public static void ClearQueue()
	{
		lock (Sync)
		{
			TrackQueue.Clear();
			Monitor.PulseAll(Sync);
		}
	}

	/// <summary>Skips straight to a queued song.</summary>
	public static void JumpTo(int index)
	{
		Track target;
		lock (Sync)
		{
			if (index < 0 || index >= TrackQueue.Count)
			{
				return;
			}
			target = TrackQueue[index];
			TrackQueue.RemoveRange(0, index);
			Monitor.PulseAll(Sync);
		}
		Advance(skipCurrent: true);
		Kick(target);
	}

	/// <summary>Back to the start of the song, or (in its first seconds) to the previous one.</summary>
	public static void Previous()
	{
		AudioPlayer player = _player;
		if (player != null && player.Position.TotalSeconds > 3.0)
		{
			player.Seek(TimeSpan.Zero);
			return;
		}
		Track previous;
		lock (Sync)
		{
			if (_history.Count == 0)
			{
				previous = null;
			}
			else
			{
				previous = _history[_history.Count - 1];
				_history.RemoveAt(_history.Count - 1);
				if (CurrentTrack != null)
				{
					TrackQueue.Insert(0, CurrentTrack);
				}
				TrackQueue.Insert(0, previous);
			}
		}
		if (previous == null)
		{
			player?.Seek(TimeSpan.Zero);
			return;
		}
		_goingBack = true;
		try
		{
			Advance(skipCurrent: true);
		}
		finally
		{
			_goingBack = false;
		}
		Kick(previous);
	}

	public static void SeekTo(TimeSpan position)
	{
		_player?.Seek(position);
	}

	public static void ToggleShuffle()
	{
		Shuffle = !Shuffle;
		if (Shuffle)
		{
			lock (Sync)
			{
				List<Track> manual = TrackQueue.Where((Track t) => !t.Autoplay).OrderBy((Track _) => Main.rand.Next()).ToList();
				List<Track> auto = TrackQueue.Where((Track t) => t.Autoplay).ToList();
				TrackQueue.Clear();
				TrackQueue.AddRange(manual);
				TrackQueue.AddRange(auto);
				Monitor.PulseAll(Sync);
			}
		}
		Save();
	}

	public static void CycleLoop()
	{
		Loop = (LoopMode)((int)(Loop + 1) % 3);
		Save();
	}

	/// <summary>YouTube search (up to 15 results) for the music window, in the background.</summary>
	public static void SearchYouTube(string query)
	{
		query = (query ?? "").Trim();
		if (query.Length == 0 || Searching)
		{
			return;
		}
		Searching = true;
		SearchedFor = query;
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				List<SearchResult> results = new List<SearchResult>();
				try
				{
					YtDlp.RunYtDlp(new List<string> { "--flat-playlist", "--print", "ISAITEM|%(id)s|%(duration)s|%(channel)s|%(title)s", "ytsearch15:" + query }, 90000, out var output, out _);
					foreach (string line in output.Split('\n'))
					{
						string trimmed = line.Trim();
						if (!trimmed.StartsWith("ISAITEM|"))
						{
							continue;
						}
						string[] parts = trimmed.Split(new char[1] { '|' }, 5);
						if (parts.Length < 5 || parts[1].Length != 11)
						{
							continue;
						}
						double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
						results.Add(new SearchResult
						{
							Id = parts[1],
							Duration = duration,
							Channel = (parts[3] == "NA") ? "" : parts[3],
							Title = parts[4]
						});
					}
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
				finally
				{
					lock (Sync)
					{
						_searchResults = results;
					}
					Searching = false;
				}
			});
		});
		if (!File.Exists(YtDlpPath) && !Installing)
		{
			Searching = false;
		}
	}

	/// <summary>Keeps a search result in the library (to favorite it or add it to a playlist before it is downloaded).</summary>
	public static void Remember(SearchResult result)
	{
		if (MusicLibrary.Get(result.Id) == null)
		{
			MusicLibrary.Register(result.Id, result.Title, result.Duration, null, "https://www.youtube.com/watch?v=" + result.Id);
		}
	}
	#endregion
	#region yt-dlp

	#endregion

	public static void SetAutoplay(bool on)
	{
		AutoplayEnabled = on;
		Save();
		if (AutoplayEnabled)
		{
			EnsureWorker();
		}
	}

	public static void SetPlayInBackground(bool on)
	{
		PlayInBackground = on;
		Save();
	}

	/// <summary>Per tick (main thread): pauses with the game's "mute when unfocused" setting unless songs play in the background.</summary>
	public static void Update()
	{
		AudioPlayer player = _player;
		if (player == null)
		{
			return;
		}
		bool allow = PlayInBackground || FocusHelper.AllowMusic;
		if (!allow && !player.IsPaused)
		{
			player.Pause();
			_focusPaused = true;
		}
		else if (allow && _focusPaused)
		{
			player.Resume();
			_focusPaused = false;
		}
	}

	/// <summary>A video started: the song pauses (and remembers it, to come back after the video).</summary>
	private static void OnMediaClaimed(string owner)
	{
		AudioPlayer player = _player;
		if (owner != MediaFocus.MusicOwner && player != null && player.IsAlive && !player.IsPaused)
		{
			player.Pause();
			_focusPaused = false;
			_pausedForVideo = true;
			Say("Paused for the video.");
		}
	}

	/// <summary>The video closed or ended: the song it paused plays again.</summary>
	private static void OnMediaReleased(string owner)
	{
		AudioPlayer player = _player;
		if (owner != MediaFocus.MusicOwner && _pausedForVideo && player != null && player.IsAlive && player.IsPaused)
		{
			player.Resume();
			Say("Music back.");
		}
		_pausedForVideo = false;
	}

	/// <summary>On exit: stop the audio thread.</summary>
	public static void Shutdown()
	{
		StopPlayer();
	}
}
