using System;
using System.Linq;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace IsaClient.Casino;

/// <summary>
/// The casino in the chat, for every IsaClient player here (over the pipe, channel <see cref="Channel"/>, each one reads
/// it in their language): who won something big, and who opened a table, with a link in the line that sits you at it.
/// </summary>
public static class CasinoNews
{
	public const byte Channel = 81;

	private const byte BigWin = 1;

	/// <summary>From here a win is told to everyone.</summary>
	public const long Big = 2500;

	/// <summary>Between two wins told by the same player (the pipe is a trickle, the chat isn't for that).</summary>
	private const double Quiet = 20.0;

	/// <summary>The slot machine's top prize.</summary>
	public const int Jackpot = 1;

	private static readonly Color Line = new Color(255, 214, 110);

	private static bool _hooked;

	private static bool _tag;

	private static double _lastTold = -100.0;

	private static double Now => GameArt.Now;

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
	}

	/// <summary>You won: when it's big, everyone hears. <paramref name="detail"/>: Roulette the number, Horses the horse, Slots <see cref="Jackpot"/>.</summary>
	public static void Won(CasinoGame game, long amount, int detail = 0)
	{
		if (amount < Big && !(game == CasinoGame.Slots && detail == Jackpot))
		{
			return;
		}
		if (Now - _lastTold < Quiet && detail != Jackpot)
		{
			return;
		}
		_lastTold = Now;
		Show(Main.LocalPlayer.name, game, amount, detail);
		if (Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready)
		{
			byte[] data = new byte[7];
			data[0] = BigWin;
			data[1] = (byte)game;
			data[2] = (byte)detail;
			BitConverter.GetBytes((int)Math.Min(int.MaxValue, amount)).CopyTo(data, 3);
			PipeNet.Broadcast(Channel, data);
		}
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 7 || m.FromPlayer == Main.myPlayer || Main.gameMenu || m.Data[0] != BigWin)
		{
			return;
		}
		Show(GameLobby.Name(m.FromPlayer), (CasinoGame)m.Data[1], BitConverter.ToInt32(m.Data, 3), m.Data[2]);
	}

	private static void Show(string who, CasinoGame game, long amount, int detail)
	{
		string chips = amount.ToString("N0");
		string text;
		int icon = CasinoWindow.IconItem;
		switch (game)
		{
		case CasinoGame.Poker:
			text = IsaClient.Languages.L10n.F("{0} won a pot of {1} chips at a Poker table!", who, chips);
			break;
		case CasinoGame.Blackjack:
			text = IsaClient.Languages.L10n.F("{0} won {1} chips at Blackjack!", who, chips);
			break;
		case CasinoGame.Slots:
			icon = BetsWindow.BetsItem;
			text = (detail == Jackpot) ? IsaClient.Languages.L10n.F("JACKPOT! {0} won {1} chips on the slot machine!", who, chips) : IsaClient.Languages.L10n.F("{0} won {1} chips on the slot machine!", who, chips);
			break;
		case CasinoGame.Roulette:
			icon = BetsWindow.BetsItem;
			text = IsaClient.Languages.L10n.F("{0} won {1} chips at the Roulette on number {2}!", who, chips, detail);
			break;
		case CasinoGame.Horses:
			icon = BetsWindow.BetsItem;
			text = IsaClient.Languages.L10n.F("{0} won {1} chips at the races with {2}!", who, chips, IsaClient.Languages.L10n.T(HorseRace.Names[Math.Max(0, Math.Min(HorseRace.Horses - 1, detail))]));
			break;
		default:
			return;
		}
		LastLine = "[i:" + icon + "] " + text;
		Main.NewText(LastLine, Line.R, Line.G, Line.B);
	}

	#region A table in the chat

	/// <summary>Someone opened a table: a line with its link. The link finds the table by what its host announces.</summary>
	public static void TableOpened(int host, CasinoGame game, byte table, string stakes)
	{
		RegisterTag();
		string name = GameLobby.Name(host);
		string what;
		if (game == CasinoGame.Roulette)
		{
			what = IsaClient.Languages.L10n.F("{0} is at the Roulette: the table is shared by everyone here.", name);
		}
		else if (game == CasinoGame.Horses)
		{
			what = IsaClient.Languages.L10n.F("{0} is at the races: bet on the same horses.", name);
		}
		else
		{
			what = IsaClient.Languages.L10n.F("{0} opened a {1} table ({2}).", name, CasinoNet.GameName(game), stakes);
		}
		string label = IsaClient.Languages.L10n.T("Join the table").Replace("]", "");
		LastLine = what + " [isatable/" + (int)game + "." + table + "." + host + ":" + label + "]";
		Main.NewText(LastLine, Line.R, Line.G, Line.B);
	}

	/// <summary>The last line told (test harness).</summary>
	internal static string LastLine = "";

	/// <summary>Test harness: clicks the link of the last line.</summary>
	internal static string TestClick()
	{
		foreach (TextSnippet snippet in ChatManager.ParseMessage(LastLine, Color.White))
		{
			if (snippet is TableSnippet)
			{
				bool chat = Main.drawingPlayerChat;
				Main.drawingPlayerChat = true;
				snippet.OnClick();
				Main.drawingPlayerChat = chat;
				return "clicked " + snippet.Text;
			}
		}
		return "no link in: " + LastLine;
	}

	public static void RegisterTag()
	{
		if (!_tag)
		{
			_tag = true;
			ChatManager.Register<TableTagHandler>("isatable");
		}
	}

	/// <summary>The link was clicked: the casino opens at that table, if it's still there.</summary>
	private static void Sit(int game, int table, int host)
	{
		if (Main.gameMenu)
		{
			return;
		}
		if (game == (int)CasinoGame.Roulette || game == (int)CasinoGame.Horses)
		{
			if (!BetsNet.Ads.Any(a => (int)a.Game == game && a.Id == table))
			{
				// Nobody's there anymore: it's your table now.
				ClientChat.Info(IsaClient.Languages.L10n.T("That table is empty now: you start a new one."));
			}
			BetsWindow.OpenGame((CasinoGame)game);
			return;
		}
		if (CasinoNet.AtTable && (int)CasinoNet.Game == game && CasinoNet.TableId == table)
		{
			CasinoWindow.OpenCards();
			return;
		}
		TableAd ad = CasinoNet.Ads.FirstOrDefault(a => (int)a.Game == game && a.Id == table && a.Host == host);
		if (ad == null)
		{
			ClientChat.Error(IsaClient.Languages.L10n.T("That table is closed."));
			return;
		}
		CasinoWindow.OpenCards();
		CasinoWindow.SitAt(ad);
	}

	private sealed class TableTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Color baseColor, string options)
		{
			string[] p = (options ?? "").Split('.');
			if (p.Length == 3 && int.TryParse(p[0], out int game) && int.TryParse(p[1], out int table) && int.TryParse(p[2], out int host))
			{
				return new TableSnippet(game, table, host, text);
			}
			return new TextSnippet(text);
		}
	}

	/// <summary>The link: the casino's cards and an underlined gold text.</summary>
	private sealed class TableSnippet : TextSnippet
	{
		private static readonly Color Link = new Color(255, 222, 120);

		private readonly int _game;

		private readonly int _table;

		private readonly int _host;

		private double _lastHover = -100.0;

		public TableSnippet(int game, int table, int host, string label)
			: base(label, Link)
		{
			_game = game;
			_table = table;
			_host = host;
			CheckForHover = true;
		}

		public override void OnHover()
		{
			_lastHover = Main.timeForVisualEffects;
			Main.instance.MouseText(IsaClient.Languages.L10n.T(Main.drawingPlayerChat ? "Click to sit at the table" : "Open the chat to click links"), 0, 0);
		}

		public override void OnClick()
		{
			if (!Main.drawingPlayerChat)
			{
				return;
			}
			Main.mouseLeftRelease = false;
			SoundEngine.PlaySound(12);
			Sit(_game, _table, _host);
		}

		public override Color GetVisibleColor() => Color;

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
		{
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			Vector2 text = font.MeasureString(Text) * scale;
			float icon = 24f * scale;
			size = new Vector2(icon + 3f + text.X, text.Y);
			if (justCheckingSize)
			{
				return true;
			}
			bool shadow = color.R < 20 && color.G < 20 && color.B < 20;
			bool hovered = Main.timeForVisualEffects - _lastHover <= 2.0;
			if (!shadow)
			{
				if (hovered)
				{
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)position.X - 2, (int)(position.Y + scale), (int)size.X + 4, (int)(size.Y * 0.78f)), Link * 0.2f);
				}
				color = hovered ? Color.White : Link;
				int item = (_game == (int)CasinoGame.Roulette || _game == (int)CasinoGame.Horses) ? BetsWindow.BetsItem : CasinoWindow.IconItem;
				Main.instance.LoadItem(item);
				Texture2D texture = TextureAssets.Item[item].Value;
				float k = Math.Min(icon / texture.Width, icon / texture.Height);
				spriteBatch.Draw(texture, position + new Vector2((icon - texture.Width * k) / 2f, 1f), null, Color.White, 0f, Vector2.Zero, k, SpriteEffects.None, 0f);
			}
			Vector2 at = position + new Vector2(icon + 3f, 0f);
			spriteBatch.DrawString(font, Text, at, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			if (!shadow)
			{
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)at.X, (int)(position.Y + text.Y * 0.72f), (int)text.X, Math.Max(1, (int)(1.5f * scale))), color);
			}
			return true;
		}
	}

	#endregion
}
