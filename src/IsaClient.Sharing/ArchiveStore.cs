using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using IsaClient.Core;

namespace IsaClient.Sharing;

/// <summary>
/// Files shared through archive.org (schematics, and characters when the pipe is off): an item per file, named after
/// its hash, so anyone can fetch it later and the same file is never uploaded twice. A just-uploaded file can take a
/// while to show up there, so fetching keeps trying for a few minutes.
/// </summary>
public static class ArchiveStore
{
	// A friends' mod's archive.org key (S3-like API, "LOW access:secret").
	private const string Access = "";

	private const string Secret = "";

	/// <summary>An upload or a download going on in the background.</summary>
	public sealed class Job
	{
		/// <summary>0..1.</summary>
		public volatile float Progress;

		public volatile bool Busy = true;

		public volatile bool Done;

		public volatile bool Failed;

		/// <summary>Fetching: archive.org doesn't have it yet (it was just uploaded), trying again in a while.</summary>
		public volatile bool Waiting;

		public string Error;

		/// <summary>Where it ended up (an Imgur picture's link), once done.</summary>
		public string Link;

		public readonly System.DateTime Started = System.DateTime.Now;
	}

	public static string Url(string item, string file) => "https://archive.org/download/" + item + "/" + file;

	public static bool Exists(string url)
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
			req.Method = "HEAD";
			req.Timeout = 15000;
			using HttpWebResponse response = (HttpWebResponse)req.GetResponse();
			return response.StatusCode == HttpStatusCode.OK;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Uploads a file to its item (made if needed); throws when it can't. Runs where it's called.</summary>
	public static void Put(string item, string file, byte[] bytes, string title, string subject, Action<float> progress)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://s3.us.archive.org/" + item + "/" + file);
		req.Method = "PUT";
		req.Timeout = 120000;
		req.ReadWriteTimeout = 120000;
		req.AllowWriteStreamBuffering = false;
		req.ContentLength = bytes.Length;
		req.ContentType = "application/octet-stream";
		req.Headers["Authorization"] = "LOW " + Access + ":" + Secret;
		req.Headers["x-archive-auto-make-bucket"] = "1";
		req.Headers["x-archive-queue-derive"] = "0";
		req.Headers["x-archive-interactive-priority"] = "1";
		req.Headers["x-archive-size-hint"] = bytes.Length.ToString();
		req.Headers["x-archive-meta-mediatype"] = "data";
		req.Headers["x-archive-meta-collection"] = "opensource";
		req.Headers["x-archive-meta-title"] = "uri(" + Uri.EscapeDataString(title) + ")";
		req.Headers["x-archive-meta-subject"] = subject;
		using (Stream body = req.GetRequestStream())
		{
			for (int at = 0; at < bytes.Length; at += 16384)
			{
				int n = Math.Min(16384, bytes.Length - at);
				body.Write(bytes, at, n);
				progress?.Invoke((float)(at + n) / bytes.Length);
			}
		}
		using (req.GetResponse())
		{
		}
	}

	/// <summary>One try at downloading a file; throws <see cref="WebException"/> when it isn't there (yet).</summary>
	public static byte[] Get(string url, Action<float> progress)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
		req.Timeout = 30000;
		req.ReadWriteTimeout = 60000;
		req.UserAgent = "IsaClient";
		using HttpWebResponse response = (HttpWebResponse)req.GetResponse();
		long total = response.ContentLength;
		using Stream stream = response.GetResponseStream();
		using MemoryStream data = new MemoryStream();
		byte[] buffer = new byte[16384];
		int n;
		while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			data.Write(buffer, 0, n);
			if (total > 0)
			{
				progress?.Invoke((float)data.Length / total);
			}
		}
		return data.ToArray();
	}

	/// <summary>Uploads in the background (nothing when the file is there already).</summary>
	public static Job Upload(string item, string file, byte[] bytes, string title, string subject)
	{
		Job job = new Job();
		Task.Run(delegate
		{
			try
			{
				if (!Exists(Url(item, file)))
				{
					Put(item, file, bytes, title, subject, p => job.Progress = p * 0.95f);
				}
				job.Done = true;
			}
			catch (Exception ex)
			{
				job.Failed = true;
				job.Error = ex.Message;
				ClientLog.LogOnce(ex);
			}
			finally
			{
				job.Progress = 1f;
				job.Busy = false;
			}
		});
		return job;
	}

	/// <summary>
	/// Downloads in the background, trying for a few minutes, until <paramref name="valid"/> takes what came (it's
	/// told the bytes, off the main thread) or <paramref name="stop"/> says it came another way.
	/// </summary>
	public static Job Fetch(string url, Func<byte[], bool> valid, Func<bool> stop)
	{
		Job job = new Job();
		Task.Run(async delegate
		{
			try
			{
				for (int attempt = 0; attempt < 40 && !stop(); attempt++)
				{
					if (attempt > 0)
					{
						job.Waiting = true;
						await Task.Delay(Math.Min(15000, 4000 + attempt * 1000));
						if (stop())
						{
							break;
						}
					}
					try
					{
						byte[] bytes = Get(url, p =>
						{
							job.Waiting = false;
							job.Progress = p;
						});
						if (valid(bytes))
						{
							job.Done = true;
							return;
						}
					}
					catch (WebException)
					{
					}
					catch (Exception ex)
					{
						ClientLog.LogException(ex);
					}
				}
				job.Failed = !stop();
			}
			finally
			{
				job.Waiting = false;
				job.Busy = false;
			}
		});
		return job;
	}
}
