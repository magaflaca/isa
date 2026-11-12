using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Music;

/// <summary>The small floating music player.</summary>
public static class MiniPlayer
{
	internal static void DrawMini(SpriteBatch sb, int screenW, int screenH)
	{
		MusicPlayer.Track current = MusicPlayer.Current;
		int w = (MusicPlayerWindow.MiniCollapsed ? 260 : 340);
		int h = (MusicPlayerWindow.MiniCollapsed ? 40 : 104);
		Rectangle r = MusicPlayerWindow.ClampToScreen(ref MusicPlayerWindow.MiniPosition, w, h, screenW, screenH, new Vector2(screenW - w - 20, screenH - h - 150));
		MusicPlayerWindow.MiniRect = r;
		MusicPlayerWindow.MiniScreenRect = MusicPlayerWindow.ToScreen(r);
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.96f);
		string key = current?.Id;
		string title = ((current != null) ? (current.Title ?? current.Source) : (MusicPlayer.Loading ? "Loading..." : "Nothing playing"));
		Texture2D playPause = MusicDrawing.Tex("Images/UI/IconPlayPause");
		Rectangle playSrc = new Rectangle(0, MusicPlayer.IsPlaying ? 16 : 0, 16, 16);
		if (MusicPlayerWindow.MiniCollapsed)
		{
			MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, new Rectangle(r.X + 8, r.Y + 6, 22, 28), Color.White);
			if (MusicDrawing.IconButton(sb, "mini:play", new Rectangle(r.X + 34, r.Y + 6, 28, 28), playPause, playSrc, MusicPlayer.IsPlaying ? "Pause" : "Play"))
			{
				MusicPlayerWindow.TogglePlay();
			}
			Marquee(sb, title, new Rectangle(r.X + 68, r.Y + 11, w - 68 - 56, 20), Color.White, 0.78f);
			if (MusicDrawing.BareIcon(sb, "mini:expand", new Rectangle(r.Right - 48, r.Y + 11, 18, 18), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(0, 0, 32, 32), "Expand", Color.White))
			{
				MusicPlayerWindow.MiniCollapsed = false;
				MusicPlayer.Save();
			}
			if (MusicDrawing.BareIcon(sb, "mini:close", new Rectangle(r.Right - 24, r.Y + 11, 16, 17), MusicDrawing.Tex("Images/MapDeath"), null, "Close the mini player (.miniplayer brings it back)", Color.White))
			{
				MusicPlayerWindow.MiniVisible = false;
				MusicPlayer.Save();
			}
			MusicPlayerWindow.HandleDrag(ref MusicPlayerWindow.DraggingMini, ref MusicPlayerWindow.MiniPosition, r, null);
			return;
		}
		MusicDrawing.Cover(sb, key, new Rectangle(r.X + 10, r.Y + 10, 60, 60));
		float textX = r.X + 80;
		float textW = r.Right - textX - 74;
		Marquee(sb, title, new Rectangle((int)textX, r.Y + 12, (int)textW, 20), Color.White, 0.85f);
		string sub = StatusLine();
		MusicDrawing.Text(sb, sub, textX, r.Y + 34, MusicPlayerWindow.MutedColor, 0.68f, textW);
		// Top-right: open the window, collapse, close.
		if (MusicDrawing.BareIcon(sb, "mini:window", new Rectangle(r.Right - 66, r.Y + 8, 18, 22), MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, MusicPlayerWindow.WindowOpen ? "Close the music window" : "Open the music window (.musicui)", MusicPlayerWindow.WindowOpen ? MusicPlayerWindow.AccentColor : Color.White))
		{
			MusicPlayerWindow.ToggleWindow();
		}
		if (MusicDrawing.BareIcon(sb, "mini:collapse", new Rectangle(r.Right - 44, r.Y + 10, 18, 18), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), "Collapse", Color.White))
		{
			MusicPlayerWindow.MiniCollapsed = true;
			MusicPlayer.Save();
		}
		if (MusicDrawing.BareIcon(sb, "mini:close", new Rectangle(r.Right - 22, r.Y + 10, 15, 16), MusicDrawing.Tex("Images/MapDeath"), null, "Close the mini player (.miniplayer brings it back)", Color.White))
		{
			MusicPlayerWindow.MiniVisible = false;
			MusicPlayer.Save();
		}
		// Controls.
		int cy = r.Y + 52;
		int cx = (int)textX;
		MusicPlayerWindow.Controls(sb, "mini", cx, cy, 24, key, small: true);
		// Progress.
		MusicPlayerWindow.Seekbar(sb, "mini", new Rectangle(r.X + 10, r.Bottom - 22, w - 20, 6), showTimes: false);
		MusicDrawing.Text(sb, MusicPlayer.FormatTime(MusicPlayer.Position), r.X + 10, r.Bottom - 15, MusicPlayerWindow.MutedColor, 0.6f);
		string len = MusicPlayer.FormatTime(MusicPlayer.Length);
		MusicDrawing.Text(sb, len, r.Right - 10 - MusicDrawing.Measure(len, 0.6f), r.Bottom - 15, MusicPlayerWindow.MutedColor, 0.6f);
		MusicPlayerWindow.HandleDrag(ref MusicPlayerWindow.DraggingMini, ref MusicPlayerWindow.MiniPosition, r, null);
	}

	internal static string StatusLine()
	{
		List<string> parts = new List<string>();
		if (MusicPlayer.Loading)
		{
			parts.Add("Downloading...");
		}
		else if (MusicPlayer.HasSong)
		{
			parts.Add(MusicPlayer.IsPlaying ? "Playing" : "Paused");
		}
		if (MusicPlayer.Shuffle)
		{
			parts.Add("Shuffle");
		}
		if (MusicPlayer.Loop == MusicPlayer.LoopMode.Song)
		{
			parts.Add("Repeat one");
		}
		else if (MusicPlayer.Loop == MusicPlayer.LoopMode.Queue)
		{
			parts.Add("Repeat all");
		}
		int queued = MusicPlayer.QueueCount;
		if (queued > 0)
		{
			parts.Add(queued + " up next");
		}
		return string.Join("  •  ", parts);
	}

	/// <summary>Long titles slide back and forth inside their box.</summary>
	internal static void Marquee(SpriteBatch sb, string text, Rectangle r, Color color, float scale)
	{
		text = text ?? "";
		float width = MusicDrawing.Measure(text, scale);
		if (width <= r.Width)
		{
			MusicDrawing.Text(sb, text, r.X, r.Y, color, scale);
			return;
		}
		float overflow = width - r.Width + 12f;
		double t = Main.timeForVisualEffects / 60.0;
		double cycle = 4.0 + overflow / 25.0;
		double phase = t % (cycle * 2.0);
		double p = (phase < cycle) ? (phase / cycle) : (2.0 - phase / cycle);
		float offset = (float)(MathHelper.Clamp((float)(p * 1.4 - 0.2), 0f, 1f) * overflow);
		bool wasClipping = MusicPlayerWindow.Clipping;
		Rectangle oldClip = MusicPlayerWindow.ClipRect;
		Rectangle area = new Rectangle(r.X, r.Y - 2, r.Width, r.Height + 6);
		if (wasClipping)
		{
			area = Rectangle.Intersect(area, oldClip);
		}
		MusicPlayerWindow.BeginClip(sb, area);
		MusicDrawing.Text(sb, text, r.X - offset, r.Y, color, scale);
		MusicPlayerWindow.EndClip(sb);
		if (wasClipping)
		{
			MusicPlayerWindow.BeginClip(sb, oldClip);
		}
	}
}
