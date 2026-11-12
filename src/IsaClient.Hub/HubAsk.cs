using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Assistant;
using IsaClient.Community;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// AI Assistant > Ask: ask something right here (the answer shows in the page with its sources), and every setting of
/// .ask: web search, language, tone, length, pages to read, region, safe search, models, memory, own instructions.
/// </summary>
internal static class HubAsk
{
	private const int ChipHeight = 28;

	private static string _question = "";

	private static string _instructions;

	private static AskResult _result;

	private static string _error;

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>"Web search on, Friendly, Normal" for the card on the main page.</summary>
	public static string Summary()
	{
		string search = AskAssistant.SearchMode switch
		{
			"always" => "Always searches",
			"never" => "No web search",
			_ => "Searches when needed"
		};
		return search + ", " + AskAssistant.Tone + ", " + AskAssistant.Length;
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		HubIcon.Item(I.CrystalBall).Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, "Ask anything", x + 30, y + 4, Theme.Accent, 0.92f);
		string[] intro = HubKit.Wrap("Ask in the chat with .ask <question>: game data, news, prices, how-tos... It searches the web, reads the best pages and answers only from them, with numbered sources. Answers are shown only to you.", width - 34, 0.72f, 3);
		for (int i = 0; i < intro.Length; i++)
		{
			HubKit.Text(sb, intro[i], x + 30, y + 28 + i * 18, Theme.Muted, 0.72f);
		}
		y += 36 + intro.Length * 18 + 6;

		y += DrawTry(sb, x, y, width) + 16;

		y += Section(sb, "Answers", x, y);
		y += Row(sb, "ask:search", "Web search", x, y, width, new[] { ("auto", "When needed"), ("always", "Always"), ("never", "Never") }.Select(o => Chip(o.Item2, AskAssistant.SearchMode == o.Item1, () => AskAssistant.SearchMode = o.Item1)));
		List<ChipSpec> languages = new List<ChipSpec>
		{
			Chip("Same as question", AskAssistant.LanguageCode == "auto", () => AskAssistant.LanguageCode = "auto"),
			Chip("Game (" + ChatLanguages.GameLanguage.Name + ")", AskAssistant.LanguageCode == "game", () => AskAssistant.LanguageCode = "game", ChatLanguages.GameLanguage.Code)
		};
		languages.AddRange(ChatLanguages.All.Select(l => Chip(l.Name, AskAssistant.LanguageCode == l.Code, () => AskAssistant.LanguageCode = l.Code, l.Code)));
		y += Row(sb, "ask:lang", "Language", x, y, width, languages);
		y += Row(sb, "ask:tone", "Tone", x, y, width, AskAssistant.Tones.Select(t => Chip(t.Name, AskAssistant.Tone == t.Name, () => AskAssistant.Tone = t.Name)));
		y += Row(sb, "ask:len", "Length", x, y, width, AskAssistant.Lengths.Select(l => Chip(l.Name, AskAssistant.Length == l.Name, () => AskAssistant.Length = l.Name)));
		y += 8;

		y += Section(sb, "Web search", x, y);
		y += Row(sb, "ask:engine", "Search with", x, y, width, new[] { ("groq", "Groq browser (recommended)"), ("free", "Free engines from here") }.Select(o => Chip(o.Item2, AskAssistant.Engine == o.Item1, () => AskAssistant.Engine = o.Item1)));
		y += Row(sb, "ask:depth", "Research depth", x, y, width, AskAssistant.Depths.Select(d => Chip(d.Name + " (" + d.Rounds + (d.Rounds == 1 ? " round)" : " rounds)"), AskAssistant.MaxRounds == d.Rounds, () => AskAssistant.MaxRounds = d.Rounds)));
		y += Row(sb, "ask:pages", "Pages per round", x, y, width, Enumerable.Range(1, 5).Select(n => Chip(n.ToString(), AskAssistant.MaxPages == n, () => AskAssistant.MaxPages = n)));
		y += Row(sb, "ask:region", "Region", x, y, width, AskAssistant.Regions.Select(r => Chip(r.Name, AskAssistant.Region == r.Code, () => AskAssistant.Region = r.Code)));
		y += Row(sb, "ask:safe", "Safe search", x, y, width, new[] { (1, "Strict"), (-1, "Moderate"), (-2, "Off") }.Select(o => Chip(o.Item2, AskAssistant.SafeSearch == o.Item1, () => AskAssistant.SafeSearch = o.Item1)));
		y += 8;

		y += Section(sb, "Backup models (when Groq's browser is out of tokens)", x, y);
		y += Row(sb, "ask:model", "Writes answers", x, y, width, AskAssistant.AnswerModels.Select(m => Chip(ModelName(m), AskAssistant.AnswerModel == m, () => AskAssistant.AnswerModel = m)));
		y += Row(sb, "ask:router", "Plans & reads", x, y, width, AskAssistant.RouterModels.Select(m => Chip(ModelName(m), AskAssistant.RouterModel == m, () => AskAssistant.RouterModel = m)));
		y += 8;

		y += Section(sb, "Chat", x, y);
		y += Toggles(sb, x, y, width) + 12;

		y += Section(sb, "Look", x, y);
		y += DrawLook(sb, x, y, width) + 12;

		y += Section(sb, "Your instructions", x, y);
		_instructions ??= AskAssistant.Instructions;
		Rectangle field = new Rectangle(x, y, Math.Max(160, width - 98), 32);
		_instructions = HubKit.Field(sb, "ask:rules", field, _instructions, "Optional: \"call me Isa\", \"always add an example\"...", 300, HubIcon.Item(I.Book));
		bool changed = _instructions != AskAssistant.Instructions;
		if ((HubKit.Button(sb, "ask:rules:save", new Rectangle(field.Right + 8, y, 90, 32), "Save", Theme.On * 0.6f, null, changed) || (HubInput.Focus == "ask:rules" && Main.inputTextEnter)) && changed)
		{
			AskAssistant.Instructions = _instructions.Trim();
			ClientSettings.Save();
			HubInput.Sound(10);
		}
		y += 44;

		HubKit.Line(sb, x, y, width, Theme.Muted * 0.25f);
		y += 10;
		string[] footer = HubKit.Wrap("Uses your Groq key (IsaAI.json). Groq's browser searches and reads on Groq's servers and answers in one call; when its free daily tokens run out, the backup searches DuckDuckGo, Brave, Wikipedia and the Terraria wiki from here. Commands: .ask <question>, .ask sources, .ask forget.", width, 0.66f, 4);
		for (int i = 0; i < footer.Length; i++)
		{
			HubKit.Text(sb, footer[i], x, y + i * 16, Theme.Muted * 0.9f, 0.66f);
		}
		y += footer.Length * 16 + 6;
		return y - start;
	}

	private static string _name;

	private static HubFeature[] _colors;

	/// <summary>The assistant's name and its three colors (name, working line, answer), with a preview.</summary>
	private static int DrawLook(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		const int labelW = 120;
		HubKit.Text(sb, "Name", x + 4, y + 8, Theme.Muted, 0.72f, labelW - 8);
		_name ??= AskAssistant.DisplayName;
		Rectangle field = new Rectangle(x + labelW, y, Math.Min(260, width - labelW - 98), 30);
		_name = HubKit.Field(sb, "ask:name", field, _name, "Isa", 24, HubIcon.Item(I.Sign));
		bool changed = _name.Trim().Length > 0 && _name.Trim() != AskAssistant.DisplayName;
		if ((HubKit.Button(sb, "ask:name:save", new Rectangle(field.Right + 8, y, 90, 30), "Save", Theme.On * 0.6f, null, changed) || (HubInput.Focus == "ask:name" && Main.inputTextEnter)) && changed)
		{
			AskAssistant.Name = _name.Trim();
			ClientSettings.Save();
			HubInput.Sound(10);
		}
		y += 40;

		_colors ??= new[]
		{
			ColorFeature("ask:c:name", "Name color", () => AskAssistant.NameColor, c => AskAssistant.NameColor = c, AskAssistant.DefaultNameColor),
			ColorFeature("ask:c:work", "Working line", () => AskAssistant.StatusColor, c => AskAssistant.StatusColor = c, AskAssistant.DefaultStatusColor),
			ColorFeature("ask:c:text", "Answer text", () => AskAssistant.AnswerColor, c => AskAssistant.AnswerColor = c, ClientChat.AccentColor)
		};
		int cx = x + labelW;
		HubKit.Text(sb, "Colors", x + 4, y + 8, Theme.Muted, 0.72f, labelW - 8);
		foreach (HubFeature feature in _colors)
		{
			int w = (int)HubKit.Measure(feature.Title, 0.7f) + 46;
			if (cx > x + labelW && cx + w > x + width)
			{
				cx = x + labelW;
				y += 36;
			}
			Rectangle r = new Rectangle(cx, y, w, 30);
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(feature.Id, "Click to pick a color"))
			{
				HubColorPicker.Open(feature);
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(feature.Id, hover)) * HubPrefs.Opacity);
			HubKit.Fill(sb, new Rectangle(r.X + 8, r.Y + 7, 16, 16), feature.GetColor());
			HubKit.Text(sb, feature.Title, r.X + 32, r.Y + 7, Theme.Text, 0.7f);
			cx += w + 6;
		}
		y += 40;

		// Preview: the working line and the start of an answer.
		Rectangle preview = new Rectangle(x + labelW, y, Math.Max(200, width - labelW), 52);
		HubKit.Box(sb, preview, new Color(10, 12, 24) * 0.75f);
		float pulse = 0.72f + 0.28f * (float)Math.Sin(Main.timeForVisualEffects * 0.11);
		Vector2 star = new Vector2(preview.X + 18, preview.Y + 15);
		for (int i = 0; i < 4; i++)
		{
			sb.Draw(HubKit.Pixel, star, new Rectangle(0, 0, 1, 1), AskAssistant.StatusColor, (float)(Main.timeForVisualEffects * 0.02) + i * MathHelper.PiOver4, new Vector2(0.5f, 0.5f), new Vector2(14f * pulse, 2.2f * pulse), SpriteEffects.None, 0f);
		}
		HubKit.Text(sb, "Spelunking...", preview.X + 30, preview.Y + 6, AskAssistant.StatusColor, 0.78f);
		HubKit.RichText(sb, ClientChat.Tint(AskAssistant.DisplayName + ": ", AskAssistant.NameColor) + "the Zenith does 190 damage", preview.X + 12, preview.Y + 27, AskAssistant.AnswerColor, 0.78f, preview.Width - 20);
		y += preview.Height + 4;
		return y - start;
	}

	private static HubFeature ColorFeature(string id, string title, Func<Color> get, Action<Color> set, Color fallback)
	{
		return new HubFeature
		{
			Id = id,
			Title = title,
			NativeKind = FeatureKind.Color,
			GetColor = get,
			SetColor = set,
			ColorDone = ClientSettings.Save,
			DefaultColor = fallback,
			Cheat = false
		};
	}

	#region Try it

	private static string AssistantTags(int count)
	{
		return (count == 1) ? IsaClient.Languages.L10n.T("1 picture") : IsaClient.Languages.L10n.F("{0} pictures", count);
	}

	private static int DrawTry(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		Rectangle field = new Rectangle(x, y, Math.Max(160, width - 118 - 42), 34);
		_question = HubKit.Field(sb, "ask:q", field, _question, "What weapons did Terraria 1.4.5 add?", 300, HubIcon.Item(I.CrystalBall));
		// Pictures: the same ones attached above the chat bar (a vision model looks at them).
		int pictures = IsaClient.ChatImages.ChatAttachments.ReadyCount;
		Rectangle attach = new Rectangle(field.Right + 8, y, 34, 34);
		if (HubKit.IconButton(sb, "ask:attach", attach, HubIcon.Item(I.PaintingTheSeason), (pictures > 0) ? IsaClient.Languages.L10n.F("{0} attached: they go with the question. Click to add more.", AssistantTags(pictures)) : "Attach pictures to ask about them", pictures > 0))
		{
			IsaClient.ChatImages.ChatAttachments.OpenPicker();
		}
		if (pictures > 0)
		{
			HubKit.Chip(sb, pictures.ToString(), attach.Right - 4, attach.Y - 6, Theme.Accent * 0.8f, Color.Black, 0.55f, alignRight: true);
		}
		bool valid = !AskAssistant.Busy && (!string.IsNullOrWhiteSpace(_question) || pictures > 0);
		bool enter = HubInput.Focus == "ask:q" && Main.inputTextEnter;
		if ((HubKit.Button(sb, "ask:go", new Rectangle(attach.Right + 8, y, 110, 34), AskAssistant.Busy ? "Thinking" : "Ask", Theme.On * 0.6f, null, valid) || enter) && valid)
		{
			_result = null;
			_error = null;
			AskAssistant.Ask(_question, toChat: false, delegate(AskResult result, string error)
			{
				_result = result;
				_error = error;
			}, AskAssistant.AttachedPictures());
			HubInput.Sound(12);
		}
		y += 44;

		if (AskAssistant.Busy)
		{
			int dots = (int)(Main.timeForVisualEffects / 20.0) % 4;
			HubKit.Text(sb, (AskAssistant.Status.Length > 0 ? AskAssistant.Status : "Thinking") + new string('.', dots), x + 4, y, Theme.Muted, 0.72f, width - 8);
			return y + 22 - start;
		}
		if (_error != null)
		{
			HubKit.Text(sb, "Could not answer: " + _error, x + 4, y, Theme.Danger, 0.74f, width - 8);
			return y + 24 - start;
		}
		if (_result == null)
		{
			return y - start;
		}

		// The answer in a card, citations tinted, then the sources (click opens them).
		List<string> lines = new List<string>();
		foreach (string paragraph in _result.Answer.Split('\n'))
		{
			lines.AddRange(paragraph.Trim().Length == 0 ? new[] { "" } : HubKit.Wrap(paragraph, width - 28, 0.78f, 40));
		}
		int sourcesH = (_result.Sources.Count > 0) ? 34 : 0;
		Rectangle card = new Rectangle(x, y, width, 18 + lines.Count * 20 + sourcesH);
		HubKit.Box(sb, card, Theme.Card * HubPrefs.Opacity);
		for (int i = 0; i < lines.Count; i++)
		{
			HubKit.RichText(sb, Cite(lines[i]), x + 14, y + 10 + i * 20, Theme.Text, 0.78f);
		}
		if (sourcesH > 0)
		{
			int cx = x + 14;
			int cy = card.Bottom - sourcesH + 2;
			HubKit.Text(sb, "Sources", cx, cy + 6, Theme.Muted, 0.66f);
			cx += (int)HubKit.Measure("Sources", 0.66f) + 10;
			foreach (AskSource source in _result.Sources)
			{
				string label = "(" + source.Number + ") " + source.Domain;
				int w = (int)HubKit.Measure(label, 0.66f) + 16;
				if (cx + w > card.Right - 10)
				{
					break;
				}
				Rectangle chip = new Rectangle(cx, cy, w, 24);
				bool hover = HubInput.In(chip);
				if (hover && HubInput.Hover("ask:src:" + source.Number, source.Title + "\n" + source.Url))
				{
					ChatLinks.Open(source.Url);
				}
				HubKit.Box(sb, chip, Color.Lerp(Theme.Slot, Theme.Accent, hover ? 0.4f : 0.15f));
				HubKit.Text(sb, label, chip.X + 8, chip.Y + 5, Color.White, 0.66f);
				cx += w + 6;
			}
		}
		return card.Bottom + 4 - start;
	}

	private static string Cite(string line)
	{
		return System.Text.RegularExpressions.Regex.Replace(line, "\\((\\d{1,2}(?:, \\d{1,2})*)\\)", m => "[c/6EB9FF:" + m.Value + "]");
	}

	#endregion

	#region Pieces

	private sealed class ChipSpec
	{
		public string Label;

		public bool Active;

		public Action Pick;

		public string Flag;
	}

	private static ChipSpec Chip(string label, bool active, Action pick, string flag = null)
	{
		return new ChipSpec { Label = label, Active = active, Pick = pick, Flag = flag };
	}

	private static int Section(SpriteBatch sb, string title, int x, int y)
	{
		HubKit.Text(sb, title, x, y, Theme.Text, 0.84f);
		return 26;
	}

	/// <summary>A label on the left and chips that wrap; picking one saves.</summary>
	private static int Row(SpriteBatch sb, string id, string label, int x, int y, int width, IEnumerable<ChipSpec> chips)
	{
		const int labelW = 120;
		HubKit.Text(sb, label, x + 4, y + 6, Theme.Muted, 0.72f, labelW - 8);
		int left = x + labelW;
		int cx = left;
		int cy = y;
		int index = 0;
		foreach (ChipSpec chip in chips)
		{
			int w = (int)HubKit.Measure(chip.Label, 0.7f) + 20 + (chip.Flag != null ? 26 : 0);
			if (cx > left && cx + w > x + width)
			{
				cx = left;
				cy += ChipHeight + 6;
			}
			Rectangle r = new Rectangle(cx, cy, w, ChipHeight);
			cx += w + 6;
			string chipId = id + ":" + index++;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(chipId) && !chip.Active)
			{
				chip.Pick();
				ClientSettings.Save();
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim(chipId, hover || chip.Active);
			Color back = chip.Active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.4f) : Color.Lerp(Theme.Card, Theme.CardHover, glow);
			HubKit.Box(sb, r, back * HubPrefs.Opacity);
			int tx = r.X + 10;
			if (chip.Flag != null)
			{
				HubFlags.Draw(sb, chip.Flag, new Rectangle(r.X + 8, r.Y + 7, 21, 14));
				tx += 26;
			}
			HubKit.Text(sb, chip.Label, tx, r.Y + 6, chip.Active ? Color.White : Theme.Text, 0.7f);
		}
		return cy + ChipHeight + 8 - y;
	}

	private static int Toggles(SpriteBatch sb, int x, int y, int width)
	{
		(string Id, string Title, string Tip, Func<bool> Get, Action<bool> Set)[] toggles =
		{
			("ask:t:src", "Show Sources", "A line with the sites the answer comes from.", () => AskAssistant.ShowSources, v => AskAssistant.ShowSources = v),
			("ask:t:prog", "Show Searches", "Gray lines with what it searched and read, above the working line.", () => AskAssistant.ShowProgress, v => AskAssistant.ShowProgress = v),
			("ask:t:mem", "Remember Conversation", "Follow-up questions know what you asked before (last " + 4 + ").", () => AskAssistant.Remember, v => AskAssistant.Remember = v)
		};
		int columns = (width >= 620) ? 3 : 1;
		int tileW = (width - (columns - 1) * 8) / columns;
		const int height = 40;
		for (int i = 0; i < toggles.Length; i++)
		{
			var t = toggles[i];
			Rectangle r = new Rectangle(x + (i % columns) * (tileW + 8), y + (i / columns) * (height + 8), tileW, height);
			bool on = t.Get();
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(t.Id, t.Tip))
			{
				t.Set(!on);
				on = !on;
				ClientSettings.Save();
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(t.Id, hover)) * HubPrefs.Opacity);
			HubKit.Text(sb, t.Title, r.X + 12, r.Y + 11, on ? Theme.Text : Theme.Muted, 0.76f, r.Width - 76);
			HubKit.Switch(sb, t.Id + ":sw", new Rectangle(r.Right - 56, r.Y + 9, 44, 22), on, hover);
		}
		int rows = (toggles.Length + columns - 1) / columns;
		int used = rows * height + (rows - 1) * 8;
		int by = y + used + 8;
		string memory = AskAssistant.Remembered == 0 ? "Nothing remembered" : (AskAssistant.Remembered + " question" + (AskAssistant.Remembered == 1 ? "" : "s") + " remembered");
		HubKit.Text(sb, memory, x + 4, by + 7, Theme.Muted, 0.7f);
		if (HubKit.Button(sb, "ask:forget", new Rectangle(x + 12 + (int)HubKit.Measure(memory, 0.7f), by, 100, 28), "Forget", Theme.Slot, "Start a new conversation", AskAssistant.Remembered > 0))
		{
			AskAssistant.Forget();
			HubInput.Sound(11);
		}
		return used + 8 + 28;
	}

	/// <summary>"qwen3.8-27b" from "qwen/qwen3.8-27b".</summary>
	private static string ModelName(string model)
	{
		int slash = model.IndexOf('/');
		return (slash >= 0) ? model.Substring(slash + 1) : model;
	}

	#endregion
}
