using System;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Drawing;

namespace IsaClient.Rendering;

/// <summary>
/// .thor: colored lightning strikes the ground until .thor again. Each bolt is the game's own lightning (the Storm
/// Lightning particle of 1.4.5, the one the lightning weapon draws): its color travels in the particle's info (a packed
/// color), its shape in a seed (the movement vector's X), and the server passes it to everyone, so every player sees
/// the same bolts. Two kinds: sent as yours it's the weapon's bolt (falls 750 px straight to its point, with its own
/// thunder); sent as nobody's it's the weather's (falls 1000 px, stops at blocks, shakes the screen and flashes the sky).
/// <list type="bullet">
/// <item>.thor starts and stops it; .thor &lt;bolts per second&gt; sets how many (no limit).</item>
/// <item>The panel (Experimental) has the rest: colors, where they fall, how wide, which kind.</item>
/// </list>
/// </summary>
public static class Thor
{
	public static bool On;

	/// <summary>Bolts a second.</summary>
	[Setting("ThorRate")]
	public static int Rate = 6;

	/// <summary>0 mixed bright colors, 1 one color, 2 between two colors, 3 rainbow going round, 4 any color.</summary>
	[Setting("ThorColors", Min = 0, Max = 4)]
	public static int ColorMode;

	/// <summary>The color for "one color", and the first for "two colors" (0xRRGGBB).</summary>
	[Setting("ThorColorA")]
	public static int ColorA = 0x3CFFE6;

	/// <summary>The second for "two colors" (0xRRGGBB).</summary>
	[Setting("ThorColorB")]
	public static int ColorB = 0xFF28DC;

	/// <summary>0 around you, 1 where your mouse points, 2 on the players around you, 3 all over the screen, 4 right at the mouse.</summary>
	[Setting("ThorWhere", Min = 0, Max = 4)]
	public static int Where;

	/// <summary>How far to the sides they fall (pixels), around you or the mouse.</summary>
	[Setting("ThorSpread", Min = 0, Max = 4000)]
	public static int Spread = 900;

	/// <summary>The weather's bolt (shakes the screen, flashes the sky, stops at blocks) instead of the weapon's.</summary>
	[Setting("ThorStorm")]
	public static bool Storm;

	public static readonly string[] ColorModes = { "Mixed bright colors", "One color", "Between two colors", "Rainbow going round", "Any color" };

	public static readonly string[] Places = { "Around you", "Where you point", "On the players around you", "All over the screen", "Right at the mouse" };

	private static float _due;

	/// <summary>Bright colors, like the picture of it: white, magenta, pink, cyan, red, purple, yellow, green, orange.</summary>
	private static readonly Color[] Mixed =
	{
		new Color(255, 255, 255), new Color(255, 40, 220), new Color(255, 120, 200), new Color(60, 255, 230),
		new Color(255, 30, 60), new Color(150, 60, 255), new Color(255, 250, 140), new Color(80, 255, 90),
		new Color(255, 150, 40)
	};

	public static Color FromInt(int rgb) => new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

	public static int ToInt(Color c) => (c.R << 16) | (c.G << 8) | c.B;

	/// <summary>.thor [bolts per second]</summary>
	public static void ThorCommand(CommandContext command)
	{
		string arg = (command.Parts.Length > 1) ? command.Parts[1] : "";
		if (int.TryParse(arg, out int rate))
		{
			Rate = Math.Max(1, rate);
			ClientSettings.Save();
			On = true;
			ClientChat.Info("Thor: " + Rate + " bolts a second.");
			return;
		}
		On = !On;
		_due = 0f;
		ClientChat.Info(On ? "Thor on: lightning all around. .thor again stops it." : "Thor off.");
	}

	/// <summary>Every tick in a world.</summary>
	public static void Update()
	{
		if (!On || Main.gameMenu || Main.netMode == 2)
		{
			return;
		}
		Player me = Main.LocalPlayer;
		if (me == null || !me.active || me.dead)
		{
			return;
		}
		_due += Math.Max(1, Rate) / 60f;
		while (_due >= 1f)
		{
			_due -= 1f;
			Strike(me);
		}
	}

	private static void Strike(Player me)
	{
		Vector2 center = Target(me, out float spread);
		float x = center.X + Main.rand.NextFloatDirection() * spread;
		// Right at the mouse: the bolt ends on the cursor itself (in the air too), so it follows it.
		Vector2 point = (Where == 4) ? (Main.MouseWorld + Main.rand.NextVector2Circular(12f, 12f)) : new Vector2(x, Ground(x, center.Y));
		ParticleOrchestrator.RequestParticleSpawn(clientOnly: false, ParticleOrchestraType.StormLightning, new ParticleOrchestraSettings
		{
			PositionInWorld = point,
			// The bolt's shape comes from this seed (whole numbers a float holds exactly).
			MovementVector = new Vector2(Main.rand.Next(1, 1 << 23), 0f),
			UniqueInfoPiece = (int)NextColor().PackedValue
		}, Storm ? 255 : (int?)null);
	}

	/// <summary>Where this bolt falls around (and how wide).</summary>
	private static Vector2 Target(Player me, out float spread)
	{
		spread = Spread;
		switch (Where)
		{
		case 1:
			return Main.MouseWorld;
		case 2:
		{
			// A random player on screen (you too).
			Rectangle screen = new Rectangle((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);
			int picked = -1;
			int seen = 0;
			for (int i = 0; i < Main.maxPlayers; i++)
			{
				Player p = Main.player[i];
				if (p != null && p.active && !p.dead && screen.Contains(p.Center.ToPoint()) && Main.rand.Next(++seen) == 0)
				{
					picked = i;
				}
			}
			spread = 40f;
			return (picked >= 0) ? Main.player[picked].Center : me.Center;
		}
		case 3:
			spread = Main.screenWidth / 2f;
			return Main.screenPosition + new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f);
		default:
			return me.Center;
		}
	}

	private static Color NextColor()
	{
		switch (ColorMode)
		{
		case 1:
			return FromInt(ColorA);
		case 2:
			return Color.Lerp(FromInt(ColorA), FromInt(ColorB), Main.rand.NextFloat());
		case 3:
			return Main.hslToRgb(Main.GlobalTimeWrappedHourly * 0.35f % 1f, 1f, 0.6f);
		case 4:
			return Main.hslToRgb(Main.rand.NextFloat(), 1f, 0.6f);
		default:
			return Mixed[Main.rand.Next(Mixed.Length)];
		}
	}

	/// <summary>Where a bolt at this x hits: the first solid tile or platform top below a point above, else near that point.</summary>
	private static float Ground(float x, float y)
	{
		int tx = (int)(x / 16f);
		int top = (int)((y - 300f) / 16f);
		if (tx < 1 || tx >= Main.maxTilesX - 1)
		{
			return y;
		}
		for (int ty = Math.Max(1, top); ty < Math.Min(Main.maxTilesY - 1, top + 70); ty++)
		{
			Tile tile = Main.tile[tx, ty];
			if (tile != null && tile.active() && (Main.tileSolid[tile.type] || Main.tileSolidTop[tile.type]) && !tile.inActive())
			{
				return ty * 16f;
			}
		}
		return y + 20f;
	}
}
