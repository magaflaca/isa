using System;
using System.IO;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Identity;
using Newtonsoft.Json.Linq;
using Terraria;

namespace IsaClient.Casino;

/// <summary>
/// Your casino chips. What you can play with comes from how you rank in the other games (the global board without the
/// casino: every point is <see cref="ChipsPerPoint"/> chips, plus <see cref="StartChips"/>), plus what you won or lost
/// at Poker and Blackjack, plus what the house gave you. What you win or lose goes to the Poker and Blackjack boards,
/// and from them to the global one. With no chips left the house always gives a small lot.
/// Kept in IsaClient/casino.json.
/// </summary>
public static class CasinoWallet
{
	public const int StartChips = 1000;

	public const int ChipsPerPoint = 2;

	/// <summary>The house's gift when you're out of chips.</summary>
	public const int Gift = 300;

	public static long PokerNet;

	public static long BlackjackNet;

	/// <summary>Slot machine, Roulette and races. Bets leave it when they go on the table, and what they give back comes in.</summary>
	public static long BetsChips;

	public static int BetsWins;

	public static int BetsLosses;

	public static int BetsEven;

	public static long Gifts;

	public static int GiftCount;

	/// <summary>Chips sitting at a table right now (out of the wallet until you get up).</summary>
	public static long OnTable;

	public static int PokerWins;

	public static int PokerLosses;

	public static int PokerSplits;

	public static int BlackjackWins;

	public static int BlackjackLosses;

	public static int BlackjackPushes;

	private static bool _loaded;

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "casino.json");

	/// <summary>Chips that come from the other games (your global score without the casino).</summary>
	public static long FromGames
	{
		get
		{
			ulong me = ClientIdentity.Id;
			(int tetris, int versus, int chess, int ttt, int snake) = Scores.GlobalParts(me);
			return StartChips + (long)(tetris + versus + chess + ttt + snake) * ChipsPerPoint;
		}
	}

	/// <summary>All your chips, with the ones at a table.</summary>
	public static long Total
	{
		get
		{
			Load();
			return Math.Max(0L, FromGames + PokerNet + BlackjackNet + BetsChips + Gifts);
		}
	}

	/// <summary>Chips you can take to a table now.</summary>
	public static long Free => Math.Max(0L, Total - OnTable);

	/// <summary>A bet goes on the table. False when you don't have the chips.</summary>
	public static bool ChargeBet(long chips)
	{
		Load();
		if (chips <= 0 || chips > Free)
		{
			return false;
		}
		BetsChips -= chips;
		Save();
		return true;
	}

	/// <summary>A bet comes off the table before it was played.</summary>
	public static void RefundBet(long chips)
	{
		Load();
		BetsChips += chips;
		Save();
	}

	/// <summary>A game was played: <paramref name="stake"/> had left already, <paramref name="payout"/> comes back.</summary>
	public static void BetResult(long stake, long payout)
	{
		Load();
		BetsChips += payout;
		if (payout > stake)
		{
			BetsWins++;
		}
		else if (payout < stake)
		{
			BetsLosses++;
		}
		else
		{
			BetsEven++;
		}
		Scores.RecordCasino(Scores.Bets, BetsChips, BetsWins, BetsLosses, BetsEven);
		Save();
	}

	/// <summary>Takes chips to a table. False when you don't have them.</summary>
	public static bool Take(long chips)
	{
		Load();
		if (chips <= 0 || chips > Free)
		{
			return false;
		}
		OnTable += chips;
		Save();
		return true;
	}

	/// <summary>Leaving a table: its chips come back (what was won or lost is already counted).</summary>
	public static void Return()
	{
		Load();
		if (OnTable != 0)
		{
			OnTable = 0;
			Save();
		}
	}

	/// <summary>The chips at the table changed (a hand or round is over): <paramref name="delta"/> won or lost.</summary>
	public static void Settle(bool poker, long delta, long stackNow, int result)
	{
		Load();
		if (poker)
		{
			PokerNet += delta;
			if (result > 0)
			{
				PokerWins++;
			}
			else if (result < 0)
			{
				PokerLosses++;
			}
			else
			{
				PokerSplits++;
			}
			Scores.RecordCasino(Scores.Poker, PokerNet, PokerWins, PokerLosses, PokerSplits);
		}
		else
		{
			BlackjackNet += delta;
			if (result > 0)
			{
				BlackjackWins++;
			}
			else if (result < 0)
			{
				BlackjackLosses++;
			}
			else
			{
				BlackjackPushes++;
			}
			Scores.RecordCasino(Scores.Blackjack, BlackjackNet, BlackjackWins, BlackjackLosses, BlackjackPushes);
		}
		OnTable = Math.Max(0L, stackNow);
		Save();
	}

	/// <summary>
	/// Out of chips (fewer than <paramref name="needed"/> to play): the house gives a small lot, always. True when it
	/// gave one.
	/// </summary>
	public static bool GiftIfBroke(long needed)
	{
		Load();
		if (Free >= needed)
		{
			return false;
		}
		long lot = Math.Max(Gift, needed - Free);
		Gifts += lot;
		GiftCount++;
		Save();
		ClientChat.Info(IsaClient.Languages.L10n.F("Out of chips: the house gives you {0} chips. Good luck!", lot.ToString("N0")));
		return true;
	}

	public static void Load()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			if (!File.Exists(FilePath))
			{
				return;
			}
			JObject o = JObject.Parse(File.ReadAllText(FilePath));
			PokerNet = (long?)o["PokerNet"] ?? 0;
			BlackjackNet = (long?)o["BlackjackNet"] ?? 0;
			BetsChips = (long?)o["BetsChips"] ?? 0;
			BetsWins = (int?)o["BetsWins"] ?? 0;
			BetsLosses = (int?)o["BetsLosses"] ?? 0;
			BetsEven = (int?)o["BetsEven"] ?? 0;
			Gifts = (long?)o["Gifts"] ?? 0;
			GiftCount = (int?)o["GiftCount"] ?? 0;
			PokerWins = (int?)o["PokerWins"] ?? 0;
			PokerLosses = (int?)o["PokerLosses"] ?? 0;
			PokerSplits = (int?)o["PokerSplits"] ?? 0;
			BlackjackWins = (int?)o["BlackjackWins"] ?? 0;
			BlackjackLosses = (int?)o["BlackjackLosses"] ?? 0;
			BlackjackPushes = (int?)o["BlackjackPushes"] ?? 0;
			// Chips left at a table when the game closed came back already (hands are counted as they end).
			OnTable = 0;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			JObject o = new JObject
			{
				["PokerNet"] = PokerNet,
				["BlackjackNet"] = BlackjackNet,
				["BetsChips"] = BetsChips,
				["BetsWins"] = BetsWins,
				["BetsLosses"] = BetsLosses,
				["BetsEven"] = BetsEven,
				["Gifts"] = Gifts,
				["GiftCount"] = GiftCount,
				["PokerWins"] = PokerWins,
				["PokerLosses"] = PokerLosses,
				["PokerSplits"] = PokerSplits,
				["BlackjackWins"] = BlackjackWins,
				["BlackjackLosses"] = BlackjackLosses,
				["BlackjackPushes"] = BlackjackPushes
			};
			string temp = FilePath + ".tmp";
			File.WriteAllText(temp, o.ToString());
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			File.Move(temp, FilePath);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
