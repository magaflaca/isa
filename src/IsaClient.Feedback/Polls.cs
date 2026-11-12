using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;

namespace IsaClient.Feedback;

/// <summary>
/// A question from the developers on the title screen (after the update popup): the open poll of the update server
/// (<see cref="Updater.ServerUrl"/>/api/poll), in the game's language. Pick an option or write an answer, and it goes to
/// the developers' Discord through the server (the client never knows where). Each poll is asked once: answered or
/// "Don't ask again" are remembered (IsaClient/Feedback/Polls.json); "Later" asks again the next time the game opens.
/// </summary>
public static class Polls
{
	private sealed class Poll
	{
		[JsonProperty("id")]
		public string Id;

		[JsonProperty("kind")]
		public string Kind;

		[JsonProperty("multi")]
		public bool Multi;

		[JsonProperty("other")]
		public bool Other;

		[JsonProperty("question")]
		public Dictionary<string, string> Question;

		[JsonProperty("options")]
		public List<Dictionary<string, string>> Options;
	}

	private sealed class Seen
	{
		[JsonProperty("done")]
		public List<string> Done = new List<string>();
	}

	private enum State
	{
		None,
		Asking,
		Sending,
		Thanks,
		Failed
	}

	public const int TextLimit = 300;

	private static State _state;

	private static Poll _poll;

	private static bool _started;

	private static readonly HashSet<int> _picked = new HashSet<int>();

	private static string _text = "";

	private static bool _typing;

	private static string _error;

	private static DateTime _thanksUntil;

	private static bool _left;

	private static bool _leftRelease;

	private static string _hovered;

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "Feedback", "Polls.json");

	private static bool IsText => _poll?.Kind == "text";

	/// <summary>On the title screen, once the update popup is gone.</summary>
	public static bool IsModal => _state != State.None && Main.gameMenu && Main.menuMode == 0 && !Updater.IsModal;

	/// <summary>Start of Main.DrawMenu, right after the updater's: asks the server once, and keeps the clicks for the popup.</summary>
	public static void BeginMenuFrame()
	{
		if (!_started && Main._isAsyncLoadComplete)
		{
			_started = true;
			Check();
		}
		if (_state == State.Thanks && DateTime.UtcNow >= _thanksUntil)
		{
			_state = State.None;
		}
		if (IsModal)
		{
			_left = Main.mouseLeft;
			_leftRelease = Main.mouseLeftRelease;
			Main.mouseLeft = false;
			Main.mouseLeftRelease = false;
			Main.mouseRight = false;
			Main.mouseRightRelease = false;
		}
	}

	private static void Check()
	{
		Task.Run(delegate
		{
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				using WebClient web = Client();
				Poll poll = JsonConvert.DeserializeObject<Poll>(web.DownloadString(Updater.ServerUrl + "/api/poll"));
				if (poll?.Id == null || poll.Question == null || (poll.Kind != "text" && (poll.Options == null || poll.Options.Count < 2)))
				{
					return;
				}
				if (LoadSeen().Done.Contains(poll.Id))
				{
					return;
				}
				Main.QueueMainThreadAction(delegate
				{
					_poll = poll;
					_picked.Clear();
					_text = "";
					_typing = IsText;
					_state = State.Asking;
					SoundEngine.PlaySound(10);
				});
			}
			catch (Exception ex)
			{
				// 404 = no poll open; no connection = nothing to ask.
				Console.WriteLine("[IsaPolls] " + ex.Message);
			}
		});
	}

	private static WebClient Client()
	{
		WebClient web = new WebClient();
		web.Encoding = Encoding.UTF8;
		web.Headers[HttpRequestHeader.UserAgent] = ClientInfo.Name + "/" + ClientInfo.FullVersion;
		web.Headers[HttpRequestHeader.CacheControl] = "no-cache";
		return web;
	}

	private static Seen LoadSeen()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonConvert.DeserializeObject<Seen>(File.ReadAllText(FilePath)) ?? new Seen();
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		return new Seen();
	}

	private static void Remember(string id)
	{
		try
		{
			Seen seen = LoadSeen();
			if (!seen.Done.Contains(id))
			{
				seen.Done.Add(id);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			File.WriteAllText(FilePath, JsonConvert.SerializeObject(seen, Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>The text in the game's language: exact code, then the base language (pt-BR -> pt), then English.</summary>
	private static string Pick(Dictionary<string, string> texts)
	{
		if (texts == null)
		{
			return "";
		}
		string lang = IsaClient.Languages.L10n.Current ?? "en";
		if (texts.TryGetValue(lang, out string own) && !string.IsNullOrWhiteSpace(own))
		{
			return own;
		}
		string bare = lang.Split('-')[0];
		KeyValuePair<string, string> near = texts.FirstOrDefault(t => t.Key.Split('-')[0].Equals(bare, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.Value));
		if (near.Value != null)
		{
			return near.Value;
		}
		return texts.TryGetValue("en", out string en) ? en : texts.Values.FirstOrDefault() ?? "";
	}

	private static bool CanSend => IsText ? !string.IsNullOrWhiteSpace(_text) : (_picked.Count > 0 || (_poll.Other && !string.IsNullOrWhiteSpace(_text)));

	private static void Send()
	{
		Poll poll = _poll;
		var body = new
		{
			id = poll.Id,
			install = FeedbackReports.InstallTag,
			lang = IsaClient.Languages.L10n.Current ?? "en",
			version = ClientInfo.FullVersion,
			choices = _picked.OrderBy(i => i).ToArray(),
			text = (IsText || poll.Other) ? _text.Trim() : ""
		};
		_state = State.Sending;
		_typing = false;
		Task.Run(delegate
		{
			string error = null;
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				using WebClient web = Client();
				web.Headers[HttpRequestHeader.ContentType] = "application/json";
				web.UploadString(Updater.ServerUrl + "/api/poll/answer", "POST", JsonConvert.SerializeObject(body));
			}
			catch (WebException ex) when (ex.Response is HttpWebResponse r && ((int)r.StatusCode == 409 || (int)r.StatusCode == 410))
			{
				// already answered from this install, or the poll closed meanwhile: nothing more to do
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			Main.QueueMainThreadAction(delegate
			{
				if (error == null)
				{
					Remember(poll.Id);
					_state = State.Thanks;
					_thanksUntil = DateTime.UtcNow.AddSeconds(2.5);
					SoundEngine.PlaySound(10);
				}
				else
				{
					_error = error;
					_state = State.Failed;
				}
			});
		});
	}

	private static void Close(bool forever)
	{
		if (forever && _poll != null)
		{
			Remember(_poll.Id);
		}
		_typing = false;
		_state = State.None;
		SoundEngine.PlaySound(11);
	}

	#region Popup

	private static readonly Color Gold = new Color(255, 231, 69);

	private static readonly Color Soft = new Color(200, 205, 230);

	public static void Draw(SpriteBatch sb)
	{
		if (!IsModal || _poll == null)
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		int sw = Main.screenWidth;
		int sh = Main.screenHeight;
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, sw + 2, sh + 2), Color.Black * 0.55f);
		int width = Math.Min(600, sw - 32);
		string[] question = Utils.WordwrapString(Pick(_poll.Question), font, width - 60, 5, out int qLines);
		qLines = Math.Min(qLines + 1, 5);
		int options = IsText ? 0 : _poll.Options.Count;
		bool box = IsText || _poll.Other;
		int height = 120 + qLines * 26 + options * 40 + (box ? 54 : 0) + 70;
		if (_state == State.Failed)
		{
			height += 26;
		}
		Rectangle panel = new Rectangle((sw - width) / 2, Math.Max(8, (sh - height) / 2), width, height);
		Utils.DrawInvBG(sb, panel, new Color(33, 43, 79) * 0.96f);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T((_state == State.Thanks) ? "Thank you!" : "Quick question"), new Vector2(panel.Center.X, panel.Y + 30), Gold, 0.55f, 0.5f, 0.5f);
		float y = panel.Y + 62;
		Centered(sb, font, IsaClient.Languages.L10n.T("The IsaClient team would like to know"), panel.Center.X, y, Soft, 0.8f);
		y += 30f;
		for (int i = 0; i < qLines; i++)
		{
			Centered(sb, font, question[i] ?? "", panel.Center.X, y, Color.White, 1f);
			y += 26f;
		}
		y += 8f;
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		string hovered = null;
		bool asking = _state == State.Asking || _state == State.Failed;
		if (!IsText)
		{
			Centered(sb, font, IsaClient.Languages.L10n.T(_poll.Multi ? "Pick one or more:" : "Pick one:"), panel.Center.X, y - 4, Soft * 0.8f, 0.75f);
			y += 16f;
			for (int i = 0; i < options; i++)
			{
				Rectangle row = new Rectangle(panel.X + 30, (int)y, panel.Width - 60, 34);
				bool on = _picked.Contains(i);
				bool hover = asking && row.Contains(mouse);
				Utils.DrawInvBG(sb, row, on ? new Color(73, 94, 171) : (hover ? new Color(63, 82, 151) : new Color(45, 58, 110) * 0.9f));
				Rectangle tick = new Rectangle(row.X + 8, row.Y + 7, 20, 20);
				Utils.DrawInvBG(sb, tick, new Color(15, 20, 45) * 0.95f);
				if (on)
				{
					sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(tick.X + 5, tick.Y + 5, 10, 10), Gold);
				}
				string text = Pick(_poll.Options[i]);
				while (text.Length > 4 && font.MeasureString(text).X * 0.9f > row.Width - 50)
				{
					text = text.Substring(0, text.Length - 4) + "...";
				}
				Shadowed(sb, font, text, new Vector2(row.X + 38, row.Y + 8), (on || hover) ? Gold : Color.White, 0.9f);
				if (hover)
				{
					hovered = "option" + i;
					if (Clicked())
					{
						if (on)
						{
							_picked.Remove(i);
						}
						else
						{
							if (!_poll.Multi)
							{
								_picked.Clear();
							}
							_picked.Add(i);
						}
						SoundEngine.PlaySound(12);
					}
				}
				y += 40f;
			}
		}
		if (box)
		{
			y += 4f;
			Rectangle field = new Rectangle(panel.X + 30, (int)y, panel.Width - 60, 40);
			DrawField(sb, font, field, mouse, asking, ref hovered);
			y += 54f;
		}
		if (_state == State.Failed)
		{
			Centered(sb, font, IsaClient.Languages.L10n.T("Could not send it:") + " " + (_error ?? ""), panel.Center.X, y, new Color(255, 160, 160), 0.8f);
		}
		int by = panel.Bottom - 58;
		switch (_state)
		{
		case State.Asking:
		case State.Failed:
		{
			Rectangle send = new Rectangle(panel.Center.X - 250, by, 160, 40);
			Rectangle later = new Rectangle(panel.Center.X - 80, by, 160, 40);
			Rectangle never = new Rectangle(panel.Center.X + 90, by, 160, 40);
			if (Button(sb, font, send, (_state == State.Failed) ? "Try again" : "Send", mouse, CanSend, ref hovered))
			{
				SoundEngine.PlaySound(10);
				Send();
			}
			if (Button(sb, font, later, "Later", mouse, true, ref hovered))
			{
				Close(forever: false);
			}
			if (Button(sb, font, never, "Don't ask again", mouse, true, ref hovered))
			{
				Close(forever: true);
			}
			break;
		}
		case State.Sending:
			Centered(sb, font, IsaClient.Languages.L10n.T("Sending..."), panel.Center.X, by + 10, Gold, 1f);
			break;
		case State.Thanks:
			Centered(sb, font, IsaClient.Languages.L10n.T("Your answer was sent."), panel.Center.X, by + 10, new Color(150, 230, 170), 1f);
			if (Clicked())
			{
				_state = State.None;
			}
			break;
		}
		if (hovered != null && hovered != _hovered)
		{
			SoundEngine.PlaySound(12);
		}
		_hovered = hovered;
	}

	private static void DrawField(SpriteBatch sb, DynamicSpriteFont font, Rectangle field, Point mouse, bool asking, ref string hovered)
	{
		bool hover = asking && field.Contains(mouse);
		if (hover)
		{
			hovered = "field";
			if (Clicked())
			{
				_typing = true;
				Main.clrInput();
			}
		}
		else if (asking && _left && _leftRelease)
		{
			// a click anywhere else stops typing (the buttons still get it)
			_typing = false;
		}
		Utils.DrawInvBG(sb, field, _typing ? new Color(40, 55, 110) : new Color(15, 20, 45) * 0.95f);
		if (_typing && asking)
		{
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
			Main.instance.HandleIME();
			string typed = Main.GetInputText(_text ?? "");
			_text = (typed.Length > TextLimit) ? typed.Substring(0, TextLimit) : typed;
			if (Main.inputTextEscape)
			{
				Main.inputTextEscape = false;
				_typing = false;
			}
			if (Main.inputTextEnter)
			{
				Main.inputTextEnter = false;
				_typing = false;
			}
		}
		string placeholder = IsaClient.Languages.L10n.T(IsText ? "Write your answer here" : "Something else? Write it here");
		string shown = string.IsNullOrEmpty(_text) ? placeholder : _text;
		Color color = string.IsNullOrEmpty(_text) ? new Color(120, 130, 160) : Color.White;
		float room = field.Width - 24;
		while (shown.Length > 0 && font.MeasureString(shown + "|").X * 0.9f > room)
		{
			shown = shown.Substring(1);
		}
		if (_typing && asking && Main.GameUpdateCount / 30 % 2 == 0)
		{
			shown = (string.IsNullOrEmpty(_text) ? "" : shown) + "|";
			color = Color.White;
		}
		sb.DrawString(font, shown, new Vector2(field.X + 12, field.Y + 10), color, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0f);
		if (_text.Length > TextLimit * 3 / 4)
		{
			string count = _text.Length + "/" + TextLimit;
			sb.DrawString(font, count, new Vector2(field.Right - 8 - font.MeasureString(count).X * 0.7f, field.Bottom + 2), Soft * 0.7f, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
		}
	}

	private static bool Clicked()
	{
		if (_left && _leftRelease)
		{
			_leftRelease = false;
			return true;
		}
		return false;
	}

	private static bool Button(SpriteBatch sb, DynamicSpriteFont font, Rectangle box, string text, Point mouse, bool enabled, ref string hovered)
	{
		bool hover = enabled && box.Contains(mouse);
		Utils.DrawInvBG(sb, box, !enabled ? (new Color(45, 50, 80) * 0.7f) : (hover ? new Color(73, 94, 171) : (new Color(63, 82, 151) * 0.8f)));
		string label = IsaClient.Languages.L10n.T(text);
		float scale = Math.Min(1f, (box.Width - 12) / Math.Max(1f, font.MeasureString(label).X));
		Centered(sb, font, label, box.Center.X, box.Y + 9 + (1f - scale) * 8f, !enabled ? (Color.White * 0.4f) : (hover ? Gold : Color.White), scale);
		if (!hover)
		{
			return false;
		}
		hovered = text;
		return Clicked();
	}

	/// <summary>Text as it is (poll texts come already in the player's language, and are not chat tags).</summary>
	private static void Centered(SpriteBatch sb, DynamicSpriteFont font, string text, float centerX, float y, Color color, float scale)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		Vector2 size = font.MeasureString(text) * scale;
		Shadowed(sb, font, text, new Vector2(centerX - size.X / 2f, y), color, scale);
	}

	private static void Shadowed(SpriteBatch sb, DynamicSpriteFont font, string text, Vector2 at, Color color, float scale)
	{
		for (int i = 0; i < 4; i++)
		{
			Vector2 offset = new Vector2((i % 2 == 0) ? -1.5f : 1.5f, (i < 2) ? -1.5f : 1.5f);
			sb.DrawString(font, text, at + offset, Color.Black * 0.8f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		}
		sb.DrawString(font, text, at, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}

	#endregion
}
