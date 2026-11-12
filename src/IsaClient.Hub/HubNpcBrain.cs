using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Movement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Experimental > NPC Brain, under its options: pick any NPC from a grid of their pictures (type to narrow it: name or
/// id), see it move in a card with what it's like, and become it (<see cref="NpcBrain"/>, also .npcai). With "A player"
/// as the target, the players here to pick from.
/// </summary>
internal static class HubNpcBrain
{
	private static HubTheme Theme => HubTheme.Current;

	private static readonly Color Green = new Color(60, 140, 80);

	private static readonly Color Danger = new Color(150, 60, 70);

	private static string _query = "";

	private static string _filteredFor;

	private static List<int> _shown = new List<int>();

	private static int _picked = int.MinValue;

	private static float _scroll;

	private static string _scrollDrag;

	private static float _scrollDragFrom;

	private const int Cell = 60;

	private const int GridRows = 4;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		if (_picked == int.MinValue)
		{
			// Only the first time: the variants (colored slimes...) have negative ids.
			_picked = NpcBrain.Type;
		}
		y = DrawCard(sb, x, y, width) + 12;
		if (NpcBrain.TargetMode == 1)
		{
			y = DrawPlayers(sb, x, y, width) + 12;
		}
		y = DrawPicker(sb, x, y, width);
		return y - start;
	}

	private static readonly Dictionary<int, Terraria.GameContent.Bestiary.UnlockableNPCEntryIcon> _icons = new Dictionary<int, Terraria.GameContent.Bestiary.UnlockableNPCEntryIcon>();

	/// <summary>
	/// An NPC drawn as the Bestiary draws it (its own icon class: bosses, odd sprite sheets and colors come out right),
	/// moving while hovered, cut to its box.
	/// </summary>
	private static void Icon(SpriteBatch sb, int netId, Rectangle box, bool portrait, bool hovered)
	{
		if (!NpcBrain.Valid(netId))
		{
			return;
		}
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		Rectangle clip = outer ? Rectangle.Intersect(box, outerClip) : box;
		if (clip.Width <= 0 || clip.Height <= 0)
		{
			return;
		}
		HubInput.BeginClip(sb, clip);
		try
		{
			if (!_icons.TryGetValue(netId, out Terraria.GameContent.Bestiary.UnlockableNPCEntryIcon icon))
			{
				icon = new Terraria.GameContent.Bestiary.UnlockableNPCEntryIcon(netId);
				_icons[netId] = icon;
			}
			Terraria.GameContent.Bestiary.EntryIconDrawSettings settings = new Terraria.GameContent.Bestiary.EntryIconDrawSettings
			{
				iconbox = box,
				IsPortrait = portrait,
				IsHovered = hovered
			};
			icon.Update(default(Terraria.GameContent.Bestiary.BestiaryUICollectionInfo), box, settings);
			icon.Draw(default(Terraria.GameContent.Bestiary.BestiaryUICollectionInfo), sb, settings);
		}
		catch
		{
		}
		finally
		{
			HubInput.EndClip(sb);
			if (outer)
			{
				HubInput.BeginClip(sb, outerClip);
			}
		}
	}

	/// <summary>The picked NPC: moving, its name, what it's like, and the button to become it (or stop).</summary>
	private static int DrawCard(SpriteBatch sb, int x, int y, int width)
	{
		int type = _picked;
		Rectangle r = new Rectangle(x, y, width, 150);
		bool on = NpcBrain.Active;
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, on ? new Color(110, 230, 110) : Theme.Accent, on ? 0.14f : 0.08f) * HubPrefs.Opacity);
		Rectangle stage = new Rectangle(r.X + 12, r.Y + 12, 126, 126);
		HubKit.Box(sb, stage, Color.Black * 0.3f);
		if (NpcBrain.Valid(type))
		{
			// It walks as it does in the game, turning now and then.
			bool flip = (int)(Main.GlobalTimeWrappedHourly / 3f) % 2 == 0;
			Icon(sb, type, new Rectangle(stage.X + 2, stage.Y + 2, stage.Width - 4, stage.Height - 4), portrait: true, hovered: true);
		}
		int tx = stage.Right + 14;
		int tw = r.Right - tx - 14;
		string name = NpcBrain.Valid(type) ? Lang.GetNPCNameValue(type) : "";
		HubKit.RawText(sb, name, tx, r.Y + 12, Color.White, 0.95f, tw - 210);
		NPC sample = NpcBrain.Model(type);
		if (sample != null)
		{
			string inner = NPCID.Search.TryGetName(sample.type, out string n) ? n : "";
			HubKit.RawText(sb, "#" + type + "  ·  " + inner + "  ·  " + IsaClient.Languages.L10n.F("AI style {0}", sample.aiStyle), tx, r.Y + 38, Theme.Muted, 0.62f, tw - 210);
			int cx = tx;
			int cy = r.Y + 60;
			void Chip(string text, Color color)
			{
				Rectangle c = HubKit.Chip(sb, text, cx, cy, color * 0.3f, color, 0.62f);
				cx = c.Right + 6;
			}
			Chip(sample.noGravity ? IsaClient.Languages.L10n.T("Flies") : IsaClient.Languages.L10n.T("Walks"), new Color(140, 220, 255));
			if (sample.noTileCollide)
			{
				Chip(IsaClient.Languages.L10n.T("Through blocks"), new Color(220, 160, 255));
			}
			if (sample.boss)
			{
				Chip(IsaClient.Languages.L10n.T("Boss"), new Color(255, 120, 120));
			}
			if (sample.townNPC || sample.friendly)
			{
				Chip(IsaClient.Languages.L10n.T("npc::Friendly"), new Color(120, 230, 120));
			}
			Chip(IsaClient.Languages.L10n.F("{0} x {1}", sample.width, sample.height), Theme.Muted);
		}
		string state = on
			? IsaClient.Languages.L10n.F("You are {0} now, after {1}.", Lang.GetNPCNameValue(NpcBrain.Type), TargetName())
			: IsaClient.Languages.L10n.T("Pick an NPC below, then become it. It moves you with its own AI; others see you, only you see its look.");
		string[] lines = HubKit.Wrap(state, tw, 0.64f, 2);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.RawText(sb, lines[i], tx, r.Y + 90 + i * 15, on ? new Color(170, 240, 170) : Theme.Muted, 0.64f);
		}
		// Become it / switch to it / stop.
		bool same = on && NpcBrain.Type == type;
		Rectangle button = new Rectangle(r.Right - 200, r.Y + 14, 186, 34);
		if (same)
		{
			if (HubKit.Button(sb, "brain:stop", button, "Stop", Danger, "Go back to moving as yourself.", true, HubIcon.Item(I.BrainOfConfusion)))
			{
				NpcBrain.Stop();
			}
		}
		else if (HubKit.Button(sb, "brain:go", button, on ? "Switch to it" : "Become it", Green, "Your character moves with this NPC's own AI (.npcai).", !Main.gameMenu && NpcBrain.Valid(type), HubIcon.Item(I.BrainOfConfusion)))
		{
			NpcBrain.Start(type);
		}
		if (on && !same && HubKit.Button(sb, "brain:stop2", new Rectangle(r.Right - 200, r.Y + 54, 186, 30), "Stop", Theme.Slot, "Go back to moving as yourself.", true))
		{
			NpcBrain.Stop();
		}
		return r.Bottom;
	}

	private static string TargetName()
	{
		if (NpcBrain.TargetMode == 1 && NpcBrain.TargetPlayer >= 0 && Main.player[NpcBrain.TargetPlayer].active)
		{
			return Main.player[NpcBrain.TargetPlayer].name;
		}
		return IsaClient.Languages.L10n.T(NpcBrain.TargetModes[NpcBrain.TargetMode]).ToLowerInvariant();
	}

	/// <summary>The players here, to pick who it goes after.</summary>
	private static int DrawPlayers(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Who it goes after", x, y, Theme.Muted, 0.72f);
		y += 22;
		List<Player> players = Main.player.Take(255).Where(p => p.active && p.whoAmI != Main.myPlayer).ToList();
		if (players.Count == 0)
		{
			Rectangle note = new Rectangle(x, y, width, 40);
			HubKit.Box(sb, note, Theme.Card * HubPrefs.Opacity);
			HubKit.Text(sb, "Nobody else is here: it goes after the cursor.", note.X + 14, note.Y + 12, Theme.Muted, 0.7f, width - 28);
			return note.Bottom;
		}
		int bx = x;
		int by = y;
		foreach (Player p in players)
		{
			int w = (int)Math.Min(220f, HubKit.Measure(p.name, 0.74f) + 58f);
			if (bx + w > x + width)
			{
				bx = x;
				by += 44;
			}
			Rectangle r = new Rectangle(bx, by, w, 38);
			bool picked = NpcBrain.TargetPlayer == p.whoAmI;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("brain:p:" + p.whoAmI, null))
			{
				NpcBrain.TargetPlayer = p.whoAmI;
			}
			HubKit.Box(sb, r, picked ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : Color.Lerp(Theme.Slot, Theme.CardHover, HubInput.Anim("brain:p:" + p.whoAmI, hover)));
			HubKit.Head(sb, p, new Vector2(r.X + 20, r.Center.Y - 1), 0.62f);
			HubKit.RawText(sb, p.name, r.X + 40, r.Y + 10, picked ? Color.White : new Color(220, 224, 235), 0.74f, w - 46);
			bx += w + 6;
		}
		return by + 38;
	}

	/// <summary>The search field and the grid of NPCs (it scrolls inside its box).</summary>
	private static int DrawPicker(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Every NPC of the game", x, y, Theme.Muted, 0.72f);
		y += 22;
		_query = HubKit.Field(sb, "brain:q", new Rectangle(x, y, width, 34), _query, "Find an NPC: name or id (slime, eye, pixie, 75...)", 40, HubIcon.Item(I.BrainOfConfusion));
		y += 42;
		if (_filteredFor != _query)
		{
			_filteredFor = _query;
			_shown = NpcBrain.Find(_query);
			_scroll = 0f;
		}
		int columns = Math.Max(1, (width - 20) / (Cell + 4));
		int rows = (_shown.Count + columns - 1) / columns;
		int viewRows = Math.Max(1, Math.Min(GridRows, rows));
		Rectangle box = new Rectangle(x, y, width, viewRows * (Cell + 4) + 8);
		HubKit.Box(sb, box, Theme.Card * HubPrefs.Opacity);
		if (_shown.Count == 0)
		{
			HubKit.Text(sb, "No NPC has that name.", box.X + 14, box.Y + 18, Theme.Muted, 0.72f);
			return box.Bottom;
		}
		float content = rows * (Cell + 4) + 8;
		Rectangle view = new Rectangle(box.X + 4, box.Y + 4, box.Width - 20, box.Height - 8);
		if (content > box.Height)
		{
			HubInput.InnerScroll = box;
			if (HubInput.Wheel != 0 && HubInput.In(box))
			{
				_scroll -= HubInput.Wheel / 2f;
				HubInput.Wheel = 0;
			}
			_scroll = MathHelper.Clamp(_scroll, 0f, content - box.Height);
			_scroll = HubKit.Scrollbar(sb, "brain:bar", new Rectangle(box.Right - 12, box.Y + 4, 8, box.Height - 8), _scroll, content, box.Height, ref _scrollDrag, ref _scrollDragFrom);
		}
		else
		{
			_scroll = 0f;
		}
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(view, outerClip) : view);
		int firstRow = Math.Max(0, (int)(_scroll / (Cell + 4)));
		for (int i = firstRow * columns; i < _shown.Count; i++)
		{
			Rectangle r = new Rectangle(view.X + (i % columns) * (Cell + 4), view.Y + (i / columns) * (Cell + 4) - (int)_scroll, Cell, Cell);
			if (r.Y > view.Bottom)
			{
				break;
			}
			int type = _shown[i];
			bool picked = _picked == type;
			bool current = NpcBrain.Active && NpcBrain.Type == type;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("brain:n:" + type, Lang.GetNPCNameValue(type) + "  #" + type))
			{
				_picked = type;
			}
			float glow = HubInput.Anim("brain:n:" + type, hover || picked);
			Color back = current ? Color.Lerp(Theme.Slot, new Color(110, 230, 110), 0.45f) : (picked ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : Color.Lerp(Theme.Slot, Theme.CardHover, glow));
			HubKit.Box(sb, r, back);
			Rectangle pic = new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10);
			pic.Inflate((int)(glow * 2f), (int)(glow * 2f));
			Icon(sb, type, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), portrait: false, hovered: hover || picked);
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} NPCs", _shown.Count), box.Right - 90, box.Bottom + 4, Theme.Muted * 0.8f, 0.6f);
		return box.Bottom + 22;
	}
}
