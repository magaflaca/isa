using Microsoft.Xna.Framework;

namespace IsaClient.Hub;

/// <summary>The colors of the new panel. Every theme is named after a place of the world.</summary>
internal sealed class HubTheme
{
	public string Name;

	/// <summary>Shown next to the name in the theme picker.</summary>
	public int IconItem;

	public Color Window;

	public Color Sidebar;

	public Color Card;

	public Color CardHover;

	public Color Slot;

	public Color Accent;

	public Color Text = new Color(236, 239, 248);

	public Color Muted = new Color(154, 167, 190);

	public Color On = new Color(110, 220, 120);

	public Color Off = new Color(70, 78, 110);

	public Color Danger = new Color(230, 90, 90);

	public static readonly HubTheme[] All =
	{
		new HubTheme
		{
			Name = "Ocean", IconItem = Terraria.ID.ItemID.Coral,
			Window = new Color(33, 43, 79), Sidebar = new Color(26, 34, 66), Card = new Color(48, 62, 116), CardHover = new Color(66, 86, 154),
			Slot = new Color(63, 82, 151), Accent = new Color(255, 231, 69)
		},
		new HubTheme
		{
			Name = "Forest", IconItem = Terraria.ID.ItemID.Acorn,
			Window = new Color(30, 52, 38), Sidebar = new Color(22, 40, 29), Card = new Color(44, 78, 52), CardHover = new Color(62, 106, 70),
			Slot = new Color(58, 104, 66), Accent = new Color(196, 240, 120)
		},
		new HubTheme
		{
			Name = "Crimson", IconItem = Terraria.ID.ItemID.CrimtaneOre,
			Window = new Color(62, 26, 32), Sidebar = new Color(46, 18, 24), Card = new Color(92, 38, 46), CardHover = new Color(124, 52, 60),
			Slot = new Color(120, 46, 56), Accent = new Color(255, 170, 120)
		},
		new HubTheme
		{
			Name = "Hallow", IconItem = Terraria.ID.ItemID.PixieDust,
			Window = new Color(54, 40, 86), Sidebar = new Color(40, 30, 66), Card = new Color(78, 60, 122), CardHover = new Color(104, 82, 160),
			Slot = new Color(96, 74, 152), Accent = new Color(120, 240, 255)
		},
		new HubTheme
		{
			Name = "Dungeon", IconItem = Terraria.ID.ItemID.BlueBrick,
			Window = new Color(30, 34, 46), Sidebar = new Color(22, 25, 34), Card = new Color(46, 52, 70), CardHover = new Color(64, 72, 96),
			Slot = new Color(58, 66, 92), Accent = new Color(150, 200, 255)
		},
		new HubTheme
		{
			Name = "Desert", IconItem = Terraria.ID.ItemID.SandBlock,
			Window = new Color(70, 52, 30), Sidebar = new Color(54, 40, 22), Card = new Color(100, 76, 44), CardHover = new Color(130, 100, 60),
			Slot = new Color(122, 94, 54), Accent = new Color(255, 214, 120)
		},
		new HubTheme
		{
			Name = "Underworld", IconItem = Terraria.ID.ItemID.Hellstone,
			Window = new Color(40, 22, 22), Sidebar = new Color(28, 14, 14), Card = new Color(66, 34, 30), CardHover = new Color(96, 48, 36),
			Slot = new Color(92, 44, 34), Accent = new Color(255, 140, 50)
		},
		new HubTheme
		{
			Name = "Shimmer", IconItem = Terraria.ID.ItemID.ShimmerBlock,
			Window = new Color(46, 40, 70), Sidebar = new Color(34, 30, 54), Card = new Color(70, 62, 104), CardHover = new Color(96, 86, 140),
			Slot = new Color(88, 80, 134), Accent = new Color(255, 190, 250)
		},
	};

	public static HubTheme Current
	{
		get
		{
			foreach (HubTheme theme in All)
			{
				if (theme.Name == HubPrefs.Theme)
				{
					return theme;
				}
			}
			return All[0];
		}
	}
}
