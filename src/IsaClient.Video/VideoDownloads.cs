using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Music;

namespace IsaClient.Video;

/// <summary>
/// Saves videos as .mp4 files in Videos/: the streams are downloaded in full (sharing what the player already has
/// when it is the same video and quality) and put together into one file (VideoRemux).
/// </summary>
public static class VideoDownloads
{
	public sealed class Job
	{
		public string Id;

		public string Title;

		public int Height;

		public volatile float Progress;

		public volatile string Status = "Starting...";

		public volatile bool Done;

		public volatile bool Failed;

		public volatile bool Cancelled;

		public string Output;
	}

	private static readonly List<Job> _jobs = new List<Job>();

	public static List<Job> Jobs()
	{
		lock (_jobs)
		{
			return new List<Job>(_jobs);
		}
	}

	public static Job Find(string id)
	{
		lock (_jobs)
		{
			return _jobs.LastOrDefault((Job j) => j.Id == id && !j.Failed && !j.Cancelled);
		}
	}

	/// <summary>Saved videos in Videos/, newest first.</summary>
	public static List<FileInfo> Saved()
	{
		try
		{
			DirectoryInfo folder = new DirectoryInfo(VideoPlayer.VideosFolder);
			if (!folder.Exists)
			{
				return new List<FileInfo>();
			}
			return (from f in folder.GetFiles()
				where VideoSource.FileExtensions.Contains(f.Extension.ToLowerInvariant()) && !f.Name.EndsWith(".part.mp4", StringComparison.OrdinalIgnoreCase)
				orderby f.LastWriteTimeUtc descending
				select f).ToList();
		}
		catch
		{
			return new List<FileInfo>();
		}
	}

	/// <summary>.video download &lt;link or name&gt;</summary>
	public static void Start(string query)
	{
		Job job = AddJob(null, query, VideoPlayer.Quality);
		job.Status = "Looking it up...";
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				try
				{
					VideoInfo info = VideoSource.Resolve(query);
					job.Id = info.Id;
					job.Title = info.Title;
					Run(job, info);
				}
				catch (Exception ex)
				{
					Finish(job, ex);
				}
			});
		});
	}

	public static void Start(VideoInfo info, int height)
	{
		Job existing = Find(info.Id);
		if (existing != null && !existing.Done)
		{
			VideoPlayer.Say("Already downloading: " + info.Title);
			return;
		}
		Job job = AddJob(info.Id, info.Title, height);
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				try
				{
					VideoInfo full = info;
					if (!info.IsLocal && (info.Pictures.Count == 0 || info.Expired))
					{
						job.Status = "Looking it up...";
						full = VideoSource.Resolve(info.Source);
					}
					Run(job, full);
				}
				catch (Exception ex)
				{
					Finish(job, ex);
				}
			});
		});
	}

	public static void Cancel(Job job)
	{
		job.Cancelled = true;
	}

	public static void Forget(Job job)
	{
		lock (_jobs)
		{
			_jobs.Remove(job);
		}
	}

	private static Job AddJob(string id, string title, int height)
	{
		Job job = new Job
		{
			Id = id,
			Title = title,
			Height = height
		};
		lock (_jobs)
		{
			_jobs.Add(job);
		}
		VideoPlayer.Say("Downloading: " + title);
		return job;
	}

	private static void Run(Job job, VideoInfo info)
	{
		if (info.Web)
		{
			SaveWeb(job, info);
			return;
		}
		if (info.IsLocal)
		{
			throw new InvalidOperationException("That video is already on this computer.");
		}
		VideoFormat picture = info.PictureFor(job.Height);
		job.Height = picture.Height;
		List<RangeCache> caches = VideoPlayer.ShareCaches(info, picture.Height);
		if (caches == null)
		{
			caches = new List<RangeCache>
			{
				NewCache(info, picture)
			};
			if (!picture.HasSound && info.Sound != null)
			{
				caches.Add(NewCache(info, info.Sound));
			}
		}
		try
		{
			foreach (RangeCache cache in caches)
			{
				cache.DownloadAll = true;
			}
			job.Status = "Downloading " + picture.Height + "p...";
			while (!caches.All((RangeCache c) => c.Complete))
			{
				if (job.Cancelled)
				{
					throw new OperationCanceledException();
				}
				RangeCache failed = caches.FirstOrDefault((RangeCache c) => c.Error != null);
				if (failed != null)
				{
					throw new IOException(failed.Error);
				}
				long total = caches.Sum((RangeCache c) => Math.Max(0L, c.Length));
				float done = (total > 0) ? ((float)caches.Sum((RangeCache c) => Math.Max(0L, c.Length) * (double)c.Progress) / total) : 0f;
				job.Progress = done * 0.92f;
				Thread.Sleep(150);
			}
			job.Status = "Saving the file...";
			Directory.CreateDirectory(VideoPlayer.VideosFolder);
			string output = Path.Combine(VideoPlayer.VideosFolder, FileName(info.Title) + " [" + info.Id + "].mp4");
			MediaInput video = new MediaInput
			{
				Cache = caches[0],
				ContentType = "video/mp4"
			};
			MediaInput audio = picture.HasSound ? new MediaInput
			{
				Cache = caches[0],
				ContentType = "video/mp4"
			} : new MediaInput
			{
				Cache = caches[1],
				ContentType = "audio/mp4"
			};
			VideoRemux.Write(video, audio, output, TimeSpan.FromSeconds(info.Duration), delegate(float p)
			{
				job.Progress = 0.92f + p * 0.08f;
			}, () => job.Cancelled);
			job.Output = output;
			job.Progress = 1f;
			job.Status = "Saved";
			job.Done = true;
			VideoPlayer.Say("Saved: " + Path.GetFileName(output) + " (.video folder opens it; it is in Saved in the video window).");
		}
		catch (Exception ex)
		{
			Finish(job, ex);
		}
		finally
		{
			foreach (RangeCache cache in caches)
			{
				cache.Release();
			}
		}
	}

	/// <summary>A web video (VideoInfo.Web): the copy yt-dlp downloaded to play it goes to Videos/.</summary>
	private static void SaveWeb(Job job, VideoInfo info)
	{
		try
		{
			if (info.LocalPath == null || !File.Exists(info.LocalPath))
			{
				job.Status = "Downloading...";
				VideoSource.Fetch(info, job.Height, null, () => job.Cancelled);
			}
			if (job.Cancelled)
			{
				throw new OperationCanceledException();
			}
			job.Status = "Saving the file...";
			job.Progress = 0.95f;
			Directory.CreateDirectory(VideoPlayer.VideosFolder);
			string output = Path.Combine(VideoPlayer.VideosFolder, FileName(info.Title) + " [" + FileName(info.Id) + "]" + Path.GetExtension(info.LocalPath));
			File.Copy(info.LocalPath, output, overwrite: true);
			job.Output = output;
			job.Progress = 1f;
			job.Status = "Saved";
			job.Done = true;
			VideoPlayer.Say("Saved: " + Path.GetFileName(output) + " (.video folder opens it; it is in Saved in the video window).");
		}
		catch (Exception ex)
		{
			Finish(job, ex);
		}
	}

	private static RangeCache NewCache(VideoInfo info, VideoFormat format)
	{
		string name = "dl-" + info.Id.GetHashCode().ToString("X") + "-" + format.Id + "-" + Environment.TickCount.ToString("X") + (format.HasPicture ? ".mp4" : ".m4a");
		return new RangeCache(format.Url, format.Headers, Path.Combine(VideoPlayer.CacheFolder, name))
		{
			HideEditLists = format.HasPicture,
			DownloadAll = true
		};
	}

	private static void Finish(Job job, Exception ex)
	{
		if (job.Done)
		{
			return;
		}
		job.Failed = true;
		if (ex is OperationCanceledException || job.Cancelled)
		{
			job.Cancelled = true;
			job.Status = "Cancelled";
			return;
		}
		job.Status = "Failed: " + ex.Message;
		VideoPlayer.SayError("Could not download " + (job.Title ?? "the video") + ": " + ex.Message);
		ClientLog.LogException(ex);
	}

	private static string FileName(string title)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string name = new string((title ?? "Video").Select((char c) => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
		if (name.Length > 80)
		{
			name = name.Substring(0, 80).Trim();
		}
		return (name.Length == 0) ? "Video" : name;
	}

	/// <summary>At start: removes stream pieces left by a game that closed while playing.</summary>
	internal static void CleanCache()
	{
		Task.Run(delegate
		{
			try
			{
				if (!Directory.Exists(VideoPlayer.CacheFolder))
				{
					return;
				}
				foreach (string file in Directory.GetFiles(VideoPlayer.CacheFolder))
				{
					try
					{
						if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-2.0))
						{
							File.Delete(file);
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		});
	}
}
