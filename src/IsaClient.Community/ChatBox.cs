using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using ReLogic.Graphics;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// IsaClient chat input: multi-line text (Shift+Enter), word wrap, a box that grows up to three lines and then
/// scrolls with a left scrollbar, a movable caret, mouse/keyboard selection and clipboard support.
/// Replaces the vanilla single-line box while <see cref="Enabled"/>; the text still lives in Main.chatText.
/// </summary>
public static class ChatBox
{
	private struct Unit
	{
		public int Start;

		public int Length;

		public bool NewLine;
	}

	private struct Line
	{
		public int FirstUnit;

		public int EndUnit;

		public bool HardBreak;

		public float[] Xs;
	}

	public const int MaxVisibleLines = 3;

	public const int LineHeight = 22;

	public const int MaxLength = 2000;

	private const int ScrollbarWidth = 12;

	private static readonly Regex _tagRegex = ChatManager.Regexes.Format;

	private static string _layoutText;

	private static float _layoutWidth = -1f;

	private static readonly List<Unit> _units = new List<Unit>();

	private static readonly List<Line> _lines = new List<Line>();

	private static int _caret;

	private static int _anchor = -1;

	private static int _scrollLine;

	private static int _blink;

	private static bool _draggingText;

	private static bool _draggingScrollbar;

	private static float _scrollbarGrabOffset;

	private static float _preferredX = -1f;

	private static uint _lastClickTick;

	private static int _lastClickCaret = -1;

	private static string _knownText = "";

	private static KeyboardState _previousKeys;

	private static readonly Dictionary<Keys, int> _heldFrames = new Dictionary<Keys, int>();

	private static readonly List<KeyValuePair<string, int>> _undoStack = new List<KeyValuePair<string, int>>();

	private static bool _wasOpen;

	private static Asset<Texture2D> _scrollbarTexture;

	private static Asset<Texture2D> _scrollbarInnerTexture;

	public static bool Enabled => ChatTools.EnhancedChat && !PlayerInput.UsingGamepad;

	/// <summary>
	/// How much higher the chat history is drawn: the input box's extra lines, and the attached pictures above it
	/// (ChatAttachments).
	/// </summary>
	public static int ExtraHeight => InputExtraHeight + IsaClient.ChatImages.ChatAttachments.StripHeight;

	/// <summary>Extra height of the input box over the vanilla one.</summary>
	public static int InputExtraHeight
	{
		get
		{
			if (!Enabled || !Main.drawingPlayerChat)
			{
				return 0;
			}
			return (VisibleLineCount - 1) * LineHeight;
		}
	}

	private static int VisibleLineCount => Math.Max(1, Math.Min(MaxVisibleLines, _lines.Count));

	private static bool HasScrollbar => _lines.Count > MaxVisibleLines;

	private static int BoxWidth
	{
		get
		{
			if (Main.screenWidth > 800)
			{
				return Main.screenWidth - 300;
			}
			return TextureAssets.TextBack.Width();
		}
	}

	private static float TextLeft => 88f + (HasScrollbar ? ((float)ScrollbarWidth + 4f) : 0f);

	private static float WrapWidth => (float)BoxWidth - 30f - (float)ScrollbarWidth - 4f - IsaClient.ChatImages.ChatAttachments.ButtonsWidth;

	private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	private static string Text
	{
		get
		{
			return Main.chatText ?? "";
		}
		set
		{
			Main.chatText = value;
			_knownText = value;
		}
	}

	#region Layout

	private static void EnsureLayout()
	{
		string text = Text;
		float width = WrapWidth;
		if (text == _layoutText && width == _layoutWidth)
		{
			return;
		}
		_layoutText = text;
		_layoutWidth = width;
		BuildUnits(text);
		BuildLines(text, width);
	}

	/// <summary>A unit is one character, a line break or a whole chat tag ([i:58], [c/ff0000:text]...), which the caret never enters.</summary>
	private static void BuildUnits(string text)
	{
		_units.Clear();
		int i = 0;
		Match match = _tagRegex.Match(text);
		while (i < text.Length)
		{
			while (match.Success && match.Index < i)
			{
				match = match.NextMatch();
			}
			if (match.Success && match.Index == i)
			{
				_units.Add(new Unit
				{
					Start = i,
					Length = match.Length
				});
				i += match.Length;
				match = match.NextMatch();
				continue;
			}
			int length = ((char.IsHighSurrogate(text[i]) && i + 1 < text.Length) ? 2 : 1);
			_units.Add(new Unit
			{
				Start = i,
				Length = length,
				NewLine = (text[i] == '\n')
			});
			i += length;
		}
	}

	private static float Measure(string text, int start, int end)
	{
		if (end <= start)
		{
			return 0f;
		}
		return ChatManager.GetStringSize(Font, text.Substring(start, end - start), Vector2.One).X;
	}

	private static void BuildLines(string text, float width)
	{
		_lines.Clear();
		int lineStart = 0;
		int u = 0;
		while (u < _units.Count)
		{
			Unit unit = _units[u];
			if (unit.NewLine)
			{
				AddLine(text, lineStart, u + 1, hardBreak: true);
				lineStart = u + 1;
				u++;
				continue;
			}
			float lineWidth = Measure(text, _units[lineStart].Start, unit.Start + unit.Length);
			if (lineWidth > width && u > lineStart && text[unit.Start] != ' ')
			{
				int breakAt = u;
				for (int k = u - 1; k > lineStart; k--)
				{
					if (text[_units[k].Start] == ' ')
					{
						breakAt = k + 1;
						break;
					}
				}
				AddLine(text, lineStart, breakAt, hardBreak: false);
				lineStart = breakAt;
				u = breakAt;
				continue;
			}
			u++;
		}
		AddLine(text, lineStart, _units.Count, hardBreak: false);
	}

	private static void AddLine(string text, int first, int end, bool hardBreak)
	{
		int drawnEnd = (hardBreak ? (end - 1) : end);
		float[] xs = new float[drawnEnd - first + 1];
		if (drawnEnd > first)
		{
			int lineStartChar = _units[first].Start;
			for (int k = first; k < drawnEnd; k++)
			{
				xs[k - first + 1] = Measure(text, lineStartChar, _units[k].Start + _units[k].Length);
			}
		}
		_lines.Add(new Line
		{
			FirstUnit = first,
			EndUnit = end,
			HardBreak = hardBreak,
			Xs = xs
		});
	}

	private static int UnitIndexOfChar(int charIndex)
	{
		for (int i = 0; i < _units.Count; i++)
		{
			if (_units[i].Start >= charIndex)
			{
				return i;
			}
		}
		return _units.Count;
	}

	private static int CharOfUnit(int unitIndex)
	{
		if (unitIndex >= _units.Count)
		{
			return Text.Length;
		}
		return _units[Math.Max(0, unitIndex)].Start;
	}

	private static int LineOfUnit(int unitIndex)
	{
		for (int i = _lines.Count - 1; i >= 0; i--)
		{
			if (_lines[i].FirstUnit <= unitIndex)
			{
				return i;
			}
		}
		return 0;
	}

	private static int LineCaretMax(Line line)
	{
		return line.HardBreak ? (line.EndUnit - 1) : line.EndUnit;
	}

	private static float CaretX(int unitIndex, out int lineIndex)
	{
		lineIndex = LineOfUnit(unitIndex);
		Line line = _lines[lineIndex];
		int k = Utils.Clamp(unitIndex - line.FirstUnit, 0, line.Xs.Length - 1);
		return line.Xs[k];
	}

	private static int UnitAtX(int lineIndex, float x)
	{
		Line line = _lines[Utils.Clamp(lineIndex, 0, _lines.Count - 1)];
		int best = 0;
		float bestDistance = float.MaxValue;
		for (int k = 0; k < line.Xs.Length; k++)
		{
			float distance = Math.Abs(line.Xs[k] - x);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = k;
			}
		}
		int unit = line.FirstUnit + best;
		// On a soft-wrapped line the end boundary belongs to the next line.
		if (!line.HardBreak && unit == line.EndUnit && lineIndex < _lines.Count - 1 && unit > line.FirstUnit)
		{
			unit--;
		}
		return unit;
	}

	#endregion

	#region Editing

	private static bool HasSelection => _anchor >= 0 && _anchor != _caret;

	private static int SelectionStart => Math.Min(_anchor, _caret);

	private static int SelectionEnd => Math.Max(_anchor, _caret);

	private static void PushUndo()
	{
		string text = Text;
		if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1].Key == text)
		{
			return;
		}
		_undoStack.Add(new KeyValuePair<string, int>(text, _caret));
		if (_undoStack.Count > 100)
		{
			_undoStack.RemoveAt(0);
		}
	}

	private static void ReplaceSelection(string insert)
	{
		string text = Text;
		int start = (HasSelection ? SelectionStart : _caret);
		int end = (HasSelection ? SelectionEnd : _caret);
		insert = insert.Replace("\r\n", "\n").Replace("\r", "\n");
		int room = MaxLength - (text.Length - (end - start));
		if (insert.Length > room)
		{
			insert = insert.Substring(0, Math.Max(0, room));
		}
		if (insert.Length == 0 && start == end)
		{
			return;
		}
		Text = text.Substring(0, start) + insert + text.Substring(end);
		_caret = start + insert.Length;
		_anchor = -1;
		_preferredX = -1f;
	}

	private static void DeleteRange(int start, int end)
	{
		if (end <= start)
		{
			return;
		}
		string text = Text;
		Text = text.Substring(0, start) + text.Substring(end);
		_caret = start;
		_anchor = -1;
		_preferredX = -1f;
	}

	/// <summary>Called by ChatManager.AddChatText (shift-clicking items into the chat, etc.): inserts at the caret.</summary>
	public static void InsertExternal(string insert)
	{
		SyncExternalChanges();
		PushUndo();
		ReplaceSelection(insert);
		_blink = 0;
	}

	private static void SyncExternalChanges()
	{
		string text = Text;
		if (text != _knownText)
		{
			_knownText = text;
			_caret = text.Length;
			_anchor = -1;
		}
		_caret = Utils.Clamp(_caret, 0, text.Length);
		if (_anchor > text.Length)
		{
			_anchor = -1;
		}
	}

	private static int PreviousBoundary(int charIndex)
	{
		int unit = UnitIndexOfChar(charIndex);
		return CharOfUnit(Math.Max(0, unit - 1));
	}

	private static int NextBoundary(int charIndex)
	{
		int unit = UnitIndexOfChar(charIndex);
		if (unit >= _units.Count)
		{
			return Text.Length;
		}
		return CharOfUnit(unit + 1);
	}

	private static bool IsWordChar(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == ']';
	}

	private static int PreviousWord(int charIndex)
	{
		string text = Text;
		int i = charIndex;
		while (i > 0 && !IsWordChar(text[i - 1]))
		{
			i = PreviousBoundary(i);
		}
		while (i > 0 && IsWordChar(text[i - 1]))
		{
			i = PreviousBoundary(i);
		}
		return i;
	}

	private static int NextWord(int charIndex)
	{
		string text = Text;
		int i = charIndex;
		while (i < text.Length && IsWordChar(text[i]))
		{
			i = NextBoundary(i);
		}
		while (i < text.Length && !IsWordChar(text[i]))
		{
			i = NextBoundary(i);
		}
		return i;
	}

	private static void MoveCaret(int newCaret, bool extend)
	{
		if (extend)
		{
			if (_anchor < 0)
			{
				_anchor = _caret;
			}
		}
		else
		{
			_anchor = -1;
		}
		_caret = Utils.Clamp(newCaret, 0, Text.Length);
		_blink = 0;
	}

	private static void SelectWordAt(int charIndex)
	{
		string text = Text;
		int start = charIndex;
		int end = charIndex;
		while (start > 0 && IsWordChar(text[start - 1]))
		{
			start = PreviousBoundary(start);
		}
		while (end < text.Length && IsWordChar(text[end]))
		{
			end = NextBoundary(end);
		}
		_anchor = start;
		_caret = end;
	}

	private static void EnsureCaretVisible()
	{
		EnsureLayout();
		int line = LineOfUnit(UnitIndexOfChar(_caret));
		if (line < _scrollLine)
		{
			_scrollLine = line;
		}
		else if (line >= _scrollLine + MaxVisibleLines)
		{
			_scrollLine = line - MaxVisibleLines + 1;
		}
		ClampScroll();
	}

	private static void ClampScroll()
	{
		_scrollLine = Utils.Clamp(_scrollLine, 0, Math.Max(0, _lines.Count - MaxVisibleLines));
	}

	#endregion

	#region Update (keyboard)

	private static bool Pressed(KeyboardState keys, Keys key)
	{
		if (!keys.IsKeyDown(key))
		{
			_heldFrames.Remove(key);
			return false;
		}
		_heldFrames.TryGetValue(key, out var frames);
		_heldFrames[key] = frames + 1;
		if (frames == 0)
		{
			return true;
		}
		return frames >= 28 && frames % 2 == 0;
	}

	/// <summary>Replaces Main.GetInputText + the vanilla width trimming for the chat.</summary>
	public static void Update()
	{
		if (!_wasOpen)
		{
			_wasOpen = true;
			_undoStack.Clear();
			_scrollLine = 0;
			_anchor = -1;
			_caret = Text.Length;
			_knownText = Text;
			_previousKeys = Main.keyState;
		}
		Main.inputTextEnter = false;
		Main.inputTextEscape = false;
		if (!FocusHelper.AllowInputProcessing)
		{
			Main.keyCount = 0;
			return;
		}
		SyncExternalChanges();
		EnsureLayout();
		string before = Text;
		KeyboardState keys = Main.keyState;
		bool control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
		bool shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
		bool alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
		bool ime = Main.imeCompositionActive;
		if (!ime)
		{
			HandleEditingKeys(keys, control, shift, alt);
		}
		if (!control || alt)
		{
			for (int i = 0; i < Main.keyCount; i++)
			{
				int code = Main.keyInt[i];
				if (code == 13)
				{
					if (shift)
					{
						PushUndo();
						ReplaceSelection("\n");
					}
					else
					{
						Main.inputTextEnter = true;
					}
				}
				else if (code == 27)
				{
					Main.inputTextEscape = true;
				}
				else if (code >= 32 && code != 127)
				{
					if (_undoStack.Count == 0 || Main.keyString[i] == " " || HasSelection)
					{
						PushUndo();
					}
					ReplaceSelection(Main.keyString[i]);
				}
			}
		}
		Main.keyCount = 0;
		Main.oldInputText = Main.inputText;
		Main.inputText = keys;
		_previousKeys = keys;
		Main.imeCompositionActive = !string.IsNullOrEmpty(Platform.Get<IImeService>().CompositionString);
		if (Text != before)
		{
			SoundEngine.PlaySound(12);
			_blink = 0;
			EnsureCaretVisible();
		}
		if (Main.inputTextEscape && !Main.imeCompositionActive)
		{
			Main.drawingPlayerChat = false;
		}
	}

	private static void HandleEditingKeys(KeyboardState keys, bool control, bool shift, bool alt)
	{
		string text = Text;
		if (control && !alt)
		{
			if (Pressed(keys, Keys.A))
			{
				_anchor = 0;
				_caret = text.Length;
			}
			if (Pressed(keys, Keys.C) || (Pressed(keys, Keys.Insert) && !shift))
			{
				Platform.Get<IClipboard>().Value = (HasSelection ? text.Substring(SelectionStart, SelectionEnd - SelectionStart) : text);
			}
			if (Pressed(keys, Keys.X))
			{
				PushUndo();
				Platform.Get<IClipboard>().Value = (HasSelection ? text.Substring(SelectionStart, SelectionEnd - SelectionStart) : text);
				if (HasSelection)
				{
					DeleteRange(SelectionStart, SelectionEnd);
				}
				else
				{
					Text = "";
					_caret = 0;
				}
			}
			// A copied picture (or picture files) is attached instead of pasted as text.
			if (Pressed(keys, Keys.V) && !IsaClient.Sharing.ShareChat.TryPaste() && !IsaClient.ChatImages.ChatAttachments.TryPaste())
			{
				PushUndo();
				ReplaceSelection(Platform.Get<IClipboard>().MultiLineValue ?? "");
			}
			if (Pressed(keys, Keys.Z))
			{
				if (_undoStack.Count > 0)
				{
					KeyValuePair<string, int> last = _undoStack[_undoStack.Count - 1];
					_undoStack.RemoveAt(_undoStack.Count - 1);
					Text = last.Key;
					_caret = Utils.Clamp(last.Value, 0, last.Key.Length);
					_anchor = -1;
				}
			}
		}
		if (shift && !control && Pressed(keys, Keys.Insert) && !IsaClient.Sharing.ShareChat.TryPaste() && !IsaClient.ChatImages.ChatAttachments.TryPaste())
		{
			PushUndo();
			ReplaceSelection(Platform.Get<IClipboard>().MultiLineValue ?? "");
		}
		if (Pressed(keys, Keys.Back))
		{
			PushUndo();
			if (HasSelection)
			{
				DeleteRange(SelectionStart, SelectionEnd);
			}
			else if (_caret > 0)
			{
				DeleteRange(control ? PreviousWord(_caret) : PreviousBoundary(_caret), _caret);
			}
		}
		if (Pressed(keys, Keys.Delete))
		{
			PushUndo();
			if (HasSelection)
			{
				if (shift)
				{
					Platform.Get<IClipboard>().Value = Text.Substring(SelectionStart, SelectionEnd - SelectionStart);
				}
				DeleteRange(SelectionStart, SelectionEnd);
			}
			else if (_caret < Text.Length)
			{
				DeleteRange(_caret, control ? NextWord(_caret) : NextBoundary(_caret));
			}
		}
		EnsureLayout();
		if (Pressed(keys, Keys.Left))
		{
			if (HasSelection && !shift)
			{
				MoveCaret(SelectionStart, extend: false);
			}
			else
			{
				MoveCaret(control ? PreviousWord(_caret) : PreviousBoundary(_caret), shift);
			}
			_preferredX = -1f;
			EnsureCaretVisible();
		}
		if (Pressed(keys, Keys.Right))
		{
			if (HasSelection && !shift)
			{
				MoveCaret(SelectionEnd, extend: false);
			}
			else
			{
				MoveCaret(control ? NextWord(_caret) : NextBoundary(_caret), shift);
			}
			_preferredX = -1f;
			EnsureCaretVisible();
		}
		int caretUnit = UnitIndexOfChar(_caret);
		int caretLine = LineOfUnit(caretUnit);
		if (Pressed(keys, Keys.Home))
		{
			MoveCaret(control ? 0 : CharOfUnit(_lines[caretLine].FirstUnit), shift);
			_preferredX = -1f;
			EnsureCaretVisible();
		}
		if (Pressed(keys, Keys.End))
		{
			MoveCaret(control ? Text.Length : CharOfUnit(LineCaretMax(_lines[caretLine])), shift);
			_preferredX = -1f;
			EnsureCaretVisible();
		}
		bool up = Pressed(keys, Keys.Up);
		bool down = Pressed(keys, Keys.Down);
		if ((up || down) && shift)
		{
			// Shift+Up/Down: the lines you sent before, like a terminal.
			TypedHistory.Step(up ? (-1) : 1);
		}
		else if (up || down)
		{
			int target = caretLine + (up ? (-1) : 1);
			if (target < 0 || target >= _lines.Count)
			{
				// Past the first/last line the arrows scroll the chat history, like vanilla.
				Main.chatMonitor.Offset(up ? 1 : (-1));
			}
			else
			{
				if (_preferredX < 0f)
				{
					_preferredX = CaretX(caretUnit, out _);
				}
				float keepX = _preferredX;
				MoveCaret(CharOfUnit(UnitAtX(target, keepX)), shift);
				_preferredX = keepX;
				EnsureCaretVisible();
			}
		}
		if (Pressed(keys, Keys.PageUp))
		{
			Main.chatMonitor.Offset(5);
		}
		if (Pressed(keys, Keys.PageDown))
		{
			Main.chatMonitor.Offset(-5);
		}
	}

	#endregion

	#region Draw (and mouse)

	public static void Draw(SpriteBatch spriteBatch)
	{
		SyncExternalChanges();
		EnsureLayout();
		ClampScroll();
		int visible = VisibleLineCount;
		int boxHeight = TextureAssets.TextBack.Height() + (visible - 1) * LineHeight;
		int boxTop = Main.screenHeight - 36 - (visible - 1) * LineHeight;
		int boxLeft = 78;
		int boxWidth = BoxWidth;
		Utils.DrawSplicedPanel(spriteBatch, TextureAssets.TextBack.Value, boxLeft, boxTop, boxWidth, boxHeight, 10, 10, 10, 10, new Color(100, 100, 100, 100));
		Rectangle box = new Rectangle(boxLeft, boxTop, boxWidth, boxHeight);
		float textLeft = TextLeft;
		float textTop = boxTop + 6;
		HandleMouse(box, textLeft, textTop, boxTop, boxHeight);
		string text = Text;
		int caretUnit = UnitIndexOfChar(_caret);
		int caretLine = LineOfUnit(caretUnit);
		int selStartUnit = (HasSelection ? UnitIndexOfChar(SelectionStart) : (-1));
		int selEndUnit = (HasSelection ? UnitIndexOfChar(SelectionEnd) : (-1));
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		for (int row = 0; row < visible; row++)
		{
			int index = _scrollLine + row;
			if (index >= _lines.Count)
			{
				break;
			}
			Line line = _lines[index];
			float y = textTop + (float)(row * LineHeight);
			int drawnEnd = (line.HardBreak ? (line.EndUnit - 1) : line.EndUnit);
			if (HasSelection)
			{
				int a = Math.Max(selStartUnit, line.FirstUnit);
				int b = Math.Min(selEndUnit, drawnEnd);
				if (b > a || (line.HardBreak && selStartUnit <= drawnEnd && selEndUnit > drawnEnd))
				{
					float x1 = line.Xs[Utils.Clamp(a - line.FirstUnit, 0, line.Xs.Length - 1)];
					float x2 = line.Xs[Utils.Clamp(b - line.FirstUnit, 0, line.Xs.Length - 1)];
					if (line.HardBreak && selEndUnit > drawnEnd)
					{
						x2 += 6f;
					}
					spriteBatch.Draw(pixel, new Rectangle((int)(textLeft + x1), (int)y - 2, Math.Max(2, (int)(x2 - x1)), LineHeight), new Color(70, 130, 255) * 0.5f);
				}
			}
			if (drawnEnd > line.FirstUnit)
			{
				int startChar = _units[line.FirstUnit].Start;
				int endChar = _units[drawnEnd - 1].Start + _units[drawnEnd - 1].Length;
				TextSnippet[] snippets = ChatManager.ParseMessage(text.Substring(startChar, endChar - startChar), Color.White).ToArray();
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, Font, snippets, new Vector2(textLeft, y), 0f, Vector2.Zero, Vector2.One, out var hovered);
				if (hovered > -1 && !PlayerInput.IgnoreMouseInterface && !_draggingText)
				{
					snippets[hovered].OnHover();
				}
			}
			if (index == caretLine)
			{
				float caretX = textLeft + CaretX(caretUnit, out _);
				Main.instance.SetIMEPanelAnchor(new Vector2(caretX + 10f, y - 6f), 0f);
				string composition = Platform.Get<IImeService>().CompositionString;
				if (!string.IsNullOrEmpty(composition))
				{
					ChatManager.DrawColorCodedStringWithShadow(spriteBatch, Font, composition, new Vector2(caretX, y), Main.imeCompositionStringColor, 0f, Vector2.Zero, Vector2.One);
					caretX += Font.MeasureString(composition).X;
				}
				if (_blink % 40 < 22)
				{
					spriteBatch.Draw(pixel, new Rectangle((int)caretX, (int)y - 1, 2, LineHeight - 4), Color.White);
				}
			}
		}
		_blink++;
		if (HasScrollbar)
		{
			DrawScrollbar(spriteBatch, ScrollbarRect(boxTop, boxHeight), ThumbRect(boxTop, boxHeight));
		}
		if (text.Length >= 200)
		{
			string counter = text.Length + "/" + MaxLength;
			Vector2 size = FontAssets.ItemStack.Value.MeasureString(counter) * 0.8f;
			Color color = ((text.Length >= MaxLength) ? new Color(255, 120, 120) : new Color(180, 180, 180));
			Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.ItemStack.Value, counter, (float)(boxLeft + boxWidth) - size.X - 10f, (float)boxTop - size.Y - 2f, color, Color.Black, Vector2.Zero, 0.8f);
		}
	}

	private static Rectangle ScrollbarRect(int boxTop, int boxHeight)
	{
		return new Rectangle(84, boxTop + 5, ScrollbarWidth, boxHeight - 10);
	}

	private static Rectangle ThumbRect(int boxTop, int boxHeight)
	{
		Rectangle track = ScrollbarRect(boxTop, boxHeight);
		int maxScroll = Math.Max(1, _lines.Count - MaxVisibleLines);
		int thumbHeight = Math.Max(10, track.Height * MaxVisibleLines / Math.Max(1, _lines.Count));
		int thumbY = track.Y + (track.Height - thumbHeight) * _scrollLine / maxScroll;
		return new Rectangle(track.X, thumbY, track.Width, thumbHeight);
	}

	private static void DrawScrollbar(SpriteBatch spriteBatch, Rectangle track, Rectangle thumb)
	{
		if (_scrollbarTexture == null)
		{
			_scrollbarTexture = Main.Assets.Request<Texture2D>("Images/UI/Scrollbar", AssetRequestMode.ImmediateLoad);
			_scrollbarInnerTexture = Main.Assets.Request<Texture2D>("Images/UI/ScrollbarInner", AssetRequestMode.ImmediateLoad);
		}
		DrawBar(spriteBatch, _scrollbarTexture.Value, new Rectangle(track.X - 2, track.Y - 2, track.Width + 4, track.Height + 4), Color.White);
		bool hot = _draggingScrollbar || thumb.Contains(Main.mouseX, Main.mouseY);
		DrawBar(spriteBatch, _scrollbarInnerTexture.Value, new Rectangle(thumb.X + 1, thumb.Y, thumb.Width - 2, thumb.Height), Color.White * (hot ? 1f : 0.85f));
	}

	private static void DrawBar(SpriteBatch spriteBatch, Texture2D texture, Rectangle dimensions, Color color)
	{
		spriteBatch.Draw(texture, new Rectangle(dimensions.X, dimensions.Y - 6, dimensions.Width, 6), new Rectangle(0, 0, texture.Width, 6), color);
		spriteBatch.Draw(texture, new Rectangle(dimensions.X, dimensions.Y, dimensions.Width, dimensions.Height), new Rectangle(0, 6, texture.Width, 4), color);
		spriteBatch.Draw(texture, new Rectangle(dimensions.X, dimensions.Y + dimensions.Height, dimensions.Width, 6), new Rectangle(0, texture.Height - 6, texture.Width, 6), color);
	}

	private static void HandleMouse(Rectangle box, float textLeft, float textTop, int boxTop, int boxHeight)
	{
		bool hovering = box.Contains(Main.mouseX, Main.mouseY);
		if (!Main.mouseLeft)
		{
			_draggingText = false;
			_draggingScrollbar = false;
		}
		if (PlayerInput.IgnoreMouseInterface)
		{
			return;
		}
		if (hovering || _draggingText || _draggingScrollbar)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		if (hovering && PlayerInput.ScrollWheelDeltaForUI != 0 && HasScrollbar)
		{
			_scrollLine -= Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
			ClampScroll();
			PlayerInput.ScrollWheelDeltaForUI = 0;
			PlayerInput.ScrollWheelDelta = 0;
		}
		if (HasScrollbar)
		{
			Rectangle track = ScrollbarRect(boxTop, boxHeight);
			Rectangle thumb = ThumbRect(boxTop, boxHeight);
			Rectangle hitTrack = new Rectangle(track.X - 3, track.Y - 3, track.Width + 6, track.Height + 6);
			if (Main.mouseLeft && Main.mouseLeftRelease && hitTrack.Contains(Main.mouseX, Main.mouseY))
			{
				_draggingScrollbar = true;
				_scrollbarGrabOffset = (thumb.Contains(Main.mouseX, Main.mouseY) ? ((float)(Main.mouseY - thumb.Y)) : ((float)thumb.Height / 2f));
				Main.mouseLeftRelease = false;
			}
			if (_draggingScrollbar)
			{
				float range = Math.Max(1, track.Height - thumb.Height);
				float t = ((float)Main.mouseY - _scrollbarGrabOffset - (float)track.Y) / range;
				_scrollLine = (int)Math.Round(MathHelper.Clamp(t, 0f, 1f) * (float)(_lines.Count - MaxVisibleLines));
				ClampScroll();
				return;
			}
		}
		if (Main.mouseLeft && Main.mouseLeftRelease && hovering)
		{
			int hit = HitTest(textLeft, textTop);
			bool extend = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
			uint now = Main.GameUpdateCount;
			if (!extend && hit == _lastClickCaret && now - _lastClickTick < 25)
			{
				SelectWordAt(hit);
				_lastClickCaret = -1;
			}
			else
			{
				MoveCaret(hit, extend);
				if (!extend)
				{
					_anchor = hit;
				}
				_draggingText = true;
				_lastClickCaret = hit;
				_lastClickTick = now;
			}
			_preferredX = -1f;
			Main.mouseLeftRelease = false;
		}
		else if (_draggingText && Main.mouseLeft)
		{
			// Dragging above/below the box scrolls while selecting.
			if (Main.mouseY < boxTop && Main.GameUpdateCount % 4 == 0)
			{
				_scrollLine--;
			}
			else if (Main.mouseY > boxTop + boxHeight && Main.GameUpdateCount % 4 == 0)
			{
				_scrollLine++;
			}
			ClampScroll();
			_caret = HitTest(textLeft, textTop);
			_blink = 0;
		}
		if (!_draggingText && _anchor == _caret)
		{
			_anchor = -1;
		}
	}

	private static int HitTest(float textLeft, float textTop)
	{
		int row = (int)Math.Floor(((float)Main.mouseY - textTop + 2f) / (float)LineHeight);
		row = Utils.Clamp(row, 0, VisibleLineCount - 1);
		int lineIndex = Utils.Clamp(_scrollLine + row, 0, _lines.Count - 1);
		return CharOfUnit(UnitAtX(lineIndex, (float)Main.mouseX - textLeft));
	}

	/// <summary>Called when the chat is closed so the next opening starts clean.</summary>
	public static void OnClosed()
	{
		_wasOpen = false;
		_draggingText = false;
		_draggingScrollbar = false;
		_heldFrames.Clear();
	}

	#endregion
}
