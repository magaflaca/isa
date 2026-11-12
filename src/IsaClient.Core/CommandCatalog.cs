using IsaClient.Assistant;
using IsaClient.Building;
using IsaClient.Community;
using IsaClient.Gameplay;
using IsaClient.Menu;
using IsaClient.Movement;
using IsaClient.MultiClient;
using IsaClient.Music;
using IsaClient.Networking;
using IsaClient.Rendering;
using IsaClient.WorldTools;

namespace IsaClient.Core;

/// <summary>
/// Every chat command of the client, in lookup order (see <see cref="CommandRegistry"/>), each with its .help entry:
/// section, arguments and what it does (<see cref="CommandHelp"/> builds .help from them). The handlers live in the module
/// each command works on.
/// </summary>
public static class CommandCatalog
{
	public static void RegisterAll()
	{
		IsaClient.Net.Pipe.PipeCommands.Register();
		CommandRegistry.Register(IsaClient.Net.Pipe.PipeCommands.PipeCommand, "pipe")
			.Help(CommandSection.General, "<player> <text> | all <text> | on | off | trace", "Sends a short message to IsaClient players through map pings (a demo of the pipe other features are built on).");
		CommandRegistry.Register(CommandHelp.HelpCommand, "help")
			.Help(CommandSection.General, "[number|section|.command]", "Lists the command sections, the commands of one section (a page at a time) or explains one command.");
		CommandRegistry.Register(ChatCommands.AbbreviationsCommand, "abbrev", "abbreviation", "alias")
			.Help(CommandSection.General, "<command> <abbreviation> | list | remove <abbreviation>", "Your own short names for commands: .abbrev wlddown wd makes .wd run .wlddown.");
		CommandRegistry.Register(ItemTools.BuffCommand, "buff", "b")
			.Help(CommandSection.Items, "<id|name> [seconds|-1]", "Gives you a buff for some seconds (-1 = permanent).");
		CommandRegistry.Register(ItemTools.ItemCommand, "item", "i")
			.Help(CommandSection.Items, "<name|id> [stack] [prefix] [player] [-c count]", "Spawns an item for you or another player. Put names with spaces in quotes.");
		CommandRegistry.Register(ItemTools.HealCommand, "heal")
			.Help(CommandSection.Items, "[player] [hearts]", "Heals you or another player by dropping hearts on them.");
		CommandRegistry.Register(ChatTools.SpamChatCommand, "spamchat")
			.Help(CommandSection.Chat, "<text>", "Sends the text again and again (.spamchat alone stops). The text can also be a client command.");
		CommandRegistry.Register(ItemTools.WearCommand, "wear")
			.Help(CommandSection.Player, "<player> [look|inv|gear|stats|all] | restore", "Wears another player's look, inventory, gear or stats; .wear restore gives you yours back.");
		CommandRegistry.Register(PixelArt.PixelPlayerCommand, "plrart", "pasteplr", "pixelplayer")
			.Help(CommandSection.Building, "<player|me>", "Builds a player's character as pixel art in the world.");
		CommandRegistry.Register(PixelArt.PixelArtCommand, "pixelart")
			.Help(CommandSection.Building, "size <1-4> | walk <on|off> | glow <on|off>", "Options for .plrart pixel art: block size, walk-through blocks and glowing blocks.");
		CommandRegistry.Register(GeneralCommands.MyCharacterCommand, "mychar", "mycharacter")
			.Help(CommandSection.Player, "", "Opens My Character: wear parts of your saved characters, restore yourself, paste one as pixel art.");
		CommandRegistry.Register(ChatTools.SpamDelayCommand, "spamdelay")
			.Help(CommandSection.Chat, "<ticks>", "Time between .spamchat messages (60 ticks = 1 second).");
		CommandRegistry.Register(RainbowText.RainbowCommand, "rainbow", "rb", "arcoiris")
			.Help(CommandSection.Render, "<text>", "Writes the text in the world with rainbow particles (up to 48 letters).");
		CommandRegistry.Register(PixelArt.ImageScaleCommand, "picscale", "drawscale", "imagescale")
			.Help(CommandSection.Building, "<percent>", "Size of the pictures drawn with .drawphoto.");
		CommandRegistry.Register(FreeCamera.FreeCamCommand, "freecam")
			.Help(CommandSection.Movement, "[speed]", "Free camera: move the view around without moving your character.");
		CommandRegistry.Register(PlayerTools.GhostModeCommand, "ghost", "ghostmode", "gh", "g")
			.Help(CommandSection.Player, "", "Ghost mode: become a ghost and float through everything; use it again to come back.");
		CommandRegistry.Register(AutoClicker.AutoClickerCommand, "autoclick", "autoclicker", "ac")
			.Help(CommandSection.Player, "[hold|click|cycle] [cps] [left|right|both] [hold=s] [release=s] [min=n]", "Auto clicker. Alone it turns on or off; the options set the mode, speed and buttons.");
		CommandRegistry.Register(PlayerTools.AnySlotCommand, "anyslot")
			.Help(CommandSection.Player, "", "Lets any item go in any inventory or equipment slot.");
		CommandRegistry.Register(HeldItemCommands.ProjectileSpeedCommand, "ut", "usetime", "at", "animationtime", "shtst", "shootstyle", "rngam", "rngamount", "crtl", "createtile", "crwl", "createwall", "plst", "placestyle", "shootperuse", "shootamount", "spu", "sha", "scale", "scl", "itemsize", "proj", "projectile", "shoot", "projspeed", "projectilespeed", "shootspeed", "damage", "dmg", "hlf", "heallife", "hlm", "healmana", "kb", "knockback");
		CommandRegistry.Register(ProtectedChests.ProtectedChestsCommand, "rochest", "readonlychests", "protectedchests")
			.Help(CommandSection.World, "", "Lets you look inside protected chests (read only).");
		CommandRegistry.Register(MuteAll.MuteCommand, "mute")
			.Help(CommandSection.General, "", "Turns all game sound off, or back to your volumes.");
		CommandRegistry.Register(Zoom.ZoomCommand, "zoom")
			.Help(CommandSection.Render, "<percent>", "Sets the zoom; with Extended Zoom Out it goes below the game's minimum.");
		CommandRegistry.Register(SscSync.KeepSscCommand, "ssc", "keepssc")
			.Help(CommandSection.Info, "[on|off|save|status|push|twoway]", "Keep SSC Character and Two-Way SSC Sync for server-side character servers.");
		CommandRegistry.Register(IsaClient.ChatImages.GifPicker.GifCommand, "gif", "gifs")
			.Help(CommandSection.Chat, "[search]", "Opens the GIF search (any GIF, or anime reactions); the GIF you pick goes with your next message. Also the GIF button on the chat bar.");
		IsaClient.Community.ChatActions.Register();
		CommandRegistry.Register(IsaClient.Community.PipeChat.WhisperCommand, "whisper", "w", "isamsg", "pm")
			.Help(CommandSection.General, "<player> <message>", "A private message to another IsaClient player, straight to them (never through the server).");
		CommandRegistry.Register(IsaClient.Community.PipeChat.ReplyCommand, "reply", "r", "replay")
			.Help(CommandSection.General, "<message>", "Answers the last IsaClient player who whispered you (.w).");
		CommandRegistry.Register(IsaClient.Community.SecretChat.SecretCommand, "secret", "enc", "encrypt")
			.Help(CommandSection.Chat, "[message]", "Secret chat: the message goes through the server as made-up words; players with IsaClient read it as you wrote it. Alone it switches everything you type to secret (and back).");
		CommandRegistry.Register(IsaClient.Community.PipeChat.InChatCommand, "inchat", "isachat")
			.Help(CommandSection.General, "[message]", "The IsaClient chat, only between IsaClient players here: alone it switches what you type to it (and back); with a message it sends just that.");
		CommandRegistry.Register(IsaClient.Games.GameCommands.TicTacToeCommand, "ttt", "tictactoe", "tateti")
			.Help(CommandSection.General, "<player | all | bot | accept | decline | leave>", "Tic Tac Toe with IsaClient players or the computer (bot): invite a player or everyone, answer the last invitation, or leave your game. Also in the panel: Games.");
		CommandRegistry.Register(IsaClient.Identity.ClientIdentity.IdCommand, "isaid", "myid")
			.Help(CommandSection.General, "[new]", "Your IsaClient id (the same for all your characters; scores and reports use it). new makes another one.");
		CommandRegistry.Register(IsaClient.Games.GameCommands.TetrisCommand, "tetris")
			.Help(CommandSection.General, "", "Opens Tetris. Your best score goes to the board shared by every IsaClient (.scores).");
		CommandRegistry.Register(IsaClient.Games.GameCommands.SnakeCommand, "snake", "serpiente")
			.Help(CommandSection.General, "", "Opens Snake. Your best score goes to the board shared by every IsaClient (Games > Scores).");
		CommandRegistry.Register(_ => IsaClient.Casino.BetsWindow.OpenLobby(), "bets", "apuestas")
			.Help(CommandSection.General, "", "Opens the casino's Bets: the slot machine, the Roulette and the horse races, with the same chips.");
		CommandRegistry.Register(_ => IsaClient.Casino.BetsWindow.OpenGame(IsaClient.Casino.CasinoGame.Slots), "slots")
			.Help(CommandSection.General, "", "Opens the slot machine.");
		CommandRegistry.Register(_ => IsaClient.Casino.BetsWindow.OpenGame(IsaClient.Casino.CasinoGame.Roulette), "roulette")
			.Help(CommandSection.General, "", "Sits you at the Roulette: one table for everyone on the server.");
		CommandRegistry.Register(_ => IsaClient.Casino.BetsWindow.OpenGame(IsaClient.Casino.CasinoGame.Horses), "horses", "races")
			.Help(CommandSection.General, "", "Goes to the horse races: everyone on the server bets on the same race.");
		CommandRegistry.Register(_ => IsaClient.Casino.CasinoWindow.OpenCards(), "casino", "poker", "blackjack", "bj")
			.Help(CommandSection.General, "", "Opens the casino: Poker (Texas Hold'em) and Blackjack dealt by the Moon Lord, alone with the computer or at a table with other IsaClient players. Your chips come from your rank in the other games; what you win or lose goes to the Poker and Blackjack boards and the global one.");
		CommandRegistry.Register(_ => IsaClient.Psp.PspWindow.Open(), "psp")
			.Help(CommandSection.General, "", "Opens the PSP window: your own games (.iso, .cso, .chd, .pbp, .elf, .prx, or a .zip or .7z with them; the PSP emulator is downloaded the first time). Just for fun (no scores). Also in the panel: Games.");
		CommandRegistry.Register(_ => IsaClient.Gba.PokemonWindow.Open(), "pokemon", "gba", "firered", "nds")
			.Help(CommandSection.General, "", "Opens the Game Boy Advance / DS window: your own games (.gba, .nds or .zip; the DS emulator is downloaded the first time); in Pokémon FireRed, LeafGreen and Emerald you play as your Terraria character. Just for fun (no scores). Also in the panel: Games.");
		CommandRegistry.Register(IsaClient.Games.SnakeArt.Command, "snakeart", "snakeworld")
			.Help(CommandSection.General, "[width height | stop | clear]", "Snake with real blocks in the world, around the cursor: everyone here sees it. Arrow keys play; the walls are solid or wrap as in the Snake setting. Blocks go out at World Edit's Block & Wall Speed (Building > Network). stop ends it, clear removes its blocks.");
		CommandRegistry.Register(IsaClient.Games.TetrisArt.Command, "tetrisart", "tetrisworld")
			.Help(CommandSection.General, "[stop | clear]", "Tetris with real blocks in the world, around the cursor: everyone here sees it. Arrows move, rotate and drop; End (or Right Ctrl) drops at once, Right Shift holds. Blocks go out at World Edit's Block & Wall Speed. stop ends it, clear removes its blocks.");
		CommandRegistry.Register(IsaClient.Games.GameCommands.ScoresCommand, "scores", "ranking", "top")
			.Help(CommandSection.General, "", "The top Tetris scores of every IsaClient player you've met (also in the panel: Games > Scores).");
		CommandRegistry.Register(IsaClient.Games.GameCommands.ChessCommand, "chess", "ajedrez")
			.Help(CommandSection.General, "<player | all | bot | accept | decline | leave>", "Chess with IsaClient players or the computer (bot): invite a player or everyone, answer the last invitation, or leave your game. Also in the panel: Games.");
		CommandRegistry.Register(IsaClient.Languages.LanguageWindow.LanguageCommand, "language", "lang", "idioma")
			.Help(CommandSection.General, "[code | auto | export | import | folder]", "IsaClient's language: alone it opens the language window (also the flag in the main menu). A code (es, ru, pt-BR...) switches to it; auto follows the game's language; export writes every text to translate; import adds a language file.");
		CommandRegistry.Register(IsaClient.ChatImages.ChatAttachments.SendPicCommand, "sendpic", "pic", "sendimage")
			.Help(CommandSection.Chat, "[file] | paste | clear | folder", "Sends pictures in the chat: alone it opens the file picker (pick several; a video is sent as a GIF). They wait above the chat bar until you send a message (text is optional). Ctrl+V in the chat attaches a copied picture too, and the picture button at the end of the chat bar opens the picker. IsaClient players see them as pictures; clicking one shows it big. folder opens the pictures you saved.");
		CommandRegistry.Register(ChatTools.KeepChatCommand, "keepchat")
			.Help(CommandSection.Chat, "[on|off]", "Keeps your chat history when you change worlds or servers.");
		CommandRegistry.Register(IsaClient.Assistant.PluginMaker.PluginAgent.Command, "pluginai", "pai", "makeplugin")
			.Help(CommandSection.General, "[message | /command | open | new | stop]", "The Plugin Maker: an AI that makes and changes plugins for you (writes the C#, compiles it and turns it on). Alone it turns Plugin Maker mode on or off: what you write in the chat goes to it, like in its page (AI Assistant > Plugin Maker, .pai open). With a message it sends just that. /model, /effort, /goal, /resume... are its commands. Needs an NVIDIA key (AI Assistant > Connection).");
		CommandRegistry.Register(ChatTools.ClearChatCommand, "clearchat", "clear")
			.Help(CommandSection.Chat, "", "Empties the chat (only on your screen).");
		CommandRegistry.Register(ChatTools.ChatBoxCommand, "chatbox")
			.Help(CommandSection.Chat, "[on|off]", "Turns the enhanced chat box on or off.");
		CommandRegistry.Register(GeneralCommands.UndoPasteCommand, "undo", "undopaste")
			.Help(CommandSection.Building, "", "Undoes your last paste (also Ctrl+Z).");
		CommandRegistry.Register(Placement.PlacementBypassCommand, "plb")
			.Help(CommandSection.Building, "", "Placement bypass: place blocks where the game normally does not let you.");
		CommandRegistry.Register(PlayerCheats.FreeCraftCommand, "fc", "freecraft")
			.Help(CommandSection.Player, "", "Free crafting: craft without having the materials.");
		CommandRegistry.Register(PlayerCheats.CraftAnywhereCommand, "craftanywhere")
			.Help(CommandSection.Player, "", "Craft as if every crafting station were next to you.");
		CommandRegistry.Register(PlayerCheats.NoHorrorCommand, "nohorror")
			.Help(CommandSection.Player, "", "Ignores the effects of the Horrified debuff.");
		CommandRegistry.Register(PlayerCheats.InfiniteAmmoCommand, "infammo", "infiniteammo", "infam")
			.Help(CommandSection.Player, "", "Infinite ammo.");
		CommandRegistry.Register(PlayerCheats.IgnoreLiquidsCommand, "ignoreliquids", "igl")
			.Help(CommandSection.Player, "", "Move through water, lava and honey as if they were not there.");
		CommandRegistry.Register(PlayerCheats.IgnoreDamageBlocksCommand, "ignoredmgblocks", "igd")
			.Help(CommandSection.Player, "", "Blocks that hurt you (spikes, hot tiles...) do not hurt you.");
		CommandRegistry.Register(PlayerCheats.InfiniteManaCommand, "infinitemana", "infmana")
			.Help(CommandSection.Player, "", "Infinite mana.");
		CommandRegistry.Register(PlayerCheats.InstantRespawnCommand, "instarespawn", "instantrespawn")
			.Help(CommandSection.Player, "", "Respawn right away when you die.");
		CommandRegistry.Register(PlayerCheats.ExtendedPickupCommand, "extendedpickup")
			.Help(CommandSection.Player, "", "Picks up items from much farther away.");
		CommandRegistry.Register(PlayerCheats.ExtendedPlacementCommand, "extendedplacement")
			.Help(CommandSection.Player, "", "Places blocks and walls from much farther away.");
		CommandRegistry.Register(PlayerCheats.InfiniteMinionsCommand, "infminions", "infiniteminions")
			.Help(CommandSection.Player, "", "No limit on how many minions you can summon.");
		CommandRegistry.Register(PlayerCheats.AntiDebuffCommand, "antidebuff")
			.Help(CommandSection.Player, "", "Removes debuffs as soon as you get them.");
		CommandRegistry.Register(PlayerCheats.AntiDeathCommand, "antideath")
			.Help(CommandSection.Player, "", "Keeps you from dying.");
		CommandRegistry.Register(PlayerCheats.DodgeAllCommand, "dodge", "dodgeall")
			.Help(CommandSection.Player, "", "Dodges all damage.");
		CommandRegistry.Register(PlayerCheats.InstantRegenCommand, "instantregen", "instaregen")
			.Help(CommandSection.Player, "", "Instant life regeneration.");
		CommandRegistry.RegisterParser(c => AiAssistant.HandleCommand(c.Name, c.Text, c.Parts));
		CommandRegistry.RegisterParser(c => MultiClientManager.HandleCommand(c.Name, c.Text, c.Parts));
		CommandRegistry.RegisterParser(c => FullbrightLighting.HandleCommand(c.Name, c.Parts));
		CommandRegistry.RegisterParser(c => Proxy.HandleCommand(c.Name, c.Text, c.Parts));
		CommandRegistry.Register(PlayerListPanel.PlayerListCommand, "playerlist", "players", "plist")
			.Help(CommandSection.Info, "", "Opens the Player List (I and Z badges show IsaClient and zMod users).");
		CommandRegistry.Register(PlayerListPanel.ClientsCommand, "clients", "zmod")
			.Help(CommandSection.Info, "", "Lists the players using IsaClient or zMod.");
		CommandRegistry.Register(PlayerListPanel.InformOthersCommand, "shareinfo", "informother", "informothers")
			.Help(CommandSection.Info, "", "Lets other IsaClient users see that you use IsaClient (the I in their Player List).");
		CommandRegistry.Register(ServerDetection.NotifyCommand, "notify")
			.Help(CommandSection.Info, "", "Tells you when the server changes your player (life, mana, team, position...).");
		CommandRegistry.Register(ServerDetection.DetectSpyingCommand, "detsp")
			.Help(CommandSection.Info, "", "Detects players spying on you.");
		CommandRegistry.Register(NetDebug.NetplayCommand, "netplay", "nettext")
			.Help(CommandSection.Info, "", "Shows network status text on screen.");
		CommandRegistry.Register(PlayerListPanel.PlayerVitalsCommand, "vitals", "playervitals")
			.Help(CommandSection.Info, "", "Shows other players' life and mana.");
		CommandRegistry.Register(InfoOverlays.ShowItemOwnerCommand, "shio", "showitemowner")
			.Help(CommandSection.Info, "", "Shows who an item on the ground is reserved for when you hover it.");
		CommandRegistry.Register(InfoOverlays.ShowIdsCommand, "ids", "showids")
			.Help(CommandSection.Info, "", "Shows item, NPC and tile IDs in tooltips.");
		CommandRegistry.Register(InfoOverlays.ExtraItemInfoCommand, "iteminfo", "extraiteminfo")
			.Help(CommandSection.Info, "", "Extra item information in tooltips.");
		CommandRegistry.Register(InfoOverlays.SeeNpcDataCommand, "seenpcdata", "nd")
			.Help(CommandSection.Info, "", "Extra information about the NPC under the cursor.");
		CommandRegistry.Register(WorldCommands.AutoWipeCommand, "autowipe", "aw")
			.Help(CommandSection.World, "", "Deletes items on the ground automatically.");
		CommandRegistry.Register(WorldCommands.DeleteGroundItemsCommand, "wipe", "delgrounditems", "wipeitems", "wi")
			.Help(CommandSection.World, "", "Deletes every item on the ground.");
		CommandRegistry.Register(AntiLag.CleanGoresCommand, "cleangores", "cgores", "cleangore", "cgore")
			.Help(CommandSection.Render, "", "Removes all gore (body parts and debris).");
		CommandRegistry.Register(PlayerListPanel.PlayerTrackerCommand, "track", "trackplayers", "plrtracker", "playertracker")
			.Help(CommandSection.Info, "", "Player tracker: arrows that point to other players.");
		CommandRegistry.Register(Combat.KillProjectilesCommand, "killprojectiles", "killproj", "kpj")
			.Help(CommandSection.Combat, "", "Removes all your projectiles.");
		CommandRegistry.Register(VanishDetection.FindVanishCommand, "findvanish", "detvanish")
			.Help(CommandSection.Info, "", "Finds vanished (hidden) players.");
		CommandRegistry.Register(PlayerCheats.FreeShopsCommand, "freeshops", "freeshop")
			.Help(CommandSection.Player, "", "Buy from NPC shops for free.");
		CommandRegistry.Register(Combat.KillBubbleCommand, "killaura", "killbubble")
			.Help(CommandSection.Combat, "[size]", "Kills hostile NPCs within <size> blocks of you (any size, 0 turns it off). Without a size it turns on or off.");
		CommandRegistry.Register(Combat.ButcherCommand, "butcher", "killall")
			.Help(CommandSection.Combat, "[damage]", "Kills every hostile NPC.");
		CommandRegistry.Register(Combat.ButcherFriendlyCommand, "butcherfriendly", "killtownnpcs")
			.Help(CommandSection.Combat, "", "Kills the town NPCs.");
		CommandRegistry.Register(Combat.AutoButcherCommand, "autobutcher", "ab", "autokill")
			.Help(CommandSection.Combat, "[damage]", "Keeps killing hostile NPCs automatically.");
		CommandRegistry.Register(GeneralCommands.LockOnPlayersCommand, "lockonplayers", "targetplayers")
			.Help(CommandSection.Combat, "", "Lets lock-on target hostile players.");
		CommandRegistry.Register(IsaClient.Items.ItemShare.ShareCommand, "shareitem", "linkitem")
			.Help(CommandSection.Combat, "", "Links the item in your hand in chat: IsaClient players see it as it is and can keep it.");
		CommandRegistry.Register(IsaClient.Gameplay.SmartAim.SmartAimCommand, "smartaim", "predictaim")
			.Help(CommandSection.Combat, "", "Lock-on leads moving targets (predicts their path and your shot).");
		CommandRegistry.Register(IsaClient.Gameplay.SmartAim.AutoAimCommand, "autoaim", "aimbot")
			.Help(CommandSection.Combat, "", "Weapons lock on the target nearest the cursor, no key needed.");
		CommandRegistry.Register(Combat.ShimmerTownCommand, "shimmertown")
			.Help(CommandSection.Combat, "[normal]", "Turns town NPCs into their shimmered look (normal turns them back).");
		CommandRegistry.Register(Combat.ShimmerEnemiesCommand, "shimmerenemies")
			.Help(CommandSection.Combat, "", "Shimmers the enemies.");
		CommandRegistry.Register(Combat.ShimmerCrittersCommand, "shimmercritters")
			.Help(CommandSection.Combat, "", "Shimmers the critters.");
		CommandRegistry.Register(FreeCamera.FreeCamAltCommand, "freecam2")
			.Help(CommandSection.Movement, "", "Free camera that does not load the chunks you look at.");
		CommandRegistry.Register(FreeCamera.FreeCamDebugCommand, "freecamd")
			.Help(CommandSection.Movement, "", "Free camera debug information.");
		CommandRegistry.Register(Chests.UnlockedChestsCommand, "openlocked", "unlockedchests")
			.Help(CommandSection.World, "", "Open locked chests without the key.");
		CommandRegistry.Register(WorldCommands.ActuatorRodCommand, "superrod", "actuatorrod")
			.Help(CommandSection.World, "", "Super Actuation Rod.");
		CommandRegistry.Register(WorldCommands.AnythingOnPlatesCommand, "plates", "anythingonplates")
			.Help(CommandSection.World, "", "Anything fits on plates and weapon racks.");
		CommandRegistry.Register(AntiLag.AntiLagCommand, "antilag")
			.Help(CommandSection.Render, "", "Anti-lag: clears projectiles, dust and gore when the frame rate drops.");
		CommandRegistry.Register(ChatTools.AntiChatSpamCommand, "antispam", "antichatspam")
			.Help(CommandSection.Chat, "", "Hides chat spam from other players.");
		CommandRegistry.Register(WorldView.InvisibleEnemiesCommand, "invisenemies", "invisibleenemies")
			.Help(CommandSection.Render, "", "Makes enemies invisible.");
		CommandRegistry.Register(AntiLag.ParticleLimiterCommand, "particlelimit", "particlelimiter")
			.Help(CommandSection.Render, "[max]", "Limits particles to that many (no number turns it off).");
		CommandRegistry.Register(Combat.CrystalKillAuraCommand, "crystalkillaura", "crystalaura", "cka", "ca")
			.Help(CommandSection.Combat, "", "Kills enemies near the Eternia Crystal automatically (Old One's Army).");
		CommandRegistry.Register(Combat.ShimmerAllCommand, "shimmerall", "shma")
			.Help(CommandSection.Combat, "<town|townalt|enemy>", "Shimmers every NPC of that kind.");
		CommandRegistry.Register(GeneralCommands.ChangeMusicCommand, "changemusic", "swapmusic", "sm")
			.Help(CommandSection.Render, "", "Swaps between the normal and the Otherworldly soundtrack.");
		CommandRegistry.Register(GeneralCommands.RotateWorldCommand, "rotw", "rotateworld")
			.Help(CommandSection.World, "", "Rotates the world (single player).");
		CommandRegistry.Register(Combat.ShimmerGodmodeCommand, "shgod", "shimmergodmode")
			.Help(CommandSection.Player, "", "Shimmer godmode: keeps the Shimmering buff on you.");
		CommandRegistry.Register(Thor.ThorCommand, "thor")
			.Help(CommandSection.Render, "[bolts per second]", "Colored lightning strikes all around you (everyone sees it); .thor again stops it.");
		CommandRegistry.Register(Cosmetics.ShimmerParticlesCommand, "shp", "shimmerparticles")
			.Help(CommandSection.Render, "", "Shimmer particles when you walk.");
		CommandRegistry.Register(Cosmetics.RgbOutlineCommand, "rgboutline", "rgbout")
			.Help(CommandSection.Render, "", "Rainbow outline around your character.");
		CommandRegistry.Register(Cosmetics.RgbNpcIconCommand, "rgbnpcicon", "rgbnpc")
			.Help(CommandSection.Render, "", "Rainbow NPC map icons.");
		CommandRegistry.Register(Cosmetics.RgbBossIconCommand, "rgbbossicon", "rgbboss")
			.Help(CommandSection.Render, "", "Rainbow boss map icons.");
		CommandRegistry.Register(TeleportTools.SpawnCommand, "spawn")
			.Help(CommandSection.Movement, "", "Teleports you to the world spawn.");
		CommandRegistry.Register(WorldInfo.DungeonCommand, "dungeon")
			.Help(CommandSection.Movement, "", "Teleports you to the dungeon.");
		CommandRegistry.Register(GeneralCommands.ReuseDelayCommand, "reud", "reusedelay")
			.Help(CommandSection.Items, "<ticks>", "Reuse delay of the held item.");
		CommandRegistry.Register(Hitboxes.ExtraProjectileHitboxesCommand, "extraproj")
			.Help(CommandSection.Render, "", "Extra projectile hitboxes (turn on Projectile Hitboxes in Render).");
		CommandRegistry.Register(WorldView.WireKitesCommand, "wirekites")
			.Help(CommandSection.Render, "", "Draws other players' wire kite projectiles.");
		CommandRegistry.Register(Travel.WindsCommand, "winds")
			.Help(CommandSection.Items, "", "Unlimited Weather Pain projectiles.");
		CommandRegistry.Register(GeneralCommands.AutoReuseCommand, "ar", "autoreuse")
			.Help(CommandSection.Items, "", "Auto reuse for the held item.");
		CommandRegistry.Register(Rejoin.AutoRejoinCommand, "arc", "autorejoin")
			.Help(CommandSection.Info, "", "Reconnects by itself when you are kicked or lose the connection.");
		CommandRegistry.Register(GeneralCommands.ShootsEveryUseCommand, "seu", "shootseveryuse")
			.Help(CommandSection.Items, "", "The held item shoots on every use.");
		CommandRegistry.Register(Travel.InfiniteReachCommand, "infinitereach", "infreach")
			.Help(CommandSection.Movement, "", "Infinite reach for mining and placing.");
		CommandRegistry.Register(SelfCommands.UnlockAchievementsCommand, "unlockachievements")
			.Help(CommandSection.Player, "", "Unlocks the achievements that only depend on luck or are hard to trigger.");
		CommandRegistry.Register(Combat.AutoKillProjectilesCommand, "autokillprojectiles", "autokillproj", "akpj")
			.Help(CommandSection.Combat, "", "Keeps removing all projectiles automatically.");
		CommandRegistry.Register(ItemTools.FishItemCommand, "fitem", "fishitem", "fishi")
			.Help(CommandSection.Items, "[item id]", "Fishes up that item every time you reel in (no id stops).");
		CommandRegistry.Register(LucyChorus.LucyCommand, "lucy")
			.Help(CommandSection.Render, "[player|me|mouse|all] [per second] | stop | say <text> | add <text> | lines | remove <n> | clear", "Lucy the Axe talks over someone: once, or that many lines a second until .lucy stop. Your own lines reach IsaClient players.");
		CommandRegistry.Register(IsaClient.WorldTools.TileSearch.Command, "searchtile", "findtile", "buscartile")
			.Help(CommandSection.World, "<id | name> | clear", "Searches the whole world for a tile or a liquid (by id or name: lava, honey...): how many and where, nearest first. The next time you open the full map they're painted and ringed; closing it takes the marks away. More in World > Find Tiles (TP, map).");
		CommandRegistry.Register(IsaClient.Movement.NpcBrain.Command, "npcai", "npcbrain", "beanpc")
			.Help(CommandSection.Movement, "<npc name | id> [player | mouse | enemy] | off", "You move as that NPC does, by its own AI (it chases the closest player, the one named, the cursor or the closest enemy). Only you see its look on you. More in Experimental > NPC Brain.");
		CommandRegistry.Register(IsaClient.Movement.PixieMode.Command, "pixie", "duende")
			.Help(CommandSection.Movement, "[player | attack | off]", "You move exactly as a Pixie does (its own AI), on the Pixie mount, with a kill aura of 1 block: after the closest player, or the one named (alone: the cursor); attack goes after the closest enemy instead. Again stops.");
		CommandRegistry.Register(TeleportTools.TeleportBypassCommand, "tpbypass", "bypasstp")
			.Help(CommandSection.Movement, "", "Teleport items and pylons move you without asking the server.");
		CommandRegistry.Register(GeneralCommands.InvisCommand, "invis")
			.Help(CommandSection.Player, "<ticks|-1>", "Makes you invisible for that many ticks (-1 = forever).");
		CommandRegistry.Register(GeneralCommands.ChangeNameCommand, "changename")
			.Help(CommandSection.Player, "<name>", "Changes your character's name.");
		CommandRegistry.Register(GeneralCommands.INameCommand, "iname")
			.Help(CommandSection.Items, "<name>", "Renames the held item.");
		CommandRegistry.Register(GeneralCommands.DebugMsgCommand, "debugmsg")
			.Help(CommandSection.General, "", "Shows or hides the client's debug messages.");
		CommandRegistry.Register(ClientInfo.AssemblyCommand, "asm")
			.Help(CommandSection.General, "", "Shows the client's build information.");
		CommandRegistry.Register(GeneralCommands.AnimCommand, "anim")
			.Help(CommandSection.Render, "<rotation> <time>", "Plays an item use animation on yourself that others see.");
		CommandRegistry.Register(AntiLag.DustCommand, "dust")
			.Help(CommandSection.Render, "<0-100>", "How much dust is drawn (0-100%).");
		CommandRegistry.Register(GeneralCommands.SeedCommand, "seed")
			.Help(CommandSection.World, "", "Shows the world seed.");
		CommandRegistry.Register(QuickCheats.QuickCheatCommand, "quickcheat", "qc")
			.Help(CommandSection.Player, "", "Turns on most cheats at once.");
		CommandRegistry.Register(GeneralCommands.FakeItemCommand, "fakeitem", "fakeitm")
			.Help(CommandSection.Items, "<item id>", "Shows an item flying from you to every player (only a visual).");
		CommandRegistry.Register(SelfCommands.KillSelfCommand, "suicide", "killself", "killme", "kms", "die")
			.Help(CommandSection.Player, "[p|n|pr|o|e] [id] [projectile]", "Kills you. The death can be by a player (p), NPC (n), projectile (pr), other (o) or have no message (e).");
		CommandRegistry.Register(Rejoin.RejoinCommand, "rj")
			.Help(CommandSection.Info, "", "Rejoins the server you are on.");
		CommandRegistry.Register(Combat.SummonMobCommand, "spawnmob", "summon", "summonmob", "npc")
			.Help(CommandSection.Combat, "<npc id|name> [amount]", "Spawns NPCs (does not work on TShock).");
		CommandRegistry.Register(GeneralCommands.CoinsCommand, "coins")
			.Help(CommandSection.Combat, "", "Gives every NPC 500 platinum coins.");
		CommandRegistry.Register(GeneralCommands.FillAllCommand, "fillall", "fa")
			.Help(CommandSection.World, "", "Fills other players' item stacks to the maximum.");
		CommandRegistry.Register(ServerDetection.BlockSscCommand, "blockssc")
			.Help(CommandSection.Info, "", "Blocks the server's server-side character handling.");
		CommandRegistry.Register(Noclip.RapidPositionCommand, "rapidpos")
			.Help(CommandSection.Movement, "", "Sends your position more often.");
		CommandRegistry.Register(WorldView.RevealInvisiblePlayersCommand, "noinvis")
			.Help(CommandSection.Render, "", "Shows invisible players.");
		CommandRegistry.Register(WorldView.CrtShaderCommand, "crt", "crtshader")
			.Help(CommandSection.Render, "", "CRT screen shader.");
		CommandRegistry.Register(WorldView.GameBoyCommand, "gameboy")
			.Help(CommandSection.Render, "", "Game Boy screen shader.");
		CommandRegistry.Register(WorldView.SeeInvisibleTilesCommand, "seeinvis")
			.Help(CommandSection.Render, "", "Echo (invisible) blocks are always visible.");
		CommandRegistry.Register(GeneralCommands.SetWorldSpawnCommand, "spawnpos", "setworldspawn")
			.Help(CommandSection.World, "", "Moves the world spawn to where you are (single player).");
		CommandRegistry.Register(GeneralCommands.SetDungeonPosCommand, "dungeonpos", "setdungeonpos")
			.Help(CommandSection.World, "", "Moves the dungeon position to where you are (single player).");
		CommandRegistry.Register(WorldCommands.FreeWireCommand, "freewire")
			.Help(CommandSection.World, "", "Free mass wire operations.");
		CommandRegistry.Register(WorldView.MapOutsideLightCommand, "mapoutsidelight", "mpol")
			.Help(CommandSection.Render, "<0-255>", "Map light for the tiles outside the normal world border.");
		CommandRegistry.Register(ItemTools.ZenithItemCommand, "zitem", "zenithitem")
			.Help(CommandSection.Items, "<item id|-1>", "Item the Zenith projectiles look like (-1 = normal).");
		CommandRegistry.Register(NpcControl.ControlNpcCommand, "controlnpc", "cnpc", "cn", "cnf")
			.Help(CommandSection.Combat, "<npc id|name|0>", "Single player: NPCs of that type follow and obey you (0 stops).");
		CommandRegistry.Register(WorldCommands.RegenWorldCommand, "regen", "regenworld")
			.Help(CommandSection.World, "[seed]", "Regenerates the world, with the same or another seed (single player).");
		CommandRegistry.Register(GeneralCommands.ClothingStyleCommand, "clothingstyle", "clothing", "clothstyle", "cltst")
			.Help(CommandSection.Player, "<0-11>", "Changes your skin variant (clothes and body); 10 and 11 are mannequins.");
		CommandRegistry.Register(ItemTools.ReflectAllCommand, "reflectall")
			.Help(CommandSection.Combat, "", "Your projectiles become reflected (hostile) projectiles.");
		CommandRegistry.Register(RemoteMenus.ClothesMenuCommand, "dresser", "clothesmenu", "cltmen")
			.Help(CommandSection.Menus, "", "Opens the dresser (clothes) window anywhere.");
		CommandRegistry.Register(RemoteMenus.HairMenuCommand, "stylist", "hairmenu", "hmen")
			.Help(CommandSection.Menus, "", "Opens the stylist (hair) window anywhere.");
		CommandRegistry.Register(RemoteMenus.CraftingGuideCommand, "guide", "guidemenu", "craftingguide", "recipeguide")
			.Help(CommandSection.Menus, "", "Opens the Guide's crafting help anywhere.");
		CommandRegistry.Register(RemoteMenus.ReforgeMenuCommand, "reforge", "reforgemenu", "goblin", "tinkerer")
			.Help(CommandSection.Menus, "", "Opens the Goblin Tinkerer's reforge anywhere.");
		CommandRegistry.Register(RemoteMenus.PiggyBankCommand, "bank", "piggybank")
			.Help(CommandSection.Menus, "", "Opens your Piggy Bank anywhere.");
		CommandRegistry.Register(RemoteMenus.SafeCommand, "safe")
			.Help(CommandSection.Menus, "", "Opens your Safe anywhere.");
		CommandRegistry.Register(RemoteMenus.DefenderForgeCommand, "forge", "defenderforge", "defforge")
			.Help(CommandSection.Menus, "", "Opens your Defender's Forge anywhere.");
		CommandRegistry.Register(RemoteMenus.VoidVaultCommand, "void", "voidvault", "voidbag")
			.Help(CommandSection.Menus, "", "Opens your Void Vault anywhere.");
		CommandRegistry.Register(ItemTools.UnlimitedSnakeRopesCommand, "infsn")
			.Help(CommandSection.Items, "", "Unlimited snake ropes.");
		CommandRegistry.Register(ItemTools.InFIceCommand, "infice")
			.Help(CommandSection.Items, "", "Unlimited Magical Ice.");
		CommandRegistry.Register(GeneralCommands.IceRodCommand, "icerod")
			.Help(CommandSection.Items, "", "Your Ice Rod places Magical Ice directly (hold it).");
		CommandRegistry.Register(ItemTools.SnakeHCommand, "snh", "snakeh")
			.Help(CommandSection.Items, "<height|0>", "Custom snake rope height (0 turns it off).");
		CommandRegistry.Register(GeneralCommands.Dd2FilmCommand, "dd2film")
			.Help(CommandSection.Render, "", "Plays the Old One's Army intro film.");
		CommandRegistry.Register(GeneralCommands.DstFilmCommand, "dstfilm")
			.Help(CommandSection.Render, "", "Plays the Don't Starve intro film.");
		CommandRegistry.Register(Chests.RelockChestsCommand, "relockchests")
			.Help(CommandSection.World, "", "Locks every chest in the world again.");
		CommandRegistry.Register(Chests.UnlockChestsCommand, "unlockchests")
			.Help(CommandSection.World, "", "Unlocks every chest in the world.");
		CommandRegistry.Register(MapTools.ExportMapCommand, "savemap", "exportmap")
			.Help(CommandSection.World, "", "Saves the map as an image.");
		CommandRegistry.Register(SelfCommands.TrueInvisibilityCommand, "tinvis")
			.Help(CommandSection.Player, "", "True invisibility: other players cannot see you (not on TShock).");
		CommandRegistry.Register(Chests.ExtendChestNameCommand, "fuckchest", "extendchestname")
			.Help(CommandSection.World, "", "Allows longer chest names.");
		CommandRegistry.Register(Chests.LocalChestsCommand, "lchest")
			.Help(CommandSection.World, "", "Opens chests without asking the server for their contents.");
		CommandRegistry.Register(Chests.FillChestCommand, "fillchest", "fillc")
			.Help(CommandSection.World, "<item id|-1>", "Fills the open chest with an item (-1 = random items).");
		CommandRegistry.Register(SelfCommands.BlockBuffCommand, "blockbuff", "bbuff")
			.Help(CommandSection.Player, "[buff id]", "Makes you immune to a buff; no id lists your immunities.");
		CommandRegistry.Register(SelfCommands.CharacterDifficultyCommand, "plrdif", "playerdifficulty", "chrdif", "characterdifficulty")
			.Help(CommandSection.Player, "<sc|mc|hc|cr>", "Changes your character's difficulty: softcore, mediumcore, hardcore or journey.");
		CommandRegistry.Register(WorldInfo.WorldDifficultyCommand, "wlddif", "worlddifficulty")
			.Help(CommandSection.World, "[c|e|m|j]", "Shows or changes the world difficulty: classic, expert, master or journey.");
		CommandRegistry.Register(GeneralCommands.LuckCommand, "luck")
			.Help(CommandSection.Player, "", "Shows your luck and where it comes from.");
		CommandRegistry.Register(BrushState.TileBrushExploitCommand, "tilebrushexploit", "tbe")
			.Help(CommandSection.Building, "[size type active fx fy]", "Brush that places raw tile data (type, active, frame). For advanced use.");
		CommandRegistry.Register(Placement.ForcePaintCommand, "forcepaint", "fpaint")
			.Help(CommandSection.Building, "<paint id>", "Paint that everything you place gets (no id turns it off).");
		CommandRegistry.Register(Placement.ForcePaintCoatCommand, "forcepaintcoat", "fpaintcoat")
			.Help(CommandSection.Building, "<coating id>", "Paint coating (echo, illuminant) that everything you place gets.");
		CommandRegistry.Register(WorldCommands.ScanWorldCommand, "scanworld")
			.Help(CommandSection.Building, "<tile id> [true]", "Finds where a tile is in the world; true teleports you there.");
		CommandRegistry.Register(WorldCommands.RemoveAllTilesCommand, "remalltile", "remtile")
			.Help(CommandSection.Building, "<tile id>", "Removes every tile of that type.");
		CommandRegistry.Register(TeleportTools.TeleportCommand, "tp")
			.Help(CommandSection.Movement, "<x> <y> | <player>", "Teleports you to tile coordinates or to a player (part of the name is enough; quotes for names with spaces).");
		CommandRegistry.Register(Bombs.BombAmountCommand, "bamount")
			.Help(CommandSection.Building, "<n>", "How many bombs the bomb filler drops.");
		CommandRegistry.Register(Bombs.BombTypeCommand, "btype")
			.Help(CommandSection.Building, "<0-5>", "Bomb filler type: 0 dirt, 1 water, 2 lava, 3 honey, 4 dry, 5 random liquid.");
		CommandRegistry.Register(Bombs.BombFillerCommand, "bombs", "bombfiller")
			.Help(CommandSection.Building, "", "Bomb filler: fills the selected area with bombs (.btype, .bamount).");
		CommandRegistry.Register(Placement.TileSyncTestCommand, "tstest")
			.Help(CommandSection.Building, "", "Tile sync test mode (debugging).");
		CommandRegistry.Register(PacketPacing.TilePacketLimitCommand, "packetmax", "packetlimit", "packm", "pl", "tilepacketmax", "tilepacketlimit", "tpackm", "tpl")
			.Help(CommandSection.Building, "<rate>", "Tile and wall packet rate for World Edit (lower it if the server kicks you).");
		CommandRegistry.Register(PacketPacing.ErasePacketLimitCommand, "erasepacketmax", "erasepacketlimit", "epackm", "epl")
			.Help(CommandSection.Building, "<rate>", "Erase packet rate for World Edit.");
		CommandRegistry.Register(PacketPacing.LiquidPacketLimitCommand, "liquidpacketmax", "liquidpacketlimit", "lpackm", "lpl")
			.Help(CommandSection.Building, "<rate>", "Liquid packet rate for World Edit.");
		CommandRegistry.Register(PacketPacing.PaintPacketLimitCommand, "paintpacketmax", "paintpacketlimit", "ppackm", "ppl")
			.Help(CommandSection.Building, "<rate>", "Paint packet rate for World Edit.");
		CommandRegistry.Register(Noclip.NoclipCommand, "noclip", "nc")
			.Help(CommandSection.Movement, "", "Noclip: fly through blocks.");
		CommandRegistry.Register(Noclip.NoclipSpeedCommand, "ncspeed", "noclipspeed")
			.Help(CommandSection.Movement, "", "Changes the noclip speed (each use picks the next one).");
		CommandRegistry.Register(TeleportTools.GoToMouseCommand, "tpm", "mousetp", "gotomouse")
			.Help(CommandSection.Movement, "", "Mouse teleport: the Mouse TP key teleports you to the cursor.");
		CommandRegistry.Register(TeleportTools.MapTeleportCommand, "maptp", "mapteleport")
			.Help(CommandSection.Movement, "", "Map teleport: click on the full map to teleport there.");
		CommandRegistry.Register(Selection.FixUpCommand, "fix", "fixup")
			.Help(CommandSection.Building, "", "Shows how many pastes, fills and conversions are running.");
		CommandRegistry.Register(BrushState.TileBrushCommand, "tilebrush", "tb")
			.Help(CommandSection.Building, "[size]", "Tile brush: places the held block in a brush of that size. Without a size it turns on or off.");
		CommandRegistry.Register(BrushState.PaintBrushCommand, "paintbrush", "pb")
			.Help(CommandSection.Building, "[size]", "Super paint brush of that size. Without a size it turns on or off.");
		CommandRegistry.Register(Placement.PaintOnlyTileCommand, "paintonlytile", "onlypainttile", "opt")
			.Help(CommandSection.Building, "<tile id|-1>", "The super paint brush only paints this tile (-1 = any).");
		CommandRegistry.Register(Placement.PaintOnlyWallCommand, "paintonlywall", "onlypaintwall", "opw")
			.Help(CommandSection.Building, "<wall id|0>", "The super paint brush only paints this wall (0 = any).");
		CommandRegistry.Register(BrushState.EraseBrushCommand, "erasebrush", "eb")
			.Help(CommandSection.Building, "[size]", "Erase brush of that size. Without a size it turns on or off.");
		CommandRegistry.Register(BrushState.LiquidBrushCommand, "liquidbrush", "lb")
			.Help(CommandSection.Building, "[size]", "Liquid and dry brush of that size. Without a size it turns on or off.");
		CommandRegistry.Register(BrushState.WallBrushCommand, "wallbrush", "wb")
			.Help(CommandSection.Building, "[size]", "Wall brush of that size. Without a size it turns on or off.");
		CommandRegistry.Register(Selection.CopyCommand, "copy")
			.Help(CommandSection.Building, "", "Selection mode: select the area to copy.");
		CommandRegistry.Register(Selection.PasteCommand, "paste")
			.Help(CommandSection.Building, "", "Paste mode: pastes the copied area or schematic at the cursor.");
		CommandRegistry.Register(GeneralCommands.LoadBpCommand, "loadbp")
			.Help(CommandSection.Building, "", "Old command, use .loadsch.");
		CommandRegistry.Register(GeneralCommands.SaveBpCommand, "savebp")
			.Help(CommandSection.Building, "", "Old command, use .savesch.");
		CommandRegistry.Register(GeneralCommands.LoadsChCommand, "loadsch", "opensch")
			.Help(CommandSection.Building, "", "Loads a TEdit schematic to paste.");
		CommandRegistry.Register(GeneralCommands.SavesChCommand, "savesch")
			.Help(CommandSection.Building, "", "Saves the copied area as a TEdit schematic.");
		CommandRegistry.Register(GeneralCommands.DrawPhotoCommand, "drawphoto", "drawpic", "loadpic")
			.Help(CommandSection.Building, "", "Picks a picture and turns it into blocks, walls or paint to paste (.dmode, .picscale).");
		CommandRegistry.Register(PixelArt.DrawModeCommand, "dmode")
			.Help(CommandSection.Building, "<0-2>", "What .drawphoto uses: 0 blocks, 1 walls, 2 blocks and walls.");
		CommandRegistry.Register(IsaClient.Transfer.TransferCommands.SendFileCommand, "sendfile")
			.Help(CommandSection.General, "[path]", "Sends any file as Imgur pictures (about one per 2 MB) and copies its code.");
		CommandRegistry.Register(IsaClient.Transfer.TransferCommands.GetFileCommand, "getfile")
			.Help(CommandSection.General, "<code>", "Downloads a file sent with .sendfile into IsaClient/Received/Files.");
		CommandRegistry.Register(IsaClient.Transfer.TransferCommands.DeleteFileCommand, "delfile")
			.Help(CommandSection.General, "<code>", "Takes down a file you sent with .sendfile.");
		CommandRegistry.Register(IsaClient.Building.TileMapFiles.Command, "loadtilemap", "tilemap")
			.Help(CommandSection.Building, "[name]", "Loads a .tilemap from the Terraria Pixel Art Generator to paste it (a name is looked up in Documents/My Games/Terraria/TerraAngel/Imagenes).");
		CommandRegistry.Register(Selection.CheckSizeCommand, "checksize")
			.Help(CommandSection.Building, "", "Shows the size of the brush queue and of the copied area.");
		CommandRegistry.Register(InfoOverlays.SeeTileDataCommand, "seetiledata", "td")
			.Help(CommandSection.Info, "", "Shows information about the tile under the cursor.");
		CommandRegistry.Register(Selection.PasteInfoCommand, "selinfo", "pasteinfo")
			.Help(CommandSection.Building, "", "Extra information while selecting and pasting.");
		CommandRegistry.Register(GeneralCommands.TileDataCommand, "tiledata")
			.Help(CommandSection.Info, "", "Prints all the data of the tile under the cursor.");
		CommandRegistry.Register(PlayerCheats.JourneyMenuCommand, "journeymenu", "journey", "jm")
			.Help(CommandSection.Menus, "", "Journey (creative) menu in any world: press C with the inventory open.");
		CommandRegistry.Register(PlayerCheats.GodmodeCommand, "god", "godmode")
			.Help(CommandSection.Player, "", "Godmode: you take no damage.");
		CommandRegistry.Register(GeneralCommands.MenuCommand, "menu", "ui")
			.Help(CommandSection.General, "[on|off|new|classic]", "Opens or closes the IsaClient panel (also the Open Menu key, default ~ or F9). new / classic: which panel to use.");
		CommandRegistry.Register(Selection.DisableAllCommand, "reset", "disableall")
			.Help(CommandSection.Building, "", "Turns off every World Edit mode: brushes, selection, paste and fills.");
		CommandRegistry.Register(GeneralCommands.RefUiCommand, "refui")
			.Help(CommandSection.General, "", "Rebuilds the panel if something in it looks wrong.");
		CommandRegistry.Register(GeneralCommands.SaveConfigCommand, "config", "saveconfig")
			.Help(CommandSection.General, "", "Saves your settings now.");
		CommandRegistry.Register(GeneralCommands.LoadConfigCommand, "loadconfig")
			.Help(CommandSection.General, "", "Loads your settings again from IsaConfig.json.");
		CommandRegistry.Register(ServerDetection.TshockCommand, "tshock")
			.Help(CommandSection.Info, "", "Tells you whether you are on a TShock server.");
		CommandRegistry.Register(ClientChat.TextColorCommand, "textcolor", "clientcolor")
			.Help(CommandSection.General, "<name> | <RRGGBB> | <r> <g> <b> | reset", "Color of the client's chat messages (default: a very light yellow). Names: red, sky, pink...");
		CommandRegistry.Register(ChatColor.ChatColorCommand, "chatcolor", "chatcolour", "colorchat")
			.Help(CommandSection.Chat, "[on|off] | <name> | <RRGGBB> | <r> <g> <b> | reset | list", "Sends your chat in a color ([c/FF66FF:text]) everyone sees. Items and other tags keep working. .chatcolor alone turns it on/off.");
		CommandRegistry.Register(ChatTranslate.TranslateCommand, "translate", "tr", "translator")
			.Help(CommandSection.Chat, "<from> <to> | <to> | on | off | swap | list | test <text> | in <language|on|off|players|server|client|mine>", "Translates what you type before sending it (e.g. .translate es en; from can be auto). .translate in es shows the messages that arrive in Spanish; Ctrl + right click a message translates just that one. Commands, [tags] and links are never translated.");
		CommandRegistry.Register(IsaClient.Assistant.AskAssistant.AskCommand, "ask", "aiask")
			.Help(CommandSection.Chat, "<question> | sources | forget", "Ask anything: it searches the web, reads the best pages and answers only from them, with numbered sources (only you see it). Settings: AI Assistant > Ask.");
		CommandRegistry.Register(ChatCommands.PrefixCommand, "prefix", "commandprefix", "cmdprefix")
			.Help(CommandSection.General, "<character>", "The character IsaClient commands start with (. by default, e.g. .prefix !). .prefix always works with a dot, so you can't lock yourself out.");
		CommandRegistry.Register(HudButtons.HudCommand, "hud", "hudbuttons")
			.Help(CommandSection.General, "<menu|character|note|all> [on|off] | reset", "The small screen buttons: IsaClient tree (panel), your head (My Character) and music note. Hold one to move it; reset puts them back.");
		CommandRegistry.Register(IsaClient.Plugins.PluginConsoleCommands.PluginsCommand, "plugins", "plugin")
			.Help(CommandSection.Plugins, "[list | on <name> | off <name> | reload [name] | folder | forks | console | devtools]", "Plugins (ImGui, TerraAngel compatible): .plugins alone shows the plugin overlay (also the Insert key). Plugins go in the Plugins folder (.dll, TerraAngel .NET 10 builds, or .cs folders in Plugins/Sources); turn them on there. forks opens Plugins/Forks: a TerraAngel fork as you edit it becomes your own TerraAngel. devtools installs the C# compiler they need.");
		CommandRegistry.Register(IsaClient.Plugins.PluginConsoleCommands.TerraAngelCommand, "ta", "terraangel")
			.Help(CommandSection.Plugins, "", "Opens and closes TerraAngel (the plugin overlay, like Insert); turns the TerraAngel pack on if it is off.");
		CommandRegistry.Register(IsaClient.Plugins.PluginConsoleCommands.ConsoleCommand, "pc", "pluginconsole")
			.Help(CommandSection.Plugins, "<command>", "Runs a plugin console command, or C# (.pc Main.LocalPlayer.statLife = 500), from the chat.");
		CommandRegistry.Register(IsaClient.Video.VideoPlayer.VideoCommand, "video", "vid", "watch")
			.Help(CommandSection.Music, "<name|link|file> | pause | resume | stop | seek <time> | speed <x> | quality <720> | volume <0-100> | loop | download [link] | mini | max | search <words> | folder | quiet", "Watches a video (YouTube search, any video link, or a file) in the video window; alone it opens the window. download saves it as an .mp4 in Videos; quiet turns the game's sounds on or off while videos and songs play.");
		CommandRegistry.Register(IsaClient.Feedback.FeedbackCommands.ReportCommand, "report", "feedback")
			.Help(CommandSection.General, "[bug|idea|feedback <text>] | crashes [on|off] | history", "Opens Report a Bug in the panel, or sends a quick report to the developers from the chat. crashes turns automatic crash reports on or off.");
		CommandRegistry.Register(IsaClient.Feedback.FeedbackCommands.BugCommand, "bug", "bugreport")
			.Help(CommandSection.General, "[what happened]", "Reports a bug to the developers (alone: opens the report form).");
		CommandRegistry.Register(IsaClient.Feedback.FeedbackCommands.SuggestCommand, "suggest", "idea", "suggestion")
			.Help(CommandSection.General, "[your idea]", "Suggests a feature or a change to the developers (alone: opens the report form).");
		DescribeOtherCommands();
	}

	/// <summary>.help for the commands that are not registered by name above (parsers, chat hooks, shared handlers).</summary>
	private static void DescribeOtherCommands()
	{
		CommandRegistry.Describe(CommandSection.Items, "<value>", "Damage of the held item.", "dmg", "damage");
		CommandRegistry.Describe(CommandSection.Items, "<ticks>", "Use time of the held item.", "ut", "usetime");
		CommandRegistry.Describe(CommandSection.Items, "<ticks>", "Animation time of the held item.", "at", "animationtime");
		CommandRegistry.Describe(CommandSection.Items, "<value>", "Knockback of the held item.", "kb", "knockback");
		CommandRegistry.Describe(CommandSection.Items, "<value>", "Size of the held item.", "scale", "scl", "itemsize");
		CommandRegistry.Describe(CommandSection.Items, "<projectile id>", "Projectile the held item shoots.", "proj", "projectile", "shoot");
		CommandRegistry.Describe(CommandSection.Items, "<value>", "Projectile speed of the held item.", "shootspeed", "projspeed", "projectilespeed");
		CommandRegistry.Describe(CommandSection.Items, "<0-93>", "Shoot style of the held item (how it fires).", "shootstyle", "shtst");
		CommandRegistry.Describe(CommandSection.Items, "<times>", "Projectiles the held item shoots per use.", "sha", "shootperuse", "shootamount", "spu");
		CommandRegistry.Describe(CommandSection.Items, "<n>", "Projectile count for the random shoot styles.", "rngamount", "rngam");
		CommandRegistry.Describe(CommandSection.Items, "<tile id>", "Tile the held item places.", "crtl", "createtile");
		CommandRegistry.Describe(CommandSection.Items, "<wall id>", "Wall the held item places.", "crwl", "createwall");
		CommandRegistry.Describe(CommandSection.Items, "<style>", "Place style of the held item.", "plst", "placestyle");
		CommandRegistry.Describe(CommandSection.Items, "<amount>", "Life the held item heals.", "hlf", "heallife");
		CommandRegistry.Describe(CommandSection.Items, "<amount>", "Mana the held item heals.", "hlm", "healmana");
		CommandRegistry.Describe(CommandSection.Render, "[full|off|half|percent]", "Fullbright: see everything without light. A percent sets the minimum brightness.", "fb", "fullbright");
		CommandRegistry.Describe(CommandSection.Info, "on | off | add <ip:port> | user <name> <pass> | best | fetch | rotate | direct | list", "Personal SOCKS5 proxy for privacy (applies the next time you connect). .proxy alone shows the status.", "proxy");
		CommandRegistry.Describe(CommandSection.Bots, "ui | add | as | connect | control | return | chat | say | fight | ai | follow | goto | list | remove | reconnect | save | uuid | proxy | set | run", "Extra clients (bots) on the server, up to 30. .bot alone explains every option.", "bot", "bots");
		CommandRegistry.Describe(CommandSection.Bots, "[on|off|everyone|about|lang|model|key|prompt|forget|config|reload|test]", "AFK AI assistant: answers chat messages that mention you. .ai alone toggles it.", "ai");
		CommandRegistry.Describe(CommandSection.Bots, "[--nick a,b,c]", "Turns on the AI assistant while you are away; --nick adds names it answers to.", "afk");
		CommandRegistry.Describe(CommandSection.World, "", "Downloads the world you are on to Terraria/Worlds (reveals the map first).", "wlddown", "worlddownload", "wlddownload", "wld");
		CommandRegistry.Describe(CommandSection.World, "", "Downloads the world right away, without revealing the map first.", "wlddownnow", "worlddownloadnow");
		CommandRegistry.Describe(CommandSection.World, "", "Reveals the whole map by asking the server for every section. Use it again to cancel.", "revmap", "revealmap");
		CommandRegistry.Describe(CommandSection.World, "", "Map reveal with section requests only (no fallback to the legacy method).", "revmapmodern", "revealmapmodern");
		CommandRegistry.Describe(CommandSection.World, "", "Map reveal with the legacy method.", "legacyrevmap", "legacyrevealmap");
		CommandRegistry.Describe(CommandSection.World, "", "Map reveal with the raw drone packet (1.4.5.8 layout, then the older one).", "rawlegacyrevmap", "rawlegacyrevealmap", "revmaplegacy");
		CommandRegistry.Describe(CommandSection.World, "", "Map reveal with the raw drone packet in the pre-1.4.5.8 layout.", "revmapold", "revmaplegacyold");
		CommandRegistry.Describe(CommandSection.World, "", "Map reveal with packet 8 (only vanilla servers).", "revmapbed", "revealmapbed");
		CommandRegistry.Describe(CommandSection.Info, "", "Writes a network log for the world download (.netlogstop stops it).", "netlog");
		CommandRegistry.Describe(CommandSection.Info, "", "Stops the network log.", "netlogstop");
		CommandRegistry.Describe(CommandSection.Music, "<song|link|file>", "Plays a song (search, YouTube/Spotify/Deezer link or file) or adds it to the queue.", "play", "p");
		CommandRegistry.Describe(CommandSection.Music, "<song>", "Puts a song first in the queue.", "firstplay", "playfirst", "pf", "playnext");
		CommandRegistry.Describe(CommandSection.Music, "<song>", "Plays a song right now.", "playnow");
		CommandRegistry.Describe(CommandSection.Music, "[n]", "Skips the current song (or n songs).", "skip", "next");
		CommandRegistry.Describe(CommandSection.Music, "", "Plays the previous song.", "prev");
		CommandRegistry.Describe(CommandSection.Music, "", "Pauses the music.", "pause");
		CommandRegistry.Describe(CommandSection.Music, "", "Resumes the music.", "resume", "unpause");
		CommandRegistry.Describe(CommandSection.Music, "", "Stops the music.", "stop");
		CommandRegistry.Describe(CommandSection.Music, "", "Shows the queue.", "queue", "q");
		CommandRegistry.Describe(CommandSection.Music, "<n>", "Removes song n from the queue.", "qremove");
		CommandRegistry.Describe(CommandSection.Music, "", "Clears the queue.", "qclear");
		CommandRegistry.Describe(CommandSection.Music, "", "Shows the song that is playing.", "np", "nowplaying");
		CommandRegistry.Describe(CommandSection.Music, "<time>", "Jumps to a time in the song, e.g. .seek 1:30.", "seek");
		CommandRegistry.Describe(CommandSection.Music, "<0-100>", "Music volume.", "volume", "vol");
		CommandRegistry.Describe(CommandSection.Music, "", "Autoplay: related songs keep playing when the queue ends.", "autoplay");
		CommandRegistry.Describe(CommandSection.Music, "", "Keeps playing while the game is minimized or in the background.", "background", "bgplay");
		CommandRegistry.Describe(CommandSection.Music, "[song|queue|off]", "Loops the song or the queue.", "loop");
		CommandRegistry.Describe(CommandSection.Music, "", "Shuffles the queue.", "shuffle");
		CommandRegistry.Describe(CommandSection.Music, "", "Adds or removes the current song from your favorites.", "fav");
		CommandRegistry.Describe(CommandSection.Music, "[search]", "Lists your downloaded songs.", "songs");
		CommandRegistry.Describe(CommandSection.Music, "", "Opens the music folder.", "musicfolder");
		CommandRegistry.Describe(CommandSection.Music, "", "Opens the music window.", "musicui", "player", "mplayer");
		CommandRegistry.Describe(CommandSection.Music, "", "Opens the floating mini player.", "miniplayer", "mini");
		CommandRegistry.Describe(CommandSection.Music, "[file|browser|off]", "Loads YouTube cookies for songs that need a login.", "loadcookies", "cookies");
		CommandRegistry.Describe(CommandSection.Music, "[deno]", "Installs or updates yt-dlp, used to play links.", "ytdlp");
		CommandRegistry.Describe(CommandSection.Music, "[on|off|folder|mute|reload|chat]", "Music status; turns the music module on or off, opens its folder, mutes the game music while songs play. chat off|commands|all: which [Music] messages show.", "music");
	}
}
