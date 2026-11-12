using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Little flags for the languages of the translator, drawn with plain shapes (stripes, discs, crosses, stars) so they
/// need no images and look sharp at any panel size. "auto" shows a globe.
/// </summary>
internal static class HubFlags
{
	private static readonly Color White = new Color(245, 245, 245);

	public static void Draw(SpriteBatch sb, string code, Rectangle r, float alpha = 1f)
	{
		if (code == "game")
		{
			code = IsaClient.Community.ChatLanguages.GameLanguage.Code;
		}
		if (code == "auto")
		{
			HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(r.Center.X - r.Height / 2 - 1, r.Y - 1, r.Height + 2, r.Height + 2), Color.White * alpha);
			return;
		}
		HubKit.Fill(sb, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), new Color(10, 12, 20) * (0.75f * alpha));
		Body(sb, code, r, alpha);
		// A soft shine on the top half.
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, r.Width, r.Height / 2), Color.White * (0.08f * alpha));
	}

	private static void Body(SpriteBatch sb, string code, Rectangle r, float a)
	{
		float h = r.Height;
		switch (code)
		{
		case "en":
			Fill(sb, r, new Color(1, 33, 105), a);
			Diagonals(sb, r, White, h * 0.2f, a);
			Diagonals(sb, r, new Color(200, 16, 46), h * 0.08f, a);
			Cross(sb, r, r.Center.X, r.Center.Y, White, (int)Math.Round(h / 3f), a);
			Cross(sb, r, r.Center.X, r.Center.Y, new Color(200, 16, 46), Math.Max(2, (int)Math.Round(h / 5f)), a);
			break;
		case "es":
			Horizontal(sb, r, a, new[] { 1f, 2f, 1f }, new Color(170, 21, 27), new Color(241, 191, 0), new Color(170, 21, 27));
			break;
		case "pt":
			Fill(sb, r, new Color(0, 151, 57), a);
			Rhombus(sb, r, (int)(r.Width * 0.1f), (int)(h * 0.14f), new Color(254, 221, 0), a);
			Disc(sb, r.Center.X, r.Center.Y, h * 0.25f, new Color(1, 33, 105), a);
			break;
		case "fr":
			Vertical(sb, r, a, new Color(0, 35, 149), White, new Color(237, 41, 57));
			break;
		case "de":
			Horizontal(sb, r, a, null, new Color(20, 20, 20), new Color(221, 0, 0), new Color(255, 206, 0));
			break;
		case "it":
			Vertical(sb, r, a, new Color(0, 146, 70), White, new Color(206, 43, 55));
			break;
		case "ru":
			Horizontal(sb, r, a, null, White, new Color(0, 57, 166), new Color(213, 43, 30));
			break;
		case "zh":
			Fill(sb, r, new Color(238, 28, 37), a);
			Star(sb, r.X + h * 0.32f, r.Y + h * 0.32f, h * 0.2f, new Color(255, 222, 0), a);
			Star(sb, r.X + h * 0.68f, r.Y + h * 0.14f, h * 0.06f, new Color(255, 222, 0), a);
			Star(sb, r.X + h * 0.8f, r.Y + h * 0.3f, h * 0.06f, new Color(255, 222, 0), a);
			Star(sb, r.X + h * 0.8f, r.Y + h * 0.5f, h * 0.06f, new Color(255, 222, 0), a);
			Star(sb, r.X + h * 0.68f, r.Y + h * 0.66f, h * 0.06f, new Color(255, 222, 0), a);
			break;
		case "ja":
			Fill(sb, r, White, a);
			Disc(sb, r.Center.X, r.Center.Y, h * 0.3f, new Color(188, 0, 45), a);
			break;
		case "ko":
			Fill(sb, r, White, a);
			Disc(sb, r.Center.X, r.Center.Y, h * 0.25f, new Color(0, 71, 160), a);
			HalfDisc(sb, r.Center.X, r.Center.Y, h * 0.25f, new Color(205, 46, 58), a);
			foreach ((float dx, float dy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
			{
				float cx = r.Center.X + dx * r.Width * 0.33f;
				float cy = r.Center.Y + dy * h * 0.3f;
				Bar(sb, new Vector2(cx, cy), dx * dy > 0 ? -0.6f : 0.6f, h * 0.22f, h * 0.12f, new Color(20, 20, 20), a);
			}
			break;
		case "ar":
			Fill(sb, r, new Color(0, 108, 53), a);
			HubKit.Fill(sb, new Rectangle(r.X + r.Width / 5, r.Y + (int)(h * 0.3f), r.Width * 3 / 5, Math.Max(1, (int)(h * 0.1f))), White * (0.9f * a));
			HubKit.Fill(sb, new Rectangle(r.X + r.Width / 4, r.Y + (int)(h * 0.66f), r.Width / 2, Math.Max(1, (int)(h * 0.08f))), White * (0.9f * a));
			break;
		case "hi":
			Horizontal(sb, r, a, null, new Color(255, 153, 51), White, new Color(19, 136, 8));
			Disc(sb, r.Center.X, r.Center.Y, h * 0.13f, new Color(0, 0, 128), a);
			Disc(sb, r.Center.X, r.Center.Y, h * 0.07f, White, a);
			break;
		case "tr":
			Fill(sb, r, new Color(227, 10, 23), a);
			Disc(sb, r.X + r.Width * 0.38f, r.Center.Y, h * 0.25f, White, a);
			Disc(sb, r.X + r.Width * 0.42f, r.Center.Y, h * 0.2f, new Color(227, 10, 23), a);
			Star(sb, r.X + r.Width * 0.62f, r.Center.Y, h * 0.12f, White, a);
			break;
		case "pl":
			Horizontal(sb, r, a, null, White, new Color(220, 20, 60));
			break;
		case "nl":
			Horizontal(sb, r, a, null, new Color(174, 28, 40), White, new Color(33, 70, 139));
			break;
		case "uk":
			Horizontal(sb, r, a, null, new Color(0, 87, 183), new Color(255, 215, 0));
			break;
		case "id":
			Horizontal(sb, r, a, null, new Color(206, 17, 38), White);
			break;
		case "vi":
			Fill(sb, r, new Color(218, 37, 29), a);
			Star(sb, r.Center.X, r.Center.Y + h * 0.03f, h * 0.3f, new Color(255, 255, 0), a);
			break;
		case "sv":
			Fill(sb, r, new Color(0, 106, 167), a);
			Cross(sb, r, r.X + (int)(r.Width * 0.38f), r.Center.Y, new Color(254, 204, 0), Math.Max(2, (int)Math.Round(h * 0.2f)), a);
			break;
		default:
			Fill(sb, r, new Color(90, 100, 130), a);
			break;
		}
	}

	#region Shapes

	private static void Fill(SpriteBatch sb, Rectangle r, Color c, float a)
	{
		HubKit.Fill(sb, r, c * a);
	}

	private static void Horizontal(SpriteBatch sb, Rectangle r, float a, float[] weights, params Color[] colors)
	{
		weights ??= new float[colors.Length];
		float total = 0f;
		for (int i = 0; i < colors.Length; i++)
		{
			total += (weights[i] == 0f) ? (weights[i] = 1f) : weights[i];
		}
		float y = r.Y;
		for (int i = 0; i < colors.Length; i++)
		{
			float next = y + r.Height * weights[i] / total;
			int top = (int)Math.Round(y);
			int bottom = (i == colors.Length - 1) ? r.Bottom : (int)Math.Round(next);
			HubKit.Fill(sb, new Rectangle(r.X, top, r.Width, bottom - top), colors[i] * a);
			y = next;
		}
	}

	private static void Vertical(SpriteBatch sb, Rectangle r, float a, params Color[] colors)
	{
		for (int i = 0; i < colors.Length; i++)
		{
			int left = r.X + r.Width * i / colors.Length;
			int right = r.X + r.Width * (i + 1) / colors.Length;
			HubKit.Fill(sb, new Rectangle(left, r.Y, right - left, r.Height), colors[i] * a);
		}
	}

	/// <summary>A plus through (x, y) that spans the whole flag.</summary>
	private static void Cross(SpriteBatch sb, Rectangle r, int x, int y, Color c, int thickness, float a)
	{
		HubKit.Fill(sb, new Rectangle(r.X, y - thickness / 2, r.Width, thickness), c * a);
		HubKit.Fill(sb, new Rectangle(x - thickness / 2, r.Y, thickness, r.Height), c * a);
	}

	/// <summary>Both corner to corner lines (the saltire), drawn row by row so they stay inside the flag.</summary>
	private static void Diagonals(SpriteBatch sb, Rectangle r, Color c, float thickness, float a)
	{
		float slope = (float)r.Width / r.Height;
		// A line of this thickness is this wide on each row.
		float half = thickness / 2f * (float)Math.Sqrt(1f + slope * slope);
		for (int y = r.Y; y < r.Bottom; y++)
		{
			float x = (y + 0.5f - r.Y) * slope;
			foreach (float center in new[] { r.X + x, r.Right - x })
			{
				int left = Math.Max(r.X, (int)Math.Round(center - half));
				int right = Math.Min(r.Right, (int)Math.Round(center + half));
				if (right > left)
				{
					HubKit.Fill(sb, new Rectangle(left, y, right - left, 1), c * a);
				}
			}
		}
	}

	/// <summary>A rotated rectangle centered on <paramref name="center"/>.</summary>
	private static void Bar(SpriteBatch sb, Vector2 center, float rotation, float length, float thickness, Color c, float a)
	{
		sb.Draw(HubKit.Pixel, center, new Rectangle(0, 0, 1, 1), c * a, rotation, new Vector2(0.5f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
	}

	private static void Disc(SpriteBatch sb, float cx, float cy, float radius, Color c, float a)
	{
		for (int dy = (int)-Math.Ceiling(radius); dy <= (int)Math.Ceiling(radius); dy++)
		{
			float y = dy + 0.5f;
			float half = radius * radius - y * y;
			if (half <= 0f)
			{
				continue;
			}
			half = (float)Math.Sqrt(half);
			HubKit.Fill(sb, new Rectangle((int)Math.Round(cx - half), (int)cy + dy, Math.Max(1, (int)Math.Round(half * 2f)), 1), c * a);
		}
	}

	/// <summary>The top half of a disc.</summary>
	private static void HalfDisc(SpriteBatch sb, float cx, float cy, float radius, Color c, float a)
	{
		for (int dy = (int)-Math.Ceiling(radius); dy < 0; dy++)
		{
			float y = dy + 0.5f;
			float half = radius * radius - y * y;
			if (half <= 0f)
			{
				continue;
			}
			half = (float)Math.Sqrt(half);
			HubKit.Fill(sb, new Rectangle((int)Math.Round(cx - half), (int)cy + dy, Math.Max(1, (int)Math.Round(half * 2f)), 1), c * a);
		}
	}

	/// <summary>A diamond inset by (<paramref name="padX"/>, <paramref name="padY"/>) from the flag's edges.</summary>
	private static void Rhombus(SpriteBatch sb, Rectangle r, int padX, int padY, Color c, float a)
	{
		int top = r.Y + padY;
		int bottom = r.Bottom - padY;
		float halfHeight = (bottom - top) / 2f;
		float halfWidth = (r.Width - padX * 2) / 2f;
		for (int y = top; y < bottom; y++)
		{
			float t = 1f - Math.Abs(y + 0.5f - (top + halfHeight)) / halfHeight;
			int w = (int)Math.Round(halfWidth * t);
			HubKit.Fill(sb, new Rectangle(r.Center.X - w, y, w * 2, 1), c * a);
		}
	}

	/// <summary>A five-pointed star: five spikes around a small disc.</summary>
	private static void Star(SpriteBatch sb, float cx, float cy, float radius, Color c, float a)
	{
		if (radius < 2.5f)
		{
			Disc(sb, cx, cy, Math.Max(1f, radius), c, a);
			return;
		}
		for (int i = 0; i < 5; i++)
		{
			float angle = -MathHelper.PiOver2 + i * MathHelper.TwoPi / 5f;
			Vector2 tip = new Vector2(cx, cy) + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius * 0.5f;
			Bar(sb, tip, angle, radius, radius * 0.38f, c, a);
		}
		Disc(sb, cx, cy, radius * 0.4f, c, a);
	}

	#endregion
}
