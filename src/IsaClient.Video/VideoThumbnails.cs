using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Video;

/// <summary>Video thumbnails for the browse page: downloaded in the background, turned into textures when drawn.</summary>
internal static class VideoThumbnails
{
	private sealed class Thumbnail
	{
		public byte[] Data;

		public Texture2D Texture;

		public bool Failed;

		public double LastUsed;
	}

	private const int MaxKept = 120;

	private static readonly Dictionary<string, Thumbnail> _thumbnails = new Dictionary<string, Thumbnail>();

	private static readonly SemaphoreSlim _downloads = new SemaphoreSlim(4);

	/// <summary>Main thread (while drawing): the picture, or null while it is on its way.</summary>
	public static Texture2D Get(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}
		Thumbnail thumbnail;
		lock (_thumbnails)
		{
			if (!_thumbnails.TryGetValue(url, out thumbnail))
			{
				thumbnail = new Thumbnail();
				_thumbnails[url] = thumbnail;
				Fetch(url, thumbnail);
			}
		}
		thumbnail.LastUsed = Main.timeForVisualEffects;
		if (thumbnail.Texture == null && thumbnail.Data != null && !thumbnail.Failed)
		{
			try
			{
				using MemoryStream stream = new MemoryStream(thumbnail.Data);
				thumbnail.Texture = Texture2D.FromStream(Main.instance.GraphicsDevice, stream);
			}
			catch
			{
				thumbnail.Failed = true;
			}
			thumbnail.Data = null;
		}
		return thumbnail.Texture;
	}

	private static void Fetch(string url, Thumbnail thumbnail)
	{
		Task.Run(delegate
		{
			_downloads.Wait();
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				using WebClient client = new WebClient();
				client.Headers[HttpRequestHeader.UserAgent] = "IsaClient";
				thumbnail.Data = client.DownloadData(url);
			}
			catch
			{
				thumbnail.Failed = true;
			}
			finally
			{
				_downloads.Release();
			}
		});
	}

	/// <summary>Per frame: forgets the thumbnails not drawn for a while once there are many.</summary>
	public static void Update()
	{
		lock (_thumbnails)
		{
			if (_thumbnails.Count <= MaxKept)
			{
				return;
			}
			foreach (KeyValuePair<string, Thumbnail> old in _thumbnails.OrderBy((KeyValuePair<string, Thumbnail> t) => t.Value.LastUsed).Take(_thumbnails.Count - MaxKept).ToList())
			{
				if (Main.timeForVisualEffects - old.Value.LastUsed < 60.0 || (old.Value.Texture == null && !old.Value.Failed))
				{
					continue;
				}
				old.Value.Texture?.Dispose();
				_thumbnails.Remove(old.Key);
			}
		}
	}
}
