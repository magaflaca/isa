using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.Core;

/// <summary>
/// Self updater for IsaClient. On the main menu it asks https://client.hacks.win/api/latest for the current
/// release; when it is newer than <see cref="IsaClient.Core.Version"/> a Terraria styled popup offers the update.
/// Files are downloaded in chunks, verified with SHA-256 and swapped in place: a running exe can be renamed on
/// Windows, so Terraria.exe becomes Terraria.exe.old, the new one takes its name and the game restarts.
/// A release can carry a picture, GIF or short video ("media", <see cref="UpdateMedia"/>): it is shown above the notes
/// and opens bigger in the middle of the screen when clicked.
/// </summary>
public static class Updater
{
	private enum State
	{
		Idle,
		Checking,
		UpToDate,
		Available,
		Downloading,
		Restarting,
		Failed
	}

	private class ManifestFile
	{
		[JsonProperty("path")]
		public string Path;

		[JsonProperty("sha256")]
		public string Sha256;

		[JsonProperty("size")]
		public long Size;

		[JsonProperty("chunks")]
		public int Chunks;
	}

	private class ManifestMedia
	{
		[JsonProperty("type")]
		public string Type;

		[JsonProperty("sha256")]
		public string Sha256;

		[JsonProperty("size")]
		public long Size;

		[JsonProperty("chunks")]
		public int Chunks;

		[JsonProperty("width")]
		public int Width;

		[JsonProperty("height")]
		public int Height;
	}

	private class Manifest
	{
		[JsonProperty("name")]
		public string Name;

		[JsonProperty("version")]
		public string Version;

		/// <summary>Hotfix number of <see cref="Version"/> (0 or missing = the release itself). Older clients ignore it.</summary>
		[JsonProperty("hotfix")]
		public int Hotfix;

		[JsonProperty("notes")]
		public string Notes;

		[JsonProperty("published")]
		public string Published;

		[JsonProperty("files")]
		public List<ManifestFile> Files;

		[JsonProperty("media")]
		public ManifestMedia Media;
	}

	private static UpdateMedia _media;

	private static string RemoteVersion => (_manifest == null) ? "?" : ClientInfo.FormatVersion(_manifest.Version, _manifest.Hotfix);

	/// <summary>0 none, 1 loading, 2 ready, 3 failed.</summary>
	private static int _mediaState;

	private static bool _lightbox;

	private static int _lightboxTicks;

	private static bool _capturedRight;

	private static bool _capturedRightRelease;

	public const string ServerUrl = "https://client.hacks.win";

	private const string OldSuffix = ".isaold";

	private static State _state;

	private static Manifest _manifest;

	private static string _error;

	private static float _progress;

	private static string _progressText = "";

	private static bool _dismissed;

	private static bool _started;

	private static bool _capturedLeft;

	private static bool _capturedLeftRelease;

	private static string _hovered;

	private static readonly object _sync = new object();

	public static bool IsModal
	{
		get
		{
			if (!_dismissed && Main.gameMenu)
			{
				if (_state != State.Available && _state != State.Downloading && _state != State.Restarting)
				{
					return _state == State.Failed;
				}
				return true;
			}
			return false;
		}
	}

	private static string GameDirectory => Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? AppDomain.CurrentDomain.BaseDirectory);

	/// <summary>Called at the start of Main.DrawMenu: the popup is modal, so the menu underneath must not see the clicks.</summary>
	public static void BeginMenuFrame()
	{
		if (!_started && Main._isAsyncLoadComplete)
		{
			_started = true;
			CleanupOldFiles();
			CheckForUpdates();
		}
		if (IsModal)
		{
			_capturedLeft = Main.mouseLeft;
			_capturedLeftRelease = Main.mouseLeftRelease;
			_capturedRight = Main.mouseRight;
			_capturedRightRelease = Main.mouseRightRelease;
			Main.mouseLeft = false;
			Main.mouseLeftRelease = false;
			Main.mouseRight = false;
			Main.mouseRightRelease = false;
		}
	}

	public static void CheckForUpdates()
	{
		lock (_sync)
		{
			if (_state == State.Checking || _state == State.Downloading || _state == State.Restarting)
			{
				return;
			}
			_state = State.Checking;
		}
		Task.Run(delegate
		{
			try
			{
				EnableTls();
				using WebClient webClient = CreateClient();
				Manifest manifest = JsonConvert.DeserializeObject<Manifest>(webClient.DownloadString(ServerUrl + "/api/latest"));
				if (manifest?.Files == null || !Version.TryParse(manifest.Version, out var remote) || !Version.TryParse(ClientInfo.Version, out var local))
				{
					_state = State.UpToDate;
					return;
				}
				_manifest = manifest;
				// Newer version, or the same version with a newer hotfix (1.3.9 -> 1.3.9+hotfix.1).
				bool newer = remote > local || (remote == local && manifest.Hotfix > ClientInfo.Hotfix);
				_state = ((newer && NeedsAnyFile(manifest)) ? State.Available : State.UpToDate);
				if (_state == State.Available)
				{
					_dismissed = false;
					SoundEngine.PlaySound(10);
					LoadMedia(manifest);
				}
			}
			catch (Exception ex)
			{
				// No connection or no release yet: the game keeps working normally.
				Console.WriteLine("[IsaUpdater] check failed: " + ex.Message);
				_state = State.UpToDate;
			}
		});
	}

	/// <summary>The release's picture / animation, downloaded in the background while the popup is already open.</summary>
	private static void LoadMedia(Manifest manifest)
	{
		ManifestMedia media = manifest.Media;
		if (media == null || _mediaState != 0)
		{
			return;
		}
		_mediaState = 1;
		Task.Run(delegate
		{
			try
			{
				using WebClient webClient = CreateClient();
				_media = UpdateMedia.Download((string path) => DownloadWithRetry(webClient, ServerUrl + path), media.Sha256, media.Size, media.Chunks);
				_mediaState = ((_media != null) ? 2 : 3);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[IsaUpdater] media failed: " + ex.Message);
				_mediaState = 3;
			}
		});
	}

	private static bool NeedsAnyFile(Manifest manifest)
	{
		foreach (ManifestFile file in manifest.Files)
		{
			if (!SafePath(file.Path, out var fullPath) || !string.Equals(HashFile(fullPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void StartDownload()
	{
		if (_manifest == null)
		{
			return;
		}
		_state = State.Downloading;
		_progress = 0f;
		_progressText = "Preparing...";
		Manifest manifest = _manifest;
		Task.Run(delegate
		{
			try
			{
				EnableTls();
				string stagingDir = Path.Combine(GameDirectory, ".isaupdate");
				Directory.CreateDirectory(stagingDir);
				List<ManifestFile> pending = manifest.Files.Where((ManifestFile f) => !SafePath(f.Path, out var p) || !string.Equals(HashFile(p), f.Sha256, StringComparison.OrdinalIgnoreCase)).ToList();
				long totalBytes = Math.Max(1L, pending.Sum((ManifestFile f) => f.Size));
				long doneBytes = 0L;
				List<KeyValuePair<string, string>> staged = new List<KeyValuePair<string, string>>();
				using (WebClient webClient = CreateClient())
				{
					foreach (ManifestFile file in pending)
					{
						if (!SafePath(file.Path, out var targetPath))
						{
							throw new InvalidDataException("Invalid path in manifest: " + file.Path);
						}
						string stagedPath = Path.Combine(stagingDir, file.Sha256 + ".part");
						using (FileStream output = new FileStream(stagedPath, FileMode.Create, FileAccess.Write))
						{
							for (int i = 0; i < file.Chunks; i++)
							{
								_progressText = "Downloading " + file.Path + " (" + (i + 1) + "/" + file.Chunks + ")";
								byte[] data = DownloadWithRetry(webClient, ServerUrl + "/api/blob/" + file.Sha256 + "/" + i);
								output.Write(data, 0, data.Length);
								doneBytes += data.Length;
								_progress = Math.Min(0.99f, (float)doneBytes / (float)totalBytes);
							}
						}
						if (!string.Equals(HashFile(stagedPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException("Checksum mismatch for " + file.Path);
						}
						staged.Add(new KeyValuePair<string, string>(stagedPath, targetPath));
					}
				}
				_progressText = "Installing...";
				foreach (KeyValuePair<string, string> item in staged)
				{
					Install(item.Key, item.Value);
				}
				_progress = 1f;
				_progressText = "Update installed. Restarting...";
				_state = State.Restarting;
				Main.QueueMainThreadAction(Restart);
			}
			catch (Exception ex)
			{
				_error = ex.Message;
				_state = State.Failed;
				ClientLog.LogException(ex);
			}
		});
	}

	private static byte[] DownloadWithRetry(WebClient client, string url)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				return client.DownloadData(url);
			}
			catch when (attempt < 4)
			{
				Thread.Sleep(1000 * attempt);
			}
		}
	}

	private static void Install(string stagedPath, string targetPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
		if (File.Exists(targetPath))
		{
			string oldPath = targetPath + OldSuffix;
			if (File.Exists(oldPath))
			{
				File.Delete(oldPath);
			}
			// Renaming works even for the running Terraria.exe; deleting it would not.
			File.Move(targetPath, oldPath);
		}
		File.Move(stagedPath, targetPath);
	}

	private static void Restart()
	{
		try
		{
			ClientSettings.Save();
			Main.SaveSettings();
		}
		catch
		{
		}
		string exe = Assembly.GetEntryAssembly().Location;
		string args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(QuoteArgument));
		Process.Start(new ProcessStartInfo(exe, args)
		{
			WorkingDirectory = Environment.CurrentDirectory,
			UseShellExecute = false
		});
		Environment.Exit(0);
	}

	private static string QuoteArgument(string arg)
	{
		if (arg.Length > 0 && arg.IndexOfAny(new char[2] { ' ', '"' }) < 0)
		{
			return arg;
		}
		return "\"" + arg.Replace("\"", "\\\"") + "\"";
	}

	private static void CleanupOldFiles()
	{
		try
		{
			foreach (string file in Directory.EnumerateFiles(GameDirectory, "*" + OldSuffix, SearchOption.AllDirectories))
			{
				try
				{
					File.Delete(file);
				}
				catch
				{
				}
			}
			string stagingDir = Path.Combine(GameDirectory, ".isaupdate");
			if (Directory.Exists(stagingDir))
			{
				Directory.Delete(stagingDir, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static bool SafePath(string relative, out string fullPath)
	{
		fullPath = null;
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(".."))
		{
			return false;
		}
		// The game itself is published as "Terraria.exe", but people run renamed copies (Terraria-1.3.7.exe...):
		// always update the exe that is actually running, which is also the one Restart() opens again.
		if (string.Equals(relative.Replace('\\', '/'), "Terraria.exe", StringComparison.OrdinalIgnoreCase))
		{
			string running = Assembly.GetEntryAssembly()?.Location;
			if (!string.IsNullOrEmpty(running) && running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				fullPath = Path.GetFullPath(running);
				return true;
			}
		}
		fullPath = Path.GetFullPath(Path.Combine(GameDirectory, relative));
		return fullPath.StartsWith(Path.GetFullPath(GameDirectory), StringComparison.OrdinalIgnoreCase);
	}

	private static string HashFile(string path)
	{
		if (path == null || !File.Exists(path))
		{
			return "";
		}
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "").ToLowerInvariant();
	}

	private static void EnableTls()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	private static WebClient CreateClient()
	{
		WebClient webClient = new WebClient();
		webClient.Headers[HttpRequestHeader.UserAgent] = ClientInfo.Name + "/" + ClientInfo.FullVersion;
		webClient.Headers[HttpRequestHeader.CacheControl] = "no-cache";
		return webClient;
	}

	#region Popup

	public static void Draw(SpriteBatch spriteBatch)
	{
		if (!IsModal)
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		int screenWidth = Main.screenWidth;
		int screenHeight = Main.screenHeight;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, screenWidth + 2, screenHeight + 2), Color.Black * 0.55f);
		bool hasMedia = _manifest?.Media != null && _mediaState != 3;
		int width = Math.Min(hasMedia ? 620 : 560, screenWidth - 32);
		string[] notes = GetNoteLines(font, width - 48, out var noteCount);
		int height = 190 + noteCount * 24;
		Point mediaSize = Point.Zero;
		if (hasMedia)
		{
			mediaSize = FitMedia(width - 48, Math.Max(90, Math.Min(260, screenHeight - 80 - height - 24)));
			height += mediaSize.Y + 24;
		}
		Rectangle panel = new Rectangle((screenWidth - width) / 2, (screenHeight - height) / 2, width, height);
		Utils.DrawInvBG(spriteBatch, panel, new Color(33, 43, 79) * 0.96f);
		Vector2 center = new Vector2(panel.Center.X, panel.Y + 30);
		string title = ((_state == State.Failed) ? "Update failed" : ((_state == State.Available) ? "Update available!" : "Updating IsaClient"));
		Utils.DrawBorderStringBig(spriteBatch, IsaClient.Languages.L10n.T(title), center, (_state == State.Failed) ? new Color(255, 120, 120) : new Color(255, 231, 69), 0.55f, 0.5f, 0.5f);
		float y = panel.Y + 62;
		DrawCentered(spriteBatch, font, ClientInfo.DisplayName + "  ->  v" + RemoteVersion, panel.Center.X, y, Color.White, 1f);
		y += 30f;
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		string hovered = null;
		if (hasMedia)
		{
			Rectangle mediaBox = new Rectangle(panel.Center.X - mediaSize.X / 2, (int)y + 2, mediaSize.X, mediaSize.Y);
			DrawMediaThumbnail(spriteBatch, font, mediaBox, mouse, ref hovered);
			y += mediaSize.Y + 24;
		}
		// The notes as one block in the middle of the panel, every line starting at its left edge (bullets line up).
		float blockWidth = 0f;
		for (int i = 0; i < noteCount; i++)
		{
			blockWidth = Math.Max(blockWidth, font.MeasureString(IsaClient.Languages.L10n.T(notes[i] ?? "")).X * 0.9f);
		}
		float left = panel.Center.X - blockWidth / 2f;
		for (int i = 0; i < noteCount; i++)
		{
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(notes[i] ?? ""), new Vector2(left, y), new Color(200, 205, 230), 0f, Vector2.Zero, new Vector2(0.9f));
			y += 24f;
		}
		if (_lightbox)
		{
			// The big view is on top of everything: the buttons underneath stay quiet.
			mouse = new Point(-1000, -1000);
		}
		switch (_state)
		{
		case State.Available:
		{
			Rectangle update = new Rectangle(panel.Center.X - 170, panel.Bottom - 62, 160, 40);
			Rectangle later = new Rectangle(panel.Center.X + 10, panel.Bottom - 62, 160, 40);
			if (DrawButton(spriteBatch, font, update, "Update now", mouse, ref hovered))
			{
				SoundEngine.PlaySound(10);
				StartDownload();
			}
			if (DrawButton(spriteBatch, font, later, "Later", mouse, ref hovered))
			{
				SoundEngine.PlaySound(11);
				_dismissed = true;
			}
			break;
		}
		case State.Downloading:
		case State.Restarting:
		{
			Rectangle bar = new Rectangle(panel.X + 30, panel.Bottom - 70, panel.Width - 60, 22);
			Utils.DrawInvBG(spriteBatch, bar, new Color(15, 20, 45) * 0.95f);
			Rectangle fill = new Rectangle(bar.X + 4, bar.Y + 4, (int)((float)(bar.Width - 8) * _progress), bar.Height - 8);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, fill, new Color(255, 231, 69));
			DrawCentered(spriteBatch, font, _progressText + "  " + (int)(_progress * 100f) + "%", panel.Center.X, bar.Bottom + 8, Color.White, 0.85f);
			break;
		}
		case State.Failed:
		{
			DrawCentered(spriteBatch, font, _error ?? "Unknown error", panel.Center.X, panel.Bottom - 100, new Color(255, 160, 160), 0.85f);
			Rectangle retry = new Rectangle(panel.Center.X - 170, panel.Bottom - 62, 160, 40);
			Rectangle close = new Rectangle(panel.Center.X + 10, panel.Bottom - 62, 160, 40);
			if (DrawButton(spriteBatch, font, retry, "Retry", mouse, ref hovered))
			{
				SoundEngine.PlaySound(10);
				StartDownload();
			}
			if (DrawButton(spriteBatch, font, close, "Close", mouse, ref hovered))
			{
				SoundEngine.PlaySound(11);
				_dismissed = true;
			}
			break;
		}
		}
		if (_lightbox)
		{
			DrawLightbox(spriteBatch, font, ref hovered);
		}
		if (hovered != null && hovered != _hovered)
		{
			SoundEngine.PlaySound(12);
		}
		_hovered = hovered;
	}

	/// <summary>The media's size inside a box, keeping its aspect ratio (before it is downloaded, the manifest's size).</summary>
	private static Point FitMedia(int maxWidth, int maxHeight)
	{
		float w = (_media != null) ? _media.Width : Math.Max(1, _manifest.Media.Width);
		float h = (_media != null) ? _media.Height : Math.Max(1, _manifest.Media.Height);
		if (w <= 1f || h <= 1f)
		{
			w = 16f;
			h = 9f;
		}
		float scale = Math.Min((float)maxWidth / w, (float)maxHeight / h);
		return new Point(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
	}

	private static Color FrameColor(bool hover)
	{
		return hover ? new Color(255, 231, 69) : new Color(89, 116, 213);
	}

	/// <summary>A framed slot like the game's item slots: dark inventory panel, a border that lights up on hover.</summary>
	private static void DrawFrame(SpriteBatch spriteBatch, Rectangle inner, bool hover, float alpha = 1f)
	{
		Rectangle outer = inner;
		outer.Inflate(8, 8);
		Utils.DrawInvBG(spriteBatch, outer, FrameColor(hover) * 0.95f * alpha);
		Rectangle back = inner;
		back.Inflate(2, 2);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, back, new Color(10, 12, 28) * alpha);
	}

	private static void DrawMediaThumbnail(SpriteBatch spriteBatch, DynamicSpriteFont font, Rectangle box, Point mouse, ref string hovered)
	{
		bool ready = _mediaState == 2 && _media != null;
		bool hover = ready && !_lightbox && box.Contains(mouse);
		DrawFrame(spriteBatch, box, hover);
		if (!ready)
		{
			// Loading: three dots pulsing like the game's own loading texts.
			int dots = (int)(Main.GlobalTimeWrappedHourly * 3f) % 4;
			DrawCentered(spriteBatch, font, "Loading preview" + new string('.', dots), box.Center.X, box.Center.Y - 11, new Color(170, 180, 220), 0.85f);
			return;
		}
		Texture2D texture = _media.Update();
		if (_media.HasPicture)
		{
			spriteBatch.Draw(texture, box, Color.White);
		}
		if (_media.Animated)
		{
			DrawPlaybackLine(spriteBatch, box);
		}
		if (!hover)
		{
			return;
		}
		hovered = "media";
		// Hover: a soft highlight and the hint in the corner.
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, box, Color.White * 0.06f);
		string hint = "Click to enlarge";
		Vector2 hintSize = font.MeasureString(IsaClient.Languages.L10n.T(hint)) * 0.75f;
		Rectangle hintBox = new Rectangle(box.Right - (int)hintSize.X - 14, box.Bottom - (int)hintSize.Y - 10, (int)hintSize.X + 10, (int)hintSize.Y + 4);
		Utils.DrawInvBG(spriteBatch, hintBox, new Color(33, 43, 79) * 0.9f);
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(hint), new Vector2(hintBox.X + 5, hintBox.Y + 3), new Color(255, 231, 69), 0f, Vector2.Zero, new Vector2(0.75f));
		if (_capturedLeft && _capturedLeftRelease)
		{
			_capturedLeftRelease = false;
			_lightbox = true;
			_lightboxTicks = 0;
			SoundEngine.PlaySound(10);
		}
	}

	private static void DrawPlaybackLine(SpriteBatch spriteBatch, Rectangle box)
	{
		Rectangle line = new Rectangle(box.X, box.Bottom - 3, box.Width, 3);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, line, Color.Black * 0.45f);
		line.Width = (int)((float)box.Width * _media.PlaybackProgress);
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, line, new Color(255, 231, 69) * 0.85f);
	}

	/// <summary>The media big in the middle of the screen; click anywhere (or right click) to close it.</summary>
	private static void DrawLightbox(SpriteBatch spriteBatch, DynamicSpriteFont font, ref string hovered)
	{
		if (_media == null)
		{
			_lightbox = false;
			return;
		}
		_lightboxTicks++;
		float t = MathHelper.Clamp((float)_lightboxTicks / 10f, 0f, 1f);
		float ease = 1f - (1f - t) * (1f - t);
		int screenWidth = Main.screenWidth;
		int screenHeight = Main.screenHeight;
		spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, screenWidth + 2, screenHeight + 2), Color.Black * (0.7f * ease));
		// Fit 82% of the screen, but never blow a small picture up more than 3x (it would only get blurry).
		float maxW = screenWidth * 0.82f;
		float maxH = screenHeight * 0.74f;
		float scale = Math.Min(Math.Min(maxW / _media.Width, maxH / _media.Height), 3f) * MathHelper.Lerp(0.88f, 1f, ease);
		int w = (int)(_media.Width * scale);
		int h = (int)(_media.Height * scale);
		Rectangle box = new Rectangle((screenWidth - w) / 2, (screenHeight - h) / 2 + 12, w, h);
		Utils.DrawBorderStringBig(spriteBatch, IsaClient.Languages.L10n.T("IsaClient v" + RemoteVersion), new Vector2(screenWidth / 2, box.Y - 40), new Color(255, 231, 69) * ease, 0.6f, 0.5f, 0.5f);
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		DrawFrame(spriteBatch, box, hover: false, ease);
		Texture2D texture = _media.Update();
		if (_media.HasPicture)
		{
			spriteBatch.Draw(texture, box, Color.White * ease);
		}
		if (_media.Animated)
		{
			DrawPlaybackLine(spriteBatch, box);
		}
		Rectangle close = new Rectangle(box.Center.X - 70, box.Bottom + 20, 140, 36);
		bool closeHover = close.Contains(mouse);
		Utils.DrawInvBG(spriteBatch, close, closeHover ? new Color(73, 94, 171) : (new Color(63, 82, 151) * 0.8f));
		DrawCentered(spriteBatch, font, "Close", close.Center.X, close.Y + 7, closeHover ? new Color(255, 231, 69) : Color.White, 1f);
		if (closeHover)
		{
			hovered = "close media";
		}
		if (_lightboxTicks > 5 && ((_capturedLeft && _capturedLeftRelease) || (_capturedRight && _capturedRightRelease)))
		{
			_capturedLeftRelease = false;
			_capturedRightRelease = false;
			_lightbox = false;
			SoundEngine.PlaySound(11);
		}
	}

	private static string[] GetNoteLines(DynamicSpriteFont font, int width, out int count)
	{
		string notes = _manifest?.Notes;
		if (string.IsNullOrWhiteSpace(notes))
		{
			count = 1;
			return new string[1] { "A new version of the client is ready." };
		}
		string[] result = Utils.WordwrapString(IsaClient.Languages.L10n.T(notes.Replace("\r", "")), font, (int)((float)width / 0.9f), 8, out count);
		count = Math.Min(count + 1, 8);
		return result;
	}

	private static bool DrawButton(SpriteBatch spriteBatch, DynamicSpriteFont font, Rectangle box, string text, Point mouse, ref string hovered)
	{
		bool isHovered = box.Contains(mouse);
		Utils.DrawInvBG(spriteBatch, box, isHovered ? new Color(73, 94, 171) : (new Color(63, 82, 151) * 0.8f));
		DrawCentered(spriteBatch, font, text, box.Center.X, box.Y + 9, isHovered ? new Color(255, 231, 69) : Color.White, 1f);
		if (!isHovered)
		{
			return false;
		}
		hovered = text;
		if (_capturedLeft && _capturedLeftRelease)
		{
			_capturedLeftRelease = false;
			return true;
		}
		return false;
	}

	private static void DrawCentered(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, float centerX, float y, Color color, float scale)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		Vector2 size = font.MeasureString(IsaClient.Languages.L10n.T(text)) * scale;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(text), new Vector2(centerX - size.X / 2f, y), color, 0f, Vector2.Zero, new Vector2(scale));
	}

	#endregion
}
