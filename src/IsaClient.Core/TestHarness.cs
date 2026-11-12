using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameInput;
using IsaClient.Building;
using IsaClient.Movement;
using IsaClient.Networking;
using IsaClient.WorldTools;
using Terraria;
using IsaClient.Menu;

namespace IsaClient.Core;

/// <summary>
/// Automated test channel, only enabled with the <c>-isatest &lt;dir&gt;</c> launch parameter.
/// Commands are read from <c>&lt;dir&gt;/cmd.txt</c> (one per line, file is deleted after reading) and
/// screenshots of the back buffer are written to <c>&lt;dir&gt;/shots/</c>. It never sends input to the OS,
/// so it cannot interfere with other windows.
/// </summary>
public static class TestHarness
{
	public static bool Enabled;

	public static string Directory;

	private static int _timer;

	private static string _pendingShot;

	private static Point? _fakeMouse;

	private static bool _fakeLeft;

	private static bool _fakeRight;

	private static readonly System.Collections.Generic.List<string> _log = new System.Collections.Generic.List<string>();

	public static void Init()
	{
		if (Program.LaunchParameters.TryGetValue("-isatest", out var value) && !string.IsNullOrEmpty(value))
		{
			Directory = value;
			System.IO.Directory.CreateDirectory(Path.Combine(Directory, "shots"));
			Enabled = true;
		}
	}

	/// <summary>Called from the mod update tick.</summary>
	public static void Update()
	{
		if (!Enabled)
		{
			return;
		}
		if (++_timer < 6)
		{
			return;
		}
		_timer = 0;
		string path = Path.Combine(Directory, "cmd.txt");
		if (!File.Exists(path))
		{
			return;
		}
		string[] lines;
		try
		{
			lines = File.ReadAllLines(path);
			File.Delete(path);
		}
		catch
		{
			return;
		}
		foreach (string line in lines)
		{
			try
			{
				Run(line.Trim());
			}
			catch (Exception ex)
			{
				Log("error running '" + line + "': " + ex.Message);
			}
		}
	}

	public static void Log(string text)
	{
		try
		{
			File.AppendAllText(Path.Combine(Directory, "log.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static void Run(string line)
	{
		if (line.Length == 0)
		{
			return;
		}
		int sep = line.IndexOf(' ');
		string cmd = (sep < 0) ? line : line.Substring(0, sep);
		string arg = (sep < 0) ? "" : line.Substring(sep + 1);
		switch (cmd)
		{
		case "chat":
			if (!ChatCommands.TryHandle(arg))
			{
				Terraria.Chat.ChatHelper.SendChatMessageFromClient(Terraria.UI.Chat.ChatManager.Commands.CreateOutgoingMessage(arg));
			}
			Log("chat: " + arg);
			break;
		case "hub":
		{
			// hub <section> [page]: opens that panel page
			string[] where = arg.Split(' ');
			IsaClient.Hub.ClientHub.OpenSection(where[0], (where.Length > 1) ? int.Parse(where[1]) : 0);
			Log("hub " + arg);
			break;
		}
		case "dumptex":
		{
			// dumptex <Images path without extension>...: those textures as PNG files in <dir>/tex
			System.IO.Directory.CreateDirectory(Path.Combine(Directory, "tex"));
			foreach (string name in arg.Split(' '))
			{
				Microsoft.Xna.Framework.Graphics.Texture2D texture = Main.Assets.Request<Microsoft.Xna.Framework.Graphics.Texture2D>("Images/" + name, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
				using (FileStream file = File.Create(Path.Combine(Directory, "tex", name + ".png")))
				{
					texture.SaveAsPng(file, texture.Width, texture.Height);
				}
			}
			Log("dumped " + arg);
			break;
		}
		case "casino":
			// casino lobby|slots [spin|jackpot]|roulette [bet|go]|horses [bet|go]|rtp: the Bets pages
			Log("casino " + arg + ": " + IsaClient.Casino.BetsWindow.Test(arg));
			break;
		case "casinoshot":
		{
			// casinoshot <name>: the casino window as it is, to <dir>/shots/<name>.png (the back buffer can't be read here)
			Microsoft.Xna.Framework.Graphics.GraphicsDevice device = Main.instance.GraphicsDevice;
			using (Microsoft.Xna.Framework.Graphics.RenderTarget2D target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 1100, 760))
			{
				device.SetRenderTarget(target);
				device.Clear(Microsoft.Xna.Framework.Color.Black);
				Main.spriteBatch.Begin(Microsoft.Xna.Framework.Graphics.SpriteSortMode.Deferred, Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend, Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp, Microsoft.Xna.Framework.Graphics.DepthStencilState.None, IsaClient.Games.GameArt.Raster, null, Microsoft.Xna.Framework.Matrix.Identity);
				IsaClient.Casino.CasinoWindow.TestDraw(Main.spriteBatch, 1000, 700);
				Main.spriteBatch.End();
				device.SetRenderTarget(null);
				using (FileStream file = File.Create(Path.Combine(Directory, "shots", arg + ".png")))
				{
					target.SaveAsPng(file, 1100, 760);
				}
			}
			Log("casinoshot " + arg);
			break;
		}
		case "popupshot":
		{
			// popupshot <name>: the server card shown now, to <dir>/shots/<name>.png (it's drawn twice: the first sets its height)
			Microsoft.Xna.Framework.Graphics.GraphicsDevice device = Main.instance.GraphicsDevice;
			using (Microsoft.Xna.Framework.Graphics.RenderTarget2D target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 640, 460))
			{
				for (int pass = 0; pass < 2; pass++)
				{
					device.SetRenderTarget(target);
					device.Clear(new Microsoft.Xna.Framework.Color(40, 70, 40));
					Main.spriteBatch.Begin(Microsoft.Xna.Framework.Graphics.SpriteSortMode.Deferred, Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend, Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp, Microsoft.Xna.Framework.Graphics.DepthStencilState.None, IsaClient.Games.GameArt.Raster, null, Microsoft.Xna.Framework.Matrix.Identity);
					IsaClient.Networking.ServerPopups.TestDraw(Main.spriteBatch, 640, 460);
					Main.spriteBatch.End();
					device.SetRenderTarget(null);
				}
				using (FileStream file = File.Create(Path.Combine(Directory, "shots", arg + ".png")))
				{
					target.SaveAsPng(file, 640, 460);
				}
			}
			Log("popupshot " + arg);
			break;
		}
		case "popup":
			// popup <title> | <text> [| style]: a card as a server would send it
		{
			if (arg == "never")
			{
				IsaClient.Networking.ServerPopups.TestNever();
				break;
			}
			if (arg == "next")
			{
				IsaClient.Networking.ServerPopups.Close();
				break;
			}
			string[] parts = arg.Split('|');
			IsaClient.Networking.ServerPopups.Show(parts[0].Trim(), (parts.Length > 1) ? parts[1].Trim().Replace("\\n", "\n") : "", (parts.Length > 2) ? (IsaClient.Networking.PopupStyle)int.Parse(parts[2].Trim()) : IsaClient.Networking.PopupStyle.Info);
			Log("popup " + arg);
			break;
		}
		case "shot":
			_pendingShot = arg;
			break;
		case "pvp":
			// pvp on|off: your PvP switch, as the game's button does
			Main.LocalPlayer.hostile = arg == "on";
			NetMessage.SendData(30, -1, -1, null, Main.myPlayer);
			Log("pvp " + Main.LocalPlayer.hostile);
			break;
		case "pvphit":
		{
			// pvphit <player> <damage>: you hit that player in PvP (what a weapon hit sends)
			string[] hit = arg.Split(' ');
			NetMessage.SendPlayerHurt(int.Parse(hit[0]), Terraria.DataStructures.PlayerDeathReason.ByPlayer(Main.myPlayer), int.Parse(hit[1]), 1, critical: false, pvp: true, -1);
			Log("pvphit " + arg);
			break;
		}
		case "bots":
			// bots: every bot's state, place and mount (to check commands and steps run as bots)
			foreach (IsaClient.MultiClient.ClientSession s in IsaClient.MultiClient.MultiClientManager.All)
			{
				Player body = (s.MyIndex >= 0 && s.MyIndex < 255) ? Main.player[s.MyIndex] : null;
				Log($"bot #{s.Id} {s.Name} idx={s.MyIndex} state={s.State} spawned={s.Spawned} pos={s.Position / 16f} mount={s.MountType} work={s.WorkBuff}/{s.WorkMount} seen={(body != null ? (body.mount.Active ? body.mount.Type.ToString() : "none") : "-")} mp={s.InMultiPaste} pixie={s.IsPixie} set={s.SetId} step={s.Script?.Progress} remote={(body != null && body.active ? (body.position / 16f).ToString() + " life " + body.statLife + " inv0 " + body.inventory[0].type + "x" + body.inventory[0].stack : "-")} status={s.StatusText} life={s.StatLife}/{s.StatLifeMax} hostile={s.Hostile} god={s.God} dead={s.Dead} aura={s.KillAuraSize} spam={s.SpamText} buffs={(body != null ? string.Join(",", body.buffType.Where(t => t > 0)) : "-")}");
			}
			Log($"hostile npcs={Main.npc.Count(n => n.active && !n.friendly)}");
			Log($"me idx={Main.myPlayer} pos={Main.LocalPlayer.position / 16f} inv0={Main.LocalPlayer.inventory[0].type} killaura={IsaClient.Gameplay.Combat.KillAura} life={Main.LocalPlayer.statLife} pvp={Main.LocalPlayer.hostile} players={string.Join(",", Main.player.Where(pl => pl.active).Select(pl => pl.whoAmI + ":" + pl.name + (pl.hostile ? "(pvp)" : "")))}");
			for (int i = 0; i < Main.maxItems; i++)
			{
				if (Main.item[i].active && Main.item[i].type == 757)
				{
					Log($"dropped item 757 at {Main.item[i].position / 16f}");
				}
			}
			break;
		case "mouse":
		{
			string[] parts = arg.Split(' ');
			_fakeMouse = new Point(int.Parse(parts[0]), int.Parse(parts[1]));
			_fakeLeft = parts.Length > 2 && parts[2].Contains("L");
			_fakeRight = parts.Length > 2 && parts[2].Contains("R");
			break;
		}
		case "mouseoff":
			_fakeMouse = null;
			_fakeLeft = false;
			_fakeRight = false;
			break;
		case "tp":
		{
			string[] parts = arg.Split(' ');
			Player p = Main.LocalPlayer;
			p.position = new Vector2(int.Parse(parts[0]) * 16, int.Parse(parts[1]) * 16 - p.height);
			p.oldPosition = p.position;
			p.velocity = Vector2.Zero;
			p.fallStart = (int)(p.position.Y / 16f);
			if (Main.netMode == 1)
			{
				NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
			}
			break;
		}
		case "map":
			Main.mapFullscreen = arg == "on";
			break;
		case "join":
		{
			// join <playerIndex>: select a character in the menu; with -join/-port the game connects automatically
			if (Main.PlayerList.Count == 0)
			{
				Main.LoadPlayers();
			}
			Main.SelectPlayer(Main.PlayerList[int.Parse(arg)]);
			break;
		}
		case "world":
		{
			// world <playerIndex> <worldIndex>: plays a single player world with that character
			string[] parts = arg.Split(' ');
			if (Main.PlayerList.Count == 0)
			{
				Main.LoadPlayers();
			}
			if (Main.WorldList.Count == 0)
			{
				Main.LoadWorlds();
			}
			Main.SelectPlayer(Main.PlayerList[int.Parse(parts[0])]);
			Main.ActiveWorldFileData = Main.WorldList[int.Parse(parts[1])];
			WorldGen.playWorld();
			Log("world: " + Main.ActiveWorldFileData.Name);
			break;
		}
		case "lists":
			Main.LoadPlayers();
			Main.LoadWorlds();
			for (int i = 0; i < Main.PlayerList.Count; i++)
			{
				Log("player " + i + ": " + Main.PlayerList[i].Name);
			}
			for (int i = 0; i < Main.WorldList.Count; i++)
			{
				Log("world " + i + ": " + Main.WorldList[i].Name);
			}
			break;
		case "savesch":
			SchematicFiles.ForcedDialogPath = arg;
			SchematicFiles.Save();
			break;
		case "loadsch":
			SchematicFiles.ForcedDialogPath = arg;
			SchematicFiles.Load();
			break;
		case "select":
		{
			string[] parts = arg.Split(' ');
			Selection.SelectionMode = true;
			Selection.PasteMode = false;
			Selection.CopyMode = false;
			Selection.SelectionA = new Point(int.Parse(parts[0]), int.Parse(parts[1]));
			Selection.SelectionB = new Point(int.Parse(parts[2]), int.Parse(parts[3]));
			break;
		}
		case "paste":
		{
			string[] parts = arg.Split(' ');
			Selection.PasteMode = true;
			PasteEngine.PasteTiles(new Point(int.Parse(parts[0]), int.Parse(parts[1])));
			Selection.PasteMode = false; // no stray click pastes again where the mouse is
			break;
		}
		case "copysel":
			Selection.CopyTilesFromFillOrDelMode();
			break;
		case "fill":
			Selection.CancelAction = false;
			Selection.FillTilesSelectionModeV4(Main.LocalPlayer.HeldItem);
			break;
		case "erase":
			Selection.CancelAction = false;
			Selection.EraseTileSelectionMode(int.Parse(arg));
			break;
		case "convert":
			WorldCommands.ConvertWorld(int.Parse(arg), selection: true);
			break;
		case "slot":
			Main.LocalPlayer.selectedItemState.Select(int.Parse(arg));
			break;
		case "give":
		{
			// give <slot> <itemType> <stack>: local inventory only (SSC off) - fills a hotbar slot
			string[] parts = arg.Split(' ');
			Item item = new Item();
			item.SetDefaults(int.Parse(parts[1]));
			item.stack = int.Parse(parts[2]);
			Main.LocalPlayer.inventory[int.Parse(parts[0])] = item;
			if (Main.netMode == 1)
			{
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, int.Parse(parts[0]), item.prefix);
			}
			break;
		}
		case "key":
		{
			// key <trigger> <on|off>: forces a trigger state on the next input frames (handled in ApplyFakeInput)
			int last = arg.LastIndexOf(' ');
			string trigger = arg.Substring(0, last);
			string state = arg.Substring(last + 1);
			if (state == "clear")
			{
				_forcedTriggers.Remove(trigger);
			}
			else
			{
				_forcedTriggers[trigger] = state == "on";
			}
			break;
		}
		case "leave":
			WorldGen.SaveAndQuit();
			break;
		case "modes":
			// modes [god|noclip|fb]: the server mode, and turns a cheat on to see it go off again
			if (arg == "god")
			{
				IsaClient.Gameplay.PlayerCheats.GodMode = true;
			}
			else if (arg == "noclip")
			{
				Noclip.Enabled = true;
			}
			else if (arg == "fb")
			{
				IsaClient.Rendering.FullbrightLighting.Set(0.8f, save: false);
			}
			Log($"modes: mode={IsaClient.Networking.ServerModes.Mode} note={IsaClient.Networking.ServerModes.ServerNote} god={IsaClient.Gameplay.PlayerCheats.GodMode} noclip={Noclip.Enabled} fb={IsaClient.Rendering.FullbrightLighting.Percent} pipe={IsaClient.Net.Pipe.PipeNet.Enabled} panel={InputState.TildeTerminal} plugins={IsaClient.Plugins.PluginLoader.AnyLoaded} netMode={Main.netMode}");
			break;
		case "state":
			Log($"pos={Main.LocalPlayer.Center / 16f} sel={Selection.SelectionMode} A={Selection.SelectionA} B={Selection.SelectionB} paste={Selection.PasteMode} copied={Selection.CopiedTiles.Count} tshock={ServerDetection.OnTshockServer} noclip={Noclip.Enabled} maptp={TeleportTools.MapTeleportation} panel={InputState.TildeTerminal} concurrentPastes={Selection.ConcurrentPastes} fills={Selection.ConcurrentFillOrDels} queued={PacketPacing.QueuedPackets} brushQueue={BrushOperations.TilesToBrush.Count}");
			break;
		case "tile":
		{
			string[] parts = arg.Split(' ');
			int x = int.Parse(parts[0]), y = int.Parse(parts[1]);
			Tile t = Main.tile[x, y];
			Log(t == null ? $"tile {x},{y}=null" : $"tile {x},{y}: active={t.active()} type={t.type} wall={t.wall} liquid={t.liquid} ltype={t.liquidType()} color={t.color()} wallColor={t.wallColor()} fx={t.frameX} fy={t.frameY}");
			break;
		}
		case "clearbuffs":
		{
			// clearbuffs: every other player's buffs gone from this game (as when the server replaces a bot's buffs)
			for (int i = 0; i < 255; i++)
			{
				if (i != Main.myPlayer && Main.player[i].active)
				{
					System.Array.Clear(Main.player[i].buffType, 0, Main.player[i].buffType.Length);
					System.Array.Clear(Main.player[i].buffTime, 0, Main.player[i].buffTime.Length);
				}
			}
			Log("buffs cleared");
			break;
		}
		case "lang":
		{
			// lang [code|auto|refresh]: picks IsaClient's language, then logs the languages and a few texts
			if (arg == "refresh")
			{
				IsaClient.Languages.LanguageRepo.Refresh();
			}
			else if (arg.Length > 0)
			{
				IsaClient.Languages.L10n.Select(arg == "auto" ? "" : arg);
			}
			Log($"lang current={IsaClient.Languages.L10n.Current} wanted={IsaClient.Languages.L10n.WantedCode} checked={IsaClient.Languages.LanguageRepo.CheckedAt} error={IsaClient.Languages.LanguageRepo.CheckError} index={IsaClient.Languages.LanguageRepo.Index.Count}");
			foreach (var p in IsaClient.Languages.L10n.Available)
			{
				Log($"  {p.Code} remote={p.Remote} texts={p.Strings.Count} coverage={IsaClient.Languages.L10n.Coverage(p):P1} downloaded={IsaClient.Languages.LanguageRepo.IsDownloaded(p.Code)} update={IsaClient.Languages.LanguageRepo.NeedsUpdate(p.Code)} progress={IsaClient.Languages.LanguageRepo.Progress(p.Code)} fail={IsaClient.Languages.LanguageRepo.Failure(p.Code)}");
			}
			foreach (string t in new[] { "Fold", "casino::Hit", "tetris::Hold", "autoclick::Hold", "chess::White", "White", "Mode: " + IsaClient.Languages.L10n.T("autoclick::Hold"), "You are now invisible.", "gba::Start" })
			{
				Log($"  T({t}) = {IsaClient.Languages.L10n.T(t)}");
			}
			break;
		}
		case "brushq":
		{
			// brushq x y w h tileType itemId: queues a brush stroke placing that tile on every tile of the rect
			string[] parts = arg.Split(' ');
			int x0 = int.Parse(parts[0]), y0 = int.Parse(parts[1]), w = int.Parse(parts[2]), h = int.Parse(parts[3]);
			lock (BrushOperations.TilesToBrush)
			{
				for (int i = 0; i < w; i++)
				{
					for (int j = 0; j < h; j++)
					{
						BrushOperations.TilesToBrush.Add(new BrushOperations.BrushData { BrushType = 0, X = x0 + i, Y = y0 + j, ToPlace = (ushort)int.Parse(parts[4]), ItemNetId = int.Parse(parts[5]) });
					}
				}
			}
			break;
		}
		case "multipaste":
		{
			// multipaste on|off|here|alone: Multi Paste with the bots here (here), you not pasting (alone)
			if (arg == "here")
			{
				IsaClient.MultiClient.MultiPaste.UseBotsHere = true;
			}
			else if (arg == "alone")
			{
				IsaClient.MultiClient.MultiPaste.YouPasteToo = false;
			}
			else
			{
				IsaClient.MultiClient.MultiPaste.SetEnabled(arg == "on");
			}
			var g = IsaClient.MultiClient.MultiPaste.Running;
			if (IsaClient.MultiClient.MultiPaste.Last != null)
			{
				foreach (var j in IsaClient.MultiClient.MultiPaste.Last.Jobs)
				{
					Log($"  {IsaClient.MultiClient.MultiPaste.Last.Kind} strip {j.Name} x={j.Origin.X} y={j.Origin.Y} w={j.SizeX} h={j.SizeY} rate={j.TileRate} done={j.Finished}");
				}
			}
			Log($"multipaste enabled={IsaClient.MultiClient.MultiPaste.Enabled} here={IsaClient.MultiClient.MultiPaste.UseBotsHere} me={IsaClient.MultiClient.MultiPaste.YouPasteToo} ready={IsaClient.MultiClient.MultiPaste.Helpers().Count} running={(g == null ? "-" : g.Done + "/" + g.Jobs.Count)}");
			foreach (IsaClient.MultiClient.ClientSession s in IsaClient.MultiClient.MultiClientManager.All)
			{
				Player body = (s.MyIndex >= 0 && s.MyIndex < 255) ? Main.player[s.MyIndex] : null;
				Log($"  {s.Name} status={IsaClient.MultiClient.MultiPaste.StatusOf(s)} pasting={s.Pasting} outfit={s.InOutfit} disabled={s.TShockDisabled} vanity={(body == null ? "-" : body.armor[10].type + "," + body.armor[11].type + "," + body.armor[12].type + " dye " + body.dye[0].type + "," + body.dye[1].type + "," + body.dye[2].type)} pos={s.Position / 16f}");
			}
			break;
		}
		case "compare":
		{
			// compare ax ay bx by w h: tiles of rect B that differ from rect A (block, type, wall, slope, paint)
			string[] parts = arg.Split(' ');
			int ax = int.Parse(parts[0]), ay = int.Parse(parts[1]), bx = int.Parse(parts[2]), by = int.Parse(parts[3]), w = int.Parse(parts[4]), h = int.Parse(parts[5]);
			int blocks = 0, walls = 0;
			int[] kinds = new int[5];
			string[] names = { "block", "shape", "paint", "wall", "wallpaint" };
			System.Collections.Generic.List<int>[] cols = new System.Collections.Generic.List<int>[5];
			for (int k = 0; k < 5; k++) cols[k] = new System.Collections.Generic.List<int>();
			for (int i = 0; i < w; i++)
			{
				for (int j = 0; j < h; j++)
				{
					Tile ta = Main.tile[ax + i, ay + j];
					Tile tb = Main.tile[bx + i, by + j];
					if (ta == null || tb == null) continue;
					if (ta.active()) blocks++;
					if (ta.wall > 0) walls++;
					bool[] bad = new bool[5];
					bad[0] = ta.active() != tb.active() || (ta.active() && ta.type != tb.type);
					bad[1] = ta.active() && !bad[0] && (ta.slope() != tb.slope() || ta.halfBrick() != tb.halfBrick());
					bad[2] = ta.active() && !bad[0] && ta.color() != tb.color();
					bad[3] = IsaClient.Building.Placement.ConvertUnsafeWall(ta.wall) != tb.wall;
					bad[4] = ta.wall > 0 && !bad[3] && ta.wallColor() != tb.wallColor();
					for (int k = 0; k < 5; k++)
					{
						if (bad[k])
						{
							kinds[k]++;
							if (!cols[k].Contains(i)) cols[k].Add(i);
						}
					}
				}
			}
			string detail = "";
			for (int k = 0; k < 5; k++) detail += $" {names[k]}={kinds[k]}[{string.Join(",", cols[k])}]";
			int diff = kinds[0] + kinds[1] + kinds[2] + kinds[3] + kinds[4];
			Log($"compare A({ax},{ay}) B({bx},{by}) {w}x{h}: blocks={blocks} walls={walls} different={diff}{detail}");
			break;
		}
		case "count":
		{
			// count x1 y1 x2 y2 type -> number of active tiles of type in rect
			string[] parts = arg.Split(' ');
			int x1 = int.Parse(parts[0]), y1 = int.Parse(parts[1]), x2 = int.Parse(parts[2]), y2 = int.Parse(parts[3]), type = int.Parse(parts[4]);
			int n = 0, walls = 0, any = 0;
			for (int i = x1; i <= x2; i++)
			{
				for (int j = y1; j <= y2; j++)
				{
					Tile t = Main.tile[i, j];
					if (t == null) continue;
					if (t.active()) any++;
					if (t.active() && t.type == type) n++;
					if (t.wall == type) walls++;
				}
			}
			Log($"count rect ({x1},{y1})-({x2},{y2}) type {type}: tiles={n} walls={walls} anyActive={any}");
			break;
		}
		default:
			Log("unknown command: " + line);
			break;
		}
	}

	private static readonly System.Collections.Generic.Dictionary<string, bool> _forcedTriggers = new System.Collections.Generic.Dictionary<string, bool>();

	/// <summary>Called right after PlayerInput updated the real mouse/keyboard for this frame.</summary>
	public static void ApplyFakeInput()
	{
		if (!Enabled)
		{
			return;
		}
		// Isolate the test instance from the real keyboard/mouse: only scripted input reaches the game.
		Main.keyState = default(Microsoft.Xna.Framework.Input.KeyboardState);
		Main.oldKeyState = default(Microsoft.Xna.Framework.Input.KeyboardState);
		Main.drawingPlayerChat = false;
		PlayerInput.Triggers.Current.Reset();
		Main.mouseLeft = false;
		Main.mouseRight = false;
		if (!_fakeMouse.HasValue)
		{
			Main.mouseX = 1;
			Main.mouseY = 1;
			PlayerInput.MouseX = 1;
			PlayerInput.MouseY = 1;
		}
		if (_fakeMouse.HasValue)
		{
			Main.mouseX = _fakeMouse.Value.X;
			Main.mouseY = _fakeMouse.Value.Y;
			Main.mouseLeft = _fakeLeft;
			Main.mouseRight = _fakeRight;
			PlayerInput.Triggers.Current.MouseLeft = _fakeLeft;
			PlayerInput.Triggers.Current.MouseRight = _fakeRight;
			PlayerInput.MouseX = _fakeMouse.Value.X;
			PlayerInput.MouseY = _fakeMouse.Value.Y;
		}
		foreach (var kv in _forcedTriggers)
		{
			PlayerInput.Triggers.Current.KeyStatusHandler(kv.Key, kv.Value);
		}
	}

	/// <summary>Called at the end of the draw; saves the back buffer when a screenshot was requested.</summary>
	public static void AfterDraw(GraphicsDevice device)
	{
		if (!Enabled || _pendingShot == null)
		{
			return;
		}
		string name = _pendingShot;
		_pendingShot = null;
		try
		{
			int w = device.PresentationParameters.BackBufferWidth;
			int h = device.PresentationParameters.BackBufferHeight;
			Color[] data = new Color[w * h];
			device.GetBackBufferData(data);
			using Texture2D texture = new Texture2D(device, w, h);
			texture.SetData(data);
			using FileStream stream = File.Create(Path.Combine(Directory, "shots", name + ".png"));
			texture.SaveAsPng(stream, w, h);
			Log("shot saved: " + name);
		}
		catch (Exception ex)
		{
			Log("shot failed: " + ex.Message);
		}
	}
}
