using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;

namespace IsaClient.Hub;

/// <summary>
/// A text box of several lines for the panel, edited like the enhanced chat: a caret you place with the mouse, selection
/// (drag, Shift+arrows, double click a word, Ctrl+A), Ctrl+C/X/V/Z, Ctrl+arrows and Ctrl+Backspace by words,
/// Shift+Enter for a new line. Enter is <see cref="Submitted"/>. It grows with its text up to <see cref="MaxLines"/>,
/// then scrolls. Typed keys come from the game's text buffer, like the chat's.
/// </summary>
internal sealed class HubTextArea
{
	public string Id;

	public string Text = "";

	public int MaxLength = 4000;

	public int MaxLines = 7;

	public float Scale = 0.78f;

	public string Placeholder = "";

	/// <summary>Enter was pressed this frame (without Shift).</summary>
	public bool Submitted;

	/// <summary>Ctrl+V found no text to paste (the caller may paste a picture instead).</summary>
	public bool PastedNothing;

	/// <summary>Optional colors for the text, one per character (code highlighting); null draws it all white.</summary>
	public Func<string, Color[]> Highlight;

	private Color[] _colors;

	private string _colorsFor;

	private int _caret;

	private int _anchor = -1;

	private int _scrollLine;

	private int _blink;

	private bool _dragging;

	private uint _lastClick;

	private int _lastClickCaret = -1;

	private readonly List<KeyValuePair<string, int>> _undo = new List<KeyValuePair<string, int>>();

	private readonly Dictionary<Keys, int> _held = new Dictionary<Keys, int>();

	private readonly List<int> _lineStarts = new List<int>();

	/// <summary>What was sent before (oldest first), for Up/Down like a console.</summary>
	private readonly List<string> _history = new List<string>();

	/// <summary>-1: writing a new one; else how many steps back in the history.</summary>
	private int _historyAt = -1;

	private string _draft = "";

	public bool HistoryEmpty => _history.Count == 0;

	public void Remember(string text)
	{
		text = (text ?? "").Trim();
		if (text.Length > 0 && (_history.Count == 0 || _history[_history.Count - 1] != text))
		{
			_history.Add(text);
			if (_history.Count > 200)
			{
				_history.RemoveAt(0);
			}
		}
		_historyAt = -1;
	}

	/// <summary>Up on the first line / Down on the last: the previous / next thing sent (Down past the newest: what you were writing).</summary>
	private bool History(bool up)
	{
		if (_history.Count == 0)
		{
			return false;
		}
		if (up)
		{
			if (_historyAt >= _history.Count - 1)
			{
				return true;
			}
			if (_historyAt < 0)
			{
				_draft = Text;
			}
			_historyAt++;
			SetText(_history[_history.Count - 1 - _historyAt]);
			return true;
		}
		if (_historyAt < 0)
		{
			return false;
		}
		_historyAt--;
		SetText((_historyAt < 0) ? _draft : _history[_history.Count - 1 - _historyAt]);
		return true;
	}

	private readonly List<int> _lineEnds = new List<int>();

	private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	private static HubTheme Theme => HubTheme.Current;

	private const int Pad = 10;

	private float LineHeight => 26f * Scale;

	public bool Focused => HubInput.Focus == Id;

	public HubTextArea(string id)
	{
		Id = id;
	}

	/// <summary>How tall the box is for its text at this width.</summary>
	public int Height(int width)
	{
		Layout(width - Pad * 2 - 4);
		return (int)(Math.Min(MaxLines, Math.Max(1, _lineStarts.Count)) * LineHeight) + Pad + 6;
	}

	public void SetText(string text)
	{
		Text = text ?? "";
		_caret = Text.Length;
		_anchor = -1;
		_scrollLine = int.MaxValue;
	}

	private bool HasSelection => _anchor >= 0 && _anchor != _caret;

	private int SelStart => Math.Min(_anchor, _caret);

	private int SelEnd => Math.Max(_anchor, _caret);

	#region Layout

	/// <summary>The text in visual lines (wrapped at spaces, broken at \n): each line's first and end characters.</summary>
	private void Layout(int width)
	{
		_lineStarts.Clear();
		_lineEnds.Clear();
		string text = Text;
		int start = 0;
		while (true)
		{
			int hard = text.IndexOf('\n', start);
			int paragraphEnd = (hard < 0) ? text.Length : hard;
			int lineStart = start;
			while (true)
			{
				int fit = Fit(text, lineStart, paragraphEnd, width);
				if (fit >= paragraphEnd)
				{
					_lineStarts.Add(lineStart);
					_lineEnds.Add(paragraphEnd);
					break;
				}
				// Break at the last space that fits (or mid-word for a very long word).
				int space = text.LastIndexOf(' ', Math.Max(lineStart, fit - 1), Math.Max(1, fit - lineStart));
				int cut = (space > lineStart) ? (space + 1) : Math.Max(lineStart + 1, fit);
				_lineStarts.Add(lineStart);
				_lineEnds.Add(cut);
				lineStart = cut;
			}
			if (hard < 0)
			{
				break;
			}
			start = hard + 1;
		}
	}

	/// <summary>The end of the longest piece from <paramref name="from"/> that fits in <paramref name="width"/>.</summary>
	private int Fit(string text, int from, int to, int width)
	{
		if (Measure(text, from, to) <= width)
		{
			return to;
		}
		int low = from;
		int high = to;
		while (low < high)
		{
			int mid = (low + high + 1) / 2;
			if (Measure(text, from, mid) <= width)
			{
				low = mid;
			}
			else
			{
				high = mid - 1;
			}
		}
		return low;
	}

	private float Measure(string text, int from, int to)
	{
		return (to <= from) ? 0f : (Font.MeasureString(text.Substring(from, to - from)).X * Scale);
	}

	private int LineOf(int index)
	{
		for (int i = _lineStarts.Count - 1; i >= 0; i--)
		{
			if (index >= _lineStarts[i])
			{
				// A caret at a soft break shows at the start of the next line.
				return i;
			}
		}
		return 0;
	}

	private int IndexAt(int line, float x)
	{
		line = Utils.Clamp(line, 0, _lineStarts.Count - 1);
		int start = _lineStarts[line];
		int end = _lineEnds[line];
		if (end > start && end <= Text.Length && end > 0 && line < _lineStarts.Count - 1 && _lineStarts[line + 1] == end && Text[end - 1] == ' ')
		{
			end--;
		}
		int best = start;
		float bestDistance = float.MaxValue;
		for (int i = start; i <= end; i++)
		{
			float d = Math.Abs(Measure(Text, start, i) - x);
			if (d < bestDistance)
			{
				bestDistance = d;
				best = i;
			}
		}
		return best;
	}

	#endregion

	#region Editing

	private void PushUndo()
	{
		if (_undo.Count == 0 || _undo[_undo.Count - 1].Key != Text)
		{
			_undo.Add(new KeyValuePair<string, int>(Text, _caret));
			if (_undo.Count > 100)
			{
				_undo.RemoveAt(0);
			}
		}
	}

	private void Replace(string insert)
	{
		insert = (insert ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
		int start = HasSelection ? SelStart : _caret;
		int end = HasSelection ? SelEnd : _caret;
		int room = MaxLength - (Text.Length - (end - start));
		if (insert.Length > room)
		{
			insert = insert.Substring(0, Math.Max(0, room));
		}
		Text = Text.Substring(0, start) + insert + Text.Substring(end);
		_caret = start + insert.Length;
		_anchor = -1;
		_blink = 0;
	}

	private void Delete(int start, int end)
	{
		start = Utils.Clamp(start, 0, Text.Length);
		end = Utils.Clamp(end, 0, Text.Length);
		if (end > start)
		{
			Text = Text.Substring(0, start) + Text.Substring(end);
			_caret = start;
		}
		_anchor = -1;
	}

	private static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

	private int PrevWord(int i)
	{
		while (i > 0 && !WordChar(Text[i - 1]))
		{
			i--;
		}
		while (i > 0 && WordChar(Text[i - 1]))
		{
			i--;
		}
		return i;
	}

	private int NextWord(int i)
	{
		while (i < Text.Length && !WordChar(Text[i]))
		{
			i++;
		}
		while (i < Text.Length && WordChar(Text[i]))
		{
			i++;
		}
		return i;
	}

	private void Move(int to, bool extend)
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
		_caret = Utils.Clamp(to, 0, Text.Length);
		_blink = 0;
	}

	private bool Pressed(KeyboardState keys, Keys key)
	{
		if (!keys.IsKeyDown(key))
		{
			_held.Remove(key);
			return false;
		}
		_held.TryGetValue(key, out int frames);
		_held[key] = frames + 1;
		return frames == 0 || (frames >= 28 && frames % 2 == 0);
	}

	private void Keyboard(int width)
	{
		PlayerInput.WritingText = true;
		Main.CurrentInputTextTakerOverride = HubInput.TextMarker;
		Main.chatRelease = false;
		Main.instance.HandleIME();
		Main.inputTextEnter = false;
		Main.inputTextEscape = false;
		KeyboardState keys = Main.keyState;
		bool control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
		bool shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
		bool alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
		if (!Main.imeCompositionActive)
		{
			EditKeys(keys, control, shift, alt);
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
						Replace("\n");
					}
					else
					{
						Submitted = true;
					}
				}
				else if (code == 27)
				{
					HubInput.Focus = null;
				}
				else if (code >= 32 && code != 127)
				{
					if (_undo.Count == 0 || Main.keyString[i] == " " || HasSelection)
					{
						PushUndo();
					}
					Replace(Main.keyString[i]);
				}
			}
		}
		Main.keyCount = 0;
		Main.imeCompositionActive = !string.IsNullOrEmpty(Platform.Get<IImeService>().CompositionString);
	}

	private void EditKeys(KeyboardState keys, bool control, bool shift, bool alt)
	{
		if (control && !alt)
		{
			if (Pressed(keys, Keys.A))
			{
				_anchor = 0;
				_caret = Text.Length;
			}
			if (Pressed(keys, Keys.C) && HasSelection)
			{
				Platform.Get<IClipboard>().Value = Text.Substring(SelStart, SelEnd - SelStart);
			}
			if (Pressed(keys, Keys.X) && HasSelection)
			{
				PushUndo();
				Platform.Get<IClipboard>().Value = Text.Substring(SelStart, SelEnd - SelStart);
				Delete(SelStart, SelEnd);
			}
			if (Pressed(keys, Keys.V))
			{
				string paste = Platform.Get<IClipboard>().MultiLineValue ?? "";
				if (paste.Length > 0)
				{
					PushUndo();
					Replace(paste);
				}
				else
				{
					PastedNothing = true;
				}
			}
			if (Pressed(keys, Keys.Z) && _undo.Count > 0)
			{
				KeyValuePair<string, int> last = _undo[_undo.Count - 1];
				_undo.RemoveAt(_undo.Count - 1);
				Text = last.Key;
				_caret = Utils.Clamp(last.Value, 0, Text.Length);
				_anchor = -1;
			}
		}
		if (Pressed(keys, Keys.Back))
		{
			PushUndo();
			if (HasSelection)
			{
				Delete(SelStart, SelEnd);
			}
			else if (_caret > 0)
			{
				Delete(control ? PrevWord(_caret) : (_caret - 1), _caret);
			}
		}
		if (Pressed(keys, Keys.Delete))
		{
			PushUndo();
			if (HasSelection)
			{
				Delete(SelStart, SelEnd);
			}
			else if (_caret < Text.Length)
			{
				int end = control ? NextWord(_caret) : (_caret + 1);
				int at = _caret;
				Delete(at, end);
				_caret = at;
			}
		}
		if (Pressed(keys, Keys.Left))
		{
			Move((HasSelection && !shift) ? SelStart : (control ? PrevWord(_caret) : (_caret - 1)), shift);
		}
		if (Pressed(keys, Keys.Right))
		{
			Move((HasSelection && !shift) ? SelEnd : (control ? NextWord(_caret) : (_caret + 1)), shift);
		}
		if (Pressed(keys, Keys.Up) || Pressed(keys, Keys.Down))
		{
			bool up = keys.IsKeyDown(Keys.Up);
			int line = LineOf(_caret);
			bool edge = up ? (line == 0) : (line == _lineStarts.Count - 1);
			if (edge && !shift && !control && History(up))
			{
				return;
			}
			float x = Measure(Text, _lineStarts[line], _caret);
			int target = line + (up ? -1 : 1);
			Move((target < 0) ? 0 : ((target >= _lineStarts.Count) ? Text.Length : IndexAt(target, x)), shift);
		}
		if (Pressed(keys, Keys.Home))
		{
			Move(control ? 0 : _lineStarts[LineOf(_caret)], shift);
		}
		if (Pressed(keys, Keys.End))
		{
			int line = LineOf(_caret);
			int end = _lineEnds[line];
			if (line < _lineStarts.Count - 1 && end > _lineStarts[line] && Text[end - 1] == ' ')
			{
				end--;
			}
			Move(control ? Text.Length : end, shift);
		}
	}

	#endregion

	/// <summary>Draws the box and handles mouse and keys. Read <see cref="Submitted"/> after it.</summary>
	public void Draw(SpriteBatch sb, Rectangle r)
	{
		Submitted = false;
		PastedNothing = false;
		int textWidth = r.Width - Pad * 2 - 4;
		Layout(textWidth);
		_caret = Utils.Clamp(_caret, 0, Text.Length);
		bool focused = Focused;
		bool hover = HubInput.Visible(r) && HubInput.In(r);
		int visible = Math.Max(1, (int)((r.Height - Pad - 6) / LineHeight + 0.5f));
		float top = r.Y + Pad / 2 + 3;
		float left = r.X + Pad;
		// Mouse: a click places the caret (and focuses), drag selects, double click selects a word.
		if (hover && HubInput.Clicked)
		{
			HubInput.SetFocus(Id);
			focused = true;
			int line = _scrollLine + (int)((HubInput.Mouse.Y - top) / LineHeight);
			int at = IndexAt(line, HubInput.Mouse.X - left);
			bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
			if (Main.GameUpdateCount - _lastClick < 20 && at == _lastClickCaret)
			{
				_anchor = PrevWordStart(at);
				_caret = NextWordEnd(at);
				_dragging = false;
			}
			else
			{
				Move(at, shift);
				if (!shift)
				{
					_anchor = _caret;
				}
				_dragging = true;
			}
			_lastClick = Main.GameUpdateCount;
			_lastClickCaret = at;
			HubInput.Consume();
		}
		if (_dragging)
		{
			if (HubInput.Down)
			{
				int line = _scrollLine + (int)Math.Floor((HubInput.Mouse.Y - top) / LineHeight);
				_caret = IndexAt(line, HubInput.Mouse.X - left);
				if (HubInput.Mouse.Y < r.Y && _scrollLine > 0)
				{
					_scrollLine--;
				}
				else if (HubInput.Mouse.Y > r.Bottom && _scrollLine < _lineStarts.Count - visible)
				{
					_scrollLine++;
				}
			}
			else
			{
				_dragging = false;
				if (_anchor == _caret)
				{
					_anchor = -1;
				}
			}
		}
		if (hover && HubInput.Wheel != 0)
		{
			_scrollLine = Utils.Clamp(_scrollLine - Math.Sign(HubInput.Wheel), 0, Math.Max(0, _lineStarts.Count - visible));
			HubInput.Wheel = 0;
		}
		if (focused)
		{
			string before = Text;
			Keyboard(textWidth);
			if (Text != before)
			{
				Layout(textWidth);
			}
			// Keep the caret in view.
			int caretLine = LineOf(_caret);
			if (caretLine < _scrollLine)
			{
				_scrollLine = caretLine;
			}
			else if (caretLine >= _scrollLine + visible)
			{
				_scrollLine = caretLine - visible + 1;
			}
		}
		_scrollLine = Utils.Clamp(_scrollLine, 0, Math.Max(0, _lineStarts.Count - visible));
		// The box.
		float glow = HubInput.Anim("field:" + Id, focused || hover);
		HubKit.Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.CardHover, glow * 0.6f) * 0.98f);
		if (focused)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 4, r.Width - 16, 2), Theme.Accent * 0.8f);
		}
		if (Text.Length == 0)
		{
			HubKit.Text(sb, Placeholder, left, top + 2, Theme.Muted * 0.9f, Scale, textWidth);
		}
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		for (int i = 0; i < visible && _scrollLine + i < _lineStarts.Count; i++)
		{
			int line = _scrollLine + i;
			int start = _lineStarts[line];
			int end = _lineEnds[line];
			float y = top + i * LineHeight;
			if (HasSelection && SelEnd > start && SelStart < end)
			{
				int a = Math.Max(start, SelStart);
				int b = Math.Min(end, SelEnd);
				float x0 = left + Measure(Text, start, a);
				float x1 = left + Measure(Text, start, b);
				sb.Draw(pixel, new Rectangle((int)x0, (int)y, Math.Max(2, (int)(x1 - x0)), (int)LineHeight - 2), Theme.Accent * 0.45f);
			}
			string piece = Text.Substring(start, end - start).TrimEnd('\n');
			if (Highlight == null)
			{
				sb.DrawString(Font, piece, new Vector2(left, y + 2), Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 0f);
				continue;
			}
			if (!ReferenceEquals(_colorsFor, Text))
			{
				_colors = Highlight(Text);
				_colorsFor = Text;
			}
			// Runs of one color, each where its characters fall.
			int run = 0;
			while (run < piece.Length)
			{
				Color color = (_colors != null && start + run < _colors.Length) ? _colors[start + run] : Color.White;
				int length = 1;
				while (run + length < piece.Length && (_colors == null || start + run + length >= _colors.Length || _colors[start + run + length] == color))
				{
					length++;
				}
				float x = left + Measure(Text, start, start + run);
				sb.DrawString(Font, piece.Substring(run, length), new Vector2(x, y + 2), color, 0f, Vector2.Zero, Scale, SpriteEffects.None, 0f);
				run += length;
			}
		}
		if (focused && _blink++ % 60 < 32)
		{
			int line = LineOf(_caret);
			if (line >= _scrollLine && line < _scrollLine + visible)
			{
				float x = left + Measure(Text, _lineStarts[line], _caret);
				sb.Draw(pixel, new Rectangle((int)x, (int)(top + (line - _scrollLine) * LineHeight + 2), 2, (int)LineHeight - 4), Color.White);
			}
		}
		if (_lineStarts.Count > visible)
		{
			// Where the view is in a long text.
			float part = (float)visible / _lineStarts.Count;
			int trackH = r.Height - 8;
			int thumbH = Math.Max(10, (int)(trackH * part));
			int thumbY = r.Y + 4 + (int)((trackH - thumbH) * ((float)_scrollLine / Math.Max(1, _lineStarts.Count - visible)));
			sb.Draw(pixel, new Rectangle(r.Right - 6, thumbY, 3, thumbH), Color.White * 0.35f);
		}
	}

	private int PrevWordStart(int i)
	{
		while (i > 0 && WordChar(Text[i - 1]))
		{
			i--;
		}
		return i;
	}

	private int NextWordEnd(int i)
	{
		while (i < Text.Length && WordChar(Text[i]))
		{
			i++;
		}
		return i;
	}
}
