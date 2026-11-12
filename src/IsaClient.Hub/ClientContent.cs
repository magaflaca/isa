using System;
using System.Linq;
using IsaClient.Building;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Sections of the new panel about the client and the game around you: Visuals, Style, Multiplayer, Bots, Music & Video,
/// AI Assistant, My Toolkit and Settings.
/// </summary>
internal static class ClientContent
{
	public static void Build(CatalogBuilder b)
	{
		Visuals(b);
		Style(b);
		Multiplayer(b);
		Sharing(b);
		Bots(b);
		Games(b);
		Translate(b);
		ProxySection(b);
		Music(b);
		Assistant(b);
		Toolkit(b);
		Feedback(b);
		Experimental(b);
		Settings(b);
	}

	/// <summary>New things to try, that may change.</summary>
	private static void Experimental(CatalogBuilder b)
	{
		b.Section("experimental", "Experimental", "New things to try: they may still change", HubIcon.Item(3000), new Color(255, 150, 230));
		b.Page("Thor", HubIcon.Item(3000));
		b.Group("Thor", HubIcon.Item(3000), "Colored lightning all around you, seen by everyone");
		Flag(b, "thor", "Thor", "Colored lightning strikes all around you (the game's own 1.4.5 lightning: every player sees it). Same as .thor.", 3000, () => IsaClient.Rendering.Thor.On, v => IsaClient.Rendering.Thor.On = v);
		HubFeature rate = b.Native("thor-rate", "Lightning per Second", "How many bolts fall each second (.thor <number> sets any amount).", HubIcon.Item(3000), FeatureKind.Slider);
		// Squared, so the low end (the usual amounts) has room on the slider: 1 to 300.
		rate.GetSlider = () => (float)Math.Sqrt(Math.Max(0, Math.Min(299, IsaClient.Rendering.Thor.Rate - 1)) / 299f);
		rate.SetSlider = delegate(float f)
		{
			IsaClient.Rendering.Thor.Rate = 1 + (int)Math.Round(f * f * 299f);
			ClientSettings.Save();
		};
		rate.GetValue = () => IsaClient.Rendering.Thor.Rate + "/s";
		rate.Keywords = "thor lightning rayos rate speed";
		rate.Cheat = false;

		b.Group("Thor Colors", HubIcon.Item(I.RainbowDye), "What colors the lightning comes in");
		HubFeature colors = b.Native("thor-colors", "Lightning Colors", "Mixed bright colors, one color, between two colors, a rainbow going round or any color. Right click goes back.", HubIcon.Item(I.RainbowDye), FeatureKind.Choice);
		colors.GetValue = () => IsaClient.Rendering.Thor.ColorModes[IsaClient.Rendering.Thor.ColorMode];
		colors.Cycle = back => Step(ref IsaClient.Rendering.Thor.ColorMode, IsaClient.Rendering.Thor.ColorModes.Length, back);
		colors.Keywords = "thor lightning color colour rainbow";
		colors.Cheat = false;
		ThorColor(b, "thor-color-a", "First Color", "The color for One Color, and where Between Two Colors starts.", () => IsaClient.Rendering.Thor.ColorA, v => IsaClient.Rendering.Thor.ColorA = v, 0x3CFFE6);
		ThorColor(b, "thor-color-b", "Second Color", "Where Between Two Colors ends.", () => IsaClient.Rendering.Thor.ColorB, v => IsaClient.Rendering.Thor.ColorB = v, 0xFF28DC);

		b.Group("Thor Strikes", HubIcon.Item(3000), "Where the lightning falls and what kind it is");
		HubFeature where = b.Native("thor-where", "Where It Falls", "Around you, where you point, on the players around you, all over the screen or right at the mouse (it follows the cursor, in the air too). Right click goes back.", HubIcon.Item(I.Radar), FeatureKind.Choice);
		where.GetValue = () => IsaClient.Rendering.Thor.Places[IsaClient.Rendering.Thor.Where];
		where.Cycle = back => Step(ref IsaClient.Rendering.Thor.Where, IsaClient.Rendering.Thor.Places.Length, back);
		where.Keywords = "thor lightning where mouse players screen";
		where.Cheat = false;
		HubFeature spread = b.Native("thor-spread", "Width", "How far to the sides they fall, around you or where you point.", HubIcon.Item(I.Binoculars), FeatureKind.Slider);
		spread.GetSlider = () => IsaClient.Rendering.Thor.Spread / 4000f;
		spread.SetSlider = delegate(float f)
		{
			IsaClient.Rendering.Thor.Spread = (int)Math.Round(f * 40f) * 100;
			ClientSettings.Save();
		};
		spread.GetValue = () => (IsaClient.Rendering.Thor.Spread / 16) + " tiles";
		spread.Keywords = "thor lightning spread width radius";
		spread.Cheat = false;
		Flag(b, "thor-storm", "Storm Lightning", "The weather's bolt: longer, stops at blocks, shakes the screen and flashes the sky for everyone near. Off: the lightning weapon's bolt.", I.Cloud, () => IsaClient.Rendering.Thor.Storm, v => IsaClient.Rendering.Thor.Storm = v, saveClient: true);
		Lucy(b);
		Rainbow(b);
		Brain(b);
		MultiPasteSection(b);
	}

	/// <summary>Multi Paste: your bots paste with you on TShock, each its own strip.</summary>
	private static void MultiPasteSection(CatalogBuilder b)
	{
		b.Page("Multi Paste", HubIcon.Item(I.PlumbersHat)).Below = HubMultiPaste.Draw;
		b.Group("Multi Paste", HubIcon.Item(I.PlumbersHat), "Your bots paste with you on TShock, each its own strip");
		HubFeature on = b.Native("multipaste-on", "Multi Paste", "Your pastes, undos (Ctrl+Z), brushes and selection fills, erases and conversions are shared out between you and your bots: each does its own strip at the same time, at its own packet rate. Turning it on calls the default set's bots (they do their steps and come to you), or uses the bots already here.", HubIcon.Item(I.PlumbersHat), FeatureKind.Toggle);
		on.GetOn = () => IsaClient.MultiClient.MultiPaste.Enabled;
		on.SetOn = v => IsaClient.MultiClient.MultiPaste.SetEnabled(v);
		on.Keywords = "multi paste multipaste bots paste together schematic tshock plumber";
	}

	/// <summary>Any NPC's own AI moving you (.npcai): its options, and below, every NPC to pick from.</summary>
	private static void Brain(CatalogBuilder b)
	{
		b.Page("NPC Brain", HubIcon.Item(I.BrainOfConfusion)).Below = HubNpcBrain.Draw;
		b.Group("NPC Brain", HubIcon.Item(I.BrainOfConfusion), "Move as any NPC does, with its own AI (pick it below)");
		Flag(b, "brain-on", "NPC Brain", "Your character moves with the AI of the NPC picked below, as it would chase someone. Same as .npcai.", I.BrainOfConfusion, () => IsaClient.Movement.NpcBrain.Active, delegate(bool v)
		{
			if (v)
			{
				IsaClient.Movement.NpcBrain.Start(IsaClient.Movement.NpcBrain.Type);
			}
			else
			{
				IsaClient.Movement.NpcBrain.Stop();
			}
		});
		HubFeature target = b.Native("brain-target", "Goes After", "The closest player, a player you pick (below), the cursor or the closest enemy. Alone, it goes after the cursor. Right click goes back.", HubIcon.Item(I.Binoculars), FeatureKind.Choice);
		target.GetValue = () => IsaClient.Languages.L10n.T(IsaClient.Movement.NpcBrain.TargetModes[IsaClient.Movement.NpcBrain.TargetMode]);
		target.Cycle = back => Step(ref IsaClient.Movement.NpcBrain.TargetMode, IsaClient.Movement.NpcBrain.TargetModes.Length, back);
		target.Keywords = "npc brain ai target chase player cursor enemy";
		target.Cheat = false;
		Flag(b, "brain-skin", "Look Like It", "You're drawn as the NPC, moving as it does (only on your screen: others see your character).", I.FamiliarWig, () => IsaClient.Movement.NpcBrain.ShowSkin, v => IsaClient.Movement.NpcBrain.ShowSkin = v, saveClient: true);
		Flag(b, "brain-hide", "Hide Yourself", "With its look on, your own character isn't drawn under it.", I.InvisibilityPotion, () => IsaClient.Movement.NpcBrain.HidePlayer, v => IsaClient.Movement.NpcBrain.HidePlayer = v, saveClient: true);
		Flag(b, "brain-shoot", "Let It Shoot", "It shoots as it does in the game. Its shots are yours: they hit enemies (and players in PvP), never you.", I.Shotgun, () => IsaClient.Movement.NpcBrain.AllowShoot, v => IsaClient.Movement.NpcBrain.AllowShoot = v, saveClient: true);
		Flag(b, "brain-summon", "Let It Summon", "It summons what it summons in the game (minions, worm bodies...). Single player only: on a server, only the server makes NPCs.", I.SlimeCrown, () => IsaClient.Movement.NpcBrain.AllowSummon, v => IsaClient.Movement.NpcBrain.AllowSummon = v, saveClient: true);
		Flag(b, "brain-aura", "Kill What It Touches", "Enemies a block from you die while it's on (as .killaura 1).", I.Skull, () => IsaClient.Movement.NpcBrain.KillAura, v => IsaClient.Movement.NpcBrain.KillAura = v, saveClient: true);
	}

	/// <summary>.rainbow: how it writes, and the font maker to draw its letters.</summary>
	private static void Rainbow(CatalogBuilder b)
	{
		b.Page("Rainbow", HubIcon.Item(I.RainbowGun)).Below = HubRainbow.Draw;
		b.Group("Rainbow Text", HubIcon.Item(I.RainbowGun), "How .rainbow writes (letters below: draw your own)");
		HubFeature size = b.Native("rainbow-size", "Letter Size", "How big .rainbow writes.", HubIcon.Item(I.RainbowGun), FeatureKind.Slider);
		size.GetSlider = () => (IsaClient.Gameplay.RainbowText.Scale - 0.3f) / 2.7f;
		size.SetSlider = delegate(float f)
		{
			IsaClient.Gameplay.RainbowText.Scale = 0.3f + (float)Math.Round(f * 27f) / 10f;
			ClientSettings.Save();
		};
		size.GetValue = () => Math.Round(IsaClient.Gameplay.RainbowText.Scale * 100f) + "%";
		size.Keywords = "rainbow size scale letters";
		size.Cheat = false;
		WorldContent.Slider(b, "rainbow-spacing", "Letter Spacing", "Extra room between letters (left: closer together).", I.Ruler, -100, 300, () => IsaClient.Gameplay.RainbowText.Spacing, v => IsaClient.Gameplay.RainbowText.Spacing = v, v => v.ToString(), squared: false).Keywords = "rainbow spacing letters";
		Flag(b, "rainbow-cursor", "At the Cursor", "The text is written right where your cursor is, centered on it (off: above you, at the height below).", I.Binoculars, () => IsaClient.Gameplay.RainbowText.AtCursor, v => IsaClient.Gameplay.RainbowText.AtCursor = v, saveClient: true);
		WorldContent.Slider(b, "rainbow-height", "Height Above You", "How high over you the text is written (left: under you).", I.Compass, -600, 1200, () => IsaClient.Gameplay.RainbowText.Height, v => IsaClient.Gameplay.RainbowText.Height = v, v => (v / 16) + " tiles", squared: false).Keywords = "rainbow height";
		WorldContent.Slider(b, "rainbow-speed", "Drawing Speed", "Rainbows sent each tick: faster writing, but the server sees more at once.", I.Stopwatch, 1, 200, () => IsaClient.Gameplay.RainbowText.Speed, v => IsaClient.Gameplay.RainbowText.Speed = v, v => v + "/tick", squared: true).Keywords = "rainbow speed";
		WorldContent.Slider(b, "rainbow-max", "Longest Text", "The most letters one .rainbow writes.", I.Book, 1, 200, () => IsaClient.Gameplay.RainbowText.MaxLetters, v => IsaClient.Gameplay.RainbowText.MaxLetters = v, v => v.ToString(), squared: true).Keywords = "rainbow letters max length";
	}

	/// <summary>Lucy the Axe talking over someone, as a chorus (.lucy).</summary>
	private static void Lucy(CatalogBuilder b)
	{
		b.Page("Lucy", HubIcon.Item(I.LucyTheAxe)).Below = HubLucy.Draw;
		b.Group("Lucy Chorus", HubIcon.Item(I.LucyTheAxe), "Lucy the Axe talks over someone, many lines a second");
		Flag(b, "lucy-on", "Lucy Chorus", "Lucy talks over the one below until you turn it off (.lucy <player> <per second> picks anyone; the Players page has a button too).", I.LucyTheAxe, () => IsaClient.Rendering.LucyChorus.On, delegate(bool v)
		{
			if (v)
			{
				IsaClient.Rendering.LucyChorus.Start(IsaClient.Rendering.LucyChorus.Target);
			}
			else
			{
				IsaClient.Rendering.LucyChorus.Stop();
			}
		});
		HubFeature over = b.Native("lucy-over", "Over Whom", "You, the mouse or everyone (a player: .lucy <player> or the Players page). Right click goes back.", HubIcon.Item(I.Radar), FeatureKind.Choice);
		over.GetValue = () => (IsaClient.Rendering.LucyChorus.Target >= 0) ? IsaClient.Rendering.LucyChorus.TargetName : IsaClient.Rendering.LucyChorus.Targets[-1 - IsaClient.Rendering.LucyChorus.Target];
		over.Cycle = delegate(bool back)
		{
			int index = (IsaClient.Rendering.LucyChorus.Target >= 0) ? 0 : (-1 - IsaClient.Rendering.LucyChorus.Target);
			index = (index + (back ? 2 : 1)) % 3;
			IsaClient.Rendering.LucyChorus.Target = -1 - index;
		};
		over.Keywords = "lucy target player mouse everyone";
		over.Cheat = false;
		HubFeature rate = b.Native("lucy-rate", "Lines per Second", "How many lines a second (.lucy <player> <number> sets any amount).", HubIcon.Item(I.LucyTheAxe), FeatureKind.Slider);
		rate.GetSlider = () => (float)Math.Sqrt(Math.Max(0, Math.Min(299, IsaClient.Rendering.LucyChorus.Rate - 1)) / 299f);
		rate.SetSlider = delegate(float f)
		{
			IsaClient.Rendering.LucyChorus.Rate = 1 + (int)Math.Round(f * f * 299f);
			ClientSettings.Save();
		};
		rate.GetValue = () => IsaClient.Rendering.LucyChorus.Rate + "/s";
		rate.Keywords = "lucy rate speed";
		rate.Cheat = false;
		HubFeature say = b.Native("lucy-one", "Say One Line", "Lucy says one line over the one above, now.", HubIcon.Item(I.LucyTheAxe), FeatureKind.Action);
		say.Verb = "Say";
		say.Run = () => IsaClient.Rendering.LucyChorus.One(IsaClient.Rendering.LucyChorus.Target);
		say.Cheat = false;

		b.Group("Lucy Lines", HubIcon.Item(I.Book), "What she says");
		HubFeature lines = b.Native("lucy-lines", "lucy::Lines", "Lucy's own lines (everyone sees them, in their language), your lines (IsaClient players see them) or both. Right click goes back.", HubIcon.Item(I.Book), FeatureKind.Choice);
		lines.GetValue = () => IsaClient.Rendering.LucyChorus.LineModes[IsaClient.Rendering.LucyChorus.Lines];
		lines.Cycle = back => Step(ref IsaClient.Rendering.LucyChorus.Lines, IsaClient.Rendering.LucyChorus.LineModes.Length, back);
		lines.Keywords = "lucy lines custom own";
		lines.Cheat = false;
		HubFeature kind = b.Native("lucy-kind", "Lucy's Mood", "Which of Lucy's own lines: any, or only the ones she says idle, in storage, thrown away, picked up, chopping trees, gem trees or cactus. Right click goes back.", HubIcon.Item(I.LucyTheAxe), FeatureKind.Choice);
		kind.GetValue = () => IsaClient.Rendering.LucyChorus.Kinds[IsaClient.Rendering.LucyChorus.Kind];
		kind.Cycle = back => Step(ref IsaClient.Rendering.LucyChorus.Kind, IsaClient.Rendering.LucyChorus.Kinds.Length, back);
		kind.Keywords = "lucy kind mood category";
		kind.Cheat = false;
		Flag(b, "lucy-standin", "Others See Lucy Instead", "With your own lines: players without IsaClient see one of Lucy's lines in their place (IsaClient players see only yours).", I.LucyTheAxe, () => IsaClient.Rendering.LucyChorus.OthersSeeLucy, v => IsaClient.Rendering.LucyChorus.OthersSeeLucy = v, saveClient: true);
	}

	/// <summary>One step through a list of choices (saved).</summary>
	private static void Step(ref int value, int count, bool back)
	{
		value = (value + (back ? count - 1 : 1)) % count;
		ClientSettings.Save();
	}

	private static void ThorColor(CatalogBuilder b, string id, string title, string description, Func<int> get, Action<int> set, int fallback)
	{
		HubFeature color = b.Native(id, title, description, HubIcon.Item(I.RainbowDye), FeatureKind.Color);
		color.GetColor = () => IsaClient.Rendering.Thor.FromInt(get());
		color.SetColor = c => set(IsaClient.Rendering.Thor.ToInt(c));
		color.ColorDone = ClientSettings.Save;
		color.DefaultColor = IsaClient.Rendering.Thor.FromInt(fallback);
		color.GetValue = () => ColorName(IsaClient.Rendering.Thor.FromInt(get()));
		color.Keywords = "thor lightning color colour";
		color.Cheat = false;
	}

	private static void Visuals(CatalogBuilder b)
	{
		b.Section("visuals", "Visuals", "Light, zoom, hitboxes, shaders and info overlays", HubIcon.Item(I.SpelunkerPotion), new Color(170, 210, 255));
		b.Page("Rendering", HubIcon.Item(I.UltrabrightTorch));
		b.Group("Light", HubIcon.Item(I.UltrabrightTorch), "See in the dark");
		b.Row("Fullbright", "Fullbright", "Light up the whole world.", I.UltrabrightTorch, "light bright dark", ".fb");
		b.Row("Global Brightness", "Brightness", "The least light any block can have. 100% skips lighting completely.", I.Torch, "light brightness", ".fb 75", wide: true);

		b.Group("Camera & Zoom", HubIcon.Item(I.Binoculars), "See more of the world");
		b.Row("Extended Zoom Out", "Zoom Out Further", "Zoom below 100% (very low zoom costs FPS).", I.Binoculars, "zoom", ".zoom");
		b.Row("Free Camera", "Free Camera", "Move the camera on its own with WASD (Shift faster, Ctrl slower).", I.Binoculars, "freecam camera spectate", ".freecam");
		b.Row("Minimum Zoom", "Lowest Zoom", null, I.Binoculars, "zoom minimum", wide: true);
		b.Row("Free Camera Speed", "Free Camera Speed", null, I.Binoculars, "freecam speed", wide: true);

		b.Group("See More", HubIcon.Item(I.SpelunkerPotion), "Potion effects for free");
		b.Row("Force Spelunker", "Spelunker", "Ores and treasure glow.", I.SpelunkerPotion, "ore treasure glow");
		b.Row("Force Hunter", "Hunter", "Enemies are outlined.", I.HunterPotion, "enemies outline");
		b.Row("Force Dangersense", "Dangersense", "Traps glow.", I.TrapsightPotion, "traps");
		b.Row("Force Biome Sight", "Biome Sight", "Evil and hallowed blocks glow.", I.BiomeSightPotion, "biome evil");
		b.Row("Always Render Invisible Tiles/Walls", "See Echo Blocks", "Echo blocks and echo-coated blocks are always visible.", I.EchoBlock, "echo invisible");
		b.Row("Render Other Player's Wire Kites", "See Others' Wires", "Shows the wires of other players' Grand Design.", I.WireKite, "wire grand design");

		b.Group("Hitboxes", HubIcon.Item(I.Ruler), "Draw the boxes the game uses for hits");
		b.Row("Player Hitboxes", "Players", null, I.Ruler, "hitbox");
		b.Row("NPC Hitboxes", "Creatures", null, I.Ruler, "hitbox npc");
		b.Row("Projectile Hitboxes", "Projectiles", null, I.Ruler, "hitbox projectile");
		b.Row("Item Hitboxes", "Dropped Items", null, I.Ruler, "hitbox item");
		b.Row("Hitbox Style", "Box Style", null, I.Ruler, "hitbox style", wide: true);

		b.Group("Shaders & Effects", HubIcon.Item(I.RainbowDye), "How the screen looks");
		b.Row("Use CRT Shader", "Retro CRT", "An unused old-TV screen effect.", I.RainbowDye, "shader crt retro");
		b.Row("Use GameBoy Shader", "Game Boy Colors", "An unused green handheld look.", I.RainbowDye, "shader gameboy");
		b.Row("Disable Blood Moon Shader", "No Blood Moon Tint", null, I.BloodMoonStarter, "shader blood moon red");
		b.Row("Disable Moon Lord Shader", "No Moon Lord Tint", null, I.CelestialSigil, "shader moon lord");
		b.Row("Invisible Enemies", "Hide Enemies", "Enemies are not drawn (bosses and town NPCs still are).", I.GhostMask, "hide enemies invisible");
		b.Row("Dust Level", "Dust Amount", "0% removes dust, 100% is normal.", I.Bottle, "dust particles", wide: true);
		b.Row("Particle Limiter", "Particle Limit", "How many dust particles can exist at once.", I.Bottle, "particles limit", ".particlelimit", wide: true);

		b.Page("Info Overlays", HubIcon.Item(I.CellPhone));
		b.Group("Information", HubIcon.Item(I.CellPhone), "Extra details on things you point at").Preferences = true;
		b.Row("Show IDs", "Show IDs", "IDs of items, prefixes, NPCs and buffs in their names.", I.CellPhone, "id ids");
		b.Row("Extra Item Information", "More Item Details", "Extra lines in item tooltips.", I.Book, "item tooltip");
		b.Row("Extra NPC Information", "More Creature Details", "Extra data when pointing at a creature.", I.LifeformAnalyzer, "npc info");
		b.Row("See Tile Data", "Block Details", "Shows the data of the block under the cursor.", I.Ruler, "tile data", ".tiledata");
		b.Row("Paste/Selection Mode Information", "Selection & Paste Details", "Extra numbers while selecting and pasting.", I.Ruler, "selection info");
		b.Row("Show Owner of Dropped Items", "Who Dropped It", "Shows the owner of a dropped item under the mouse.", I.Sign, "owner item");
		b.Row("Shift+Click Wiki Lookup", "Wiki Lookup", "Shift + click an item in your inventory to open it on the Terraria wiki.", I.Book, "wiki shift click");
	}

	private static void Style(CatalogBuilder b)
	{
		b.Section("style", "Style", "Particles, death effects and colors that show off", HubIcon.Item(I.RainbowCursor), new Color(230, 150, 255));
		b.Group("Trails & Glow", HubIcon.Item(I.RainbowCursor), "Some are visible to other players");
		b.Row("Walking Particles", "Walking Trail", "Leave particles behind you when you walk.", I.FlameWakerBoots, "trail particles walk", wide: true);
		b.Row("Particle Cursor", "Sparkly Cursor", "A particle at your cursor everyone can see.", I.RainbowCursor, "cursor particles", wide: true);
		b.Row("Always Have After-Images", "After-Images", "A trail of after-images follows you.", I.MasterNinjaGear, "afterimage trail");
		b.Row("Always Pulsate", "Pulsate", "You glow in and out.", I.StarinaBottle, "pulse glow");

		b.Group("Death Effects", HubIcon.Item(I.Tombstone), "What happens when you die");
		b.Row("Enable Death Particle", "Death Particle", null, I.Confetti, "death particle");
		b.Row("Death Particle", "Particle", null, I.Confetti, "death particle", wide: true);
		b.Row("Enable Death Effect", "Death Effect", null, I.Tombstone, "death effect gore");
		b.Row("Death Effect", "Effect", null, I.Tombstone, "death effect", wide: true);
		b.Row("Enable Death Sound", "Death Sound", null, I.Bell, "death sound");
		b.Row("Death Sound", "Sound", null, I.Bell, "death sound", wide: true);
		b.Row("Death Cosmetics for Others", "Death Effects for Everyone", "Other players get death effects too.", I.Headstone, "death others");

		b.Group("Map Colors", HubIcon.Item(I.TrifoldMap), "Because RGB is cool");
		b.Row("RGB Map Outline for Self", "Rainbow Map Icon", "Your map icon cycles colors.", I.TrifoldMap, "rgb map rainbow");
		b.Row("RGB Map Icons for NPCs", "Rainbow NPC Icons", null, I.TrifoldMap, "rgb map npc");
		b.Row("RGB Map Icons for Bosses", "Rainbow Boss Icons", null, I.TrifoldMap, "rgb map boss");

		b.Group("Lucy the Axe", HubIcon.Item(I.LucyTheAxe));
		b.Row("Lucy Messages", "Lucy Talks", "Make Lucy talk, or keep her quiet.", I.LucyTheAxe, "lucy axe", wide: true);
		b.Row("Lucy Message on Death", "Lucy on Death", "Lucy says something when you die.", I.LucyTheAxe, "lucy death");
	}

	private static void Multiplayer(CatalogBuilder b)
	{
		b.Section("multiplayer", "Multiplayer", "Players, chat, servers and anti-lag", HubIcon.Texture("Images/UI/PVP_0", new Rectangle(40, 2, 32, 32)), new Color(120, 230, 210));
		b.Page("Players", HubIcon.Texture("Images/UI/PVP_0", new Rectangle(40, 40, 32, 32))).Custom = HubPlayers.Draw;
		b.Find("Players", "Everyone on the server: look, inventory, teleport to them.", "players list inventory teleport spectate");
		b.Page("Online", HubIcon.Item(I.Megaphone));
		b.Group("Awareness", HubIcon.Item(I.Radar), "Know who is around and what the server does");
		b.Row("Player List", "Player List Overlay", "A list of connected players on screen (I = IsaClient, Z = zMod).", I.Radar, "players list overlay", ".playerlist", cheat: false);
		b.Row("Inform Other Clients", "Show Me to IsaClient Users", "Other IsaClient users see you in their player list. Off = hidden.", I.Megaphone, "share info hidden", ".shareinfo", cheat: false);
		b.Row("Player Vitals", "Player Health Bars", "Health, mana and defense under every player.", I.LifeformAnalyzer, "hp mana defense", cheat: false);
		b.Row("Track Players", "Track Players", "See everyone on the map, whatever their team or PvP.", I.Radar, "map players track");
		b.Row("Disable Invisibility on Players", "See Invisible Players", "Players can never look invisible (unless dead).", I.GhostMask, "invisible");
		b.Row("Show Netplay Text", "Connection Info", "Network details at the top of the screen.", I.CellPhone, "netplay network", cheat: false);
		b.Row("Notify When Server Modifies You", "Server Change Alerts", "A chat message when the server changes your player.", I.Megaphone, "server notify", cheat: false);
		b.Row("Notify When Players Connect", "Tell Me When Players Join", "Also reveals vanished players that still send data.", I.Megaphone, "join connect vanish", cheat: false);
		Flag(b, "join-details", "Join Details", "With Tell Me When Players Join: also what they sync (life, buffs, items, mana) and how long they took. Off: only \"is joining\".", I.Megaphone, () => IsaClient.Networking.VanishDetection.Detailed, v => IsaClient.Networking.VanishDetection.Detailed = v, saveClient: true);

		b.Group("Chat", HubIcon.Item(I.Sign), "A better chat box and colored messages").Preferences = true;
		b.Row("Enhanced Chat Box", "Better Chat Box", "Multi-line chat with selection, copy and paste.", I.Sign, "chat multiline paste");
		b.Row("Keep Chat Between Worlds", "Keep Chat History", "The chat stays when you leave a world or server.", I.Sign, "chat history keep", ".keepchat");
		b.Row("Anti-Chat Spam", "Hide Chat Spam", "Ignores annoying repeated messages.", I.Sign, "spam chat");
		Flag(b, "chatcolor-on", "Colored Chat", "Your messages go out in your color for everyone. Items and tags keep working.", I.RainbowDye, () => IsaClient.Community.ChatColor.Enabled, v => IsaClient.Community.ChatColor.Enabled = v, saveClient: true);
		Flag(b, "chatcopy", "Right Click Copies Messages", "With the chat open, right click a message to copy it exactly as it came.", I.Sign, () => IsaClient.Community.ChatCopy.Enabled, v => IsaClient.Community.ChatCopy.Enabled = v, saveClient: true);
		HubFeature chatColor = b.Native("chatcolor", "Chat Color", "The color of your messages when Colored Chat is on.", HubIcon.Item(I.RainbowDye), FeatureKind.Color);
		chatColor.GetColor = () => IsaClient.Community.ChatColor.Current;
		chatColor.SetColor = c => IsaClient.Community.ChatColor.Current = c;
		chatColor.ColorDone = ClientSettings.Save;
		chatColor.DefaultColor = IsaClient.Community.ChatColor.DefaultColor;
		chatColor.GetValue = () => ColorName(IsaClient.Community.ChatColor.Current);
		chatColor.Keywords = "chat color colour text tag";
		chatColor.Command = ".chatcolor";
		Flag(b, "translate-on", "Translate My Messages", "What you type goes out in another language. Commands and [tags] stay as typed.", I.WorldGlobe, () => IsaClient.Community.ChatTranslate.Enabled, v => IsaClient.Community.ChatTranslate.Enabled = v, saveClient: true);
		HubFeature translator = b.Native("translator-open", "Chat Languages", "Pick the language you write in and the one your messages go out in.", HubIcon.Item(I.WorldGlobe), FeatureKind.Action);
		translator.GetValue = HubTranslate.Summary;
		translator.Run = () => ClientHub.OpenSection("translate", 0);
		translator.Keywords = "translate translator language idioma traducir english spanish";
		translator.Command = ".translate";
		Flag(b, "translate-in", "Translate Incoming Messages", "The chat shows in your language (the game's one by default). Ctrl + right click translates one message.", I.WorldGlobe, () => IsaClient.Community.IncomingTranslation.Enabled, v => IsaClient.Community.IncomingTranslation.Enabled = v, saveClient: true);
		HubFeature incoming = b.Native("translator-incoming", "Incoming Languages", "What to translate and in which language the chat is shown.", HubIcon.Item(I.Megaphone), FeatureKind.Action);
		incoming.GetValue = HubTranslate.IncomingSummary;
		incoming.Run = () => ClientHub.OpenSection("translate", 1);
		incoming.Keywords = "translate incoming chat language idioma traducir received";
		incoming.Command = ".translate in";
		incoming.Cheat = false;
		translator.Cheat = false;
		ChatPictureSettings(b);

		b.Group("Server Characters", HubIcon.Item(I.Mannequin), "For servers with server-side characters (TShock SSC)").Preferences = true;
		b.Row("Keep SSC Character (One-Way)", "Save Server Character", "Your local character follows the one on the server.", I.Mannequin, "ssc tshock character", ".ssc", wide: true);
		b.Row("Two-Way SSC Sync", "Two-Way Character Sync", "Whichever side changed last wins. Set it before joining.", I.Mannequin, "ssc sync", ".ssc twoway", wide: true);
		b.Row("Rapid Position Updates", "Fast Position Updates", "Sends your position very often (not vanilla: may look suspicious).", I.Wire, "position packets", wide: true).Cheat = true;

		b.Group("Anti-Lag", HubIcon.Item(I.Stopwatch), "Protection against players who spam the server").Preferences = true;
		b.Row("Anti-Lag - Projectile Spam Blocking", "Block Projectile Spam", null, I.WoodenArrow, "lag spam projectile");
		b.Row("Anti-Lag - Sound Spam Blocking", "Block Sound Spam", null, I.Harp, "lag spam sound instrument");
		b.Row("Anti-Lag - Particle Spam Blocking", "Block Particle Spam", null, I.Confetti, "lag spam particle");
		b.Row("Anti-Lag - Low FPS Measures", "Low FPS Rescue", "When FPS drops, clears projectiles, dust and gore.", I.Stopwatch, "lag fps");

		b.Page("IsaClient Pipe", HubIcon.Item(I.Wire)).Custom = HubPipe.Draw;
		b.Find("IsaClient Pipe", "Turn on or off everything that goes between IsaClient players (games, IsaClient chat, sharing).", "pipe off on shutdown data network apagar desactivar");
	}

	/// <summary>Characters, styles and schematics linked in chat (pipe and archive.org).</summary>
	private static void Sharing(CatalogBuilder b)
	{
		b.Section("sharing", "Sharing", "Send characters, styles and schematics in chat", HubIcon.Item(I.Mannequin), new Color(110, 200, 235));
		b.Page("Characters", HubIcon.Item(I.Mannequin)).Custom = HubShare.DrawCharacters;
		b.Find("Send a character", "Link a .plr in chat: IsaClient players get the whole character, with its IsaClient items.", "share send character plr player personaje enviar");
		b.Page("Styles", HubIcon.Item(I.Paintbrush)).Custom = HubShare.DrawStyles;
		b.Find("Character styles", "Hair, clothes and colors from character creation: link them, keep them, try them on.", "style look hair template estilo pelo colores");
		b.Page("Schematics", HubIcon.Item(I.ArchitectGizmoPack)).Custom = HubShare.DrawSchematics;
		b.Find("Send a schematic", "Link a .TEditSch or what you copied (.copy) in chat, with a map preview.", "schematic tedit copy paste esquematica share");
		b.Page("Received", HubIcon.Item(I.Present)).Custom = HubShare.DrawReceived;
		b.Find("Received", "What others linked in chat: characters, styles and schematics.", "received inbox shared recibidos");
	}

	private static void Translate(CatalogBuilder b)
	{
		b.Section("translate", "Translator", "Your chat in any language: English, Spanish, Portuguese...", HubIcon.Item(I.WorldGlobe), new Color(130, 215, 170));
		b.Page("My Messages", HubIcon.Item(I.WorldGlobe)).Custom = HubTranslate.DrawOutgoing;
		b.Find("Translate my messages", "The language you write in and the one your messages go out in.", "translator translate language chat");
		b.Page("Incoming", HubIcon.Item(I.Megaphone)).Custom = HubTranslate.DrawIncoming;
		b.Find("Translate incoming chat", "Which messages are translated and into what language.", "translator translate language incoming");
	}

	/// <summary>Games with other IsaClient players over the pipe; each game adds itself to the Play page.</summary>
	private static void Games(CatalogBuilder b)
	{
		b.Section("games", "Games", "Chess, Tic Tac Toe, Tetris, Snake and Pokémon: with IsaClient players or the computer", HubIcon.Item(I.GoodieBag), new Color(255, 170, 110));
		b.Page("Play games", HubIcon.Item(859)).Custom = HubGames.DrawPlay;
		b.Find("Play games", "Tic Tac Toe, Chess, Tetris, Tetris Versus and Snake with IsaClient players or the computer.", "games chess tetris tictactoe ttt snake invite");
		b.Find("Chess", "Play chess online or against the computer.", "chess ajedrez");
		b.Find("Tetris", "Tetris solo, or Versus against a player.", "tetris versus blocks");
		b.Find("Snake", "The snake of the old phones, with Terraria fruit.", "snake serpiente nokia worm");
		b.Find("PlayStation Portable", "A PSP in Terraria: your own games, the emulator is downloaded the first time you play.", "psp playstation portable sony emulator ppsspp iso cso umd");
		b.Find("Game Boy Advance", "A GBA and a DS in Terraria: your own games; in Pokémon FireRed, LeafGreen and Emerald you play as your Terraria character.", "pokemon firered leafgreen emerald esmeralda gba gameboy emulator rojo fuego mario zelda nds ds nintendo");
		b.Page("Scores", HubIcon.Item(I.GoldCrown)).Custom = HubGames.DrawScores;
		b.Find("Scores", "Global ranking and the ranking of each game.", "scores ranking elo top leaderboard");
		b.Page("Customize", HubIcon.Item(I.Paintbrush)).Custom = HubGames.DrawCustomize;
		b.Find("Game options", "Who starts, colors, computer level, turn time, Tetris and Snake speed.", "games options bot level speed turn snake");
	}

	/// <summary>Bots: extra players you control, on your server or others (multi-client). Room for more tabs.</summary>
	private static void Bots(CatalogBuilder b)
	{
		b.Section("bots", "Bots", "Extra players you control: play as them, on any server", HubIcon.Item(I.RobotHat), new Color(130, 200, 255));
		b.Page("Sessions", HubIcon.Item(I.RobotHat)).Custom = HubBots.DrawSessions;
		b.Find("Bot sessions", "Your bots playing in the background.", "bots sessions multiclient");
		b.Page("New Bot", HubIcon.Item(I.Mannequin)).Custom = HubBots.DrawNew;
		b.Find("New bot", "Start a bot that joins a server.", "bot new join");
		b.Page("Sets", HubIcon.Item(I.GoldCrown)).Custom = HubBotSets.Draw;
		b.Find("Bot sets", "Your accounts as a team: they join together, log in and do their steps.", "bots sets team accounts login steps script batch");
		b.Page("Saved", HubIcon.Item(I.Book)).Custom = HubBots.DrawSaved;
		b.Find("Saved bots", "Bots you saved to start again.", "bots saved");
		b.Page("Options", HubIcon.Item(I.Cog)).Custom = HubBots.DrawOptions;
		b.Find("Bot options", "How bots behave.", "bots options");
		b.Page("Connection", HubIcon.Item(I.WorldGlobe)).Custom = HubBotRoutes.Draw;
		b.Find("Bots through proxies", "Where bots connect from: your IP, then proxies, never more than the server lets in per IP.", "bots proxy proxies ip limit connection vpn country");
	}

	private static void ProxySection(CatalogBuilder b)
	{
		b.Section("proxy", "Proxy", "Hide your IP: connect through another country, like a VPN", HubIcon.Item(I.CobaltShield), new Color(80, 220, 140));
		b.Page("Locations", HubIcon.Item(I.WorldGlobe)).Custom = HubProxy.DrawConnectPage;
		b.Find("Proxy locations", "Connect through a proxy in another country.", "proxy vpn country location ip");
		b.Page("My Proxies", HubIcon.Item(I.GoldenKey)).Custom = HubProxy.DrawMine;
		b.Find("My proxies", "Proxies you added yourself.", "proxy vpn mine");
		b.Page("Settings", HubIcon.Item(I.Cog)).Custom = HubProxy.DrawSettings;
		b.Find("Proxy settings", "How proxies are found and tested.", "proxy vpn settings");
	}

	private static void Music(CatalogBuilder b)
	{
		b.Section("music", "Music & Video", "Your music, videos and the game's sound", HubIcon.Item(I.MusicBoxOverworldDay), new Color(255, 160, 220));
		b.Group("Music Player", HubIcon.Item(I.MusicBoxOverworldDay), "Play songs and links with .play");
		b.Row("Open Music Player", "Open Music Player", "Now playing, queue, library, favorites, playlists and search.", I.MusicBoxOverworldDay, "music window", ".musicui", verb: "Open", cheat: false);
		b.Row("Music Module", "Music Player On", "Lets .play play songs and fades out Terraria's music.", I.MusicBox, "music enable", cheat: false);
		b.Row("Mini Player", "Mini Player", "A small floating player while a song plays.", I.MusicBox, "mini player", ".miniplayer", cheat: false);
		b.Row("Now Playing", "Now Playing", "Click to show the queue.", I.MusicBox, "song current queue", wide: true, cheat: false);
		b.Row("Pause / Resume", "Pause / Resume", null, I.MusicBox, "pause play", ".pause", verb: "Pause", cheat: false);
		b.Row("Skip", "Next Song", null, I.MusicBox, "skip next", ".skip", verb: "Skip", cheat: false);
		b.Row("Stop & Clear Queue", "Stop & Clear Queue", null, I.MusicBox, "stop clear", ".stop", verb: "Stop", cheat: false);
		b.Row("Loop", "Repeat", "Off, this song, or the whole queue.", I.MusicBox, "loop repeat", ".loop", cheat: false);
		b.Row("Music Volume", "Music Player Volume", "Separate from Terraria's volume.", I.MusicBox, "volume", ".volume", wide: true, cheat: false);
		b.Row("Autoplay", "Autoplay", "Keeps playing related songs when the queue ends.", I.MusicBox, "autoplay related", ".autoplay", cheat: false);
		b.Row("Play In Background", "Play in Background", "Songs keep playing with the game minimized.", I.MusicBox, "background minimized", ".background", cheat: false);
		b.Row("Mute Terraria Music", "Quiet Game Music", "Terraria's music fades out while a song plays.", I.MusicBox, "mute game music", cheat: false);
		b.Row("Music Chat Messages", "Music Messages in Chat", "Commands, all, or none. Right click goes back.", I.Sign, "chat messages", ".music chat", wide: true, cheat: false);

		b.Group("Video Player", HubIcon.Item(I.Binoculars), "YouTube and video links, inside the game");
		b.Row("Open Video Player", "Open Video Player", "Search, watch, continue where you stopped, save videos.", I.Binoculars, "video youtube watch movie", ".video", verb: "Open", cheat: false);
		b.Row("Quiet Game While Playing", "Quiet Game While Playing", "Game sounds fade out while a song or video plays.", I.Bell, "quiet sound effects ambience video", ".video quiet", cheat: false);

		b.Group("Downloads", HubIcon.Item(I.Chest), "Where songs come from");
		b.Row("Open Music Folder", "Open Music Folder", "Downloaded songs, settings and yt-dlp.", I.Chest, "folder files", verb: "Open", cheat: false);
		b.Row("Load Cookies", "Load Cookies", "For videos that need a login. Right click removes them.", I.Book, "cookies login youtube", ".loadcookies", verb: "Load", cheat: false);
		b.Row("Update yt-dlp", "Update yt-dlp", "Gets the newest yt-dlp.", I.Wrench, "ytdlp youtube update", ".ytdlp", verb: "Update", cheat: false);

		b.Group("Game Soundtrack", HubIcon.Item(I.MusicBoxBoss1), "Old themes and fewer special themes").Preferences = true;
		b.Row("Mute Game Sound", "Mute the Game", "Music, sound and ambience off; again to bring your volumes back.", I.Bell, "mute sound", ".mute");
		b.Row("Swap Music", "Swap Soundtrack", "Switch soundtrack until you leave the world.", I.MusicBoxBoss1, "otherworld soundtrack", wide: true);
		b.Row("Force Soundtrack", "Always This Soundtrack", null, I.MusicBoxBoss1, "soundtrack force", wide: true);
		b.Row("Revert Jungle Night Theme", "Old Jungle Night", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Revert Underground Jungle Theme", "Old Underground Jungle", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Classic Dungeon Theme", "Old Dungeon Theme", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Classic Underworld Theme", "Old Underworld Theme", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Revert Underground Desert Theme", "Old Underground Desert", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Revert Duke Fishron Theme", "Old Duke Fishron Theme", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Classic Goblin Army Theme", "Old Goblin Army Theme", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Classic Pirate Invasion Theme", "Old Pirate Theme", null, I.MusicBoxBoss1, "theme classic");
		b.Row("Disable Morning Rain Theme", "No Morning Rain Theme", null, I.MusicBoxBoss1, "theme disable");
		b.Row("Disable Storm Theme", "No Storm Theme", null, I.MusicBoxBoss1, "theme disable");
		b.Row("Disable Windy Day Theme", "No Windy Day Theme", null, I.MusicBoxBoss1, "theme disable");
		b.Row("Disable Slime Rain Theme", "No Slime Rain Theme", null, I.MusicBoxBoss1, "theme disable");
		b.Row("Disable Graveyard Theme", "No Graveyard Theme", null, I.MusicBoxBoss1, "theme disable");
		b.Row("Disable Town Themes", "No Town Themes", null, I.MusicBoxBoss1, "theme disable");
	}

	private static void Assistant(CatalogBuilder b)
	{
		b.Section("ai", "AI Assistant", "Answers your questions with the web, and replies for you while you are away", HubIcon.Item(I.SpellTome), new Color(120, 200, 255));
		b.Page("Assistant", HubIcon.Item(I.SpellTome));
		b.Group("Ask Anything", HubIcon.Item(I.CrystalBall), "Questions answered with web search: .ask <question>");
		HubFeature ask = b.Native("ai-ask", "Ask Settings", "Language, tone, length, web search, models... and a box to ask right there.", HubIcon.Item(I.CrystalBall), FeatureKind.Action);
		ask.GetValue = HubAsk.Summary;
		ask.Run = () => ClientHub.OpenSection("ai", 2);
		ask.Keywords = "ask question search web internet google answer pregunta buscar";
		ask.Command = ".ask";
		ask.Wide = true;
		ask.Cheat = false;

		HubFeature connection = b.Native("ai-connection", "API Key & Connection", "Your Groq key (or the built-in one), a connection test and the search key.", HubIcon.Item(I.GoldenKey), FeatureKind.Action);
		connection.GetValue = HubAiSetup.Summary;
		connection.Run = () => ClientHub.OpenSection("ai", 3);
		connection.Keywords = "ai api key groq token endpoint connection brave search";
		connection.Wide = true;
		connection.Cheat = false;

		b.Group("Plugin Maker", HubIcon.Item(I.TinkerersWorkshop), "An AI that writes, builds and fixes plugins for you: .pluginai <message>");
		HubFeature maker = b.Native("ai-pluginmaker", "Plugin Maker", "Tell it what plugin you want (or what to change in one): it writes the C#, compiles it and turns it on, like a programmer friend.", HubIcon.Item(I.TinkerersWorkshop), FeatureKind.Action);
		maker.GetValue = HubPluginMaker.Summary;
		maker.Run = () => ClientHub.OpenSection("ai", 4);
		maker.Keywords = "plugin maker ai code coder agent make create build fix imgui harmony deepseek nvidia mistral programar";
		maker.Command = ".pluginai";
		maker.Wide = true;
		maker.Cheat = false;

		b.Group("Auto Reply", HubIcon.Item(I.SpellTome), "Uses Groq (key in Connection)").Preferences = true;
		b.Row("AFK AI", "Reply for Me", "Answers for you when someone mentions you.", I.SpellTome, "ai afk chat bot", ".ai");
		HubFeature names = b.Native("ai-names", "Names It Answers To", "Your name and nicknames. Click to add or remove them.", HubIcon.Item(I.Megaphone), FeatureKind.Action);
		names.GetValue = HubAiNames.Summary;
		names.Run = () => ClientHub.OpenSection("ai", 1);
		names.Keywords = "ai nick nickname names mention call alias";
		names.Command = ".ai nick";
		names.Wide = true;
		names.Cheat = false;
		b.Row("Reply to Everyone", "Reply to Everyone", "Also answers messages meant for you that don't mention you.", I.Megaphone, "ai reply");
		b.Row("Include Game Info", "Tell It About the Game", "The world, players online and your character.", I.WorldGlobe, "ai context game");

		b.Group("Reply Style", HubIcon.Item(I.Sign)).Preferences = true;
		b.Row("Lowercase Replies", "Lowercase", null, I.Sign, "ai lowercase");
		b.Row("Remove Accents", "No Accents", null, I.Sign, "ai accents");
		b.Row("Remove Emojis", "No Emojis", null, I.Sign, "ai emoji");

		b.Group("Model & Timing", HubIcon.Item(I.Cog)).Preferences = true;
		b.Row("AI Model", "Model", "Click to switch Groq models.", I.Cog, "ai model groq", ".ai model", wide: true);
		b.Row("AI Temperature", "Creativity", "Lower is more predictable, higher is more creative.", I.Cog, "ai temperature", wide: true);
		b.Row("AI Reply Delay", "Typing Delay", "Waits before sending, like a person typing.", I.Stopwatch, "ai delay", wide: true);
		b.Row("AI Cooldown", "Time Between Replies", null, I.Stopwatch, "ai cooldown", wide: true);
		b.Row("AI Context Messages", "Messages Remembered", null, I.Book, "ai context memory", wide: true);

		b.Group("Tools", HubIcon.Item(I.Wrench));
		b.Row("Test AI", "Try It", "Asks something and shows the answer only to you.", I.SpellTome, "ai test", ".ai test", verb: "Test", cheat: false);
		b.Row("Forget Conversation", "Forget the Chat", null, I.TrashCan, "ai forget", ".ai forget", verb: "Forget", cheat: false);
		b.Row("Edit AI Config (Prompt, About Me, Key...)", "Edit AI Settings", "Opens IsaAI.json: prompt, about me, nicknames, language, key...", I.Book, "ai config prompt key", verb: "Edit", wide: true, cheat: false);
		b.Row("Reload AI Config", "Reload AI Settings", null, I.Book, "ai reload", verb: "Reload", cheat: false);

		b.Page("Names", HubIcon.Item(I.Megaphone)).Custom = HubAiNames.Draw;
		b.Find("AI names", "Names the assistant answers to.", "ai assistant names");
		b.Page("Ask", HubIcon.Item(I.CrystalBall)).Custom = HubAsk.Draw;
		b.Find("Ask the AI", "Ask the assistant anything (.ask).", "ai ask assistant question groq");
		b.Page("Connection", HubIcon.Item(I.GoldenKey)).Custom = HubAiSetup.Draw;
		b.Find("AI connection", "The assistant's key and model.", "ai key model groq connection");
		b.Find("NVIDIA API key", "The Plugin Maker's key (free at build.nvidia.com), and Mistral's.", "nvidia mistral api key plugin maker");
		b.Page("Plugin Maker", HubIcon.Item(I.TinkerersWorkshop)).Custom = HubPluginMaker.Draw;
		b.Find("Plugin Maker", "An AI that makes and changes plugins: writes the C#, builds it, turns it on.", "plugin maker ai code agent create build fix pluginai");
	}

	private static void Toolkit(CatalogBuilder b)
	{
		b.Section("toolkit", "My Toolkit", "Your own buttons and saved setups", HubIcon.Item(I.Toolbox), new Color(255, 200, 120));
		b.Page("My Buttons", HubIcon.Item(I.FallenStar)).Custom = HubToolkit.DrawActions;
		b.Find("My buttons", "Buttons that send commands or messages.", "toolkit buttons macros commands");
		b.Page("Setups", HubIcon.Item(I.Book)).Custom = HubToolkit.DrawProfiles;
		b.Find("Button setups", "Sets of buttons to switch between.", "toolkit setups profiles");
	}

	private static void Feedback(CatalogBuilder b)
	{
		b.Section("feedback", "Report a Bug", "Bugs, ideas and crash reports for the developers", HubIcon.Item(I.Buggy), new Color(240, 120, 130));
		b.Page("New Report", HubIcon.Item(I.Buggy)).Custom = HubFeedback.DrawReport;
		b.Find("Report a bug", "Send a bug report or a suggestion.", "bug report suggestion feedback");
		b.Page("Crashes", HubIcon.Item(I.Dynamite)).Custom = HubFeedback.DrawCrashes;
		b.Find("Crashes", "Crash reports of the client.", "crash reports");
		b.Page("My Reports", HubIcon.Item(I.Book)).Custom = HubFeedback.DrawHistory;
		b.Find("My reports", "Reports you sent.", "reports history");
	}

	private static void Settings(CatalogBuilder b)
	{
		b.Section("settings", "Settings", "How this panel looks and works", HubIcon.Texture("Images/UI/Settings_Inputs_2", new Rectangle(0, 0, 32, 32)), new Color(200, 210, 230));
		b.Page("Interface", HubIcon.Texture("Images/UI/Settings_Inputs_2", new Rectangle(0, 0, 32, 32)));
		b.Group("Look", HubIcon.Item(I.RainbowDye), "Make the panel yours");
		HubFeature theme = b.Native("theme", "Theme", "Colors named after places of the world. Right click goes back.", HubIcon.Item(I.RainbowDye), FeatureKind.Choice);
		theme.GetValue = () => HubTheme.Current.Name;
		theme.Cycle = delegate(bool back)
		{
			int index = Array.FindIndex(HubTheme.All, t => t.Name == HubTheme.Current.Name);
			index = (index + (back ? HubTheme.All.Length - 1 : 1)) % HubTheme.All.Length;
			HubPrefs.Theme = HubTheme.All[index].Name;
			HubPrefs.Changed();
		};
		theme.Keywords = "color colour theme ocean forest crimson hallow dungeon desert underworld shimmer";
		HubFeature opacity = b.Native("opacity", "Opacity", "How much of the game shows through the panel.", HubIcon.Item(I.Bottle), FeatureKind.Slider);
		opacity.GetSlider = () => (HubPrefs.Opacity - 0.5f) / 0.5f;
		opacity.SetSlider = delegate(float f)
		{
			HubPrefs.Opacity = 0.5f + (float)Math.Round(f * 10f) / 20f;
			HubPrefs.Changed();
		};
		opacity.GetValue = () => Math.Round(HubPrefs.Opacity * 100f) + "%";
		opacity.Wide = true;
		HubFeature size = b.Native("scale", "Panel Size", "Makes the whole panel bigger or smaller.", HubIcon.Item(I.Binoculars), FeatureKind.Slider);
		size.GetSlider = () => (HubPrefs.Scale - 0.8f) / 0.5f;
		size.SetSlider = delegate(float f)
		{
			HubPrefs.Scale = 0.8f + (float)Math.Round(f * 10f) / 20f;
			HubPrefs.Changed();
		};
		size.GetValue = () => Math.Round(HubPrefs.Scale * 100f) + "%";
		size.Wide = true;
		Flag(b, "compact", "Compact Cards", "Smaller cards, no descriptions: more on screen.", I.Book, () => HubPrefs.Compact, v => HubPrefs.Compact = v);
		Flag(b, "descriptions", "Show Descriptions", "A short line under every feature.", I.Sign, () => HubPrefs.ShowDescriptions, v => HubPrefs.ShowDescriptions = v);
		Flag(b, "sidebar", "Slim Sidebar", "Only the section icons on the left.", I.Toolbox, () => HubPrefs.CollapsedSidebar, v => HubPrefs.CollapsedSidebar = v);
		Flag(b, "animations", "Animations", "Smooth movement of switches, cards and pages.", I.FairyCritterPink, () => HubPrefs.Animations, v => HubPrefs.Animations = v);
		Flag(b, "sounds", "Interface Sounds", "The little ticks when you point at and click things.", I.Bell, () => HubPrefs.Sounds, v => HubPrefs.Sounds = v);
		Flag(b, "openhome", "Always Open on Home", "Off (normal): the panel opens where you left it.", I.FallenStar, () => HubPrefs.OpenOnHome, v => HubPrefs.OpenOnHome = v);
		HubFeature reset = b.Native("resetwindow", "Reset Window", "Puts the panel back in the middle with its normal size.", HubIcon.Item(I.MagicMirror), FeatureKind.Action);
		reset.Verb = "Reset";
		reset.Run = ClientHub.ResetWindow;
		HubFeature classic = b.Native("classic", "Classic Interface", "Go back to the old panel (come back with .menu new).", HubIcon.Item(I.Book), FeatureKind.Action);
		classic.Verb = "Switch";
		classic.Run = () => ClientHub.SetClassic(true);
		classic.Keywords = "old legacy classic ui interface";

		b.Group("Sections", HubIcon.Item(I.Toolbox), "Hide the sections you never use (search still finds their features)");
		foreach (HubSection section in b.Sections.Where(s => !s.Special && s.Id != "settings"))
		{
			string id = section.Id;
			HubFeature show = b.Native("show-" + id, section.Title, "Show it in the sidebar.", section.Icon, FeatureKind.Toggle);
			show.GetOn = () => !HubPrefs.HiddenSections.Contains(id);
			show.SetOn = delegate(bool v)
			{
				HubPrefs.HiddenSections.Remove(id);
				if (!v)
				{
					HubPrefs.HiddenSections.Add(id);
				}
				HubPrefs.Changed();
			};
			show.Keywords = "hide sidebar section";
		}

		b.Page("Client", HubIcon.Item(I.Cog));
		b.Group("Screen Buttons", HubIcon.Item(I.Acorn), "Small buttons next to your hearts; hold one to drag it").Preferences = true;
		b.Row("Client Menu Button", "Panel Button (Tree)", "Opens and closes this panel.", I.Acorn, "hud tree button", ".hud menu", cheat: false);
		b.Row("My Character Button", "My Character Button", "Your head: opens My Character.", I.Mannequin, "hud head character", ".hud character", cheat: false);
		b.Row("Music Note Button", "Music Note Button", "Opens the music player.", I.MusicBox, "hud note music", ".hud note", cheat: false);
		b.Row("Use Windows Cursor", "Windows Cursor", "The normal Windows mouse instead of Terraria's.", I.Cog, "cursor mouse", cheat: false);
		Flag(b, "dpiaware", "Sharp Display (DPI Aware)", "With Windows scaling at 125% or more: the game draws at your screen's real resolution, sharper and with everything smaller. Off: Windows stretches it like always. Takes effect when the game starts again.", I.Lens, () => DisplayScaling.DpiAware, DisplayScaling.Set);

		b.Group("Client", HubIcon.Item(I.Cog), "General behavior");
		HubFeature language = b.Native("language", "Language", "The language of IsaClient's menus and messages: the game's own, or another one. Import and export language files there too.", HubIcon.Item(I.WorldGlobe), FeatureKind.Action);
		language.Verb = "Change";
		language.Command = ".language";
		language.Keywords = "language idioma lingua sprache langue язык 语言 言語 translate";
		language.Cheat = false;
		language.GetValue = () => (IsaClient.Languages.L10n.CurrentPack ?? IsaClient.Languages.L10n.Find(IsaClient.Languages.L10n.English))?.Name ?? "English";
		language.Run = IsaClient.Languages.LanguageWindow.Open;
		Flag(b, "remember", "Remember What Was On", "Features that were on come back on next time you play.", I.Book, () => Selection.RememberEnabled, v => Selection.RememberEnabled = v, saveClient: true);
		Flag(b, "plugins", "Plugins & TerraAngel", "ImGui, the plugin overlay (Insert, .ta) and every plugin. Off: all of it is unloaded at once and the client is as it always was.", I.AlphabetStatueT, () => !IsaClient.Plugins.PluginHost.SystemOff, IsaClient.Plugins.PluginHost.SetSystemOn);
		Flag(b, "tatheme", "TerraAngel ImGui Theme", "Plugin windows (ImGui) look like TerraAngel's instead of following the panel's theme.", I.AlphabetStatueT, () => IsaClient.Plugins.ImGuiTheme.TerraAngelTheme, v => IsaClient.Plugins.ImGuiTheme.TerraAngelTheme = v, saveClient: true);
		Flag(b, "crashreports", "Send Crash Reports", "When the game crashes, the error goes to the developers so it gets fixed. Off: crashes are only kept in Report a Bug > Crashes.", I.Dynamite, () => IsaClient.Feedback.FeedbackReports.AutoCrashReports, v => IsaClient.Feedback.FeedbackReports.AutoCrashReports = v, saveClient: true);
		HubFeature report = b.Native("report", "Report a Bug or Idea", "Tell the developers about a bug, or suggest a feature.", HubIcon.Item(I.Buggy), FeatureKind.Action);
		report.Verb = "Open";
		report.GetValue = HubFeedback.Summary;
		report.Run = () => ClientHub.OpenSection("feedback");
		report.Keywords = "bug report suggest suggestion idea feedback crash problem error";
		report.Command = ".report";
		report.Cheat = false;
		Flag(b, "welcome", "Welcome Message", "The IsaClient message when you join a world.", I.Sign, () => !ClientLifecycle.HideWelcomeMessage, v => ClientLifecycle.HideWelcomeMessage = !v, saveClient: true);
		HubFeature color = b.Native("textcolor", "Client Text Color", "The color of IsaClient's messages in the chat.", HubIcon.Item(I.RainbowDye), FeatureKind.Color);
		color.GetValue = () => (ClientChat.TextColor ?? "").Length == 0 ? "[c/" + Hex(ClientChat.AccentColor) + ":Default]" : ColorName(ClientChat.AccentColor);
		color.GetColor = () => ClientChat.AccentColor;
		color.SetColor = c => ClientChat.TextColor = (c == ClientChat.DefaultTextColor) ? "" : (c.R + "," + c.G + "," + c.B);
		color.ColorDone = ClientSettings.Save;
		color.DefaultColor = ClientChat.DefaultTextColor;
		color.Keywords = "chat color textcolor";
		color.Command = ".textcolor";
		HubFeature prefix = b.Native("prefix", "Command Prefix", "The symbol IsaClient commands start with. Right click goes back.", HubIcon.Item(I.Sign), FeatureKind.Choice);
		prefix.GetValue = () => "[c/" + Hex(ClientChat.AccentColor) + ":" + ChatCommands.CommandSpecifier + "]  (" + ChatCommands.CommandSpecifier + "help)";
		prefix.Cycle = CyclePrefix;
		prefix.Keywords = "command prefix specifier dot";
		prefix.Command = ".prefix";
		HubFeature save = b.Native("save", "Save Settings Now", "Writes every setting to IsaConfig.json.", HubIcon.Item(I.Book), FeatureKind.Action);
		save.Verb = "Save";
		save.Command = ".config";
		save.Run = delegate
		{
			ClientSettings.Save();
			HubPrefs.Save();
			ClientChat.Info("Settings saved.");
		};

		b.Group("Reset to Defaults", HubIcon.Item(I.TrashCan), "Turns off a whole area and puts its values back");
		b.Row("Disable All@Player", "Reset Player & Combat", null, I.LifeCrystal, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@Travel", "Reset Movement", null, I.HermesBoots, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@Informational", "Reset Info Overlays", null, I.CellPhone, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@Render", "Reset Visuals", null, I.SpelunkerPotion, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable & Reset All@Cosmetic", "Reset Style", null, I.RainbowCursor, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable & Reset All@Miscellaneous", "Reset World Tools & Misc", null, I.Toolbox, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable & Reset All@World Edit", "Reset Building", null, I.ArchitectGizmoPack, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@Biome Spoofers", "Reset Fake Biomes", null, I.MushroomGrassSeeds, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable & Reset All@Audio Tweaks", "Reset Soundtrack", null, I.MusicBoxBoss1, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@Music", "Reset Music Player", null, I.MusicBox, "reset disable", verb: "Reset", cheat: false);
		b.Row("Disable All@AI", "Reset AI Assistant", null, I.SpellTome, "reset disable", verb: "Reset", cheat: false);

		b.Group("Panel Data", HubIcon.Item(I.Chest));
		HubFeature favorites = b.Native("clearfav", "Clear Favorites", "Removes every favorite (click twice).", HubIcon.Texture("Images/UI/ButtonFavoriteInactive"), FeatureKind.Action);
		favorites.Verb = "Clear";
		favorites.Confirm = true;
		favorites.Run = delegate
		{
			HubPrefs.Favorites.Clear();
			HubPrefs.Changed();
		};
		HubFeature recent = b.Native("clearrecent", "Clear Recently Used", null, HubIcon.Item(I.Stopwatch), FeatureKind.Action);
		recent.Verb = "Clear";
		recent.Run = delegate
		{
			HubPrefs.Recent.Clear();
			HubPrefs.Changed();
		};
	}

	private static void ChatPictureSettings(CatalogBuilder b)
	{
		b.Group("Chat Pictures", HubIcon.Item(I.PaintingTheSeason), "Send pictures and videos (as GIFs) in the chat").Preferences = true;
		Flag(b, "chatpics-on", "Chat Pictures", "Send pictures in the chat (the picture button on the chat bar, Ctrl+V, .sendpic) and see the ones IsaClient players send.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.Enabled, v => IsaClient.ChatImages.ChatImages.Enabled = v, saveClient: true);
		HubFeature send = b.Native("chatpics-send", "Send Pictures", "Pick pictures or a video; they wait above the chat bar until you send a message.", HubIcon.Item(I.PaintingTheSeason), FeatureKind.Action);
		send.Verb = "Pick";
		send.Command = ".sendpic";
		send.Keywords = "image picture photo screenshot gif video send imgur paste";
		send.Cheat = false;
		send.Run = IsaClient.ChatImages.ChatAttachments.OpenPicker;
		Flag(b, "chatpics-show", "Show Pictures in Chat", "Picture links IsaClient players send show as framed thumbnails. Off: they stay links.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.ShowInChat, v => IsaClient.ChatImages.ChatImages.ShowInChat = v, saveClient: true);
		PictureChoice(b, "chatpics-size", "Thumbnail Size", "How tall pictures are in the chat. Right click goes back.", new[] { 3, 4, 6, 8 }, new[] { "Small", "Medium", "Large", "Huge" }, () => IsaClient.ChatImages.ChatImages.ThumbLines, v => IsaClient.ChatImages.ChatImages.ThumbLines = v);
		PictureChoice(b, "chatpics-via", "Send Pictures Through", "IsaClient only: your pictures go straight to IsaClient players (the server and other clients never see them). Server chat: they go in the normal chat, as short codes or links. Your text always goes to the normal chat. Right click goes back.", new[] { 1, 0 }, new[] { "IsaClient only", "Server chat" }, () => IsaClient.ChatImages.ChatImages.SendVia, v => IsaClient.ChatImages.ChatImages.SendVia = v);
		Flag(b, "chatpics-codes", "Send Pictures as Short Codes", "Your pictures go as a few words between « » instead of a long Imgur link: players without IsaClient see short random words. IsaClient players see the picture either way. Off: the links are sent as they are.", I.PaintingTheSeason, () => IsaClient.ChatImages.PictureCodes.Enabled, v => IsaClient.ChatImages.PictureCodes.Enabled = v, saveClient: true);
		Flag(b, "chatpics-animate", "Animate GIFs", "GIFs move in the chat and in the viewer. Off: they stand still.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.Animate, v => IsaClient.ChatImages.ChatImages.Animate = v, saveClient: true);
		Flag(b, "chatpics-autoload", "Load Pictures Automatically", "Off: a picture loads when you click it (Imgur doesn't see you until then).", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.AutoLoad, v => IsaClient.ChatImages.ChatImages.AutoLoad = v, saveClient: true);
		Flag(b, "chatpics-button", "Picture Button on the Chat Bar", "A little framed picture at the end of the chat bar opens the file picker.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.AttachButton, v => IsaClient.ChatImages.ChatImages.AttachButton = v, saveClient: true);
		Flag(b, "chatpics-gifbutton", "GIF Button on the Chat Bar", "A GIF button next to the picture button opens the GIF search (any GIF, or anime reactions). Also .gif; .hug, .pat, .kiss... send a reaction with a GIF.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.GifButton, v => IsaClient.ChatImages.ChatImages.GifButton = v, saveClient: true);
		Flag(b, "chat-emoticons", "Emoticons Button on the Chat Bar", "The emote bubble on the chat bar opens a small panel of text emoticons (:) ^_^ uwu...); a click writes one in your message.", I.PaintingTheSeason, () => IsaClient.ChatImages.EmoticonPicker.Button, v => IsaClient.ChatImages.EmoticonPicker.Button = v, saveClient: true);
		HubFeature gifs = b.Native("chatpics-gifs", "Search GIFs", "Trending and search (Klipy), and anime reactions by kind (nekos.best). The GIF goes with your next message.", HubIcon.Item(I.PaintingTheSeason), FeatureKind.Action);
		gifs.Verb = "Open";
		gifs.Command = ".gif";
		gifs.Keywords = "gif gifs anime reaction hug pat kiss klipy nekos search";
		gifs.Cheat = false;
		gifs.Run = () => IsaClient.ChatImages.GifPicker.Open();
		Flag(b, "chatpics-paste", "Ctrl+V Attaches Pictures", "A copied picture (or picture files copied in Explorer) is attached when you paste in the chat.", I.PaintingTheSeason, () => IsaClient.ChatImages.ChatImages.PasteImages, v => IsaClient.ChatImages.ChatImages.PasteImages = v, saveClient: true);
		PictureChoice(b, "chatpics-gif-length", "Video GIF Length", "How much of a video goes in its GIF (from the start). Right click goes back.", new[] { 5, 10, 15, 20, 30 }, null, () => IsaClient.ChatImages.ChatImages.GifSeconds, v => IsaClient.ChatImages.ChatImages.GifSeconds = v, " seconds");
		PictureChoice(b, "chatpics-gif-size", "Video GIF Size", "Width of the GIF a video becomes: bigger is sharper and heavier. Right click goes back.", new[] { 240, 320, 360, 480, 640 }, null, () => IsaClient.ChatImages.ChatImages.GifWidth, v => IsaClient.ChatImages.ChatImages.GifWidth = v, " px wide");
		PictureChoice(b, "chatpics-gif-fps", "Video GIF Smoothness", "Frames a second of a video's GIF: more is smoother and heavier. Right click goes back.", new[] { 10, 12, 15, 20, 25 }, null, () => IsaClient.ChatImages.ChatImages.GifFps, v => IsaClient.ChatImages.ChatImages.GifFps = v, " fps");
		HubFeature folder = b.Native("chatpics-folder", "Saved Pictures", "Pictures you saved from the viewer.", HubIcon.Item(I.Chest), FeatureKind.Action);
		folder.Verb = "Open";
		folder.Command = ".sendpic folder";
		folder.Cheat = false;
		folder.Run = delegate
		{
			System.IO.Directory.CreateDirectory(IsaClient.ChatImages.ChatImages.SavedFolder);
			Utils.OpenFolder(IsaClient.ChatImages.ChatImages.SavedFolder);
		};
	}

	/// <summary>A setting that steps through <paramref name="values"/> (shown as <paramref name="names"/>, or the number and <paramref name="unit"/>).</summary>
	private static void PictureChoice(CatalogBuilder b, string id, string title, string description, int[] values, string[] names, Func<int> get, Action<int> set, string unit = "")
	{
		HubFeature feature = b.Native(id, title, description, HubIcon.Item(I.PaintingTheSeason), FeatureKind.Choice);
		feature.Cheat = false;
		feature.Keywords = "chat picture image gif video";
		feature.GetValue = delegate
		{
			int index = Array.IndexOf(values, get());
			return (names != null && index >= 0) ? names[index] : (get() + unit);
		};
		feature.Cycle = delegate(bool back)
		{
			int index = Array.IndexOf(values, get());
			index = (index < 0) ? 0 : ((index + (back ? values.Length - 1 : 1)) % values.Length);
			set(values[index]);
			ClientSettings.Save();
		};
	}

	internal static void Flag(CatalogBuilder b, string id, string title, string description, int icon, Func<bool> get, Action<bool> set, bool saveClient = false)
	{
		HubFeature feature = b.Native(id, title, description, HubIcon.Item(icon), FeatureKind.Toggle);
		feature.GetOn = get;
		feature.SetOn = delegate(bool v)
		{
			set(v);
			if (saveClient)
			{
				ClientSettings.Save();
			}
			else
			{
				HubPrefs.Changed();
			}
		};
	}

	/// <summary>"Magenta" or "#12AB34", written in that color.</summary>
	private static string ColorName(Color color)
	{
		return "[c/" + Hex(color) + ":" + (NamedColors.NameOf(color) ?? ("#" + Hex(color))) + "]";
	}

	private static readonly char[] _prefixes = { '.', '!', '#', '$', '%', '&', '*', '-', '+', '=', '~', ';', ',', '?', '@' };

	private static void CyclePrefix(bool back)
	{
		int index = Array.IndexOf(_prefixes, ChatCommands.CommandSpecifier);
		index = (index < 0) ? 0 : ((index + (back ? _prefixes.Length - 1 : 1)) % _prefixes.Length);
		ChatCommands.CommandSpecifier = _prefixes[index];
		ClientSettings.Save();
	}

	private static string Hex(Color color)
	{
		return color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}
}
