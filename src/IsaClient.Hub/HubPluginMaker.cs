using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using IsaClient.Assistant.PluginMaker;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// AI Assistant > Plugin Maker: the conversation with the plugin-making agent (PluginAgent), filling the page. A bar on
/// top (model, effort, goal; conversations, new, settings, help), the chat in its own scrolling box (your messages, its
/// answers, each thing it did, and "✻ Tinkering..." while it works), and the box to write pinned at the bottom (Enter
/// sends, / shows the commands, Tab completes them, Ctrl+V pastes a picture). Model, settings, conversations and help
/// open as windows in place of the chat.
/// </summary>
internal static class HubPluginMaker
{
	private enum Window
	{
		None,
		Model,
		Settings,
		Chats,
		Help
	}

	private static HubTheme Theme => HubTheme.Current;

	private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	private const float BodyScale = 0.74f;

	private static Window _window;

	private static readonly HubTextArea _box = new HubTextArea("pmaker:input") { Placeholder = "Tell it what to make or change...  (/ for commands, Shift+Enter: new line)" };

	/// <summary>Entries below the view since you last were at the bottom (the "new" button counts them).</summary>
	private static int _seenCount;

	private static bool _jump;

	private static readonly List<KeyValuePair<IsaClient.Assistant.AskImage, Texture2D>> _pictures = new List<KeyValuePair<IsaClient.Assistant.AskImage, Texture2D>>();

	private static bool _picking;

	private static bool _tabDown;

	// The chat box's scrolling.
	private static float _scroll;

	private static bool _atBottom = true;

	private static string _scrollDrag;

	private static float _scrollDragFrom;

	// The chat laid out (rebuilt when it changes or the width does).
	private static readonly List<Block> _blocks = new List<Block>();

	private static int _laidVersion = -1;

	private static int _laidWidth;

	private static float _windowScroll;

	private static List<PluginAgent.Conversation> _saved;

	private static string _confirmDelete;

	private sealed class Block
	{
		public PluginAgent.Entry Entry;

		public List<KeyValuePair<string, bool>> Lines;

		public int Height;
	}

	private static readonly string[] Verbs =
	{
		"Tinkering", "Compiling", "Forging", "Flambeing", "Conjuring", "Smelting", "Hammering", "Wiring", "Enchanting",
		"Reforging", "Brewing", "Whittling", "Debugging", "Spelunking", "Refactoring", "Pondering", "status::Crafting", "Soldering",
		"Summoning", "Mining", "Polishing", "Assembling", "Weaving", "Sleuthing", "Noodling", "Percolating", "Shimmering", "Roosting"
	};

	public static string Summary()
	{
		PluginAiSettings.EnsureLoaded();
		if (!PluginAgent.HasAnyKey)
		{
			return "No key";
		}
		return PluginAgent.Busy ? "Working..." : PluginAgent.ModelName(PluginAiSettings.Model);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		PluginAiSettings.EnsureLoaded();
		int total = Math.Max(420, ClientHub.PageViewHeight - 14);
		int start = y;
		y = DrawBar(sb, x, y, width);
		int bottom = start + total;
		int boxHeight = Math.Max(36, _box.Height(width - (PluginAgent.Busy ? 280 : 180)));
		int inputHeight = boxHeight + 22 + ((_pictures.Count > 0) ? 64 : 0);
		Rectangle area = new Rectangle(x, y, width, Math.Max(160, bottom - inputHeight - 8 - y));
		if (_window != Window.None)
		{
			DrawWindow(sb, area);
		}
		else
		{
			DrawChat(sb, area);
			DrawSuggestions(sb, area);
		}
		DrawInput(sb, x, area.Bottom + 8, width, boxHeight);
		return total;
	}

	#region Top bar

	private static int DrawBar(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle bar = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, bar, Color.Lerp(Theme.Card, new Color(120, 200, 255), 0.12f) * HubPrefs.Opacity);
		HubIcon.Item(I.TinkerersWorkshop).Draw(sb, new Rectangle(bar.X + 10, bar.Y + 8, 32, 32), Color.White, 3f);
		HubKit.Text(sb, "Plugin Maker", bar.X + 50, bar.Y + 5, Color.White, 0.84f);
		string title = (PluginAgent.Current.Title ?? "").Length > 0 ? PluginAgent.Current.Title : IsaClient.Languages.L10n.T("New conversation");
		HubKit.RawText(sb, Safe(title), bar.X + 50, bar.Y + 27, Theme.Muted, 0.62f, 200);
		// Chips: model, effort, goal (each opens its window or command).
		int cx = bar.X + 262;
		cx = Chip(sb, "pmaker:chip:model", cx, bar.Y + 11, IsaClient.Languages.L10n.T("Model") + ": " + ((PluginAiSettings.Model == "auto") ? "Auto" : PluginAgent.ModelName(PluginAiSettings.Model)), Window.Model, "Which AI model works (and the ones it falls back to).");
		cx = Chip(sb, "pmaker:chip:effort", cx, bar.Y + 11, IsaClient.Languages.L10n.T("Effort") + ": " + IsaClient.Languages.L10n.T(PluginAiSettings.Effort), Window.Model, "How thorough it is: /effort low | medium | high");
		if (PluginAgent.Goal.Length > 0)
		{
			Chip(sb, "pmaker:chip:goal", cx, bar.Y + 11, IsaClient.Languages.L10n.T("Goal") + ": " + PluginAgent.Flat(PluginAgent.Goal, 40), Window.Help, "The goal of this conversation: it keeps it in mind in every message. /goal clear removes it.");
		}
		// Windows and actions, on the right.
		int bx = bar.Right - 40;
		if (HubKit.IconButton(sb, "pmaker:help", new Rectangle(bx, bar.Y + 9, 32, 30), HubIcon.Item(I.Sign), "Help: commands and how it works", _window == Window.Help))
		{
			Toggle(Window.Help);
		}
		bx -= 36;
		if (HubKit.IconButton(sb, "pmaker:settings", new Rectangle(bx, bar.Y + 9, 32, 30), HubIcon.Item(I.Cog), "Settings", _window == Window.Settings))
		{
			Toggle(Window.Settings);
		}
		bx -= 36;
		if (HubKit.IconButton(sb, "pmaker:folder", new Rectangle(bx, bar.Y + 9, 32, 30), HubIcon.Item(I.Chest), "Open the plugins' folder (Plugins/Sources)"))
		{
			OpenFolder(PluginAgent.LastPlugin);
		}
		bx -= 36;
		if (HubKit.IconButton(sb, "pmaker:chats", new Rectangle(bx, bar.Y + 9, 32, 30), HubIcon.Item(I.Book), "Conversations: resume one or start a new one", _window == Window.Chats))
		{
			_saved = null;
			Toggle(Window.Chats);
		}
		bx -= 36;
		if (HubKit.IconButton(sb, "pmaker:new", new Rectangle(bx, bar.Y + 9, 32, 30), HubIcon.Item(I.FallenStar), "New conversation (this one is saved)", false, PluginAgent.Transcript.Count > 0))
		{
			PluginAgent.New();
			_window = Window.None;
			HubInput.Sound(12);
		}
		return bar.Bottom + 8;
	}

	/// <summary>A button's width for its label (the panel draws labels at 0.78).</summary>
	private static int Wide(string label, bool icon = false)
	{
		return (int)HubKit.Measure(label, 0.78f) + (icon ? 52 : 26);
	}

	private static int Chip(SpriteBatch sb, string id, int x, int y, string text, Window window, string tip)
	{
		text = Safe(text);
		int w = Wide(text);
		if (HubKit.Button(sb, id, new Rectangle(x, y, w, 26), text, (_window == window) ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Theme.Slot, tip))
		{
			Toggle(window);
		}
		return x + w + 6;
	}

	private static void Toggle(Window window)
	{
		_window = (_window == window) ? Window.None : window;
		_windowScroll = 0f;
		HubInput.Sound(12);
	}

	#endregion

	#region The chat

	private static void DrawChat(SpriteBatch sb, Rectangle area)
	{
		HubKit.Box(sb, area, Color.Black * 0.22f);
		Rectangle inner = new Rectangle(area.X + 8, area.Y + 8, area.Width - 26, area.Height - 16);
		Layout(inner.Width);
		int content = _blocks.Sum(b => b.Height) + (PluginAgent.Busy ? 40 : 0);
		List<PluginAgent.Entry> entries = PluginAgent.Transcript;
		if (entries.Count == 0)
		{
			content = DrawEmptyHeight(inner.Width);
		}
		float max = Math.Max(0f, content - inner.Height);
		// Its own wheel and scrollbar. At the bottom it follows what comes in; scrolled up, it stays where you are
		// (like Claude Code), until you go back down.
		HubInput.InnerScroll = area;
		if (_jump || _atBottom)
		{
			_scroll = max;
			_jump = false;
		}
		if (HubInput.Wheel != 0 && HubInput.In(area))
		{
			_scroll -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		_scroll = MathHelper.Clamp(_scroll, 0f, max);
		float bar = HubKit.Scrollbar(sb, "pmaker:scroll", new Rectangle(area.Right - 14, area.Y + 6, 10, area.Height - 12), _scroll, content, inner.Height, ref _scrollDrag, ref _scrollDragFrom);
		if (_scrollDrag == "pmaker:scroll")
		{
			_scroll = bar;
		}
		_atBottom = _scroll >= max - 4f;
		if (_atBottom)
		{
			_seenCount = _blocks.Count;
		}
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(inner, outerClip) : inner);
		int y = inner.Y - (int)_scroll;
		if (entries.Count == 0)
		{
			DrawEmpty(sb, inner.X, y, inner.Width);
		}
		foreach (Block block in _blocks)
		{
			if (y + block.Height >= inner.Y && y <= inner.Bottom)
			{
				DrawBlock(sb, block, inner.X, y, inner.Width);
			}
			y += block.Height;
		}
		if (PluginAgent.Busy)
		{
			DrawWorking(sb, inner.X, y + 4, inner.Width);
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
		// Scrolled up: a way back down (it follows again from there), with how many messages came meanwhile.
		if (!_atBottom && max - _scroll > 30f)
		{
			int fresh = Math.Max(0, _blocks.Count - _seenCount);
			string label = (fresh > 0) ? (IsaClient.Languages.L10n.T("New") + " (" + fresh + ")") : IsaClient.Languages.L10n.T("Bottom");
			if (HubKit.Button(sb, "pmaker:tobottom", new Rectangle(area.Right - 20 - Wide(label), area.Bottom - 34, Wide(label), 26), label, (fresh > 0) ? Color.Lerp(Theme.Slot, Theme.Accent, 0.5f) : Theme.Slot, "Scroll to the newest"))
			{
				_jump = true;
			}
		}
	}

	private static void Layout(int width)
	{
		if (_laidVersion == PluginAgent.Version && _laidWidth == width)
		{
			return;
		}
		_laidVersion = PluginAgent.Version;
		_laidWidth = width;
		_blocks.Clear();
		foreach (PluginAgent.Entry entry in PluginAgent.Transcript)
		{
			Block block = new Block { Entry = entry };
			switch (entry.Kind)
			{
			case PluginAgent.EntryKind.Step:
			case PluginAgent.EntryKind.Note:
				block.Height = 22;
				break;
			default:
			{
				int bubbleWidth = (entry.Kind == PluginAgent.EntryKind.User) ? (width - 60) : (width - 30);
				block.Lines = LayoutText(entry.Text, bubbleWidth - 24);
				block.Height = 30 + block.Lines.Sum(l => l.Value ? 18 : 20) + 8 + 8;
				break;
			}
			}
			_blocks.Add(block);
		}
	}

	private static void DrawBlock(SpriteBatch sb, Block block, int x, int y, int width)
	{
		PluginAgent.Entry entry = block.Entry;
		switch (entry.Kind)
		{
		case PluginAgent.EntryKind.User:
			Bubble(sb, (entry.Detail != null) ? (IsaClient.Languages.L10n.T("You") + "  (" + entry.Detail + ")") : IsaClient.Languages.L10n.T("You"), block.Lines, x + 60, y, width - 60, block.Height - 8, Color.Lerp(Theme.Card, Theme.Accent, 0.22f), Theme.Accent);
			break;
		case PluginAgent.EntryKind.Assistant:
			Bubble(sb, "Plugin Maker", block.Lines, x, y, width - 30, block.Height - 8, Theme.Card, new Color(150, 220, 255));
			break;
		case PluginAgent.EntryKind.Error:
			Bubble(sb, IsaClient.Languages.L10n.T("Error"), block.Lines, x, y, width - 30, block.Height - 8, Color.Lerp(Theme.Card, new Color(200, 60, 60), 0.25f), new Color(255, 150, 140));
			break;
		case PluginAgent.EntryKind.Step:
		{
			Color dot = entry.Failed ? new Color(240, 110, 100) : new Color(120, 220, 140);
			HubKit.Fill(sb, new Rectangle(x + 14, y + 8, 6, 6), dot);
			string text = Safe(entry.Text) + ((entry.Detail != null) ? ("   " + Safe(entry.Detail)) : "");
			HubKit.RawText(sb, text, x + 28, y + 1, entry.Failed ? new Color(240, 160, 150) : Theme.Muted, 0.66f, width - 40);
			break;
		}
		default:
			HubKit.RawText(sb, Safe(entry.Text), x + 28, y + 1, new Color(200, 190, 140), 0.64f, width - 40);
			break;
		}
	}

	private static void Bubble(SpriteBatch sb, string who, List<KeyValuePair<string, bool>> lines, int x, int y, int width, int height, Color back, Color name)
	{
		HubKit.Box(sb, new Rectangle(x, y, width, height), back * HubPrefs.Opacity);
		HubKit.RawText(sb, Safe(who), x + 12, y + 7, name, 0.72f);
		int ly = y + 28;
		int i = 0;
		while (i < lines.Count)
		{
			if (lines[i].Value)
			{
				int first = i;
				while (i < lines.Count && lines[i].Value)
				{
					i++;
				}
				HubKit.Fill(sb, new Rectangle(x + 10, ly - 2, width - 20, (i - first) * 18 + 4), Color.Black * 0.35f);
				for (int c = first; c < i; c++)
				{
					HubKit.RawText(sb, lines[c].Key, x + 16, ly, new Color(210, 225, 190), 0.64f, width - 32);
					ly += 18;
				}
				continue;
			}
			string line = lines[i].Key;
			Color color = Color.White;
			if (line.StartsWith("#"))
			{
				line = line.TrimStart('#', ' ');
				color = new Color(255, 225, 150);
			}
			HubKit.RawText(sb, line, x + 12, ly, color, BodyScale);
			ly += 20;
			i++;
		}
	}

	private static List<KeyValuePair<string, bool>> LayoutText(string text, int width)
	{
		List<KeyValuePair<string, bool>> result = new List<KeyValuePair<string, bool>>();
		bool code = false;
		foreach (string raw in Safe(text).Replace("\r", "").Split('\n'))
		{
			if (raw.TrimStart().StartsWith("```"))
			{
				code = !code;
				continue;
			}
			if (code)
			{
				result.Add(new KeyValuePair<string, bool>(raw.Replace("\t", "    "), true));
				continue;
			}
			string line = raw.Replace("**", "").Replace("`", "");
			if (line.TrimStart().StartsWith("* "))
			{
				line = "- " + line.TrimStart().Substring(2);
			}
			foreach (string piece in Wrap(line, width, BodyScale))
			{
				result.Add(new KeyValuePair<string, bool>(piece, false));
			}
		}
		while (result.Count > 0 && !result[result.Count - 1].Value && result[result.Count - 1].Key.Trim().Length == 0)
		{
			result.RemoveAt(result.Count - 1);
		}
		return result;
	}

	private static IEnumerable<string> Wrap(string line, int width, float scale)
	{
		if (Font.MeasureString(line).X * scale <= width)
		{
			yield return line;
			yield break;
		}
		StringBuilder current = new StringBuilder();
		foreach (string word in line.Split(' '))
		{
			string next = (current.Length == 0) ? word : (current + " " + word);
			if (Font.MeasureString(next).X * scale > width && current.Length > 0)
			{
				yield return current.ToString();
				current.Clear().Append(word);
			}
			else
			{
				current.Clear().Append(next);
			}
		}
		if (current.Length > 0)
		{
			yield return current.ToString();
		}
	}

	/// <summary>Text the game's font can draw: plain dashes and quotes, no emojis.</summary>
	private static string Safe(string text)
	{
		text = ChatText.Plain(text ?? "");
		StringBuilder b = new StringBuilder(text.Length);
		DynamicSpriteFont font = Font;
		foreach (char c in text)
		{
			if (c == '\n' || c == '\t' || (!char.IsSurrogate(c) && font.IsCharacterSupported(c)))
			{
				b.Append(c);
			}
		}
		return b.ToString();
	}

	private static void DrawWorking(SpriteBatch sb, int x, int y, int width)
	{
		double seconds = (DateTime.Now - PluginAgent.StartedAt).TotalSeconds;
		string verb = IsaClient.Languages.L10n.T(PluginAgent.Stopping ? "Stopping" : Verbs[(int)(seconds / 2.6) * 7 % Verbs.Length]) + "...";
		Color color = IsaClient.Assistant.AskAssistant.StatusColor;
		Vector2 center = new Vector2(x + 16, y + 11);
		double time = Main.timeForVisualEffects;
		float pulse = 0.72f + 0.28f * (float)Math.Sin(time * 0.11);
		float turn = (float)(time * 0.02);
		for (int i = 0; i < 4; i++)
		{
			sb.Draw(TextureAssets.MagicPixel.Value, center, new Rectangle(0, 0, 1, 1), color, turn + i * MathHelper.PiOver4, new Vector2(0.5f, 0.5f), new Vector2(16f * pulse, Math.Max(1.5f, 2.6f * pulse)), SpriteEffects.None, 0f);
		}
		HubKit.RawText(sb, verb, x + 32, y + 1, color, 0.8f);
		// The star (and its verb): Tetris to pass the time.
		if (HubInput.In(new Rectangle(x, y, 40 + (int)(Font.MeasureString(verb).X * 0.8f), 24)) && HubInput.Hover("pmaker:tetris", "Play Tetris while you wait"))
		{
			IsaClient.Games.Tetris.Open();
			HubInput.Sound(12);
		}
		string detail = "(" + IsaClient.Assistant.AskStatus.Stats(DateTime.Now - PluginAgent.StartedAt, PluginAgent.Tokens) + ((PluginAgent.StepCount > 0) ? (" · " + PluginAgent.StepCount + " steps") : "") + " · " + PluginAgent.ModelLabel + ")";
		HubKit.RawText(sb, Safe(detail), x + 32 + Font.MeasureString(verb).X * 0.8f + 10, y + 4, new Color(150, 150, 160), 0.64f, width - 260);
	}

	private static readonly string[] Ideas =
	{
		"Make a plugin that shows boxes around chests near me, with a toggle in its tab",
		"Make a DPS meter window for the boss I'm fighting",
		"Make a .heal command that fills my life and mana",
		"Look at my plugins and tell me what each one does"
	};

	private static int DrawEmptyHeight(int width) => 60 + Ideas.Length * 34;

	private static void DrawEmpty(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Tell it what you want, like you'd tell a programmer friend: it writes the plugin, compiles it and turns it on. Type / for commands. Some ideas:", x + 4, y + 6, Theme.Muted, 0.7f, width - 8);
		int iy = y + 36;
		foreach (string idea in Ideas)
		{
			string label = IsaClient.Languages.L10n.T(idea);
			if (HubKit.Button(sb, "pmaker:idea:" + idea.GetHashCode(), new Rectangle(x + 4, iy, Math.Min(width - 8, (int)(Font.MeasureString(label).X * 0.68f) + 44), 28), idea, Theme.Slot, "Click to write it in the box below.", true, HubIcon.Item(I.FallenStar)))
			{
				_box.SetText(label);
				HubInput.Focus = "pmaker:input";
			}
			iy += 34;
		}
	}

	/// <summary>While typing "/...": the commands that match, above the box (click or Tab fills one in).</summary>
	private static void DrawSuggestions(SpriteBatch sb, Rectangle area)
	{
		bool tab = Main.keyState.IsKeyDown(Keys.Tab);
		bool tabPressed = tab && !_tabDown;
		_tabDown = tab;
		string typed = _box.Text;
		if (!typed.StartsWith("/") || typed.Contains("\n"))
		{
			_cycle = null;
			return;
		}
		int space = typed.IndexOf(' ');
		if (space > 0)
		{
			DrawChoices(sb, area, typed.Substring(0, space), typed.Substring(space + 1), tabPressed);
			return;
		}
		_cycle = null;
		List<PluginAgent.SlashCommand> matches = PluginAgent.Commands.Where(c => c.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToList();
		if (matches.Count == 0)
		{
			return;
		}
		if (tabPressed && HubInput.Focus == "pmaker:input")
		{
			_box.SetText(matches[0].Name + " ");
			return;
		}
		int height = matches.Count * 26 + 10;
		Rectangle box = new Rectangle(area.X + 8, area.Bottom - height - 6, Math.Min(area.Width - 30, 620), height);
		HubKit.Box(sb, box, Color.Lerp(Theme.Card, Color.Black, 0.25f));
		int y = box.Y + 5;
		foreach (PluginAgent.SlashCommand command in matches)
		{
			Rectangle row = new Rectangle(box.X + 4, y, box.Width - 8, 24);
			bool hover = HubInput.In(row);
			if (hover)
			{
				HubKit.Fill(sb, row, Color.White * 0.08f);
				if (HubInput.Hover("pmaker:slash:" + command.Name))
				{
					_box.SetText(command.Name + " ");
					HubInput.Focus = "pmaker:input";
				}
			}
			HubKit.RawText(sb, command.Name + ((command.Args.Length > 0) ? (" " + command.Args) : ""), row.X + 6, row.Y + 3, new Color(150, 220, 255), 0.68f);
			HubKit.Text(sb, command.Help, row.X + 230, row.Y + 3, Theme.Muted, 0.64f, row.Width - 236);
			y += 26;
		}
	}

	/// <summary>The completion being cycled with Tab: what it was cycling over, and where it is.</summary>
	private static string _cycle;

	private static string _cycleFrom;

	private static int _cycleIndex;

	/// <summary>The choices a command takes (value, what it is).</summary>
	private static List<KeyValuePair<string, string>> Choices(string command)
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		switch (command.ToLowerInvariant())
		{
		case "/model":
			list.Add(new KeyValuePair<string, string>("auto", "MiniMax M3.1 first, then the other NVIDIA models, then Groq"));
			foreach (AiModel m in AiModel.Catalog)
			{
				list.Add(new KeyValuePair<string, string>(m.Label, (m.Provider.HasKey ? "" : "(needs a " + m.Provider.Name + " key) ") + m.Speed + (m.Sees ? ", sees pictures" : "")));
			}
			break;
		case "/effort":
			list.Add(new KeyValuePair<string, string>("low", "Quick, few checks"));
			list.Add(new KeyValuePair<string, string>("medium", "Balanced"));
			list.Add(new KeyValuePair<string, string>("high", "Thorough, slower"));
			break;
		case "/goal":
			list.Add(new KeyValuePair<string, string>("clear", "Removes the goal"));
			break;
		case "/rewind":
			list.Add(new KeyValuePair<string, string>("1", "Before your last message (files too)"));
			list.Add(new KeyValuePair<string, string>("2", "Before your last two messages"));
			list.Add(new KeyValuePair<string, string>("3", "Before your last three messages"));
			list.Add(new KeyValuePair<string, string>("1 chat", "Only the conversation; files stay as they are"));
			break;
		case "/resume":
		{
			_saved ??= PluginAgent.Saved();
			int n = 1;
			foreach (PluginAgent.Conversation c in _saved.Where(c => c.Id != PluginAgent.Current.Id).Take(12))
			{
				list.Add(new KeyValuePair<string, string>(n.ToString(), (c.Title ?? "") + "  (" + c.Updated.ToString("MMM d HH:mm") + ")"));
				n++;
			}
			break;
		}
		}
		return list;
	}

	private static void DrawChoices(SpriteBatch sb, Rectangle area, string command, string arg, bool tabPressed)
	{
		List<KeyValuePair<string, string>> all = Choices(command);
		if (all.Count == 0)
		{
			return;
		}
		// Tab: the first choice that matches what's typed, then the next one each time.
		string current = command + " " + arg;
		if (_cycle != current)
		{
			_cycle = null;
			_cycleFrom = arg;
			_cycleIndex = -1;
		}
		List<KeyValuePair<string, string>> matches = all.Where(c => c.Key.IndexOf(_cycleFrom ?? "", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		if (matches.Count == 0)
		{
			return;
		}
		if (tabPressed && HubInput.Focus == "pmaker:input")
		{
			_cycleIndex = (_cycleIndex + 1) % matches.Count;
			_cycle = command + " " + matches[_cycleIndex].Key;
			_box.SetText(_cycle);
			return;
		}
		int shown = Math.Min(matches.Count, 10);
		int height = shown * 26 + 10;
		Rectangle box = new Rectangle(area.X + 8, area.Bottom - height - 6, Math.Min(area.Width - 30, 620), height);
		HubKit.Box(sb, box, Color.Lerp(Theme.Card, Color.Black, 0.25f));
		int y = box.Y + 5;
		for (int i = 0; i < shown; i++)
		{
			KeyValuePair<string, string> choice = matches[i];
			Rectangle row = new Rectangle(box.X + 4, y, box.Width - 8, 24);
			bool lit = i == _cycleIndex;
			if (lit || HubInput.In(row))
			{
				HubKit.Fill(sb, row, Color.White * (lit ? 0.14f : 0.08f));
			}
			if (HubInput.In(row) && HubInput.Hover("pmaker:choice:" + i))
			{
				_box.SetText(command + " " + choice.Key);
				HubInput.Focus = "pmaker:input";
			}
			HubKit.RawText(sb, Safe(choice.Key), row.X + 6, row.Y + 3, new Color(150, 220, 255), 0.68f, 220);
			HubKit.RawText(sb, Safe(choice.Value), row.X + 230, row.Y + 3, Theme.Muted, 0.64f, row.Width - 236);
			y += 26;
		}
	}

	#endregion

	#region The box to write

	private static void DrawInput(SpriteBatch sb, int x, int y, int width, int boxHeight)
	{
		if (_pictures.Count > 0)
		{
			y = DrawPictures(sb, x, y);
		}
		bool busy = PluginAgent.Busy;
		int buttons = 40 + 40 + 96 + (busy ? 100 : 0);
		Rectangle field = new Rectangle(x, y, width - buttons, boxHeight);
		_box.Placeholder = busy ? "Write more (it reads it when it finishes)..." : "Tell it what to make or change...  (/ for commands, Shift+Enter: new line)";
		if (_box.HistoryEmpty)
		{
			// What you sent in this conversation, for Up/Down.
			foreach (PluginAgent.Entry entry in PluginAgent.Transcript.Where(e => e.Kind == PluginAgent.EntryKind.User))
			{
				_box.Remember(entry.Text);
			}
		}
		if (PluginAgent.Rewound != null)
		{
			// /rewind: your message back in the box, to change it and send it again.
			_box.SetText(PluginAgent.Rewound);
			PluginAgent.Rewound = null;
			HubInput.Focus = "pmaker:input";
		}
		_box.Draw(sb, field);
		if (_box.PastedNothing)
		{
			// Ctrl+V with a picture copied (no text): it's attached.
			PastePicture(quiet: true);
		}
		y = field.Bottom - 34;
		int bx = field.Right + 6;
		if (HubKit.IconButton(sb, "pmaker:pic", new Rectangle(bx, y + 2, 34, 30), HubIcon.Item(I.PaintingTheSeason), _picking ? "Choosing..." : "Attach pictures (a screenshot of an error, the window you want...)", false, !_picking && _pictures.Count < PluginAgent.MaxPictures))
		{
			PickPictures();
		}
		bx += 40;
		if (HubKit.IconButton(sb, "pmaker:paste", new Rectangle(bx, y + 2, 34, 30), HubIcon.Item(I.Book), "Paste the picture you copied (also Ctrl+V while writing here)", false, _pictures.Count < PluginAgent.MaxPictures))
		{
			PastePicture(quiet: false);
		}
		bx += 40;
		if (busy)
		{
			if (HubKit.Button(sb, "pmaker:stop", new Rectangle(bx, y, Math.Max(94, Wide("Stop now")), 34), PluginAgent.Stopping ? "Stop now" : "Stop", new Color(160, 60, 60), PluginAgent.Stopping ? "Stops right now, even in the middle of a step." : "Stops after the step it's on (press again to stop right now)."))
			{
				PluginAgent.Stop();
				HubInput.Sound(11);
			}
			bx += 100;
		}
		bool ready = _box.Text.Trim().Length > 0 || _pictures.Count > 0;
		bool enter = _box.Submitted;
		if ((HubKit.Button(sb, "pmaker:send", new Rectangle(bx, y, 92, 34), "Send", Theme.On * 0.6f, "Enter sends too (Shift+Enter: a new line).", ready) || enter) && ready)
		{
			string text = _box.Text;
			_box.Remember(text);
			_box.SetText("");
			if (text.Trim() == "/model")
			{
				_window = Window.Model;
			}
			else if (text.Trim() == "/resume")
			{
				_saved = null;
				_window = Window.Chats;
			}
			else if (text.Trim() == "/help")
			{
				_window = Window.Help;
			}
			else
			{
				PluginAgent.Send(text, toChat: false, _pictures.Select(p => p.Key).ToList());
				ClearPictures();
				_window = Window.None;
				_jump = true;
			}
			HubInput.Sound(12);
		}
		HubKit.Text(sb, "Enter: send   Shift+Enter: new line   /: commands (Tab completes)   Ctrl+V: paste text or a picture   In the game chat: .pai (Plugin Maker mode)", x + 2, field.Bottom + 5, Theme.Muted, 0.6f, width);
	}

	private static int DrawPictures(SpriteBatch sb, int x, int y)
	{
		int px = x;
		for (int i = 0; i < _pictures.Count; i++)
		{
			Rectangle r = new Rectangle(px, y, 76, 56);
			HubKit.Slot(sb, r, Theme.Slot);
			Texture2D thumb = _pictures[i].Value;
			if (thumb != null && !thumb.IsDisposed)
			{
				float scale = Math.Min((r.Width - 6f) / thumb.Width, (r.Height - 6f) / thumb.Height);
				int w = (int)(thumb.Width * scale);
				int h = (int)(thumb.Height * scale);
				sb.Draw(thumb, new Rectangle(r.Center.X - w / 2, r.Center.Y - h / 2, w, h), Color.White);
			}
			else
			{
				HubIcon.Item(I.PaintingTheSeason).Draw(sb, new Rectangle(r.X + 20, r.Y + 10, 36, 36), Color.White, 2f);
			}
			if (HubKit.Button(sb, "pmaker:picx" + i, new Rectangle(r.Right - 18, r.Y + 2, 16, 16), "x", new Color(150, 60, 60), "Take it out"))
			{
				_pictures[i].Value?.Dispose();
				_pictures.RemoveAt(i);
				break;
			}
			px += r.Width + 6;
		}
		HubKit.RawText(sb, _pictures.Count + " / " + PluginAgent.MaxPictures, px + 6, y + 20, Theme.Muted, 0.66f);
		return y + 64;
	}

	private static void AddPicture(byte[] data, string type)
	{
		if (data == null || data.Length == 0 || _pictures.Count >= PluginAgent.MaxPictures)
		{
			return;
		}
		Texture2D thumb = null;
		try
		{
			using MemoryStream stream = new MemoryStream(data);
			thumb = Texture2D.FromStream(Main.instance.GraphicsDevice, stream);
		}
		catch
		{
		}
		_pictures.Add(new KeyValuePair<IsaClient.Assistant.AskImage, Texture2D>(new IsaClient.Assistant.AskImage { Data = data, Type = type }, thumb));
		HubInput.Sound(12);
	}

	private static void ClearPictures()
	{
		foreach (KeyValuePair<IsaClient.Assistant.AskImage, Texture2D> picture in _pictures)
		{
			picture.Value?.Dispose();
		}
		_pictures.Clear();
	}

	private static void PastePicture(bool quiet)
	{
		try
		{
			if (!System.Windows.Forms.Clipboard.ContainsImage())
			{
				if (!quiet)
				{
					ClientChat.Error("There's no picture in the clipboard (copy one first, or use the picture button).");
				}
				return;
			}
			using System.Drawing.Image image = System.Windows.Forms.Clipboard.GetImage();
			if (image == null)
			{
				return;
			}
			using MemoryStream png = new MemoryStream();
			image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
			AddPicture(png.ToArray(), "image/png");
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void PickPictures()
	{
		_picking = true;
		IntPtr owner = Main.instance.Window.Handle;
		System.Threading.Thread thread = new System.Threading.Thread((System.Threading.ThreadStart)delegate
		{
			List<KeyValuePair<byte[], string>> files = new List<KeyValuePair<byte[], string>>();
			try
			{
				using System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog
				{
					Title = "Pictures for the Plugin Maker",
					Multiselect = true,
					Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
					InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
				};
				if (dialog.ShowDialog(new Owner(owner)) == System.Windows.Forms.DialogResult.OK)
				{
					foreach (string file in dialog.FileNames.Take(PluginAgent.MaxPictures))
					{
						string ext = Path.GetExtension(file).ToLowerInvariant();
						files.Add(new KeyValuePair<byte[], string>(File.ReadAllBytes(file), (ext == ".png") ? "image/png" : ((ext == ".gif") ? "image/gif" : "image/jpeg")));
					}
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			Main.QueueMainThreadAction(delegate
			{
				_picking = false;
				foreach (KeyValuePair<byte[], string> file in files)
				{
					AddPicture(file.Key, file.Value);
				}
			});
		});
		thread.SetApartmentState(System.Threading.ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	private sealed class Owner : System.Windows.Forms.IWin32Window
	{
		public Owner(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}

	#endregion

	#region Windows

	/// <summary>A window in place of the chat: a title, a close button, and its content (scrolling when long).</summary>
	private static void DrawWindow(SpriteBatch sb, Rectangle area)
	{
		HubKit.Box(sb, area, Color.Lerp(Theme.Card, Color.Black, 0.15f) * HubPrefs.Opacity);
		string title = _window switch
		{
			Window.Model => "Model & effort",
			Window.Settings => "Settings",
			Window.Chats => "Conversations",
			_ => "Help"
		};
		HubKit.Text(sb, title, area.X + 14, area.Y + 10, Theme.Accent, 0.86f);
		if (HubKit.Button(sb, "pmaker:close", new Rectangle(area.Right - 18 - Wide("Back to chat"), area.Y + 8, Wide("Back to chat"), 28), "Back to chat", Theme.Slot, null))
		{
			_window = Window.None;
			HubInput.Sound(11);
			return;
		}
		Rectangle inner = new Rectangle(area.X + 14, area.Y + 46, area.Width - 40, area.Height - 56);
		int content = _window switch
		{
			Window.Model => ModelWindowHeight(inner.Width),
			Window.Settings => 7 * 40 + 20,
			Window.Chats => ChatsWindowHeight(),
			_ => HelpWindowHeight(inner.Width)
		};
		float max = Math.Max(0f, content - inner.Height);
		HubInput.InnerScroll = area;
		if (HubInput.Wheel != 0 && HubInput.In(area))
		{
			_windowScroll -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		_windowScroll = MathHelper.Clamp(_windowScroll, 0f, max);
		string drag = _scrollDrag;
		_windowScroll = HubKit.Scrollbar(sb, "pmaker:wscroll", new Rectangle(area.Right - 14, inner.Y, 10, inner.Height), _windowScroll, content, inner.Height, ref _scrollDrag, ref _scrollDragFrom);
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(inner, outerClip) : inner);
		int y = inner.Y - (int)_windowScroll;
		switch (_window)
		{
		case Window.Model:
			DrawModelWindow(sb, inner.X, y, inner.Width);
			break;
		case Window.Settings:
			DrawSettingsWindow(sb, inner.X, y, inner.Width);
			break;
		case Window.Chats:
			DrawChatsWindow(sb, inner.X, y, inner.Width);
			break;
		default:
			DrawHelpWindow(sb, inner.X, y, inner.Width);
			break;
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
	}

	private static int ModelWindowHeight(int width) => 74 + 110 + AiProvider.All.Length * 40 + AiModel.Catalog.Length * 46 + 30;

	private static void DrawModelWindow(SpriteBatch sb, int x, int y, int width)
	{
		// Effort.
		HubKit.Text(sb, "Effort", x, y + 4, Color.White, 0.76f);
		int ex = x + 90;
		string[] notes = { "Quick: few steps, short answers.", "Balanced.", "Thorough: reads more, double-checks, polishes. Slower." };
		for (int i = 0; i < PluginAiSettings.Efforts.Length; i++)
		{
			string effort = PluginAiSettings.Efforts[i];
			bool on = PluginAiSettings.Effort == effort;
			if (HubKit.Button(sb, "pmaker:effort:" + effort, new Rectangle(ex, y, 96, 28), effort, on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.5f) : Theme.Slot, notes[i]))
			{
				PluginAiSettings.Effort = effort;
				PluginAiSettings.Save();
				HubInput.Sound(12);
			}
			ex += 102;
		}
		y += 34;
		foreach (string line in HubKit.Wrap("Low: quick, few checks. High: reads more, double-checks and polishes (slower). Groq's Qwen and GPT-OSS also think less or more.", width - 90, 0.62f, 2))
		{
			HubKit.Text(sb, line, x + 90, y, Theme.Muted, 0.62f);
			y += 16;
		}
		y += 12;
		// Auto.
		y = ModelRow(sb, "auto", "Auto", "MiniMax M3.1 first; when it fails: DeepSeek V4.1, Mistral Large 4, Step 5, GLM 5.3 (all NVIDIA), then Groq's Qwen and GPT-OSS.", "", true, true, x, y, width);
		y += 10;
		foreach (AiProvider provider in AiProvider.All)
		{
			bool has = provider.HasKey;
			HubKit.Text(sb, provider.Name, x, y + 10, Theme.Accent, 0.76f);
			string state = has ? ((provider == AiProvider.Groq && IsaClient.Assistant.AiAssistant.ApiKey == IsaClient.Assistant.AiAssistant.DefaultApiKey) ? "shared key" : "your key") : "no key";
			HubKit.Text(sb, state, x + 110, y + 12, has ? Theme.On : Theme.Muted, 0.64f);
			if (!has && HubKit.Button(sb, "pmaker:addkey:" + provider.Id, new Rectangle(x + 200, y + 6, 110, 26), "Add key", Theme.Slot, "AI Assistant > Connection"))
			{
				ClientHub.OpenSection("ai", 3);
			}
			y += 40;
			foreach (AiModel model in AiModel.Catalog.Where(m => m.Provider == provider))
			{
				y = ModelRow(sb, model.Key, model.Label, model.Note, model.Speed + (model.Sees ? "   sees pictures" : "") + (model.HasEffort ? "   effort" : ""), has, false, x, y, width);
			}
		}
	}

	private static int ModelRow(SpriteBatch sb, string key, string label, string note, string facts, bool usable, bool tall, int x, int y, int width)
	{
		Rectangle row = new Rectangle(x, y, width, tall ? 56 : 42);
		bool on = PluginAiSettings.Model == key;
		bool hover = usable && HubInput.In(row) && HubInput.Visible(row);
		HubKit.Box(sb, row, (on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.35f) : (hover ? Theme.CardHover : Theme.Card)) * (usable ? 1f : 0.5f));
		HubKit.Fill(sb, new Rectangle(row.X + 10, row.Y + 15, 10, 10), on ? Theme.Accent : Theme.Slot);
		HubKit.RawText(sb, Safe(label), row.X + 30, row.Y + 5, usable ? Color.White : Theme.Muted, 0.74f);
		if (facts.Length > 0)
		{
			HubKit.Text(sb, facts.Trim(), row.X + 290, row.Y + 7, new Color(160, 200, 160), 0.62f, width - 300);
		}
		if (!string.IsNullOrEmpty(note))
		{
			HubKit.Text(sb, note, row.X + 30, row.Y + 24, Theme.Muted, 0.6f, width - 40);
		}
		if (hover && HubInput.Hover("pmaker:model:" + key))
		{
			PluginAiSettings.Model = key;
			PluginAiSettings.Save();
			PluginAgent.ModelChanged();
			HubInput.Sound(12);
		}
		return row.Bottom + 4;
	}

	private static void DrawSettingsWindow(SpriteBatch sb, int x, int y, int width)
	{
		Switch(sb, "pmaker:router", x, y, width, "Smart routing", "A quick model reads each message first: small talk gets a fast answer, real work goes to the main model with the plugin it's about already in view.", ref PluginAiSettings.Router);
		y += 40;
		Switch(sb, "pmaker:fallback", x, y, width, "Fall back to other models", "When the chosen model fails or takes too long, the others of Auto take over.", ref PluginAiSettings.Fallback);
		y += 40;
		Switch(sb, "pmaker:autoenable", x, y, width, "Turn on what it builds", "Plugins it builds are turned on and reloaded by themselves, so you can try them right away.", ref PluginAiSettings.AutoEnable);
		y += 40;
		Switch(sb, "pmaker:echo", x, y, width, "Show my message in the chat (.pai)", "With .pai, your message is written in the chat above its answer.", ref PluginAiSettings.EchoToChat);
		y += 40;
		Stepper(sb, "pmaker:steps", x, y, "Most steps per message", PluginAiSettings.MaxSteps.ToString(), "Each file read, edit, build or search is a step.", () => PluginAiSettings.MaxSteps = Math.Max(5, PluginAiSettings.MaxSteps - 5), () => PluginAiSettings.MaxSteps = Math.Min(200, PluginAiSettings.MaxSteps + 5));
		y += 40;
		Stepper(sb, "pmaker:timeout", x, y, "Wait for a model (seconds)", PluginAiSettings.TimeoutSeconds.ToString(), "Longer than this, it tries the next model.", () => PluginAiSettings.TimeoutSeconds = Math.Max(30, PluginAiSettings.TimeoutSeconds - 30), () => PluginAiSettings.TimeoutSeconds = Math.Min(600, PluginAiSettings.TimeoutSeconds + 30));
		y += 40;
		Stepper(sb, "pmaker:temp", x, y, "Creativity (temperature)", PluginAiSettings.Temperature.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "Low: precise code (recommended). High: more varied ideas.", () => PluginAiSettings.Temperature = Math.Max(0f, (float)Math.Round(PluginAiSettings.Temperature - 0.1f, 1)), () => PluginAiSettings.Temperature = Math.Min(1.5f, (float)Math.Round(PluginAiSettings.Temperature + 0.1f, 1)));
	}

	private static void Switch(SpriteBatch sb, string id, int x, int y, int width, string label, string tip, ref bool value)
	{
		Rectangle row = new Rectangle(x, y, width, 34);
		bool hover = HubInput.In(row) && HubInput.Visible(row);
		HubKit.Box(sb, row, hover ? Theme.CardHover : Theme.Card);
		HubKit.Switch(sb, id, new Rectangle(x + 8, y + 6, 44, 22), value, hover);
		HubKit.Text(sb, label, x + 62, y + 4, Color.White, 0.72f);
		HubKit.Text(sb, tip, x + 62, y + 19, Theme.Muted, 0.56f, width - 70);
		if (hover && HubInput.Hover(id))
		{
			value = !value;
			PluginAiSettings.Save();
			HubInput.Sound(12);
		}
	}

	private static void Stepper(SpriteBatch sb, string id, int x, int y, string label, string value, string tip, Action less, Action more)
	{
		HubKit.Text(sb, label, x + 8, y + 2, Color.White, 0.72f);
		HubKit.Text(sb, tip, x + 8, y + 18, Theme.Muted, 0.56f, 330);
		if (HubKit.Button(sb, id + "-", new Rectangle(x + 360, y + 2, 30, 28), "-", Theme.Slot))
		{
			less();
			PluginAiSettings.Save();
		}
		HubKit.RawText(sb, value, x + 402, y + 7, Color.White, 0.76f);
		if (HubKit.Button(sb, id + "+", new Rectangle(x + 450, y + 2, 30, 28), "+", Theme.Slot))
		{
			more();
			PluginAiSettings.Save();
		}
	}

	private static int ChatsWindowHeight()
	{
		_saved ??= PluginAgent.Saved();
		return 44 + Math.Max(1, _saved.Count) * 52;
	}

	private static void DrawChatsWindow(SpriteBatch sb, int x, int y, int width)
	{
		_saved ??= PluginAgent.Saved();
		if (HubKit.Button(sb, "pmaker:chats:new", new Rectangle(x, y, 180, 30), "New conversation", Theme.On * 0.6f, "This one is saved; you can come back to it here.", true, HubIcon.Item(I.FallenStar)))
		{
			PluginAgent.New();
			_window = Window.None;
			return;
		}
		HubKit.Text(sb, "Saved in IsaClient/PluginMaker/Chats", x + 196, y + 8, Theme.Muted, 0.6f);
		y += 44;
		if (_saved.Count == 0)
		{
			HubKit.Text(sb, "No conversations yet.", x + 4, y + 6, Theme.Muted, 0.7f);
			return;
		}
		foreach (PluginAgent.Conversation c in _saved)
		{
			bool current = c.Id == PluginAgent.Current.Id;
			Rectangle row = new Rectangle(x, y, width, 46);
			HubKit.Box(sb, row, current ? Color.Lerp(Theme.Card, Theme.Accent, 0.25f) : Theme.Card);
			HubKit.RawText(sb, Safe(string.IsNullOrEmpty(c.Title) ? "(untitled)" : c.Title), row.X + 10, row.Y + 5, Color.White, 0.74f, width - 260);
			string facts = c.Updated.ToString("MMM d, HH:mm") + "   " + c.Messages + " " + IsaClient.Languages.L10n.T("messages") + ((c.Goal ?? "").Length > 0 ? ("   " + IsaClient.Languages.L10n.T("Goal") + ": " + PluginAgent.Flat(c.Goal, 50)) : "") + (current ? ("   " + IsaClient.Languages.L10n.T("(this one)")) : "");
			HubKit.RawText(sb, Safe(facts), row.X + 10, row.Y + 25, Theme.Muted, 0.6f, width - 260);
			if (!current && HubKit.Button(sb, "pmaker:resume:" + c.Id, new Rectangle(row.Right - 220, row.Y + 9, 100, 28), "Resume", Theme.On * 0.6f, "Go back to this conversation"))
			{
				PluginAgent.Resume(c);
				_window = Window.None;
				_jump = true;
				return;
			}
			bool asking = _confirmDelete == c.Id;
			if (HubKit.Button(sb, "pmaker:delete:" + c.Id, new Rectangle(row.Right - 112, row.Y + 9, 100, 28), asking ? "Sure?" : "Delete", asking ? new Color(170, 60, 60) : Theme.Slot, "Deletes it (click twice)"))
			{
				if (asking)
				{
					PluginAgent.Delete(c);
					_saved = null;
					_confirmDelete = null;
					return;
				}
				_confirmDelete = c.Id;
			}
			y += 52;
		}
	}

	private static readonly string[] HowItWorks =
	{
		"How it decides what to do: a quick model (the router) reads your message first, with the list of your plugins, the one it worked on last and the goal. It says whether it's small talk or real work, and which plugin it's about. The main model then gets that plugin's files in view.",
		"Edit or create: if you name one of your plugins, or talk about \"my plugin\" / \"it\" right after it worked on one, it edits that one (reads it first, changes only what you asked). It makes a new plugin only when you ask for something none of yours does. If it could be either, it asks. To be sure, name the plugin, or set a /goal.",
		"Each message: it reads, searches the game's API, writes the code, builds it, fixes the errors and turns the plugin on, until it works; then it tells you what it did. Stop once: it stops after the step it's on. Twice: right away.",
		"Models: Auto starts with MiniMax M3.1 (fast) and falls back to the other NVIDIA models, then Groq. Every model of the NVIDIA chain and Groq's Qwen see pictures."
	};

	private static int HelpWindowHeight(int width)
	{
		int lines = HowItWorks.Sum(p => HubKit.Wrap(p, width - 8, 0.66f, 20).Length);
		return PluginAgent.Commands.Length * 26 + 40 + lines * 17 + HowItWorks.Length * 8 + 20;
	}

	private static void DrawHelpWindow(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Commands (type them in the box)", x, y, Color.White, 0.76f);
		y += 26;
		foreach (PluginAgent.SlashCommand command in PluginAgent.Commands)
		{
			HubKit.RawText(sb, command.Name + ((command.Args.Length > 0) ? (" " + command.Args) : ""), x + 6, y, new Color(150, 220, 255), 0.68f);
			HubKit.Text(sb, command.Help, x + 240, y, Theme.Muted, 0.66f, width - 244);
			y += 26;
		}
		y += 14;
		foreach (string paragraph in HowItWorks)
		{
			foreach (string line in HubKit.Wrap(paragraph, width - 8, 0.66f, 20))
			{
				HubKit.Text(sb, line, x + 4, y, Theme.Text, 0.66f);
				y += 17;
			}
			y += 8;
		}
	}

	#endregion

	private static void OpenFolder(string plugin)
	{
		try
		{
			string folder = IsaClient.Plugins.PluginLoader.SourcesFolder;
			Directory.CreateDirectory(folder);
			if (!string.IsNullOrEmpty(plugin) && Directory.Exists(Path.Combine(folder, plugin)))
			{
				folder = Path.Combine(folder, plugin);
			}
			Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
