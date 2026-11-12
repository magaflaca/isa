using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IsaClient.Core;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace IsaClient.ChatImages;

/// <summary>
/// Pictures waiting to go with the next chat message: added with the picture button at the end of the chat bar, Ctrl+V
/// (a copied picture, or picture and video files copied in Explorer) or .sendpic. They show as small framed chips above
/// the chat bar (click one to remove it). A video becomes a GIF right away (VideoToGif), so sending is quick.
/// When the message is sent, the pictures are uploaded to Imgur and their links go at the end of it.
/// </summary>
public static class ChatAttachments
{
	internal sealed class Attachment
	{
		public string Name;

		public bool IsVideo;

		public volatile float Progress;

		public volatile string Status = "Preparing...";

		public volatile bool Ready;

		public volatile bool Failed;

		public volatile bool Cancelled;

		public string Error;

		public PictureFiles.Prepared Prepared;

		/// <summary>A GIF already online (the GIF picker): sent as this link, nothing to upload.</summary>
		public string Link;

		/// <summary>The smaller GIF the chip shows for <see cref="Link"/>.</summary>
		public string PreviewLink;

		public ImageFrames Preview;

		public bool PreviewLoading;

		/// <summary>A shared character, style or schematic (IsaClient.Sharing): its chat tag, sent like a picture link.</summary>
		public string ShareTag;

		public Action<SpriteBatch, Rectangle> DrawShare;

		/// <summary>False while it still has to get somewhere first (a schematic going up to archive.org).</summary>
		public Func<bool> ShareReady;

		public Func<float> ShareProgress;

		/// <summary>Why it can't go (null: it can).</summary>
		public Func<string> ShareError;

		/// <summary>Its message went (its data starts going through the pipe).</summary>
		public Action ShareSent;

		/// <summary>The tag to send, when it's only known once ready (a file: where its pictures are). Null: <see cref="ShareTag"/>.</summary>
		public Func<string> ShareFinalTag;
	}

	/// <summary>One message being sent: its pictures upload one after the other, then it goes.</summary>
	private sealed class Sending
	{
		public string Text;

		/// <summary>Set when the message goes to the IsaClient chat (.inchat, .w, .r) instead of the server.</summary>
		public Action<string> Route;

		public List<Attachment> Attachments;

		public volatile float Progress;

		public volatile string Status = "Preparing...";
	}

	private static readonly List<Attachment> _pending = new List<Attachment>();

	private static readonly List<Sending> _sending = new List<Sending>();

	/// <summary>Attachments work (and show) when chat pictures are on, or when something shared is attached.</summary>
	private static bool Active => ChatImages.Enabled || _pending.Any(a => a.ShareTag != null) || _sending.Count > 0;

	/// <summary>
	/// A shared character, style or schematic, attached above the chat bar next to the pictures: it goes with the
	/// next message (or a blank line) and shows under it, like a picture.
	/// </summary>
	public static bool AddShare(string name, string tag, Action<SpriteBatch, Rectangle> draw, Func<bool> ready, Func<float> progress, Func<string> error, Action sent, Func<string> finalTag = null)
	{
		if (_pending.Any(a => a.ShareTag == tag))
		{
			return true;
		}
		if (!Room())
		{
			return false;
		}
		_pending.Add(new Attachment
		{
			Name = name,
			ShareTag = tag,
			DrawShare = draw,
			ShareReady = ready,
			ShareProgress = progress,
			ShareError = error,
			ShareSent = sent,
			ShareFinalTag = finalTag,
			Ready = true,
			Progress = 1f,
			Status = "Ready"
		});
		return true;
	}

	private static bool ShareGoes(Attachment a) => a.ShareError?.Invoke() == null && (a.ShareReady?.Invoke() ?? true);

	private static bool _pickerOpen;

	private const int ChipSize = 64;

	public static int Count => _pending.Count;

	/// <summary>The chips above the chat bar push the chat history up this much.</summary>
	public static int StripHeight => (Active && Main.drawingPlayerChat) ? (((_pending.Count > 0) ? (ChipSize + 12) : 0) + ((_sending.Count > 0) ? 28 : 0)) : 0;

	public static bool ShowsButton => ChatImages.Enabled && ChatImages.AttachButton;

	public static bool ShowsGifButton => ChatImages.Enabled && ChatImages.GifButton;

	/// <summary>Room the buttons take at the end of the chat bar.</summary>
	public static float ButtonsWidth => (ShowsButton ? 34f : 0f) + (ShowsGifButton ? 34f : 0f) + (EmoticonPicker.Button ? 32f : 0f) + (IsaClient.Assistant.PluginMaker.PluginAgent.ChatMode ? (ModeChipWidth + 6f) : 0f);

	private const string ModeChipText = "Plugin Maker";

	private static float ModeChipWidth => FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(ModeChipText)).X * 0.7f + 34f;

	/// <summary>Plugin Maker mode's label, in the chat bar left of its buttons (it never covers the chat).</summary>
	private static void DrawModeChip(SpriteBatch sb)
	{
		Rectangle anchor = EmoticonPicker.Button ? EmoticonButtonRect : (ShowsGifButton ? GifButtonRect : ButtonRect);
		int width = (int)ModeChipWidth;
		Rectangle chip = new Rectangle(anchor.X - width - 6, anchor.Center.Y - 11, width, 22);
		bool hover = chip.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Plugin Maker mode: what you write goes to the Plugin Maker, not the server.\n.pai again (or a click here) to leave."), 0, 0);
		}
		Utils.DrawInvBG(sb, chip, (hover ? new Color(60, 100, 150) : new Color(40, 70, 110)) * 0.95f);
		// The working star, small.
		Vector2 center = new Vector2(chip.X + 12, chip.Center.Y);
		float turn = (float)(Main.timeForVisualEffects * 0.02);
		for (int i = 0; i < 4; i++)
		{
			sb.Draw(TextureAssets.MagicPixel.Value, center, new Rectangle(0, 0, 1, 1), new Color(150, 220, 255), turn + i * Microsoft.Xna.Framework.MathHelper.PiOver4, new Vector2(0.5f, 0.5f), new Vector2(11f, 1.6f), SpriteEffects.None, 0f);
		}
		Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(ModeChipText), new Vector2(chip.X + 24, chip.Y + 3), new Color(150, 220, 255), 0.7f);
	}

	/// <summary>A click on the mode's label leaves the mode.</summary>
	private static bool ClickModeChip()
	{
		if (!IsaClient.Assistant.PluginMaker.PluginAgent.ChatMode || PlayerInput.IgnoreMouseInterface || !Main.mouseLeft || !Main.mouseLeftRelease)
		{
			return false;
		}
		Rectangle anchor = EmoticonPicker.Button ? EmoticonButtonRect : (ShowsGifButton ? GifButtonRect : ButtonRect);
		int width = (int)ModeChipWidth;
		if (!new Rectangle(anchor.X - width - 6, anchor.Center.Y - 11, width, 22).Contains(Main.mouseX, Main.mouseY))
		{
			return false;
		}
		Main.mouseLeftRelease = false;
		Main.LocalPlayer.mouseInterface = true;
		SoundEngine.PlaySound(12);
		IsaClient.Assistant.PluginMaker.PluginAgent.SetChatMode(false);
		return true;
	}

	#region Adding

	/// <summary>A picture or video file (.sendpic, the picker, files pasted from Explorer).</summary>
	public static bool AddFile(string path)
	{
		if (!ChatImages.Enabled)
		{
			return false;
		}
		bool video = PictureFiles.IsVideo(path);
		if (!video && !PictureFiles.IsPicture(path))
		{
			ClientChat.Error(Path.GetFileName(path) + " is not a picture or a video IsaClient can send.");
			return false;
		}
		if (!File.Exists(path))
		{
			ClientChat.Error("Can't find " + path);
			return false;
		}
		if (!Room())
		{
			return false;
		}
		Attachment attachment = new Attachment
		{
			Name = Path.GetFileName(path),
			IsVideo = video,
			Status = video ? "Making a GIF..." : "Preparing..."
		};
		_pending.Add(attachment);
		Prepare(attachment, () => video ? PictureFiles.FromVideo(path, ChatImages.GifWidth, ChatImages.GifFps, ChatImages.GifSeconds, p => attachment.Progress = p, () => attachment.Cancelled) : PictureFiles.FromPictureFile(path));
		return true;
	}

	/// <summary>A picture from the clipboard (a screenshot...).</summary>
	private static void AddBitmap(Bitmap bitmap)
	{
		if (!Room())
		{
			bitmap.Dispose();
			return;
		}
		Attachment attachment = new Attachment
		{
			Name = "Pasted picture"
		};
		_pending.Add(attachment);
		Prepare(attachment, delegate
		{
			using (bitmap)
			{
				return PictureFiles.FromBitmap(bitmap, "picture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
			}
		});
	}

	private static bool Room()
	{
		if (_pending.Count >= ChatImages.MaxPerMessage)
		{
			ClientChat.Error("A message takes up to " + ChatImages.MaxPerMessage + " pictures.");
			return false;
		}
		return true;
	}

	private static void Prepare(Attachment attachment, Func<PictureFiles.Prepared> make)
	{
		Task.Run(delegate
		{
			try
			{
				PictureFiles.Prepared prepared = make();
				attachment.Prepared = prepared;
				attachment.Progress = 1f;
				attachment.Status = Size(prepared.Data.Length);
				attachment.Ready = true;
			}
			catch (OperationCanceledException)
			{
				attachment.Failed = true;
			}
			catch (Exception ex)
			{
				attachment.Error = ex.Message;
				attachment.Status = "Failed";
				attachment.Failed = true;
				if (!(ex is InvalidOperationException))
				{
					ClientLog.LogException(ex);
				}
			}
		});
	}

	/// <summary>A GIF from the picker, attached above the chat bar like a picture (it goes as its link).</summary>
	public static bool AddLink(GifItem gif)
	{
		if (gif == null || !ChatImages.IsImageLink(gif.Url) || !Room())
		{
			return false;
		}
		_pending.Add(new Attachment
		{
			Name = string.IsNullOrWhiteSpace(gif.Title) ? "GIF" : gif.Title,
			Link = gif.Url,
			PreviewLink = gif.Preview ?? gif.Url,
			Ready = true,
			Progress = 1f,
			Status = "GIF"
		});
		return true;
	}

	/// <summary>Pictures attached above the chat bar that are ready: taken off it, for the AI (.ask). Data, type, or a link.</summary>
	internal static List<(byte[] Data, string Type, string Url)> TakeReady()
	{
		List<Attachment> ready = _pending.Where(a => a.ShareTag == null && IsUsable(a)).ToList();
		foreach (Attachment attachment in ready)
		{
			_pending.Remove(attachment);
			attachment.Preview?.Dispose();
		}
		return ready.Select(a => (a.Prepared?.Data, a.Prepared?.ContentType, a.Link)).ToList();
	}

	private static bool IsUsable(Attachment a)
	{
		if (a.ShareTag != null)
		{
			return ShareGoes(a);
		}
		return a.Ready && !a.Failed && (a.Prepared != null || a.Link != null);
	}

	/// <summary>Pictures attached that are ready (without taking them).</summary>
	internal static int ReadyCount => _pending.Count(IsUsable);

	internal static void Remove(Attachment attachment)
	{
		attachment.Cancelled = true;
		_pending.Remove(attachment);
		attachment.Preview?.Dispose();
		attachment.Preview = null;
	}

	public static void Clear()
	{
		foreach (Attachment attachment in _pending.ToList())
		{
			Remove(attachment);
		}
	}

	/// <summary>
	/// Ctrl+V in the chat: a copied picture, or picture/video files copied in Explorer, are attached; false when the
	/// clipboard has none (the text is pasted as usual).
	/// </summary>
	public static bool TryPaste()
	{
		if (!ChatImages.Enabled || !ChatImages.PasteImages)
		{
			return false;
		}
		try
		{
			if (Clipboard.ContainsFileDropList())
			{
				StringCollection files = Clipboard.GetFileDropList();
				List<string> usable = files.Cast<string>().Where(f => PictureFiles.IsPicture(f) || PictureFiles.IsVideo(f)).ToList();
				if (usable.Count > 0)
				{
					foreach (string file in usable)
					{
						AddFile(file);
					}
					return true;
				}
			}
			Bitmap bitmap = ClipboardBitmap();
			if (bitmap != null)
			{
				AddBitmap(bitmap);
				return true;
			}
		}
		catch (Exception ex)
		{
			// The clipboard can be busy (another program holds it).
			ClientLog.LogException(ex);
		}
		return false;
	}

	/// <summary>The clipboard's picture: the PNG form when a program put one (it keeps transparency), else the bitmap.</summary>
	private static Bitmap ClipboardBitmap()
	{
		IDataObject data = Clipboard.GetDataObject();
		if (data == null)
		{
			return null;
		}
		if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
		{
			try
			{
				using Image image = Image.FromStream(png);
				return new Bitmap(image);
			}
			catch
			{
			}
		}
		if (Clipboard.ContainsImage())
		{
			using Image image = Clipboard.GetImage();
			return (image == null) ? null : new Bitmap(image);
		}
		return null;
	}

	/// <summary>Windows' file picker (several files at once), on its own thread so the game keeps drawing.</summary>
	public static void OpenPicker()
	{
		if (!ChatImages.Enabled || _pickerOpen)
		{
			return;
		}
		_pickerOpen = true;
		IntPtr owner = Main.instance.Window.Handle;
		Thread thread = new Thread((ThreadStart)delegate
		{
			string[] files = null;
			try
			{
				using OpenFileDialog dialog = new OpenFileDialog
				{
					Title = "Send files in the chat",
					Multiselect = true,
					Filter = "All files|*.*|Pictures and videos|" + Pattern(PictureFiles.PictureExtensions.Concat(PictureFiles.VideoExtensions)) + "|Pictures|" + Pattern(PictureFiles.PictureExtensions) + "|Videos (sent as a GIF)|" + Pattern(PictureFiles.VideoExtensions) + "|Characters, schematics and items|*.plr;*.TEditSch;*.isaitem",
					InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
				};
				if (dialog.ShowDialog(new WindowOwner(owner)) == DialogResult.OK)
				{
					files = dialog.FileNames;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			Main.QueueMainThreadAction(delegate
			{
				_pickerOpen = false;
				if (files == null)
				{
					return;
				}
				foreach (string file in files)
				{
					IsaClient.Sharing.FileShare.Attach(file);
				}
				if (!Main.gameMenu && !Main.drawingPlayerChat)
				{
					Main.OpenPlayerChat();
				}
			});
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	private static string Pattern(IEnumerable<string> extensions)
	{
		return string.Join(";", extensions.Select(e => "*" + e));
	}

	private sealed class WindowOwner : IWin32Window
	{
		public WindowOwner(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}

	#endregion

	#region Sending

	/// <summary>
	/// Enter in the chat: with pictures attached, they upload and the message goes when they are done (with their links at
	/// the end). False (the message goes as usual) when nothing is attached or the line is a command.
	/// </summary>
	public static bool TrySend(string text)
	{
		if (!Active || _pending.Count == 0)
		{
			return false;
		}
		string trimmed = (text ?? "").Trim();
		// The IsaClient chat (.inchat on, .w, .r, .inchat <message>) takes the pictures too.
		Action<string> route = IsaClient.Community.PipeChat.RouteFor(trimmed, out string routedText);
		if (route == null && (trimmed.StartsWith("/") || (trimmed.Length > 0 && (trimmed[0] == IsaClient.Core.ChatCommands.CommandSpecifier || trimmed[0] == '.'))))
		{
			// Commands run; the pictures wait for a real message.
			return false;
		}
		Sending sending = new Sending
		{
			Text = (route != null) ? routedText : trimmed,
			Route = route,
			Attachments = _pending.ToList()
		};
		_pending.Clear();
		_sending.Add(sending);
		Task.Run(delegate
		{
			Upload(sending);
		});
		return true;
	}

	private static void Upload(Sending sending)
	{
		List<string> links = new List<string>();
		List<string> errors = new List<string>();
		int count = sending.Attachments.Count;
		for (int i = 0; i < count; i++)
		{
			Attachment attachment = sending.Attachments[i];
			int number = i;
			if (attachment.ShareTag != null)
			{
				// A schematic still going up to archive.org: the message waits for it, with its progress.
				while (!(attachment.ShareReady?.Invoke() ?? true) && attachment.ShareError?.Invoke() == null)
				{
					sending.Status = "Uploading " + attachment.Name + "...";
					sending.Progress = (number + (attachment.ShareProgress?.Invoke() ?? 0f)) / count;
					Thread.Sleep(100);
				}
				string shareError = attachment.ShareError?.Invoke();
				if (shareError != null)
				{
					errors.Add(attachment.Name + ": " + shareError);
					continue;
				}
				string finalTag = attachment.ShareFinalTag?.Invoke();
				if (finalTag != null)
				{
					attachment.ShareTag = finalTag;
				}
				links.Add(attachment.ShareTag);
				continue;
			}
			while (!attachment.Ready && !attachment.Failed)
			{
				sending.Status = attachment.IsVideo ? "Making a GIF..." : "Preparing...";
				sending.Progress = (number + attachment.Progress * 0.5f) / count;
				Thread.Sleep(100);
			}
			if (attachment.Failed)
			{
				if (attachment.Error != null)
				{
					errors.Add(attachment.Name + ": " + attachment.Error);
				}
				continue;
			}
			if (attachment.Link != null)
			{
				// Already online (a GIF from the picker).
				links.Add(attachment.Link);
				continue;
			}
			try
			{
				sending.Status = "Uploading" + ((count > 1) ? (" " + (number + 1) + " of " + count) : "") + "...";
				ImgurClient.Upload upload = ImgurClient.Send(attachment.Prepared.Data, attachment.Prepared.FileName, attachment.Prepared.ContentType, p => sending.Progress = (number + p) / count, null);
				ChatImages.RememberSent(upload);
				links.Add(upload.Link);
				try
				{
					// The message comes back from the server: the picture shows at once, without downloading it again.
					Directory.CreateDirectory(ChatImages.CacheFolder);
					File.WriteAllBytes(ChatPictures.Get(upload.Link).CacheFile, attachment.Prepared.Data);
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				errors.Add(attachment.Name + ": " + ex.Message);
			}
		}
		Main.QueueMainThreadAction(delegate
		{
			_sending.Remove(sending);
			foreach (Attachment attachment in sending.Attachments)
			{
				attachment.Preview?.Dispose();
			}
			foreach (string error in errors)
			{
				ClientChat.Error("Couldn't send " + error);
			}
			Deliver(sending, links);
			// What was shared starts going to the IsaClient players (after its message, so the cards are there).
			foreach (Attachment attachment in sending.Attachments)
			{
				if (attachment.ShareTag != null && links.Contains(attachment.ShareTag))
				{
					attachment.ShareSent?.Invoke();
				}
			}
		});
	}

	/// <summary>
	/// A message with pictures already online (the action commands: .hug, .pat...): sent like one with attachments, through
	/// the same way (pipe or server chat, IsaClient chat when it applies).
	/// </summary>
	public static void SendNow(string text, List<string> links)
	{
		Deliver(new Sending
		{
			Text = (text ?? "").Trim(),
			Attachments = new List<Attachment>()
		}, links ?? new List<string>());
	}

	/// <summary>On the game thread: the message and its picture links go where the settings say.</summary>
	private static void Deliver(Sending sending, List<string> links)
	{
		{
			if (links.Count == 0 && sending.Text.Length == 0)
			{
				return;
			}
			if (Main.gameMenu || Main.netMode == 2)
			{
				ClientChat.Error("You left the world before the pictures were sent.");
				return;
			}
			if (sending.Route != null)
			{
				// Coded (a few words instead of each link), and split when the codes of one message get long.
				IsaClient.Community.PipeChat.SendWithPictures(sending.Route, sending.Text, PictureCodes.Group(links));
				return;
			}
			// Pictures only to IsaClient players (the pipe): the text goes to the server's chat as usual. Shared
			// characters, styles and schematics go this way while the pipe is on (only IsaClient can open them); with
			// it off they go in the server's chat as codes, like pictures.
			if ((ChatImages.ViaPipe || (links.Any(IsaClient.Sharing.ShareChat.IsShareTag) && IsaClient.Sharing.ShareChat.ViaPipe)) && links.Count > 0)
			{
				// No text: a blank line, so the server still shows your name (its format, your colors) for them to go under.
				Main.SubmitChatLine((sending.Text.Length > 0) ? sending.Text : " ", null);
				IsaClient.Community.PipeChat.SendPictures(links, sending.Text);
				return;
			}
			// In the server's chat: everything in one packet of words at the end of the message (ChatData); what
			// doesn't fit goes in lines of their own that IsaClient hides. Links as they are when short codes are off.
			List<string> coded = links.Where(ChatData.Handles).ToList();
			// Shared characters and schematics also say where their Imgur picture is.
			List<string> parts = (coded.Count > 0) ? ChatData.Encode(coded.Select(ChatData.LinkRecord).Concat(coded.Select(IsaClient.Sharing.ShareChat.FileRecord).Where(r => r != null))) : null;
			if (coded.Count > 0 && parts == null)
			{
				ClientChat.Error("That's too much to send in the chat at once.");
			}
			string plain = string.Join(" ", links.Where(l => !ChatData.Handles(l) && !IsaClient.Sharing.ShareChat.IsShareTag(l)));
			// With secret chat on the message is scrambled whole: the packet goes on its own line then.
			bool together = parts != null && !IsaClient.Community.SecretChat.On;
			string tail = (plain + (together ? (" " + parts[0]) : "")).Trim();
			if (sending.Text.Length > 0 || tail.Length > 0)
			{
				Main.SubmitChatLine(sending.Text, (tail.Length > 0) ? tail : null);
			}
			if (parts != null)
			{
				ChatData.SendAlone(parts.Skip(together ? 1 : 0));
			}
		}
	}

	#endregion

	#region Drawing (with the chat bar)

	private static readonly Color FrameColor = new Color(73, 94, 171);

	private static readonly Color FrameHover = new Color(255, 214, 110);

	/// <summary>Per frame (main thread): previews of the attached pictures.</summary>
	internal static void Update()
	{
		foreach (Attachment attachment in _pending)
		{
			if (attachment.Ready && attachment.Preview == null && !attachment.PreviewLoading && (attachment.Prepared != null || attachment.PreviewLink != null))
			{
				attachment.PreviewLoading = true;
				byte[] data = attachment.Prepared?.Data;
				string previewLink = attachment.PreviewLink;
				int size = (int)Math.Round(ChipSize * Math.Min(2f, Main.UIScale));
				Attachment owner = attachment;
				ChatPictures.Run(delegate
				{
					ImageFrames preview = null;
					try
					{
						// A GIF from the picker: its small version is downloaded.
						if (data == null)
						{
							data = ImgurClient.Download(previewLink, 8 * 1024 * 1024, null);
						}
						// Wide pictures make wide chips (up to 2.5 times as wide as tall).
						preview = ImageFrames.Open(data, size * 5 / 2, size, ChatImages.Animate);
					}
					catch (Exception ex)
					{
						owner.Error = ex.Message;
					}
					Main.QueueMainThreadAction(delegate
					{
						if (owner.Cancelled || !_pending.Contains(owner))
						{
							preview?.Dispose();
							return;
						}
						owner.Preview = preview;
					});
				});
			}
		}
	}

	/// <summary>Main.DrawPlayerChat, after the chat bar: the picture button, the chips and the sending note.</summary>
	public static void Draw(SpriteBatch sb)
	{
		if (Main.drawingPlayerChat && IsaClient.Assistant.PluginMaker.PluginAgent.ChatMode)
		{
			DrawModeChip(sb);
		}
		if (Main.drawingPlayerChat && EmoticonPicker.Button)
		{
			EmoticonPicker.DrawButton(sb, EmoticonButtonRect);
			EmoticonPicker.DrawPanel(sb, EmoticonButtonRect);
		}
		if (!Active)
		{
			return;
		}
		int barTop = Main.screenHeight - 36;
		int inputTop = barTop - IsaClient.Community.ChatBox.InputExtraHeight;
		if (Main.drawingPlayerChat && ShowsButton)
		{
			DrawButton(sb, ButtonRect);
		}
		if (Main.drawingPlayerChat && ShowsGifButton)
		{
			DrawGifButton(sb, GifButtonRect);
		}
		int stripTop = inputTop - ChipSize - 10;
		if (Main.drawingPlayerChat && _pending.Count > 0)
		{
			DrawChips(sb, 80, stripTop);
		}
		if (_sending.Count > 0)
		{
			Sending sending = _sending[0];
			string text = sending.Status + "  " + (int)(sending.Progress * 100f) + "%" + ((_sending.Count > 1) ? ("  (+" + (_sending.Count - 1) + " more)") : "");
			Vector2 size = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(text)) * 0.8f;
			int y = Main.drawingPlayerChat ? (((_pending.Count > 0) ? stripTop : inputTop) - 26) : (barTop + 6);
			Rectangle pill = new Rectangle(80, y, (int)size.X + 40, 22);
			Utils.DrawInvBG(sb, pill, new Color(33, 43, 79) * 0.92f);
			Rectangle bar = new Rectangle(pill.X + 6, pill.Bottom - 5, pill.Width - 12, 2);
			sb.Draw(TextureAssets.MagicPixel.Value, bar, Color.Black * 0.5f);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bar.X, bar.Y, (int)(bar.Width * sending.Progress), bar.Height), FrameHover);
			DrawPictureIcon(sb, new Rectangle(pill.X + 6, pill.Y + 3, 22, 15), hover: false);
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(text), new Vector2(pill.X + 32, pill.Y + 2), Color.White, 0.8f);
		}
	}

	/// <summary>A shared thing's chip: same frame as a picture's, its picture on the left and its name. Returns its width.</summary>
	private static int DrawShareChip(SpriteBatch sb, Attachment attachment, int x, int y, ref Attachment remove)
	{
		string name = attachment.Name ?? "";
		float nameWidth = Math.Min(130f, FontAssets.MouseText.Value.MeasureString(name).X * 0.7f);
		Rectangle chip = new Rectangle(x, y, ChipSize + (int)nameWidth + 14, ChipSize);
		bool hover = chip.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
		string error = attachment.ShareError?.Invoke();
		bool ready = attachment.ShareReady?.Invoke() ?? true;
		Color frame = (error != null) ? new Color(200, 70, 80) : (hover ? FrameHover : FrameColor);
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(chip.X + 3, chip.Y + 3, chip.Width, chip.Height), Color.Black * 0.35f);
		sb.Draw(TextureAssets.MagicPixel.Value, chip, new Color(14, 18, 38));
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(chip.X + 1, chip.Y + 1, chip.Width - 2, chip.Height - 2), frame);
		Rectangle inner = new Rectangle(chip.X + 3, chip.Y + 3, chip.Width - 6, chip.Height - 6);
		sb.Draw(TextureAssets.MagicPixel.Value, inner, new Color(8, 10, 20));
		Rectangle art = new Rectangle(inner.X + 2, inner.Y + 2, inner.Height - 4, inner.Height - 4);
		try
		{
			attachment.DrawShare?.Invoke(sb, art);
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		string shown = name;
		while (shown.Length > 1 && FontAssets.MouseText.Value.MeasureString(shown).X * 0.7f > nameWidth)
		{
			shown = shown.Substring(0, shown.Length - 1);
		}
		Utils.DrawBorderString(sb, (shown == name) ? name : (shown + "..."), new Vector2(art.Right + 6, inner.Y + 8), Color.White, 0.7f);
		if (!ready && error == null)
		{
			Rectangle bar = new Rectangle(art.Right + 6, inner.Bottom - 10, inner.Right - art.Right - 12, 4);
			sb.Draw(TextureAssets.MagicPixel.Value, bar, Color.Black * 0.7f);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bar.X, bar.Y, (int)(bar.Width * (attachment.ShareProgress?.Invoke() ?? 0f)), bar.Height), FrameHover);
		}
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			sb.Draw(TextureAssets.MagicPixel.Value, inner, Color.Black * 0.45f);
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T("X"), new Vector2(inner.Center.X - 6, inner.Center.Y - 12), new Color(255, 110, 110), 1.1f);
			string tip = name + "\n" + ((error != null) ? ("Can't send it: " + error) : (ready ? "Goes with your message" : "Uploading...")) + "\nClick to remove";
			Main.instance.MouseText(IsaClient.Languages.L10n.T(tip), 0, 0);
			if (Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				SoundEngine.PlaySound(12);
				remove = attachment;
			}
		}
		return chip.Width;
	}

	/// <summary>The picture button, at the right end of the chat bar's last line.</summary>
	private static Rectangle ButtonRect
	{
		get
		{
			int barHeight = TextureAssets.TextBack.Height();
			int barRight = 78 + ((Main.screenWidth > 800) ? (Main.screenWidth - 300) : TextureAssets.TextBack.Width());
			return new Rectangle(barRight - 34, Main.screenHeight - 36 + (barHeight - 26) / 2, 28, 26);
		}
	}

	/// <summary>The GIF button: left of the picture button (or where it would be).</summary>
	private static Rectangle GifButtonRect
	{
		get
		{
			Rectangle picture = ButtonRect;
			return ShowsButton ? new Rectangle(picture.X - 34, picture.Y, 30, picture.Height) : new Rectangle(picture.X - 2, picture.Y, 30, picture.Height);
		}
	}

	/// <summary>The emoticons button: left of the GIF and picture buttons (or where they would be).</summary>
	private static Rectangle EmoticonButtonRect
	{
		get
		{
			Rectangle picture = ButtonRect;
			int left = ShowsGifButton ? GifButtonRect.X : (ShowsButton ? picture.X : (picture.Right + 2));
			return new Rectangle(left - 30, picture.Y, 26, picture.Height);
		}
	}

	private static void DrawGifButton(SpriteBatch sb, Rectangle r)
	{
		bool hover = r.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Search GIFs (anime reactions too)\n.gif opens it, .hug .pat .kiss... send one with a message"), 0, 0);
		}
		Color frame = hover ? new Color(255, 225, 120) : new Color(222, 172, 64);
		Rectangle box = new Rectangle(r.X + 1, r.Center.Y - 9, r.Width - 2, 18);
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2), new Color(20, 14, 6) * 0.9f);
		sb.Draw(TextureAssets.MagicPixel.Value, box, frame);
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4), hover ? new Color(150, 90, 210) : new Color(120, 70, 180));
		Utils.DrawBorderString(sb, "GIF", new Vector2(box.Center.X, box.Center.Y + 2), Color.White, hover ? 0.72f : 0.66f, 0.5f, 0.5f);
	}

	/// <summary>Before the chat bar takes the mouse (a click in it moves the caret): a click on the picture button is ours.</summary>
	public static void BeforeChatBar()
	{
		if (Main.drawingPlayerChat && ClickModeChip())
		{
			return;
		}
		if (EmoticonPicker.BeforeChatBar(EmoticonButtonRect))
		{
			return;
		}
		if (Main.drawingPlayerChat && ShowsGifButton && !PlayerInput.IgnoreMouseInterface && GifButtonRect.Contains(Main.mouseX, Main.mouseY) && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			Main.LocalPlayer.mouseInterface = true;
			SoundEngine.PlaySound(12);
			GifPicker.Open();
			return;
		}
		if (!Main.drawingPlayerChat || !ShowsButton || PlayerInput.IgnoreMouseInterface)
		{
			return;
		}
		if (ButtonRect.Contains(Main.mouseX, Main.mouseY) && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			Main.LocalPlayer.mouseInterface = true;
			SoundEngine.PlaySound(12);
			OpenPicker();
		}
	}

	private static void DrawButton(SpriteBatch sb, Rectangle button)
	{
		bool hover = button.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Attach files\nCtrl+V pastes them too"), 0, 0);
		}
		float grow = hover ? 1.12f : 1f;
		int size = (int)(22 * grow);
		DrawClipIcon(sb, new Rectangle(button.Center.X - size / 2, button.Center.Y - size / 2, size, size), hover);
		if (_pending.Count > 0)
		{
			// How many are attached, on the corner.
			string count = _pending.Count.ToString();
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(count), new Vector2(button.Right - 6, button.Y - 4), FrameHover, 0.7f);
		}
	}

	/// <summary>A paperclip (the attach button): its loops drawn as short strokes, tilted, with a dark edge.</summary>
	internal static void DrawClipIcon(SpriteBatch sb, Rectangle r, bool hover)
	{
		List<Vector2> path = new List<Vector2>();
		void Line(float x, float y1, float y2)
		{
			path.Add(new Vector2(x, y1));
			path.Add(new Vector2(x, y2));
		}
		void Arc(float cx, float cy, float radius, float from, float to)
		{
			for (int i = 0; i <= 10; i++)
			{
				float t = Microsoft.Xna.Framework.MathHelper.Lerp(from, to, i / 10f);
				path.Add(new Vector2(cx + (float)Math.Cos(t) * radius, cy + (float)Math.Sin(t) * radius));
			}
		}
		// In units of a 24 tall clip, centered: inner leg, bottom loop, outer leg, top loop, long leg.
		Line(-1.5f, -3.5f, 6f);
		Arc(0.75f, 6f, 2.25f, Microsoft.Xna.Framework.MathHelper.Pi, 0f);
		Line(3f, 6f, -7f);
		Arc(-0.5f, -7f, 3.5f, 0f, -Microsoft.Xna.Framework.MathHelper.Pi);
		Line(-4f, -7f, 8.5f);
		Arc(-0.5f, 8.5f, 3.5f, Microsoft.Xna.Framework.MathHelper.Pi, 0f);
		Line(3f, 8.5f, 7f);
		float unit = r.Height / 24f;
		float tilt = 0.7f;
		Vector2 center = new Vector2(r.Center.X, r.Center.Y);
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		Color edge = new Color(20, 14, 6) * 0.9f;
		Color metal = hover ? new Color(255, 225, 120) : new Color(205, 214, 232);
		for (int pass = 0; pass < 2; pass++)
		{
			float thick = (pass == 0) ? Math.Max(3f, 3.2f * unit) : Math.Max(1.5f, 1.6f * unit);
			for (int i = 1; i < path.Count; i++)
			{
				Vector2 a = center + path[i - 1].RotatedBy(tilt) * unit;
				Vector2 b = center + path[i].RotatedBy(tilt) * unit;
				Vector2 d = b - a;
				float length = d.Length();
				if (length < 0.01f)
				{
					continue;
				}
				// Each stroke a bit longer, so the joints close.
				sb.Draw(pixel, a - d / length * (thick * 0.4f), new Rectangle(0, 0, 1, 1), (pass == 0) ? edge : metal, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(length + thick * 0.8f, thick), SpriteEffects.None, 0f);
			}
		}
	}

	/// <summary>A little framed landscape (sky, sun, hills) drawn with rectangles: the "picture" icon.</summary>
	internal static void DrawPictureIcon(SpriteBatch sb, Rectangle r, bool hover)
	{
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		Color frame = hover ? new Color(255, 225, 120) : new Color(222, 172, 64);
		sb.Draw(pixel, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), new Color(20, 14, 6) * 0.9f);
		sb.Draw(pixel, r, frame);
		Rectangle inner = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
		sb.Draw(pixel, inner, hover ? new Color(140, 195, 255) : new Color(105, 165, 245));
		int sun = Math.Max(2, inner.Height / 4);
		sb.Draw(pixel, new Rectangle(inner.Right - sun - 2, inner.Y + 2, sun, sun), new Color(255, 236, 120));
		// Two hills.
		Color hill = hover ? new Color(110, 215, 110) : new Color(76, 186, 86);
		int baseY = inner.Bottom;
		for (int x = 0; x < inner.Width; x++)
		{
			float t = (float)x / inner.Width;
			float height = Math.Max(0.35f + 0.25f * (float)Math.Cos((t - 0.3f) * 7f), 0.25f + 0.2f * (float)Math.Cos((t - 0.85f) * 9f)) * inner.Height;
			sb.Draw(pixel, new Rectangle(inner.X + x, baseY - (int)height, 1, (int)height), hill);
		}
	}

	private static void DrawChips(SpriteBatch sb, int x, int y)
	{
		Attachment remove = null;
		foreach (Attachment attachment in _pending)
		{
			if (attachment.ShareTag != null)
			{
				x += DrawShareChip(sb, attachment, x, y, ref remove) + 8;
				continue;
			}
			Texture2D preview = attachment.Preview?.Update(ChatImages.Animate);
			// The chip takes the picture's shape: same height for all, the width follows the picture (within limits).
			int width = ChipSize;
			if (preview != null && preview.Height > 0)
			{
				width = 6 + (int)Math.Round((ChipSize - 6) * (float)preview.Width / preview.Height);
				width = Math.Max(ChipSize / 2, Math.Min(ChipSize * 5 / 2, width));
			}
			Rectangle chip = new Rectangle(x, y, width, ChipSize);
			bool hover = chip.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
			Color frame = attachment.Failed ? new Color(200, 70, 80) : (hover ? FrameHover : FrameColor);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(chip.X + 3, chip.Y + 3, chip.Width, chip.Height), Color.Black * 0.35f);
			sb.Draw(TextureAssets.MagicPixel.Value, chip, new Color(14, 18, 38));
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(chip.X + 1, chip.Y + 1, chip.Width - 2, chip.Height - 2), frame);
			Rectangle inner = new Rectangle(chip.X + 3, chip.Y + 3, chip.Width - 6, chip.Height - 6);
			sb.Draw(TextureAssets.MagicPixel.Value, inner, new Color(8, 10, 20));
			if (preview != null)
			{
				sb.Draw(preview, ChatImageLayout.Fit(preview.Width, preview.Height, inner), Color.White);
			}
			if (!attachment.Ready && !attachment.Failed)
			{
				// Making the GIF (or reading the file): a bar that fills.
				Rectangle bar = new Rectangle(inner.X + 4, inner.Bottom - 10, inner.Width - 8, 4);
				sb.Draw(TextureAssets.MagicPixel.Value, bar, Color.Black * 0.7f);
				sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(bar.X, bar.Y, (int)(bar.Width * attachment.Progress), bar.Height), FrameHover);
				Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(attachment.IsVideo ? "GIF..." : "..."), new Vector2(inner.X + 5, inner.Y + 4), Color.White, 0.7f);
			}
			else if (attachment.Failed)
			{
				Utils.DrawBorderString(sb, "!", new Vector2(inner.Center.X - 4, inner.Center.Y - 12), new Color(255, 120, 120), 1.2f);
			}
			else if (attachment.IsVideo || attachment.Link != null || attachment.Prepared?.ContentType == "image/gif")
			{
				Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T("GIF"), new Vector2(inner.X + 3, inner.Bottom - 16), Color.White, 0.6f);
			}
			if (hover)
			{
				Main.LocalPlayer.mouseInterface = true;
				// A red cross: clicking the chip takes it out.
				sb.Draw(TextureAssets.MagicPixel.Value, inner, Color.Black * 0.45f);
				Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T("X"), new Vector2(inner.Center.X - 6, inner.Center.Y - 12), new Color(255, 110, 110), 1.1f);
				string tip = attachment.Name + "\n" + (attachment.Failed ? ("Can't send it: " + attachment.Error) : (attachment.Ready ? (attachment.Status + (attachment.IsVideo ? " GIF" : "")) : attachment.Status)) + "\nClick to remove";
				Main.instance.MouseText(IsaClient.Languages.L10n.T(tip), 0, 0);
				if (Main.mouseLeft && Main.mouseLeftRelease)
				{
					Main.mouseLeftRelease = false;
					SoundEngine.PlaySound(12);
					remove = attachment;
				}
			}
			x += chip.Width + 8;
		}
		if (remove != null)
		{
			Remove(remove);
		}
		// A "+" chip adds more while there is room.
		if (_pending.Count < ChatImages.MaxPerMessage)
		{
			Rectangle add = new Rectangle(x, y + ChipSize / 2 - 16, 32, 32);
			bool hover = add.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
			Utils.DrawInvBG(sb, add, (hover ? new Color(73, 94, 171) : new Color(33, 43, 79)) * 0.9f);
			Utils.DrawBorderString(sb, "+", new Vector2(add.X + 10, add.Y + 3), hover ? FrameHover : Color.White, 1.1f);
			if (hover)
			{
				Main.LocalPlayer.mouseInterface = true;
				Main.instance.MouseText(IsaClient.Languages.L10n.T("Add more pictures"), 0, 0);
				if (Main.mouseLeft && Main.mouseLeftRelease)
				{
					Main.mouseLeftRelease = false;
					SoundEngine.PlaySound(12);
					OpenPicker();
				}
			}
		}
	}

	private static string Size(long bytes)
	{
		return (bytes >= 1048576L) ? ((bytes / 1048576.0).ToString("0.0") + " MB") : ((bytes / 1024L) + " KB");
	}

	#endregion

	#region .sendpic

	/// <summary>.sendpic [file | paste | clear]</summary>
	internal static void SendPicCommand(CommandContext command)
	{
		string rest = (command.Rest ?? "").Trim().Trim('"');
		if (!ChatImages.Enabled)
		{
			ClientChat.Error("Chat pictures are off (turn them on in the panel: Multiplayer > Chat Pictures).");
			return;
		}
		switch (rest.ToLowerInvariant())
		{
		case "":
			OpenPicker();
			return;
		case "paste":
			if (!TryPaste())
			{
				ClientChat.Error("There is no picture in the clipboard.");
			}
			else
			{
				OpenChatLater();
			}
			return;
		case "clear":
			Clear();
			ClientChat.Info("Attached pictures removed.");
			return;
		case "folder":
			Directory.CreateDirectory(ChatImages.SavedFolder);
			Utils.OpenFolder(ChatImages.SavedFolder);
			return;
		}
		if (AddFile(rest))
		{
			OpenChatLater();
		}
	}

	/// <summary>The chat closes after a command runs: it opens again on the next frame, showing what is attached.</summary>
	private static void OpenChatLater()
	{
		Main.QueueMainThreadAction(delegate
		{
			if (!Main.gameMenu && !Main.drawingPlayerChat)
			{
				Main.OpenPlayerChat();
			}
		});
	}

	#endregion
}
