using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Music;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Video;

/// <summary>
/// The video player: .video &lt;name | link | file&gt; finds the video with yt-dlp, streams it into the video window
/// and keeps what is playing (volume, speed, quality, loop). A video pauses the music player and the music comes
/// back when the video closes; the game's sounds fade out while it plays (MediaFocus).
/// </summary>
public static class VideoPlayer
{
	[Setting("VideoVolume", Min = 0, Max = 1)]
	public static float Volume = 0.8f;

	/// <summary>Highest picture height to stream (the quality menu picks the exact one).</summary>
	[Setting("VideoQuality", Min = 144, Max = 2160)]
	public static int Quality = 720;

	/// <summary>Sound track language: "" the game's language, "original" the video's own, or a code ("en-US").</summary>
	[Setting("VideoAudioLanguage")]
	public static string AudioLanguage = "";

	public static bool Muted;

	public static bool Loop;

	public static float Speed = 1f;

	public static readonly float[] Speeds = new float[8] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };

	private static VideoPlayback _playback;

	private static readonly List<RangeCache> _caches = new List<RangeCache>();

	private static int _generation;

	private static bool _initialized;

	private static double _historySavedAt;

	private static DateTime _stuckSince = DateTime.MaxValue;

	private static int _recoveries;

	/// <summary>What is open in the player (playing, paused, loading or ended).</summary>
	public static VideoInfo Current { get; private set; }

	/// <summary>The picture height playing now.</summary>
	public static int CurrentHeight { get; private set; }

	/// <summary>Finding the video (yt-dlp) or opening its streams.</summary>
	public static bool Loading { get; private set; }

	public static string LoadingWhat { get; private set; } = "";

	public static string Error { get; private set; }

	public static bool Ended { get; private set; }

	public static string VideosFolder => Path.Combine(ClientSettings.IsaClientFolder, "Videos");

	internal static string CacheFolder => Path.Combine(VideosFolder, "Cache");

	public static bool HasVideo => Current != null;

	public static bool IsPlaying => _playback != null && !_playback.IsPaused && !Ended;

	public static bool IsPaused => _playback != null && _playback.IsPaused;

	public static bool IsBuffering => _playback != null && _playback.IsBuffering;

	public static TimeSpan Position => _playback?.Position ?? TimeSpan.Zero;

	public static TimeSpan Length
	{
		get
		{
			TimeSpan length = _playback?.Duration ?? TimeSpan.Zero;
			if (length <= TimeSpan.Zero && Current != null)
			{
				length = TimeSpan.FromSeconds(Current.Duration);
			}
			return length;
		}
	}

	/// <summary>Downloaded parts of the picture stream (fractions), for the seek bar.</summary>
	public static List<KeyValuePair<float, float>> Buffered()
	{
		lock (_caches)
		{
			return (_caches.Count > 0) ? _caches[0].Ranges() : new List<KeyValuePair<float, float>>();
		}
	}

	public static int VideoWidth => _playback?.VideoWidth ?? 0;

	public static int VideoHeight => _playback?.VideoHeight ?? 0;

	/// <summary>Main thread: the newest picture.</summary>
	public static Texture2D Frame()
	{
		VideoPlayback playback = _playback;
		if (playback == null || !playback.HasPicture)
		{
			return null;
		}
		return playback.Frame(Main.instance.GraphicsDevice);
	}

	#region Playing

	/// <summary>Plays a search, a link or a file (what .video gets).</summary>
	public static void Play(string query)
	{
		query = (query ?? "").Trim().Trim('"');
		if (query.Length == 0)
		{
			return;
		}
		Initialize();
		VideoWindow.ShowPlayer();
		if (File.Exists(query))
		{
			Open(VideoSource.FromFile(query), TimeSpan.Zero);
			return;
		}
		int generation = ++_generation;
		StopPlayback(release: false);
		Current = null;
		Error = null;
		Ended = false;
		Loading = true;
		LoadingWhat = VideoSource.IsLink(query) ? "Opening the link..." : ("Searching \"" + query + "\"...");
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				try
				{
					VideoInfo info = VideoSource.Resolve(query);
					FetchIfWeb(info, generation);
					Main.QueueMainThreadAction(delegate
					{
						if (generation == _generation)
						{
							Open(info, TimeSpan.Zero);
						}
					});
				}
				catch (Exception ex)
				{
					Main.QueueMainThreadAction(delegate
					{
						if (generation == _generation)
						{
							Fail(ex.Message);
						}
					});
				}
			});
		});
	}

	/// <summary>Plays something from the browse page (a search result or a recent video), from <paramref name="start"/>.</summary>
	public static void Play(VideoInfo info, TimeSpan start)
	{
		Initialize();
		VideoWindow.ShowPlayer();
		if (info.Web && info.LocalPath != null && !File.Exists(info.LocalPath))
		{
			// Its copy was cleaned up: downloaded again.
			info.LocalPath = null;
		}
		if (info.IsLocal)
		{
			Open(info, start);
			return;
		}
		if (info.Pictures.Count > 0 && !info.Expired)
		{
			Open(info, start);
			return;
		}
		int generation = ++_generation;
		StopPlayback(release: false);
		Current = info;
		Error = null;
		Ended = false;
		Loading = true;
		LoadingWhat = "Opening " + info.Title + "...";
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				try
				{
					VideoInfo resolved = VideoSource.Resolve(info.Source);
					FetchIfWeb(resolved, generation);
					Main.QueueMainThreadAction(delegate
					{
						if (generation == _generation)
						{
							Open(resolved, start);
						}
					});
				}
				catch (Exception ex)
				{
					Main.QueueMainThreadAction(delegate
					{
						if (generation == _generation)
						{
							Fail(ex.Message);
						}
					});
				}
			});
		});
	}

	/// <summary>
	/// Background thread: a video from a site other than YouTube is downloaded by yt-dlp before it plays
	/// (VideoSource.Fetch); closing it or playing another stops the download.
	/// </summary>
	private static void FetchIfWeb(VideoInfo info, int generation)
	{
		if (!info.Web)
		{
			return;
		}
		Main.QueueMainThreadAction(delegate
		{
			// The title shows while it downloads.
			if (generation == _generation)
			{
				Current = info;
			}
		});
		LoadingWhat = "Downloading from " + SiteName(info) + "...";
		VideoSource.Fetch(info, Quality, delegate(long bytes)
		{
			if (bytes > 0 && generation == _generation)
			{
				LoadingWhat = "Downloading from " + SiteName(info) + "... " + (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
			}
		}, () => generation != _generation);
	}

	private static string SiteName(VideoInfo info)
	{
		int dash = (info.Id ?? "").IndexOf('-');
		return (dash > 0) ? info.Id.Substring(0, dash) : "the site";
	}

	/// <summary>Starts the streams of a found video (main thread).</summary>
	private static void Open(VideoInfo info, TimeSpan start)
	{
		_recoveries = 0;
		Current = info;
		Error = null;
		Ended = false;
		Loading = true;
		LoadingWhat = "Loading...";
		Start(info, info.IsLocal ? 0 : Quality, start, paused: false);
		VideoHistory.Note(info, start.TotalSeconds);
		Say("Playing: " + info.Title + (string.IsNullOrEmpty(info.Channel) ? "" : (" (" + info.Channel + ")")));
	}

	private static void Start(VideoInfo info, int quality, TimeSpan start, bool paused)
	{
		int generation = ++_generation;
		StopPlayback(release: false);
		MediaInput video;
		MediaInput audio;
		if (info.IsLocal)
		{
			video = MediaInput.File(info.LocalPath);
			audio = MediaInput.File(info.LocalPath);
			CurrentHeight = 0;
		}
		else
		{
			VideoFormat picture = info.PictureFor(quality);
			CurrentHeight = picture.Height;
			RangeCache pictureCache = OpenCache(info, picture);
			video = new MediaInput
			{
				Cache = pictureCache,
				ContentType = "video/mp4"
			};
			if (picture.HasSound)
			{
				audio = new MediaInput
				{
					Cache = pictureCache,
					ContentType = "video/mp4"
				};
			}
			else
			{
				audio = new MediaInput
				{
					Cache = OpenCache(info, info.Sound),
					ContentType = "audio/mp4"
				};
			}
		}
		VideoPlayback playback = new VideoPlayback(video, audio, start, Muted ? 0f : Volume, Speed, paused);
		playback.SetDuration(TimeSpan.FromSeconds(info.Duration));
		playback.Finished += delegate
		{
			Main.QueueMainThreadAction(delegate
			{
				if (generation == _generation)
				{
					OnFinished();
				}
			});
		};
		playback.Failed += delegate(string message)
		{
			Main.QueueMainThreadAction(delegate
			{
				if (generation == _generation)
				{
					Fail(message);
				}
			});
		};
		_playback = playback;
		Loading = false;
		// The streams open on the playback's own threads (the first bytes come from the network there).
		playback.Start();
		if (!paused)
		{
			MediaFocus.Claim(MediaFocus.VideoOwner);
		}
	}

	private static RangeCache OpenCache(VideoInfo info, VideoFormat format)
	{
		string name = Safe(info.Id) + "-" + Safe(format.Id) + "-" + Environment.TickCount.ToString("X") + (format.HasPicture ? ".mp4" : ".m4a");
		RangeCache cache = new RangeCache(format.Url, format.Headers, Path.Combine(CacheFolder, name))
		{
			HideEditLists = format.HasPicture
		};
		lock (_caches)
		{
			_caches.Add(cache);
		}
		return cache;
	}

	/// <summary>The caches of what plays now, for a download of the same streams (adds a reference to each).</summary>
	internal static List<RangeCache> ShareCaches(VideoInfo info, int height)
	{
		lock (_caches)
		{
			if (Current == null || info == null || Current.Id != info.Id || CurrentHeight != height || _caches.Count == 0)
			{
				return null;
			}
			foreach (RangeCache cache in _caches)
			{
				cache.AddReference();
			}
			return new List<RangeCache>(_caches);
		}
	}

	private static string Safe(string text)
	{
		char[] chars = (text ?? "x").Select((char c) => char.IsLetterOrDigit(c) ? c : '_').ToArray();
		return new string(chars, 0, Math.Min(chars.Length, 40));
	}

	private static void StopPlayback(bool release)
	{
		VideoPlayback playback = _playback;
		_playback = null;
		if (playback != null)
		{
			SavePosition(playback);
			playback.Stop();
			playback.Dispose();
		}
		lock (_caches)
		{
			foreach (RangeCache cache in _caches)
			{
				cache.Release();
			}
			_caches.Clear();
		}
		if (release)
		{
			MediaFocus.Release(MediaFocus.VideoOwner);
		}
	}

	private static void SavePosition(VideoPlayback playback)
	{
		if (Current != null && playback != null)
		{
			VideoHistory.Note(Current, Ended ? 0.0 : playback.Position.TotalSeconds);
		}
	}

	private static void OnFinished()
	{
		if (Loop && _playback != null)
		{
			_playback.Seek(TimeSpan.Zero);
			return;
		}
		Ended = true;
		_playback?.Pause();
		VideoHistory.Note(Current, 0.0);
		MediaFocus.Release(MediaFocus.VideoOwner);
	}

	private static void Fail(string message)
	{
		Loading = false;
		Error = message;
		StopPlayback(release: true);
		SayError("Video: " + message);
	}

	public static void TogglePause()
	{
		if (_playback == null)
		{
			if (Current != null && !Loading)
			{
				Play(Current, TimeSpan.Zero);
			}
			return;
		}
		if (Ended)
		{
			Replay();
			return;
		}
		if (_playback.IsPaused)
		{
			Resume();
		}
		else
		{
			Pause();
		}
	}

	public static void Pause()
	{
		_playback?.Pause();
		if (Current != null)
		{
			VideoHistory.Note(Current, Position.TotalSeconds);
		}
	}

	public static void Resume()
	{
		if (_playback == null)
		{
			return;
		}
		if (Ended)
		{
			Replay();
			return;
		}
		if (Current != null && !Current.IsLocal && Current.Expired)
		{
			// The stream links stopped working while paused: find them again and go on from here.
			Play(Current, Position);
			return;
		}
		_playback.Resume();
		MediaFocus.Claim(MediaFocus.VideoOwner);
	}

	public static void Replay()
	{
		if (_playback == null)
		{
			return;
		}
		Ended = false;
		_playback.Seek(TimeSpan.Zero);
		_playback.Resume();
		MediaFocus.Claim(MediaFocus.VideoOwner);
	}

	public static void Seek(TimeSpan position)
	{
		if (_playback == null)
		{
			return;
		}
		TimeSpan length = Length;
		if (length > TimeSpan.Zero && position >= length)
		{
			position = length - TimeSpan.FromSeconds(1.0);
		}
		if (position < TimeSpan.Zero)
		{
			position = TimeSpan.Zero;
		}
		if (Ended)
		{
			Ended = false;
			_playback.Resume();
			MediaFocus.Claim(MediaFocus.VideoOwner);
		}
		_playback.Seek(position);
	}

	public static void SeekBy(double seconds)
	{
		Seek(Position + TimeSpan.FromSeconds(seconds));
	}

	/// <summary>Closes the video (the music it paused comes back).</summary>
	public static void Close()
	{
		_generation++;
		StopPlayback(release: true);
		Current = null;
		Loading = false;
		Ended = false;
		Error = null;
	}

	public static void SetVolume(float volume)
	{
		Volume = Math.Max(0f, Math.Min(1f, volume));
		Muted = false;
		if (_playback != null)
		{
			_playback.Volume = Volume;
		}
	}

	public static void ToggleMute()
	{
		Muted = !Muted;
		if (_playback != null)
		{
			_playback.Volume = Muted ? 0f : Volume;
		}
	}

	public static void SetSpeed(float speed)
	{
		Speed = Math.Max(0.25f, Math.Min(2f, speed));
		if (_playback != null)
		{
			_playback.Speed = Speed;
		}
	}

	/// <summary>Another picture quality: the same video goes on from the same moment.</summary>
	public static void SetQuality(int height)
	{
		Quality = height;
		ClientSettings.Save();
		if (Current == null || Current.IsLocal || _playback == null || height == CurrentHeight)
		{
			return;
		}
		if (Current.Expired)
		{
			Play(Current, Position);
			return;
		}
		bool paused = _playback.IsPaused;
		Start(Current, height, Position, paused);
	}

	/// <summary>Another sound track (language): the video goes on from the same moment, and the choice is kept.</summary>
	public static void SetSoundTrack(VideoFormat sound, bool remember = true)
	{
		if (remember)
		{
			AudioLanguage = sound.Original ? "original" : sound.Language;
		}
		ClientSettings.Save();
		if (Current == null || Current.IsLocal || _playback == null || Current.Sound == sound)
		{
			return;
		}
		Current.Sound = sound;
		if (Current.Expired)
		{
			Play(Current, Position);
			return;
		}
		Start(Current, (CurrentHeight > 0) ? CurrentHeight : Quality, Position, _playback.IsPaused);
	}

	public static string SpeedLabel(float speed)
	{
		return speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
	}

	#endregion

	#region Frame

	private static void Initialize()
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		// Video links in the chat play here (Shift+click still opens the browser).
		IsaClient.Community.ChatLinks.IsVideoLink = VideoSource.IsVideoPage;
		IsaClient.Community.ChatLinks.PlayVideo = Play;
		MediaFocus.VideoPlaying = () => IsPlaying;
		MediaFocus.VideoOpen = () => _playback != null && !Ended;
		MediaFocus.Claimed += delegate(string owner)
		{
			if (owner != MediaFocus.VideoOwner && IsPlaying)
			{
				Pause();
			}
		};
		VideoWindow.Register();
		VideoDownloads.CleanCache();
	}

	/// <summary>Per frame (main thread, menu and game).</summary>
	public static void Update()
	{
		if (!_initialized)
		{
			// The window is registered early so .video works from the first frame.
			Initialize();
		}
		VideoThumbnails.Update();
		WatchStream();
		if (_playback != null && Main.timeForVisualEffects - _historySavedAt > 600.0)
		{
			_historySavedAt = Main.timeForVisualEffects;
			if (IsPlaying && Current != null)
			{
				VideoHistory.Note(Current, Position.TotalSeconds);
			}
		}
	}

	/// <summary>
	/// A stream that stopped (a failed or stuck download, a link that expired) starts again from the same moment by
	/// itself, like closing and opening the video does: after 8 seconds of loading, or at once when a download failed.
	/// </summary>
	private static void WatchStream()
	{
		VideoPlayback playback = _playback;
		if (playback == null || Loading || Ended || playback.IsPaused || Current == null)
		{
			_stuckSince = DateTime.MaxValue;
			return;
		}
		string failed;
		lock (_caches)
		{
			failed = _caches.Select((RangeCache c) => c.Error).FirstOrDefault((string e) => e != null);
		}
		if (!playback.IsBuffering && failed == null)
		{
			_stuckSince = DateTime.MaxValue;
			return;
		}
		if (_stuckSince == DateTime.MaxValue)
		{
			_stuckSince = DateTime.UtcNow;
		}
		if (failed == null && (DateTime.UtcNow - _stuckSince).TotalSeconds < 8.0)
		{
			return;
		}
		_stuckSince = DateTime.MaxValue;
		if (_recoveries >= 3)
		{
			if (failed != null)
			{
				Fail(failed);
			}
			return;
		}
		_recoveries++;
		TimeSpan at = playback.Position;
		Say("Video: the stream stopped, reconnecting...");
		if (failed != null && failed.Contains("Not enough free disk space"))
		{
			Fail(failed);
			return;
		}
		if (!Current.IsLocal && (failed != null || Current.Expired))
		{
			// The links may have expired: ask yt-dlp for new ones.
			Current.ResolvedUtc = DateTime.MinValue;
			Play(Current, at);
			return;
		}
		Start(Current, Current.IsLocal ? 0 : ((CurrentHeight > 0) ? CurrentHeight : Quality), at, paused: false);
	}

	#endregion

	#region Chat

	internal static void Say(string text)
	{
		Main.QueueMainThreadAction(delegate
		{
			ClientChat.Info(text);
		});
	}

	internal static void SayError(string text)
	{
		Main.QueueMainThreadAction(delegate
		{
			ClientChat.Error(text);
		});
	}

	#endregion

	#region Chat commands

	/// <summary>.video [name | link | file] | pause | resume | stop | seek | speed | quality | volume | download | mini | max | folder | search</summary>
	internal static void VideoCommand(CommandContext command)
	{
		string first = (command.ArgumentCount > 0) ? command.Parts[1].ToLowerInvariant() : "";
		string rest = (command.ArgumentCount > 0 && command.Rest.Length > command.Parts[1].Length) ? command.Rest.Substring(command.Parts[1].Length).Trim() : "";
		switch (first)
		{
		case "":
			VideoWindow.Toggle();
			return;
		case "pause":
			Pause();
			return;
		case "resume":
		case "play":
			if (rest.Length > 0)
			{
				Play(rest);
			}
			else
			{
				Resume();
			}
			return;
		case "stop":
		case "close":
			Close();
			VideoWindow.Hide();
			return;
		case "seek":
		{
			if (TryParseTime(rest, out var time))
			{
				Seek(time);
			}
			else
			{
				ClientChat.Error("Usage: .video seek 1:30");
			}
			return;
		}
		case "speed":
		{
			if (float.TryParse(rest.TrimEnd('x', 'X'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
			{
				SetSpeed(speed);
			}
			ClientChat.Info("Video speed: " + SpeedLabel(Speed));
			return;
		}
		case "quality":
		{
			string value = rest.ToLowerInvariant().TrimEnd('p');
			if (value == "best" || value == "max")
			{
				SetQuality(1080);
			}
			else if (int.TryParse(value, out var height) && height >= 144)
			{
				SetQuality(height);
			}
			ClientChat.Info("Video quality: up to " + Quality + "p" + ((CurrentHeight > 0) ? (" (playing " + CurrentHeight + "p)") : "") + ".");
			return;
		}
		case "volume":
		case "vol":
		{
			if (float.TryParse(rest, out var volume))
			{
				SetVolume(volume / 100f);
				ClientSettings.Save();
			}
			ClientChat.Info("Video volume: " + (int)Math.Round(Volume * 100f) + "%");
			return;
		}
		case "loop":
			Loop = !Loop;
			ClientChat.Info("Video loop " + (Loop ? "on." : "off."));
			return;
		case "download":
		case "save":
			if (rest.Length > 0)
			{
				VideoDownloads.Start(rest);
			}
			else if (Current != null && (!Current.IsLocal || Current.Web))
			{
				VideoDownloads.Start(Current, (CurrentHeight > 0) ? CurrentHeight : Quality);
			}
			else
			{
				ClientChat.Error("Nothing to download: play a video first, or .video download <link>.");
			}
			return;
		case "mini":
			VideoWindow.SetMode(VideoWindow.WindowMode.Mini);
			return;
		case "max":
		case "full":
		case "fullscreen":
			VideoWindow.SetMode(VideoWindow.WindowMode.Maximized);
			return;
		case "normal":
		case "window":
			VideoWindow.SetMode(VideoWindow.WindowMode.Normal);
			return;
		case "folder":
			Directory.CreateDirectory(VideosFolder);
			Utils.OpenFolder(VideosFolder);
			return;
		case "search":
			VideoWindow.Search(rest);
			return;
		case "quiet":
			MediaFocus.QuietGame = !MediaFocus.QuietGame;
			ClientSettings.Save();
			ClientChat.Info("Game sounds while a video or song plays: " + (MediaFocus.QuietGame ? "quiet." : "on."));
			return;
		default:
			Play(command.Rest);
			return;
		}
	}

	private static bool TryParseTime(string text, out TimeSpan time)
	{
		time = TimeSpan.Zero;
		string[] parts = text.Trim().Split(':');
		double seconds = 0.0;
		foreach (string part in parts)
		{
			if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}
			seconds = seconds * 60.0 + value;
		}
		time = TimeSpan.FromSeconds(seconds);
		return parts.Length > 0 && text.Trim().Length > 0;
	}

	#endregion
}
