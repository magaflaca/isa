using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Casino;

/// <summary>
/// The casino's drawing. The Moon Lord dealer is Terraria's own Moon Lord (its textures, put together the way the game
/// draws the boss, smaller, resting its hands on the table's far edge); cards, chips, pills and rings come from
/// IsaClient.Assets/Casino.png (made by tools/casino/make_art.py); the room, the table, chairs, the shoe and the chip
/// rack are drawn here; people sit as their Terraria character. No soft glows: crisp shapes and clean colours.
/// </summary>
public static class CasinoArt
{
	private static Texture2D _atlas;

	private static bool _failed;

	public static Texture2D Atlas
	{
		get
		{
			if (_atlas == null && !_failed)
			{
				try
				{
					using Stream stream = typeof(CasinoArt).Assembly.GetManifestResourceStream("IsaClient.Assets.Casino.png");
					_atlas = Texture2D.FromStream(Main.instance.GraphicsDevice, stream);
				}
				catch (Exception ex)
				{
					_failed = true;
					ClientLog.LogException(ex);
				}
			}
			return _atlas;
		}
	}

	// Atlas layout (see tools/casino/make_art.py).
	public const int CardW = 40;

	public const int CardH = 56;

	private static readonly Rectangle CardFace = new Rectangle(0, 0, CardW, CardH);

	private static readonly Rectangle CardBack = new Rectangle(41, 0, CardW, CardH);

	private static readonly Rectangle ButtonSrc = new Rectangle(82, 0, 18, 18);

	private static Rectangle BigSuit(int suit) => new Rectangle(suit * 12, 57, 11, 11);

	private static Rectangle Glyph(int rank) => new Rectangle(rank * 10, 69, (rank == 8) ? 9 : 7, 9);

	private static Rectangle ChipTop(int kind) => new Rectangle(kind * 19, 79, 18, 18);

	private static Rectangle ChipSide(int kind) => new Rectangle(kind * 19, 98, 18, 5);

	private static readonly Rectangle Carpet = new Rectangle(150, 0, 32, 32);

	private static readonly Rectangle PillSrc = new Rectangle(0, 104, 24, 14);

	private static readonly Rectangle ChipShadowSrc = new Rectangle(25, 104, 20, 6);

	private static readonly Rectangle RingSrc = new Rectangle(46, 104, 44, 44);

	private static readonly Rectangle CardShadowSrc = new Rectangle(91, 104, CardW, CardH);

	public static readonly Color Cream = new Color(255, 244, 222);

	public static readonly Color Gold = new Color(255, 208, 96);

	public static readonly Color SoftRed = new Color(255, 120, 110);

	public static readonly Color SoftGreen = new Color(130, 236, 140);

	public static readonly Color Ink = new Color(28, 28, 36);

	public static readonly Color RedInk = new Color(204, 28, 44);

	/// <summary>The pills' fill: a deep night blue, like Governor's labels.</summary>
	public static readonly Color PillFill = new Color(18, 22, 44);

	private static void Px(SpriteBatch sb, Rectangle src, Vector2 at, float scale, Color color, SpriteEffects fx = SpriteEffects.None)
	{
		Texture2D atlas = Atlas;
		if (atlas != null)
		{
			sb.Draw(atlas, new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y)), src, color, 0f, Vector2.Zero, scale, fx, 0f);
		}
	}

	#region Cards

	public static Vector2 CardSize(float scale) => new Vector2(CardW * scale, CardH * scale);

	/// <summary>
	/// A card with its top-left at <paramref name="at"/>: only its number and suit, big. <paramref name="turn"/>
	/// squeezes it sideways (1 flat, 0 edge-on) to flip it over; <paramref name="lit"/> rims it in gold (part of the
	/// winning hand).
	/// </summary>
	public static void Card(SpriteBatch sb, Vector2 at, float scale, byte card, bool faceUp, float alpha = 1f, float turn = 1f, bool lit = false)
	{
		Texture2D atlas = Atlas;
		if (atlas == null)
		{
			return;
		}
		turn = Math.Max(0.02f, Math.Min(1f, turn));
		float w = CardW * scale;
		Vector2 origin = new Vector2((float)Math.Round(at.X + w * (1f - turn) / 2f), (float)Math.Round(at.Y));
		Vector2 sc = new Vector2(scale * turn, scale);
		// a crisp shadow, just under it
		sb.Draw(atlas, origin + new Vector2(scale * 1.5f, scale * 2f), CardShadowSrc, Color.Black * (0.32f * alpha), 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
		if (lit)
		{
			// a gold rim: the card's shape, a little bigger, behind it
			float pad = 2f;
			sb.Draw(atlas, origin - new Vector2(pad, pad), CardShadowSrc, Gold * alpha, 0f, Vector2.Zero, new Vector2((w * turn + pad * 2f) / CardW, (CardH * scale + pad * 2f) / CardH), SpriteEffects.None, 0f);
		}
		Color white = Color.White * alpha;
		if (!faceUp || card == Cards.NoCard)
		{
			sb.Draw(atlas, origin, CardBack, white, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
			return;
		}
		sb.Draw(atlas, origin, CardFace, white, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
		int rank = Cards.Rank(card);
		int suit = Cards.Suit(card);
		Color ink = (Cards.Red(card) ? RedInk : Ink) * alpha;
		void Part(Rectangle src, float x, float y, float size)
		{
			sb.Draw(atlas, origin + new Vector2((float)Math.Round(x * scale * turn), (float)Math.Round(y * scale)), src, ink, 0f, Vector2.Zero, new Vector2(scale * turn * size, scale * size), SpriteEffects.None, 0f);
		}
		Part(Glyph(rank), 4f, 4f, 2f);
		Part(BigSuit(suit), 4f, 25f, 1f);
		Part(BigSuit(suit), 17f, 30f, 2f);
	}

	public static void DealerButton(SpriteBatch sb, Vector2 center, float scale)
	{
		Px(sb, ChipShadowSrc, center + new Vector2(-10f, 6f) * scale, scale, Color.Black * 0.35f);
		Px(sb, ButtonSrc, center - new Vector2(9f, 9f) * scale, scale, Color.White);
	}

	/// <summary>A betting spot printed on the felt: a double gold ring.</summary>
	public static void BetRing(SpriteBatch sb, Vector2 center, float scale, Color color)
	{
		Px(sb, RingSrc, center - new Vector2(22f, 22f) * scale, scale, color);
	}

	#endregion

	#region Chips

	private static readonly int[] Values = { 1000, 500, 100, 25, 5, 1 };

	private static int KindOf(int value)
	{
		switch (value)
		{
		case 1000:
			return 5;
		case 500:
			return 4;
		case 100:
			return 3;
		case 25:
			return 2;
		case 5:
			return 1;
		default:
			return 0;
		}
	}

	/// <summary>
	/// Chips worth <paramref name="amount"/> standing at <paramref name="bottom"/> (bottom middle): a stack per colour,
	/// side by side, each on its own small shadow. Returns the width used.
	/// </summary>
	public static float Chips(SpriteBatch sb, Vector2 bottom, int amount, float scale, int tallest = 8, float alpha = 1f)
	{
		if (amount <= 0 || Atlas == null)
		{
			return 0f;
		}
		List<(int kind, int count)> stacks = new List<(int, int)>();
		int left = amount;
		foreach (int v in Values)
		{
			int n = left / v;
			if (n > 0)
			{
				stacks.Add((KindOf(v), Math.Min(n, tallest)));
				left -= n * v;
			}
			if (stacks.Count == 4)
			{
				break;
			}
		}
		float step = 17f * scale;
		float width = (stacks.Count - 1) * step + 18f * scale;
		float x = bottom.X - width / 2f;
		Color color = Color.White * alpha;
		foreach ((int kind, int count) in stacks)
		{
			Px(sb, ChipShadowSrc, new Vector2(x - scale, bottom.Y - 3f * scale), scale, Color.Black * (0.4f * alpha));
		}
		foreach ((int kind, int count) in stacks)
		{
			for (int i = 0; i < count; i++)
			{
				Px(sb, ChipSide(kind), new Vector2(x, bottom.Y - 5f * scale - i * 3f * scale), scale, color);
			}
			Px(sb, ChipTop(kind), new Vector2(x, bottom.Y - 5f * scale - count * 3f * scale - 15f * scale), scale, color);
			x += step;
		}
		return width;
	}

	public static void Chip(SpriteBatch sb, Vector2 center, int value, float scale, float alpha = 1f)
	{
		Px(sb, ChipTop(KindOf(value)), center - new Vector2(9f, 9f) * scale, scale, Color.White * alpha);
	}

	public static int ChipFor(int amount)
	{
		foreach (int v in Values)
		{
			if (amount >= v)
			{
				return v;
			}
		}
		return 1;
	}

	/// <summary>
	/// A crisp rounded label (three pieces: round ends, a stretched middle) with text, centred at
	/// <paramref name="center"/>; a chip before the text when <paramref name="chip"/>. Returns its box.
	/// </summary>
	public static Rectangle Pill(SpriteBatch sb, Vector2 center, string text, Color color, float scale = 0.62f, bool chip = true, Color? fill = null)
	{
		Texture2D atlas = Atlas;
		float tw = MusicDrawing.Measure(text, scale);
		int h = 22;
		int w = (int)tw + (chip ? 34 : 22);
		Rectangle r = new Rectangle((int)Math.Round(center.X - w / 2f), (int)Math.Round(center.Y - h / 2f), w, h);
		if (atlas != null)
		{
			GameArt.Sharp(sb);
			Color rim = new Color(6, 6, 12) * 0.9f;
			Color inside = (fill ?? PillFill) * 0.92f;
			float k = h / 14f;
			int cap = (int)Math.Round(7 * k);
			// the rim (the whole pill a little darker), then the fill drawn by the same pieces
			foreach ((Color c, int inset) in new[] { (rim, 0), (inside, 1) })
			{
				Rectangle rr = new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
				int cw = Math.Min(cap - inset, rr.Width / 2);
				sb.Draw(atlas, new Rectangle(rr.X, rr.Y, cw, rr.Height), new Rectangle(PillSrc.X + 1, PillSrc.Y + 1, 6, 12), c);
				sb.Draw(atlas, new Rectangle(rr.X + cw, rr.Y, rr.Width - cw * 2, rr.Height), new Rectangle(PillSrc.X + 8, PillSrc.Y + 1, 8, 12), c);
				sb.Draw(atlas, new Rectangle(rr.Right - cw, rr.Y, cw, rr.Height), new Rectangle(PillSrc.X + 17, PillSrc.Y + 1, 6, 12), c);
			}
			if (chip)
			{
				Chip(sb, new Vector2(r.X + 12, r.Center.Y), 1000, 0.8f);
			}
			GameArt.Smooth(sb);
		}
		MusicDrawing.RawText(sb, text, chip ? (r.X + 24) : (r.X + 11), r.Y + 4, color, scale);
		return r;
	}

	#endregion

	#region The room and the table

	/// <summary>The casino around the table: dark wood walls with a gold rail, a red casino carpet.</summary>
	public static void Room(SpriteBatch sb, Rectangle r, float wallPart)
	{
		int wallH = (int)(r.Height * wallPart);
		Rectangle wall = new Rectangle(r.X, r.Y, r.Width, wallH);
		GameArt.Gradient(sb, wall, new Color(40, 22, 18), new Color(66, 38, 28));
		for (int x = r.X + 30; x < r.Right; x += 86)
		{
			MusicDrawing.Fill(sb, new Rectangle(x, wall.Y, 2, wallH - 12), Color.Black * 0.22f);
			MusicDrawing.Fill(sb, new Rectangle(x + 2, wall.Y, 1, wallH - 12), new Color(255, 200, 150) * 0.05f);
		}
		MusicDrawing.Fill(sb, new Rectangle(r.X, wall.Bottom - 10, r.Width, 6), new Color(120, 82, 36));
		MusicDrawing.Fill(sb, new Rectangle(r.X, wall.Bottom - 10, r.Width, 1), new Color(255, 214, 120) * 0.6f);
		MusicDrawing.Fill(sb, new Rectangle(r.X, wall.Bottom - 4, r.Width, 4), new Color(26, 14, 10));
		Rectangle floor = new Rectangle(r.X, wall.Bottom, r.Width, r.Bottom - wall.Bottom);
		Texture2D atlas = Atlas;
		if (atlas != null)
		{
			GameArt.Sharp(sb);
			const int tile = 64;
			for (int y = floor.Y; y < floor.Bottom; y += tile)
			{
				for (int x = floor.X; x < floor.Right; x += tile)
				{
					int w = Math.Min(tile, floor.Right - x);
					int h = Math.Min(tile, floor.Bottom - y);
					sb.Draw(atlas, new Rectangle(x, y, w, h), new Rectangle(Carpet.X, Carpet.Y, w / 2, h / 2), Color.White);
				}
			}
			GameArt.Smooth(sb);
		}
		MusicDrawing.Fill(sb, floor, Color.Black * 0.38f);
		GameArt.Gradient(sb, new Rectangle(floor.X, floor.Y, floor.Width, Math.Min(36, floor.Height)), Color.Black * 0.5f, Color.Transparent);
	}

	/// <summary>A stadium (a rectangle with round ends) drawn row by row.</summary>
	private static void Stadium(SpriteBatch sb, Rectangle r, Func<float, Color> colorAt)
	{
		float radius = r.Height / 2f;
		for (int y = 0; y < r.Height; y++)
		{
			float dy = y + 0.5f - radius;
			float half = (float)Math.Sqrt(Math.Max(0.0, radius * radius - dy * dy));
			int x0 = (int)Math.Round(r.X + radius - half);
			int x1 = (int)Math.Round(r.Right - radius + half);
			MusicDrawing.Fill(sb, new Rectangle(x0, r.Y + y, x1 - x0, 1), colorAt((float)y / r.Height));
		}
	}

	/// <summary>
	/// The table: a crisp shadow, a thick dark wood rail with a highlight, the felt in one lively colour (a little
	/// lighter in the middle) and a printed gold line. Returns the felt.
	/// </summary>
	public static Rectangle Table(SpriteBatch sb, Rectangle r, Color felt, Color feltEdge)
	{
		Stadium(sb, new Rectangle(r.X + 2, r.Y + 10, r.Width - 4, r.Height), _ => Color.Black * 0.35f);
		Stadium(sb, r, t => Color.Lerp(new Color(92, 52, 30), new Color(42, 22, 14), t));
		Stadium(sb, new Rectangle(r.X + 4, r.Y + 3, r.Width - 8, r.Height - 10), t => Color.Lerp(new Color(132, 78, 42), new Color(70, 38, 22), (float)Math.Pow(t, 0.85)));
		Rectangle f = new Rectangle(r.X + 20, r.Y + 18, r.Width - 40, r.Height - 36);
		// a thin dark lip where the felt meets the wood
		Stadium(sb, new Rectangle(f.X - 2, f.Y - 2, f.Width + 4, f.Height + 4), _ => new Color(30, 16, 10));
		float cx = f.Center.X;
		float cy = f.Center.Y;
		Color FeltAt(float y)
		{
			float t = Math.Abs((y - cy) / (f.Height / 2f));
			return Color.Lerp(felt, feltEdge, t * t);
		}
		Stadium(sb, f, t => FeltAt(f.Y + t * f.Height));
		Rectangle line = new Rectangle(f.X + 30, f.Y + 26, f.Width - 60, f.Height - 52);
		Stadium(sb, line, _ => Color.Lerp(felt, Gold, 0.55f));
		Rectangle inner = new Rectangle(line.X + 2, line.Y + 2, line.Width - 4, line.Height - 4);
		Stadium(sb, inner, t => FeltAt(inner.Y + t * inner.Height));
		// the rail's shine along the top
		Stadium(sb, new Rectangle(r.X + 40, r.Y + 4, r.Width - 80, 4), _ => new Color(255, 214, 160) * 0.16f);
		return f;
	}

	/// <summary>Printed text on the felt (no shadow): centred, optionally on an arc (radius in pixels, a smile).</summary>
	public static void Print(SpriteBatch sb, string text, Vector2 center, float scale, Color color, float radius = 0f)
	{
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		if (radius <= 0f)
		{
			Vector2 size = font.MeasureString(text) * scale;
			sb.DrawString(font, text, new Vector2((float)Math.Round(center.X - size.X / 2f), (float)Math.Round(center.Y - size.Y / 2f)), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			return;
		}
		// on an arc whose lowest point is at center
		Vector2 o = center - new Vector2(0f, radius);
		float total = font.MeasureString(text).X * scale;
		float a = (float)Math.PI / 2f + total / radius / 2f;
		foreach (char ch in text)
		{
			string s = ch.ToString();
			float w = font.MeasureString(s).X * scale;
			a -= w / 2f / radius;
			Vector2 p = o + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;
			sb.DrawString(font, s, p, color, a - (float)Math.PI / 2f, new Vector2(font.MeasureString(s).X / 2f, 12f), scale, SpriteEffects.None, 0f);
			a -= w / 2f / radius;
		}
	}

	/// <summary>The dealing shoe: a dark wooden box with the next card showing, its mouth toward the players.</summary>
	public static void Shoe(SpriteBatch sb, Vector2 at)
	{
		Rectangle body = new Rectangle((int)at.X - 26, (int)at.Y - 16, 52, 32);
		MusicDrawing.Fill(sb, new Rectangle(body.X + 3, body.Bottom, body.Width - 2, 4), Color.Black * 0.35f);
		// the back of the box, the next card sticking out of it, then the box's front over the card's bottom
		MusicDrawing.Fill(sb, new Rectangle(body.X, body.Y, body.Width, 12), new Color(30, 16, 12));
		MusicDrawing.Fill(sb, new Rectangle(body.X + 2, body.Y + 2, body.Width - 4, 10), new Color(70, 40, 24));
		GameArt.Sharp(sb);
		Card(sb, new Vector2(body.X + 13, body.Y - 12), 0.62f, Cards.NoCard, false);
		GameArt.Smooth(sb);
		// the front: wood with a brass rim along its top edge (no dark line across the card)
		Rectangle front = new Rectangle(body.X, body.Y + 10, body.Width, body.Height - 10);
		MusicDrawing.Fill(sb, new Rectangle(front.X, front.Y + 2, front.Width, front.Height - 2), new Color(30, 16, 12));
		GameArt.Gradient(sb, new Rectangle(front.X + 2, front.Y + 2, front.Width - 4, front.Height - 4), new Color(118, 70, 40), new Color(62, 36, 22));
		MusicDrawing.Fill(sb, new Rectangle(front.X, front.Y, front.Width, 2), new Color(214, 170, 90));
		MusicDrawing.Fill(sb, new Rectangle(front.X + 6, front.Bottom - 7, front.Width - 12, 2), Gold * 0.6f);
	}

	/// <summary>The house's chip rack: a wooden tray with columns of chips by colour.</summary>
	public static void ChipRack(SpriteBatch sb, Vector2 center)
	{
		Rectangle tray = new Rectangle((int)center.X - 64, (int)center.Y - 16, 128, 32);
		MusicDrawing.Fill(sb, new Rectangle(tray.X + 3, tray.Bottom, tray.Width - 2, 4), Color.Black * 0.35f);
		MusicDrawing.Fill(sb, tray, new Color(30, 16, 12));
		GameArt.Gradient(sb, new Rectangle(tray.X + 2, tray.Y + 2, tray.Width - 4, tray.Height - 4), new Color(100, 60, 34), new Color(56, 32, 20));
		GameArt.Sharp(sb);
		int[] kinds = { 5, 4, 3, 2, 1, 0 };
		for (int col = 0; col < kinds.Length; col++)
		{
			for (int i = 0; i < 4; i++)
			{
				Px(sb, ChipSide(kinds[col]), new Vector2(tray.X + 5 + col * 20, tray.Y + 21 - i * 4), 1f, Color.White);
			}
		}
		GameArt.Smooth(sb);
	}

	#endregion

	#region The Moon Lord

	private static Texture2D T(string path) => MusicDrawing.Tex("Images/" + path);

	/// <summary>The hands can reach this far from the shoulders (two arm pieces of 340, in the game's size).</summary>
	private const float Reach = 600f;

	private static Vector2 Shoulder(bool left) => new Vector2(left ? -220f : 220f, -60f);

	/// <summary>Where the hands rest (game units from the core): out to the sides on the table's edge, like the boss holds them.</summary>
	public static Vector2 Rest(bool left) => new Vector2(left ? -330f : 330f, 30f);

	private static Vector2 Reachable(Vector2 hand, bool left)
	{
		Vector2 s = Shoulder(left);
		// never toward the other side of the body (the arms would cross the chest)
		hand.X = left ? Math.Min(hand.X, -150f) : Math.Max(hand.X, 150f);
		Vector2 d = hand + new Vector2(0f, 76f) - s;
		float len = d.Length();
		if (len > Reach)
		{
			hand = s + d * (Reach / len) - new Vector2(0f, 76f);
		}
		return hand;
	}

	/// <summary>
	/// The whole Moon Lord, drawn behind the table: upper arms, body with its open chest and beating core, forearms and
	/// hands (each at <see cref="Rest"/> plus its move, game units; frames 0 open .. 3 fist), and the head (tentacles
	/// moving, eyes looking at <paramref name="look"/>, a unit vector).
	/// </summary>
	public static void MoonLord(SpriteBatch sb, Vector2 core, float scale, Vector2 leftMove, Vector2 rightMove, int leftFrame, int rightFrame, Vector2 look)
	{
		Vector2 P(Vector2 local) => core + local * scale;
		Vector2[] hands =
		{
			Reachable(Rest(true) + leftMove, true),
			Reachable(Rest(false) + rightMove, false)
		};
		Texture2D upper = T("Extra_14");
		for (int i = 0; i < 2; i++)
		{
			bool left = i == 0;
			float d = left ? -1f : 1f;
			Vector2 shoulder = Shoulder(left);
			Vector2 v = (hands[i] + new Vector2(0f, 76f) - shoulder) * 0.5f;
			float bend = (float)Math.Acos(Math.Min(1f, v.Length() / 340f)) * (-d);
			Vector2 origin = left ? new Vector2(76f, 66f) : new Vector2(upper.Width - 76f, 66f);
			sb.Draw(upper, P(shoulder), null, Color.White, v.ToRotation() - bend - (float)Math.PI / 2f, origin, scale, left ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
		}
		Texture2D body = T("Extra_13");
		sb.Draw(body, core, null, Color.White, 0f, new Vector2(body.Width, 278f), scale, SpriteEffects.None, 0f);
		sb.Draw(body, core, null, Color.White, 0f, new Vector2(0f, 278f), scale, SpriteEffects.FlipHorizontally, 0f);
		sb.Draw(T("Extra_16"), core, null, Color.White, 0f, new Vector2(112f, 101f), scale, SpriteEffects.None, 0f);
		Main.instance.LoadNPC(398);
		Texture2D heart = TextureAssets.Npc[398].Value;
		int fh = heart.Height / 5;
		int beat = 1 + (int)(GameArt.Now * 5.0) % 4;
		sb.Draw(heart, core, new Rectangle(0, fh * beat, heart.Width, fh), Color.White, 0f, new Vector2(heart.Width / 2f, fh / 2f), scale, SpriteEffects.None, 0f);
		// forearms and hands
		Texture2D fore = T("Extra_15");
		Texture2D white = T("Extra_17");
		Texture2D pupil = T("Extra_19");
		Main.instance.LoadNPC(397);
		Texture2D handTex = TextureAssets.Npc[397].Value;
		int hfh = handTex.Height / 4;
		for (int i = 0; i < 2; i++)
		{
			bool left = i == 0;
			float d = left ? -1f : 1f;
			SpriteEffects fx = left ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
			Vector2 h = hands[i];
			Vector2 elbowFrom = h + new Vector2(0f, 76f);
			Vector2 v = (Shoulder(left) - elbowFrom) * 0.5f;
			float bend = (float)Math.Acos(Math.Min(1f, v.Length() / 340f)) * (-d);
			Vector2 origin = left ? new Vector2(60f, 30f) : new Vector2(fore.Width - 60f, 30f);
			sb.Draw(fore, P(elbowFrom), null, Color.White, v.ToRotation() + bend - (float)Math.PI / 2f, origin, scale, fx, 0f);
			sb.Draw(white, P(h), null, Color.White, 0f, left ? new Vector2(26f, 42f) : new Vector2(white.Width - 26f, 42f), scale, fx, 0f);
			sb.Draw(pupil, P(h + new Vector2(-d, 3f) + look * new Vector2(10f, 18f)), null, Color.White, 0f, new Vector2(16f, 16f), scale, SpriteEffects.None, 0f);
			int f = Math.Max(0, Math.Min(3, left ? leftFrame : rightFrame));
			sb.Draw(handTex, P(h), new Rectangle(0, f * hfh, handTex.Width, hfh), Color.White, 0f, left ? new Vector2(120f, 180f) : new Vector2(handTex.Width - 120f, 180f), scale, fx, 0f);
		}
		// the head
		Vector2 head = new Vector2(0f, -400f);
		sb.Draw(T("Extra_18"), P(head), null, Color.White, 0f, new Vector2(19f, 34f), scale, SpriteEffects.None, 0f);
		sb.Draw(T("Extra_19"), P(head + look * new Vector2(12f, 26f)), null, Color.White, 0f, new Vector2(16f, 16f), scale, SpriteEffects.None, 0f);
		Main.instance.LoadNPC(396);
		Texture2D face = TextureAssets.Npc[396].Value;
		int frame = (int)(GameArt.Now * 15.0) % 8;
		Rectangle src = face.Frame(3, 3, frame / 3, frame % 3);
		sb.Draw(face, P(head), src, Color.White, 0f, new Vector2(191f, 130f) / 2f, scale * 2f, SpriteEffects.None, 0f);
		Texture2D mouth = T("Extra_25");
		Rectangle mouthSrc = mouth.Frame(1, 3, 0, 0);
		sb.Draw(mouth, P(head + new Vector2(0f, 214f)), mouthSrc, Color.White, 0f, mouthSrc.Size() / 2f, scale, SpriteEffects.None, 0f);
	}

	/// <summary>Where a hand is on the screen (its rest plus its move).</summary>
	public static Vector2 HandOnScreen(Vector2 core, float scale, Vector2 move, bool left) => core + Reachable(Rest(left) + move, left) * scale;

	#endregion

	#region Furniture, plates and buttons

	/// <summary>A casino chair seen from the side: gold frame, red velvet seat and back (the back away from the table).</summary>
	public static void Chair(SpriteBatch sb, Vector2 feet, bool faceRight, float scale)
	{
		Color gold = new Color(196, 148, 60);
		Color goldDark = new Color(120, 82, 30);
		Color velvet = new Color(150, 28, 44);
		Color velvetDark = new Color(96, 16, 28);
		float s = scale;
		int seatY = (int)(feet.Y - 15 * s);
		int seatW = (int)(24 * s);
		int x0 = (int)(feet.X - seatW / 2f);
		Px(sb, ChipShadowSrc, new Vector2(feet.X - 15f * s, feet.Y - 2f * s), 1.5f * s, Color.Black * 0.35f);
		MusicDrawing.Fill(sb, new Rectangle(x0 + (int)(2 * s), seatY + (int)(4 * s), (int)(2 * s), (int)(11 * s)), goldDark);
		MusicDrawing.Fill(sb, new Rectangle(x0 + seatW - (int)(4 * s), seatY + (int)(4 * s), (int)(2 * s), (int)(11 * s)), goldDark);
		MusicDrawing.Fill(sb, new Rectangle(x0 + (int)(3 * s), seatY + (int)(9 * s), seatW - (int)(6 * s), (int)s), goldDark);
		int backX = faceRight ? x0 - (int)(1 * s) : x0 + seatW - (int)(4 * s);
		MusicDrawing.Fill(sb, new Rectangle(backX, seatY - (int)(22 * s), (int)(5 * s), (int)(26 * s)), goldDark);
		MusicDrawing.Fill(sb, new Rectangle(backX + (int)s, seatY - (int)(21 * s), (int)(3 * s), (int)(20 * s)), velvet);
		MusicDrawing.Fill(sb, new Rectangle(backX + (int)s, seatY - (int)(21 * s), (int)s, (int)(20 * s)), velvetDark);
		MusicDrawing.Fill(sb, new Rectangle(backX, seatY - (int)(23 * s), (int)(5 * s), (int)(2 * s)), gold);
		MusicDrawing.Fill(sb, new Rectangle(x0, seatY, seatW, (int)(5 * s)), goldDark);
		MusicDrawing.Fill(sb, new Rectangle(x0 + (int)s, seatY - (int)(2 * s), seatW - (int)(2 * s), (int)(5 * s)), velvet);
		MusicDrawing.Fill(sb, new Rectangle(x0 + (int)s, seatY - (int)(2 * s), seatW - (int)(2 * s), (int)s), new Color(214, 70, 86));
	}

	/// <summary>A plate in Terraria's own panel style: a gold border on their turn, green for a winner.</summary>
	public static void Plate(SpriteBatch sb, Rectangle r, bool lit, bool winner, float dim = 1f)
	{
		Color back = new Color(26, 32, 66) * (0.95f * dim);
		Color border = winner ? SoftGreen : (lit ? Color.Lerp(Gold, Color.White, 0.25f * GameArt.Pulse(0.9)) : new Color(8, 10, 22));
		GameOverlay.MenuPanel(sb, r, back, border);
	}

	/// <summary>The turn's clock inside a plate: a slim bar, gold to red as it runs out.</summary>
	public static void Clock(SpriteBatch sb, Rectangle plate, float left)
	{
		left = Math.Max(0f, Math.Min(1f, left));
		Rectangle track = new Rectangle(plate.X + 10, plate.Bottom - 8, plate.Width - 20, 3);
		MusicDrawing.Fill(sb, track, Color.Black * 0.55f);
		MusicDrawing.Fill(sb, new Rectangle(track.X, track.Y, (int)(track.Width * left), 3), Color.Lerp(SoftRed, Gold, left));
	}

	/// <summary>
	/// A button in Terraria's panel style: its colour inside, a gold border on hover; when <paramref name="hint"/>
	/// (what's best to do) its border pulses gold. Returns true on click.
	/// </summary>
	public static bool Button(SpriteBatch sb, string id, Rectangle r, string label, Color color, string tooltip = null, bool enabled = true, bool hint = false)
	{
		bool hover = enabled && MusicPlayerWindow.MouseIn(r);
		Color back = enabled ? (hover ? Color.Lerp(color, Color.White, 0.14f) : color) : Color.Lerp(color, new Color(30, 30, 40), 0.6f);
		Color border = hover ? Gold : ((hint && enabled) ? Color.Lerp(new Color(120, 90, 30), Gold, GameArt.Pulse(1.1)) : new Color(8, 8, 16));
		GameOverlay.MenuPanel(sb, r, back, border);
		string text = IsaClient.Languages.L10n.T(label);
		float scale = (r.Height >= 34) ? 0.82f : 0.72f;
		float tw = MusicDrawing.Measure(text, scale);
		if (tw > r.Width - 14)
		{
			scale *= (r.Width - 14) / tw;
			tw = MusicDrawing.Measure(text, scale);
		}
		MusicDrawing.RawText(sb, text, (float)Math.Round(r.Center.X - tw / 2f), (float)Math.Round(r.Center.Y - 11f * scale / 0.82f), enabled ? Color.White : (Cream * 0.4f), scale);
		if (hover)
		{
			MusicDrawing.Hover(id, tooltip);
		}
		return hover && MusicPlayerWindow.Clicked;
	}

	#endregion

	#region Players

	private static readonly Dictionary<int, Player> _bots = new Dictionary<int, Player>();

	private static readonly int[] Hats = { 0, Terraria.ID.ItemID.TopHat, Terraria.ID.ItemID.Fez, Terraria.ID.ItemID.CowboyHat, 0, Terraria.ID.ItemID.SummerHat, Terraria.ID.ItemID.PartyHat, Terraria.ID.ItemID.WizardHat, Terraria.ID.ItemID.SailorHat, 0, Terraria.ID.ItemID.PirateHat, Terraria.ID.ItemID.ArchaeologistsHat, Terraria.ID.ItemID.Sunglasses, Terraria.ID.ItemID.GoldCrown, Terraria.ID.ItemID.RobotHat, Terraria.ID.ItemID.EngineeringHelmet };

	public static Player Bot(int bot)
	{
		if (_bots.TryGetValue(bot, out Player p))
		{
			return p;
		}
		Random r = new Random(bot * 7919 + 17);
		p = new Player();
		p.name = PokerBot.Name(bot);
		p.Male = r.Next(2) == 0;
		p.hair = r.Next(1, Math.Min(TextureAssets.PlayerHair.Length, 160));
		p.skinVariant = p.Male ? r.Next(0, 4) : r.Next(4, 8);
		Color Pick() => new Color(r.Next(40, 240), r.Next(40, 240), r.Next(40, 240));
		p.hairColor = Pick();
		p.skinColor = Color.Lerp(new Color(255, 214, 180), new Color(120, 76, 50), (float)r.NextDouble() * 0.8f);
		p.eyeColor = Pick();
		p.shirtColor = Pick();
		p.underShirtColor = Pick();
		p.pantsColor = Pick();
		p.shoeColor = Pick();
		int hat = Hats[Math.Abs(bot) % Hats.Length];
		// a good share of them are blond cowboys and cowgirls (the rest stay as random as before)
		if (r.NextDouble() < 0.4)
		{
			Color[] blond = { new Color(240, 204, 96), new Color(252, 228, 150), new Color(224, 172, 70), new Color(255, 214, 120) };
			p.hairColor = blond[r.Next(blond.Length)];
			// fair skin for them
			p.skinColor = Color.Lerp(new Color(255, 230, 206), new Color(246, 204, 172), (float)r.NextDouble());
			hat = Terraria.ID.ItemID.CowboyHat;
		}
		if (hat > 0)
		{
			p.armor[10].SetDefaults(hat);
		}
		p.PlayerFrame();
		_bots[bot] = p;
		return p;
	}

	/// <summary>
	/// A character sitting: its feet on the floor at <paramref name="seat"/> (bottom middle), facing the table. The
	/// game's sitting pose; nothing in hand. Everything changed on the character for the drawing is put back.
	/// </summary>
	public static void Seated(SpriteBatch sb, Player p, Vector2 seat, bool faceRight, float scale, Action<SpriteBatch> resume)
	{
		if (p == null)
		{
			return;
		}
		bool wasSitting = p.sitting.isSitting;
		int sittingIndex = p.sitting.sittingIndex;
		Vector2 offset = p.sitting.offsetForSeat;
		int direction = p.direction;
		Rectangle body = p.bodyFrame;
		Rectangle legs = p.legFrame;
		Rectangle head = p.headFrame;
		int itemAnimation = p.itemAnimation;
		float rotation = p.fullRotation;
		int held = p.selectedItem;
		Item heldItem = (held >= 0 && held < p.inventory.Length) ? p.inventory[held] : null;
		bool alwaysLit = p.isaAlwaysLit;
		try
		{
			p.sitting.isSitting = true;
			p.sitting.sittingIndex = 0;
			p.sitting.offsetForSeat = Vector2.Zero;
			p.direction = faceRight ? 1 : -1;
			p.bodyFrame.Y = p.legFrame.Y = p.headFrame.Y = 0;
			p.itemAnimation = 0;
			p.fullRotation = 0f;
			p.isaAlwaysLit = true;
			if (heldItem != null)
			{
				p.inventory[held] = new Item();
			}
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, GameArt.Raster, null, Main.UIScaleMatrix);
			Vector2 topLeft = new Vector2(seat.X - p.width / 2f, seat.Y - p.height);
			Main.PlayerRenderer.DrawPlayer(Main.Camera, p, topLeft + Main.screenPosition, 0f, Vector2.Zero, 0f, scale);
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		finally
		{
			if (heldItem != null)
			{
				p.inventory[held] = heldItem;
			}
			p.sitting.isSitting = wasSitting;
			p.sitting.sittingIndex = sittingIndex;
			p.sitting.offsetForSeat = offset;
			p.direction = direction;
			p.bodyFrame = body;
			p.legFrame = legs;
			p.headFrame = head;
			p.itemAnimation = itemAnimation;
			p.fullRotation = rotation;
			p.isaAlwaysLit = alwaysLit;
			sb.End();
			resume(sb);
		}
	}

	#endregion
}
