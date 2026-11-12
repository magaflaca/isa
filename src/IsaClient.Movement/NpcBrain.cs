using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace IsaClient.Movement;

/// <summary>
/// .npcai (Experimental > NPC Brain): your character moves as any NPC of the game does, by its own AI. A stand-in NPC
/// of that type rides on you and its real AI runs on it every tick (as in single player, so it decides by itself);
/// what it does to its velocity, plus its gravity, moves you, and your collision is what it bumps into (the same hook
/// as <see cref="PixieMode"/>). Its target is the closest other player, the one you pick, the cursor or the closest
/// enemy. It never makes NPCs or projectiles (those are let go while it thinks). Only you see its look on you.
/// </summary>
public static class NpcBrain
{
	public static bool Active { get; private set; }

	/// <summary>The NPC whose AI you have (its net id: the colored slimes and other variants are negative).</summary>
	[Setting("NpcBrainType", Min = -100, Max = 2000)]
	public static int Type = NPCID.Pixie;

	/// <summary>It shoots as it does in the game: its projectiles are yours (they hit enemies, and players in PvP).</summary>
	[Setting("NpcBrainShoot")]
	public static bool AllowShoot;

	/// <summary>It summons what it summons in the game (single player: a server makes NPCs itself).</summary>
	[Setting("NpcBrainSummon")]
	public static bool AllowSummon;

	/// <summary>0 the closest player, 1 the player picked (<see cref="TargetPlayer"/>), 2 the cursor, 3 the closest enemy.</summary>
	[Setting("NpcBrainTarget", Min = 0, Max = 3)]
	public static int TargetMode;

	public static readonly string[] TargetModes = { "Closest player", "A player", "The cursor", "Closest enemy" };

	/// <summary>The player chased with <see cref="TargetMode"/> 1.</summary>
	public static int TargetPlayer = -1;

	/// <summary>You look like the NPC (only on your screen).</summary>
	[Setting("NpcBrainSkin")]
	public static bool ShowSkin = true;

	/// <summary>With the look on: your own character isn't drawn under it.</summary>
	[Setting("NpcBrainHide")]
	public static bool HidePlayer = true;

	/// <summary>A kill aura of a block while it's on (.killaura 1).</summary>
	[Setting("NpcBrainAura")]
	public static bool KillAura = true;

	/// <summary>While the AI thinks: NPC.NewNPC makes nothing (it answers "no room"), unless summoning is on.</summary>
	public static bool Suppressing;

	/// <summary>While the AI thinks: Projectile.NewProjectile makes nothing, unless shooting is on.</summary>
	public static bool SuppressingShots;

	private static NPC _npc;

	private static bool _collideX;

	private static bool _collideY;

	/// <summary>Its velocity before your collision (the NPC's oldVelocity).</summary>
	private static Vector2 _before;

	/// <summary>A ghost-like NPC (no tile collision): moved here, its velocity put back after the collision.</summary>
	private static Vector2 _passThrough;

	private static int _lastLife;

	public static bool HidesPlayer => Active && ShowSkin && HidePlayer && _npc != null && !Main.gameMenu;

	public static NPC Puppet => _npc;

	#region On and off

	public static void Start(int type)
	{
		if (Main.gameMenu || !Valid(type))
		{
			return;
		}
		if (PixieMode.Active)
		{
			PixieMode.Stop();
		}
		Player me = Main.LocalPlayer;
		NPC npc = new NPC();
		npc.SetDefaults(type);
		npc.whoAmI = Main.maxNPCs;
		npc.active = true;
		npc.Center = me.Center;
		npc.velocity = me.velocity;
		npc.direction = me.direction;
		npc.target = 255;
		_npc = npc;
		Type = type;
		_before = me.velocity;
		_collideX = _collideY = false;
		_lastLife = me.statLife;
		Active = true;
		ClientSettings.Save();
		ClientChat.Info(IsaClient.Languages.L10n.F("NPC brain: you move as {0} now ({1}). .npcai off stops.", Lang.GetNPCNameValue(type), IsaClient.Languages.L10n.T(TargetModes[TargetMode])));
	}

	public static void Stop(string why = null)
	{
		if (!Active)
		{
			return;
		}
		Active = false;
		_npc = null;
		ClientChat.Info(why ?? IsaClient.Languages.L10n.T("NPC brain: you're you again."));
	}

	/// <summary>.npcai &lt;name | id&gt; [player | mouse | enemy] | off</summary>
	internal static void Command(CommandContext command)
	{
		string[] args = command.Rest.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (args.Length == 0 || args[0].Equals("off", StringComparison.OrdinalIgnoreCase) || args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
		{
			if (Active)
			{
				Stop();
			}
			else
			{
				ClientChat.Info("Usage: .npcai <npc name | id> [player | mouse | enemy], .npcai off. Also in the panel: Experimental > NPC Brain.");
			}
			return;
		}
		int last = args.Length;
		string tail = args[args.Length - 1].ToLowerInvariant();
		if (args.Length > 1 && (tail == "mouse" || tail == "cursor"))
		{
			TargetMode = 2;
			last--;
		}
		else if (args.Length > 1 && (tail == "enemy" || tail == "enemies" || tail == "attack"))
		{
			TargetMode = 3;
			last--;
		}
		string name = string.Join(" ", args.Take(last));
		List<int> found = Find(name);
		if (found.Count == 0 && last > 1)
		{
			// The last word may be a player: the rest is the NPC.
			Player p = IsaClient.Community.ChatTools.FindPlayer(args[last - 1], out _);
			List<int> rest = Find(string.Join(" ", args.Take(last - 1)));
			if (p != null && rest.Count > 0)
			{
				TargetMode = 1;
				TargetPlayer = p.whoAmI;
				found = rest;
			}
		}
		if (found.Count == 0)
		{
			ClientChat.Error(IsaClient.Languages.L10n.F("No NPC called \"{0}\".", name));
			return;
		}
		Start(found[0]);
	}

	/// <summary>A net id there is an NPC for (the variants are -65 to -1).</summary>
	public static bool Valid(int netId)
	{
		return netId != 0 && netId >= -65 && netId < NPCID.Count;
	}

	private static List<int> _all;

	/// <summary>Every NPC with a name: the variants (colored slimes, big and small ones...) next to the rest, by net id.</summary>
	public static List<int> All
	{
		get
		{
			if (_all == null)
			{
				_all = Enumerable.Range(-65, 65).Concat(Enumerable.Range(1, NPCID.Count - 1)).Where(t => !string.IsNullOrWhiteSpace(Lang.GetNPCNameValue(t))).OrderBy(t => (t < 0) ? (Model(t)?.type ?? 0) * 2 - 1 : t * 2).ToList();
			}
			return _all;
		}
	}

	private static readonly Dictionary<int, NPC> _models = new Dictionary<int, NPC>();

	/// <summary>An NPC made from a net id, to read and draw (kept; null when it can't be made).</summary>
	public static NPC Model(int netId)
	{
		if (!Valid(netId))
		{
			return null;
		}
		if (!_models.TryGetValue(netId, out NPC npc))
		{
			try
			{
				npc = new NPC();
				npc.SetDefaults(netId);
				npc.IsABestiaryIconDummy = false;
			}
			catch
			{
				npc = null;
			}
			_models[netId] = npc;
		}
		return npc;
	}

	/// <summary>NPCs (net ids) by id or name, best first.</summary>
	public static List<int> Find(string query)
	{
		query = (query ?? "").Trim();
		List<int> all = All;
		if (query.Length == 0)
		{
			return all;
		}
		if (int.TryParse(query.TrimStart('#'), out int id))
		{
			return all.Where(t => t == id).ToList();
		}
		string q = query.ToLowerInvariant();
		string squashed = q.Replace(" ", "");
		int Rank(int t)
		{
			string name = Lang.GetNPCNameValue(t).ToLowerInvariant();
			string inner = NPCID.Search.TryGetName(t, out string n) ? n.ToLowerInvariant() : "";
			if (name == q || inner == squashed)
			{
				return 0;
			}
			if (name.StartsWith(q) || inner.StartsWith(squashed))
			{
				return 1;
			}
			if (name.Contains(q) || inner.Contains(squashed))
			{
				return 2;
			}
			return 3;
		}
		return all.Select(t => (t, r: Rank(t))).Where(p => p.r < 3).OrderBy(p => p.r).ThenBy(p => Lang.GetNPCNameValue(p.t).Length).Select(p => p.t).ToList();
	}

	#endregion

	/// <summary>Every tick in a world: the kill aura, and off when you leave.</summary>
	public static void Update()
	{
		if (!Active)
		{
			return;
		}
		if (Main.gameMenu)
		{
			Active = false;
			_npc = null;
			return;
		}
		if (TargetMode == 1 && (TargetPlayer < 0 || TargetPlayer >= 255 || !Main.player[TargetPlayer].active))
		{
			TargetPlayer = -1;
		}
		if (KillAura && !Main.LocalPlayer.dead && (!IsaClient.Gameplay.Combat.KillAura || IsaClient.Gameplay.Combat.KillAuraSize <= 0))
		{
			IsaClient.Gameplay.Combat.KillBubble(1);
		}
		if (Main.netMode == 1 && ++_sendTicks % 2 == 0)
		{
			// The AI moves you, not your keys: the game wouldn't tell the others often, and they'd see you jump about.
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
	}

	private static int _sendTicks;

	#region The brain

	/// <summary>Player.Update, right before the collision: the NPC's AI and gravity set your velocity.</summary>
	public static void BeforeCollision(Player me)
	{
		NPC npc = _npc;
		if (!Active || npc == null || me.whoAmI != Main.myPlayer || me.dead)
		{
			return;
		}
		// It stands where you stand (bottoms together), as it was after the last collision.
		npc.position = new Vector2(me.Center.X - npc.width / 2f, me.position.Y + me.height - npc.height);
		npc.collideX = _collideX;
		npc.collideY = _collideY;
		npc.oldVelocity = _before;
		npc.wet = me.wet;
		npc.lavaWet = me.lavaWet;
		npc.honeyWet = me.honeyWet;
		npc.justHit = me.statLife < _lastLife;
		_lastLife = me.statLife;
		npc.life = Math.Max(1, (int)((long)npc.lifeMax * me.statLife / Math.Max(1, me.statLifeMax2)));
		npc.active = true;
		npc.timeLeft = 750;
		if (!Think(npc))
		{
			return;
		}
		if (!npc.noGravity)
		{
			npc.velocity.Y = Math.Min(npc.velocity.Y + 0.3f, 10f);
		}
		if (float.IsNaN(npc.velocity.X) || float.IsNaN(npc.velocity.Y))
		{
			npc.velocity = Vector2.Zero;
		}
		if (npc.velocity.Length() > 40f)
		{
			npc.velocity = Vector2.Normalize(npc.velocity) * 40f;
		}
		_before = npc.velocity;
		if (npc.noTileCollide)
		{
			// It goes through blocks: moved here, nothing for your collision to stop.
			_passThrough = npc.velocity;
			me.position += npc.velocity;
			me.velocity = Vector2.Zero;
		}
		else
		{
			_passThrough = Vector2.Zero;
			me.velocity = npc.velocity;
		}
		if (npc.direction != 0)
		{
			me.direction = npc.direction;
		}
		me.fallStart = (int)(me.position.Y / 16f);
	}

	/// <summary>Player.Update, right after the collision: what it changed is the NPC's collideX / collideY.</summary>
	public static void AfterCollision(Player me)
	{
		NPC npc = _npc;
		if (!Active || npc == null || me.whoAmI != Main.myPlayer || me.dead)
		{
			return;
		}
		if (npc.noTileCollide)
		{
			_collideX = _collideY = false;
			me.velocity = _passThrough;
			npc.velocity = _passThrough;
			return;
		}
		_collideX = me.velocity.X != _before.X;
		_collideY = me.velocity.Y != _before.Y;
		npc.velocity = me.velocity;
	}

	/// <summary>
	/// One tick of the NPC's own AI, set up so it chases what you chose and not you: you (and the players not chosen)
	/// count as ghosts for that moment, and the cursor or an enemy are stood for by the empty player slot 255. It
	/// thinks as in single player, so it decides by itself; what it would spawn isn't made. False when it broke.
	/// </summary>
	private static bool Think(NPC npc)
	{
		Player me = Main.LocalPlayer;
		// The cursor or an enemy are stood for by an empty player slot (NPCs only target slots 0 to 254).
		int standSlot = -1;
		for (int i = 0; i < 255; i++)
		{
			if (i != Main.myPlayer && !Main.player[i].active)
			{
				standSlot = i;
				break;
			}
		}
		Player stand = Main.player[(standSlot >= 0) ? standSlot : 255];
		bool[] ghosts = new bool[255];
		bool[] deads = new bool[255];
		for (int i = 0; i < 255; i++)
		{
			ghosts[i] = Main.player[i].ghost;
			deads[i] = Main.player[i].dead;
		}
		bool standActive = stand.active;
		bool standDead = stand.dead;
		bool standGhost = stand.ghost;
		Vector2 standPosition = stand.position;
		Vector2 standVelocity = stand.velocity;
		int standTank = stand.tankPet;
		int standAggro = stand.aggro;
		int netMode = Main.netMode;
		try
		{
			Vector2? point = null;
			bool othersToo = false;
			switch (TargetMode)
			{
			case 1:
				if (TargetPlayer >= 0 && TargetPlayer < 255 && TargetPlayer != Main.myPlayer && Main.player[TargetPlayer].active)
				{
					othersToo = true;
				}
				break;
			case 2:
				point = Main.MouseWorld;
				othersToo = true;
				break;
			case 3:
				point = ClosestEnemy(me)?.Center ?? Main.MouseWorld;
				othersToo = true;
				break;
			}
			if (TargetMode == 0 && !AnyOtherPlayer())
			{
				// Alone: the cursor.
				point = Main.MouseWorld;
			}
			// Left out: ghosts and dead for that moment. Some AIs pick with TargetClosest (it skips ghosts), others with
			// Player.FindClosest (it only skips the dead): both pass you by, and the players not chosen.
			me.ghost = true;
			me.dead = true;
			if (othersToo)
			{
				for (int i = 0; i < 255; i++)
				{
					if (i != Main.myPlayer && i != standSlot && !(TargetMode == 1 && i == TargetPlayer))
					{
						Main.player[i].ghost = true;
						Main.player[i].dead = true;
					}
				}
			}
			if (point.HasValue && standSlot >= 0)
			{
				// Aimed at it from the start (AIs that keep their target don't look again every tick).
				npc.target = standSlot;
			}
			else if (TargetMode == 1 && TargetPlayer >= 0 && TargetPlayer < 255)
			{
				npc.target = TargetPlayer;
			}
			if (point.HasValue && standSlot >= 0)
			{
				stand.active = true;
				stand.dead = false;
				stand.ghost = false;
				stand.position = point.Value - new Vector2(stand.width / 2f, stand.height / 2f);
				stand.velocity = Vector2.Zero;
				// An empty slot may keep a pet tank or aggro from whoever had it: NPCs would aim at those instead.
				standTank = stand.tankPet;
				standAggro = stand.aggro;
				stand.tankPet = -1;
				stand.aggro = 0;
			}
			bool[] shotsBefore = null;
			if (AllowShoot)
			{
				shotsBefore = new bool[Main.maxProjectiles];
				for (int i = 0; i < Main.maxProjectiles; i++)
				{
					shotsBefore[i] = Main.projectile[i].active;
				}
			}
			Main.netMode = 0;
			Suppressing = !(AllowSummon && netMode == 0);
			SuppressingShots = !AllowShoot;
			npc.AI();
			Suppressing = false;
			SuppressingShots = false;
			Main.netMode = netMode;
			if (shotsBefore != null)
			{
				Claim(shotsBefore);
			}
			if (ShowSkin)
			{
				Main.instance.LoadNPC(npc.type);
				npc.FindFrame();
			}
			return true;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			Stop(IsaClient.Languages.L10n.F("NPC brain: {0}'s AI doesn't work on a player ({1}). Stopped.", Lang.GetNPCNameValue(npc.type), ex.GetType().Name));
			return false;
		}
		finally
		{
			Suppressing = false;
			SuppressingShots = false;
			Main.netMode = netMode;
			for (int i = 0; i < 255; i++)
			{
				Main.player[i].ghost = ghosts[i];
				Main.player[i].dead = deads[i];
			}
			stand.active = standActive;
			stand.dead = standDead;
			stand.ghost = standGhost;
			stand.position = standPosition;
			stand.velocity = standVelocity;
			stand.tankPet = standTank;
			stand.aggro = standAggro;
		}
	}

	/// <summary>
	/// What it just shot becomes yours, as a player's shot: it hurts enemies (and players in PvP, not you), and on a
	/// server it's sent like any projectile of yours.
	/// </summary>
	private static void Claim(bool[] before)
	{
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile p = Main.projectile[i];
			if (!p.active || before[i])
			{
				continue;
			}
			p.owner = Main.myPlayer;
			p.hostile = false;
			p.friendly = true;
			p.npcProj = false;
			p.netUpdate = true;
			if (Main.netMode == 1)
			{
				NetMessage.SendData(27, -1, -1, null, i);
			}
		}
	}

	private static bool AnyOtherPlayer()
	{
		for (int i = 0; i < 255; i++)
		{
			Player p = Main.player[i];
			if (i != Main.myPlayer && p.active && !p.dead && !p.ghost)
			{
				return true;
			}
		}
		return false;
	}

	private static NPC ClosestEnemy(Player me)
	{
		NPC found = null;
		float best = float.MaxValue;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (!n.active || n.friendly || n.townNPC || n.dontTakeDamage || n.lifeMax <= 5 || n.type == NPCID.TargetDummy)
			{
				continue;
			}
			float d = Vector2.DistanceSquared(n.Center, me.Center);
			if (d < best)
			{
				best = d;
				found = n;
			}
		}
		return found;
	}

	#endregion

	/// <summary>Its look on you, drawn over the players (only on your screen).</summary>
	public static void DrawSkin(SpriteBatch sb)
	{
		NPC npc = _npc;
		if (!Active || !ShowSkin || npc == null || Main.gameMenu || Main.LocalPlayer.dead)
		{
			return;
		}
		try
		{
			// The game's own NPC drawing (bosses, odd sheets, colors), standing where you stand.
			Player me0 = Main.LocalPlayer;
			Vector2 keep = npc.position;
			int alpha = npc.alpha;
			npc.Bottom = new Vector2(me0.Center.X, me0.position.Y + me0.height + me0.gfxOffY);
			if (npc.alpha >= 255)
			{
				// Still fading in: whole.
				npc.alpha = 0;
			}
			Main.instance.LoadNPC(npc.type);
			Main.instance.DrawNPCDirect(sb, npc, npc.behindTiles, Main.screenPosition);
			npc.position = keep;
			npc.alpha = alpha;
			return;
		}
		catch
		{
		}
		try
		{
			Main.instance.LoadNPC(npc.type);
			Texture2D texture = TextureAssets.Npc[npc.type].Value;
			Rectangle frame = npc.frame;
			if (frame.Width <= 0 || frame.Height <= 0)
			{
				frame = texture.Frame(1, Math.Max(1, Main.npcFrameCount[npc.type]));
			}
			Player me = Main.LocalPlayer;
			Vector2 bottom = new Vector2(me.Center.X, me.position.Y + me.height + me.gfxOffY);
			Vector2 at = bottom - Main.screenPosition - new Vector2(0f, frame.Height * npc.scale / 2f) + new Vector2(0f, 4f);
			Color light = Lighting.GetColor((int)(me.Center.X / 16f), (int)(me.Center.Y / 16f));
			SpriteEffects flip = (npc.spriteDirection == 1) ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			sb.Draw(texture, at, frame, (npc.alpha >= 255) ? light : npc.GetAlpha(light), npc.rotation, frame.Size() / 2f, npc.scale, flip, 0f);
			if (npc.color != default(Color))
			{
				// Tinted ones (slimes...): their color over the gray sprite, as the game draws them.
				sb.Draw(texture, at, frame, npc.GetColor(light), npc.rotation, frame.Size() / 2f, npc.scale, flip, 0f);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// Draws an NPC (by net id) fitted in a box, walking when <paramref name="animate"/>: its frame comes from the game's
	/// own FindFrame (sheets with several columns or odd frames are cut right), with its color when it's tinted.
	/// </summary>
	public static void DrawModel(SpriteBatch sb, int netId, Rectangle box, bool animate, bool flip, float maxScale = 2f)
	{
		NPC npc = Model(netId);
		if (npc == null)
		{
			return;
		}
		try
		{
			Main.instance.LoadNPC(npc.type);
			if (!TextureAssets.Npc[npc.type].IsLoaded)
			{
				return;
			}
			Texture2D texture = TextureAssets.Npc[npc.type].Value;
			npc.velocity = animate ? new Vector2(flip ? 1.5f : -1.5f, 0f) : Vector2.Zero;
			npc.direction = npc.spriteDirection = flip ? 1 : -1;
			if (animate || npc.frame.Width <= 0)
			{
				npc.FindFrame();
			}
			Rectangle frame = npc.frame;
			if (frame.Width <= 0 || frame.Height <= 0 || frame.Right > texture.Width || frame.Bottom > texture.Height)
			{
				frame = texture.Frame(1, Math.Max(1, Main.npcFrameCount[npc.type]));
			}
			float scale = Math.Min(maxScale, Math.Min((float)box.Width / frame.Width, (float)box.Height / frame.Height));
			Vector2 at = new Vector2(box.Center.X, box.Center.Y);
			SpriteEffects effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Color white = Color.White;
			// Many start fully see-through (they fade in when they spawn): pictures show them whole.
			Color body = (npc.alpha >= 255) ? white : npc.GetAlpha(white);
			sb.Draw(texture, at, frame, body, 0f, frame.Size() / 2f, scale, effects, 0f);
			if (npc.color != default(Color))
			{
				sb.Draw(texture, at, frame, npc.GetColor(white), 0f, frame.Size() / 2f, scale, effects, 0f);
			}
		}
		catch
		{
		}
	}
}
