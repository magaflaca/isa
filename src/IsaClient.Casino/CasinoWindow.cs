using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Casino;

/// <summary>
/// The casino window: the lobby (your chips, Poker and Blackjack, open tables) and the table itself, in a little casino
/// room, with the Moon Lord dealing (cards fly from his shoe, chips go to the pot and to the winners, his eyes follow
/// them), everyone sitting around as their Terraria character, and the actions of whoever plays.
/// </summary>
public static class CasinoWindow
{
	/// <summary>The card deck item (Dead Cells' Killing Deck): the casino's icon.</summary>
	public const int IconItem = 5461;

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "games:casino" };

	private static bool _registered;

	private static bool _open;

	private const int HeaderH = 48;

	private static Color Cream => CasinoArt.Cream;

	private static Color Gold => CasinoArt.Gold;

	private static readonly Color Muted = new Color(214, 196, 170);

	private static readonly Color GreenButton = new Color(44, 120, 70);

	private static readonly Color RedButton = new Color(140, 40, 46);

	private static readonly Color GoldButton = new Color(150, 106, 34);

	private static readonly Color BlueButton = new Color(52, 72, 140);

	private static readonly Color PlainButton = new Color(44, 50, 88);

	/// <summary>The Moon Lord's size at the tables (of the game's).</summary>
	private const float DealerScale = 0.22f;

	public static bool IsOpen => _open;

	public static void Open()
	{
		if (Main.gameMenu)
		{
			return;
		}
		Ensure();
		if (!_open)
		{
			IsaClient.Games.GamePlays.Count(IsaClient.Games.GamePlays.Casino);
		}
		_open = true;
		CasinoWallet.Load();
		MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
	}

	public static void Close()
	{
		_open = false;
	}

	/// <summary>The Bets page instead of the cards.</summary>
	internal static bool BetsPage;

	/// <summary>The cards: Poker and Blackjack.</summary>
	public static void OpenCards()
	{
		BetsPage = false;
		Open();
	}

	/// <summary>The slot machine, the Roulette and the races.</summary>
	public static void OpenBets()
	{
		BetsPage = true;
		Open();
	}

	/// <summary>Sits at an open table with the usual chips.</summary>
	internal static void SitAt(TableAd ad)
	{
		int buyIn = (ad.Game == CasinoGame.Poker) ? BuyIn(ad.Stake * 100, ad.Stake * 20) : BuyIn(ad.Stake * 40, ad.Stake * 5);
		CasinoNet.JoinTable(ad, buyIn);
	}

	/// <summary>Test harness: the window drawn on its own.</summary>
	internal static void TestDraw(SpriteBatch sb, int screenW, int screenH) => Draw(sb, screenW, screenH);

	private static void Ensure()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _panel.Layer,
			Shown = () => _open && !Main.gameMenu,
			ScreenRect = () => _panel.ScreenRect,
			Draw = Draw
		});
		CasinoNet.PokerChanged += OnPoker;
		CasinoNet.BlackjackChanged += OnBlackjack;
	}

	/// <summary>Game loop (from the games' update).</summary>
	public static void Update()
	{
		CasinoNet.Update();
		BetsNet.Update();
		CasinoNews.Update();
		BetsWindow.Update();
		CasinoMusic.Update(_open);
		if (CasinoNet.AtTable && !_registered)
		{
			Ensure();
		}
	}

	internal static void Resume(SpriteBatch sb)
	{
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, GameArt.Raster, null, Main.UIScaleMatrix);
	}

	#region Window

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		int width = Math.Min(980, screenW - 20);
		int height = Math.Min(680, screenH - 20);
		Rectangle r = GameOverlay.Place(_panel, width, height, screenW, screenH, 0);
		GameOverlay.Window(sb, r, HeaderH);
		GameOverlay.Grip(sb, r);
		if (DrawHeader(sb, r))
		{
			return;
		}
		Rectangle body = new Rectangle(r.X + 10, r.Y + HeaderH + 4, r.Width - 20, r.Height - HeaderH - 14);
		try
		{
			if (BetsPage)
			{
				BetsWindow.Draw(sb, body);
			}
			else if (CasinoNet.Poker != null)
			{
				DrawPoker(sb, body, CasinoNet.Poker);
			}
			else if (CasinoNet.Blackjack != null)
			{
				DrawBlackjack(sb, body, CasinoNet.Blackjack);
			}
			else
			{
				DrawLobby(sb, body);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 240, HeaderH - 4), screenW, screenH);
	}

	private static void ItemIcon(SpriteBatch sb, int item, Rectangle area)
	{
		Main.instance.LoadItem(item);
		Texture2D texture = TextureAssets.Item[item].Value;
		Rectangle frame = (Main.itemAnimations[item] != null) ? Main.itemAnimations[item].GetFrame(texture) : texture.Bounds;
		MusicDrawing.Icon(sb, texture, frame, area, Color.White);
	}

	private static bool DrawHeader(SpriteBatch sb, Rectangle r)
	{
		ItemIcon(sb, BetsPage ? BetsWindow.BetsItem : IconItem, new Rectangle(r.X + 12, r.Y + 8, 32, 32));
		string title;
		string sub;
		if (BetsPage)
		{
			BetsWindow.Titles(out title, out sub);
		}
		else if (CasinoNet.Poker != null)
		{
			PokerTable t = CasinoNet.Poker;
			title = "Texas Hold'em";
			sub = IsaClient.Languages.L10n.F("Blinds {0}/{1}", t.SmallBlind.ToString("N0"), t.BigBlind.ToString("N0")) + "   " + HostLine();
		}
		else if (CasinoNet.Blackjack != null)
		{
			BlackjackTable t = CasinoNet.Blackjack;
			title = "Blackjack";
			sub = IsaClient.Languages.L10n.F("Bets {0} to {1}", t.MinBet.ToString("N0"), t.MaxBet.ToString("N0")) + "   " + HostLine();
		}
		else
		{
			title = IsaClient.Languages.L10n.T("Casino");
			sub = IsaClient.Languages.L10n.T("Dealt by the Moon Lord");
		}
		MusicDrawing.RawText(sb, title, r.X + 52, r.Y + 7, Gold, 0.92f, r.Width - 610);
		MusicDrawing.RawText(sb, sub, r.X + 52, r.Y + 28, Muted, 0.62f, r.Width - 610);
		Tab(sb, "casino:tabcards", new Rectangle(r.Right - 500, r.Y + 10, 96, 28), "Cards", IconItem, !BetsPage, OpenCards);
		Tab(sb, "casino:tabbets", new Rectangle(r.Right - 400, r.Y + 10, 96, 28), "Bets", BetsWindow.BetsItem, BetsPage, OpenBets);
		// your chips
		Rectangle pill = CasinoArt.Pill(sb, new Vector2(r.Right - 250, r.Y + 24), CasinoWallet.Free.ToString("N0"), Gold, 0.74f);
		if (MusicPlayerWindow.MouseIn(pill))
		{
			MusicDrawing.Hover("casino:chips", IsaClient.Languages.L10n.F("Your chips. {0} more are at the table.", CasinoWallet.OnTable.ToString("N0")));
		}
		int x = r.Right - 34;
		if (MusicDrawing.BareIcon(sb, "casino:close", new Rectangle(x, r.Y + 14, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, CasinoNet.AtTable ? "Close the window (you stay at the table)" : "Close", Color.White))
		{
			Close();
			return true;
		}
		if (BetsPage)
		{
			if (BetsWindow.Page != 0)
			{
				x -= 112;
				if (CasinoArt.Button(sb, "casino:betsback", new Rectangle(x, r.Y + 10, 104, 28), "Back", RedButton, "Back to the games (chips not played yet come back)", BetsWindow.CanLeave))
				{
					BetsWindow.BackToLobby();
				}
			}
		}
		else if (CasinoNet.AtTable)
		{
			x -= 112;
			if (CasinoArt.Button(sb, "casino:leave", new Rectangle(x, r.Y + 10, 104, 28), "Leave table", RedButton, "Get up: your chips go back to your wallet (what's in a hand you're playing is lost)"))
			{
				CasinoNet.Close();
			}
		}
		return false;
	}

	/// <summary>A page of the casino, up in the window's bar.</summary>
	private static void Tab(SpriteBatch sb, string id, Rectangle r, string label, int item, bool on, Action open)
	{
		bool hover = MusicPlayerWindow.MouseIn(r);
		GameOverlay.MenuPanel(sb, r, on ? new Color(70, 80, 150) : (hover ? new Color(46, 54, 104) : new Color(30, 36, 70)), on ? Gold : (hover ? Gold * 0.5f : new Color(10, 12, 24)));
		ItemIcon(sb, item, new Rectangle(r.X + 6, r.Y + 4, 20, 20));
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), r.X + 32, r.Y + 5, on ? Color.White : Cream * 0.8f, 0.7f, r.Width - 38);
		if (hover)
		{
			MusicDrawing.Hover(id);
			if (MusicPlayerWindow.Clicked && !on)
			{
				MusicPlayerWindow.Clicked = false;
				SoundEngine.PlaySound(12);
				open();
			}
		}
	}

	private static string HostLine()
	{
		if (!CasinoNet.Open)
		{
			return IsaClient.Languages.L10n.T("with the computer");
		}
		return CasinoNet.Hosting ? IsaClient.Languages.L10n.T("your table") : IsaClient.Languages.L10n.F("{0}'s table", GameLobby.Name(CasinoNet.Host));
	}

	#endregion

	#region Lobby

	private static int _pokerStake = 1;

	private static int _bjStake = 1;

	private static int _bots = 3;

	private static readonly int[] BigBlinds = { 10, 50, 200, 1000 };

	private static readonly int[] MinBets = { 10, 50, 200, 1000 };

	private static Vector2 _lobbyLeft;

	private static Vector2 _lobbyRight;

	/// <summary>Where the Moon Lord's eyes look: toward a point on the screen.</summary>
	private static Vector2 LookAt(Vector2 core, Vector2 at)
	{
		Vector2 d = at - (core + new Vector2(0f, -90f));
		return (d.LengthSquared() > 1f) ? Vector2.Normalize(d) : Vector2.Zero;
	}

	private static void DrawLobby(SpriteBatch sb, Rectangle body)
	{
		CasinoArt.Room(sb, body, 0.46f);
		// the Moon Lord waving from behind a little table, left
		Rectangle stage = new Rectangle(body.X + 10, body.Y + 8, 320, 252);
		Vector2 core = new Vector2(stage.Center.X, stage.Y + 158);
		float wave = (float)Math.Sin(GameArt.Now * 3.2);
		Vector2 look = LookAt(core, new Vector2(MusicPlayerWindow.MousePoint.X, MusicPlayerWindow.MousePoint.Y));
		CasinoArt.MoonLord(sb, core, 0.2f, Vector2.Zero, new Vector2(30f + wave * 30f, -250f), 0, 0, look);
		Rectangle bar = new Rectangle(stage.X - 4, stage.Y + 186, stage.Width + 8, 66);
		CasinoArt.Table(sb, bar, new Color(44, 150, 90), new Color(24, 98, 58));
		GameArt.Sharp(sb);
		CasinoArt.Card(sb, new Vector2(stage.Center.X - 62, stage.Y + 202), 0.7f, 12, true);
		CasinoArt.Card(sb, new Vector2(stage.Center.X - 32, stage.Y + 204), 0.7f, 24, true);
		CasinoArt.Chips(sb, new Vector2(stage.Center.X + 54, stage.Y + 236), 1630, 1f, 5);
		GameArt.Smooth(sb);
		DrawWallet(sb, new Rectangle(body.X + 10, stage.Bottom + 10, 320, body.Bottom - stage.Bottom - 20));
		Rectangle right = new Rectangle(stage.Right + 14, body.Y + 8, body.Right - stage.Right - 24, body.Height - 18);
		int cardH = 196;
		DrawPokerCard(sb, new Rectangle(right.X, right.Y, right.Width, cardH));
		DrawBlackjackCard(sb, new Rectangle(right.X, right.Y + cardH + 10, right.Width, cardH - 16));
		int tablesY = right.Y + cardH * 2 + 4;
		DrawTables(sb, new Rectangle(right.X, tablesY, right.Width, right.Bottom - tablesY));
	}

	internal static void DrawWalletPanel(SpriteBatch sb, Rectangle r) => DrawWallet(sb, r);

	private static void DrawWallet(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, new Color(26, 32, 66) * 0.95f, new Color(8, 10, 22));
		int y = r.Y + 12;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Your chips"), r.X + 14, y, Muted, 0.64f);
		GameArt.Sharp(sb);
		CasinoArt.Chips(sb, new Vector2(r.Right - 52, y + 42), (int)Math.Min(int.MaxValue, CasinoWallet.Total), 1.15f, 6);
		GameArt.Smooth(sb);
		y += 18;
		MusicDrawing.RawText(sb, CasinoWallet.Total.ToString("N0"), r.X + 14, y, Gold, 1.25f);
		y += 38;
		void Line(string label, long value, string tip, bool signed)
		{
			string v = (signed && value > 0 ? "+" : "") + value.ToString("N0");
			Color c = (!signed || value == 0) ? Cream : ((value > 0) ? CasinoArt.SoftGreen : CasinoArt.SoftRed);
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), r.X + 14, y, Cream * 0.8f, 0.62f);
			float w = MusicDrawing.Measure(v, 0.66f);
			MusicDrawing.RawText(sb, v, r.Right - 14 - w, y, c, 0.66f);
			if (tip != null && MusicPlayerWindow.MouseIn(new Rectangle(r.X + 8, y - 2, r.Width - 16, 18)))
			{
				MusicDrawing.Hover("casino:wallet:" + label, IsaClient.Languages.L10n.T(tip));
			}
			y += 19;
		}
		Line("From your games", CasinoWallet.FromGames, "Every point you have on the global board (the casino aside) is 2 chips, plus 1,000 to start.", false);
		Line("Poker", CasinoWallet.PokerNet, null, true);
		Line("Blackjack", CasinoWallet.BlackjackNet, null, true);
		Line("Bets", CasinoWallet.BetsChips, null, true);
		Line("Gifts from the house", CasinoWallet.Gifts, "When you run out, the house always gives you a small lot.", false);
		y += 8;
		MusicDrawing.Fill(sb, new Rectangle(r.X + 12, y, r.Width - 24, 2), Color.Black * 0.3f);
		y += 10;
		// the best of each board, side by side
		ulong me = IsaClient.Identity.ClientIdentity.Id;
		int colW = (r.Width - 28) / 2;
		int top = y;
		int col = 0;
		foreach ((byte board, string name) in BetsPage ? new[] { (Scores.Bets, "Bets") } : new[] { (Scores.Poker, "Poker"), (Scores.Blackjack, "Blackjack") })
		{
			int x = r.X + 14 + col * colW;
			y = top;
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(name), x, y, Gold, 0.66f);
			y += 20;
			List<ScoreEntry> ranking = Scores.Ranking(board);
			for (int i = 0; i < Math.Min(3, ranking.Count); i++)
			{
				ScoreEntry e = ranking[i];
				long chips = (long)e.Score - Scores.CasinoBase;
				Color c = (e.Id == me) ? MusicPlayerWindow.AccentColor : Cream * 0.85f;
				MusicDrawing.RawText(sb, (i + 1) + ". " + e.Name, x, y, c, 0.58f, colW - 56);
				string v = (chips > 0 ? "+" : "") + chips.ToString("N0");
				MusicDrawing.RawText(sb, v, x + colW - 8 - MusicDrawing.Measure(v, 0.58f), y, (chips >= 0) ? CasinoArt.SoftGreen : CasinoArt.SoftRed, 0.58f);
				y += 17;
			}
			if (ranking.Count == 0)
			{
				MusicDrawing.RawText(sb, "-", x, y, Muted, 0.58f);
			}
			int rank = Scores.RankOf(board, me);
			if (rank > 3)
			{
				MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("You: #{0}", rank), x, top + 20 + 3 * 17, MusicPlayerWindow.AccentColor, 0.58f);
			}
			col++;
		}
		if (BetsPage)
		{
			// your own rounds, beside the board
			int sx = r.X + 14 + colW;
			y = top;
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Your games"), sx, y, Gold, 0.66f);
			y += 20;
			foreach ((string label, int count, Color color) in new[] { ("Won", CasinoWallet.BetsWins, CasinoArt.SoftGreen), ("Lost", CasinoWallet.BetsLosses, CasinoArt.SoftRed), ("Even", CasinoWallet.BetsEven, Cream) })
			{
				MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), sx, y, Cream * 0.85f, 0.58f);
				string v = count.ToString("N0");
				MusicDrawing.RawText(sb, v, sx + colW - 8 - MusicDrawing.Measure(v, 0.58f), y, color, 0.58f);
				y += 17;
			}
		}
		if (CasinoArt.Button(sb, "casino:scores", new Rectangle(r.X + 14, r.Bottom - 42, r.Width - 28, 30), "Boards", PlainButton, BetsPage ? "The Bets and global boards" : "The Poker, Blackjack and global boards"))
		{
			IsaClient.Hub.HubGames.ShowBoard(BetsPage ? Scores.Bets : Scores.Poker);
		}
	}

	/// <summary>A choice as Terraria-style tabs: the chosen one lit with a gold border.</summary>
	private static int Choice(SpriteBatch sb, string id, string label, string[] labels, int current, int x, int y, Action<int> set)
	{
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), x, y + 6, Muted, 0.62f);
		int bx = x + 86;
		for (int i = 0; i < labels.Length; i++)
		{
			int w = (int)MusicDrawing.Measure(labels[i], 0.7f) + 22;
			Rectangle r = new Rectangle(bx, y, w, 26);
			bool on = i == current;
			bool hover = MusicPlayerWindow.MouseIn(r);
			GameOverlay.MenuPanel(sb, r, on ? new Color(70, 80, 150) : (hover ? new Color(46, 54, 104) : new Color(30, 36, 70)), on ? Gold : (hover ? Gold * 0.5f : new Color(10, 12, 24)));
			MusicDrawing.RawText(sb, labels[i], r.X + 11, r.Y + 5, on ? Color.White : Cream * 0.8f, 0.7f);
			if (hover)
			{
				MusicDrawing.Hover(id + ":" + i);
				if (MusicPlayerWindow.Clicked)
				{
					MusicPlayerWindow.Clicked = false;
					set(i);
					SoundEngine.PlaySound(12);
				}
			}
			bx += w + 4;
		}
		return y + 32;
	}

	private static void DrawPokerCard(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, new Color(30, 38, 76) * 0.95f, new Color(10, 12, 24));
		// a little felt with a royal flush on it
		Rectangle art = new Rectangle(r.X + 10, r.Y + 10, 170, r.Height - 20);
		GameOverlay.MenuPanel(sb, art, new Color(30, 120, 74), new Color(8, 10, 22));
		GameArt.Sharp(sb);
		byte[] fan = { 8, 9, 10, 11, 12 };
		for (int i = 0; i < fan.Length; i++)
		{
			CasinoArt.Card(sb, new Vector2(art.X + 10 + i * 26, art.Y + 22 + Math.Abs(i - 2) * 6), 1f, (byte)(fan[i] + 13), true);
		}
		CasinoArt.Chips(sb, new Vector2(art.Center.X, art.Bottom - 12), 2630, 1.2f, 5);
		GameArt.Smooth(sb);
		int tx = art.Right + 16;
		MusicDrawing.RawText(sb, "Texas Hold'em", tx, r.Y + 10, Gold, 0.92f);
		int y = r.Y + 40;
		y = Choice(sb, "casino:pstake", "Blinds", BigBlinds.Select(b => (b / 2).ToString("N0") + "/" + b.ToString("N0")).ToArray(), _pokerStake, tx, y, i => _pokerStake = i);
		y = Choice(sb, "casino:pbots", "Rivals", new[] { "1", "2", "3", "4", "5" }, _bots - 1, tx, y, i => _bots = i + 1);
		int bb = BigBlinds[_pokerStake];
		int buyIn = BuyIn(bb * 100, bb * 20);
		Rectangle buy = CasinoArt.Pill(sb, new Vector2(tx + 48, y + 11), buyIn.ToString("N0"), Gold, 0.64f);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("to the table"), buy.Right + 8, buy.Y + 4, Muted, 0.6f);
		y += 30;
		int bw = Math.Min(210, (r.Right - tx - 20) / 2);
		if (CasinoArt.Button(sb, "casino:pbot", new Rectangle(tx, y, bw, 34), "Play", GreenButton, IsaClient.Languages.L10n.F("A table with {0} computer players. Works anywhere, even alone.", _bots)))
		{
			CasinoNet.HostPoker(bb, buyIn, _bots, open: false);
		}
		bool online = Main.netMode == 1 && IsaClient.Net.Pipe.PipeNet.Enabled && IsaClient.Net.Pipe.PipeNet.Ready;
		if (CasinoArt.Button(sb, "casino:popen", new Rectangle(tx + bw + 8, y, bw, 34), "Open to everyone", PlainButton, online ? "Every IsaClient player here can sit at it (the computer players give up their seats)." : "Join a server (with the pipe on) to play with others.", online))
		{
			CasinoNet.HostPoker(bb, buyIn, Math.Min(_bots, 2), open: true);
		}
	}

	/// <summary>What you sit with: <paramref name="wanted"/>, or what you have (at least <paramref name="least"/>, which the house gives when needed).</summary>
	private static int BuyIn(int wanted, int least)
	{
		long free = CasinoWallet.Free;
		return (int)Math.Max(least, Math.Min(wanted, free));
	}

	private static void DrawBlackjackCard(SpriteBatch sb, Rectangle r)
	{
		GameOverlay.MenuPanel(sb, r, new Color(30, 38, 76) * 0.95f, new Color(10, 12, 24));
		Rectangle art = new Rectangle(r.X + 10, r.Y + 10, 170, r.Height - 20);
		GameOverlay.MenuPanel(sb, art, new Color(150, 32, 52), new Color(8, 10, 22));
		GameArt.Sharp(sb);
		CasinoArt.Card(sb, new Vector2(art.X + 34, art.Y + 18), 1.2f, 12, true);
		CasinoArt.Card(sb, new Vector2(art.X + 70, art.Y + 30), 1.2f, 11 + 13, true);
		CasinoArt.Chips(sb, new Vector2(art.Center.X, art.Bottom - 10), 1130, 1.2f, 4);
		GameArt.Smooth(sb);
		int tx = art.Right + 16;
		MusicDrawing.RawText(sb, "Blackjack", tx, r.Y + 10, Gold, 0.92f);
		int y = r.Y + 40;
		y = Choice(sb, "casino:bstake", "Bets from", MinBets.Select(b => b.ToString("N0")).ToArray(), _bjStake, tx, y, i => _bjStake = i);
		int min = MinBets[_bjStake];
		int buyIn = BuyIn(min * 40, min * 5);
		Rectangle buy = CasinoArt.Pill(sb, new Vector2(tx + 48, y + 11), buyIn.ToString("N0"), Gold, 0.64f);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("to the table"), buy.Right + 8, buy.Y + 4, Muted, 0.6f);
		y += 30;
		int bw = Math.Min(210, (r.Right - tx - 20) / 2);
		if (CasinoArt.Button(sb, "casino:bsolo", new Rectangle(tx, y, bw, 34), "Play", GreenButton, "Just you and the dealer. Works anywhere."))
		{
			CasinoNet.HostBlackjack(min, buyIn, open: false);
		}
		bool online = Main.netMode == 1 && IsaClient.Net.Pipe.PipeNet.Enabled && IsaClient.Net.Pipe.PipeNet.Ready;
		if (CasinoArt.Button(sb, "casino:bopen", new Rectangle(tx + bw + 8, y, bw, 34), "Open to everyone", PlainButton, online ? "Friends can sit with you (up to 4), each playing against the dealer." : "Join a server (with the pipe on) to play with others.", online))
		{
			CasinoNet.HostBlackjack(min, buyIn, open: true);
		}
	}

	private static void DrawTables(SpriteBatch sb, Rectangle r)
	{
		if (r.Height < 40)
		{
			return;
		}
		GameOverlay.MenuPanel(sb, r, new Color(30, 38, 76) * 0.9f, new Color(10, 12, 24));
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Open tables"), r.X + 14, r.Y + 8, Gold, 0.7f);
		int y = r.Y + 32;
		if (CasinoNet.Ads.Count == 0)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("None right now."), r.X + 14, y, Muted, 0.62f, r.Width - 28);
			return;
		}
		foreach (TableAd ad in CasinoNet.Ads.ToArray())
		{
			if (y + 32 > r.Bottom - 6)
			{
				break;
			}
			Rectangle row = new Rectangle(r.X + 8, y, r.Width - 16, 30);
			GameOverlay.MenuPanel(sb, row, new Color(20, 24, 48), new Color(8, 10, 22));
			string what = (ad.Game == CasinoGame.Poker) ? ("Texas Hold'em  " + (ad.Stake / 2).ToString("N0") + "/" + ad.Stake.ToString("N0")) : ("Blackjack  " + ad.Stake.ToString("N0") + "+");
			MusicDrawing.RawText(sb, GameLobby.Name(ad.Host), row.X + 10, row.Y + 6, Cream, 0.66f, 160);
			MusicDrawing.RawText(sb, what + "   " + ad.Seated + "/" + ad.Max, row.X + 180, row.Y + 6, Muted, 0.64f, row.Width - 300);
			int buyIn = (ad.Game == CasinoGame.Poker) ? BuyIn(ad.Stake * 100, ad.Stake * 20) : BuyIn(ad.Stake * 40, ad.Stake * 5);
			if (CasinoArt.Button(sb, "casino:join:" + ad.Host + ":" + ad.Id, new Rectangle(row.Right - 104, row.Y + 2, 100, 26), "Sit down", GreenButton, IsaClient.Languages.L10n.F("Sit with {0} chips", buyIn.ToString("N0")), ad.Seated < ad.Max))
			{
				CasinoNet.JoinTable(ad, buyIn);
			}
			y += 34;
		}
	}

	#endregion

	#region Flying cards and chips

	private sealed class Flight
	{
		public string From;

		public string To;

		public double Start;

		public double Duration;

		/// <summary>A card (or <see cref="Cards.NoCard"/> face down), or chips (<see cref="Chips"/> &gt; 0).</summary>
		public byte Card = Cards.NoCard;

		public bool FaceUp;

		public int Chips;

		/// <summary>The dealer's hand that carries it: 1 right (cards), 0 left (chips), 2 both.</summary>
		public int HandSide;
	}

	private static readonly List<Flight> _flights = new List<Flight>();

	/// <summary>When each card slot gets its card (it shows from then on).</summary>
	private static readonly Dictionary<string, double> _arrive = new Dictionary<string, double>();

	private static double Now => GameArt.Now;

	private static double FlightsEnd => _flights.Count == 0 ? Now : Math.Max(Now, _flights.Max(f => f.Start + f.Duration));

	private static void Fly(string from, string to, double delay, double duration, byte card, bool faceUp, int chips, int side)
	{
		Flight f = new Flight { From = from, To = to, Start = Now + delay, Duration = duration, Card = card, FaceUp = faceUp, Chips = chips, HandSide = side };
		_flights.Add(f);
		if (chips == 0)
		{
			_arrive[to] = f.Start + duration;
		}
	}

	private static bool Arrived(string slot) => !_arrive.TryGetValue(slot, out double at) || Now >= at;

	/// <summary>How far a card is turned face up after landing (0..1, a quick flip).</summary>
	private static float FlipOf(string slot)
	{
		if (!_arrive.TryGetValue(slot, out double at))
		{
			return 1f;
		}
		return GameArt.Clamp01((Now - at) / 0.16);
	}

	private static void OnPoker(PokerTable before, PokerTable now)
	{
		_flights.RemoveAll(f => Now > f.Start + f.Duration + 0.5);
		if (before == null || now == null)
		{
			return;
		}
		double delay = Math.Max(0.0, FlightsEnd - Now);
		if (now.HandNo != before.HandNo && now.Phase >= PokerPhase.Preflop)
		{
			_arrive.Clear();
			_equityKey = null;
			List<int> order = new List<int>();
			for (int i = 1; i <= PokerTable.MaxSeats; i++)
			{
				int seat = (now.Button + i) % PokerTable.MaxSeats;
				if (now.Seats[seat].Dealt)
				{
					order.Add(seat);
				}
			}
			double t = 0.15;
			for (int round = 0; round < 2; round++)
			{
				foreach (int seat in order)
				{
					Fly("deck", "hole:" + seat + ":" + round, t, 0.34, Cards.NoCard, false, 0, 1);
					t += 0.14;
				}
			}
			SoundEngine.PlaySound(Terraria.ID.SoundID.Grab);
			before = null;
		}
		if (before != null && now.HandNo == before.HandNo && (now.BoardCount > before.BoardCount || (now.Phase == PokerPhase.Showdown && before.Phase != PokerPhase.Showdown)))
		{
			for (int seat = 0; seat < PokerTable.MaxSeats; seat++)
			{
				int bet = before.Seats[seat].Bet;
				if (bet > 0 && now.Seats[seat].Bet == 0)
				{
					Fly("bet:" + seat, "pot", delay, 0.42, Cards.NoCard, false, bet, 0);
				}
			}
			if (before.Seats.Any(s => s.Bet > 0))
			{
				delay += 0.48;
			}
		}
		if (before != null && now.HandNo == before.HandNo)
		{
			for (int i = before.BoardCount; i < now.BoardCount; i++)
			{
				Fly("deck", "board:" + i, delay, 0.36, now.Board[i], false, 0, 1);
				delay += (now.Phase == PokerPhase.Showdown && before.Phase != PokerPhase.Showdown) ? 0.5 : 0.2;
			}
		}
		if (now.Phase == PokerPhase.Showdown && (before == null || before.Phase != PokerPhase.Showdown))
		{
			double wait = delay + (now.QuietWin ? 0.3 : 1.1);
			for (int seat = 0; seat < PokerTable.MaxSeats; seat++)
			{
				if (now.Seats[seat].Won > 0)
				{
					Fly("pot", "stack:" + seat, wait, 0.62, Cards.NoCard, false, now.Seats[seat].Won, 2);
					wait += 0.2;
				}
			}
		}
	}

	private static double _holeFlipAt;

	private static void OnBlackjack(BlackjackTable before, BlackjackTable now)
	{
		_flights.RemoveAll(f => Now > f.Start + f.Duration + 0.5);
		if (before == null || now == null)
		{
			return;
		}
		double delay = Math.Max(0.0, FlightsEnd - Now);
		if (now.RoundNo != before.RoundNo && now.Phase != BlackjackPhase.Betting)
		{
			_arrive.Clear();
			double t = 0.15;
			for (int round = 0; round < 2; round++)
			{
				for (int seat = 0; seat < BlackjackTable.MaxSeats; seat++)
				{
					if (now.Seats[seat].Hands.Count > 0 && now.Seats[seat].Hands[0].Cards.Count > round)
					{
						Fly("deck", "bj:" + seat + ":0:" + round, t, 0.34, now.Seats[seat].Hands[0].Cards[round], true, 0, 1);
						t += 0.16;
					}
				}
				if (now.Dealer.Count > round)
				{
					Fly("deck", "dealer:" + round, t, 0.34, now.Dealer[round], round == 0 || now.HoleShown, 0, 1);
					t += 0.16;
				}
			}
			SoundEngine.PlaySound(Terraria.ID.SoundID.Grab);
			return;
		}
		if (now.RoundNo != before.RoundNo)
		{
			return;
		}
		for (int seat = 0; seat < BlackjackTable.MaxSeats; seat++)
		{
			BlackjackSeat a = before.Seats[seat];
			BlackjackSeat b = now.Seats[seat];
			for (int h = 0; h < b.Hands.Count; h++)
			{
				int had = (h < a.Hands.Count) ? a.Hands[h].Cards.Count : 1;
				if (h < a.Hands.Count && b.Hands.Count > a.Hands.Count)
				{
					had = Math.Min(had, 1);
				}
				for (int c = had; c < b.Hands[h].Cards.Count; c++)
				{
					Fly("deck", "bj:" + seat + ":" + h + ":" + c, delay, 0.32, b.Hands[h].Cards[c], true, 0, 1);
					delay += 0.16;
				}
			}
		}
		for (int c = before.Dealer.Count; c < now.Dealer.Count; c++)
		{
			Fly("deck", "dealer:" + c, delay, 0.32, now.Dealer[c], true, 0, 1);
			delay += 0.16;
		}
		if (now.HoleShown && !before.HoleShown)
		{
			_arrive["dealer:1"] = Now + delay;
			_holeFlipAt = Now + delay;
		}
		if (now.Phase == BlackjackPhase.Payout && before.Phase != BlackjackPhase.Payout)
		{
			double wait = delay + 0.6;
			for (int seat = 0; seat < BlackjackTable.MaxSeats; seat++)
			{
				for (int h = 0; h < now.Seats[seat].Hands.Count; h++)
				{
					BlackjackHand hand = now.Seats[seat].Hands[h];
					if (hand.Result == 3 || hand.Result == 4)
					{
						Fly("tray", "bjbet:" + seat + ":" + h, wait, 0.5, Cards.NoCard, false, hand.Bet, 0);
					}
					else if (hand.Result == 1)
					{
						Fly("bjbet:" + seat + ":" + h, "tray", wait, 0.5, Cards.NoCard, false, hand.Bet, 0);
					}
					wait += 0.12;
				}
			}
		}
	}

	private static Vector2? FlightAt(Flight f, Func<string, Vector2> where, out float t)
	{
		t = (float)((Now - f.Start) / f.Duration);
		if (t < 0f || t > 1f)
		{
			return null;
		}
		float e = GameArt.EaseInOut(t);
		Vector2 a = where(f.From);
		Vector2 b = where(f.To);
		Vector2 p = Vector2.Lerp(a, b, e);
		p.Y -= (float)Math.Sin(t * Math.PI) * 18f;
		return p;
	}

	/// <summary>How far each dealer hand has moved from its rest (game units), its frame, and where the eyes look.</summary>
	private static Vector2 _leftMove;

	private static Vector2 _rightMove;

	private static Vector2 _look = new Vector2(0f, 1f);

	private static int _leftFrame;

	private static int _rightFrame;

	/// <summary>
	/// The dealer this frame: from their rest on the far edge of the table the hands flick toward each card dealt (the
	/// card flies on from there) and sweep toward the chips; the eyes follow what flies.
	/// </summary>
	private static void MoveDealer(Vector2 core, Func<string, Vector2> where)
	{
		Vector2 targetL = Vector2.Zero;
		Vector2 targetR = Vector2.Zero;
		int frameL = 0;
		int frameR = 0;
		bool holding = false;
		Vector2 lookAt = core + new Vector2(0f, 260f);
		foreach (Flight f in _flights)
		{
			Vector2? at = FlightAt(f, where, out float t);
			if (at.HasValue)
			{
				lookAt = at.Value;
				Vector2 d = where(f.To) - where(f.From);
				Vector2 dir = (d.LengthSquared() > 1f) ? Vector2.Normalize(d) : Vector2.Zero;
				float push = (float)Math.Sin(Math.Min(1.0, t / 0.4) * Math.PI);
				// the hand lifts and swings toward where it goes (never below the table's edge)
				if (f.HandSide == 1)
				{
					targetR = new Vector2(dir.X * 110f, -50f) * push;
					frameR = (t < 0.15f) ? 1 : 0;
				}
				else
				{
					targetL = new Vector2(dir.X * 100f, -40f) * push;
					if (f.HandSide == 2)
					{
						targetR = new Vector2(dir.X * 100f, -40f) * push;
					}
				}
			}
			else if (f.HandSide == 1 && f.Start > Now && f.Start - Now < 0.3)
			{
				holding = true;
			}
		}
		if (holding && targetR == Vector2.Zero)
		{
			frameR = 1;
		}
		_leftMove = Vector2.Lerp(_leftMove, targetL, 0.3f);
		_rightMove = Vector2.Lerp(_rightMove, targetR, 0.3f);
		_leftFrame = frameL;
		_rightFrame = frameR;
		_look = Vector2.Lerp(_look, LookAt(core, lookAt), 0.2f);
	}

	private static Vector2 Breath(int side) => new Vector2(0f, (float)Math.Sin(Now * 2.1 + side * 1.7) * 6f);

	private static void DrawFlights(SpriteBatch sb, Func<string, Vector2> where, float cardScale)
	{
		GameArt.Sharp(sb);
		foreach (Flight f in _flights)
		{
			Vector2? at = FlightAt(f, where, out float t);
			if (!at.HasValue)
			{
				continue;
			}
			if (f.Chips > 0)
			{
				int value = CasinoArt.ChipFor(f.Chips);
				int n = Math.Min(5, Math.Max(1, f.Chips / value));
				for (int i = 0; i < n; i++)
				{
					CasinoArt.Chip(sb, at.Value + new Vector2(i * 5f - n * 2.5f, -i * 3f), value, 1.4f);
				}
			}
			else
			{
				Vector2 size = CasinoArt.CardSize(cardScale);
				CasinoArt.Card(sb, at.Value - size / 2f, cardScale, f.Card, f.FaceUp && f.Card != Cards.NoCard);
			}
		}
		GameArt.Smooth(sb);
	}

	#endregion

	#region Poker table

	/// <summary>Seats around the table (degrees, 90 is straight down), you at the bottom.</summary>
	private static readonly float[] PokerAngles = { 90f, 145f, 196f, 238f, 302f, 344f };

	private static int _raiseTo;

	private static ushort _raiseFor = 65535;

	/// <summary>Your chance to win the hand (Monte Carlo in the background), and what it was worked out for.</summary>
	private static string _equityKey;

	private static double _equity = -1.0;

	private static Task<double> _equityTask;

	private static double MyEquity(PokerTable t, int mySeat)
	{
		if (mySeat < 0 || CasinoNet.MyHoleHand != t.HandNo || CasinoNet.MyHole[0] == Cards.NoCard || !t.Seats[mySeat].Live || t.Phase < PokerPhase.Preflop || t.Phase > PokerPhase.River)
		{
			return -1.0;
		}
		int rivals = t.Seats.Count(s => s.Live) - 1;
		string key = t.HandNo + ":" + t.BoardCount + ":" + rivals;
		if (key != _equityKey)
		{
			_equityKey = key;
			_equity = -1.0;
			byte[] hole = (byte[])CasinoNet.MyHole.Clone();
			byte[] board = t.Board.Take(t.BoardCount).ToArray();
			_equityTask = Task.Run(() => PokerHands.Equity(hole, board, Math.Max(1, rivals), 1500, new Random()));
		}
		if (_equity < 0.0 && _equityTask != null && _equityTask.IsCompleted && _equityTask.Status == TaskStatus.RanToCompletion)
		{
			_equity = _equityTask.Result;
		}
		return _equity;
	}

	private static void DrawPoker(SpriteBatch sb, Rectangle body, PokerTable t)
	{
		int mySeat = t.SeatOf(CasinoNet.Me);
		Rectangle bar = new Rectangle(body.X, body.Bottom - 58, body.Width, 58);
		Rectangle room = new Rectangle(body.X, body.Y, body.Width, bar.Y - body.Y - 4);
		CasinoArt.Room(sb, room, 0.34f);
		Rectangle table = new Rectangle(room.X + 120, room.Y + 134, room.Width - 240, room.Height - 230);
		Vector2 center = new Vector2(table.Center.X, table.Center.Y);
		float rx = table.Width / 2f;
		float ry = table.Height / 2f;
		int Visual(int seat) => (mySeat >= 0) ? ((seat - mySeat + PokerTable.MaxSeats) % PokerTable.MaxSeats) : seat;
		double Angle(int seat) => PokerAngles[Visual(seat)];
		Vector2 OnTable(int seat, float k)
		{
			double a = Angle(seat) * Math.PI / 180.0;
			return center + new Vector2((float)Math.Cos(a) * rx * k, (float)Math.Sin(a) * ry * k);
		}
		Vector2 core = new Vector2(center.X, table.Y - 14f);
		Vector2 deck = CasinoArt.HandOnScreen(core, DealerScale, Vector2.Zero, false) + new Vector2(4f, 46f);
		Vector2 potAt = new Vector2(center.X, center.Y + 36f);
		Vector2 BetAt(int seat) => (seat == mySeat) ? (center + new Vector2(-96f, ry * 0.52f)) : OnTable(seat, 0.58f);
		Vector2 HoleAt(int seat) => (seat == mySeat) ? (center + new Vector2(86f, ry * 0.5f)) : (OnTable(seat, 0.78f) + new Vector2(-8f, 0f));
		Vector2 Where(string slot)
		{
			string[] p = slot.Split(':');
			switch (p[0])
			{
			case "deck":
				return deck;
			case "pot":
				return potAt;
			case "bet":
				return BetAt(int.Parse(p[1]));
			case "stack":
				return OnTable(int.Parse(p[1]), 1.0f);
			case "board":
				return BoardSlot(center, int.Parse(p[1]));
			case "hole":
			{
				int seat = int.Parse(p[1]);
				return HoleAt(seat) + new Vector2(int.Parse(p[2]) * (seat == mySeat ? 40f : 16f), 0f);
			}
			default:
				return center;
			}
		}
		MoveDealer(core, Where);
		CasinoArt.MoonLord(sb, core, DealerScale, _leftMove + Breath(0), _rightMove + Breath(1), _leftFrame, _rightFrame, _look);
		List<int> behind = new List<int>();
		List<int> front = new List<int>();
		for (int s = 0; s < PokerTable.MaxSeats; s++)
		{
			if (!t.Seats[s].Empty)
			{
				((Angle(s) > 180.0 && Angle(s) < 360.0) ? behind : front).Add(s);
			}
		}
		foreach (int s in behind)
		{
			DrawPokerPerson(sb, t, s, OnTable(s, 1.14f), OnTable(s, 1.14f).X < center.X);
		}
		CasinoArt.Table(sb, table, new Color(40, 150, 92), new Color(20, 92, 56));
		CasinoArt.Shoe(sb, deck);
		GameArt.Sharp(sb);
		for (int i = 0; i < 5; i++)
		{
			Vector2 slot = BoardSlot(center, i);
			Vector2 size = CasinoArt.CardSize(1.25f);
			if (i < t.BoardCount && Arrived("board:" + i))
			{
				CasinoArt.Card(sb, slot - size / 2f, 1.25f, t.Board[i], true, 1f, Math.Max(0.05f, FlipOf("board:" + i)), Lit(t, t.Board[i]));
			}
		}
		int middle = t.Seats.Sum(s => s.Committed - s.Bet);
		float potW = (middle > 0) ? CasinoArt.Chips(sb, potAt, middle, 1.3f, 7) : 0f;
		GameArt.Smooth(sb);
		if (middle > 0)
		{
			CasinoArt.Pill(sb, potAt + new Vector2(0f, 18f), middle.ToString("N0"), Gold, 0.68f);
		}
		for (int s = 0; s < PokerTable.MaxSeats; s++)
		{
			PokerSeat seat = t.Seats[s];
			if (seat.Empty)
			{
				continue;
			}
			bool me = s == mySeat;
			GameArt.Sharp(sb);
			if (seat.Dealt && t.Phase != PokerPhase.Waiting)
			{
				Vector2 spot = HoleAt(s);
				float scale = me ? 1.55f : 0.8f;
				for (int i = 0; i < 2; i++)
				{
					string key = "hole:" + s + ":" + i;
					if (!Arrived(key) || (seat.Folded && !me))
					{
						continue;
					}
					byte card = Cards.NoCard;
					bool up = false;
					if (seat.Shown[i] != Cards.NoCard)
					{
						card = seat.Shown[i];
						up = true;
					}
					else if (me && CasinoNet.MyHoleHand == t.HandNo)
					{
						card = CasinoNet.MyHole[i];
						up = card != Cards.NoCard;
					}
					Vector2 size = CasinoArt.CardSize(scale);
					Vector2 at = spot + new Vector2(i * (me ? 40f : 16f), me ? i * 4f : 0f) - size / 2f;
					CasinoArt.Card(sb, at, scale, card, up, seat.Folded ? 0.45f : 1f, up ? FlipOf(key) : 1f, up && Lit(t, card));
				}
			}
			float chipsW = (seat.Bet > 0) ? CasinoArt.Chips(sb, BetAt(s), seat.Bet, 1.1f, 5) : 0f;
			if (s == t.Button && t.Phase != PokerPhase.Waiting)
			{
				CasinoArt.DealerButton(sb, BetAt(s) + new Vector2(me ? 34f : -34f, -26f), 1.1f);
			}
			GameArt.Smooth(sb);
			if (seat.Bet > 0)
			{
				string bet = seat.Bet.ToString("N0");
				CasinoArt.Pill(sb, BetAt(s) + new Vector2(0f, 16f), bet, Cream, 0.6f, chip: false);
			}
		}
		DrawFlights(sb, Where, 0.8f);
		foreach (int s in front)
		{
			DrawPokerPerson(sb, t, s, OnTable(s, 1.14f), OnTable(s, 1.14f).X < center.X);
		}
		foreach (int s in behind.Concat(front))
		{
			Vector2 at = OnTable(s, 1.14f);
			double a = Angle(s);
			Vector2 plate = (a > 220.0 && a < 320.0) ? (at + new Vector2((at.X < center.X) ? -100f : 100f, -58f)) : ((a > 180.0) ? (at + new Vector2(0f, -112f)) : (at + new Vector2(0f, 38f)));
			DrawPokerPlate(sb, t, s, plate);
		}
		if (t.Phase == PokerPhase.Showdown && !t.QuietWin && t.WinningScore > 0 && Now > FlightsEnd - 0.6)
		{
			Banner(sb, new Vector2(center.X, center.Y - ry * 0.66f), IsaClient.Languages.L10n.T(PokerHands.Name(t.WinningScore)));
		}
		if (CasinoNet.Hosting)
		{
			// the host changes the computer players from the corner
			if (CasinoArt.Button(sb, "casino:addbot", new Rectangle(room.Right - 76, room.Y + 8, 32, 30), "+", PlainButton, "Another computer player sits down", t.Seated < PokerTable.MaxSeats))
			{
				CasinoNet.AddBot();
			}
			if (CasinoArt.Button(sb, "casino:rembot", new Rectangle(room.Right - 40, room.Y + 8, 32, 30), "-", PlainButton, "A computer player gets up (after the hand)", t.Seats.Any(s => s.Kind == SeatKind.Bot)))
			{
				CasinoNet.RemoveBot();
			}
		}
		DrawNotice(sb, new Rectangle(room.X + 10, room.Y + 10, 380, 24));
		DrawPokerBar(sb, bar, t, mySeat);
	}

	private static Vector2 BoardSlot(Vector2 center, int i) => center + new Vector2((i - 2) * 56f, -26f);

	/// <summary>A banner in Terraria's panel style (the winning hand).</summary>
	private static void Banner(SpriteBatch sb, Vector2 center, string text)
	{
		float w = MusicDrawing.Measure(text, 0.9f);
		Rectangle r = new Rectangle((int)(center.X - w / 2f - 22f), (int)center.Y - 17, (int)w + 44, 34);
		GameOverlay.MenuPanel(sb, r, new Color(70, 44, 18) * 0.96f, Gold);
		MusicDrawing.RawText(sb, text, r.X + 22, r.Y + 7, Gold, 0.9f);
	}

	/// <summary>Is this card part of the winning hand being shown?</summary>
	private static bool Lit(PokerTable t, byte card)
	{
		if (t.Phase != PokerPhase.Showdown || t.QuietWin || card == Cards.NoCard || t.WinningScore == 0)
		{
			return false;
		}
		foreach (PokerSeat s in t.Seats)
		{
			if (s.Won > 0 && s.Shown[0] != Cards.NoCard)
			{
				byte[] seven = s.Shown.Concat(t.Board.Take(t.BoardCount)).ToArray();
				if (!seven.Contains(card))
				{
					continue;
				}
				byte[] without = seven.Where(c => c != card).ToArray();
				if (without.Length >= 5 && PokerHands.Best(without) < PokerHands.Best(seven))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static Player PlayerOf(SeatKind kind, int player, int bot)
	{
		if (kind == SeatKind.Bot)
		{
			return CasinoArt.Bot(bot);
		}
		return (player >= 0 && player < 255 && Main.player[player] != null && Main.player[player].active) ? Main.player[player] : null;
	}

	/// <summary>Someone at the table: a casino chair and the character sitting on it, facing the felt.</summary>
	private static void DrawPokerPerson(SpriteBatch sb, PokerTable t, int s, Vector2 at, bool faceRight)
	{
		PokerSeat seat = t.Seats[s];
		Vector2 feet = at + new Vector2(0f, 30f);
		CasinoArt.Chair(sb, feet, faceRight, 1.4f);
		Player p = PlayerOf(seat.Kind, seat.Player, seat.Bot);
		if (p != null)
		{
			CasinoArt.Seated(sb, p, feet + new Vector2(0f, -10f), faceRight, 1.4f, Resume);
		}
	}

	/// <summary>A seat's plate (top middle at <paramref name="at"/>): name, chips, what they did; the clock on their turn.</summary>
	private static void DrawPokerPlate(SpriteBatch sb, PokerTable t, int s, Vector2 at)
	{
		PokerSeat seat = t.Seats[s];
		bool turn = s == t.ToAct && t.Phase >= PokerPhase.Preflop && t.Phase <= PokerPhase.River;
		bool won = seat.Won > 0 && t.Phase == PokerPhase.Showdown;
		float dim = (seat.Folded || seat.SittingOut) ? 0.7f : 1f;
		Rectangle plate = new Rectangle((int)at.X - 64, (int)at.Y, 128, 42);
		CasinoArt.Plate(sb, plate, turn, won, dim);
		MusicDrawing.RawText(sb, CasinoNet.SeatName(seat), plate.X + 10, plate.Y + 5, Cream * dim, 0.6f, plate.Width - 20);
		GameArt.Sharp(sb);
		CasinoArt.Chip(sb, new Vector2(plate.X + 16, plate.Y + 28), 1000, 0.7f);
		GameArt.Smooth(sb);
		MusicDrawing.RawText(sb, seat.Stack.ToString("N0"), plate.X + 26, plate.Y + 20, Gold * dim, 0.62f);
		string state = seat.SittingOut ? IsaClient.Languages.L10n.T("Away") : (seat.Leaving ? IsaClient.Languages.L10n.T("Leaving") : (seat.AllIn ? IsaClient.Languages.L10n.T("All in") : (seat.Folded ? IsaClient.Languages.L10n.T("Fold") : IsaClient.Languages.L10n.T(PokerAction.Name(seat.LastAction)))));
		Color stateColor = seat.AllIn ? CasinoArt.SoftRed : Muted;
		if (won)
		{
			state = "+" + seat.Won.ToString("N0");
			stateColor = CasinoArt.SoftGreen;
		}
		if (!string.IsNullOrEmpty(state))
		{
			float w = MusicDrawing.Measure(state, 0.54f);
			MusicDrawing.RawText(sb, state, plate.Right - 10 - w, plate.Y + 21, stateColor, 0.54f);
		}
		if (turn)
		{
			CasinoArt.Clock(sb, plate, (float)(t.Clock / PokerTable.TurnSeconds));
		}
	}

	private static void DrawNotice(SpriteBatch sb, Rectangle r)
	{
		if (CasinoNet.Notice == null || Now - CasinoNet.NoticeAt > 6.0)
		{
			return;
		}
		float a = GameArt.Clamp01(6.0 - (Now - CasinoNet.NoticeAt));
		float w = MusicDrawing.Measure(CasinoNet.Notice, 0.64f);
		GameOverlay.MenuPanel(sb, new Rectangle(r.X, r.Y, (int)Math.Min(r.Width, w + 24), r.Height), new Color(20, 24, 48) * (0.92f * a), new Color(8, 10, 22) * a);
		MusicDrawing.RawText(sb, CasinoNet.Notice, r.X + 12, r.Y + 4, Cream * a, 0.64f, r.Width - 24);
	}

	private static void BarBack(SpriteBatch sb, Rectangle bar)
	{
		GameOverlay.MenuPanel(sb, bar, new Color(28, 32, 62) * 0.97f, new Color(10, 12, 24));
	}

	private static void DrawPokerBar(SpriteBatch sb, Rectangle bar, PokerTable t, int mySeat)
	{
		BarBack(sb, bar);
		int x = bar.X + 12;
		int y = bar.Y + 11;
		if (mySeat < 0)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Taking a seat..."), x, y + 8, Muted, 0.7f);
			return;
		}
		PokerSeat me = t.Seats[mySeat];
		bool myTurn = t.ToAct == mySeat && t.Phase >= PokerPhase.Preflop && t.Phase <= PokerPhase.River;
		// right: your hand and your chance to win it
		int right = bar.Right - 14;
		double equity = MyEquity(t, mySeat);
		if (CasinoNet.MyHoleHand == t.HandNo && CasinoNet.MyHole[0] != Cards.NoCard && me.Live)
		{
			string hand = (t.BoardCount >= 3) ? IsaClient.Languages.L10n.T(PokerHands.Name(PokerHands.Best(CasinoNet.MyHole.Concat(t.Board.Take(t.BoardCount)).ToArray()))) : (Cards.RankText(Cards.Rank(CasinoNet.MyHole[0])) + Cards.RankText(Cards.Rank(CasinoNet.MyHole[1])) + ((Cards.Suit(CasinoNet.MyHole[0]) == Cards.Suit(CasinoNet.MyHole[1])) ? IsaClient.Languages.L10n.T(" suited") : ""));
			float hw = MusicDrawing.Measure(hand, 0.66f);
			MusicDrawing.RawText(sb, hand, right - hw, y - 1, Cream, 0.66f);
			if (equity >= 0.0)
			{
				string win = IsaClient.Languages.L10n.F("Win {0}%", (int)Math.Round(equity * 100.0));
				Color c = (equity >= 0.5) ? CasinoArt.SoftGreen : ((equity >= 0.25) ? Gold : CasinoArt.SoftRed);
				float ww = MusicDrawing.Measure(win, 0.66f);
				MusicDrawing.RawText(sb, win, right - ww, y + 18, c, 0.66f);
				Rectangle meter = new Rectangle(right - (int)ww - 70, y + 25, 60, 5);
				MusicDrawing.Fill(sb, new Rectangle(meter.X - 1, meter.Y - 1, meter.Width + 2, meter.Height + 2), Color.Black * 0.6f);
				MusicDrawing.Fill(sb, new Rectangle(meter.X, meter.Y, (int)(meter.Width * equity), meter.Height), c);
			}
		}
		if (me.Stack == 0 && !me.Dealt || me.SittingOut)
		{
			int amount = t.BigBlind * 100;
			if (CasinoArt.Button(sb, "casino:rebuy", new Rectangle(x, y, 230, 36), IsaClient.Languages.L10n.F("Buy {0} chips", amount.ToString("N0")), GreenButton, "From your wallet (the house gives some when it's empty), and back in the game"))
			{
				CasinoNet.AddChips(amount);
			}
			return;
		}
		if (!myTurn)
		{
			string wait = "";
			if (t.Phase == PokerPhase.Waiting)
			{
				wait = (t.Seats.Count(s => !s.Empty && !s.SittingOut && s.Stack > 0) < 2) ? IsaClient.Languages.L10n.T("Waiting for another player...") : IsaClient.Languages.L10n.T("Shuffling...");
			}
			else if (t.Phase != PokerPhase.Showdown && t.ToAct >= 0)
			{
				wait = IsaClient.Languages.L10n.F("{0} is thinking...", CasinoNet.SeatName(t.Seats[t.ToAct]));
			}
			MusicDrawing.RawText(sb, wait, x, y + 8, Muted, 0.7f, right - x - 220);
			return;
		}
		int toCall = t.ToCall(mySeat);
		int min = t.MinRaiseTo(mySeat);
		int max = t.MaxRaiseTo(mySeat);
		ushort key = (ushort)(t.HandNo * 7 + t.BoardCount * 131 + t.CurrentBet);
		if (_raiseFor != key || _raiseTo < min || _raiseTo > max)
		{
			_raiseFor = key;
			_raiseTo = min;
		}
		if (CasinoArt.Button(sb, "casino:fold", new Rectangle(x, y, 88, 36), "Fold", RedButton))
		{
			CasinoNet.Act(PokerAction.Fold, 0);
		}
		x += 94;
		string call = (toCall == 0) ? IsaClient.Languages.L10n.T("Check") : IsaClient.Languages.L10n.F("Call {0}", Math.Min(toCall, me.Stack).ToString("N0"));
		string callTip = (toCall == 0) ? null : IsaClient.Languages.L10n.F("Worth it if you win more than {0}% of the time", (int)Math.Round(100.0 * toCall / (t.Pot + toCall)));
		if (CasinoArt.Button(sb, "casino:call", new Rectangle(x, y, 128, 36), call, GreenButton, callTip))
		{
			CasinoNet.Act((toCall == 0) ? PokerAction.Check : PokerAction.Call, 0);
		}
		x += 136;
		if (max > t.CurrentBet && me.Stack > toCall)
		{
			int step = t.BigBlind;
			if (CasinoArt.Button(sb, "casino:less", new Rectangle(x, y + 4, 28, 28), "-", PlainButton))
			{
				_raiseTo = Math.Max(min, _raiseTo - step);
			}
			x += 30;
			Rectangle amount = new Rectangle(x, y + 4, 84, 28);
			GameOverlay.MenuPanel(sb, amount, new Color(12, 12, 24), new Color(8, 8, 16));
			string text = _raiseTo.ToString("N0");
			MusicDrawing.RawText(sb, text, (float)Math.Round(amount.Center.X - MusicDrawing.Measure(text, 0.74f) / 2f), amount.Y + 5, Gold, 0.74f);
			if (MusicPlayerWindow.MouseIn(amount) && MusicPlayerWindow.WheelDelta != 0)
			{
				_raiseTo = Math.Max(min, Math.Min(max, _raiseTo + Math.Sign(MusicPlayerWindow.WheelDelta) * step));
				MusicPlayerWindow.WheelDelta = 0;
			}
			x += 86;
			if (CasinoArt.Button(sb, "casino:more", new Rectangle(x, y + 4, 28, 28), "+", PlainButton))
			{
				_raiseTo = Math.Min(max, _raiseTo + step);
			}
			x += 34;
			int pot = t.Pot + toCall;
			foreach ((string label, int to, string tip) in new[] { ("1/2", t.CurrentBet + pot / 2, "Half the pot"), ("Pot", t.CurrentBet + pot, "The size of the pot") })
			{
				int w = (int)MusicDrawing.Measure(label, 0.72f) + 20;
				if (CasinoArt.Button(sb, "casino:pick:" + label, new Rectangle(x, y + 4, w, 28), label, PlainButton, tip))
				{
					_raiseTo = Math.Max(min, Math.Min(max, to));
				}
				x += w + 4;
			}
			x += 4;
			bool allIn = _raiseTo >= max;
			string raise = allIn ? IsaClient.Languages.L10n.T("All in") : IsaClient.Languages.L10n.F((t.CurrentBet == 0) ? "Bet {0}" : "Raise {0}", _raiseTo.ToString("N0"));
			if (CasinoArt.Button(sb, "casino:raise", new Rectangle(x, y, 128, 36), raise, GoldButton))
			{
				CasinoNet.Act(allIn ? PokerAction.AllIn : ((t.CurrentBet == 0) ? PokerAction.Bet : PokerAction.Raise), _raiseTo);
			}
			x += 134;
		}
		if (me.Stack > 0 && CasinoArt.Button(sb, "casino:allin", new Rectangle(x, y, 84, 36), "All in", new Color(160, 46, 30), IsaClient.Languages.L10n.F("Everything: {0}", (me.Stack + me.Bet).ToString("N0"))))
		{
			CasinoNet.Act(PokerAction.AllIn, max);
		}
	}

	#endregion

	#region Blackjack table

	private static int _bet;

	private static void DrawBlackjack(SpriteBatch sb, Rectangle body, BlackjackTable t)
	{
		int mySeat = t.SeatOf(CasinoNet.Me);
		Rectangle bar = new Rectangle(body.X, body.Bottom - 58, body.Width, 58);
		Rectangle room = new Rectangle(body.X, body.Y, body.Width, bar.Y - body.Y - 4);
		CasinoArt.Room(sb, room, 0.34f);
		Rectangle table = new Rectangle(room.X + 50, room.Y + 134, room.Width - 100, room.Height - 238);
		Vector2 center = new Vector2(table.Center.X, table.Center.Y);
		float spacing = table.Width / 4.6f;
		float SeatX(int seat) => center.X + (seat - 1.5f) * spacing;
		Vector2 Person(int seat) => new Vector2(SeatX(seat), table.Bottom + 6f);
		Vector2 core = new Vector2(center.X, table.Y - 14f);
		// the shoe under his right hand, the chip rack under his left
		Vector2 deck = CasinoArt.HandOnScreen(core, DealerScale, Vector2.Zero, false) + new Vector2(10f, 46f);
		Vector2 tray = CasinoArt.HandOnScreen(core, DealerScale, Vector2.Zero, true) + new Vector2(-30f, 46f);
		Vector2 DealerCard(int i) => new Vector2(center.X - 26f + i * 34f, table.Y + 84f);
		Vector2 HandCard(int seat, int hand, int i, int hands)
		{
			Vector2 c = new Vector2(SeatX(seat) + ((hands > 1) ? (hand - 0.5f) * 74f : 0f), table.Bottom - 146f);
			return c + new Vector2(i * 15f - 10f, -i * 9f);
		}
		Vector2 BetSpot(int seat, int hand, int hands) => new Vector2(SeatX(seat) + ((hands > 1) ? (hand - 0.5f) * 46f : 0f), table.Bottom - 72f);
		Vector2 Where(string slot)
		{
			string[] p = slot.Split(':');
			switch (p[0])
			{
			case "deck":
				return deck;
			case "tray":
				return tray;
			case "dealer":
				return DealerCard(int.Parse(p[1]));
			case "bj":
			{
				int s = int.Parse(p[1]);
				return HandCard(s, int.Parse(p[2]), int.Parse(p[3]), Math.Max(1, t.Seats[s].Hands.Count));
			}
			case "bjbet":
			{
				int s = int.Parse(p[1]);
				return BetSpot(s, int.Parse(p[2]), Math.Max(1, t.Seats[s].Hands.Count));
			}
			default:
				return center;
			}
		}
		MoveDealer(core, Where);
		CasinoArt.MoonLord(sb, core, DealerScale, _leftMove + Breath(0), _rightMove + Breath(1), _leftFrame, _rightFrame, _look);
		CasinoArt.Table(sb, table, new Color(176, 40, 62), new Color(112, 20, 40));
		// what the felt says, printed on an arc like a real table
		CasinoArt.Print(sb, "BLACKJACK PAYS 3 TO 2", new Vector2(center.X, table.Y + 132f), 0.78f, Gold * 0.8f, 330f);
		CasinoArt.Shoe(sb, deck);
		CasinoArt.ChipRack(sb, tray);
		GameArt.Sharp(sb);
		for (int i = 0; i < t.Dealer.Count; i++)
		{
			string key = "dealer:" + i;
			if (!Arrived(key) && !(i == 1 && t.HoleShown && Now >= _holeFlipAt))
			{
				continue;
			}
			// the dealer's second card stays face down until the dealer plays (the host's own table knows it: never show it)
			bool up = t.Dealer[i] != Cards.NoCard && (i != 1 || t.HoleShown);
			Vector2 size = CasinoArt.CardSize(1.2f);
			CasinoArt.Card(sb, DealerCard(i) - size / 2f, 1.2f, t.Dealer[i], up, 1f, up ? FlipOf(key) : 1f);
		}
		GameArt.Smooth(sb);
		if (t.Dealer.Count > 0)
		{
			bool all = t.HoleShown;
			int v = all ? BlackjackHands.Value(t.Dealer) : BlackjackHands.Value(new[] { t.Dealer[0] });
			string text = all ? ((v > 21) ? IsaClient.Languages.L10n.T("Bust") : (BlackjackHands.Natural(t.Dealer) ? "Blackjack" : v.ToString())) : v.ToString();
			CasinoArt.Pill(sb, DealerCard(0) + new Vector2(-60f, 0f), text, (v > 21) ? CasinoArt.SoftRed : Cream, 0.7f, chip: false);
		}
		for (int s = 0; s < BlackjackTable.MaxSeats; s++)
		{
			BlackjackSeat seat = t.Seats[s];
			int hands = Math.Max(1, seat.Hands.Count);
			GameArt.Sharp(sb);
			for (int h = 0; h < hands; h++)
			{
				CasinoArt.BetRing(sb, BetSpot(s, h, hands), 1.15f, Gold * 0.75f);
			}
			if (seat.Empty)
			{
				GameArt.Smooth(sb);
				continue;
			}
			if (seat.Hands.Count == 0 && seat.Bet > 0)
			{
				CasinoArt.Chips(sb, BetSpot(s, 0, 1) + new Vector2(0f, 10f), seat.Bet, 1.15f, 5);
			}
			for (int h = 0; h < seat.Hands.Count; h++)
			{
				BlackjackHand hand = seat.Hands[h];
				string betKey = "bjbet:" + s + ":" + h;
				bool chipsGone = hand.Result == 1 && t.Phase == BlackjackPhase.Payout && !_flights.Any(f => f.From == betKey && Now < f.Start);
				if (!chipsGone)
				{
					CasinoArt.Chips(sb, BetSpot(s, h, hands) + new Vector2(0f, 10f), hand.Bet, 1.15f, 5);
				}
				for (int c = 0; c < hand.Cards.Count; c++)
				{
					string key = "bj:" + s + ":" + h + ":" + c;
					if (!Arrived(key))
					{
						continue;
					}
					Vector2 size = CasinoArt.CardSize(1.1f);
					CasinoArt.Card(sb, HandCard(s, h, c, hands) - size / 2f, 1.1f, hand.Cards[c], true, 1f, FlipOf(key));
				}
			}
			GameArt.Smooth(sb);
			for (int h = 0; h < seat.Hands.Count; h++)
			{
				BlackjackHand hand = seat.Hands[h];
				int v = BlackjackHands.Value(hand.Cards, out bool soft);
				string text = hand.Natural ? "Blackjack" : ((v > 21) ? IsaClient.Languages.L10n.T("Bust") : ((soft && v < 21) ? ((v - 10) + "/" + v) : v.ToString()));
				Color color = (v > 21) ? CasinoArt.SoftRed : (hand.Natural ? Gold : Cream);
				if (hand.Result != 0 && t.Phase == BlackjackPhase.Payout)
				{
					(text, color) = hand.Result switch
					{
						4 => ("+" + (hand.Bet * 3 / 2).ToString("N0"), Gold),
						3 => ("+" + hand.Bet.ToString("N0"), CasinoArt.SoftGreen),
						2 => (IsaClient.Languages.L10n.T("Push"), Cream),
						_ => ("-" + hand.Bet.ToString("N0"), CasinoArt.SoftRed)
					};
				}
				bool turn = s == t.ToActSeat && h == t.ToActHand && t.Phase == BlackjackPhase.Playing;
				// the hand's value, left of its cards (gold when it's the hand playing)
				Vector2 side = HandCard(s, h, 0, hands) + new Vector2(-32f - MusicDrawing.Measure(text, 0.66f) / 2f, -6f);
				CasinoArt.Pill(sb, side, text, turn ? Color.White : color, 0.66f, chip: false, fill: turn ? new Color(120, 84, 22) : (Color?)null);
			}
		}
		DrawFlights(sb, Where, 1.1f);
		for (int s = 0; s < BlackjackTable.MaxSeats; s++)
		{
			BlackjackSeat seat = t.Seats[s];
			Vector2 at = Person(s);
			Vector2 feet = at + new Vector2(0f, 34f);
			bool faceRight = at.X < center.X;
			CasinoArt.Chair(sb, feet, faceRight, 1.4f);
			if (seat.Empty)
			{
				continue;
			}
			Player p = PlayerOf(SeatKind.Player, seat.Player, 0);
			if (p != null)
			{
				CasinoArt.Seated(sb, p, feet + new Vector2(0f, -10f), faceRight, 1.4f, Resume);
			}
		}
		for (int s = 0; s < BlackjackTable.MaxSeats; s++)
		{
			BlackjackSeat seat = t.Seats[s];
			if (seat.Empty)
			{
				continue;
			}
			Vector2 at = Person(s);
			bool turn = s == t.ToActSeat && t.Phase == BlackjackPhase.Playing;
			Rectangle plate = new Rectangle((int)at.X - 64, (int)at.Y + 40, 128, 42);
			CasinoArt.Plate(sb, plate, turn, false);
			MusicDrawing.RawText(sb, GameLobby.Name(seat.Player), plate.X + 10, plate.Y + 5, Cream, 0.6f, plate.Width - 20);
			GameArt.Sharp(sb);
			CasinoArt.Chip(sb, new Vector2(plate.X + 16, plate.Y + 28), 1000, 0.7f);
			GameArt.Smooth(sb);
			MusicDrawing.RawText(sb, seat.Stack.ToString("N0"), plate.X + 26, plate.Y + 20, Gold, 0.62f);
			if (t.Phase == BlackjackPhase.Betting)
			{
				string ready = seat.Ready ? ((seat.Bet > 0) ? IsaClient.Languages.L10n.T("Ready") : IsaClient.Languages.L10n.T("Out")) : "...";
				float w = MusicDrawing.Measure(ready, 0.54f);
				MusicDrawing.RawText(sb, ready, plate.Right - 10 - w, plate.Y + 21, seat.Ready ? CasinoArt.SoftGreen : Muted, 0.54f);
			}
			if (turn)
			{
				CasinoArt.Clock(sb, plate, (float)(t.Clock / BlackjackTable.TurnSeconds));
			}
		}
		DrawNotice(sb, new Rectangle(room.X + 10, room.Y + 10, 380, 24));
		DrawBlackjackBar(sb, bar, t, mySeat);
	}

	/// <summary>A small gauge with a label and a percentage.</summary>
	private static void Odds(SpriteBatch sb, int x, int y, string label, double chance, Color color)
	{
		string pct = (int)Math.Round(chance * 100.0) + "%";
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), x, y, Muted, 0.56f);
		float lw = MusicDrawing.Measure(IsaClient.Languages.L10n.T(label), 0.56f);
		MusicDrawing.RawText(sb, pct, x + lw + 6, y - 1, color, 0.66f);
		Rectangle meter = new Rectangle(x, y + 16, 110, 4);
		MusicDrawing.Fill(sb, meter, Color.Black * 0.5f);
		MusicDrawing.Fill(sb, new Rectangle(meter.X, meter.Y, (int)(meter.Width * chance), meter.Height), color);
	}

	private static void DrawBlackjackBar(SpriteBatch sb, Rectangle bar, BlackjackTable t, int mySeat)
	{
		BarBack(sb, bar);
		int x = bar.X + 12;
		int y = bar.Y + 11;
		if (mySeat < 0)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Taking a seat..."), x, y + 8, Muted, 0.7f);
			return;
		}
		BlackjackSeat me = t.Seats[mySeat];
		// your chips, always, on the right
		int right = bar.Right - 12;
		Rectangle mine = CasinoArt.Pill(sb, new Vector2(right - 60, bar.Center.Y), me.Stack.ToString("N0"), Gold, 0.8f);
		if (MusicPlayerWindow.MouseIn(mine))
		{
			MusicDrawing.Hover("casino:bjstack", IsaClient.Languages.L10n.T("Your chips at this table"));
		}
		right = mine.X - 14;
		if (t.Phase == BlackjackPhase.Betting)
		{
			if (me.Ready)
			{
				string text = (me.Bet > 0) ? IsaClient.Languages.L10n.F("Bet {0}  ·  {1}s", me.Bet.ToString("N0"), (int)Math.Ceiling(t.Clock)) : IsaClient.Languages.L10n.T("You sit this one out.");
				MusicDrawing.RawText(sb, text, x, y + 8, Muted, 0.7f, right - x);
				return;
			}
			if (me.Stack < t.MinBet)
			{
				int amount = t.MinBet * 40;
				if (CasinoArt.Button(sb, "casino:bjrebuy", new Rectangle(x, y, 230, 36), IsaClient.Languages.L10n.F("Buy {0} chips", amount.ToString("N0")), GreenButton, "From your wallet (the house gives some when it's empty)"))
				{
					CasinoNet.AddChips(amount);
				}
				return;
			}
			if (_bet <= 0)
			{
				_bet = Math.Max(t.MinBet, Math.Min(me.LastBet, me.Stack));
			}
			_bet = Math.Max(t.MinBet, Math.Min(Math.Min(t.MaxBet, me.Stack), _bet));
			// chips to click: each adds its value (right click takes it away)
			GameArt.Sharp(sb);
			int[] steps = { t.MinBet, t.MinBet * 5, t.MinBet * 25 };
			foreach (int step in steps)
			{
				Rectangle chip = new Rectangle(x, y, 36, 36);
				bool hover = MusicPlayerWindow.MouseIn(chip);
				CasinoArt.Chip(sb, new Vector2(chip.Center.X, chip.Center.Y - (hover ? 2 : 0)), CasinoArt.ChipFor(step), hover ? 2.05f : 1.9f);
				if (hover)
				{
					MusicDrawing.Hover("casino:chip:" + step, IsaClient.Languages.L10n.F("+{0}  (right click: -{0})", step.ToString("N0")));
					if (MusicPlayerWindow.Clicked)
					{
						MusicPlayerWindow.Clicked = false;
						_bet = Math.Min(Math.Min(t.MaxBet, me.Stack), _bet + step);
						SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
					}
					else if (MusicPlayerWindow.RightClicked)
					{
						_bet = Math.Max(t.MinBet, _bet - step);
					}
				}
				x += 40;
			}
			GameArt.Smooth(sb);
			x += 6;
			Rectangle amount2 = new Rectangle(x, y + 4, 100, 28);
			GameOverlay.MenuPanel(sb, amount2, new Color(12, 12, 24), new Color(8, 8, 16));
			string bet = _bet.ToString("N0");
			MusicDrawing.RawText(sb, bet, amount2.Center.X - MusicDrawing.Measure(bet, 0.78f) / 2f, amount2.Y + 5, Gold, 0.78f);
			x += 106;
			if (CasinoArt.Button(sb, "casino:bjmin", new Rectangle(x, y + 4, 54, 28), "Min", PlainButton))
			{
				_bet = t.MinBet;
			}
			x += 60;
			if (CasinoArt.Button(sb, "casino:bjdeal", new Rectangle(x, y, 140, 36), "Deal", GreenButton, "Put the bet and get the cards"))
			{
				CasinoNet.Act(BlackjackAction.Bet, _bet);
			}
			x += 146;
			if (t.Seated > 1 && CasinoArt.Button(sb, "casino:bjskip", new Rectangle(x, y, 110, 36), "Sit out", PlainButton, "Skip this round (the others play)"))
			{
				CasinoNet.Act(BlackjackAction.Bet, 0);
			}
			return;
		}
		bool myTurn = t.Phase == BlackjackPhase.Playing && t.ToActSeat == mySeat;
		// the odds of your hand: busting if you take a card, the dealer busting
		BlackjackHand hand = (t.ToActSeat == mySeat && t.ToActHand < me.Hands.Count) ? me.Hands[t.ToActHand] : me.Hands.FirstOrDefault();
		if (hand != null && t.Dealer.Count > 0 && t.Phase == BlackjackPhase.Playing)
		{
			double bust = BlackjackHands.BustChance(hand.Cards);
			double dealer = BlackjackHands.DealerBustChance(t.Dealer[0]);
			Odds(sb, right - 250, y + 1, "Bust if you hit", bust, (bust > 0.5) ? CasinoArt.SoftRed : ((bust > 0.25) ? Gold : CasinoArt.SoftGreen));
			Odds(sb, right - 120, y + 1, "Dealer busts", dealer, (dealer > 0.35) ? CasinoArt.SoftGreen : Gold);
		}
		if (!myTurn)
		{
			string wait = (t.Phase == BlackjackPhase.Playing && t.ToActSeat >= 0) ? IsaClient.Languages.L10n.F("{0} is playing...", GameLobby.Name(t.Seats[t.ToActSeat].Player)) : ((t.Phase == BlackjackPhase.Dealer) ? IsaClient.Languages.L10n.T("The dealer plays...") : "");
			MusicDrawing.RawText(sb, wait, x, y + 8, Muted, 0.7f, right - x - 270);
			return;
		}
		byte best = BlackjackHands.BestPlay(hand.Cards, t.Dealer[0], t.CanDouble(mySeat), t.CanSplit(mySeat));
		if (CasinoArt.Button(sb, "casino:hit", new Rectangle(x, y, 104, 36), "casino::Hit", GreenButton, "One more card", true, best == BlackjackAction.Hit))
		{
			CasinoNet.Act(BlackjackAction.Hit, 0);
		}
		x += 110;
		if (CasinoArt.Button(sb, "casino:stand", new Rectangle(x, y, 104, 36), "Stand", RedButton, "No more cards", true, best == BlackjackAction.Stand))
		{
			CasinoNet.Act(BlackjackAction.Stand, 0);
		}
		x += 110;
		if (CasinoArt.Button(sb, "casino:double", new Rectangle(x, y, 104, 36), "casino::Double", GoldButton, "Double the bet, take exactly one card", t.CanDouble(mySeat), best == BlackjackAction.Double))
		{
			CasinoNet.Act(BlackjackAction.Double, 0);
		}
		x += 110;
		if (CasinoArt.Button(sb, "casino:split", new Rectangle(x, y, 104, 36), "Split", BlueButton, "Two hands from a pair (same bet on the second)", t.CanSplit(mySeat), best == BlackjackAction.Split))
		{
			CasinoNet.Act(BlackjackAction.Split, 0);
		}
	}

	#endregion
}
