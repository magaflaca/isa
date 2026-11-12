using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Terraria;

namespace IsaClient.ChatImages;

/// <summary>
/// Pictures in the chat. A picture (or a video, as a GIF) is uploaded to Imgur and its link goes in a normal chat message;
/// IsaClient shows Imgur picture links as framed thumbnails under the message (ChatImageLayout), and clicking one opens
/// it in the middle of the screen (ImageViewer). Attaching: the picture button on the chat bar, Ctrl+V, or .sendpic
/// (ChatAttachments). Everything here can be turned off.
/// </summary>
public static class ChatImages
{
	[Setting("ChatImages")]
	public static bool Enabled = true;

	[Setting("ChatImagesShow")]
	public static bool ShowInChat = true;

	/// <summary>
	/// Where your pictures go: 1 only to IsaClient players, through the pipe (the server never sees them); 0 in the
	/// server's chat (as short codes, or links). The text of the message always goes to the server's chat.
	/// </summary>
	[Setting("ChatImagesVia", Min = 0, Max = 1)]
	public static int SendVia = 1;

	/// <summary>True when pictures go through the pipe right now (online with the pipe on).</summary>
	public static bool ViaPipe => SendVia == 1 && IsaClient.Net.Pipe.PipeNet.Enabled && IsaClient.Net.Pipe.PipeNet.Ready;

	/// <summary>How tall thumbnails are, in chat lines.</summary>
	[Setting("ChatImagesSize", Min = 2, Max = 8)]
	public static int ThumbLines = 4;

	[Setting("ChatImagesAnimate")]
	public static bool Animate = true;

	[Setting("ChatImagesAutoLoad")]
	public static bool AutoLoad = true;

	[Setting("ChatImagesButton")]
	public static bool AttachButton = true;

	/// <summary>The GIF button on the chat bar (the GIF search).</summary>
	[Setting("ChatImagesGifButton")]
	public static bool GifButton = true;

	[Setting("ChatImagesPaste")]
	public static bool PasteImages = true;

	[Setting("ChatImagesGifSeconds", Min = 2, Max = 30)]
	public static int GifSeconds = 10;

	[Setting("ChatImagesGifWidth", Min = 160, Max = 640)]
	public static int GifWidth = 360;

	[Setting("ChatImagesGifFps", Min = 5, Max = 25)]
	public static int GifFps = 15;

	/// <summary>Pictures in one message.</summary>
	public const int MaxPerMessage = 8;

	public const int ChatLineHeight = 22;

	/// <summary>Imgur picture links (what IsaClient sends): i.imgur.com/ID.png.</summary>
	private static readonly Regex _imageLink = new Regex(@"^https?://i\.imgur\.com/[A-Za-z0-9]{5,12}\.(?:png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>GIFs from the GIF picker and the action commands: nekos.best (anime), Klipy and Giphy, only their own GIF links.</summary>
	private static readonly Regex _gifLink = new Regex(@"^https://(?:nekos\.best/api/v2/[a-z]{2,16}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.gif|static\.klipy\.com/ii/[0-9a-f]{32}/[0-9a-f]{2}/[0-9a-f]{2}/[A-Za-z0-9]{4,40}\.gif|i\.giphy\.com/media/[A-Za-z0-9]{6,40}/200\.gif)$", RegexOptions.Compiled);

	public static bool ShowsThumbnails => Enabled && ShowInChat;

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "ChatImages");

	internal static string CacheFolder => Path.Combine(Folder, "Cache");

	public static string SavedFolder => Path.Combine(Folder, "Saved");

	public static bool IsImageLink(string url)
	{
		return !string.IsNullOrEmpty(url) && (_imageLink.IsMatch(url.Trim()) || _gifLink.IsMatch(url.Trim()));
	}

	private static bool _loaded;

	/// <summary>At start (once): old cached pictures go.</summary>
	internal static void Load()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		Task.Run(delegate
		{
			try
			{
				if (!Directory.Exists(CacheFolder))
				{
					return;
				}
				foreach (string file in Directory.GetFiles(CacheFolder))
				{
					try
					{
						if (File.GetLastAccessTimeUtc(file) < DateTime.UtcNow.AddDays(-7.0) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7.0))
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

	/// <summary>Per frame (main thread): attachments, thumbnails that are not seen any more give their memory back.</summary>
	public static void Update()
	{
		Load();
		ChatAttachments.Update();
		ChatPictures.Update();
	}

	#region Pictures I sent (to delete them)

	internal sealed class SentPicture
	{
		public string Link;

		public string DeleteHash;

		public DateTime SentUtc;
	}

	private static readonly object _sentLock = new object();

	private static List<SentPicture> _sent;

	private static string SentFile => Path.Combine(Folder, "Sent.json");

	internal static void RememberSent(ImgurClient.Upload upload)
	{
		lock (_sentLock)
		{
			LoadSent();
			_sent.Add(new SentPicture
			{
				Link = upload.Link,
				DeleteHash = upload.DeleteHash,
				SentUtc = DateTime.UtcNow
			});
			if (_sent.Count > 500)
			{
				_sent.RemoveRange(0, _sent.Count - 500);
			}
			SaveSent();
		}
	}

	internal static SentPicture FindSent(string link)
	{
		lock (_sentLock)
		{
			LoadSent();
			return _sent.LastOrDefault(s => string.Equals(s.Link, link, StringComparison.OrdinalIgnoreCase));
		}
	}

	internal static void ForgetSent(SentPicture picture)
	{
		lock (_sentLock)
		{
			LoadSent();
			_sent.RemoveAll(s => s.Link == picture.Link);
			SaveSent();
		}
	}

	private static void LoadSent()
	{
		if (_sent != null)
		{
			return;
		}
		try
		{
			_sent = File.Exists(SentFile) ? (JsonConvert.DeserializeObject<List<SentPicture>>(File.ReadAllText(SentFile)) ?? new List<SentPicture>()) : new List<SentPicture>();
		}
		catch
		{
			_sent = new List<SentPicture>();
		}
	}

	private static void SaveSent()
	{
		try
		{
			Directory.CreateDirectory(Folder);
			File.WriteAllText(SentFile, JsonConvert.SerializeObject(_sent, Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion
}

internal enum PictureState
{
	/// <summary>Not downloaded (or its bytes were let go to save memory).</summary>
	Idle,
	Loading,
	Ready,
	Failed
}

/// <summary>A picture of the chat, by its link: downloaded once (and kept on disk a week), with its thumbnail.</summary>
internal sealed class ChatPicture
{
	public readonly string Url;

	public volatile PictureState State;

	public string Error;

	/// <summary>The picture's bytes (GDI+ can read them); null while not loaded.</summary>
	public byte[] Data;

	/// <summary>Width / height (1 until it is known).</summary>
	public float Aspect = 1f;

	public bool Animated;

	public int SourceWidth;

	public int SourceHeight;

	public double LastDrawn;

	private ImageFrames _thumb;

	private int _thumbHeight;

	private int _thumbWanted;

	private bool _thumbAnimated;

	private bool _thumbLoading;

	public ChatPicture(string url)
	{
		Url = url.Trim();
	}

	public string CacheFile => Path.Combine(ChatImages.CacheFolder, new string(Url.Substring(Url.LastIndexOf('/') + 1).Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray()));

	/// <summary>Starts downloading it (or reading it from the disk) if it is not there yet.</summary>
	public void Request()
	{
		if (State != PictureState.Idle)
		{
			return;
		}
		State = PictureState.Loading;
		Error = null;
		ChatPictures.Run(delegate
		{
			try
			{
				byte[] bytes = null;
				string cached = CacheFile;
				if (File.Exists(cached))
				{
					bytes = File.ReadAllBytes(cached);
					File.SetLastAccessTimeUtc(cached, DateTime.UtcNow);
				}
				if (bytes == null || bytes.Length == 0)
				{
					bytes = ImgurClient.Download(Url, 40 * 1024 * 1024, null);
					try
					{
						Directory.CreateDirectory(ChatImages.CacheFolder);
						File.WriteAllBytes(cached, bytes);
					}
					catch
					{
					}
				}
				byte[] readable = PictureFiles.Readable(bytes) ?? throw new InvalidOperationException("Not a picture IsaClient can show.");
				System.Drawing.Size size = ImageFrames.Measure(readable, out var animated);
				SourceWidth = size.Width;
				SourceHeight = size.Height;
				Aspect = (size.Height > 0) ? ((float)size.Width / size.Height) : 1f;
				Animated = animated;
				Data = readable;
				State = PictureState.Ready;
			}
			catch (Exception ex)
			{
				Error = ex.Message;
				State = PictureState.Failed;
			}
		});
	}

	/// <summary>Main thread: the thumbnail texture at <paramref name="height"/> pixels (null while it is made).</summary>
	public Texture2D Thumbnail(int height, int maxWidth)
	{
		LastDrawn = Main.timeForVisualEffects;
		if (State == PictureState.Idle && ChatImages.AutoLoad)
		{
			Request();
		}
		if (State != PictureState.Ready)
		{
			return null;
		}
		bool animate = ChatImages.Animate;
		if ((_thumb == null || _thumbHeight != height || _thumbAnimated != animate) && !_thumbLoading && Data != null)
		{
			_thumbLoading = true;
			_thumbWanted = height;
			byte[] data = Data;
			ChatPictures.Run(delegate
			{
				ImageFrames made = null;
				try
				{
					made = ImageFrames.Open(data, maxWidth, height, animate);
				}
				catch (Exception ex)
				{
					Error = ex.Message;
					State = PictureState.Failed;
				}
				Main.QueueMainThreadAction(delegate
				{
					_thumbLoading = false;
					if (made == null)
					{
						return;
					}
					if (_thumbWanted != height)
					{
						made.Dispose();
						return;
					}
					_thumb?.Dispose();
					_thumb = made;
					_thumbHeight = height;
					_thumbAnimated = animate;
				});
			});
		}
		return _thumb?.Update(animate);
	}

	/// <summary>Lets go of the thumbnail (and the bytes after a longer while) when it has not been drawn for some time.</summary>
	public void Trim(double now)
	{
		if (_thumb != null && now - LastDrawn > 3600.0)
		{
			_thumb.Dispose();
			_thumb = null;
		}
		if (Data != null && State == PictureState.Ready && now - LastDrawn > 18000.0 && !ImageViewer.Shows(this))
		{
			Data = null;
			State = PictureState.Idle;
		}
	}

	/// <summary>Try again (a failed download).</summary>
	public void Retry()
	{
		if (State == PictureState.Failed)
		{
			State = PictureState.Idle;
		}
		Request();
	}
}

/// <summary>All the chat's pictures by link, and the few threads that download and decode them.</summary>
internal static class ChatPictures
{
	private static readonly Dictionary<string, ChatPicture> _pictures = new Dictionary<string, ChatPicture>(StringComparer.OrdinalIgnoreCase);

	private static readonly SemaphoreSlim _workers = new SemaphoreSlim(3);

	private static int _trimTimer;

	public static ChatPicture Get(string url)
	{
		lock (_pictures)
		{
			if (!_pictures.TryGetValue(url, out var picture))
			{
				picture = new ChatPicture(url);
				_pictures[url] = picture;
			}
			return picture;
		}
	}

	/// <summary>Background work, at most three at a time (a chat full of pictures doesn't take every core).</summary>
	public static void Run(Action work)
	{
		Task.Run(async delegate
		{
			await _workers.WaitAsync();
			try
			{
				work();
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			finally
			{
				_workers.Release();
			}
		});
	}

	public static void Update()
	{
		if (++_trimTimer < 120)
		{
			return;
		}
		_trimTimer = 0;
		List<ChatPicture> all;
		lock (_pictures)
		{
			all = _pictures.Values.ToList();
		}
		double now = Main.timeForVisualEffects;
		foreach (ChatPicture picture in all)
		{
			picture.Trim(now);
		}
	}
}
