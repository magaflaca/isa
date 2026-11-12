using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Community;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Translator page: turn translated chat on, pick the language you write in and the one your messages go out in
/// (flags), swap them and try a sentence.
/// </summary>
internal static class HubTranslate
{
	private const int TileWidth = 158;

	private const int TileHeight = 36;

	private static string _sample = "";

	private static string _result;

	private static bool _resultIsError;

	private static bool _busy;

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>"On: Spanish to English" for cards and search.</summary>
	public static string Summary()
	{
		return (ChatTranslate.Enabled ? "On: " : "Off: ") + ChatTranslate.Describe();
	}

	/// <summary>"On: in Spanish (players)" for the incoming card.</summary>
	public static string IncomingSummary()
	{
		return (IncomingTranslation.Enabled ? "On: " : "Off: ") + "in " + IncomingTranslation.To.Name + " (" + IncomingTranslation.Filters() + ")";
	}

	#region My messages

	public static int DrawOutgoing(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Header(sb, "Translate what I write", "What you type is translated before it is sent. Server commands (/), client commands (" + ChatCommands.CommandSpecifier + "), links and anything in brackets like [i:58] or [c/FF66FF:text] go out exactly as you wrote them.", x, y, width);

		bool canSwap = ChatTranslate.From != ChatLanguages.Auto;
		bool swap = Status(sb, "tr:out", x, y, width, ChatTranslate.Enabled, "your messages", "messages go as typed", ChatTranslate.From, ChatTranslate.To, canSwap ? "Swap the two languages" : "Pick the language you write in to swap", canSwap, delegate
		{
			ChatTranslate.Enabled = !ChatTranslate.Enabled;
		});
		if (swap)
		{
			ChatLanguage from = ChatTranslate.From;
			ChatTranslate.From = ChatTranslate.To;
			ChatTranslate.To = from;
			ClientSettings.Save();
		}
		y += StatusHeight + 14;

		y += Label(sb, "I write in", null, x, y);
		y += Grid(sb, "tr:from", new[] { ChatLanguages.Auto }.Concat(ChatLanguages.All), ChatTranslate.From, x, y, width, PickFrom) + 12;
		y += Label(sb, "Send my messages in", null, x, y);
		y += Grid(sb, "tr:to", ChatLanguages.All, ChatTranslate.To, x, y, width, PickTo) + 14;

		y += DrawTry(sb, x, y, width) + 10;
		y += Footer(sb, "Free translations by MyMemory. Every install has its own daily quota (" + TranslatorAccounts.Email + "); when it runs out another one is used. If a translation fails, the message is sent as you typed it. Command: .translate <from> <to>", x, y, width);
		return y - start;
	}

	#endregion

	#region Incoming messages

	public static int DrawIncoming(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Header(sb, "Translate the chat", "Messages that arrive are shown in your language and look just as they came: names, items and colors stay. They wait a moment so they appear already translated.", x, y, width);

		Status(sb, "tr:in", x, y, width, IncomingTranslation.Enabled, "the chat", "messages show as they come", ChatLanguages.Auto, IncomingTranslation.To, null, false, delegate
		{
			IncomingTranslation.Enabled = !IncomingTranslation.Enabled;
		});
		y += StatusHeight + 14;

		y += Label(sb, "Show messages in", null, x, y);
		y += Grid(sb, "tr:in", new[] { ChatLanguages.Game }.Concat(ChatLanguages.All), IncomingTranslation.Choice, x, y, width, delegate(ChatLanguage language)
		{
			IncomingTranslation.Choice = language;
			ClientSettings.Save();
		}) + 14;

		y += Label(sb, "What to translate", "messages already in your language stay as they are", x, y);
		y += Options(sb, x, y, width, new[]
		{
			new Option("tr:f:players", I.Megaphone, "Players", "What other players write (also the \"Name: text\" chat of TShock servers).", () => IncomingTranslation.Players, v => IncomingTranslation.Players = v),
			new Option("tr:f:mine", I.Sign, "My Messages", "What you write, as it comes back from the server.", () => IncomingTranslation.Mine, v => IncomingTranslation.Mine = v),
			new Option("tr:f:server", I.Book, "Server Messages", "Joins, events, command replies and other server text.", () => IncomingTranslation.Server, v => IncomingTranslation.Server = v),
			new Option("tr:f:client", I.Cog, "Client Messages", "IsaClient's own replies and notes.", () => IncomingTranslation.Client, v => IncomingTranslation.Client = v)
		}) + 14;

		y += Label(sb, "How it works", null, x, y);
		y += Options(sb, x, y, width, new[]
		{
			new Option("tr:o:wait", I.Stopwatch, "Wait for the Translation", "Up to 2.5 seconds, so a message never changes on screen. Off: it shows at once and then changes.", () => IncomingTranslation.Wait, v => IncomingTranslation.Wait = v),
			new Option("tr:o:hover", I.Binoculars, "Original on Hover", "With the chat open, hovering a translated message shows what it said.", () => IncomingTranslation.OriginalOnHover, v => IncomingTranslation.OriginalOnHover = v),
			new Option("tr:o:ctrl", I.Sign, "Ctrl + Right Click", "Translates one message (even with the chat translation off), or shows its original again.", () => IncomingTranslation.CtrlRightClick, v => IncomingTranslation.CtrlRightClick = v)
		}) + 10;

		y += Footer(sb, "Right click copies a message as it came; Ctrl + right click translates it. Command: .translate in <language> (and players, server, client or mine to pick what is translated).", x, y, width);
		return y - start;
	}

	#endregion

	#region Pieces

	private const int StatusHeight = 64;

	private static int Header(SpriteBatch sb, string title, string text, int x, int y, int width)
	{
		HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, title, x + 30, y + 4, Theme.Accent, 0.92f);
		string[] intro = HubKit.Wrap(text, width - 34, 0.72f, 3);
		for (int i = 0; i < intro.Length; i++)
		{
			HubKit.Text(sb, intro[i], x + 30, y + 28 + i * 18, Theme.Muted, 0.72f);
		}
		return 36 + intro.Length * 18 + 6;
	}

	private static int Label(SpriteBatch sb, string text, string note, int x, int y)
	{
		HubKit.Text(sb, text, x, y, Theme.Text, 0.82f);
		if (note != null)
		{
			HubKit.Text(sb, "(" + note + ")", x + HubKit.Measure(text, 0.82f) + 8, y + 3, Theme.Muted, 0.66f);
		}
		return 24;
	}

	private static int Footer(SpriteBatch sb, string text, int x, int y, int width)
	{
		HubKit.Line(sb, x, y, width, Theme.Muted * 0.25f);
		y += 10;
		string[] lines = HubKit.Wrap(text, width, 0.66f, 4);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.Text(sb, lines[i], x, y + i * 16, Theme.Muted * 0.9f, 0.66f);
		}
		return 10 + lines.Length * 16 + 6;
	}

	/// <summary>
	/// The big card: on/off switch, "flag Spanish to flag English" and, with a <paramref name="swapTooltip"/>, a Swap
	/// button (true when clicked). The switch calls <paramref name="toggle"/> and saves.
	/// </summary>
	private static bool Status(SpriteBatch sb, string id, int x, int y, int width, bool on, string onText, string offText, ChatLanguage from, ChatLanguage to, string swapTooltip, bool canSwap, Action toggle)
	{
		Rectangle card = new Rectangle(x, y, width, StatusHeight);
		HubKit.Box(sb, card, Color.Lerp(Theme.Card, Theme.Accent, on ? 0.12f : 0f) * HubPrefs.Opacity);

		Rectangle area = new Rectangle(x + 12, y + 12, 200, 40);
		bool hover = HubInput.In(area);
		if (hover && HubInput.Hover(id + ":toggle", on ? "Turn it off" : "Turn it on"))
		{
			toggle();
			ClientSettings.Save();
			HubInput.Sound(12);
		}
		HubKit.Switch(sb, id + ":toggle", new Rectangle(area.X, area.Y + 8, 48, 24), on, hover);
		HubKit.Text(sb, on ? "Translating" : "Off", area.X + 58, area.Y + 4, on ? Theme.On : Theme.Muted, 0.86f);
		HubKit.Text(sb, on ? onText : offText, area.X + 58, area.Y + 22, Theme.Muted, 0.66f);

		bool swapped = false;
		int right = card.Right - 10;
		if (swapTooltip != null)
		{
			int swapW = 84;
			Rectangle swap = new Rectangle(card.Right - swapW - 12, y + 16, swapW, 32);
			if (HubKit.Button(sb, id + ":swap", swap, "Swap", Theme.Slot, swapTooltip, canSwap) && canSwap)
			{
				swapped = true;
				HubInput.Sound(12);
			}
			right = swap.X - 10;
		}

		// From -> To, centered in the space left.
		int left = area.Right + 10;
		if (right - left > 120)
		{
			string fromName = (from == ChatLanguages.Auto) ? "Any language" : from.Name;
			float fromW = HubKit.Measure(fromName, 0.84f);
			float toW = HubKit.Measure(to.Name, 0.84f);
			float arrowW = HubKit.Measure("to", 0.7f);
			float total = 36 + fromW + 14 + arrowW + 14 + 36 + toW;
			int cy = y + StatusHeight / 2;
			if (total <= right - left)
			{
				int cx = left + (int)(((right - left) - total) / 2f);
				HubFlags.Draw(sb, from.Code, new Rectangle(cx, cy - 10, 30, 20));
				HubKit.Text(sb, fromName, cx + 36, cy - 9, Theme.Text, 0.84f);
				cx += 36 + (int)fromW + 14;
				HubKit.Text(sb, "to", cx, cy - 7, Theme.Accent, 0.7f);
				cx += (int)arrowW + 14;
				HubFlags.Draw(sb, to.Code, new Rectangle(cx, cy - 10, 30, 20));
				HubKit.Text(sb, to.Name, cx + 36, cy - 9, Theme.Text, 0.84f);
			}
			else
			{
				// Narrow panel: just the flags.
				int cx = left + (int)Math.Max(0f, ((right - left) - (74 + arrowW)) / 2f);
				HubFlags.Draw(sb, from.Code, new Rectangle(cx, cy - 10, 30, 20));
				HubKit.Text(sb, "to", cx + 40, cy - 7, Theme.Accent, 0.7f);
				HubFlags.Draw(sb, to.Code, new Rectangle(cx + 44 + (int)arrowW, cy - 10, 30, 20));
			}
		}
		return swapped;
	}

	/// <summary>An on/off option tile.</summary>
	private sealed class Option
	{
		public readonly string Id;

		public readonly int Icon;

		public readonly string Title;

		public readonly string Text;

		public readonly Func<bool> Get;

		public readonly Action<bool> Set;

		public Option(string id, int icon, string title, string text, Func<bool> get, Action<bool> set)
		{
			Id = id;
			Icon = icon;
			Title = title;
			Text = text;
			Get = get;
			Set = set;
		}
	}

	/// <summary>Option tiles in two columns (one when narrow): icon, title, a line or two of text and a switch.</summary>
	private static int Options(SpriteBatch sb, int x, int y, int width, Option[] options)
	{
		const int height = 58;
		int columns = (width >= 560) ? 2 : 1;
		int tileW = (width - (columns - 1) * 8) / columns;
		for (int i = 0; i < options.Length; i++)
		{
			Option option = options[i];
			Rectangle r = new Rectangle(x + (i % columns) * (tileW + 8), y + (i / columns) * (height + 8), tileW, height);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			bool on = option.Get();
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(option.Id, null))
			{
				on = !on;
				option.Set(on);
				ClientSettings.Save();
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim(option.Id, hover);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, glow) * HubPrefs.Opacity);
			HubIcon.Item(option.Icon).Draw(sb, new Rectangle(r.X + 10, r.Y + 17, 24, 24), Color.White * (on ? 1f : 0.55f));
			int textW = r.Width - 44 - 66;
			HubKit.Text(sb, option.Title, r.X + 44, r.Y + 9, on ? Theme.Text : Theme.Muted, 0.8f, textW);
			string[] lines = HubKit.Wrap(option.Text, textW, 0.62f, 2);
			for (int k = 0; k < lines.Length; k++)
			{
				HubKit.Text(sb, lines[k], r.X + 44, r.Y + 27 + k * 14, Theme.Muted, 0.62f);
			}
			HubKit.Switch(sb, option.Id + ":sw", new Rectangle(r.Right - 58, r.Y + (height - 24) / 2, 46, 24), on, hover);
		}
		int rows = (options.Length + columns - 1) / columns;
		return rows * height + Math.Max(0, rows - 1) * 8;
	}

	/// <summary>ChatLanguage tiles (flag + name) in rows; returns the height used.</summary>
	private static int Grid(SpriteBatch sb, string id, IEnumerable<ChatLanguage> languages, ChatLanguage selected, int x, int y, int width, Action<ChatLanguage> pick)
	{
		int columns = Math.Max(1, (width + 8) / (TileWidth + 8));
		int tileW = (width - (columns - 1) * 8) / columns;
		int index = 0;
		foreach (ChatLanguage language in languages)
		{
			Rectangle r = new Rectangle(x + (index % columns) * (tileW + 8), y + (index / columns) * (TileHeight + 8), tileW, TileHeight);
			index++;
			if (!HubInput.Visible(r))
			{
				continue;
			}
			bool active = language == selected;
			bool hover = HubInput.In(r);
			string tooltip = (language == ChatLanguages.Auto) ? "Detect the language of each message" : ((language == ChatLanguages.Game) ? "Follow the game's language (Settings > Language)" : language.Label + " (" + language.Code + ")");
			if (hover && HubInput.Hover(id + ":" + language.Code, tooltip) && !active)
			{
				pick(language);
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim(id + ":" + language.Code, hover || active);
			Color back = active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.35f) : Color.Lerp(Theme.Card, Theme.CardHover, glow);
			HubKit.Box(sb, r, back * HubPrefs.Opacity);
			if (active)
			{
				HubKit.Fill(sb, new Rectangle(r.X + 6, r.Bottom - 4, r.Width - 12, 2), Theme.Accent);
			}
			HubFlags.Draw(sb, language.Code, new Rectangle(r.X + 9, r.Y + 8, 30, 20), (active || hover) ? 1f : 0.85f);
			string name = (language == ChatLanguages.Auto) ? "Detect" : ((language == ChatLanguages.Game) ? "Game" : language.Name);
			HubKit.Text(sb, name, r.X + 47, r.Y + 6, active ? Color.White : Theme.Text, 0.78f, r.Width - 54);
			string small = (language == ChatLanguages.Auto) ? "auto" : (language == ChatLanguages.Game) ? ("now " + ChatLanguages.GameLanguage.Name) : ((language.Native != null && language.Native != language.Name) ? language.Native + " (" + language.Code + ")" : language.Code);
			HubKit.Text(sb, small, r.X + 47, r.Y + 21, Theme.Muted, 0.6f, r.Width - 54);
		}
		int rows = (index + columns - 1) / columns;
		return rows * TileHeight + Math.Max(0, rows - 1) * 8;
	}

	private static void PickFrom(ChatLanguage language)
	{
		if (language == ChatTranslate.To)
		{
			ChatTranslate.To = ChatTranslate.From == ChatLanguages.Auto ? ChatLanguages.All.First(l => l != language) : ChatTranslate.From;
		}
		ChatTranslate.From = language;
		ClientSettings.Save();
	}

	private static void PickTo(ChatLanguage language)
	{
		if (language == ChatTranslate.From)
		{
			ChatTranslate.From = ChatTranslate.To;
		}
		ChatTranslate.To = language;
		ClientSettings.Save();
	}

	/// <summary>A sentence field: shows the translation here, without sending anything.</summary>
	private static int DrawTry(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		HubKit.Text(sb, "Try it", x, y, Theme.Text, 0.82f);
		HubKit.Text(sb, "(only you see this)", x + HubKit.Measure("Try it", 0.82f) + 8, y + 3, Theme.Muted, 0.66f);
		y += 24;
		int buttonW = 110;
		Rectangle field = new Rectangle(x, y, Math.Max(120, width - buttonW - 8), 32);
		_sample = HubKit.Field(sb, "tr:sample", field, _sample, "Hola [i:58] como andas?", 200, HubIcon.Item(I.Sign));
		bool enter = HubInput.Focus == "tr:sample" && Main.inputTextEnter;
		bool valid = !_busy && !string.IsNullOrWhiteSpace(_sample);
		if ((HubKit.Button(sb, "tr:try", new Rectangle(field.Right + 8, y, buttonW, 32), _busy ? "Translating" : "Translate", Theme.On * 0.6f, null, valid) || enter) && valid)
		{
			_busy = true;
			_result = null;
			ChatTranslate.Preview(_sample, ChatTranslate.From, ChatTranslate.To, delegate(string result, string error)
			{
				_busy = false;
				_resultIsError = error != null;
				_result = error ?? result;
			});
			HubInput.Sound(12);
		}
		y += 40;
		if (_result != null)
		{
			string[] lines = HubKit.Wrap(_resultIsError ? ("Could not translate: " + _result) : _result, width - 12, 0.78f, 4);
			Rectangle box = new Rectangle(x, y, width, 14 + lines.Length * 20);
			HubKit.Box(sb, box, Theme.Card * HubPrefs.Opacity);
			for (int i = 0; i < lines.Length; i++)
			{
				if (_resultIsError)
				{
					HubKit.Text(sb, lines[i], x + 10, y + 8 + i * 20, Theme.Danger, 0.78f);
				}
				else
				{
					HubKit.RichText(sb, lines[i], x + 10, y + 8 + i * 20, Theme.Text, 0.78f);
				}
			}
			y += box.Height + 4;
		}
		return y - start;
	}
	#endregion
}
