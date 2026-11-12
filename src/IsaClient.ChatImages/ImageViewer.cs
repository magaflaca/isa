using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.OS;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;

namespace IsaClient.ChatImages;

/// <summary>
/// A chat picture big in the middle of the screen: it grows out of its thumbnail, the wheel zooms where the mouse is,
/// dragging moves it when zoomed in, the arrows (or the side buttons) go through the pictures of the same message.
/// Buttons: open in the browser, copy the link or the picture, save it, delete it (pictures you sent). A click outside,
/// right click or Escape closes it. It is one of the music player's floating surfaces, so it gets the mouse first.
/// </summary>
internal static class ImageViewer
{
	private const string Layer = "chatimage";

	private static List<ChatPicture> _pictures;

	private static int _index;

	private static ImageFrames _full;

	private static ChatPicture _fullFor;

	private static bool _fullLoading;

	private static Rectangle _from;

	private static float _open;

	private static bool _closing;

	private static float _zoom = 1f;

	private static Vector2 _pan;

	private static bool _dragging;

	private static Point _dragStart;

	private static Vector2 _panStart;

	private static bool _dragMoved;

	private static double _lastClick = -100.0;

	private static Rectangle _screenRect;

	/// <summary>Where the toolbar and the side buttons were last drawn: clicks there are not "outside".</summary>
	private static Rectangle _toolbar;

	private static Rectangle _previousButton;

	private static Rectangle _nextButton;

	private static KeyboardState _keys;

	private static string _note;

	private static double _noteUntil;

	private static bool _registered;

	private static RasterizerState _scissor;

	public static bool IsOpen => _pictures != null;

	internal static bool Shows(ChatPicture picture)
	{
		return _pictures != null && _pictures.Contains(picture);
	}

	internal static void Register()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = Layer,
			Shown = () => IsOpen,
			ScreenRect = () => _screenRect,
			Draw = Draw
		});
	}

	/// <summary>Opens <paramref name="pictures"/> at <paramref name="index"/>, growing out of <paramref name="from"/> (UI coordinates).</summary>
	public static void Open(List<ChatPicture> pictures, int index, Rectangle from)
	{
		Register();
		_pictures = pictures;
		_from = from;
		_open = 0f;
		_closing = false;
		_keys = Main.keyState;
		Show(index);
		MusicPlayerWindow.BringSurfaceToFront(Layer);
		SoundEngine.PlaySound(10);
	}

	private static void Show(int index)
	{
		_index = (index + _pictures.Count) % _pictures.Count;
		_zoom = 1f;
		_pan = Vector2.Zero;
		_dragging = false;
		ChatPicture picture = _pictures[_index];
		picture.Request();
	}

	public static void Close()
	{
		if (IsOpen && !_closing)
		{
			_closing = true;
			SoundEngine.PlaySound(11);
		}
	}

	private static void Finish()
	{
		_toolbar = Rectangle.Empty;
		_previousButton = Rectangle.Empty;
		_nextButton = Rectangle.Empty;
		_pictures = null;
		_full?.Dispose();
		_full = null;
		_fullFor = null;
		_screenRect = Rectangle.Empty;
		_dragging = false;
	}

	#region Drawing

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		_screenRect = new Rectangle(0, 0, Main.instance.GraphicsDevice.Viewport.Width, Main.instance.GraphicsDevice.Viewport.Height);
		_open = MathHelper.Clamp(_open + (_closing ? -0.16f : 0.12f), 0f, 1f);
		if (_closing && _open <= 0f)
		{
			Finish();
			return;
		}
		float ease = 1f - (float)Math.Pow(1f - _open, 3);
		ChatPicture picture = _pictures[_index];
		picture.LastDrawn = Main.timeForVisualEffects;
		MusicDrawing.Fill(sb, new Rectangle(0, 0, screenW, screenH), Color.Black * (0.78f * ease));
		Rectangle area = new Rectangle(40, 40, screenW - 80, screenH - 40 - 96);
		Texture2D texture = Picture(picture, screenW, screenH);
		int sourceW = (picture.SourceWidth > 0) ? picture.SourceWidth : 400;
		int sourceH = (picture.SourceHeight > 0) ? picture.SourceHeight : 300;
		// Fits the screen; small pictures grow up to 3x so they can be seen.
		float fit = Math.Min(Math.Min((float)area.Width / sourceW, (float)area.Height / sourceH), 3f);
		Vector2 size = new Vector2(sourceW, sourceH) * fit;
		Rectangle target = new Rectangle((int)(area.Center.X - size.X / 2f), (int)(area.Center.Y - size.Y / 2f), (int)size.X, (int)size.Y);
		HandleInput(picture, target, area, screenW, screenH);
		Rectangle shown = Zoomed(target);
		Rectangle rect = Lerp(_from, shown, ease);
		bool clip = _zoom > 1.01f;
		// Shadow and frame, like the thumbnails but bigger.
		if (!clip)
		{
			MusicDrawing.Fill(sb, new Rectangle(rect.X - 2, rect.Y + 6, rect.Width + 10, rect.Height + 4), Color.Black * (0.45f * ease));
			MusicDrawing.Fill(sb, new Rectangle(rect.X - 5, rect.Y - 5, rect.Width + 10, rect.Height + 10), new Color(14, 18, 38) * ease);
			MusicDrawing.Fill(sb, new Rectangle(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8), new Color(73, 94, 171) * ease);
			MusicDrawing.Fill(sb, new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, 1), new Color(150, 170, 235) * ease);
		}
		MusicDrawing.Fill(sb, rect, new Color(8, 10, 20) * ease);
		if (texture != null)
		{
			// Point sampling when zoomed in past the picture's own pixels (pixel art stays sharp), smooth otherwise.
			bool sharp = rect.Width * Main.UIScale > sourceW * 1.5f;
			DrawPicture(sb, texture, rect, clip ? area : Rectangle.Empty, Color.White * ease, sharp);
		}
		else
		{
			string status = (picture.State == PictureState.Failed) ? ("Couldn't load the picture: " + picture.Error) : "Loading...";
			Vector2 textSize = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(status));
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(status), new Vector2(rect.Center.X - textSize.X / 2f, rect.Center.Y - 10), Color.White * ease);
		}
		if (_open < 1f || _closing)
		{
			return;
		}
		DrawSideButtons(sb, area);
		DrawToolbar(sb, picture, screenW, screenH);
		if (_note != null && Main.timeForVisualEffects < _noteUntil)
		{
			Vector2 noteSize = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(_note)) * 0.85f;
			Rectangle pill = new Rectangle((int)(screenW / 2 - noteSize.X / 2 - 14), 18, (int)noteSize.X + 28, 30);
			MusicDrawing.Box(sb, pill, MusicPlayerWindow.BackColor * 0.95f);
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(_note), new Vector2(pill.X + 14, pill.Y + 7), Color.White, 0.85f);
		}
	}

	/// <summary>The full picture once it is decoded (the thumbnail meanwhile).</summary>
	private static Texture2D Picture(ChatPicture picture, int screenW, int screenH)
	{
		if (picture.State != PictureState.Ready || picture.Data == null)
		{
			return null;
		}
		if (_fullFor != picture && !_fullLoading)
		{
			_fullLoading = true;
			ChatPicture wanted = picture;
			byte[] data = picture.Data;
			int viewW = Main.instance.GraphicsDevice.Viewport.Width;
			int viewH = Main.instance.GraphicsDevice.Viewport.Height;
			// Still pictures up to 2048 px (room to zoom); animated ones at screen size (each frame is decoded as it plays).
			int maxW = picture.Animated ? viewW : Math.Max(viewW, 2048);
			int maxH = picture.Animated ? viewH : Math.Max(viewH, 2048);
			Task.Run(delegate
			{
				ImageFrames made = null;
				try
				{
					made = ImageFrames.Open(data, maxW, maxH, ChatImages.Animate);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
				Main.QueueMainThreadAction(delegate
				{
					_fullLoading = false;
					if (made == null)
					{
						return;
					}
					if (!IsOpen || _pictures[_index] != wanted)
					{
						made.Dispose();
						return;
					}
					_full?.Dispose();
					_full = made;
					_fullFor = wanted;
				});
			});
		}
		Texture2D full = (_fullFor == picture) ? _full?.Update(ChatImages.Animate) : null;
		return full ?? picture.Thumbnail((int)Math.Round((ChatImages.ThumbLines * ChatImages.ChatLineHeight - 12) * Main.UIScale), 4096);
	}

	private static void DrawPicture(SpriteBatch sb, Texture2D texture, Rectangle rect, Rectangle clip, Color color, bool sharp)
	{
		RasterizerState rasterizer = RasterizerState.CullCounterClockwise;
		if (clip != Rectangle.Empty)
		{
			rasterizer = _scissor ?? (_scissor = new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			});
			sb.End();
			sb.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(MusicPlayerWindow.ToScreen(clip), sb.GraphicsDevice.Viewport.Bounds);
		}
		else
		{
			sb.End();
		}
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, sharp ? SamplerState.PointClamp : SamplerState.LinearClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
		sb.Draw(texture, rect, color);
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.gameMenu ? SamplerState.PointClamp : SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
	}

	private static void DrawSideButtons(SpriteBatch sb, Rectangle area)
	{
		if (_pictures.Count < 2)
		{
			_previousButton = Rectangle.Empty;
			_nextButton = Rectangle.Empty;
			return;
		}
		_previousButton = new Rectangle(area.X - 4, area.Center.Y - 22, 44, 44);
		_nextButton = new Rectangle(area.Right - 40, area.Center.Y - 22, 44, 44);
		if (MusicDrawing.IconButton(sb, "chatimage:prev", _previousButton, MusicDrawing.Tex("Images/UI/IconPrev"), null, "Previous picture (Left arrow)"))
		{
			Show(_index - 1);
		}
		if (MusicDrawing.IconButton(sb, "chatimage:next", _nextButton, MusicDrawing.Tex("Images/UI/IconNext"), null, "Next picture (Right arrow)"))
		{
			Show(_index + 1);
		}
	}

	private static void DrawToolbar(SpriteBatch sb, ChatPicture picture, int screenW, int screenH)
	{
		ChatImages.SentPicture mine = ChatImages.FindSent(picture.Url);
		List<KeyValuePair<string, string>> buttons = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("open", "Open in browser"),
			new KeyValuePair<string, string>("link", "Copy link"),
			new KeyValuePair<string, string>("copy", "Copy picture"),
			new KeyValuePair<string, string>("save", "Save")
		};
		if (mine != null && !string.IsNullOrEmpty(mine.DeleteHash))
		{
			buttons.Add(new KeyValuePair<string, string>("delete", "Delete"));
		}
		buttons.Add(new KeyValuePair<string, string>("close", "Close"));
		string info = ((picture.SourceWidth > 0) ? (picture.SourceWidth + " x " + picture.SourceHeight) : "") + (picture.Animated ? "  GIF" : "") + ((_pictures.Count > 1) ? ("   " + (_index + 1) + " / " + _pictures.Count) : "") + ((_zoom > 1.01f) ? ("   " + (int)Math.Round(_zoom * 100f) + "%") : "");
		float infoW = MusicDrawing.Measure(info, 0.75f);
		int width = 16 + (int)infoW + 16;
		foreach (KeyValuePair<string, string> button in buttons)
		{
			width += (int)MusicDrawing.Measure(button.Value, 0.78f) + 22 + 6;
		}
		Rectangle bar = new Rectangle(screenW / 2 - width / 2, screenH - 74, width, 44);
		_toolbar = bar;
		MusicDrawing.Box(sb, bar, MusicPlayerWindow.BackColor * 0.95f);
		MusicDrawing.Text(sb, info, bar.X + 16, bar.Y + 13, MusicPlayerWindow.MutedColor, 0.75f);
		int x = bar.X + 16 + (int)infoW + 16;
		foreach (KeyValuePair<string, string> button in buttons)
		{
			int w = (int)MusicDrawing.Measure(button.Value, 0.78f) + 22;
			Color color = (button.Key == "delete") ? new Color(150, 45, 60) : MusicPlayerWindow.PanelColor;
			if (MusicDrawing.TextButton(sb, "chatimage:" + button.Key, new Rectangle(x, bar.Y + 8, w, 28), button.Value, color, Tooltip(button.Key)))
			{
				Act(button.Key, picture, mine);
			}
			x += w + 6;
		}
	}

	private static string Tooltip(string key)
	{
		switch (key)
		{
		case "open":
			return "Opens the picture's page in your browser";
		case "link":
			return "Copies the link (paste it anywhere)";
		case "copy":
			return "Copies the picture itself";
		case "save":
			return "Saves it in IsaClient/ChatImages/Saved";
		case "delete":
			return "You sent this one: takes it off Imgur, for everyone";
		default:
			return "Close (Escape, right click, or click outside)";
		}
	}

	#endregion

	#region Input

	private static void HandleInput(ChatPicture picture, Rectangle target, Rectangle area, int screenW, int screenH)
	{
		Point mouse = MusicPlayerWindow.MousePoint;
		KeyboardState keys = Main.keyState;
		bool typing = Main.drawingPlayerChat || Main.editSign || Main.editChest || MusicPlayerWindow.FocusedField != null;
		if (Pressed(keys, Keys.Escape) || MusicPlayerWindow.RightClicked)
		{
			Close();
		}
		if (!typing && _pictures.Count > 1)
		{
			if (Pressed(keys, Keys.Left))
			{
				Show(_index - 1);
			}
			if (Pressed(keys, Keys.Right))
			{
				Show(_index + 1);
			}
		}
		_keys = keys;
		Rectangle shown = Zoomed(target);
		if (MusicPlayerWindow.WheelDelta != 0 && _open >= 1f)
		{
			// Zooms around the point under the mouse.
			float old = _zoom;
			_zoom = MathHelper.Clamp(_zoom * ((MusicPlayerWindow.WheelDelta > 0) ? 1.25f : 0.8f), 1f, 8f);
			Vector2 center = new Vector2(target.Center.X, target.Center.Y) + _pan;
			Vector2 fromCenter = new Vector2(mouse.X, mouse.Y) - center;
			_pan += fromCenter - fromCenter * (_zoom / old);
			if (_zoom <= 1.01f)
			{
				_pan = Vector2.Zero;
			}
			MusicPlayerWindow.WheelDelta = 0;
			shown = Zoomed(target);
		}
		bool overUi = _open >= 1f && (_toolbar.Contains(mouse) || _previousButton.Contains(mouse) || _nextButton.Contains(mouse));
		bool overPicture = !overUi && shown.Contains(mouse) && Rectangle.Intersect(shown, area).Contains(mouse);
		if (overPicture && MusicPlayerWindow.HoveredControl == null)
		{
			MusicDrawing.Hover("chatimage:picture", (_zoom > 1.01f) ? null : "Wheel to zoom, double click for 2x");
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.HoveredControl == "chatimage:picture")
		{
			if (Main.timeForVisualEffects - _lastClick < 18.0)
			{
				_lastClick = -100.0;
				ToggleZoom(target, mouse);
			}
			else
			{
				_lastClick = Main.timeForVisualEffects;
				_dragging = true;
				_dragMoved = false;
				_dragStart = mouse;
				_panStart = _pan;
				MusicPlayerWindow.StartSurfaceDrag();
			}
		}
		else if (MusicPlayerWindow.Clicked && !overPicture && !overUi)
		{
			Close();
		}
		if (_dragging)
		{
			if (!MusicPlayerWindow.MouseDown)
			{
				_dragging = false;
			}
			else if (_zoom > 1.01f)
			{
				_pan = _panStart + new Vector2(mouse.X - _dragStart.X, mouse.Y - _dragStart.Y);
				_dragMoved |= Math.Abs(mouse.X - _dragStart.X) + Math.Abs(mouse.Y - _dragStart.Y) > 3;
			}
		}
		// The picture never leaves the screen completely.
		Vector2 limit = new Vector2(Math.Max(0f, (target.Width * _zoom - area.Width) / 2f + area.Width * 0.25f), Math.Max(0f, (target.Height * _zoom - area.Height) / 2f + area.Height * 0.25f));
		_pan = Vector2.Clamp(_pan, -limit, limit);
	}

	private static void ToggleZoom(Rectangle target, Point mouse)
	{
		if (_zoom > 1.01f)
		{
			_zoom = 1f;
			_pan = Vector2.Zero;
			return;
		}
		_zoom = 2f;
		Vector2 fromCenter = new Vector2(mouse.X - target.Center.X, mouse.Y - target.Center.Y);
		_pan = -fromCenter;
	}

	private static Rectangle Zoomed(Rectangle target)
	{
		Vector2 size = new Vector2(target.Width, target.Height) * _zoom;
		Vector2 center = new Vector2(target.Center.X, target.Center.Y) + _pan;
		return new Rectangle((int)(center.X - size.X / 2f), (int)(center.Y - size.Y / 2f), (int)size.X, (int)size.Y);
	}

	private static bool Pressed(KeyboardState keys, Keys key)
	{
		return keys.IsKeyDown(key) && !_keys.IsKeyDown(key);
	}

	private static Rectangle Lerp(Rectangle a, Rectangle b, float t)
	{
		if (a.Width <= 0)
		{
			return b;
		}
		return new Rectangle((int)MathHelper.Lerp(a.X, b.X, t), (int)MathHelper.Lerp(a.Y, b.Y, t), (int)MathHelper.Lerp(a.Width, b.Width, t), (int)MathHelper.Lerp(a.Height, b.Height, t));
	}

	#endregion

	#region Buttons

	private static void Act(string key, ChatPicture picture, ChatImages.SentPicture mine)
	{
		switch (key)
		{
		case "open":
			ChatLinks.Open(picture.Url);
			break;
		case "link":
			Platform.Get<IClipboard>().Value = picture.Url;
			Note("Link copied");
			break;
		case "copy":
			CopyPicture(picture);
			break;
		case "save":
			Save(picture);
			break;
		case "delete":
			Delete(picture, mine);
			break;
		default:
			Close();
			break;
		}
	}

	private static void Note(string text)
	{
		_note = text;
		_noteUntil = Main.timeForVisualEffects + 120.0;
	}

	private static void CopyPicture(ChatPicture picture)
	{
		byte[] data = picture.Data;
		if (data == null)
		{
			return;
		}
		try
		{
			using System.Drawing.Bitmap bitmap = PictureFiles.ReadBitmap(data);
			if (bitmap == null)
			{
				return;
			}
			// The window's thread is the clipboard's (STA).
			System.Windows.Forms.Clipboard.SetImage(bitmap);
			Note("Picture copied");
		}
		catch (Exception ex)
		{
			Note("Couldn't copy it: " + ex.Message);
		}
	}

	private static void Save(ChatPicture picture)
	{
		try
		{
			Directory.CreateDirectory(ChatImages.SavedFolder);
			string name = picture.Url.Substring(picture.Url.LastIndexOf('/') + 1);
			string path = Path.Combine(ChatImages.SavedFolder, name);
			if (File.Exists(picture.CacheFile))
			{
				File.Copy(picture.CacheFile, path, overwrite: true);
			}
			else if (picture.Data != null)
			{
				File.WriteAllBytes(path, picture.Data);
			}
			Note("Saved as ChatImages/Saved/" + name);
			ClientChat.Info("Picture saved: " + path);
		}
		catch (Exception ex)
		{
			Note("Couldn't save it: " + ex.Message);
		}
	}

	private static void Delete(ChatPicture picture, ChatImages.SentPicture mine)
	{
		Note("Deleting...");
		Task.Run(delegate
		{
			try
			{
				ImgurClient.Delete(mine.DeleteHash);
				ChatImages.ForgetSent(mine);
				Main.QueueMainThreadAction(delegate
				{
					try
					{
						File.Delete(picture.CacheFile);
					}
					catch
					{
					}
					picture.Error = "The picture was deleted.";
					picture.State = PictureState.Failed;
					ClientChat.Info("Picture deleted from Imgur: its link no longer works.");
					Close();
				});
			}
			catch (Exception ex)
			{
				Main.QueueMainThreadAction(delegate
				{
					Note("Couldn't delete it: " + ex.Message);
				});
			}
		});
	}

	#endregion
}
