using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Color = Microsoft.Xna.Framework.Color;
using L = IsaClient.Languages.L10n;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Casino;

/// <summary>
/// The casino's Bets page: the same window and the same chips as the cards, with the slot machine (yours alone), the
/// Roulette and the races (shared by everyone here, see <see cref="BetsNet"/>). Fruit, gems and horses are Terraria's
/// own; the wheel, the cabinet and the stands come from IsaClient.Assets/CasinoBets.png (tools/casino/make_bets_art.py).
/// </summary>
public static class BetsWindow
{
	/// <summary>The Lucky Clover: the page's icon.</summary>
	public const int BetsItem = 5574;

	/// <summary>The game open (0: the page with the three games).</summary>
	public static CasinoGame Page;

	private static Color Cream => CasinoArt.Cream;

	private static Color Gold => CasinoArt.Gold;

	private static readonly Color Muted = new Color(214, 196, 170);

	private static readonly Color GreenButton = new Color(44, 120, 70);

	private static readonly Color RedButton = new Color(140, 40, 46);

	private static readonly Color GoldButton = new Color(150, 106, 34);

	private static readonly Color PlainButton = new Color(44, 50, 88);

	private static readonly Color Panel = new Color(30, 38, 76);

	private static readonly Color PanelEdge = new Color(10, 12, 24);

	private static double Now => GameArt.Now;

	#region Atlas

	private static Texture2D _atlas;

	private static bool _failed;

	private static readonly Rectangle Rotor = new Rectangle(0, 0, 320, 320);

	private static readonly Rectangle Bowl = new Rectangle(322, 0, 320, 320);

	private static readonly Rectangle Cabinet = new Rectangle(0, 322, 190, 236);

	private static readonly Rectangle SevenSrc = new Rectangle(192, 322, 32, 32);

	private static readonly Rectangle Ball = new Rectangle(226, 322, 10, 10);

	private static readonly Rectangle Turret = new Rectangle(238, 322, 34, 34);

	private static readonly Rectangle Stands = new Rectangle(280, 322, 256, 96);

	private static readonly Rectangle Hills = new Rectangle(280, 420, 256, 64);

	private static readonly Rectangle Rail = new Rectangle(192, 358, 32, 20);

	private static readonly Rectangle Post = new Rectangle(226, 358, 16, 72);

	private static readonly Rectangle Flag = new Rectangle(244, 358, 24, 16);

	private static readonly Rectangle Gate = new Rectangle(192, 380, 20, 72);

	private static Texture2D Atlas
	{
		get
		{
			if (_atlas == null && !_failed)
			{
				try
				{
					using Stream stream = typeof(BetsWindow).Assembly.GetManifestResourceStream("IsaClient.Assets.CasinoBets.png");
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

	private static void Px(SpriteBatch sb, Rectangle src, Vector2 at, float scale, Color? color = null)
	{
		if (Atlas != null)
		{
			sb.Draw(Atlas, new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y)), src, color ?? Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		}
	}

	/// <summary>A piece repeated sideways over <paramref name="area"/>, moved by <paramref name="scroll"/> pixels, cut at its edges.</summary>
	private static void Strip(SpriteBatch sb, Rectangle src, Rectangle area, float scroll, float scale = 1f)
	{
		if (Atlas == null)
		{
			return;
		}
		float w = src.Width * scale;
		float start = -(((scroll % w) + w) % w);
		for (float x = start; x < area.Width; x += w)
		{
			float left = Math.Max(0f, -x);
			float right = Math.Min(w, area.Width - x);
			if (right <= left)
			{
				continue;
			}
			Rectangle part = new Rectangle(src.X + (int)(left / scale), src.Y, (int)Math.Ceiling((right - left) / scale), src.Height);
			sb.Draw(Atlas, new Rectangle(area.X + (int)(x + left), area.Y, (int)(right - left), (int)(src.Height * scale)), part, Color.White);
		}
	}

	private static Texture2D ItemTexture(int item, out Rectangle frame)
	{
		Main.instance.LoadItem(item);
		Texture2D texture = TextureAssets.Item[item].Value;
		frame = (Main.itemAnimations[item] != null) ? Main.itemAnimations[item].GetFrame(texture) : texture.Bounds;
		return texture;
	}

	#endregion

	#region Opening

	public static void OpenLobby()
	{
		if (Page != 0)
		{
			BackToLobby();
		}
		CasinoWindow.OpenBets();
	}

	public static void OpenGame(CasinoGame game)
	{
		CasinoWindow.OpenBets();
		if (Page != game)
		{
			BackToLobby();
		}
		Page = game;
		if (game != CasinoGame.Slots)
		{
			BetsNet.Enter(game);
		}
	}

	/// <summary>Can you get up now? Not with your chips in play (the ball is rolling, the race is on, the reels turn).</summary>
	public static bool CanLeave => Page == CasinoGame.Slots ? !_spinning : !(BetsNet.AtTable && BetsNet.Phase == BetsPhase.Running && BetsNet.Staked > 0);

	public static void BackToLobby()
	{
		_auto = false;
		if (BetsNet.AtTable)
		{
			BetsNet.Exit();
		}
		Page = 0;
	}

	/// <summary>The header's two lines.</summary>
	public static void Titles(out string title, out string sub)
	{
		switch (Page)
		{
		case CasinoGame.Slots:
			title = L.T("Slot machine");
			sub = L.F("Five lines. Three sevens pay {0} times a line's bet.", Slots.Pays[Slots.Seven].ToString("N0"));
			break;
		case CasinoGame.Roulette:
			title = L.T("Roulette");
			sub = TableLine();
			break;
		case CasinoGame.Horses:
			title = L.T("Horse races");
			sub = TableLine();
			break;
		default:
			title = L.T("Bets");
			sub = L.T("The house takes the bets");
			break;
		}
	}

	private static string TableLine()
	{
		if (BetsNet.Waiting)
		{
			return L.T("Finding the table...");
		}
		int players = BetsNet.Members.Count + 1;
		return (players > 1) ? L.F("{0} players at this table", players) : L.T("You're alone: the others can join");
	}

	#endregion

	/// <summary>The game loop.</summary>
	public static void Update()
	{
		SlotsTick();
	}

	public static void Draw(SpriteBatch sb, Rectangle body)
	{
		switch (Page)
		{
		case CasinoGame.Slots:
			DrawSlots(sb, body);
			break;
		case CasinoGame.Roulette:
			DrawRoulette(sb, body);
			break;
		case CasinoGame.Horses:
			DrawHorses(sb, body);
			break;
		default:
			DrawLobby(sb, body);
			break;
		}
	}

	#region The page with the three games

	private static void DrawLobby(SpriteBatch sb, Rectangle body)
	{
		CasinoArt.Room(sb, body, 0.46f);
		// the Moon Lord, waving a lucky hand over his little table
		Rectangle stage = new Rectangle(body.X + 10, body.Y + 8, 320, 252);
		Vector2 core = new Vector2(stage.Center.X, stage.Y + 158);
		float wave = (float)Math.Sin(Now * 3.2);
		Vector2 look = MusicPlayerWindow.MousePoint.ToVector2() - (core + new Vector2(0f, -90f));
		CasinoArt.MoonLord(sb, core, 0.2f, Vector2.Zero, new Vector2(30f + wave * 30f, -250f), 0, 0, (look.LengthSquared() > 1f) ? Vector2.Normalize(look) : Vector2.Zero);
		Rectangle bar = new Rectangle(stage.X - 4, stage.Y + 186, stage.Width + 8, 66);
		CasinoArt.Table(sb, bar, new Color(120, 40, 150), new Color(70, 22, 100));
		GameArt.Sharp(sb);
		foreach ((int item, int x) in new[] { (BetsItem, -82), (Terraria.ID.ItemID.LuckyHorseshoe, -36) })
		{
			Texture2D t = ItemTexture(item, out Rectangle f);
			sb.Draw(t, new Vector2(stage.Center.X + x, stage.Y + 203), f, Color.White, 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
		}
		CasinoArt.Chips(sb, new Vector2(stage.Center.X + 54, stage.Y + 236), 1630, 1f, 5);
		GameArt.Smooth(sb);
		CasinoWindow.DrawWalletPanel(sb, new Rectangle(body.X + 10, stage.Bottom + 10, 320, body.Bottom - stage.Bottom - 20));
		Rectangle right = new Rectangle(stage.Right + 14, body.Y + 8, body.Right - stage.Right - 24, body.Height - 18);
		int h = (right.Height - 20) / 3;
		SlotsCard(sb, new Rectangle(right.X, right.Y, right.Width, h));
		RouletteCard(sb, new Rectangle(right.X, right.Y + h + 10, right.Width, h));
		HorsesCard(sb, new Rectangle(right.X, right.Y + (h + 10) * 2, right.Width, h));
	}

	/// <summary>A game's card: its picture on the left, its name, its lines, and the button.</summary>
	private static bool GameCard(SpriteBatch sb, Rectangle r, Color back, Action<Rectangle> art, string id, string title, string line, string status, string button, Color statusColor)
	{
		GameOverlay.MenuPanel(sb, r, Panel * 0.95f, PanelEdge);
		Rectangle box = new Rectangle(r.X + 10, r.Y + 10, 170, r.Height - 20);
		GameOverlay.MenuPanel(sb, box, back, new Color(8, 10, 22));
		art(box);
		int tx = box.Right + 16;
		int room = r.Right - tx - 14;
		MusicDrawing.RawText(sb, title, tx, r.Y + 12, Gold, 0.92f, room);
		int y = r.Y + 42;
		foreach (string l in Wrap(line, room, 0.64f, 3))
		{
			MusicDrawing.RawText(sb, l, tx, y, Cream * 0.86f, 0.64f);
			y += 18;
		}
		if (status != null)
		{
			MusicDrawing.RawText(sb, status, tx, r.Bottom - 34, statusColor, 0.64f, room - 150);
		}
		return CasinoArt.Button(sb, id, new Rectangle(r.Right - 146, r.Bottom - 46, 132, 34), button, GreenButton);
	}

	private static IEnumerable<string> Wrap(string text, int width, float scale, int lines)
	{
		List<string> result = new List<string>();
		string line = "";
		foreach (string word in text.Split(' '))
		{
			string next = (line.Length == 0) ? word : (line + " " + word);
			if (MusicDrawing.Measure(next, scale) > width && line.Length > 0)
			{
				result.Add(line);
				line = word;
			}
			else
			{
				line = next;
			}
		}
		result.Add(line);
		return result.Take(lines);
	}

	private static void SlotsCard(SpriteBatch sb, Rectangle r)
	{
		bool click = GameCard(sb, r, new Color(72, 30, 110), box =>
		{
			GameArt.Sharp(sb);
			float k = (box.Height - 10) / (float)Cabinet.Height;
			Vector2 at = new Vector2(box.Center.X - Cabinet.Width * k / 2f, box.Y + 5);
			if (Atlas != null)
			{
				sb.Draw(Atlas, at, Cabinet, Color.White, 0f, Vector2.Zero, k, SpriteEffects.None, 0f);
			}
			float cell = 138f * k / 3f;
			for (int i = 0; i < 3; i++)
			{
				Symbol(sb, Slots.Seven, new Rectangle((int)(at.X + 26f * k + i * cell), (int)(at.Y + 84f * k + 26f * k), (int)cell, (int)(50f * k)), null, 1f);
			}
			GameArt.Smooth(sb);
		}, "bets:slots", L.T("Slot machine"), L.T("Fruit, bells, diamonds and sevens on five lines. Just you and the machine."), L.F("Up to {0} times a line", Slots.Pays[Slots.Seven].ToString("N0")), L.T("Play"), Muted);
		if (click)
		{
			OpenGame(CasinoGame.Slots);
		}
	}

	private static void RouletteCard(SpriteBatch sb, Rectangle r)
	{
		BetsAd ad = BetsNet.Ads.FirstOrDefault(a => a.Game == CasinoGame.Roulette);
		string status = (ad != null) ? L.F("{0} playing now", ad.Players) : (Online ? L.T("Be the first at the table") : L.T("Alone until you join a server"));
		bool click = GameCard(sb, r, new Color(24, 96, 62), box =>
		{
			float size = box.Height - 12;
			Wheel(sb, new Vector2(box.Center.X, box.Center.Y), size, (float)(Now * 0.35));
		}, "bets:roulette", L.T("Roulette"), L.T("One zero, the same table for everyone on the server: the same ball, everyone's chips on the same felt."), status, L.T((ad != null) ? "Join" : "Play"), (ad != null) ? CasinoArt.SoftGreen : Muted);
		if (click)
		{
			OpenGame(CasinoGame.Roulette);
		}
	}

	private static void HorsesCard(SpriteBatch sb, Rectangle r)
	{
		BetsAd ad = BetsNet.Ads.FirstOrDefault(a => a.Game == CasinoGame.Horses);
		string status = (ad != null) ? L.F("{0} playing now", ad.Players) : (Online ? L.T("Be the first at the table") : L.T("Alone until you join a server"));
		bool click = GameCard(sb, r, new Color(50, 120, 170), box =>
		{
			MusicDrawing.Fill(sb, new Rectangle(box.X + 3, box.Bottom - 34, box.Width - 6, 31), new Color(70, 150, 76));
			GameArt.Sharp(sb);
			Strip(sb, Rail, new Rectangle(box.X + 3, box.Bottom - 48, box.Width - 6, 20), (float)(Now * 90.0));
			DrawHorse(sb, 0, new Vector2(box.Center.X + 6, box.Bottom - 10), true, 0.95f);
			GameArt.Smooth(sb);
		}, "bets:horses", L.T("Horse races"), L.T("Six horses, odds for each one, and a race everyone on the server watches together."), status, L.T((ad != null) ? "Join" : "Play"), (ad != null) ? CasinoArt.SoftGreen : Muted);
		if (click)
		{
			OpenGame(CasinoGame.Horses);
		}
	}

	private static bool Online => Main.netMode == 1 && IsaClient.Net.Pipe.PipeNet.Enabled && IsaClient.Net.Pipe.PipeNet.Ready;

	#endregion

	#region Chips to bet with

	private static int _chip = 1;

	/// <summary>The chips you bet with, to pick from. Returns where it ended.</summary>
	private static int ChipPicker(SpriteBatch sb, int x, int y)
	{
		GameArt.Sharp(sb);
		for (int i = 0; i < BetsLimits.Chips.Length; i++)
		{
			int value = BetsLimits.Chips[i];
			Rectangle chip = new Rectangle(x, y, 38, 38);
			bool hover = MusicPlayerWindow.MouseIn(chip);
			bool on = i == _chip;
			if (on)
			{
				MusicDrawing.Fill(sb, new Rectangle(chip.X + 3, chip.Bottom + 1, chip.Width - 6, 3), Gold);
			}
			CasinoArt.Chip(sb, new Vector2(chip.Center.X, chip.Center.Y - ((on || hover) ? 2 : 0)), CasinoArt.ChipFor(value), on ? 2.05f : 1.8f);
			if (hover)
			{
				MusicDrawing.Hover("bets:chip:" + i, L.F("Bet {0} at a time", value.ToString("N0")));
				if (MusicPlayerWindow.Clicked)
				{
					MusicPlayerWindow.Clicked = false;
					_chip = i;
					SoundEngine.PlaySound(12);
				}
			}
			x += 42;
		}
		GameArt.Smooth(sb);
		return x;
	}

	private static string Short(long chips)
	{
		if (chips >= 1000000)
		{
			return (chips / 100000 / 10.0).ToString("0.#") + "M";
		}
		return (chips >= 1000) ? ((chips / 100 / 10.0).ToString("0.#") + "k") : chips.ToString();
	}

	/// <summary>A chip on the felt with what it's worth on it.</summary>
	private static void BetChip(SpriteBatch sb, Vector2 center, int amount, float alpha = 1f)
	{
		GameArt.Sharp(sb);
		CasinoArt.Chip(sb, center, CasinoArt.ChipFor(amount), 1.4f, alpha);
		GameArt.Smooth(sb);
		string text = Short(amount);
		float w = MusicDrawing.Measure(text, 0.5f);
		MusicDrawing.RawText(sb, text, (float)Math.Round(center.X - w / 2f), center.Y - 7f, Color.White * alpha, 0.5f);
	}

	/// <summary>The buttons every shared game has under the table, and the clock or the start button on the right.</summary>
	private static void SharedBar(SpriteBatch sb, Rectangle bar, string goLabel, string goTip)
	{
		GameOverlay.MenuPanel(sb, bar, new Color(28, 32, 62) * 0.97f, PanelEdge);
		int x = ChipPicker(sb, bar.X + 12, bar.Y + 8) + 8;
		int y = bar.Y + 12;
		bool can = BetsNet.CanBet;
		if (CasinoArt.Button(sb, "bets:undo", new Rectangle(x, y, 74, 34), "Undo", PlainButton, "Takes back the last chips you put", can && BetsNet.Mine.Count > 0))
		{
			BetsNet.Undo();
		}
		x += 80;
		if (CasinoArt.Button(sb, "bets:clear", new Rectangle(x, y, 74, 34), "Clear", PlainButton, "All your chips back", can && BetsNet.Mine.Count > 0))
		{
			BetsNet.ClearBets();
		}
		x += 80;
		if (CasinoArt.Button(sb, "bets:repeat", new Rectangle(x, y, 84, 34), "Repeat", PlainButton, "The same bets as last round", can && BetsNet.Last.Count > 0))
		{
			BetsNet.Repeat();
		}
		x += 96;
		long staked = BetsNet.Staked;
		string bet = (staked > 0) ? L.F("Bet: {0}", staked.ToString("N0")) : L.T("No bets yet");
		MusicDrawing.RawText(sb, bet, x, y + 7, (staked > 0) ? Gold : Muted, 0.7f, bar.Right - x - 200);
		Rectangle go = new Rectangle(bar.Right - 180, bar.Y + 10, 168, 38);
		if (BetsNet.Waiting)
		{
			return;
		}
		if (BetsNet.Phase == BetsPhase.Betting && BetsNet.SecondsLeft < 0.0)
		{
			if (CasinoArt.Button(sb, "bets:go", go, goLabel, GoldButton, goTip, BetsNet.Hosting, staked > 0))
			{
				BetsNet.Go();
			}
		}
		else if (BetsNet.Phase == BetsPhase.Betting)
		{
			int left = (int)Math.Ceiling(BetsNet.SecondsLeft);
			GameOverlay.MenuPanel(sb, go, new Color(12, 12, 24), new Color(8, 8, 16));
			string t = L.F("{0} s to bet", left);
			float w = MusicDrawing.Measure(t, 0.76f);
			MusicDrawing.RawText(sb, t, go.Center.X - w / 2f, go.Y + 9, (left <= 5) ? CasinoArt.SoftRed : Gold, 0.76f);
		}
		else
		{
			GameOverlay.MenuPanel(sb, go, new Color(12, 12, 24), new Color(8, 8, 16));
			string t = L.T((BetsNet.Phase == BetsPhase.Result) ? "Next round soon" : "No more bets");
			float w = MusicDrawing.Measure(t, 0.76f);
			MusicDrawing.RawText(sb, t, go.Center.X - w / 2f, go.Y + 9, Muted, 0.76f);
		}
	}

	/// <summary>Who is at the table and what they have on it.</summary>
	private static void PlayersPanel(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, Panel * 0.94f, PanelEdge);
		MusicDrawing.RawText(sb, L.T("At the table"), r.X + 12, r.Y + 8, Gold, 0.66f);
		int y = r.Y + 30;
		List<int> players = new List<int> { BetsNet.Me };
		if (!BetsNet.Hosting && BetsNet.Host >= 0)
		{
			players.Add(BetsNet.Host);
		}
		players.AddRange(BetsNet.Members.Where(p => !players.Contains(p)));
		foreach (int p in players)
		{
			if (y + 18 > r.Bottom - 4)
			{
				break;
			}
			long bet = (p == BetsNet.Me) ? BetsNet.Staked : (BetsNet.Others.TryGetValue(p, out Dictionary<ulong, int> bets) ? bets.Values.Sum(v => (long)v) : 0);
			string v = (bet > 0) ? bet.ToString("N0") : "-";
			MusicDrawing.RawText(sb, GameLobby.Name(p), r.X + 12, y, (p == BetsNet.Me) ? MusicPlayerWindow.AccentColor : Cream * 0.86f, 0.62f, r.Width - 84);
			MusicDrawing.RawText(sb, v, r.Right - 12 - MusicDrawing.Measure(v, 0.62f), y, (bet > 0) ? Gold : Muted, 0.62f);
			y += 19;
		}
	}

	/// <summary>What the round paid, over the table.</summary>
	private static void ResultBanner(SpriteBatch sb, Vector2 center)
	{
		if (BetsNet.Phase != BetsPhase.Result || BetsNet.LastStake <= 0)
		{
			return;
		}
		long net = BetsNet.LastPayout - BetsNet.LastStake;
		string text = (net > 0) ? L.F("You won {0}", net.ToString("N0")) : ((net == 0) ? L.T("You got your bet back") : L.F("You lost {0}", (-net).ToString("N0")));
		Color color = (net > 0) ? Gold : ((net == 0) ? Cream : CasinoArt.SoftRed);
		float w = MusicDrawing.Measure(text, 0.92f);
		Rectangle r = new Rectangle((int)(center.X - w / 2f - 22f), (int)center.Y - 18, (int)w + 44, 36);
		GameOverlay.MenuPanel(sb, r, new Color(70, 44, 18) * 0.96f, color);
		MusicDrawing.RawText(sb, text, r.X + 22, r.Y + 8, color, 0.92f);
	}

	#endregion

	#region Slot machine

	/// <summary>What each symbol is: Terraria's fruit, bell, clover, star and diamond (0: the seven of the atlas).</summary>
	private static readonly int[] SymbolItems = { 4286, 4291, 4290, 4284, 425, 5574, 4954, 1527, 0 };

	private const float CabinetScale = 2f;

	private const int CellH = 68;

	/// <summary>How long each reel turns.</summary>
	private static readonly double[] ReelTime = { 1.1, 1.55, 2.0 };

	private static readonly int[] ReelTurns = { 21, 29, 37 };

	private static int[] _stops = { 3, 11, 19 };

	private static int[] _startStops = { 3, 11, 19 };

	private static bool _spinning;

	private static double _spinAt;

	private static int _reelsDone;

	private static List<SlotWin> _wins = new List<SlotWin>();

	private static long _lastWin;

	private static double _winAt = -100.0;

	private static int _betIndex = 2;

	private static bool _auto;

	private static double _autoAt;

	private static double _leverAt = -100.0;

	private static int SlotBet => Slots.Bets[_betIndex];

	private static void Spin()
	{
		if (_spinning)
		{
			return;
		}
		CasinoWallet.GiftIfBroke(Slots.Bets[0]);
		while (_betIndex > 0 && SlotBet > CasinoWallet.Free)
		{
			_betIndex--;
		}
		if (!CasinoWallet.ChargeBet(SlotBet))
		{
			_auto = false;
			return;
		}
		_startStops = (int[])_stops.Clone();
		_stops = Slots.Spin();
		_spinning = true;
		_spinAt = Now;
		_leverAt = Now;
		_reelsDone = 0;
		_wins = new List<SlotWin>();
		_lastWin = 0;
		SoundEngine.PlaySound(Terraria.ID.SoundID.Unlock);
	}

	private static void SlotsTick()
	{
		if (Main.gameMenu)
		{
			_auto = false;
		}
		if (_spinning)
		{
			while (_reelsDone < 3 && Now - _spinAt >= ReelTime[_reelsDone])
			{
				_reelsDone++;
				SoundEngine.PlaySound(Terraria.ID.SoundID.Grab);
			}
			if (_reelsDone == 3)
			{
				_spinning = false;
				_wins = Slots.Evaluate(_stops);
				long line = SlotBet / Slots.Lines;
				long payout = _wins.Sum(w => w.Pay * line);
				_lastWin = payout;
				_winAt = Now;
				CasinoWallet.BetResult(SlotBet, payout);
				_autoAt = Now + ((payout > 0) ? 1.6 : 0.6);
				if (payout > 0)
				{
					SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
				}
				if (payout > SlotBet)
				{
					CasinoNews.Won(CasinoGame.Slots, payout - SlotBet, _wins.Any(w => w.Symbol == Slots.Seven && w.Count == 3) ? CasinoNews.Jackpot : 0);
				}
			}
		}
		else if (_auto && Page == CasinoGame.Slots && Now >= _autoAt)
		{
			Spin();
		}
	}

	/// <summary>A symbol fitted in its cell (twice its size at most), cut to <paramref name="clip"/>.</summary>
	private static void Symbol(SpriteBatch sb, int symbol, Rectangle cell, Rectangle? clip, float alpha)
	{
		Texture2D texture;
		Rectangle frame;
		int item = SymbolItems[symbol];
		if (item == 0)
		{
			texture = Atlas;
			frame = SevenSrc;
		}
		else
		{
			texture = ItemTexture(item, out frame);
		}
		if (texture == null)
		{
			return;
		}
		float k = Math.Min(2f, Math.Min((cell.Width - 6f) / frame.Width, (cell.Height - 6f) / frame.Height));
		if (k > 1f)
		{
			k = (float)Math.Floor(k * 2f) / 2f;
		}
		int w = (int)(frame.Width * k);
		int h = (int)(frame.Height * k);
		Rectangle dest = new Rectangle(cell.Center.X - w / 2, cell.Center.Y - h / 2, w, h);
		if (clip.HasValue)
		{
			Rectangle c = clip.Value;
			int top = Math.Max(0, c.Y - dest.Y);
			int bottom = Math.Max(0, dest.Bottom - c.Bottom);
			if (top + bottom >= h)
			{
				return;
			}
			frame = new Rectangle(frame.X, frame.Y + (int)(top / k), frame.Width, frame.Height - (int)(top / k) - (int)(bottom / k));
			dest = new Rectangle(dest.X, dest.Y + top, w, h - top - bottom);
		}
		sb.Draw(texture, dest, frame, Color.White * alpha);
	}

	/// <summary>Where a reel is: its stop while still; while turning, places above it, slowing down to land on it.</summary>
	private static double ReelAt(int reel)
	{
		if (!_spinning || _reelsDone > reel)
		{
			return _stops[reel];
		}
		float t = GameArt.Clamp01((Now - _spinAt) / ReelTime[reel]);
		float e = 1f - (float)Math.Pow(1f - t, 2.4);
		return _stops[reel] + ReelTurns[reel] * (1.0 - e);
	}

	private static void DrawSlots(SpriteBatch sb, Rectangle body)
	{
		Rectangle bar = new Rectangle(body.X, body.Bottom - 58, body.Width, 58);
		Rectangle room = new Rectangle(body.X, body.Y, body.Width, bar.Y - body.Y - 4);
		CasinoArt.Room(sb, room, 0.5f);
		int cabH = (int)(Cabinet.Height * CabinetScale);
		float scale = Math.Min(CabinetScale, (room.Height - 16f) / Cabinet.Height);
		cabH = (int)(Cabinet.Height * scale);
		int cabW = (int)(Cabinet.Width * scale);
		Vector2 cab = new Vector2(room.X + 56, room.Bottom - cabH - 8);
		MusicDrawing.Fill(sb, new Rectangle((int)cab.X + 8, room.Bottom - 12, cabW - 16, 8), Color.Black * 0.4f);
		GameArt.Sharp(sb);
		if (Atlas != null)
		{
			sb.Draw(Atlas, cab, Cabinet, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		}
		Rectangle window = new Rectangle((int)(cab.X + 26 * scale), (int)(cab.Y + 84 * scale), (int)(138 * scale), (int)(102 * scale));
		int reelW = window.Width / 3;
		int cellH = window.Height / 3;
		for (int reel = 0; reel < 3; reel++)
		{
			Rectangle col = new Rectangle(window.X + reel * reelW, window.Y, reelW, window.Height);
			double at = ReelAt(reel);
			int first = (int)Math.Floor(at) - 1;
			for (int i = first; i <= first + 5; i++)
			{
				int y = (int)Math.Round(window.Y + (i - at) * cellH);
				if (y + cellH <= window.Y || y >= window.Bottom)
				{
					continue;
				}
				Rectangle cell = new Rectangle(col.X, y, reelW, cellH);
				float alpha = 1f;
				if (_wins.Count > 0 && !_spinning)
				{
					bool part = _wins.Any(w => Slots.LineRows[w.Line][reel] == i - _stops[reel] && reel < w.Count);
					alpha = part ? 1f : 0.35f;
				}
				Symbol(sb, Slots.Symbol(reel, 0, i), cell, window, alpha);
			}
			if (reel > 0)
			{
				MusicDrawing.Fill(sb, new Rectangle(col.X - 1, col.Y, 2, col.Height), new Color(190, 170, 130));
			}
		}
		// the shade of a round reel, top and bottom
		for (int i = 0; i < 12; i++)
		{
			float a = 0.26f * (1f - i / 12f);
			MusicDrawing.Fill(sb, new Rectangle(window.X, window.Y + i, window.Width, 1), Color.Black * a);
			MusicDrawing.Fill(sb, new Rectangle(window.X, window.Bottom - 1 - i, window.Width, 1), Color.Black * a);
		}
		GameArt.Smooth(sb);
		// the lines that paid
		if (!_spinning && _wins.Count > 0)
		{
			double t = Now - _winAt;
			SlotWin win = _wins[(int)(t / 1.1) % _wins.Count];
			Color lineColor = Color.Lerp(Gold, Color.White, GameArt.Pulse(0.5));
			Vector2 Spot(int reel) => new Vector2(window.X + reel * reelW + reelW / 2f, window.Y + Slots.LineRows[win.Line][reel] * cellH + cellH / 2f);
			GameArt.Line(sb, new Vector2(window.X + 2, Spot(0).Y), Spot(0), 4f, lineColor);
			GameArt.Line(sb, Spot(0), Spot(1), 4f, lineColor);
			GameArt.Line(sb, Spot(1), Spot(2), 4f, lineColor);
			GameArt.Line(sb, Spot(2), new Vector2(window.Right - 2, Spot(2).Y), 4f, lineColor);
		}
		DrawBulbs(sb, cab, scale);
		DrawLever(sb, new Vector2(cab.X + cabW - 2, cab.Y + 118 * scale));
		// what you won, on the machine's tray
		Rectangle tray = new Rectangle((int)(cab.X + 30 * scale), (int)(cab.Y + 216 * scale), (int)(130 * scale), (int)(12 * scale));
		if (_lastWin > 0 && !_spinning)
		{
			string won = "+" + _lastWin.ToString("N0");
			float w = MusicDrawing.Measure(won, 0.7f);
			MusicDrawing.RawText(sb, won, tray.Center.X - w / 2f, tray.Y + 1, Gold, 0.7f);
		}
		DrawCoins(sb, new Vector2(tray.Center.X, tray.Y), scale);
		Banner(sb, new Vector2(cab.X + cabW / 2f, window.Y + window.Height / 2f));
		PayTable(sb, new Rectangle((int)cab.X + cabW + 76, room.Y + 12, room.Right - ((int)cab.X + cabW + 76) - 12, room.Height - 24));
		SlotsBar(sb, bar);
	}

	/// <summary>Big wins say so over the reels for a moment.</summary>
	private static void Banner(SpriteBatch sb, Vector2 center)
	{
		if (_spinning || Now - _winAt > 2.6 || _lastWin < SlotBet * 5L)
		{
			return;
		}
		bool jackpot = _wins.Any(w => w.Symbol == Slots.Seven && w.Count == 3);
		string text = L.T(jackpot ? "JACKPOT!" : ((_lastWin >= SlotBet * 20L) ? "BIG WIN!" : "NICE WIN!"));
		float k = GameArt.EaseBack(GameArt.Clamp01((Now - _winAt) / 0.3));
		float scale = 1.25f * k;
		float w = MusicDrawing.Measure(text, scale);
		Rectangle r = new Rectangle((int)(center.X - w / 2f - 20f), (int)center.Y - 24, (int)w + 40, 48);
		GameOverlay.MenuPanel(sb, r, new Color(90, 20, 40) * 0.96f, Gold);
		MusicDrawing.RawText(sb, text, r.X + 20, r.Y + 10, Color.Lerp(Gold, Color.White, GameArt.Pulse(0.4)), scale);
	}

	/// <summary>The bulbs around the sign: they take turns, and chase each other when it pays.</summary>
	private static void DrawBulbs(SpriteBatch sb, Vector2 cab, float scale)
	{
		Rectangle sign = new Rectangle((int)(cab.X + 14 * scale), (int)(cab.Y + 14 * scale), (int)(162 * scale), (int)(50 * scale));
		bool party = !_spinning && _lastWin > 0 && Now - _winAt < 3.0;
		double speed = party ? 14.0 : (_spinning ? 8.0 : 2.0);
		int phase = (int)(Now * speed);
		int n = 0;
		void Bulb(int x, int y)
		{
			bool on = (n + phase) % 3 == 0;
			n++;
			MusicDrawing.Fill(sb, new Rectangle(x - 3, y - 3, 6, 6), new Color(40, 20, 50));
			MusicDrawing.Fill(sb, new Rectangle(x - 2, y - 2, 4, 4), on ? new Color(255, 236, 150) : new Color(150, 90, 40));
		}
		for (int x = sign.X + 10; x < sign.Right - 6; x += 18)
		{
			Bulb(x, sign.Y - 8);
		}
		for (int y = sign.Y; y < sign.Bottom; y += 18)
		{
			Bulb(sign.Right + 8, y);
		}
		for (int x = sign.Right - 10; x > sign.X + 6; x -= 18)
		{
			Bulb(x, sign.Bottom + 8);
		}
		for (int y = sign.Bottom - 2; y > sign.Y; y -= 18)
		{
			Bulb(sign.X - 8, y);
		}
	}

	private static void DrawLever(SpriteBatch sb, Vector2 at)
	{
		float pull = GameArt.Clamp01((Now - _leverAt) / 0.18);
		float back = GameArt.Clamp01((Now - _leverAt - 0.18) / 0.4);
		float down = (Now - _leverAt < 0.6) ? (pull * (1f - back)) : 0f;
		Vector2 top = at + new Vector2(36f, -92f + down * 150f);
		// the axle, the arm, the ball
		MusicDrawing.Fill(sb, new Rectangle((int)at.X - 2, (int)at.Y - 12, 22, 24), new Color(16, 14, 26));
		MusicDrawing.Fill(sb, new Rectangle((int)at.X, (int)at.Y - 10, 18, 20), new Color(120, 84, 26));
		MusicDrawing.Fill(sb, new Rectangle((int)at.X, (int)at.Y - 10, 18, 4), Gold);
		GameArt.Line(sb, at + new Vector2(16f, 0f), top, 8f, new Color(16, 14, 26));
		GameArt.Line(sb, at + new Vector2(16f, 0f), top, 5f, new Color(208, 208, 224));
		GameArt.Sharp(sb);
		Rectangle knob = new Rectangle((int)top.X - 13, (int)top.Y - 13, 26, 26);
		Texture2D texture = ItemTexture(Terraria.ID.ItemID.LifeCrystal, out Rectangle frame);
		sb.Draw(texture, knob, frame, Color.White);
		GameArt.Smooth(sb);
		bool hover = MusicPlayerWindow.MouseIn(new Rectangle(knob.X - 8, knob.Y - 8, knob.Width + 16, knob.Height + 16));
		if (hover && !_spinning)
		{
			MusicDrawing.Hover("bets:lever", L.T("Pull to spin"));
			if (MusicPlayerWindow.Clicked)
			{
				MusicPlayerWindow.Clicked = false;
				Spin();
			}
		}
	}

	/// <summary>Coins jumping out of the tray when it pays.</summary>
	private static void DrawCoins(SpriteBatch sb, Vector2 tray, float scale)
	{
		double t = Now - _winAt;
		if (_spinning || _lastWin <= 0 || t > 1.8)
		{
			return;
		}
		int count = (int)Math.Min(26.0, 4.0 + _lastWin / (double)Math.Max(1, SlotBet) * 3.0);
		GameArt.Sharp(sb);
		Texture2D texture = ItemTexture(Terraria.ID.ItemID.GoldCoin, out Rectangle frame);
		for (int i = 0; i < count; i++)
		{
			double start = (i % 9) * 0.12;
			double u = t - start;
			if (u < 0.0 || u > 1.1)
			{
				continue;
			}
			double side = ((i * 37) % 21 - 10) * 11.0;
			double up = 190.0 + (i * 53) % 80;
			Vector2 p = tray + new Vector2((float)(side * u), (float)(-up * u + 300.0 * u * u));
			sb.Draw(texture, p, frame, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
		}
		GameArt.Smooth(sb);
	}

	private static void PayTable(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, Panel * 0.95f, PanelEdge);
		MusicDrawing.RawText(sb, L.T("What it pays"), r.X + 14, r.Y + 10, Gold, 0.82f);
		string note = L.F("{0} a line", (SlotBet / Slots.Lines).ToString("N0"));
		MusicDrawing.RawText(sb, note, r.Right - 14 - MusicDrawing.Measure(note, 0.62f), r.Y + 13, Muted, 0.62f);
		int y = r.Y + 40;
		long line = SlotBet / Slots.Lines;
		int rowH = Math.Min(38, (r.Height - 116) / 10);
		for (int s = Slots.Symbols - 1; s >= 0; s--)
		{
			DrawPayRow(sb, r, y, rowH, s, 3, Slots.Pays[s] * line);
			y += rowH;
		}
		DrawPayRow(sb, r, y, rowH, Slots.Cherry, 2, Slots.TwoCherries * line);
		y += rowH + 8;
		// the five lines, as little boards
		int gx = r.X + 14;
		for (int l = 0; l < Slots.Lines; l++)
		{
			Rectangle g = new Rectangle(gx, y, 42, 30);
			MusicDrawing.Fill(sb, g, new Color(14, 16, 34));
			for (int reel = 0; reel < 3; reel++)
			{
				MusicDrawing.Fill(sb, new Rectangle(g.X + 4 + reel * 12, g.Y + 3 + Slots.LineRows[l][reel] * 9, 10, 6), Gold);
			}
			gx += 48;
		}
		MusicDrawing.RawText(sb, L.T("Lines"), gx + 6, y + 6, Muted, 0.62f);
	}

	private static void DrawPayRow(SpriteBatch sb, Rectangle r, int y, int h, int symbol, int count, long chips)
	{
		bool lit = !_spinning && _wins.Any(w => w.Symbol == symbol && w.Count == count);
		if (lit)
		{
			MusicDrawing.Fill(sb, new Rectangle(r.X + 8, y, r.Width - 16, h - 2), Gold * 0.22f);
		}
		GameArt.Sharp(sb);
		for (int i = 0; i < count; i++)
		{
			Symbol(sb, symbol, new Rectangle(r.X + 14 + i * 36, y - 2, 36, h + 2), null, 1f);
		}
		GameArt.Smooth(sb);
		string text = chips.ToString("N0");
		MusicDrawing.RawText(sb, text, r.Right - 16 - MusicDrawing.Measure(text, 0.72f), y + h / 2f - 9f, (symbol == Slots.Seven) ? Gold : Cream, 0.72f);
	}

	private static void SlotsBar(SpriteBatch sb, Rectangle bar)
	{
		GameOverlay.MenuPanel(sb, bar, new Color(28, 32, 62) * 0.97f, PanelEdge);
		int x = bar.X + 14;
		int y = bar.Y + 12;
		MusicDrawing.RawText(sb, L.T("Bet"), x, y + 7, Muted, 0.68f);
		x += (int)MusicDrawing.Measure(L.T("Bet"), 0.68f) + 12;
		for (int i = 0; i < Slots.Bets.Length; i++)
		{
			string label = Slots.Bets[i].ToString("N0");
			int w = (int)MusicDrawing.Measure(label, 0.72f) + 24;
			Rectangle r = new Rectangle(x, y, w, 34);
			bool on = i == _betIndex;
			bool hover = MusicPlayerWindow.MouseIn(r);
			GameOverlay.MenuPanel(sb, r, on ? new Color(70, 80, 150) : (hover ? new Color(46, 54, 104) : new Color(30, 36, 70)), on ? Gold : (hover ? Gold * 0.5f : PanelEdge));
			MusicDrawing.RawText(sb, label, r.X + 12, r.Y + 7, on ? Color.White : Cream * 0.8f, 0.72f);
			if (hover && !_spinning && MusicPlayerWindow.Clicked)
			{
				MusicPlayerWindow.Clicked = false;
				_betIndex = i;
				SoundEngine.PlaySound(12);
			}
			x += w + 4;
		}
		Rectangle spin = new Rectangle(bar.Right - 170, bar.Y + 10, 158, 38);
		if (CasinoArt.Button(sb, "bets:spin", spin, "Spin", GoldButton, L.F("{0} chips, all five lines", SlotBet.ToString("N0")), !_spinning))
		{
			Spin();
		}
		Rectangle auto = new Rectangle(spin.X - 92, bar.Y + 12, 84, 34);
		if (CasinoArt.Button(sb, "bets:auto", auto, _auto ? "Stop" : "Auto", _auto ? RedButton : PlainButton, "Keeps spinning until you stop it or the chips run out"))
		{
			_auto = !_auto;
			_autoAt = Now;
		}
	}

	#endregion

	#region Roulette

	private const float WheelTurns = 6f;

	/// <summary>The wheel, its numbers turned by <paramref name="turn"/> (radians).</summary>
	private static void Wheel(SpriteBatch sb, Vector2 center, float size, float turn)
	{
		if (Atlas == null)
		{
			return;
		}
		float k = size / 320f;
		sb.Draw(Atlas, center, Bowl, Color.White, 0f, new Vector2(160f, 160f), k, SpriteEffects.None, 0f);
		sb.Draw(Atlas, center, Rotor, Color.White, turn, new Vector2(160f, 160f), k * 0.9f, SpriteEffects.None, 0f);
		sb.Draw(Atlas, center, Turret, Color.White, turn, new Vector2(17f, 17f), k * 1.7f, SpriteEffects.None, 0f);
	}

	/// <summary>How far the wheel has gone at this moment, and how far the round's spin is (0..1).</summary>
	private static float SpinOf()
	{
		if (BetsNet.Phase == BetsPhase.Running)
		{
			return GameArt.Clamp01((Now - BetsNet.RunStart) / (BetsNet.RunSeconds - 0.4));
		}
		return (BetsNet.Phase == BetsPhase.Result) ? 1f : 0f;
	}

	private static float PocketAngle(int number)
	{
		int i = Array.IndexOf(Roulette.Wheel, number);
		return (float)(-Math.PI / 2.0 + i * Math.PI * 2.0 / 37.0);
	}

	private static double _ballSound;

	private static void DrawWheelGame(SpriteBatch sb, Vector2 center, float size)
	{
		float u = SpinOf();
		// the wheel turns slowly all the time; a spin adds whole turns that slow down to nothing
		float extra = WheelTurns * (float)Math.PI * 2f * (1f - (1f - u) * (1f - u));
		float turn = (float)(Now * 0.3) + extra;
		Wheel(sb, center, size, turn);
		if (BetsNet.Phase == BetsPhase.Betting || Atlas == null)
		{
			return;
		}
		float k = size / 320f;
		// around the rim against the wheel, then it drops and hops into its pocket and goes round with it
		float s = Math.Min(1f, u / 0.84f);
		float left = 1f - s;
		float rel = PocketAngle(BetsNet.Outcome) - 34f * left * left * left - 0.0f;
		float rim = 140f * k;
		float pocket = 94f * k;
		float drop = GameArt.Clamp01((s - 0.62f) / 0.38f);
		float hop = (float)(Math.Abs(Math.Sin(drop * Math.PI * 3.0)) * (1f - drop) * 12f * k);
		float radius = MathHelper.Lerp(rim, pocket, drop * drop) + hop;
		float angle = turn + rel;
		Vector2 at = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
		sb.Draw(Atlas, at, Ball, Color.White, 0f, new Vector2(5f, 5f), Math.Max(0.9f, k * 1.2f), SpriteEffects.None, 0f);
		if (u < 1f && drop > 0f && Now - _ballSound > 0.22 && hop > 1f)
		{
			_ballSound = Now;
			SoundEngine.PlaySound(12);
		}
	}

	private const int BoardCols = 12;

	private static Color NumberColor(int n) => (n == 0) ? new Color(30, 130, 76) : (Roulette.IsRed(n) ? new Color(176, 34, 46) : new Color(26, 26, 36));

	private static int NumberAt(int col, int row) => col * 3 + (3 - row);

	/// <summary>The felt's squares: where a number and each outside bet are.</summary>
	private sealed class Board
	{
		public Rectangle Grid;

		public int Cell;

		public int CellH;

		public Rectangle Zero;

		public readonly List<(Rectangle Area, ulong Mask, string Label, Color Color)> Outside = new List<(Rectangle, ulong, string, Color)>();

		public Vector2 Center(int n)
		{
			if (n == 0)
			{
				return new Vector2(Zero.Center.X, Zero.Center.Y);
			}
			int col = (n - 1) / 3;
			int row = 2 - (n - 1) % 3;
			return new Vector2(Grid.X + col * Cell + Cell / 2f, Grid.Y + row * CellH + CellH / 2f);
		}

		/// <summary>Where the chips of a bet stand.</summary>
		public Vector2 Spot(ulong mask)
		{
			foreach ((Rectangle area, ulong m, string _, Color _) in Outside)
			{
				if (m == mask)
				{
					return new Vector2(area.Center.X, area.Center.Y);
				}
			}
			Vector2 sum = Vector2.Zero;
			int count = 0;
			for (int n = 0; n <= 36; n++)
			{
				if ((mask & (1UL << n)) != 0)
				{
					sum += Center(n);
					count++;
				}
			}
			Vector2 spot = sum / Math.Max(1, count);
			// a street's chips stand on the edge of the bottom row
			return (count == 3) ? new Vector2(spot.X, Grid.Bottom) : spot;
		}
	}

	private static Board MakeBoard(int x, int y, int cell, int cellH)
	{
		Board b = new Board { Cell = cell, CellH = cellH };
		int zeroW = 36;
		b.Zero = new Rectangle(x, y, zeroW, cellH * 3);
		b.Grid = new Rectangle(x + zeroW, y, cell * BoardCols, cellH * 3);
		Color felt = new Color(26, 112, 68);
		for (int i = 0; i < 3; i++)
		{
			// the rows' 2 to 1, at the end of each
			b.Outside.Add((new Rectangle(b.Grid.Right, y + (2 - i) * cellH, 44, cellH), Roulette.Column(i), "2:1", felt));
		}
		int outY = b.Grid.Bottom;
		string[] dozens = { "1st 12", "2nd 12", "3rd 12" };
		for (int i = 0; i < 3; i++)
		{
			b.Outside.Add((new Rectangle(b.Grid.X + i * cell * 4, outY, cell * 4, 34), Roulette.Dozen(i), dozens[i], felt));
		}
		int w = cell * 2;
		(ulong, string, Color)[] even =
		{
			(Roulette.Low, "1-18", felt), (Roulette.Even, "Even", felt), (Roulette.Red, "Red", new Color(176, 34, 46)),
			(Roulette.Black, "Black", new Color(26, 26, 36)), (Roulette.Odd, "Odd", felt), (Roulette.High, "19-36", felt)
		};
		for (int i = 0; i < even.Length; i++)
		{
			b.Outside.Add((new Rectangle(b.Grid.X + i * w, outY + 34, w, 34), even[i].Item1, even[i].Item2, even[i].Item3));
		}
		return b;
	}

	/// <summary>The bet under the mouse: near a line it covers both sides, near a corner four, at the bottom edge the whole street.</summary>
	private static ulong BetUnderMouse(Board b)
	{
		Point m = MusicPlayerWindow.MousePoint;
		if (b.Zero.Contains(m))
		{
			return Roulette.Of(0);
		}
		foreach ((Rectangle area, ulong mask, string _, Color _) in b.Outside)
		{
			if (area.Contains(m))
			{
				return mask;
			}
		}
		if (!b.Grid.Contains(m))
		{
			return 0;
		}
		int col = (m.X - b.Grid.X) / b.Cell;
		int row = (m.Y - b.Grid.Y) / b.CellH;
		float fx = (m.X - b.Grid.X - col * b.Cell) / (float)b.Cell;
		float fy = (m.Y - b.Grid.Y - row * b.CellH) / (float)b.CellH;
		int dx = (fx < 0.2f) ? -1 : ((fx > 0.8f) ? 1 : 0);
		int dy = (fy < 0.2f) ? -1 : ((fy > 0.8f) ? 1 : 0);
		if (dy == 1 && row == 2 && dx == 0)
		{
			return Roulette.Of(col * 3 + 1, col * 3 + 2, col * 3 + 3);
		}
		int ncol = col + dx;
		int nrow = row + dy;
		bool colOk = ncol >= 0 && ncol < BoardCols;
		bool rowOk = nrow >= 0 && nrow < 3;
		if (dx != 0 && dy != 0 && colOk && rowOk)
		{
			return Roulette.Of(NumberAt(col, row), NumberAt(ncol, row), NumberAt(col, nrow), NumberAt(ncol, nrow));
		}
		if (dx != 0 && dy == 0 && colOk)
		{
			return Roulette.Of(NumberAt(col, row), NumberAt(ncol, row));
		}
		if (dy != 0 && dx == 0 && rowOk)
		{
			return Roulette.Of(NumberAt(col, row), NumberAt(col, nrow));
		}
		return Roulette.Of(NumberAt(col, row));
	}

	private static void DrawRoulette(SpriteBatch sb, Rectangle body)
	{
		Rectangle bar = new Rectangle(body.X, body.Bottom - 58, body.Width, 58);
		Rectangle room = new Rectangle(body.X, body.Y, body.Width, bar.Y - body.Y - 4);
		CasinoArt.Room(sb, room, 0.16f);
		Rectangle table = new Rectangle(room.X + 8, room.Y + 56, room.Width - 16, room.Height - 62);
		GameOverlay.MenuPanel(sb, table, new Color(92, 54, 30), new Color(40, 22, 12));
		Rectangle felt = new Rectangle(table.X + 10, table.Y + 10, table.Width - 20, table.Height - 20);
		GameArt.Gradient(sb, felt, new Color(34, 132, 82), new Color(22, 96, 60));
		GameArt.Frame(sb, felt, new Color(14, 60, 38), 2);
		float size = Math.Min(340f, felt.Height - 150f);
		Vector2 center = new Vector2(felt.X + 14 + size / 2f, felt.Y + 12 + size / 2f);
		DrawWheelGame(sb, center, size);
		PlayersPanel(sb, new Rectangle(felt.X + 12, (int)(center.Y + size / 2f) + 10, (int)size + 4, felt.Bottom - (int)(center.Y + size / 2f) - 20));
		int bx = (int)(felt.X + size + 34);
		int cell = (felt.Right - 12 - bx - 36 - 44) / BoardCols;
		Board board = MakeBoard(bx, felt.Y + 96, cell, 76);
		DrawHistory(sb, new Rectangle(bx, room.Y + 12, felt.Right - bx, 36));
		DrawRouletteStatus(sb, new Rectangle(bx, felt.Y + 14, felt.Right - bx - 12, 70));
		DrawBoard(sb, board);
		ResultBanner(sb, new Vector2(board.Grid.Center.X, board.Grid.Bottom + 96));
		SharedBar(sb, bar, "Spin", "Throw the ball");
	}

	/// <summary>The last numbers, newest first.</summary>
	private static void DrawHistory(SpriteBatch sb, Rectangle r)
	{
		int x = r.X;
		foreach (int n in BetsNet.History.Take(14))
		{
			Rectangle c = new Rectangle(x, r.Y + 2, 32, 30);
			GameOverlay.MenuPanel(sb, c, NumberColor(n), PanelEdge);
			string t = n.ToString();
			MusicDrawing.RawText(sb, t, c.Center.X - MusicDrawing.Measure(t, 0.68f) / 2f, c.Y + 6, Color.White, 0.68f);
			x += 35;
			if (x + 32 > r.Right)
			{
				break;
			}
		}
		if (BetsNet.History.Count == 0)
		{
			MusicDrawing.RawText(sb, L.T("No numbers yet"), r.X, r.Y + 8, Muted, 0.66f);
		}
	}

	private static void DrawRouletteStatus(SpriteBatch sb, Rectangle r)
	{
		string text;
		Color color = Cream;
		if (BetsNet.Waiting)
		{
			text = L.T("Finding the table...");
		}
		else if (BetsNet.Phase == BetsPhase.Betting)
		{
			text = L.T("Place your bets");
			color = Gold;
		}
		else if (BetsNet.Phase == BetsPhase.Running && SpinOf() < 1f)
		{
			text = L.T("No more bets");
			color = Muted;
		}
		else
		{
			int n = BetsNet.Outcome;
			text = n + "  " + L.T((n == 0) ? "Zero" : (Roulette.IsRed(n) ? "Red" : "Black"));
			color = (n == 0) ? CasinoArt.SoftGreen : (Roulette.IsRed(n) ? CasinoArt.SoftRed : Color.White);
		}
		MusicDrawing.RawText(sb, text, r.X, r.Y, color, 1.1f, r.Width);
		MusicDrawing.RawText(sb, L.T("Click a number, a line between two, a corner or an outside bet. Right click takes it off."), r.X, r.Y + 36, Cream * 0.75f, 0.58f, r.Width);
	}

	private static void DrawBoard(SpriteBatch sb, Board b)
	{
		ulong hover = BetsNet.CanBet ? BetUnderMouse(b) : 0;
		bool lit = BetsNet.Phase == BetsPhase.Result;
		int won = BetsNet.Outcome;
		void Square(Rectangle area, Color color, string label, float scale, ulong mask)
		{
			bool over = hover != 0 && (hover & mask) == mask && mask != 0;
			bool winner = lit && (mask & (1UL << won)) != 0 && Roulette.Covers(mask) == 1;
			MusicDrawing.Fill(sb, area, Color.Lerp(color, Color.White, over ? 0.3f : 0f));
			GameArt.Frame(sb, area, new Color(236, 224, 190) * 0.7f, 1);
			if (winner)
			{
				GameArt.Frame(sb, area, Color.Lerp(Gold, Color.White, GameArt.Pulse(0.6)), 3);
			}
			float w = MusicDrawing.Measure(label, scale);
			MusicDrawing.RawText(sb, label, (float)Math.Round(area.Center.X - w / 2f), (float)Math.Round(area.Center.Y - 11f * scale / 0.8f), Color.White, scale);
		}
		Square(b.Zero, NumberColor(0), "0", 0.84f, Roulette.Of(0));
		for (int col = 0; col < BoardCols; col++)
		{
			for (int row = 0; row < 3; row++)
			{
				int n = NumberAt(col, row);
				Square(new Rectangle(b.Grid.X + col * b.Cell, b.Grid.Y + row * b.CellH, b.Cell, b.CellH), NumberColor(n), n.ToString(), 0.8f, Roulette.Of(n));
			}
		}
		foreach ((Rectangle area, ulong mask, string label, Color color) in b.Outside)
		{
			bool over = hover == mask;
			MusicDrawing.Fill(sb, area, Color.Lerp(color, Color.White, over ? 0.3f : 0f));
			GameArt.Frame(sb, area, new Color(236, 224, 190) * 0.7f, 1);
			bool outsideWon = lit && (mask & (1UL << won)) != 0;
			if (outsideWon)
			{
				GameArt.Frame(sb, area, Gold, 2);
			}
			string text = L.T(label);
			float w = MusicDrawing.Measure(text, 0.66f);
			MusicDrawing.RawText(sb, text, (float)Math.Round(area.Center.X - w / 2f), area.Center.Y - 9f, Color.White, 0.66f);
		}
		foreach (KeyValuePair<int, Dictionary<ulong, int>> other in BetsNet.Others)
		{
			foreach (KeyValuePair<ulong, int> bet in other.Value)
			{
				BetChip(sb, b.Spot(bet.Key) + new Vector2(6f, 6f), bet.Value, 0.6f);
			}
		}
		foreach (KeyValuePair<ulong, int> bet in BetsNet.Mine)
		{
			BetChip(sb, b.Spot(bet.Key), bet.Value);
		}
		if (hover != 0)
		{
			int covers = Roulette.Covers(hover);
			MusicDrawing.Hover("bets:board:" + hover, L.F("Pays {0} to 1", 36 / covers - 1));
			if (MusicPlayerWindow.Clicked)
			{
				MusicPlayerWindow.Clicked = false;
				BetsNet.Place(hover, BetsLimits.Chips[_chip]);
			}
			else if (MusicPlayerWindow.RightClicked)
			{
				BetsNet.Remove(hover);
			}
		}
	}

	#endregion

	#region Horse races

	private const float TrackLength = 3000f;

	private static readonly Texture2D[] _horses = new Texture2D[3];

	private static Texture2D HorseSheet(int kind)
	{
		if (_horses[kind] == null)
		{
			_horses[kind] = Main.Assets.Request<Texture2D>("Images/Extra_" + (161 + kind), ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
		}
		return _horses[kind];
	}

	/// <summary>The coats: Terraria's three horses and the same three in other colours.</summary>
	private static Color Coat(int horse) => (horse < 3) ? Color.White : new[] { new Color(255, 214, 170), new Color(190, 200, 255), new Color(170, 150, 150) }[horse - 3];

	/// <summary>A horse with its hooves at <paramref name="feet"/> (bottom middle): standing, or galloping.</summary>
	private static void DrawHorse(SpriteBatch sb, int horse, Vector2 feet, bool running, float scale)
	{
		Texture2D sheet = HorseSheet(horse % 3);
		int frame = running ? 9 + (int)((Now * 13.0 + horse * 1.7) % 7.0) : 0;
		Rectangle src = new Rectangle(0, frame * 82, 126, 80);
		sb.Draw(sheet, new Vector2((float)Math.Round(feet.X), (float)Math.Round(feet.Y)), src, Coat(horse), 0f, new Vector2(70f, 76f), scale, SpriteEffects.None, 0f);
	}

	private static double RaceTime => (BetsNet.Phase == BetsPhase.Betting) ? 0.0 : (Now - BetsNet.RunStart);

	private static void DrawHorses(SpriteBatch sb, Rectangle body)
	{
		Rectangle bar = new Rectangle(body.X, body.Bottom - 58, body.Width, 58);
		Rectangle top = new Rectangle(body.X, body.Y, body.Width, 262);
		DrawTrack(sb, top);
		Rectangle under = new Rectangle(body.X, top.Bottom + 4, body.Width, bar.Y - top.Bottom - 8);
		CasinoArt.Room(sb, under, 0f);
		int w = 620;
		DrawField(sb, new Rectangle(under.X + 6, under.Y + 6, w, under.Height - 12));
		Rectangle side = new Rectangle(under.X + w + 14, under.Y + 6, under.Width - w - 20, under.Height - 12);
		PlayersPanel(sb, new Rectangle(side.X, side.Y, side.Width, side.Height - 92));
		DrawWinners(sb, new Rectangle(side.X, side.Bottom - 84, side.Width, 84));
		SharedBar(sb, bar, "Start the race", "They run now");
	}

	/// <summary>The stands, the rail and the turf going by, the camera with the leaders.</summary>
	private static void DrawTrack(SpriteBatch sb, Rectangle r)
	{
		HorseRace race = BetsNet.Race;
		double t = RaceTime;
		bool running = BetsNet.Phase != BetsPhase.Betting && race?.Order != null;
		float lead = 0f;
		if (running)
		{
			for (int h = 0; h < HorseRace.Horses; h++)
			{
				lead = Math.Max(lead, (float)race.Position(h, t));
			}
		}
		float cam = Math.Min(lead * TrackLength - r.Width * 0.66f, TrackLength + 110f - r.Width * 0.66f);
		cam = Math.Max(-120f, cam);
		int skyH = 58;
		GameArt.Gradient(sb, new Rectangle(r.X, r.Y, r.Width, skyH + 40), new Color(96, 170, 236), new Color(190, 226, 246));
		GameArt.Sharp(sb);
		Strip(sb, Hills, new Rectangle(r.X, r.Y + 12, r.Width, 64), cam * 0.15f);
		Strip(sb, Stands, new Rectangle(r.X, r.Y + 34, r.Width, 96), cam * 0.5f);
		int ground = r.Y + 130;
		GameArt.Smooth(sb);
		MusicDrawing.Fill(sb, new Rectangle(r.X, ground, r.Width, r.Bottom - ground), new Color(84, 168, 86));
		// mown stripes that go by
		float stripe = 120f;
		for (float x = -((cam % (stripe * 2f) + stripe * 2f) % (stripe * 2f)); x < r.Width; x += stripe * 2f)
		{
			int a = Math.Max(0, (int)x);
			int b = Math.Min(r.Width, (int)(x + stripe));
			if (b > a)
			{
				MusicDrawing.Fill(sb, new Rectangle(r.X + a, ground, b - a, r.Bottom - ground), new Color(74, 154, 78));
			}
		}
		GameArt.Sharp(sb);
		Strip(sb, Rail, new Rectangle(r.X, ground - 12, r.Width, 20), cam);
		float finish = TrackLength - cam;
		if (finish > -40f && finish < r.Width + 40f)
		{
			// the line and its posts
			for (int y = ground + 6; y < r.Bottom; y += 8)
			{
				for (int i = 0; i < 2; i++)
				{
					Rectangle sq = new Rectangle(r.X + (int)finish + i * 8, y, 8, 8);
					if (sq.X >= r.X && sq.Right <= r.Right)
					{
						MusicDrawing.Fill(sb, new Rectangle(sq.X, Math.Min(sq.Y, r.Bottom - 8), 8, Math.Min(8, r.Bottom - sq.Y)), (((y / 8) + i) % 2 == 0) ? Color.White : new Color(24, 24, 30));
					}
				}
			}
			Px(sb, Post, new Vector2(r.X + finish - 4f, ground - 66f), 1f);
			Px(sb, Flag, new Vector2(r.X + finish + 10f, ground - 66f), 1f);
		}
		if (race != null)
		{
			float gate = 0f - cam;
			for (int lane = 0; lane < HorseRace.Horses; lane++)
			{
				float y = ground + 26f + lane * 19f;
				float x = (running ? (float)race.Position(lane, t) * TrackLength : 0f) - cam;
				if (gate + 40f > -30f)
				{
					Px(sb, Gate, new Vector2(r.X + gate + 30f, y - 66f), 1f);
				}
				if (x > -90f && x < r.Width + 90f)
				{
					DrawHorse(sb, lane, new Vector2(r.X + x, y), running && (float)race.Position(lane, t) < 1.045f, 0.8f);
					CasinoArt.Seated(sb, CasinoArt.Bot(lane * 3), new Vector2(r.X + x - 12f, y - 24f), true, 0.8f, CasinoWindow.Resume);
					GameArt.Sharp(sb);
				}
			}
			GameArt.Smooth(sb);
			for (int lane = 0; lane < HorseRace.Horses; lane++)
			{
				float y = ground + 26f + lane * 19f;
				float x = (running ? (float)race.Position(lane, t) * TrackLength : 0f) - cam;
				if (x > -30f && x < r.Width + 30f)
				{
					Saddle(sb, lane, new Vector2(r.X + x - 14f, y - 28f));
				}
			}
		}
		GameArt.Smooth(sb);
		GameArt.Frame(sb, r, PanelEdge, 2);
		TrackMap(sb, new Rectangle(r.X + 12, r.Y + 8, r.Width - 24, 22), race, t, running);
		if (BetsNet.Phase == BetsPhase.Result && race?.Order != null)
		{
			Podium(sb, new Vector2(r.Center.X, r.Y + 96), race);
		}
		else if (BetsNet.Phase == BetsPhase.Betting)
		{
			string text = L.T(BetsNet.Waiting ? "Finding the table..." : "Place your bets");
			float w = MusicDrawing.Measure(text, 1f);
			Rectangle p = new Rectangle((int)(r.Center.X - w / 2f - 20f), r.Y + 40, (int)w + 40, 36);
			GameOverlay.MenuPanel(sb, p, new Color(20, 24, 48) * 0.92f, Gold);
			MusicDrawing.RawText(sb, text, p.X + 20, p.Y + 8, Gold, 1f);
		}
		ResultBanner(sb, new Vector2(r.Center.X, r.Bottom - 30));
	}

	/// <summary>A horse's number on its cloth, in its rider's colour.</summary>
	private static void Saddle(SpriteBatch sb, int horse, Vector2 center)
	{
		(byte R, byte G, byte B) silk = HorseRace.Silks[horse];
		Color color = new Color(silk.R, silk.G, silk.B);
		Rectangle r = new Rectangle((int)center.X - 10, (int)center.Y - 9, 20, 18);
		MusicDrawing.Fill(sb, r, new Color(12, 12, 20));
		MusicDrawing.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), color);
		string n = (horse + 1).ToString();
		MusicDrawing.RawText(sb, n, r.Center.X - MusicDrawing.Measure(n, 0.6f) / 2f, r.Y + 1, Color.White, 0.6f);
	}

	/// <summary>The whole race as a little line: where each horse is.</summary>
	private static void TrackMap(SpriteBatch sb, Rectangle r, HorseRace race, double t, bool running)
	{
		if (!running || race == null || BetsNet.Phase != BetsPhase.Running)
		{
			return;
		}
		Rectangle line = new Rectangle(r.X + 12, r.Y + 9, r.Width - 24, 4);
		MusicDrawing.Fill(sb, new Rectangle(line.X - 1, line.Y - 1, line.Width + 2, line.Height + 2), new Color(12, 12, 20) * 0.8f);
		MusicDrawing.Fill(sb, line, Cream * 0.9f);
		foreach (int h in race.Standing(t).AsEnumerable().Reverse())
		{
			float at = Math.Min(1f, (float)race.Position(h, t));
			Saddle(sb, h, new Vector2(line.X + at * line.Width, line.Y + 2));
		}
	}

	/// <summary>The first three, with their trophies.</summary>
	private static void Podium(SpriteBatch sb, Vector2 center, HorseRace race)
	{
		Rectangle r = new Rectangle((int)center.X - 250, (int)center.Y - 60, 500, 120);
		GameOverlay.MenuPanel(sb, r, new Color(20, 24, 48) * 0.95f, Gold);
		int[] trophies = { Terraria.ID.ItemID.GolfTrophyGold, Terraria.ID.ItemID.GolfTrophySilver, Terraria.ID.ItemID.GolfTrophyBronze };
		for (int place = 0; place < 3; place++)
		{
			int h = race.Order[place];
			int x = r.X + 14 + place * 160;
			GameArt.Sharp(sb);
			Texture2D texture = ItemTexture(trophies[place], out Rectangle frame);
			sb.Draw(texture, new Vector2(x + 54f, r.Y + 10), frame, Color.White, 0f, Vector2.Zero, 1.3f, SpriteEffects.None, 0f);
			GameArt.Smooth(sb);
			Saddle(sb, h, new Vector2(x + 24f, r.Y + 34));
			string name = L.T(HorseRace.Names[h]);
			float w = Math.Min(150f, MusicDrawing.Measure(name, 0.64f));
			MusicDrawing.RawText(sb, name, x + 72f - w / 2f, r.Y + 66, (place == 0) ? Gold : Cream, 0.64f, 150f);
			string odds = "x" + race.OddsWin[h].ToString("0.0");
			MusicDrawing.RawText(sb, odds, x + 72f - MusicDrawing.Measure(odds, 0.6f) / 2f, r.Y + 88, Muted, 0.6f);
		}
	}

	/// <summary>The six horses, their chances and odds, and where the chips go.</summary>
	private static void DrawField(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, Panel * 0.95f, PanelEdge);
		HorseRace race = BetsNet.Race;
		if (race == null)
		{
			MusicDrawing.RawText(sb, L.T("Finding the table..."), r.X + 14, r.Y + 12, Muted, 0.7f);
			return;
		}
		int colWin = r.Right - 216;
		int colTop = r.Right - 112;
		int y = r.Y + 8;
		MusicDrawing.RawText(sb, L.T("Horse"), r.X + 44, y, Muted, 0.6f);
		MusicDrawing.RawText(sb, L.T("Chance"), r.X + 246, y, Muted, 0.6f);
		MusicDrawing.RawText(sb, L.T("To win"), colWin + 10, y, Gold, 0.6f);
		MusicDrawing.RawText(sb, L.T("First two"), colTop + 8, y, Gold, 0.6f);
		y += 22;
		int rowH = Math.Max(24, (r.Bottom - y - 8) / HorseRace.Horses);
		int[] live = (BetsNet.Phase == BetsPhase.Running && race.Order != null) ? race.Standing(RaceTime) : null;
		for (int h = 0; h < HorseRace.Horses; h++)
		{
			Rectangle row = new Rectangle(r.X + 8, y, r.Width - 16, rowH - 3);
			MusicDrawing.Fill(sb, row, new Color(14, 16, 34) * 0.75f);
			Saddle(sb, h, new Vector2(row.X + 18, row.Center.Y));
			MusicDrawing.RawText(sb, L.T(HorseRace.Names[h]), r.X + 44, row.Y + 3, Cream, 0.68f, 190f);
			Rectangle meter = new Rectangle(r.X + 246, row.Center.Y - 3, 90, 6);
			MusicDrawing.Fill(sb, meter, Color.Black * 0.5f);
			MusicDrawing.Fill(sb, new Rectangle(meter.X, meter.Y, (int)(meter.Width * Math.Min(1.0, race.Chance[h] * 2.2)), meter.Height), Color.Lerp(CasinoArt.SoftRed, CasinoArt.SoftGreen, (float)Math.Min(1.0, race.Chance[h] * 2.5)));
			if (live != null)
			{
				string place = "#" + (Array.IndexOf(live, h) + 1);
				MusicDrawing.RawText(sb, place, r.X + 346, row.Y + 3, Gold, 0.64f);
			}
			OddsCell(sb, new Rectangle(colWin, row.Y + 1, 98, row.Height - 2), h, HorseRace.Win, race.OddsWin[h]);
			OddsCell(sb, new Rectangle(colTop, row.Y + 1, 98, row.Height - 2), h, HorseRace.TopTwo, race.OddsTopTwo[h]);
			y += rowH;
		}
	}

	private static void OddsCell(SpriteBatch sb, Rectangle r, int horse, int kind, double odds)
	{
		ulong key = HorseRace.Key(horse, kind);
		bool hover = BetsNet.CanBet && MusicPlayerWindow.MouseIn(r);
		BetsNet.Mine.TryGetValue(key, out int mine);
		int others = BetsNet.Others.Values.Sum(o => o.TryGetValue(key, out int v) ? v : 0);
		bool won = BetsNet.Phase == BetsPhase.Result && BetsNet.Race?.Order != null && (BetsNet.Race.Order[0] == horse || (kind == HorseRace.TopTwo && BetsNet.Race.Order[1] == horse));
		GameOverlay.MenuPanel(sb, r, (mine > 0) ? new Color(120, 84, 22) : (hover ? new Color(60, 70, 130) : new Color(36, 42, 84)), won ? CasinoArt.SoftGreen : (hover ? Gold : PanelEdge));
		string text = "x" + odds.ToString("0.0");
		MusicDrawing.RawText(sb, text, r.X + 8, r.Y + 2, Color.White, 0.66f);
		string chips = (mine > 0) ? Short(mine) : ((others > 0) ? Short(others) : "");
		if (chips.Length > 0)
		{
			MusicDrawing.RawText(sb, chips, r.Right - 7 - MusicDrawing.Measure(chips, 0.58f), r.Y + 3, (mine > 0) ? Gold : Muted, 0.58f);
		}
		if (hover)
		{
			MusicDrawing.Hover("bets:horse:" + key, L.F("Each chip gives back {0}", odds.ToString("0.0")));
			if (MusicPlayerWindow.Clicked)
			{
				MusicPlayerWindow.Clicked = false;
				BetsNet.Place(key, BetsLimits.Chips[_chip]);
			}
			else if (MusicPlayerWindow.RightClicked)
			{
				BetsNet.Remove(key);
			}
		}
	}

	/// <summary>The last winners.</summary>
	private static void DrawWinners(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, Panel * 0.94f, PanelEdge);
		MusicDrawing.RawText(sb, L.T("Last winners"), r.X + 12, r.Y + 8, Gold, 0.66f);
		int x = r.X + 24;
		foreach (int h in BetsNet.History.Take(10))
		{
			if (x + 12 > r.Right)
			{
				break;
			}
			Saddle(sb, h, new Vector2(x, r.Y + 50));
			x += 26;
		}
		if (BetsNet.History.Count == 0)
		{
			MusicDrawing.RawText(sb, L.T("None yet"), r.X + 12, r.Y + 36, Muted, 0.62f);
		}
	}

	#endregion

	/// <summary>Test harness: a page in a given moment.</summary>
	internal static string Test(string what)
	{
		string[] p = what.Split(' ');
		switch (p[0])
		{
		case "lobby":
			OpenLobby();
			break;
		case "slots":
			OpenGame(CasinoGame.Slots);
			if (p.Length > 1 && p[1] == "spin")
			{
				Spin();
			}
			if (p.Length > 1 && p[1] == "jackpot")
			{
				_stops = new[] { Array.IndexOf(Slots.Strips[0], Slots.Seven) - 1, Array.IndexOf(Slots.Strips[1], Slots.Seven) - 1, Array.IndexOf(Slots.Strips[2], Slots.Seven) - 1 };
				_wins = Slots.Evaluate(_stops);
				_lastWin = _wins.Sum(w => w.Pay * (long)(SlotBet / Slots.Lines));
				_winAt = Now;
			}
			break;
		case "roulette":
			OpenGame(CasinoGame.Roulette);
			if (p.Length > 1 && p[1] == "bet")
			{
				BetsNet.Place(Roulette.Red, 100);
				BetsNet.Place(Roulette.Of(17), 50);
				BetsNet.Place(Roulette.Of(20, 23), 50);
				BetsNet.Place(Roulette.Of(25, 26, 28, 29), 500);
				BetsNet.Place(Roulette.Dozen(1), 100);
			}
			if (p.Length > 1 && p[1] == "go")
			{
				BetsNet.Go();
			}
			break;
		case "horses":
			OpenGame(CasinoGame.Horses);
			if (p.Length > 1 && p[1] == "bet")
			{
				BetsNet.Place(HorseRace.Key(1, HorseRace.Win), 100);
				BetsNet.Place(HorseRace.Key(4, HorseRace.TopTwo), 500);
			}
			if (p.Length > 1 && p[1] == "go")
			{
				BetsNet.Go();
			}
			break;
		case "poker":
			CasinoNet.HostPoker(50, 5000, 1, open: true);
			CasinoWindow.OpenCards();
			break;
		case "cards":
			return "at " + CasinoNet.AtTable + " " + CasinoNet.Game + " host " + CasinoNet.Host + " hosting " + CasinoNet.Hosting + " ads " + CasinoNet.Ads.Count;
		case "leavecards":
			CasinoNet.Close();
			break;
		case "click":
			return CasinoNews.TestClick();
		case "win":
			CasinoNews.Won(CasinoGame.Poker, 8290);
			break;
		case "state":
			return "at " + BetsNet.AtTable + " " + BetsNet.Game + " table " + BetsNet.TableId + " host " + BetsNet.Host + " hosting " + BetsNet.Hosting + " members " + string.Join(",", BetsNet.Members) + " round " + BetsNet.Round + " " + BetsNet.Phase + " left " + BetsNet.SecondsLeft.ToString("0.0") + " outcome " + BetsNet.Outcome + " mine " + BetsNet.Staked + " others " + BetsNet.Others.Count + " history " + string.Join(",", BetsNet.History) + " ads " + BetsNet.Ads.Count + " line " + CasinoNews.LastLine;
		case "leave":
			BackToLobby();
			break;
		case "rtp":
			return "slots return " + Slots.ReturnRate().ToString("0.0000") + "; " + TestRoulette() + "; " + TestRaces();
		}
		return BetsNet.Phase + " " + BetsNet.Outcome + " free " + CasinoWallet.Free;
	}

	private static string TestRoulette()
	{
		double paid = 0;
		ulong[] bets = { Roulette.Of(17), Roulette.Of(1, 2), Roulette.Of(1, 2, 3), Roulette.Of(1, 2, 4, 5), Roulette.Red, Roulette.Dozen(0), Roulette.Column(2), Roulette.Even };
		foreach (ulong bet in bets)
		{
			for (int n = 0; n <= 36; n++)
			{
				paid += Roulette.Payout(bet, 1, n);
			}
		}
		return "roulette return " + (paid / (37.0 * bets.Length)).ToString("0.0000");
	}

	private static string TestRaces()
	{
		Random random = new Random(5);
		double win = 0;
		double top = 0;
		int[] firsts = new int[HorseRace.Horses];
		int races = 40000;
		bool ordered = true;
		for (int i = 0; i < races; i++)
		{
			HorseRace race = new HorseRace(random.Next());
			race.Run(random.Next());
			int h = random.Next(HorseRace.Horses);
			win += race.Payout(HorseRace.Key(h, HorseRace.Win), 100) / 100.0;
			top += race.Payout(HorseRace.Key(h, HorseRace.TopTwo), 100) / 100.0;
			firsts[race.Order[0]]++;
			if (i < 300 && race.Standing(HorseRace.Length * 1.3)[0] != race.Order[0])
			{
				ordered = false;
			}
		}
		return "race return win " + (win / races).ToString("0.000") + " top two " + (top / races).ToString("0.000") + " finish matches " + ordered;
	}
}
