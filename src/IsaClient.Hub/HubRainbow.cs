using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using IsaClient.Core;
using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Color = Microsoft.Xna.Framework.Color;
using I = Terraria.ID.ItemID;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using XKeys = Microsoft.Xna.Framework.Input.Keys;

namespace IsaClient.Hub;

/// <summary>
/// The rainbow font maker (Experimental, Rainbow): every letter .rainbow can write, drawn on a canvas that shows it as
/// the game will (the real rainbow sprites) or as its strokes. Pick a character on the left (or add any symbol), draw,
/// and it's saved as you go (your letters replace the built-in ones; Reset brings a built-in one back).
/// <list type="bullet">
/// <item>Pencil: draw freely; while you draw it's turned into the lines and arcs closest to your hand.</item>
/// <item>Line, Arc (drag the ends, then bend and click), Circle (drag from the middle), Dot (click; drag to aim).</item>
/// <item>Select: click or box strokes (Shift adds), drag to move them, drag a lone stroke's handles to reshape it;
/// arrows nudge, Delete removes, Ctrl+D duplicates, Ctrl+C / Ctrl+V copy and paste, Ctrl+A picks all.</item>
/// <item>Code: the letter as RainbowLetters XML, colored, edited with the canvas showing it live.</item>
/// <item>Ctrl+Z / Ctrl+Y undo and redo. Right click or Escape cancels what you're drawing.</item>
/// </list>
/// Import and export use the RainbowLetters XML, as a file or as text.
/// </summary>
internal static class HubRainbow
{
	private enum Tool
	{
		Select,
		Pencil,
		Line,
		Arc,
		Circle,
		Dot,
		Erase
	}

	private enum View
	{
		Game,
		Strokes,
		Both
	}

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>Glyph units shown from the canvas' middle to its edge.</summary>
	private const float Half = 100f;

	private const float Grid = 5f;

	private const int RainbowProjectile = 251;

	private static Tool _tool = Tool.Pencil;

	private static View _view = View.Game;

	private static bool _snap = true;

	private static char _char = 'A';

	private static RainbowGlyph _glyph;

	private static int _loadedVersion = -1;

	private static readonly List<RainbowGlyph> _undo = new List<RainbowGlyph>();

	private static readonly List<RainbowGlyph> _redo = new List<RainbowGlyph>();

	private static readonly HashSet<int> _sel = new HashSet<int>();

	private static List<RainbowShape> _copied = new List<RainbowShape>();

	private static bool _wasDown;

	/// <summary>A drag in progress (pressed inside the canvas).</summary>
	private static bool _dragging;

	private static Vector2 _dragStart;

	/// <summary>Select: what's dragged (-1 the selection, else a handle of the lone selected stroke).</summary>
	private static int _handle = -2;

	private static Dictionary<int, RainbowShape> _dragOriginals;

	/// <summary>Select: a box being drawn over empty canvas.</summary>
	private static bool _boxing;

	/// <summary>Arc: the two ends are set, the mouse bends it until a click.</summary>
	private static bool _bending;

	private static Vector2 _arcA;

	private static Vector2 _arcB;

	/// <summary>Pencil: what the hand drew, and what it becomes.</summary>
	private static List<Vector2> _ink;

	private static List<RainbowShape> _inkShapes = new List<RainbowShape>();

	private static string _newChar = "";

	private static string _try = "";

	private static bool _codeOpen;

	private static HubTextArea _code;

	private static string _codeShown;

	private static string _codeError;

	private static bool _codeDirty;

	private static KeyboardState _oldKeys;

	#region Page

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		Load();
		y = Header(sb, x, y, width);
		y = Toolbar(sb, x, y, width);
		// The canvas and, beside it, the actions or the code (seen together); the characters along the bottom.
		int sideWidth = Math.Max(260, Math.Min(360, width / 3));
		// Never taller than what the panel shows at once (the tools, the canvas and the try row all in view).
		int fits = ClientHub.PageViewHeight - 46 - 44 - 16;
		int size = Math.Max(220, Math.Min(440, Math.Min(width - sideWidth - 12, fits)));
		Rectangle canvas = new Rectangle(x, y, size, size);
		Canvas(sb, canvas);
		int tryBottom = TryRow(sb, x, canvas.Bottom + 8, size);
		int sideX = canvas.Right + 12;
		int sideW = x + width - sideX;
		int sideBottom = _codeOpen ? Code(sb, sideX, y, sideW, tryBottom - y) : Side(sb, sideX, y, sideW);
		y = Math.Max(tryBottom, sideBottom) + 12;
		return DrawCharacters(sb, x, y, width) + 8 - start;
	}

	private static void Load()
	{
		if (_glyph != null && _glyph.Char == _char && (_loadedVersion == RainbowFont.Version || _codeDirty))
		{
			return;
		}
		if (_glyph == null || _glyph.Char != _char)
		{
			_undo.Clear();
			_redo.Clear();
			_sel.Clear();
			_bending = false;
			_dragging = false;
			_ink = null;
			_codeDirty = false;
		}
		_glyph = RainbowFont.Get(_char)?.Clone() ?? new RainbowGlyph { Char = _char };
		_glyph.Char = _char;
		_loadedVersion = RainbowFont.Version;
	}

	/// <summary>Title and the font's file buttons.</summary>
	private static int Header(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle bar = new Rectangle(x, y, width, 44);
		HubKit.Box(sb, bar, Theme.Card * HubPrefs.Opacity);
		HubIcon.Item(I.RainbowGun).Draw(sb, new Rectangle(bar.X + 10, bar.Y + 8, 28, 28), Color.White, 1.5f);
		HubKit.Text(sb, "Rainbow Font Maker", bar.X + 46, bar.Y + 13, Color.White, 0.85f);
		int right = bar.Right - 8;
		(string Label, int Icon, string Tip, Action Run)[] buttons =
		{
			("Export", I.Book, "Saves the whole font (yours and the built-in letters) as a RainbowLetters XML file.", ExportFile),
			("Import", I.Book, "Adds the letters of a RainbowLetters XML file to yours.", ImportFile),
			("Copy", I.Paintbrush, "Copies the whole font as text, to paste anywhere (or in a friend's Paste).", () => CopyText(RainbowFont.Export(), "The rainbow font is copied.")),
			("Paste", I.Paintbrush, "Adds the letters from text you copied (RainbowLetters XML).", PasteText),
			("Send font", I.RainbowRod, "Sends your letters in the chat (the whole font when you have none of your own): whoever gets them picks which to add.", SendFont),
			("Send letter", I.RainbowRod, "Sends this letter in the chat: whoever gets it can add it to their font.", SendLetter)
		};
		foreach ((string label, int icon, string tip, Action run) in buttons)
		{
			int w = (int)HubKit.Measure(IsaClient.Languages.L10n.T(label), 0.76f) + 44;
			Rectangle b = new Rectangle(right - w, bar.Y + 7, w, 30);
			if (HubKit.Button(sb, "rbf:" + label, b, label, Theme.Slot, tip, true, HubIcon.Item(icon)))
			{
				run();
			}
			right = b.X - 6;
		}
		return bar.Bottom + 10;
	}

	#endregion

	#region Characters

	private static int DrawCharacters(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Characters", x, y + 6, Theme.Muted, 0.72f);
		// Any character, symbol or accent: typed here, beside the title.
		int fieldWidth = Math.Min(220, width / 3);
		_newChar = HubKit.Field(sb, "rbf:new", new Rectangle(x + width - fieldWidth - 80, y, fieldWidth, 30), _newChar, "New symbol: type one...", 1);
		bool can = !string.IsNullOrEmpty(_newChar) && !char.IsWhiteSpace(_newChar[0]);
		if (HubKit.Button(sb, "rbf:add", new Rectangle(x + width - 74, y, 74, 30), "Add", new Color(60, 140, 80), "Makes a letter for it (or opens the one there is).", can))
		{
			_char = _newChar[0];
			_newChar = "";
			_codeDirty = false;
			Load();
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("Green dot: yours."), x + 90, y + 8, Theme.Muted * 0.8f, 0.6f, width - fieldWidth - 180);
		y += 38;
		List<char> chars = RainbowFont.Characters;
		if (!chars.Contains(_char))
		{
			chars.Add(_char);
		}
		const int cell = 46;
		int columns = Math.Max(1, (width + 4) / (cell + 4));
		for (int i = 0; i < chars.Count; i++)
		{
			char c = chars[i];
			Rectangle r = new Rectangle(x + (i % columns) * (cell + 4), y + (i / columns) * (cell + 4), cell, cell);
			bool on = c == _char;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : (hover ? Theme.CardHover : Theme.Card)) * HubPrefs.Opacity);
			// Its drawing in the top part, the character itself, large, under it.
			RainbowGlyph g = (c == _char) ? _glyph : RainbowFont.Get(c);
			if (g != null && g.Shapes.Count > 0)
			{
				DrawStrokes(sb, g.Shapes, new Vector2(r.Center.X, r.Y + 17), 26f / (Half * 2f), 1.2f, null);
			}
			string label = c.ToString();
			float labelWidth = HubKit.Measure(label, 0.72f);
			HubKit.RawText(sb, label, r.Center.X - labelWidth / 2f, r.Bottom - 19, Color.White, 0.72f);
			if (RainbowFont.IsMine(c))
			{
				HubKit.Fill(sb, new Rectangle(r.Right - 8, r.Y + 4, 5, 5), Theme.On);
			}
			if (hover && HubInput.Hover("rbf:char:" + (int)c, "'" + c + "'  " + (RainbowFont.IsMine(c) ? IsaClient.Languages.L10n.T("yours") : IsaClient.Languages.L10n.T("built-in"))))
			{
				_char = c;
				_codeDirty = false;
				Load();
				HubInput.Consume();
			}
		}
		return y + ((chars.Count + columns - 1) / columns) * (cell + 4);
	}

	#endregion

	#region Editor

	/// <summary>Tools, how the canvas looks, the code switch, and undo / redo, in one row.</summary>
	private static int Toolbar(SpriteBatch sb, int x, int y, int width)
	{
		(Tool Tool, int Icon, string Tip)[] tools =
		{
			(Tool.Select, I.Wrench, "Select: click or box strokes (Shift adds).\nDrag to move them; a lone stroke's handles reshape it.\nArrows nudge, Delete removes, Ctrl+D duplicates,\nCtrl+C / Ctrl+V copy and paste, Ctrl+A picks all."),
			(Tool.Pencil, I.Paintbrush, "Pencil: draw freely.\nWhile you draw it becomes the lines and arcs closest to your hand."),
			(Tool.Line, I.Ruler, "Line: drag from one end to the other."),
			(Tool.Arc, I.Sickle, "Arc: drag from one end to the other,\nthen move the mouse to bend it and click."),
			(Tool.Circle, I.BandofRegeneration, "Circle: drag from the middle out."),
			(Tool.Dot, I.FallenStar, "Dot: click to place one rainbow (drag to aim it)."),
			(Tool.Erase, I.PaintScraper, "Erase: click a stroke, or drag across strokes, to take them out.")
		};
		int bx = x;
		foreach ((Tool tool, int icon, string tip) in tools)
		{
			if (HubKit.IconButton(sb, "rbf:tool:" + tool, new Rectangle(bx, y, 36, 36), HubIcon.Item(icon), tip, _tool == tool))
			{
				_tool = tool;
				Cancel();
			}
			bx += 40;
		}
		bx += 10;
		if (HubKit.IconButton(sb, "rbf:snap", new Rectangle(bx, y, 36, 36), HubIcon.Item(I.WireCutter), "Snap to the grid (every " + Grid + " units): " + (_snap ? "on" : "off") + ".", _snap))
		{
			_snap = !_snap;
		}
		bx += 40;
		string viewTip = _view switch
		{
			View.Game => "View: as in the game (the real rainbows). Click: strokes.",
			View.Strokes => "View: strokes only. Click: both.",
			_ => "View: the rainbows with the strokes over them. Click: as in the game."
		};
		if (HubKit.IconButton(sb, "rbf:view", new Rectangle(bx, y, 36, 36), HubIcon.Item((_view == View.Strokes) ? I.Ruler : I.RainbowRod), viewTip, _view != View.Strokes))
		{
			_view = (View)(((int)_view + 1) % 3);
		}
		bx += 40;
		if (HubKit.IconButton(sb, "rbf:code", new Rectangle(bx, y, 36, 36), HubIcon.Item(I.Book), _codeOpen ? "Back to the actions." : "Code: the letter as XML, colored,\nbeside the canvas, which shows it live.", _codeOpen))
		{
			_codeOpen = !_codeOpen;
			_codeDirty = false;
		}
		int right = x + width;
		if (HubKit.Button(sb, "rbf:redo", new Rectangle(right - 80, y + 3, 80, 30), "Redo", Theme.Slot, "Ctrl+Y", _redo.Count > 0))
		{
			Redo();
		}
		if (HubKit.Button(sb, "rbf:undo", new Rectangle(right - 166, y + 3, 80, 30), "Undo", Theme.Slot, "Ctrl+Z", _undo.Count > 0))
		{
			Undo();
		}
		return y + 46;
	}

	/// <summary>The letter, what's selected, and what to do with it.</summary>
	private static int Side(SpriteBatch sb, int x, int y, int width)
	{
		bool mine = RainbowFont.IsMine(_char);
		int rainbows = RainbowFont.Points(_glyph, center: false).Count;
		Rectangle card = new Rectangle(x, y, width, 104);
		HubKit.Box(sb, card, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, "'" + _char + "'", card.X + 12, card.Y + 8, Color.White, 1.1f);
		HubKit.Chip(sb, _codeDirty ? "Not applied" : (mine ? "Yours" : (RainbowFont.HasBuiltIn(_char) ? "Built-in" : "New")), card.X + 58, card.Y + 10, _codeDirty ? new Color(170, 120, 40) : (mine ? new Color(60, 140, 80) : Theme.Slot), Color.White, 0.66f);
		HubKit.RawText(sb, _glyph.Shapes.Count + " " + IsaClient.Languages.L10n.T("strokes") + ", " + rainbows + " " + IsaClient.Languages.L10n.T("rainbows"), card.X + 12, card.Y + 38, Theme.Muted, 0.66f, width - 24);
		HubKit.RawText(sb, Describe(), card.X + 12, card.Y + 58, new Color(150, 200, 255), 0.62f, width - 24);
		HubKit.RawText(sb, Hint(), card.X + 12, card.Y + 78, new Color(255, 214, 110), 0.62f, width - 24);
		y = card.Bottom + 8;
		int half = (width - 6) / 2;
		bool any = _glyph.Shapes.Count > 0;
		string scope = (_sel.Count > 0) ? "the selected strokes" : "the whole letter";
		if (HubKit.Button(sb, "rbf:mirrorx", new Rectangle(x, y, half, 30), "Flip sideways", Theme.Slot, "Mirrors " + scope + " left to right.", any))
		{
			Edit(() => Mirror(true));
		}
		if (HubKit.Button(sb, "rbf:mirrory", new Rectangle(x + half + 6, y, half, 30), "Flip up-down", Theme.Slot, "Mirrors " + scope + " upside down.", any))
		{
			Edit(() => Mirror(false));
		}
		y += 36;
		if (HubKit.Button(sb, "rbf:center", new Rectangle(x, y, half, 30), "Center", Theme.Slot, "Moves the letter to the middle, sideways (up and down stays).", any))
		{
			Edit(CenterSideways);
		}
		bool selection = _sel.Count > 0;
		if (HubKit.Button(sb, "rbf:clear", new Rectangle(x + half + 6, y, half, 30), selection ? "Delete" : "Clear", new Color(150, 58, 58), selection ? "Takes out the selected strokes (Delete)." : "Takes every stroke out (Undo brings them back).", any))
		{
			if (selection)
			{
				DeleteSelected();
			}
			else
			{
				Edit(() => _glyph.Shapes.Clear());
			}
		}
		y += 36;
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("Width in text") + ": " + (int)_glyph.Advance, x, y + 7, Color.White, 0.7f, width - 70);
		if (HubKit.Button(sb, "rbf:narrow", new Rectangle(x + width - 64, y, 30, 30), "-", Theme.Slot, "Narrower (the next letter comes closer)."))
		{
			Edit(() => _glyph.Advance = Math.Max(20f, _glyph.Advance - 10f));
		}
		if (HubKit.Button(sb, "rbf:wider", new Rectangle(x + width - 30, y, 30, 30), "+", Theme.Slot, "Wider."))
		{
			Edit(() => _glyph.Advance = Math.Min(400f, _glyph.Advance + 10f));
		}
		y += 36;
		if (RainbowFont.HasBuiltIn(_char) && mine)
		{
			if (HubKit.Button(sb, "rbf:reset", new Rectangle(x, y, width, 30), "Back to the built-in one", Theme.Slot, "Drops your version of this letter."))
			{
				RainbowFont.Forget(_char);
				_undo.Clear();
				_redo.Clear();
				_codeDirty = false;
				Load();
			}
			y += 36;
		}
		else if (!RainbowFont.HasBuiltIn(_char) && mine)
		{
			if (HubKit.Button(sb, "rbf:remove", new Rectangle(x, y, width, 30), "Remove this symbol", new Color(150, 58, 58), "Drops this letter from your font."))
			{
				RainbowFont.Forget(_char);
				_char = 'A';
				_codeDirty = false;
				Load();
			}
			y += 36;
		}
		bool world = !Main.gameMenu;
		if (HubKit.Button(sb, "rbf:drawone", new Rectangle(x, y, width, 30), "Write it in the world", new Color(150, 60, 160), world ? "Writes this letter over you with real rainbows." : "Join a world first.", world && any && !RainbowText.Drawing && !_codeDirty, HubIcon.Item(I.RainbowGun)))
		{
			RainbowText.Draw(_char.ToString());
		}
		return y + 36;
	}

	/// <summary>Under the canvas: some text to write with your letters.</summary>
	private static int TryRow(SpriteBatch sb, int x, int y, int width)
	{
		bool world = !Main.gameMenu;
		int button = 150;
		_try = HubKit.Field(sb, "rbf:try", new Rectangle(x, y, width - button - 6, 30), _try, "Try some text...", 60, HubIcon.Item(I.RainbowGun));
		if (HubKit.Button(sb, "rbf:drawtext", new Rectangle(x + width - button, y, button, 30), "Write it", new Color(150, 60, 160), world ? "Writes the text over you, with your letters (.rainbow)." : "Join a world first.", world && _try.Trim().Length > 0 && !RainbowText.Drawing, HubIcon.Item(I.RainbowGun)))
		{
			RainbowText.Draw(_try);
		}
		return y + 36;
	}

	/// <summary>What's selected, in numbers (the lone stroke's own, or how many).</summary>
	private static string Describe()
	{
		if (_sel.Count == 0)
		{
			return IsaClient.Languages.L10n.T("Nothing selected.");
		}
		if (_sel.Count > 1)
		{
			return _sel.Count + " " + IsaClient.Languages.L10n.T("strokes selected.");
		}
		RainbowShape s = _glyph.Shapes[_sel.First()];
		string N(float v) => ((int)Math.Round(v)).ToString();
		string Deg(float r) => ((int)Math.Round(r * 180f / Math.PI)) + "°";
		return s.Kind switch
		{
			RainbowShape.ShapeKind.Line => IsaClient.Languages.L10n.T("Line") + " " + N(s.A.X) + "," + N(s.A.Y) + " → " + N(s.B.X) + "," + N(s.B.Y),
			RainbowShape.ShapeKind.Arc => IsaClient.Languages.L10n.T("Arc") + " " + N(s.A.X) + "," + N(s.A.Y) + "  r " + N(s.Radius) + "  " + Deg(s.Start) + " → " + Deg(s.End),
			_ => IsaClient.Languages.L10n.T("rainbow::Dot") + " " + N(s.A.X) + "," + N(s.A.Y) + "  " + Deg(s.Start)
		};
	}

	private static string Hint()
	{
		if (_bending)
		{
			return IsaClient.Languages.L10n.T("Move to bend the arc, click to place it.");
		}
		return _tool switch
		{
			Tool.Select => IsaClient.Languages.L10n.T("Click or box strokes; drag to move."),
			Tool.Pencil => IsaClient.Languages.L10n.T("Draw: it becomes lines and arcs."),
			Tool.Line => IsaClient.Languages.L10n.T("Drag to draw a line."),
			Tool.Arc => IsaClient.Languages.L10n.T("Drag end to end, then bend."),
			Tool.Circle => IsaClient.Languages.L10n.T("Drag from the middle out."),
			Tool.Dot => IsaClient.Languages.L10n.T("Click for a dot (drag to aim)."),
			_ => IsaClient.Languages.L10n.T("Click or drag across to erase.")
		};
	}

	#endregion

	#region Canvas

	private static float PixelsPerUnit(Rectangle canvas) => canvas.Width / (Half * 2f);

	private static Vector2 Middle(Rectangle canvas) => new Vector2(canvas.Center.X, canvas.Center.Y);

	private static Vector2 ToScreen(Rectangle canvas, Vector2 unit) => Middle(canvas) + unit * PixelsPerUnit(canvas);

	private static Vector2 ToUnits(Rectangle canvas, Point mouse, bool snap)
	{
		Vector2 u = (new Vector2(mouse.X, mouse.Y) - Middle(canvas)) / PixelsPerUnit(canvas);
		return snap ? Snap(u) : u;
	}

	private static Vector2 Snap(Vector2 u) => new Vector2((float)Math.Round(u.X / Grid) * Grid, (float)Math.Round(u.Y / Grid) * Grid);

	private static void Canvas(SpriteBatch sb, Rectangle canvas)
	{
		HubKit.Fill(sb, canvas, new Color(12, 14, 30));
		float ppu = PixelsPerUnit(canvas);
		Vector2 middle = Middle(canvas);
		for (int u = -100; u <= 100; u += 10)
		{
			Color c = (u % 50 == 0) ? new Color(50, 58, 104) : new Color(24, 28, 54);
			HubKit.Fill(sb, new Rectangle((int)(middle.X + u * ppu), canvas.Y, 1, canvas.Height), c);
			HubKit.Fill(sb, new Rectangle(canvas.X, (int)(middle.Y + u * ppu), canvas.Width, 1), c);
		}
		foreach (float guide in new[] { -60f, 60f })
		{
			int py = (int)(middle.Y + guide * ppu);
			for (int px = canvas.X; px < canvas.Right; px += 8)
			{
				HubKit.Fill(sb, new Rectangle(px, py, 4, 1), new Color(255, 214, 110) * 0.45f);
			}
		}
		foreach (float side in new[] { -_glyph.Advance / 2f, _glyph.Advance / 2f })
		{
			int px = (int)(middle.X + side * ppu);
			if (px > canvas.X && px < canvas.Right)
			{
				for (int py = canvas.Y; py < canvas.Bottom; py += 8)
				{
					HubKit.Fill(sb, new Rectangle(px, py, 1, 4), new Color(120, 230, 210) * 0.4f);
				}
			}
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("top"), canvas.X + 4, middle.Y - 60 * ppu - 16, new Color(255, 214, 110) * 0.7f, 0.55f);
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("bottom"), canvas.X + 4, middle.Y + 60 * ppu + 2, new Color(255, 214, 110) * 0.7f, 0.55f);
		Interact(canvas);
		// The letter: as in the game, as strokes, or both.
		if (_view != View.Strokes)
		{
			Rainbows(sb, canvas, _glyph.Shapes, 1f);
		}
		if (_view != View.Game)
		{
			DrawStrokes(sb, _glyph.Shapes, middle, ppu, 2f, null);
		}
		foreach (int i in _sel)
		{
			if (i >= 0 && i < _glyph.Shapes.Count)
			{
				Stroke(sb, _glyph.Shapes[i], middle, ppu, Color.White, (_view == View.Game) ? 1.5f : 3f);
			}
		}
		// What's being drawn, in the same look.
		List<RainbowShape> ghosts = Ghosts(canvas);
		if (ghosts.Count > 0)
		{
			if (_view != View.Strokes)
			{
				Rainbows(sb, canvas, ghosts, 0.9f);
			}
			foreach (RainbowShape g in ghosts)
			{
				Stroke(sb, g, middle, ppu, Color.White * ((_view == View.Strokes) ? 0.9f : 0.45f), (_view == View.Strokes) ? 2f : 1f);
			}
		}
		if (_ink != null && _ink.Count > 1)
		{
			for (int i = 1; i < _ink.Count; i++)
			{
				Segment(sb, ToScreen(canvas, _ink[i - 1]), ToScreen(canvas, _ink[i]), Color.White * 0.3f, 1f);
			}
		}
		if (_boxing)
		{
			Rectangle box = Box(canvas);
			HubKit.Fill(sb, box, new Color(120, 170, 255) * 0.12f);
			Outline(sb, box, new Color(150, 200, 255) * 0.8f);
		}
		if (_tool == Tool.Select && _sel.Count == 1)
		{
			int only = _sel.First();
			if (only < _glyph.Shapes.Count)
			{
				foreach (Vector2 handle in Handles(_glyph.Shapes[only]))
				{
					Vector2 p = ToScreen(canvas, handle);
					HubKit.Fill(sb, new Rectangle((int)p.X - 5, (int)p.Y - 5, 10, 10), Color.White);
					HubKit.Fill(sb, new Rectangle((int)p.X - 3, (int)p.Y - 3, 6, 6), new Color(73, 94, 171));
				}
			}
		}
		if (HubInput.In(canvas))
		{
			Vector2 u = ToUnits(canvas, HubInput.Mouse, _snap);
			HubKit.RawText(sb, (int)u.X + ", " + (int)u.Y, canvas.Right - 64, canvas.Bottom - 18, Color.White * 0.6f, 0.55f);
		}
		Outline(sb, canvas, Theme.Slot);
	}

	/// <summary>
	/// The rainbows these strokes make, drawn as the game draws them: the rainbow sprite (projectile 251) at each one,
	/// turned along its stroke, at its scale and offset, glowing (added light).
	/// </summary>
	private static void Rainbows(SpriteBatch sb, Rectangle canvas, List<RainbowShape> shapes, float alpha)
	{
		List<Vector4> points = RainbowFont.Points(new RainbowGlyph { Shapes = shapes }, center: false);
		if (points.Count == 0)
		{
			return;
		}
		Main.instance.LoadProjectile(RainbowProjectile);
		Texture2D tex = TextureAssets.Projectile[RainbowProjectile].Value;
		float ppu = PixelsPerUnit(canvas);
		// As Main.DrawProj draws projectile 251: at its middle + (width / 2 - 17, 0), origin (width / 2, 25), scale 1.25;
		// its AI turns it to the way it moves minus 90 degrees and flips it when it moves right (AI style 46).
		Vector2 offset = new Vector2(tex.Width / 2f - 17f, 0f) * ppu;
		Vector2 origin = new Vector2(tex.Width / 2f, 25f);
		Color glow = new Color(255, 255, 255, 0) * alpha;
		foreach (Vector4 p in points)
		{
			float rotation = (float)Math.Atan2(p.W, p.Z) - 1.57f;
			SpriteEffects flip = (p.Z > 0f) ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			sb.Draw(tex, ToScreen(canvas, new Vector2(p.X, p.Y)) + offset, null, glow, rotation, origin, 1.25f * ppu, flip, 0f);
		}
	}

	/// <summary>Strokes in rainbow colors (or one color).</summary>
	private static void DrawStrokes(SpriteBatch sb, List<RainbowShape> shapes, Vector2 middle, float ppu, float thickness, Color? color)
	{
		for (int i = 0; i < shapes.Count; i++)
		{
			Stroke(sb, shapes[i], middle, ppu, color ?? Main.hslToRgb(i / (float)Math.Max(1, shapes.Count) * 0.85f, 0.9f, 0.62f), thickness);
		}
	}

	private static void Stroke(SpriteBatch sb, RainbowShape s, Vector2 middle, float ppu, Color c, float t)
	{
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
			Segment(sb, middle + s.A * ppu, middle + s.B * ppu, c, t);
			break;
		case RainbowShape.ShapeKind.Arc:
		{
			int steps = Math.Max(6, (int)(Math.Abs(s.End - s.Start) * s.Radius * ppu / 6f));
			Vector2 last = middle + s.At(s.Start) * ppu;
			for (int k = 1; k <= steps; k++)
			{
				Vector2 next = middle + s.At(s.Start + (s.End - s.Start) * k / steps) * ppu;
				Segment(sb, last, next, c, t);
				last = next;
			}
			break;
		}
		default:
		{
			Vector2 p = middle + s.A * ppu;
			int r = (int)Math.Max(2f, t + 1.5f);
			HubKit.Fill(sb, new Rectangle((int)p.X - r, (int)p.Y - r, r * 2, r * 2), c);
			Segment(sb, p, p + new Vector2((float)Math.Cos(s.Start), (float)Math.Sin(s.Start)) * 8f * ppu, c * 0.6f, Math.Max(1f, t - 1f));
			break;
		}
		}
	}

	private static void Segment(SpriteBatch sb, Vector2 a, Vector2 b, Color c, float thickness)
	{
		Vector2 d = b - a;
		float length = d.Length();
		if (length < 0.5f)
		{
			return;
		}
		sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), c, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
	}

	private static void Outline(SpriteBatch sb, Rectangle r, Color c)
	{
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, r.Width, 1), c);
		HubKit.Fill(sb, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, 1, r.Height), c);
		HubKit.Fill(sb, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
	}

	/// <summary>What's being drawn right now, before it's placed.</summary>
	private static List<RainbowShape> Ghosts(Rectangle canvas)
	{
		Vector2 at = ToUnits(canvas, HubInput.Mouse, _snap);
		if (_bending)
		{
			return new List<RainbowShape> { ArcThrough(_arcA, at, _arcB) ?? RainbowShape.Line(_arcA, _arcB) };
		}
		if (_ink != null)
		{
			return _inkShapes;
		}
		if (!_dragging)
		{
			return new List<RainbowShape>();
		}
		RainbowShape ghost = _tool switch
		{
			Tool.Line => RainbowShape.Line(_dragStart, at),
			Tool.Arc => RainbowShape.Line(_dragStart, at),
			Tool.Circle => RainbowShape.Arc(_dragStart, Vector2.Distance(_dragStart, at), 0f, (float)Math.PI * 2f),
			Tool.Dot => RainbowShape.Dot(_dragStart, (Vector2.Distance(_dragStart, at) > 3f) ? (float)Math.Atan2(at.Y - _dragStart.Y, at.X - _dragStart.X) : 0f),
			_ => null
		};
		return (ghost != null) ? new List<RainbowShape> { ghost } : new List<RainbowShape>();
	}

	private static Rectangle Box(Rectangle canvas)
	{
		Vector2 a = ToScreen(canvas, _dragStart);
		Vector2 b = new Vector2(HubInput.Mouse.X, HubInput.Mouse.Y);
		return new Rectangle((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Abs(a.X - b.X), (int)Math.Abs(a.Y - b.Y));
	}

	#endregion

	#region Mouse and keys

	private static bool Shift => Main.keyState.IsKeyDown(XKeys.LeftShift) || Main.keyState.IsKeyDown(XKeys.RightShift);

	private static void Interact(Rectangle canvas)
	{
		bool inside = HubInput.In(canvas);
		bool down = HubInput.Down;
		bool pressed = down && !_wasDown && inside;
		bool released = !down && _wasDown;
		_wasDown = down;
		Shortcuts();
		Vector2 at = ToUnits(canvas, HubInput.Mouse, _snap);
		Vector2 raw = ToUnits(canvas, HubInput.Mouse, snap: false);
		if ((_bending || _dragging || _ink != null || _boxing) && (HubInput.RightClicked || Main.keyState.IsKeyDown(XKeys.Escape)))
		{
			Cancel();
			HubInput.Consume();
			return;
		}
		if (inside && (pressed || down))
		{
			HubInput.Consume();
		}
		if (_bending)
		{
			if (pressed)
			{
				RainbowShape arc = ArcThrough(_arcA, at, _arcB) ?? RainbowShape.Line(_arcA, _arcB);
				Edit(() => _glyph.Shapes.Add(arc));
				_bending = false;
			}
			return;
		}
		switch (_tool)
		{
		case Tool.Select:
			SelectTool(canvas, pressed, down, released, at, raw);
			return;
		case Tool.Erase:
			EraseTool(canvas, pressed, down, released, raw);
			return;
		case Tool.Pencil:
			PencilTool(pressed, down, released, raw);
			return;
		}
		if (pressed)
		{
			_dragging = true;
			_dragStart = at;
		}
		else if (_dragging && released)
		{
			_dragging = false;
			float length = Vector2.Distance(_dragStart, at);
			Vector2 a = _dragStart;
			switch (_tool)
			{
			case Tool.Line:
				if (length >= 2f)
				{
					Edit(() => _glyph.Shapes.Add(RainbowShape.Line(a, at)));
				}
				break;
			case Tool.Arc:
				if (length >= 2f)
				{
					_arcA = a;
					_arcB = at;
					_bending = true;
				}
				break;
			case Tool.Circle:
				if (length >= 2f)
				{
					Edit(() => _glyph.Shapes.Add(RainbowShape.Arc(a, length, 0f, (float)Math.PI * 2f)));
				}
				break;
			case Tool.Dot:
			{
				float direction = (length > 3f) ? (float)Math.Atan2(at.Y - a.Y, at.X - a.X) : 0f;
				Edit(() => _glyph.Shapes.Add(RainbowShape.Dot(a, direction)));
				break;
			}
			}
		}
	}

	private static void SelectTool(Rectangle canvas, bool pressed, bool down, bool released, Vector2 at, Vector2 raw)
	{
		if (pressed)
		{
			_handle = -2;
			// A handle of the lone selected stroke first.
			if (_sel.Count == 1)
			{
				int only = _sel.First();
				List<Vector2> handles = Handles(_glyph.Shapes[only]);
				float near = 7f / PixelsPerUnit(canvas);
				for (int h = 0; h < handles.Count; h++)
				{
					if (Vector2.Distance(handles[h], raw) <= near)
					{
						_handle = h;
					}
				}
			}
			if (_handle == -2)
			{
				int hit = HitTest(raw, canvas);
				if (hit >= 0)
				{
					if (Shift)
					{
						// Shift: add or take out, no drag.
						if (!_sel.Remove(hit))
						{
							_sel.Add(hit);
						}
						return;
					}
					if (!_sel.Contains(hit))
					{
						_sel.Clear();
						_sel.Add(hit);
					}
					_handle = -1;
				}
				else
				{
					if (!Shift)
					{
						_sel.Clear();
					}
					_boxing = true;
					_dragStart = raw;
					return;
				}
			}
			Snapshot();
			_dragOriginals = _sel.Where(i => i < _glyph.Shapes.Count).ToDictionary(i => i, i => _glyph.Shapes[i].Clone());
			_dragStart = at;
			_dragging = true;
			return;
		}
		if (_boxing)
		{
			if (released)
			{
				_boxing = false;
				Vector2 a = _dragStart;
				Vector2 lo = new Vector2(Math.Min(a.X, raw.X), Math.Min(a.Y, raw.Y));
				Vector2 hi = new Vector2(Math.Max(a.X, raw.X), Math.Max(a.Y, raw.Y));
				if ((hi - lo).Length() > 1f)
				{
					// Every stroke with a point of it inside the box.
					for (int i = 0; i < _glyph.Shapes.Count; i++)
					{
						if (Samples(_glyph.Shapes[i]).Any(p => p.X >= lo.X && p.X <= hi.X && p.Y >= lo.Y && p.Y <= hi.Y))
						{
							_sel.Add(i);
						}
					}
				}
			}
			return;
		}
		if (_dragging && down && _dragOriginals != null)
		{
			foreach (KeyValuePair<int, RainbowShape> o in _dragOriginals)
			{
				_glyph.Shapes[o.Key] = Dragged(o.Value, _handle, at - _dragStart, at);
			}
			return;
		}
		if (_dragging && released)
		{
			_dragging = false;
			bool moved = _dragOriginals != null && _dragOriginals.Any(o => !Same(o.Value, _glyph.Shapes[o.Key]));
			if (moved)
			{
				Commit();
			}
			else
			{
				// Only clicked: nothing to keep in the undo list.
				_undo.RemoveAt(_undo.Count - 1);
			}
			_dragOriginals = null;
		}
	}

	private static void EraseTool(Rectangle canvas, bool pressed, bool down, bool released, Vector2 raw)
	{
		if (pressed)
		{
			Snapshot();
			_dragging = true;
		}
		if (_dragging && down)
		{
			int hit = HitTest(raw, canvas);
			if (hit >= 0)
			{
				_glyph.Shapes.RemoveAt(hit);
				_sel.Clear();
			}
		}
		if (_dragging && released)
		{
			_dragging = false;
			if (_undo.Count > 0 && _undo[_undo.Count - 1].Shapes.Count == _glyph.Shapes.Count)
			{
				_undo.RemoveAt(_undo.Count - 1);
			}
			else
			{
				Commit();
			}
		}
	}

	private static void PencilTool(bool pressed, bool down, bool released, Vector2 raw)
	{
		if (pressed)
		{
			_ink = new List<Vector2> { raw };
			_inkShapes = new List<RainbowShape>();
			return;
		}
		if (_ink == null)
		{
			return;
		}
		if (down)
		{
			if (Vector2.Distance(_ink[_ink.Count - 1], raw) >= 1.5f)
			{
				_ink.Add(raw);
				_inkShapes = Fit(_ink);
			}
			return;
		}
		if (released)
		{
			List<RainbowShape> shapes = Fit(_ink);
			_ink = null;
			_inkShapes = new List<RainbowShape>();
			if (shapes.Count > 0)
			{
				Edit(() => _glyph.Shapes.AddRange(shapes));
			}
		}
	}

	private static bool Same(RainbowShape a, RainbowShape b) => a.A == b.A && a.B == b.B && a.Radius == b.Radius && a.Start == b.Start && a.End == b.End;

	/// <summary>The grab points of a stroke: line ends; arc middle, start, end, and its bend (radius); a dot.</summary>
	private static List<Vector2> Handles(RainbowShape s)
	{
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
			return new List<Vector2> { s.A, s.B };
		case RainbowShape.ShapeKind.Arc:
			return new List<Vector2> { s.A, s.At(s.Start), s.At(s.End), s.At((s.Start + s.End) / 2f) };
		default:
			return new List<Vector2> { s.A };
		}
	}

	/// <summary>Points along a stroke (for box selection).</summary>
	private static IEnumerable<Vector2> Samples(RainbowShape s)
	{
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
			for (int k = 0; k <= 16; k++)
			{
				yield return Vector2.Lerp(s.A, s.B, k / 16f);
			}
			break;
		case RainbowShape.ShapeKind.Arc:
			for (int k = 0; k <= 32; k++)
			{
				yield return s.At(s.Start + (s.End - s.Start) * k / 32f);
			}
			break;
		default:
			yield return s.A;
			break;
		}
	}

	/// <summary>A stroke as it is while it (-1) or one of its handles is dragged.</summary>
	private static RainbowShape Dragged(RainbowShape original, int handle, Vector2 delta, Vector2 at)
	{
		RainbowShape s = original.Clone();
		if (handle == -1)
		{
			s.A += delta;
			s.B += delta;
			return s;
		}
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
			if (handle == 0)
			{
				s.A = at;
			}
			else
			{
				s.B = at;
			}
			break;
		case RainbowShape.ShapeKind.Arc:
			switch (handle)
			{
			case 0:
				s.A = original.A + delta;
				break;
			case 1:
				s.Start = Unwrap((float)Math.Atan2(at.Y - s.A.Y, at.X - s.A.X), original.Start);
				break;
			case 2:
				s.End = Unwrap((float)Math.Atan2(at.Y - s.A.Y, at.X - s.A.X), original.End);
				break;
			default:
				s.Radius = Math.Max(1f, Vector2.Distance(s.A, at));
				break;
			}
			break;
		default:
			s.A = at;
			break;
		}
		return s;
	}

	/// <summary>The same angle, but the turn closest to where it was (so dragging an end doesn't flip the arc round).</summary>
	private static float Unwrap(float angle, float near)
	{
		const float Tau = (float)Math.PI * 2f;
		while (angle - near > Math.PI)
		{
			angle -= Tau;
		}
		while (near - angle > Math.PI)
		{
			angle += Tau;
		}
		return angle;
	}

	/// <summary>The stroke under a point (within a few pixels), or -1.</summary>
	private static int HitTest(Vector2 at, Rectangle canvas)
	{
		float best = 8f / PixelsPerUnit(canvas);
		int found = -1;
		for (int i = _glyph.Shapes.Count - 1; i >= 0; i--)
		{
			float d = Distance(_glyph.Shapes[i], at);
			if (d < best)
			{
				best = d;
				found = i;
			}
		}
		return found;
	}

	private static float Distance(RainbowShape s, Vector2 p)
	{
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
		{
			Vector2 d = s.B - s.A;
			float t = (d.LengthSquared() < 0.001f) ? 0f : MathHelper.Clamp(Vector2.Dot(p - s.A, d) / d.LengthSquared(), 0f, 1f);
			return Vector2.Distance(p, s.A + d * t);
		}
		case RainbowShape.ShapeKind.Arc:
		{
			float best = float.MaxValue;
			for (int k = 0; k <= 48; k++)
			{
				best = Math.Min(best, Vector2.Distance(p, s.At(s.Start + (s.End - s.Start) * k / 48f)));
			}
			return best;
		}
		default:
			return Vector2.Distance(p, s.A);
		}
	}

	/// <summary>The arc from a to b passing through m (null when the three are in a line).</summary>
	private static RainbowShape ArcThrough(Vector2 a, Vector2 m, Vector2 b)
	{
		float d = 2f * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
		if (Math.Abs(d) < 0.5f)
		{
			return null;
		}
		float a2 = a.LengthSquared();
		float m2 = m.LengthSquared();
		float b2 = b.LengthSquared();
		Vector2 c = new Vector2((a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d, (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d);
		float radius = Vector2.Distance(c, a);
		if (radius > 2000f)
		{
			return null;
		}
		const float Tau = (float)Math.PI * 2f;
		float start = (float)Math.Atan2(a.Y - c.Y, a.X - c.X);
		float through = (float)Math.Atan2(m.Y - c.Y, m.X - c.X);
		float end = (float)Math.Atan2(b.Y - c.Y, b.X - c.X);
		float sweep = ((end - start) % Tau + Tau) % Tau;
		float toThrough = ((through - start) % Tau + Tau) % Tau;
		// Round the way that passes through the mouse.
		return RainbowShape.Arc(c, radius, start, (toThrough <= sweep) ? (start + sweep) : (start - (Tau - sweep)));
	}

	private static void Shortcuts()
	{
		KeyboardState keys = Main.keyState;
		bool Press(XKeys k) => keys.IsKeyDown(k) && !_oldKeys.IsKeyDown(k);
		if (HubInput.Focus == null && !(_codeOpen && _code != null && _code.Focused))
		{
			bool ctrl = keys.IsKeyDown(XKeys.LeftControl) || keys.IsKeyDown(XKeys.RightControl);
			if (ctrl && Press(XKeys.Z))
			{
				Undo();
			}
			else if (ctrl && Press(XKeys.Y))
			{
				Redo();
			}
			else if (ctrl && Press(XKeys.A))
			{
				_tool = Tool.Select;
				_sel.Clear();
				for (int i = 0; i < _glyph.Shapes.Count; i++)
				{
					_sel.Add(i);
				}
			}
			else if (ctrl && Press(XKeys.C) && _sel.Count > 0)
			{
				_copied = _sel.Where(i => i < _glyph.Shapes.Count).Select(i => _glyph.Shapes[i].Clone()).ToList();
			}
			else if (ctrl && Press(XKeys.V) && _copied.Count > 0)
			{
				Paste(_copied, Vector2.Zero);
			}
			else if (ctrl && Press(XKeys.D) && _sel.Count > 0)
			{
				Paste(_sel.Where(i => i < _glyph.Shapes.Count).Select(i => _glyph.Shapes[i]).ToList(), new Vector2(Grid * 2f));
			}
			else if (Press(XKeys.Delete) && _sel.Count > 0)
			{
				DeleteSelected();
			}
			else if (_sel.Count > 0 && _tool == Tool.Select)
			{
				float step = _snap ? Grid : 1f;
				Vector2 nudge = Press(XKeys.Left) ? new Vector2(-step, 0f) : Press(XKeys.Right) ? new Vector2(step, 0f) : Press(XKeys.Up) ? new Vector2(0f, -step) : Press(XKeys.Down) ? new Vector2(0f, step) : Vector2.Zero;
				if (nudge != Vector2.Zero)
				{
					Edit(delegate
					{
						foreach (int i in _sel.Where(i => i < _glyph.Shapes.Count))
						{
							_glyph.Shapes[i].A += nudge;
							_glyph.Shapes[i].B += nudge;
						}
					});
				}
			}
		}
		_oldKeys = keys;
	}

	/// <summary>Copies of strokes added (moved a little), and selected.</summary>
	private static void Paste(List<RainbowShape> shapes, Vector2 offset)
	{
		List<RainbowShape> copies = shapes.Select(s =>
		{
			RainbowShape c = s.Clone();
			c.A += offset;
			c.B += offset;
			return c;
		}).ToList();
		Edit(() => _glyph.Shapes.AddRange(copies));
		_sel.Clear();
		for (int i = _glyph.Shapes.Count - copies.Count; i < _glyph.Shapes.Count; i++)
		{
			_sel.Add(i);
		}
		_tool = Tool.Select;
	}

	private static void DeleteSelected()
	{
		List<int> gone = _sel.Where(i => i < _glyph.Shapes.Count).OrderByDescending(i => i).ToList();
		Edit(delegate
		{
			foreach (int i in gone)
			{
				_glyph.Shapes.RemoveAt(i);
			}
		});
		_sel.Clear();
	}

	private static void Cancel()
	{
		_bending = false;
		_boxing = false;
		_ink = null;
		_inkShapes = new List<RainbowShape>();
		if (_dragging && _tool == Tool.Select && _dragOriginals != null)
		{
			foreach (KeyValuePair<int, RainbowShape> o in _dragOriginals)
			{
				_glyph.Shapes[o.Key] = o.Value;
			}
			if (_undo.Count > 0)
			{
				_undo.RemoveAt(_undo.Count - 1);
			}
		}
		_dragging = false;
		_dragOriginals = null;
	}

	#endregion

	#region Pencil fitting

	/// <summary>How far (units) a stroke may stray from what the hand drew.</summary>
	private const float LineTolerance = 2.5f;

	private const float ArcTolerance = 3f;

	/// <summary>
	/// A hand-drawn path as lines and arcs: a closed loop that fits a circle is a circle; otherwise each piece is a line
	/// if it's straight enough, else an arc if it fits one, else it's split where it strays most from straight (the
	/// Ramer-Douglas-Peucker point) and each half is tried again. A tap is a dot.
	/// </summary>
	private static List<RainbowShape> Fit(List<Vector2> ink)
	{
		List<RainbowShape> shapes = new List<RainbowShape>();
		if (ink.Count == 0)
		{
			return shapes;
		}
		float length = 0f;
		for (int i = 1; i < ink.Count; i++)
		{
			length += Vector2.Distance(ink[i - 1], ink[i]);
		}
		if (length < 3f)
		{
			shapes.Add(RainbowShape.Dot(_snap ? Snap(ink[0]) : ink[0]));
			return shapes;
		}
		if (ink.Count >= 8 && Vector2.Distance(ink[0], ink[ink.Count - 1]) < Math.Max(6f, length * 0.08f) && CircleFit(ink, 0, ink.Count - 1, out Vector2 c, out float r, out float error) && error <= ArcTolerance * 1.5f && r > 3f)
		{
			shapes.Add(RainbowShape.Arc(Round(c), Round(r), 0f, (float)Math.PI * 2f));
			return shapes;
		}
		Piece(ink, 0, ink.Count - 1, shapes, 0);
		return shapes;
	}

	private static void Piece(List<Vector2> ink, int from, int to, List<RainbowShape> shapes, int depth)
	{
		if (One(ink, from, to, out RainbowShape whole, out _))
		{
			shapes.Add(whole);
			return;
		}
		if (to - from < 6 || depth > 12)
		{
			shapes.Add(LineOf(ink[from], ink[to]));
			return;
		}
		// The split where each half is one clean line or arc (an S splits where it turns the other way).
		int step = Math.Max(1, (to - from) / 48);
		int best = -1;
		float bestError = float.MaxValue;
		RainbowShape bestA = null;
		RainbowShape bestB = null;
		for (int k = from + 3; k <= to - 3; k += step)
		{
			if (One(ink, from, k, out RainbowShape first, out float e1) && One(ink, k, to, out RainbowShape second, out float e2) && e1 + e2 < bestError)
			{
				bestError = e1 + e2;
				best = k;
				bestA = first;
				bestB = second;
			}
		}
		if (best >= 0)
		{
			shapes.Add(bestA);
			shapes.Add(bestB);
			return;
		}
		// Else split where it strays most from straight (Ramer-Douglas-Peucker) and try each half.
		Vector2 a = ink[from];
		Vector2 b = ink[to];
		int far = from + 1;
		float farDistance = 0f;
		for (int i = from + 1; i < to; i++)
		{
			float d = DistanceToLine(ink[i], a, b);
			if (d > farDistance)
			{
				farDistance = d;
				far = i;
			}
		}
		Piece(ink, from, far, shapes, depth + 1);
		Piece(ink, far, to, shapes, depth + 1);
	}

	private static RainbowShape LineOf(Vector2 a, Vector2 b) => RainbowShape.Line(_snap ? Snap(a) : Round(a), _snap ? Snap(b) : Round(b));

	/// <summary>A piece of the path as one stroke: a line if it's straight enough, else an arc if it fits one (and how far off it is).</summary>
	private static bool One(List<Vector2> ink, int from, int to, out RainbowShape shape, out float error)
	{
		shape = null;
		error = 0f;
		Vector2 a = ink[from];
		Vector2 b = ink[to];
		for (int i = from + 1; i < to; i++)
		{
			error = Math.Max(error, DistanceToLine(ink[i], a, b));
		}
		if (error <= LineTolerance)
		{
			shape = LineOf(a, b);
			return true;
		}
		if (to - from >= 5 && CircleFit(ink, from, to, out Vector2 center, out float radius, out float arcError) && arcError <= ArcTolerance && radius < 400f)
		{
			float sweep = Sweep(ink, from, to, center);
			if (Math.Abs(sweep) > 0.25f && Math.Abs(sweep) < Math.PI * 2f + 0.2f)
			{
				float start = (float)Math.Atan2(a.Y - center.Y, a.X - center.X);
				shape = RainbowShape.Arc(Round(center), Round(radius), start, start + sweep);
				error = arcError;
				return true;
			}
		}
		return false;
	}

	private static Vector2 Round(Vector2 v) => new Vector2((float)Math.Round(v.X * 2f) / 2f, (float)Math.Round(v.Y * 2f) / 2f);

	private static float Round(float v) => (float)Math.Round(v * 2f) / 2f;

	private static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 d = b - a;
		float t = (d.LengthSquared() < 0.001f) ? 0f : MathHelper.Clamp(Vector2.Dot(p - a, d) / d.LengthSquared(), 0f, 1f);
		return Vector2.Distance(p, a + d * t);
	}

	/// <summary>The circle closest to points (algebraic least squares, Kasa's method), and how far the worst point is from it.</summary>
	private static bool CircleFit(List<Vector2> ink, int from, int to, out Vector2 center, out float radius, out float error)
	{
		center = Vector2.Zero;
		radius = 0f;
		error = float.MaxValue;
		double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sz = 0, sxz = 0, syz = 0;
		int n = to - from + 1;
		for (int i = from; i <= to; i++)
		{
			double x = ink[i].X;
			double y = ink[i].Y;
			double z = x * x + y * y;
			sx += x;
			sy += y;
			sxx += x * x;
			syy += y * y;
			sxy += x * y;
			sz += z;
			sxz += x * z;
			syz += y * z;
		}
		// [sxx sxy sx; sxy syy sy; sx sy n] [D E F] = -[sxz syz sz]
		double det = sxx * (syy * n - sy * sy) - sxy * (sxy * n - sy * sx) + sx * (sxy * sy - syy * sx);
		if (Math.Abs(det) < 1e-9)
		{
			return false;
		}
		double r1 = -sxz;
		double r2 = -syz;
		double r3 = -sz;
		double dD = (r1 * (syy * n - sy * sy) - sxy * (r2 * n - sy * r3) + sx * (r2 * sy - syy * r3)) / det;
		double dE = (sxx * (r2 * n - sy * r3) - r1 * (sxy * n - sy * sx) + sx * (sxy * r3 - r2 * sx)) / det;
		double dF = (sxx * (syy * r3 - r2 * sy) - sxy * (sxy * r3 - r2 * sx) + r1 * (sxy * sy - syy * sx)) / det;
		double cx = -dD / 2;
		double cy = -dE / 2;
		double rr = cx * cx + cy * cy - dF;
		if (rr <= 0)
		{
			return false;
		}
		center = new Vector2((float)cx, (float)cy);
		radius = (float)Math.Sqrt(rr);
		error = 0f;
		for (int i = from; i <= to; i++)
		{
			error = Math.Max(error, Math.Abs(Vector2.Distance(ink[i], center) - radius));
		}
		return true;
	}

	/// <summary>How far round a center the points go (signed: which way the hand turned).</summary>
	private static float Sweep(List<Vector2> ink, int from, int to, Vector2 center)
	{
		float total = 0f;
		float last = (float)Math.Atan2(ink[from].Y - center.Y, ink[from].X - center.X);
		for (int i = from + 1; i <= to; i++)
		{
			float angle = (float)Math.Atan2(ink[i].Y - center.Y, ink[i].X - center.X);
			float step = angle - last;
			while (step > Math.PI)
			{
				step -= (float)Math.PI * 2f;
			}
			while (step < -Math.PI)
			{
				step += (float)Math.PI * 2f;
			}
			total += step;
			last = angle;
		}
		return total;
	}

	#endregion

	#region Code

	/// <summary>The letter as RainbowLetters XML: colored, edited by hand, shown live on the canvas until applied.</summary>
	private static int Code(SpriteBatch sb, int x, int y, int width, int height)
	{
		_code ??= new HubTextArea("rbf:codearea") { MaxLines = 18, MaxLength = 20000, Scale = 0.66f, Highlight = Highlight };
		string current = Xml(_glyph);
		if (!_codeDirty && _codeShown != current && !_code.Focused)
		{
			_code.SetText(current);
			_codeShown = current;
			_codeError = null;
		}
		HubKit.Text(sb, "Code", x, y, Theme.Muted, 0.72f);
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("Enter applies. Shift+Enter: new line."), x + 50, y + 2, Theme.Muted * 0.8f, 0.6f, width - 50);
		y += 22;
		string before = _code.Text;
		// As tall as the canvas (less the title and the buttons), so you write while you look at it.
		int boxHeight = Math.Max(160, height - 22 - 42 - ((_codeError != null) ? 20 : 0));
		_code.MaxLines = Math.Max(6, (boxHeight - 10) / 16);
		Rectangle box = new Rectangle(x, y, width, boxHeight);
		// Once you've clicked into it, the wheel scrolls the code instead of the page.
		if (_code.Focused)
		{
			HubInput.InnerScroll = box;
		}
		_code.Draw(sb, box);
		if (_code.Text != before)
		{
			// Live: what the text says shows on the canvas (not saved until applied).
			_codeDirty = true;
			try
			{
				List<RainbowGlyph> parsed = RainbowFont.Parse(_code.Text);
				if (parsed.Count == 0)
				{
					_codeError = IsaClient.Languages.L10n.T("There's no <char> in it.");
				}
				else
				{
					RainbowGlyph g = parsed[0];
					g.Char = _char;
					_glyph = g;
					_sel.Clear();
					_codeError = null;
				}
			}
			catch (Exception ex)
			{
				_codeError = ex.Message;
			}
		}
		y += boxHeight + 6;
		if (_codeError != null)
		{
			HubKit.RawText(sb, _codeError, x, y, Theme.Danger, 0.62f, width);
			y += 20;
		}
		int third = (width - 12) / 3;
		bool can = _codeDirty && _codeError == null;
		if (HubKit.Button(sb, "rbf:apply", new Rectangle(x, y, third, 30), "Apply", new Color(60, 140, 80), "Saves the letter as the code says (Enter).", can) || (_code.Submitted && can))
		{
			RainbowGlyph applied = _glyph;
			_glyph = RainbowFont.Get(_char)?.Clone() ?? new RainbowGlyph { Char = _char };
			Snapshot();
			_glyph = applied;
			_codeDirty = false;
			Commit();
			_codeShown = null;
		}
		if (HubKit.Button(sb, "rbf:revert", new Rectangle(x + third + 6, y, third, 30), "Revert", Theme.Slot, "Back to the letter as it's saved.", _codeDirty))
		{
			_codeDirty = false;
			_codeShown = null;
			_codeError = null;
			_loadedVersion = -1;
			Load();
		}
		if (HubKit.Button(sb, "rbf:copycode", new Rectangle(x + (third + 6) * 2, y, third, 30), "Copy", Theme.Slot, "Copies this letter's code."))
		{
			CopyText(_code.Text, "This letter's code is copied.");
		}
		return y + 36;
	}

	private static string Xml(RainbowGlyph g) => RainbowFont.Write(new[] { g }).Replace("\t", "  ").TrimEnd() + "\n";

	private static readonly Color TagColor = new Color(110, 170, 255);

	private static readonly Color AttributeColor = new Color(255, 214, 110);

	private static readonly Color ValueColor = new Color(140, 230, 140);

	private static readonly Color NumberColor = new Color(255, 160, 120);

	private static readonly Color MarkColor = new Color(150, 150, 175);

	/// <summary>XML colors: marks gray, tag names blue, attributes yellow, values green with numbers orange, the character white.</summary>
	private static Color[] Highlight(string text)
	{
		Color[] colors = new Color[text.Length];
		bool inTag = false;
		bool inValue = false;
		bool name = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			Color color = Color.White;
			if (inValue)
			{
				color = (char.IsDigit(c) || c == '.' || c == '-' || c == ',') ? NumberColor : ValueColor;
				if (c == '"')
				{
					inValue = false;
					color = ValueColor;
				}
			}
			else if (inTag)
			{
				if (c == '>')
				{
					inTag = false;
					color = MarkColor;
				}
				else if (c == '"')
				{
					inValue = true;
					color = ValueColor;
				}
				else if (c == '/' || c == '=')
				{
					color = MarkColor;
					name = false;
				}
				else if (char.IsWhiteSpace(c))
				{
					name = false;
				}
				else
				{
					// The first word is the tag's name, the rest are attributes.
					color = (name || (i > 0 && (text[i - 1] == '<' || text[i - 1] == '/'))) ? TagColor : AttributeColor;
					if (i > 0 && (text[i - 1] == '<' || text[i - 1] == '/'))
					{
						name = true;
					}
				}
			}
			else if (c == '<')
			{
				inTag = true;
				name = false;
				color = MarkColor;
			}
			colors[i] = color;
		}
		return colors;
	}

	#endregion

	#region Edits

	private static void Snapshot()
	{
		_undo.Add(_glyph.Clone());
		if (_undo.Count > 200)
		{
			_undo.RemoveAt(0);
		}
		_redo.Clear();
	}

	/// <summary>A change: undoable, and saved at once.</summary>
	private static void Edit(Action change)
	{
		Snapshot();
		change();
		Commit();
	}

	private static void Commit()
	{
		_codeDirty = false;
		RainbowFont.Set(_glyph);
		_loadedVersion = RainbowFont.Version;
	}

	private static void Undo()
	{
		if (_undo.Count == 0)
		{
			return;
		}
		_redo.Add(_glyph.Clone());
		_glyph = _undo[_undo.Count - 1];
		_undo.RemoveAt(_undo.Count - 1);
		_sel.Clear();
		Commit();
	}

	private static void Redo()
	{
		if (_redo.Count == 0)
		{
			return;
		}
		_undo.Add(_glyph.Clone());
		_glyph = _redo[_redo.Count - 1];
		_redo.RemoveAt(_redo.Count - 1);
		_sel.Clear();
		Commit();
	}

	/// <summary>Flips the selected strokes (or all) across the letter's middle line.</summary>
	private static void Mirror(bool sideways)
	{
		IEnumerable<RainbowShape> shapes = (_sel.Count > 0) ? _sel.Where(i => i < _glyph.Shapes.Count).Select(i => _glyph.Shapes[i]) : _glyph.Shapes;
		foreach (RainbowShape s in shapes)
		{
			if (sideways)
			{
				s.A.X = -s.A.X;
				s.B.X = -s.B.X;
				// x -> -x turns an angle a into pi - a.
				s.Start = (float)Math.PI - s.Start;
				s.End = (float)Math.PI - s.End;
			}
			else
			{
				s.A.Y = -s.A.Y;
				s.B.Y = -s.B.Y;
				s.Start = -s.Start;
				s.End = -s.End;
			}
		}
	}

	private static void CenterSideways()
	{
		List<Vector4> points = RainbowFont.Points(_glyph, center: false);
		if (points.Count == 0)
		{
			return;
		}
		float shift = (float)Math.Round(-RainbowFont.Middle(points).X);
		foreach (RainbowShape s in _glyph.Shapes)
		{
			s.A.X += shift;
			s.B.X += shift;
		}
	}

	#endregion

	#region Files

	/// <summary>The letter being edited, attached in the chat (IsaClient.Sharing.FileShare).</summary>
	private static void SendLetter()
	{
		if (RainbowFont.Get(_char) == null)
		{
			ClientChat.Error("This letter has nothing drawn yet.");
			return;
		}
		if (IsaClient.Sharing.FileShare.AttachRainbow(RainbowFont.Export(new[] { _char }), "Rainbow " + _char))
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Rainbow letter {0} attached: send a message to share it.", _char));
		}
	}

	/// <summary>Your letters (or the whole font), attached in the chat.</summary>
	private static void SendFont()
	{
		List<char> mine = RainbowFont.Characters.Where(RainbowFont.IsMine).ToList();
		List<char> chars = (mine.Count > 0) ? mine : RainbowFont.Characters;
		if (IsaClient.Sharing.FileShare.AttachRainbow(RainbowFont.Export(chars), "Rainbow letters " + new string(chars.Take(8).ToArray()) + ((chars.Count > 8) ? "..." : "")))
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("{0} rainbow letters attached: send a message to share them.", chars.Count));
		}
	}

	private static void ExportFile()
	{
		string xml = RainbowFont.Export();
		IsaClient.Items.ItemFiles.Dialog(delegate(IWin32Window owner)
		{
			Directory.CreateDirectory(RainbowFont.ExportFolder);
			using SaveFileDialog dialog = new SaveFileDialog
			{
				Title = "Save the rainbow font",
				Filter = "Rainbow font (RainbowLetters XML)|*.xml|All files|*.*",
				FileName = "RainbowFont.xml",
				InitialDirectory = RainbowFont.ExportFolder
			};
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return null;
			}
			File.WriteAllText(dialog.FileName, xml);
			string fileName = Path.GetFileName(dialog.FileName);
			return () => ClientChat.Info("Rainbow font saved: " + fileName);
		});
	}

	private static void ImportFile()
	{
		IsaClient.Items.ItemFiles.Dialog(delegate(IWin32Window owner)
		{
			Directory.CreateDirectory(RainbowFont.ExportFolder);
			using OpenFileDialog dialog = new OpenFileDialog
			{
				Title = "Add letters from a rainbow font",
				Filter = "Rainbow font (RainbowLetters XML)|*.xml|All files|*.*",
				InitialDirectory = RainbowFont.ExportFolder
			};
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return null;
			}
			string xml = File.ReadAllText(dialog.FileName);
			return () => Imported(xml);
		});
	}

	private static void Imported(string xml)
	{
		try
		{
			int count = RainbowFont.Import(xml);
			ClientChat.Info((count > 0) ? ("Added " + count + " rainbow letters to yours.") : "There were no rainbow letters in it.");
			_codeDirty = false;
			Load();
		}
		catch (Exception ex)
		{
			ClientChat.Error("That isn't a rainbow font (RainbowLetters XML): " + ex.Message);
		}
	}

	private static void CopyText(string text, string done)
	{
		try
		{
			Clipboard.SetText(text);
			ClientChat.Info(done);
		}
		catch (Exception ex)
		{
			ClientChat.Error("Could not copy: " + ex.Message);
		}
	}

	private static void PasteText()
	{
		try
		{
			string text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
			if (text.IndexOf("<char", StringComparison.OrdinalIgnoreCase) < 0)
			{
				ClientChat.Error("Copy a rainbow font (RainbowLetters XML) first.");
				return;
			}
			Imported(text);
		}
		catch (Exception ex)
		{
			ClientChat.Error("Could not paste: " + ex.Message);
		}
	}

	#endregion
}
