using IsaClient.Menu;
using Microsoft.Xna.Framework;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>Sections of the new panel about your character: Player, Combat, Movement, Items.</summary>
internal static class GameplayContent
{
	public static void Build(CatalogBuilder b)
	{
		Player(b);
		Combat(b);
		Movement(b);
		Items(b);
	}

	private static void Player(CatalogBuilder b)
	{
		b.Section("player", "Player", "Health, survival and what your character can do", HubIcon.Item(I.LifeCrystal), new Color(120, 220, 120));
		HubGroup character = b.Group("Your Character", HubIcon.Item(I.Mannequin), "Look, items and stats: edit them like in character creation");
		character.Preferences = true;
		HubFeature edit = b.Native("editme", "Edit My Character", "Change your look, items (any item, like the Journey menu) and stats.", HubIcon.Item(I.FamiliarWig), FeatureKind.Action);
		edit.Verb = "Edit";
		edit.Keywords = "appearance hair color look inventory items editor journey";
		edit.Run = () => HubCharacterEditor.OpenMe(fromCharacterWindow: false);
		HubFeature create = b.Native("newcharacter", "New Character", "Make a new character file with any look and items.", HubIcon.Item(I.Mannequin), FeatureKind.Action);
		create.Verb = "Create";
		create.Keywords = "create character plr new";
		create.Run = () => HubCharacterEditor.OpenNew(fromCharacterWindow: false);
		HubFeature window = b.Native("mycharacter", "My Character", "Your saved characters: wear them, edit them, paste them as pixel art.", HubIcon.Item(I.Mannequin), FeatureKind.Action);
		window.Verb = "Open";
		window.Keywords = "plr characters wear";
		window.Run = CharacterWindow.Open;

		b.Group("Survival", HubIcon.Item(I.LifeCrystal), "Stay alive no matter what");
		b.Row("Godmode", "God Mode", "Nothing can hurt or kill you.", HubIcon.Texture("Images/UI/Creative/Infinite_Powers", new Rectangle(218, 0, 30, 34)), "invincible immortal god", ".god");
		b.Row("Anti Death", "Can't Die", "You still take hits, but your health never reaches zero.", I.AnkhShield, "immortal survive");
		b.Row("Dodge All Damage", "Dodge Everything", "Every attack misses you.", I.BlackBelt, "evade invincible");
		b.Row("Instant Regen", "Instant Regeneration", "Your health refills right away.", I.BandofRegeneration, "heal health life");
		b.Row("Infinite Mana", "Infinite Mana", "Spells never use up your mana.", I.ManaCrystal, "magic");
		b.Row("Infinite Breath", "Infinite Breath", "Never drown, in water or anywhere else.", I.BreathingReed, "water drown gills");
		b.Row("No Fall Damage", "No Fall Damage", "Land safely from any height.", I.LuckyHorseshoe, "fall");
		b.Row("Ignore Damaging Blocks", "Safe Blocks", "Hellstone, meteorite, spikes and honey don't affect you.", I.ObsidianSkull, "spikes hellstone meteorite honey");
		b.Row("Instant Respawn", "Instant Respawn", "Come back the moment you die.", I.Tombstone, "death respawn");
		b.Row("Always PvP Deaths", "Respawn Like in PvP", "Deaths count as PvP: respawn at full health, safe for longer.", I.Headstone, "pvp death");
		b.Row("Item Drop Mode", "Items Dropped on Death", "What Mediumcore and Hardcore characters drop when they die.", I.LockBox, "mediumcore hardcore drop death", wide: true);
		b.Row("Buff Loss Mode", "Buffs Kept on Death", "Which buffs and debuffs you keep when you die.", I.RegenerationPotion, "buff persist death", wide: true);

		b.Group("Buffs & Debuffs", HubIcon.Item(I.RegenerationPotion), "Control what can affect you");
		HubFeature buffs = b.Native("buffpicker", "Buff Picker", "Give yourself any buff: hover to read it, right click for infinite.", HubIcon.Item(I.WrathPotion), FeatureKind.Action);
		buffs.Verb = "Open";
		buffs.Keywords = "buffs potions give add infinite";
		buffs.Command = ".buff";
		buffs.Run = CharacterWindow.OpenBuffs;
		b.Row("Anti Debuff", "Debuff Immunity", "No debuffs, not even potion sickness.", I.AnkhCharm, "immune poison sickness");
		b.Row("Remove Debuffs (Right Click)", "Right-Click Debuffs Away", "Right-click any debuff in your buff bar to remove it.", I.Vitamins, "cancel remove debuff");
		b.Row("Stack Buffs", "Stack Buffs", "Buffs that replace each other stay: all Well Fed tiers, flasks...", I.ToxicFlask, "flask well fed food");
		b.Row("Suppress Horrified Debuff", "Ignore Horrified", "The Wall of Flesh can't trap you with Horrified.", I.GuideVoodooDoll, "wall of flesh");
		b.Row("Suppress Creative Shock Debuff", "Ignore Creative Shock", "Keep building and mining during boss fights in Journey worlds.", I.Picksaw, "journey build boss");
		b.Row("Ignore Crowd Control", "Ignore Crowd Control", "Move freely while frozen, webbed, stoned or stunned.", I.CountercurseMantra, "frozen stoned webbed stun");

		b.Group("Abilities", HubIcon.Item(I.CharmofMyths), "Bend the rules a little");
		b.Row("Infinite Minions", "Unlimited Minions", "Summon as many minions as you want.", I.SlimeStaff, "summon minion");
		b.Row("Infinite Ammo", "Infinite Ammo", "Arrows, bullets and other ammo are never used up.", I.EndlessQuiver, "arrows bullets");
		b.Row("Extended Pick-Up Range", "Item Magnet", "Pick up items from far away.", I.CelestialMagnet, "pickup grab range");
		b.Row("Extended Placement Range", "Long Reach", "Build and dig much further away.", HubIcon.Texture("Images/UI/Creative/Infinite_Powers", new Rectangle(254, 2, 32, 30)), "range reach build place");
		b.Row("Unrestrict Rod Of Discord", "Free Rod of Discord", "No Chaos State limits on the Rod of Discord.", I.RodofDiscord, "teleport chaos state");
		b.Row("Enable Extra Accessory Slots", "Extra Accessory Slots", "Unlock the Expert and Master Mode accessory slots.", I.DemonHeart, "accessory slot expert master");
		b.Row("Equip Duplicate Accessories", "Duplicate Accessories", "Wear the same accessory more than once.", I.ShinyRedBalloon, "accessory twice");
		b.Row("Grapple While Mounted", "Grapple While Mounted", "Use hooks without getting off your mount.", I.GrapplingHook, "hook mount");
		b.Row("Items On Transform Mounts", "Items on Rat, Bat & Pixie", "Use weapons and items while you are the Rat, the Bat or the Pixie.", I.GrapplingHook, "mount rat bat pixie transform weapon item use");
		b.Row("Ghost Mode", "Ghost Mode", "Float around as a ghost. Turning it off respawns you if you were dead.", I.GhostMask, "ghost spectate", ".ghost");
		b.Row("Luck Override", "Luck", "Max luck, or Dream luck far beyond what the game allows.", I.LuckyCoin, "luck lucky", wide: true);
		b.Row("Force Dash Type", "Dash", "Give yourself any dash (Shield of Cthulhu, Tabi, Solar...).", I.EoCShield, "dash tabi shield", wide: true);

		b.Group("Armor Set Bonuses", HubIcon.Item(I.StardustHelmet), "Set bonuses without wearing the armor");
		b.Row("Always Explode on Hit", "Solar Explosion", "Getting hit makes you explode like Solar Flare armor.", I.SolarFlareHelmet, "solar flare set bonus");
		b.Row("Always Have Stardust Guardian", "Stardust Guardian", "The Stardust Guardian always protects you.", I.StardustHelmet, "stardust set bonus");
		b.Row("Always Spirit Heal", "Spectre Healing", "Hurting enemies sends healing orbs to the most hurt player nearby.", I.SpectreHood, "spectre heal set bonus");
		b.Row("Always Spectre Wrath", "Spectre Wrath", "Hurting enemies sends attacking orbs at enemies nearby.", I.SpectreMask, "spectre damage set bonus");
		b.Row("Always Nebula Boosters", "Nebula Boosters", "Hurting enemies drops Nebula boosters.", I.NebulaHelmet, "nebula set bonus");
	}

	private static void Combat(CatalogBuilder b)
	{
		b.Section("combat", "Combat", "Auto clicker, targeting and clearing enemies", HubIcon.Item(I.TerraBlade), new Color(255, 120, 90));
		b.Group("Auto Clicker", HubIcon.Item(I.Stopwatch), "Uses your held item by itself. Bind a key in Settings > Controls > IsaClient: Combat > Auto Clicker.");
		b.Row("Auto Clicker", "Auto Clicker", "Clicks for you, even with the game in the background.", I.Stopwatch, "autoclick macro afk", ".ac");
		b.Row("Auto Clicker Mode", "Click Style", "Hold, click repeatedly, or hold & release in a loop.", I.Cog, "autoclick hold release");
		b.Row("Auto Clicker Button", "Mouse Button", "Left (use item), right (use/interact) or both.", I.Cog, "autoclick left right");
		b.Row("Auto Clicker Pauses in Inventory", "Pause in Inventory", "Stops clicking while your inventory is open.", I.PiggyBank, "autoclick inventory");
		b.Row("Auto Clicker Speed", "Clicks per Second", "How fast it clicks in click mode (up to 30).", I.Stopwatch, "autoclick cps speed", wide: true);
		b.Row("Auto Clicker Hold/Release", "Hold & Release Timing", "Left click changes the hold time, right click the release time.", I.Stopwatch, "autoclick hold release timing", wide: true);
		b.Row("Auto Clicker Timer", "Stop After", "Turns the auto clicker off after this many minutes.", I.Stopwatch, "autoclick timer minutes", wide: true);

		b.Group("Clear Enemies", HubIcon.Item(I.DeathSickle), "Get rid of everything around you");
		b.Row("Kill Aura", "Kill Aura", "Kills every hostile creature within this radius around you.", I.DeathSickle, "killaura aura radius", ".killaura", wide: true);
		b.Row("Auto-Butcher Hostile NPCs", "Keep Killing Enemies", "Kills every hostile creature all the time.", I.BookofSkulls, "butcher kill all");
		b.Row("Butcher Hostile NPCs", "Kill All Enemies", "Kills every hostile creature once.", I.DemonScythe, "butcher kill all", verb: "Kill");
		b.Row("Butcher Friendly NPCs", "Kill Town NPCs", "Kills every friendly creature, town NPCs too.", I.Gravestone, "butcher town npc", verb: "Kill");
		b.Row("Kill All Owned Projectiles", "Remove My Projectiles", "Removes every projectile you own.", I.WoodenArrow, "projectile clear", verb: "Remove");

		b.Group("Targeting", HubIcon.Item(I.SniperScope), "Lock-on works with mouse and keyboard too");
		b.Row("Enable Lock-On", "Lock-On Targeting", "The controller lock-on, for mouse and keyboard (bind a key in Settings > Controls > IsaClient: Combat).", I.SniperScope, "lockon aim target");
		b.Row("Lock-On Can Target Hostile Players", "Lock-On Targets Players", "Lock-on can also pick hostile players in PvP.", I.ReconScope, "lockon pvp players");
		b.Row("Smart Aim", "Smart Aim", "Lock-on leads moving targets: it predicts their path and aims where your shot meets them.", I.RifleScope, "aim predict lead aimbot", ".smartaim");
		b.Row("Auto Aim", "Auto Aim", "No key: weapons lock on the target nearest the cursor.", I.DestroyerEmblem, "aim aimbot auto", ".autoaim");
	}

	private static void Movement(CatalogBuilder b)
	{
		b.Section("movement", "Movement", "Flight, noclip, teleports and how the ground feels", HubIcon.Item(I.HermesBoots), new Color(240, 200, 90));
		b.Group("Flight", HubIcon.Item(I.AngelWings), "Fly further and better");
		b.Row("Infinite Flight", "Infinite Flight", "Your wings never run out of flight time.", I.AngelWings, "fly wings");
		b.Row("Infinite Carpet Flight", "Infinite Flying Carpet", "The Flying Carpet never runs out while you hold jump.", I.FlyingCarpet, "carpet fly float");
		ClientContent.Flag(b, "carpet-broom", "Fly the Carpet Like a Broom", "With the Flying Carpet on as an accessory, R gets on and off it and it flies like the Witch's Broom, in every direction and without running out. Everyone sees the carpet (take wings and rocket boots off so others see it right away).", I.FlyingCarpet, () => IsaClient.Movement.CarpetBroom.Enabled, v => IsaClient.Movement.CarpetBroom.Enabled = v, saveClient: true);
		b.Row("Can Always Hover & Boost Upwards", "Hover With Any Wings", "Every pair of wings can hover and boost upwards.", I.WingsNebula, "hover wings");
		b.Row("Max Wing Stats", "Best Wing Stats", "Your wings fly like the best wings in the game.", I.WingsSolar, "wings speed");

		b.Group("Noclip", HubIcon.Item(I.GravitationPotion), "Move through anything. Also on its own key in Controls.");
		b.Row("Noclip", "Noclip", "No collision: fly freely through blocks.", I.GravitationPotion, "fly walls through", ".noclip");
		b.Row("Noclip Speed", "Noclip Speed", "Slow, normal or fast.", I.SwiftnessPotion, "noclip speed", wide: true);

		b.Group("Teleport", HubIcon.Item(I.MagicMirror), "Bind the keys in Controls > IsaClient");
		b.Row("Mouse Teleportation", "Teleport to Cursor", "Press your teleport key to jump to the mouse.", I.RodofDiscord, "tp cursor mouse");
		b.Row("Map Teleportation", "Teleport on the Map", "Press your map teleport key on the full map to go there.", I.TrifoldMap, "tp map");
		b.Row("Bypass Teleport Requests", "Teleport Without Asking", "Teleport items work even where the server does not allow them.", I.TeleportationPotion, "tp server");

		b.Group("Ground & Physics", HubIcon.Item(I.IceSkates), "Change how surfaces and gravity treat you");
		b.Row("Asphalt Surfaces", "Speedy Ground", "Every surface lets you run extremely fast.", I.AsphaltBlock, "asphalt speed run");
		b.Row("Bouncy Surfaces", "Bouncy Ground", "Every surface bounces you up when you land.", I.PinkGel, "bounce");
		b.Row("Always Portal Physics", "Portal Physics", "Move as if you just came out of a portal.", I.PortalGun, "portal momentum");
		b.Row("Ignore Liquids", "Ignore Liquids", "Move through water, lava and honey as if they weren't there.", I.WaterWalkingPotion, "water lava honey swim");
		b.Row("Surface Friction", "Ground Friction", "Normal, no ice, everything is ice, or no friction at all.", I.IceSkates, "ice friction slide", wide: true);
		b.Row("Conveyer Belt Handling", "Conveyor Belts", "Ignore conveyor belts, or make every block one.", I.ConveyorBeltLeft, "conveyor belt", wide: true);
	}

	private static void Items(CatalogBuilder b)
	{
		b.Section("items", "Items", "Crafting, inventory and the held item editor", HubIcon.Item(I.TinkerersWorkshop), new Color(255, 214, 90));
		b.Page("Crafting & Inventory", HubIcon.Item(I.WorkBench));
		b.Group("Crafting", HubIcon.Item(I.WorkBench), "Make anything, anywhere");
		b.Row("Free Crafting", "Free Crafting", "Craft as if you had every material.", I.WorkBench, "craft materials free");
		b.Row("Craft Anywhere", "Craft Anywhere", "Craft as if you stood next to every crafting station.", I.AlchemyTable, "craft station");
		b.Row("Journey Menu", "Journey Powers", "Journey menu on any character and server (C in the inventory).", HubIcon.Texture("Images/UI/Creative/Journey_Toggle"), "journey creative duplicate research", ".journey");

		b.Group("Inventory", HubIcon.Item(I.PiggyBank), "Fewer rules for your items");
		b.Row("Any Item in Any Slot", "Any Item in Any Slot", "Put any item in armor, accessory, vanity, ammo and coin slots.", I.Mannequin, "slot armor accessory", ".anyslot");
		b.Row("No Swap Restriction", "Swap Items Anytime", "Switch items even while another one is being used.", I.Toolbelt, "swap hotbar");
		b.Row("Free Shops", "Free Shopping", "Everything in shops costs nothing.", I.GoldCoin, "shop buy money free");
		b.Row("Open Locked Chests", "Open Locked Chests", "Open chests even when they are locked.", I.GoldenKey, "chest key locked");
		b.Row("Anything Fits on Plates/Weapon Racks", "Display Anything", "Put any item on plates and weapon racks.", I.WeaponRack, "plate rack display");

		b.Page("Item Editor", HubIcon.Item(I.TinkerersWorkshop)).Custom = HubItemEditor.Draw;
		b.Replaced(IsaClient.Menu.HeldItemMenu.Entries);
		b.Find("Item Editor", "Change any item: stats, use, shooting, tools, type, looks, network id.", "item editor held modify weapon");
		b.Find("Item damage", "Damage, critical chance, knockback, mana cost.", "dmg damage crit knockback mana");
		b.Find("Item prefix", "Every prefix the item can roll.", "prefix modifier reforge");
		b.Find("Use time", "Use time, animation time, size.", "usetime speed animation scale size");
		b.Find("Use style", "How the item is swung, held or used.", "usestyle swing");
		b.Find("Hold style", "How the item is held while not in use.", "holdstyle hold");
		b.Find("Auto swing", "Auto reuse, turn while using, hold to use.", "autoreuse autoswing channel turn");
		b.Find("Item projectile", "What the item shoots and how fast.", "projectile shoot speed");
		b.Find("Shots per use", "How many projectiles one use shoots.", "multishot times shots");
		b.Find("Shot pattern", "Shoot like any weapon (Star Wrath, Zenith...), with a preview.", "pattern style shootstyle zenith starwrath");
		b.Find("Item ammo", "The ammo it uses.", "ammo");
		b.Find("Pickaxe power", "Pickaxe, axe, hammer, reach, fishing.", "pickaxe axe hammer tools reach fishing");
		b.Find("Places block", "What block or wall it places.", "place tile wall build");
		b.Find("Damage class", "Melee, ranged, magic, summon, sentry.", "class melee ranged magic summon");
		b.Find("Item defense", "Defense, accessory, vanity, wings, buff.", "defense accessory vanity wings buff");
		b.Find("Item name", "The item's own name and the name in My Items.", "rename name custom");
		b.Find("Item description", "Your own lines in its tooltip.", "description tooltip text lines");
		b.Find("Tag color", "The wire tag on its icon.", "tag wire color label");
		b.Find("Item rarity", "The color of its name, and its tint.", "rarity color tint");
		b.Find("Item network id", "The item servers and others see.", "netid spoof network id disguise");
		b.Page("My Items", HubIcon.Item(I.Safe)).Custom = HubMyItems.Draw;
		b.Find("My Items", "Your IsaClient items and the ones others linked you; also in the Journey menu.", "my items presets saved custom library share link journey");
		b.Find("Import item presets", "Turn the old item presets into My Items.", "import presets old");

		b.Page("Old Presets", HubIcon.Item(I.Book));
		b.Group("Item Mod Presets", HubIcon.Item(I.Book), "The older item presets (by item type). My Items can import them.");
		b.Row("Current Preset", "Current Preset", "Left/right click to pick the preset.", I.Book, "preset", wide: true, cheat: false);
		b.Row("Preset Author", "Author", null, I.Sign, "preset author", wide: true, cheat: false);
		b.Row("Apply Preset", "Apply Preset", "Apply the preset to your items.", I.Book, "preset apply", wide: true, cheat: false);
		b.Row("Preset Tied To Character Name", "Only for This Character", null, I.Book, "preset character", wide: true, cheat: false);
		b.Row("Load Item From Current Preset", "Load Held Item From Preset", null, I.Book, "preset load", cheat: false);
		b.Row("Create New Preset", "New Preset", "Creates a preset named after your character.", I.Book, "preset new", wide: true, cheat: false);
		b.Row("Delete Current Preset", "Delete Preset", null, I.TrashCan, "preset delete", cheat: false);
	}
}
