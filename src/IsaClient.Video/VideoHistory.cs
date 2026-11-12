using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Core;
using Newtonsoft.Json;

namespace IsaClient.Video;

/// <summary>Recently watched videos (Videos/History.json), with where you stopped, for "Continue watching".</summary>
public static class VideoHistory
{
	public sealed class Entry
	{
		public string Id;

		public string Title;

		public string Channel;

		public double Duration;

		public string Source;

		public string Thumbnail;

		public string LocalPath;

		/// <summary>Seconds watched (0 when it was watched to the end).</summary>
		public double Position;

		public DateTime WatchedUtc;

		[JsonIgnore]
		public float Watched => (Duration > 0.0) ? ((float)Math.Min(1.0, Position / Duration)) : 0f;

		public VideoInfo ToInfo()
		{
			if (LocalPath != null)
			{
				VideoInfo file = VideoSource.FromFile(LocalPath);
				file.Duration = Duration;
				return file;
			}
			return new VideoInfo
			{
				Id = Id,
				Title = Title,
				Channel = Channel,
				Duration = Duration,
				Source = Source,
				Thumbnail = Thumbnail
			};
		}
	}

	private const int MaxEntries = 60;

	private static readonly object _lock = new object();

	private static List<Entry> _entries;

	private static string FilePath => Path.Combine(VideoPlayer.VideosFolder, "History.json");

	public static List<Entry> Recent()
	{
		lock (_lock)
		{
			Load();
			return new List<Entry>(_entries);
		}
	}

	/// <summary>Remembers a video and where it is (seconds).</summary>
	public static void Note(VideoInfo info, double position)
	{
		if (info == null)
		{
			return;
		}
		lock (_lock)
		{
			Load();
			Entry entry = _entries.FirstOrDefault((Entry e) => e.Id == info.Id);
			if (entry == null)
			{
				entry = new Entry
				{
					Id = info.Id
				};
			}
			_entries.Remove(entry);
			_entries.Insert(0, entry);
			entry.Title = info.Title;
			entry.Channel = info.Channel;
			entry.Duration = (info.Duration > 0.0) ? info.Duration : entry.Duration;
			entry.Source = info.Source;
			entry.Thumbnail = info.Thumbnail;
			// A web video's copy is temporary: it is downloaded again from its page.
			entry.LocalPath = info.Web ? null : info.LocalPath;
			// Near the start or the end: next time it starts over.
			entry.Position = (position < 10.0 || (entry.Duration > 0.0 && position > entry.Duration - 15.0)) ? 0.0 : position;
			entry.WatchedUtc = DateTime.UtcNow;
			if (_entries.Count > MaxEntries)
			{
				_entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
			}
			Save();
		}
	}

	public static void Forget(string id)
	{
		lock (_lock)
		{
			Load();
			_entries.RemoveAll((Entry e) => e.Id == id);
			Save();
		}
	}

	public static void Clear()
	{
		lock (_lock)
		{
			_entries = new List<Entry>();
			Save();
		}
	}

	private static void Load()
	{
		if (_entries != null)
		{
			return;
		}
		try
		{
			_entries = File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(FilePath)) : null;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		_entries = _entries ?? new List<Entry>();
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(VideoPlayer.VideosFolder);
			string temp = FilePath + ".tmp";
			File.WriteAllText(temp, JsonConvert.SerializeObject(_entries, Formatting.Indented));
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			File.Move(temp, FilePath);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
