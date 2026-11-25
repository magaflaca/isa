using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Music;
using IsaClient.Video;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Gta;

internal static class GtaTrailer
{
	private const string Link = "https://youtu.be/AM7HHe3XdBw";

	private const long ExpectedBytes = 7536444L + 409817L;

	private const double KnownLength = 25.2;

	private static int _busy;

	private static VideoPlayback _playback;

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Games", "GTA VI");

	private static string VideoPath => Path.Combine(Folder, "intro.mp4");

	private static string AudioPath => Path.Combine(Folder, "intro.m4a");

	private static string ReadyMark => Path.Combine(Folder, "ready.txt");

	public static bool Ready => File.Exists(ReadyMark) && File.Exists(VideoPath) && File.Exists(AudioPath);

	public static bool Downloading => Volatile.Read(ref _busy) == 1;

	public static float Progress { get; private set; }

	public static string Error { get; private set; }

	public static void Prepare()
	{
		if (Ready || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
		{
			return;
		}
		Error = null;
		Progress = 0f;
		Task.Run(delegate
		{
			if (!File.Exists(MusicPlayer.YtDlpPath))
			{
				YtDlp.EnsureYtDlp(null);
				while (MusicPlayer.Installing)
				{
					Thread.Sleep(200);
				}
			}
			if (!File.Exists(MusicPlayer.YtDlpPath))
			{
				Error = "Couldn't get the downloader. Check your connection and try again.";
				Volatile.Write(ref _busy, 0);
				return;
			}
			Download();
		});
	}

	private static void Download()
	{
		bool finished = false;
		try
		{
			Directory.CreateDirectory(Folder);
			foreach (string old in Directory.GetFiles(Folder))
			{
				File.Delete(old);
			}
			Task watch = Task.Run(delegate
			{
				while (!Volatile.Read(ref finished))
				{
					long bytes = 0L;
					try
					{
						bytes = Directory.GetFiles(Folder).Sum((string f) => new FileInfo(f).Length);
					}
					catch
					{
					}
					Progress = Math.Min(0.98f, (float)bytes / ExpectedBytes);
					Thread.Sleep(150);
				}
			});
			Fetch("299/298/137/136/bv*[vcodec^=avc1][height<=1080]", VideoPath);
			Fetch("140/ba[ext=m4a]", AudioPath);
			Volatile.Write(ref finished, true);
			watch.Wait(1000);
			HideEditLists(VideoPath);
			HideEditLists(AudioPath);
			File.WriteAllText(ReadyMark, DateTime.UtcNow.ToString("o"));
			Progress = 1f;
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			ClientLog.LogException(ex);
		}
		finally
		{
			Volatile.Write(ref finished, true);
			Volatile.Write(ref _busy, 0);
		}
	}

	private static void Fetch(string formats, string file)
	{
		int code = YtDlp.RunYtDlp(new List<string> { "--no-playlist", "--no-mtime", "--fixup", "never", "-f", formats, "-o", file.Replace("%", "%%"), Link }, 600000, out _, out string error, addCommon: true);
		if (code != 0 || !File.Exists(file))
		{
			string line = (error ?? "").Split('\n').Select((string l) => l.Trim()).LastOrDefault((string l) => l.StartsWith("ERROR", StringComparison.Ordinal));
			throw new InvalidOperationException(line ?? "The download didn't finish.");
		}
	}

	private static void HideEditLists(string path)
	{
		using FileStream f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
		foreach (long at in Children(f, 0L, f.Length, "moov").ToList())
		{
			foreach (long trak in Children(f, at + 8, End(f, at), "trak").ToList())
			{
				foreach (long edts in Children(f, trak + 8, End(f, trak), "edts").ToList())
				{
					f.Position = edts + 4;
					f.Write(Encoding.ASCII.GetBytes("free"), 0, 4);
				}
			}
		}
	}

	private static long End(FileStream f, long at)
	{
		(long size, _) = Box(f, at);
		return at + size;
	}

	private static (long size, string type) Box(FileStream f, long at)
	{
		byte[] head = new byte[16];
		f.Position = at;
		if (f.Read(head, 0, 16) < 8)
		{
			return (0L, "");
		}
		long size = ((long)head[0] << 24) | ((long)head[1] << 16) | ((long)head[2] << 8) | head[3];
		string type = Encoding.ASCII.GetString(head, 4, 4);
		if (size == 1)
		{
			size = 0L;
			for (int i = 8; i < 16; i++)
			{
				size = (size << 8) | head[i];
			}
		}
		else if (size == 0)
		{
			size = f.Length - at;
		}
		return (size, type);
	}

	private static IEnumerable<long> Children(FileStream f, long from, long to, string type)
	{
		long at = from;
		while (at + 8 <= to)
		{
			(long size, string kind) = Box(f, at);
			if (size < 8)
			{
				yield break;
			}
			if (kind == type)
			{
				yield return at;
			}
			at += size;
		}
	}

	public static bool Playing => _playback != null;

	public static bool HasPicture => _playback != null && _playback.HasPicture;

	public static double Position => _playback?.Position.TotalSeconds ?? 0.0;

	public static double Length
	{
		get
		{
			double known = _playback?.Duration.TotalSeconds ?? 0.0;
			return (known > 1.0) ? known : KnownLength;
		}
	}

	public static bool Ended => _playback != null && _playback.Ended;

	public static void Play(float volume)
	{
		Stop();
		_playback = new VideoPlayback(MediaInput.File(VideoPath), MediaInput.File(AudioPath), TimeSpan.Zero, volume, 1f, paused: false);
		_playback.SetDuration(TimeSpan.FromSeconds(KnownLength));
		_playback.Failed += delegate(string message)
		{
			ClientLog.LogException(new InvalidOperationException("GTA VI intro: " + message));
		};
		_playback.Start();
	}

	public static void SetVolume(float volume)
	{
		if (_playback != null)
		{
			_playback.Volume = Math.Max(0f, volume);
		}
	}

	public static Texture2D Frame()
	{
		VideoPlayback playback = _playback;
		if (playback == null || !playback.HasPicture)
		{
			return null;
		}
		return playback.Frame(Main.instance.GraphicsDevice);
	}

	public static void Stop()
	{
		VideoPlayback playback = _playback;
		_playback = null;
		if (playback != null)
		{
			try
			{
				playback.Stop();
				playback.Dispose();
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}
}
