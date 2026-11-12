using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>Sections of the new panel about the world: Building, World, Bosses & Events.</summary>
internal static class WorldContent
{
	public static void Build(CatalogBuilder b)
	{
		Building(b);
		World(b);
		Bosses(b);
	}

	private static void Building(CatalogBuilder b)
	{
		b.Section("building", "Building", "Select, copy, paste, brushes and paint", HubIcon.Item(I.ArchitectGizmoPack), new Color(255, 150, 110));
		b.Page("Select & Edit", HubIcon.Item(I.Ruler));
		b.Group("Selection", HubIcon.Item(I.Ruler), "Turn on Selection Mode, then left and right click two corners");
		b.Row("Selection Mode", "Selection Mode", "Pick an area with left and right click to work on it.", I.Ruler, "select area region", wide: true);
		b.Row("Copy Selection", "Copy", "Copies the selected area for pasting or saving.", I.Book, "copy clipboard", verb: "Copy");
		b.Row("Clear Selection", "Clear Selection", "Forgets the selected area.", I.TrashCan, "deselect", verb: "Clear");
		b.Row("Save Copied Tiles as Schematic", "Save as Schematic", "Saves what you copied as a TEdit schematic file.", I.Book, "tedit schematic file export", verb: "Save", wide: true);
		b.Row("Clear Copied Tiles", "Forget Copied Tiles", "Empties what you copied.", I.TrashCan, "clipboard clear", verb: "Clear", wide: true);

		b.Group("Fill & Delete", HubIcon.Item(I.Drax), "Works on the selected area");
		b.Row("Fill Selection", "Fill With Held Block", "Fills the area with the block or wall in your hand.", I.StoneBlock, "fill place", wide: true);
		b.Row("Remove From Selection", "Remove Held Block", "Removes the held block from the area.", I.Drax, "remove", wide: true);
		b.Row("Delete Everything Selected", "Delete Everything", "Removes blocks, walls and liquids in the area.", I.Dynamite, "erase delete clear", verb: "Delete");
		b.Row("Delete Selected Tiles", "Delete Blocks", "Removes only the blocks.", I.Drax, "erase tiles", verb: "Delete");
		b.Row("Delete Selected Walls", "Delete Walls", "Removes only the walls.", I.Pwnhammer, "erase walls", verb: "Delete");
		b.Row("Delete Selected Liquid", "Delete Liquids", "Removes only the water, lava, honey and shimmer.", I.SuperAbsorbantSponge, "erase liquid water lava", verb: "Delete");
		b.Row("Remove Protected Walls", "Remove Protected Walls", "Also delete Dungeon and Lihzahrd walls.", I.LihzahrdBrick, "dungeon temple walls");
		b.Row("Cancel Fill/Delete Action", "Stop Fill / Delete", "Stops a fill or delete that is still running.", I.TrashCan, "cancel stop", verb: "Stop");
		b.Row("Tile Packet Type", "Block Updates", "Chunk for vanilla servers, Singular for TShock.", I.Wire, "packet chunk singular tshock");

		b.Group("Convert Biome", HubIcon.Item(I.Clentaminator), "Like the Clentaminator, on the selected area");
		b.Row("Convert to Purity", "Purity", null, I.GreenSolution, "convert purify", verb: "Convert");
		b.Row("Convert to Corruption", "Corruption", null, I.PurpleSolution, "convert corrupt", verb: "Convert");
		b.Row("Convert to Hallow", "Hallow", null, I.BlueSolution, "convert hallow", verb: "Convert");
		b.Row("Convert to Crimson", "Crimson", null, I.RedSolution, "convert crimson", verb: "Convert");
		b.Row("Convert Jungle to Mushrooms", "Jungle to Mushroom", null, I.DarkBlueSolution, "convert mushroom", verb: "Convert");
		b.Row("Convert to Desert", "Desert", null, I.SandSolution, "convert desert sand", verb: "Convert");
		b.Row("Convert to Snow", "Snow", null, I.SnowSolution, "convert snow ice", verb: "Convert");
		b.Row("Convert to Forest", "Forest", null, I.DirtSolution, "convert forest dirt", verb: "Convert");

		b.Page("Paste", HubIcon.Item(I.Book));
		b.Group("Paste", HubIcon.Item(I.Book), "Copy or load something, turn on Paste Mode and click where it goes");
		b.Row("Paste Mode", "Paste Mode", "Shows what you copied under the mouse; click to paste it.", I.Book, "paste place", wide: true);
		b.Row("Auto-Exit Paste", "Exit Paste After Pasting", "Paste mode turns itself off once a paste starts.", I.Book, "paste exit", wide: true);
		b.Row("Blend Tiles", "Keep Existing Blocks", "Empty parts of the paste don't erase what is already there.", I.StoneBlock, "blend air merge");
		b.Row("Paste in TShock", "Paste on TShock", "Lets pasting work on TShock servers.", I.Wire, "tshock paste");
		b.Row("Undo Last Paste", "Undo Last Paste", "Puts back what was there before the last paste (Ctrl+Z).", I.IronAnvil, "undo ctrl z", ".undo", verb: "Undo", wide: true);
		b.Row("Cancel Paste", "Stop Paste", "Stops a paste that is still running.", I.TrashCan, "cancel stop", verb: "Stop", wide: true);

		b.Group("Schematics & Pictures", HubIcon.Item(I.PaintingTheSeason), "Load a schematic or turn a picture into blocks. You can also drop files on the game window.");
		b.Row("Load Tiles From Schematic", "Load Schematic", "Opens a TEdit schematic to paste.", I.Book, "tedit schematic open load", verb: "Open", wide: true);
		b.Row("Load Image (Draw Photo)", "Picture to Blocks", "Turns an image into blocks and walls ready to paste.", I.PaintingTheSeason, "image photo picture pixel art drawpic", ".drawpic", verb: "Open", wide: true);
		b.Row("Image Scale", "Picture Size", "Shrinks pictures before turning them into blocks.", I.PaintingTheSeason, "image scale picscale", ".picscale", wide: true);

		b.Page("Picture to Blocks", HubIcon.Item(I.TheScreamer)).Custom = HubPictureArt.Draw;
		b.Find("Picture to Blocks", "Turn any picture into walls and blocks with their real colors: size, color matching, dithering and filters, with a preview.", "image photo picture pixel art drawpic picscale dmode webp dither colors");
		b.Page("Sprite Art", HubIcon.Item(I.PaintingTheSeason)).Custom = HubSpriteArt.Draw;
		b.Find("Sprite Art", "Any sprite of the game pasted as pixel art made of blocks.", "sprite pixel art paste items npcs bosses projectiles buffs");
		b.Page("Brushes & Paint", HubIcon.Item(I.Paintbrush));
		b.Group("Brushes", HubIcon.Item(I.Paintbrush), "Hold the item the brush uses and click on the world or the full map");
		b.Row("Brush Shape", "Brush Shape", "Circle or square.", I.Paintbrush, "brush circle square", wide: true);
		b.Row("Brush Pattern", "Brush Pattern", "Dithers, lines and other patterns. Right click goes back.", I.Paintbrush, "pattern dither", wide: true);
		b.Row("Tile Brush", "Block Brush", "Draw with the block in your hand.", I.StoneBlock, "brush tiles draw", wide: true);
		b.Row("Tile Brush Size", "Block Brush Size", null, I.StoneBlock, "brush size", wide: true);
		b.Row("Erase Brush", "Eraser", "Erase blocks and walls (hold anything that is not a block or wall).", I.Drax, "erase brush", wide: true);
		b.Row("Erase Brush Size", "Eraser Size", null, I.Drax, "erase size", wide: true);
		b.Row("Wall Brush", "Wall Brush", "Draw with the wall in your hand.", I.WoodWall, "brush walls", wide: true);
		b.Row("Wall Brush Size", "Wall Brush Size", null, I.WoodWall, "brush size", wide: true);
		b.Row("Liquid Brush", "Liquid Brush", "Draw with the liquid in your bucket.", I.WaterBucket, "brush water lava honey", wide: true);
		b.Row("Liquid Brush Size", "Liquid Brush Size", null, I.WaterBucket, "brush size", wide: true);

		b.Group("Paint", HubIcon.Item(I.PaintSprayer), "Hold a paintbrush, roller or scraper");
		b.Row("Super Paint Brush", "Paint Brush", "Paint blocks or walls over a big area.", I.PaintRoller, "paint brush", wide: true);
		b.Row("Super Paint Brush Size", "Paint Brush Size", null, I.PaintRoller, "paint size", wide: true);
		b.Row("Special Paint Pattern", "Rainbow Patterns", "Paint with rainbows, rings, diamonds... based on the position.", I.RainbowBrick, "rainbow paint pattern", wide: true);
		b.Row("Special Paint Patterns 1-3 Use Deep Paint", "Deep Paint for Patterns 1-3", null, I.DeepRedPaint, "deep paint", wide: true);

		b.Group("Placing", HubIcon.Item(I.Actuator), "Place and shape blocks with fewer rules");
		b.Row("Place Anywhere", "Place Anywhere", "Place any block anywhere (the server may not show it).", I.StoneBlock, "place float");
		b.Row("Hammer Anything", "Hammer Anything", "Slope or half-block almost any block.", I.Pwnhammer, "slope hammer half");
		b.Row("Super Actuation Rod", "Super Actuation Rod", "Actuate blocks with no actuator (one is placed for you online).", I.ActuationRod, "actuator actuate");

		b.Page("Border & Network", HubIcon.Item(I.Wire));
		b.Group("World Border", HubIcon.Item(I.TrifoldMap), "The edge of the world");
		b.Row("World Border Size", "Border Size", "How wide the world's border is (-1 removes it).", I.TrifoldMap, "border edge", wide: true);
		b.Row("No Screen Clamp", "Camera Past the Border", "The screen can move past the world's edge.", I.Binoculars, "clamp camera edge");
		b.Row("Persist Thin Vanilla Border", "Keep a Thin Border", "Keeps a thin normal border whatever the size.", I.TrifoldMap, "border thin");

		b.Group("Network Speed", HubIcon.Item(I.Wire), "How fast big edits are sent to the server. Too fast can get you kicked.");
		b.Row("Packet Mode", "Sending Mode", "Bursts up to the rate with pauses, or a steady 100 per millisecond.", I.Wire, "packet mode network", wide: true);
		b.Row("Tile & Wall Packet Rate", "Block & Wall Speed", null, I.StoneBlock, "packet rate tiles walls", wide: true);
		b.Row("Erase Packet Rate", "Erase Speed", null, I.Drax, "packet rate erase", wide: true);
		b.Row("Liquid Packet Rate", "Liquid Speed", null, I.WaterBucket, "packet rate liquid", wide: true);
		b.Row("Paint Packet Rate", "Paint Speed", null, I.PaintRoller, "packet rate paint", wide: true);
		b.Row("Bypass FPS & Timeout Protections", "Ignore Safety Slowdowns", "Don't slow down when FPS is low or the server goes quiet.", I.Stopwatch, "fps timeout safety", wide: true);
	}

	private static void World(CatalogBuilder b)
	{
		b.Section("world", "World", "Information, tools, secret seeds and fake biomes", HubIcon.Item(I.WorldGlobe), new Color(120, 200, 255));
		b.Page("Overview", HubIcon.Item(I.WorldGlobe));
		b.Group("This World", HubIcon.Item(I.WorldGlobe), "Your copy of the world's details. Some can be changed for you only.");
		b.Row("World Name", "Name", null, I.Sign, "world name", wide: true, cheat: false);
		b.Row("World Size", "Size", null, I.WorldGlobe, "world size", wide: true, cheat: false);
		b.Row("World Difficulty", "Difficulty", "Click to change this client's copy.", HubIcon.Texture("Images/UI/WorldCreation/IconDifficultyMaster"), "difficulty expert master journey", wide: true, cheat: false);
		b.Row("World Evil", "World Evil", "Click to swap Corruption and Crimson (for you only).", I.ShadowOrb, "corruption crimson evil", wide: true, cheat: false);
		b.Row("World ID", "World ID", null, I.Sign, "id", wide: true, cheat: false);
		b.Row("World GUID", "World GUID", null, I.Sign, "guid", wide: true, cheat: false);
		b.Row("World Generator Version", "Generator Version", null, I.Sign, "version", wide: true, cheat: false);
		b.Row("Seed", "Seed", null, I.GrassSeeds, "seed", wide: true, cheat: false);
		b.Row("Hardmode", "Hardmode", "This client's copy of the Hardmode flag.", I.Pwnhammer, "hardmode", wide: true, cheat: false);
		b.Row("World Infection", "World Infection", null, I.PurpleSolution, "corruption percent infection", wide: true, cheat: false);

		b.Group("World Tools", HubIcon.Item(I.Toolbox), "Quick fixes and helpers");
		b.Row("Reveal Map", "Reveal Map", "Uncovers the whole map.", I.TrifoldMap, "map reveal explore", verb: "Reveal", wide: true);
		b.Row("Wipe Ground Items", "Clear Dropped Items", "Deletes every item on the ground.", I.TrashCan, "items ground wipe", verb: "Clear");
		b.Row("Auto-Wipe Ground Items", "Keep Ground Clear", "Deletes items on the ground all the time.", I.TrashCan, "items ground wipe auto");
		b.Row("Clean Gores", "Clear Gore", "Removes blood and body parts lying around.", I.Bottle, "gore clean", verb: "Clear");
		b.Row("Shimmer Town NPCs", "Shimmer Town NPCs", "Left click: shimmered look. Right click: normal look.", I.ShimmerBlock, "shimmer town npc", verb: "Shimmer");
		b.Row("Shimmer Hostile NPCs", "Shimmer Enemies", "Transforms every enemy that can be shimmered.", I.ShimmerBlock, "shimmer enemy", verb: "Shimmer");
		b.Row("Shimmer Critters", "Shimmer Critters", "Turns critters into Faelings and friends.", I.FairyCritterPink, "shimmer critter faeling", verb: "Shimmer");
		b.Row("Fix Fake_NewChest", "Fix Broken Chests", "Stops you freezing on broken (fake) chests.", I.Chest, "fake chest freeze");
		b.Row("View Protected Chests", "Peek in Protected Chests", "Opening a protected chest shows your copy of it, read-only.", I.GoldChest, "chest protected readonly", ".rochest");

		RevealSettings(b);

		b.Group("Time & Unlocks", HubIcon.Item(I.Sundial), "Use items without having them");
		b.Row("Use Enchanted Sundial", "Skip to Dawn", "Like the Enchanted Sundial.", I.Sundial, "time day sundial", verb: "Use", cheat: false);
		b.Row("Use Enchanted Moondial", "Skip to Dusk", "Like the Enchanted Moondial.", I.Moondial, "time night moondial", verb: "Use", cheat: false);
		b.Row("Use Advanced Combat Techniques", "Combat Techniques", "Stronger town NPCs.", I.Book, "combat book", verb: "Use", cheat: false);
		b.Row("Use Advanced Combat Techniques V2", "Combat Techniques Vol. 2", null, I.Book, "combat book", verb: "Use", cheat: false);
		b.Row("Use Peddler's Satchel", "Peddler's Satchel", "More items in the Traveling Merchant's shop.", I.PeddlersSatchel, "merchant satchel", verb: "Use", cheat: false);
		b.Row("Use Cat License", "Cat License", null, I.LicenseCat, "pet cat", verb: "Use", cheat: false);
		b.Row("Use Dog License", "Dog License", null, I.LicenseDog, "pet dog", verb: "Use", cheat: false);
		b.Row("Use Bunny License", "Bunny License", null, I.LicenseBunny, "pet bunny", verb: "Use", cheat: false);

		b.Group("World Events", HubIcon.Item(I.Pwnhammer), "Single player only");
		b.Row("Smash Shadow Orb", "Smash a Shadow Orb", null, I.ShadowOrb, "orb", verb: "Smash", cheat: false);
		b.Row("Smash Crimson Heart", "Smash a Crimson Heart", null, I.CrimtaneOre, "heart", verb: "Smash", cheat: false);
		b.Row("Spawn Meteor", "Drop a Meteor", null, I.Meteorite, "meteor", verb: "Drop", cheat: false);
		b.Row("Smash Demon Altar", "Smash a Demon Altar", null, I.Pwnhammer, "altar", verb: "Smash", cheat: false);
		b.Row("Generate Hardmode Biomes", "Start Hardmode Biomes", null, I.Pwnhammer, "hardmode", verb: "Start", cheat: false);
		b.Row("Spawn Plantera Bulb", "Grow a Plantera Bulb", null, I.PlanteraBossBag, "plantera bulb", verb: "Grow", cheat: false);

		b.Group("Transform World", HubIcon.Item(I.Clentaminator), "Single player only: the whole world at once");
		b.Row("Purify World", "Purify", null, I.GreenSolution, "purity", verb: "Go", cheat: false);
		b.Row("Corrupt World", "Corrupt", null, I.PurpleSolution, "corruption", verb: "Go", cheat: false);
		b.Row("Hallow World", "Hallow", null, I.BlueSolution, "hallow", verb: "Go", cheat: false);
		b.Row("Crimson World", "Crimson", null, I.RedSolution, "crimson", verb: "Go", cheat: false);
		b.Row("Jungle to Mushroom", "Mushroom", null, I.DarkBlueSolution, "mushroom", verb: "Go", cheat: false);
		b.Row("Desert World", "Desert", null, I.SandSolution, "desert", verb: "Go", cheat: false);
		b.Row("Snow World", "Snow", null, I.SnowSolution, "snow", verb: "Go", cheat: false);
		b.Row("Forest World", "Forest", null, I.DirtSolution, "forest", verb: "Go", cheat: false);

		b.Page("Find Tiles", HubIcon.Item(I.MetalDetector)).Custom = HubTileSearch.Draw;
		b.Find("Find Tiles", "Search the whole world for a tile or a liquid: where each place is, on the map, and TP there.", "search find tile block ore chest searchtile locate liquid water lava honey shimmer");

		b.Page("Secret Seeds", HubIcon.Item(I.GrassSeeds));
		b.Group("Secret Seeds", HubIcon.Item(I.GrassSeeds), "Your copy of the world's special seed flags");
		b.Row("Drunk", "Drunk World", null, I.Mug, "05162020 drunk", cheat: false);
		b.Row("For the Worthy", "For the Worthy", null, I.Skull, "ftw worthy", cheat: false);
		b.Row("Not the Bees", "Not the Bees", null, I.Hive, "bees", cheat: false);
		b.Row("Celebration", "Celebrationmk10", null, I.SliceOfCake, "anniversary 10th", cheat: false);
		b.Row("The Constant", "The Constant", null, I.DeerThing, "dont starve constant", cheat: false);
		b.Row("No Traps", "No Traps", null, I.DartTrap, "traps", cheat: false);
		b.Row("Remix (Don't Dig Up)", "Don't Dig Up", null, I.Hellstone, "remix dig up", cheat: false);
		b.Row("Zenith (GetFixedBoi)", "Get Fixed Boi", null, I.Zenith, "zenith getfixedboi", cheat: false);
		b.Row("Skyblock", "Skyblock", null, I.SunplateBlock, "skyblock", cheat: false);
		b.Row("Vampire", "Vampire", null, I.VampireMask, "vampire", cheat: false);
		b.Row("Infected", "Infected", null, I.VilePowder, "infected", cheat: false);
		b.Row("Team Based Spawns", "Team Spawns", null, I.Bed, "team spawn", cheat: false);
		b.Row("Dual Dungeons", "Dual Dungeons", null, I.BlueBrick, "dungeon", cheat: false);
		b.Row("More Lightning", "More Lightning", null, I.RainCloud, "storm lightning", cheat: false);
		b.Row("No Lightning", "No Lightning", null, I.Umbrella, "storm lightning", cheat: false);

		b.Page("Fake Biomes", HubIcon.Item(I.MushroomGrassSeeds));
		b.Group("Fake Biomes", HubIcon.Item(I.MushroomGrassSeeds), "Make the game think you are somewhere else (music, backgrounds, spawns). Most do nothing on servers.");
		b.Row("Enforce Only Overrides", "Only the Biomes Below", "Ignore where you really are and use only the ones turned on.", I.WorldGlobe, "override", wide: true);
		b.Row("Corruption", "Corruption", null, I.EbonstoneBlock, "biome");
		b.Row("Crimson", "Crimson", null, I.CrimstoneBlock, "biome");
		b.Row("Hallow", "Hallow", null, I.PearlstoneBlock, "biome");
		b.Row("Jungle", "Jungle", null, I.MudBlock, "biome");
		b.Row("Glowing Mushroom", "Glowing Mushroom", null, I.MushroomGrassSeeds, "biome");
		b.Row("Snow", "Snow", null, I.IceBlock, "biome");
		b.Row("Desert", "Desert", null, I.SandBlock, "biome");
		b.Row("Dungeon", "Dungeon", null, I.BlueBrick, "biome");
		b.Row("Jungle Temple", "Jungle Temple", null, I.LihzahrdBrick, "biome lihzahrd");
		b.Row("Underground Desert", "Underground Desert", null, I.HardenedSand, "biome");
		b.Row("Hive", "Bee Hive", null, I.Hive, "biome");
		b.Row("Aether", "Aether (Shimmer)", null, I.ShimmerBlock, "biome shimmer");
		b.Row("Gem Cave", "Gem Cave", null, I.Amethyst, "biome");
		b.Row("Marble", "Marble Cave", null, I.MarbleBlock, "biome");
		b.Row("Granite", "Granite Cave", null, I.GraniteBlock, "biome");
		b.Row("Meteor", "Meteorite", null, I.Meteorite, "biome");
		b.Row("Graveyard", "Graveyard", null, I.Gravestone, "biome");
		b.Row("Rain", "Rain", null, I.RainCloud, "weather");
		b.Row("Sandstorm", "Sandstorm", null, I.SandstorminaBottle, "weather");
		b.Row("Overworld", "Surface", null, I.DirtBlock, "layer");
		b.Row("Underground", "Underground", null, I.StoneBlock, "layer");
		b.Row("Caverns", "Caverns", null, I.StoneBlock, "layer");
		b.Row("Underworld", "Underworld", null, I.AshBlock, "layer hell");
		b.Row("Space", "Space", null, I.SunplateBlock, "layer");
		b.Row("Beach", "Ocean", null, I.Coral, "biome beach");
		b.Row("Solar Tower", "Solar Pillar", null, I.FragmentSolar, "pillar");
		b.Row("Vortex Tower", "Vortex Pillar", null, I.FragmentVortex, "pillar");
		b.Row("Nebula Tower", "Nebula Pillar", null, I.FragmentNebula, "pillar");
		b.Row("Stardust Tower", "Stardust Pillar", null, I.FragmentStardust, "pillar");
		b.Row("Old One's Army", "Old One's Army", null, I.DD2ElderCrystal, "event");
		b.Row("Water Candle", "Water Candle", null, I.WaterCandle, "spawn");
		b.Row("Peace Candle", "Peace Candle", null, I.PeaceCandle, "spawn");
		b.Row("Shadow Candle", "Shadow Candle", null, I.ShadowCandle, "spawn");

		b.Page("World Data", HubIcon.Item(I.CellPhone));
		b.Group("Places & Layers", HubIcon.Item(I.TrifoldMap), "Spawn and Dungeon: click to teleport there");
		Data(b, I.TrifoldMap, "Spawn Position", "Dungeon Position", "World Surface", "Rock Layer");
		b.Group("Sky & Weather", HubIcon.Item(I.RainCloud));
		Data(b, I.RainCloud, "Moon Phase", "Moon Type", "Clouds", "Raining", "Max Raining", "Sandstorm Ongoing", "Sandstorm Severity", "Slime Rain", "Blood Moon", "Eclipse", "Pumpkin Moon", "Frost Moon",
			"Natural Lantern Night", "Manually Started Lantern Night", "Force Christmas for Today", "Force Halloween for Today", "Ongoing Natural Party", "Ongoing Manually Started Party", "Ongoing Old One's Army", "Invasion Type");
		b.Group("Backgrounds", HubIcon.Item(I.PaintingTheSeason));
		Data(b, I.PaintingTheSeason, "Ice Background Style", "Jungle Background Style", "Hell Background Style");
		b.Group("Progress", HubIcon.Item(I.Book));
		Data(b, I.Book, "Killed a Clown", "Killed any Mechanical Boss", "Killed a Mourning Wood", "Killed a Pumpking", "Killed an Everscream", "Killed a Santa-NK1", "Killed an Ice Queen",
			"Defeated Old One's Army T1", "Defeated Old One's Army T2", "Defeated Old One's Army T3", "Defeated Solar Pillar", "Defeated Vortex Pillar", "Defeated Nebula Pillar", "Defeated Stardust Pillar",
			"Used Advanced Combat Techniques", "Used Advanced Combat Techniques V2", "Used Peddler's Satchel", "Smashed Orb", "Smashed Orb Count", "Smashed Altar Count", "Generating Hardmode",
			"Enchanted Sundial Cooldown", "Enchanted Moondial Cooldown");
		b.Group("Ores", HubIcon.Item(I.GoldOre));
		Data(b, I.GoldOre, "Copper Ore", "Iron Ore", "Silver Ore", "Gold Ore", "Cobalt Ore", "Mythril Ore", "Adamantite Ore");
		b.Group("Town", HubIcon.Item(I.LicenseCat));
		Data(b, I.LicenseCat, "Purchased a Town Cat", "Purchased a Town Dog", "Purchased a Town Bunny", "Free Cake from Party Girl", "Rescued Nerdy Slime", "Unlocked Cool Slime", "Rescued Elder Slime",
			"Rescued Clumsy Slime", "Unlocked Diva Slime", "Rescued Surly Slime", "Rescued Mystic Slime", "Unlocked Squire Slime", "Unlocked Truffle", "Saved Goblin Tinkerer", "Saved Wizard",
			"Saved Mechanic", "Saved Stylist", "Saved Angler", "Saved Tax Collector", "Saved Tavernkeep", "Saved Golfer");
	}

	/// <summary>World data rows keep their names; they are facts, not cheats.</summary>
	private static void Data(CatalogBuilder b, int icon, params string[] rows)
	{
		foreach (string row in rows)
		{
			b.Row(row, null, null, icon, "world data", cheat: false);
		}
	}

	private static void Bosses(CatalogBuilder b)
	{
		b.Section("bosses", "Bosses & Events", "Summon bosses and invasions, or mark them as beaten", HubIcon.Item(I.SuspiciousLookingEye), new Color(255, 90, 110));
		b.Page("Bosses", HubIcon.Item(I.SuspiciousLookingEye));
		HubGroup pre = b.Group("Pre-Hardmode", HubIcon.Item(I.SlimeCrown), "The switch is whether it counts as defeated. Summon spawns it next to you.");
		pre.HeaderActions.Add(SummonAll("pre", "Summon every Pre-Hardmode boss", false));
		b.Pair("Defeated King Slime", "Spawn King Slime", "King Slime", null, I.SlimeCrown, "Summon", "boss slime");
		b.Pair("Defeated Eye of Cthulhu", "Spawn Eye of Cthulhu", "Eye of Cthulhu", null, I.SuspiciousLookingEye, "Summon", "boss eye");
		b.Pair("Defeated Boss 2", "Spawn Boss 2", "Eater of Worlds / Brain of Cthulhu", "The world evil's boss.", I.WormFood, "Summon", "boss eater brain evil");
		b.Row("Spawn Alternate Evil Boss", "Other World Evil Boss", "Summons the boss of the other world evil.", I.BloodySpine, "boss eater brain", verb: "Summon", cheat: false);
		b.Pair("Defeated Skeletron", "Spawn Skeletron", "Skeletron", null, I.ClothierVoodooDoll, "Summon", "boss dungeon");
		b.Pair("Defeated Queen Bee", "Spawn Queen Bee", "Queen Bee", null, I.Abeemination, "Summon", "boss bee");
		b.Pair("Defeated Deerclops", "Spawn Deerclops", "Deerclops", null, I.DeerThing, "Summon", "boss deer");
		b.Row("Spawn Wall of Flesh", "Wall of Flesh", "Summons the Wall of Flesh (single player).", I.GuideVoodooDoll, "boss wof", verb: "Summon", cheat: false);

		HubGroup hard = b.Group("Hardmode", HubIcon.Item(I.MechanicalSkull));
		hard.HeaderActions.Add(SummonAll("hard", "Summon every Hardmode boss", true));
		b.Pair("Defeated Queen Slime", "Spawn Queen Slime", "Queen Slime", null, I.QueenSlimeCrystal, "Summon", "boss slime");
		b.Pair("Defeated The Destroyer", "Spawn The Destroyer", "The Destroyer", null, I.MechanicalWorm, "Summon", "boss mech worm");
		b.Pair("Defeated The Twins", "Spawn The Twins", "The Twins", null, I.MechanicalEye, "Summon", "boss mech eye");
		b.Pair("Defeated Skeletron Prime", "Spawn Skeletron Prime", "Skeletron Prime", null, I.MechanicalSkull, "Summon", "boss mech skull");
		b.Row("Spawn Mechdusa", "Mechdusa", "All three mechanical bosses at once.", I.MechdusaSummon, "boss mech", verb: "Summon", cheat: false);
		b.Pair("Defeated Plantera", "Spawn Plantera", "Plantera", null, I.PlanteraBossBag, "Summon", "boss jungle");
		b.Pair("Defeated Golem", "Spawn Golem", "Golem", "Needs a Lihzahrd Altar within 20 blocks.", I.LihzahrdPowerCell, "Summon", "boss temple");
		b.Pair("Defeated Duke Fishron", "Spawn Duke Fishron", "Duke Fishron", null, I.TruffleWorm, "Summon", "boss fish");
		b.Pair("Defeated Empress of Light", "Spawn Empress of Light", "Empress of Light", null, I.EmpressButterfly, "Summon", "boss empress");
		b.Pair("Defeated Lunatic Cultist", "Spawn Lunatic Cultist", "Lunatic Cultist", null, I.CultistBossBag, "Summon", "boss cultist");
		b.Pair("Defeated Moon Lord", "Spawn Moon Lord", "Moon Lord", null, I.CelestialSigil, "Summon", "boss moon");

		b.Page("Invasions & Events", HubIcon.Item(I.GoblinBattleStandard));
		b.Group("Invasions", HubIcon.Item(I.GoblinBattleStandard), "The switch is whether it counts as defeated");
		b.Pair("Defeated Goblin Army", "Summon Goblin Army", "Goblin Army", null, I.GoblinBattleStandard, "Start", "invasion goblin");
		b.Pair("Defeated Frost Legion", "Summon Frost Legion", "Frost Legion", null, I.SnowGlobe, "Start", "invasion frost");
		b.Pair("Defeated Pirate Invasion", "Summon Pirate Invasion", "Pirate Invasion", null, I.PirateMap, "Start", "invasion pirate");
		b.Pair("Defeated Martian Madness", "Summon Martian Madness", "Martian Madness", null, I.MartianConduitPlating, "Start", "invasion martian");
		b.Row("Summon Old One's Army", "Old One's Army", null, I.DD2ElderCrystal, "invasion dd2", verb: "Start", cheat: false);

		b.Group("Events", HubIcon.Item(I.BloodMoonStarter), "Start an event right now");
		b.Row("Start Blood Moon", "Blood Moon", null, I.BloodMoonStarter, "event blood", verb: "Start", cheat: false);
		b.Row("Start Eclipse", "Solar Eclipse", null, I.SolarTablet, "event eclipse", verb: "Start", cheat: false);
		b.Row("Start Pumpkin Moon", "Pumpkin Moon", null, I.PumpkinMoonMedallion, "event pumpkin halloween", verb: "Start", cheat: false);
		b.Row("Start Frost Moon", "Frost Moon", null, I.NaughtyPresent, "event frost christmas", verb: "Start", cheat: false);
		b.Row("Trigger Lunar Apocalypse", "Lunar Events", "Starts the celestial pillars (single player).", I.FragmentSolar, "event pillars lunar", verb: "Start", cheat: false);
	}

	/// <summary>The classic panel's Shift + click on the boss box title, as a button that asks for a second click.</summary>
	/// <summary>How fast Reveal Map (.revmap) asks the server for the map.</summary>
	private static void RevealSettings(CatalogBuilder b)
	{
		b.Group("Map Reveal", HubIcon.Item(I.TrifoldMap), "How fast Reveal Map asks the server for the world");
		Slider(b, "reveal-window", "Sections at Once", "How many sections are asked for together (RequestSection). More is faster; lower it if a server kicks you for it.", I.TrifoldMap, 1, 1024, () => MapReveal.RevealWindow, v => MapReveal.RevealWindow = v, v => v.ToString(), squared: true);
		Slider(b, "reveal-pertick", "Requests per Tick", "New requests sent each tick (60 a second).", I.Stopwatch, 1, 512, () => MapReveal.RevealPerTick, v => MapReveal.RevealPerTick = v, v => v + " (" + v * 60 + "/s)", squared: true);
		Slider(b, "reveal-drones", "Legacy Drones", "For servers that ignore RequestSection: how many drones ask at once (each makes the server send what's around it).", I.JimsDrone, 1, 16, () => MapReveal.RevealDrones, v => MapReveal.RevealDrones = v, v => v.ToString(), squared: false);
		Slider(b, "reveal-timeout", "Ask Again After", "Seconds before a section that didn't arrive is asked for again.", I.Stopwatch, 1, 60, () => MapReveal.RevealTimeoutSeconds, v => MapReveal.RevealTimeoutSeconds = v, v => v + " s", squared: true);
		Slider(b, "reveal-retries", "Retries", "How many more times a missing section is asked for.", I.Stopwatch, 0, 20, () => MapReveal.RevealRetries, v => MapReveal.RevealRetries = v, v => v.ToString(), squared: false);
		Toggle(b, "reveal-near", "Near Me First", "The map fills from where you are, outwards. Off: column by column from the left.", I.Compass, () => MapReveal.RevealNearFirst, v => MapReveal.RevealNearFirst = v);
		Toggle(b, "reveal-fallback", "Switch to Legacy by Itself", "When the server doesn't answer RequestSection, use the drone method on its own.", I.JimsDrone, () => MapReveal.RevealAutoFallback, v => MapReveal.RevealAutoFallback = v);
		Toggle(b, "reveal-keepsmall", "Keep Far Chests Small", "Chests and mannequins, racks, frames far from you are kept small while revealing, so worlds full of them fit in memory. They come back as you get near and go whole into World Download. Off: as before.", I.Chest, () => MapReveal.RevealKeepFarSmall, v => MapReveal.RevealKeepFarSmall = v);
		Toggle(b, "reveal-trim", "Free Empty Tiles Always", "Empty tiles far from you are let go with every method (before: only the legacy one).", I.EmptyBucket, () => MapReveal.RevealTrimAllMethods, v => MapReveal.RevealTrimAllMethods = v);
		Toggle(b, "reveal-guard", "Memory Guard", "Waits while sections are still being processed or memory is nearly full, and stops the reveal before the game runs out of memory. Off: as before.", I.Shackle, () => MapReveal.RevealMemoryGuard, v => MapReveal.RevealMemoryGuard = v);
	}

	internal static HubFeature Slider(CatalogBuilder b, string id, string title, string description, int icon, int min, int max, System.Func<int> get, System.Action<int> set, System.Func<int, string> show, bool squared)
	{
		HubFeature feature = b.Native(id, title, description, HubIcon.Item(icon), FeatureKind.Slider);
		// Squared: the low end, where the usual values are, gets more of the bar.
		feature.GetSlider = delegate
		{
			float f = (float)(get() - min) / (max - min);
			return squared ? (float)System.Math.Sqrt(MathHelper.Clamp(f, 0f, 1f)) : MathHelper.Clamp(f, 0f, 1f);
		};
		feature.SetSlider = delegate(float f)
		{
			set(min + (int)System.Math.Round((squared ? f * f : f) * (max - min)));
			IsaClient.Core.ClientSettings.Save();
		};
		feature.GetValue = () => show(get());
		feature.Cheat = false;
		feature.Keywords = "revmap reveal map speed";
		return feature;
	}

	private static void Toggle(CatalogBuilder b, string id, string title, string description, int icon, System.Func<bool> get, System.Action<bool> set)
	{
		HubFeature feature = b.Native(id, title, description, HubIcon.Item(icon), FeatureKind.Toggle);
		feature.GetOn = get;
		feature.SetOn = delegate(bool v)
		{
			set(v);
			IsaClient.Core.ClientSettings.Save();
		};
		feature.Cheat = false;
		feature.Keywords = "revmap reveal map";
	}

	private static HubFeature SummonAll(string id, string title, bool hardmode)
	{
		return new HubFeature
		{
			Id = "hub:summonall-" + id,
			Title = title,
			Description = "Click twice: every boss of this list at once.",
			Icon = HubIcon.Item(hardmode ? I.MechanicalSkull : I.SlimeCrown),
			NativeKind = FeatureKind.Action,
			Verb = "Summon all",
			Confirm = true,
			Cheat = false,
			Run = delegate
			{
				WorldInfo.SpawnAllBosses(hardmode);
			}
		};
	}
}
