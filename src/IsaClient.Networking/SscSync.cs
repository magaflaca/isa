using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Terraria.ID;
using Terraria.IO;
using IsaClient.Core;
using IsaClient.Gameplay;
using Terraria;

namespace IsaClient.Networking;

/// <summary>
/// Keep SSC Character: on servers with server-side characters the game never writes your .plr. With this on, your
/// character file follows the server's character: every time the game saves it (autosave, leaving, being disconnected),
/// the character the server gave you is written to your local .plr.
/// Nothing is written until the server has actually sent you your character (your inventory slots), as the
/// "server modified you" detection sees it; no chat texts are read, they differ on every server. When you type
/// /login (or /register), only what the server sends after it counts, so the guest starter character a server gives
/// before your login is never written. The first write of each connection keeps a copy of your old file in
/// Players/SSC Backups (the last 10 per character).
///
/// Two-Way SSC Sync (<see cref="TwoWay"/>) works in both directions: whichever side changed last wins. Your local .plr
/// is remembered per server; if it changed since the last time it followed that server's character (you played it in
/// single player or on another server), it is uploaded to the server once the server has sent you its character (the
/// server's version is saved to Players/SSC Backups first). Otherwise the server's character wins and your local .plr
/// follows it, as with Keep SSC Character.
/// </summary>
public static class SscSync
{
	[Setting("KeepSSCCharacter")]
	public static bool Enabled;

	[Setting("TwoWaySSCSync")]
	public static bool TwoWay;

	/// <summary>Local .plr as it was when this connection started, and its write time (before the server changed anything).</summary>
	private static Player _localSnapshot;

	private static DateTime _localWriteUtc;

	private static string _syncKey;

	/// <summary>Two-way: the local character goes up once the server stops sending slots.</summary>
	private static bool _uploadPending;

	private static uint _lastSlotTick;

	private static bool _decided;

	private static bool Writes => Enabled || TwoWay;

	private static string RecordsPath => Path.Combine(Main.PlayerPath, "SSC Backups", "two-way-sync.txt");

	private static bool _loggedIn;

	private static bool _announced;

	private static bool _backedUp;

	private const int BackupsKept = 10;

	private static int _slotsAfterLogin;

	/// <summary>Slot packets the server must send after your /login before the character counts as yours.</summary>
	private const int SlotsNeeded = 10;

	public static bool OnSscServer => Main.netMode == 1 && (Main.ServerSideCharacter || (Main.ActivePlayerFileData != null && Main.ActivePlayerFileData.ServerSideCharacter));

	public static string StatusText
	{
		get
		{
			if (TwoWay)
			{
				if (!OnSscServer)
				{
					return "Two-Way SSC Sync is on (this server does not use server-side characters).";
				}
				if (_uploadPending)
				{
					return "Two-Way SSC Sync: your local character is newer, it is uploaded as soon as the server finishes sending yours.";
				}
				return _loggedIn ? "Two-Way SSC Sync: your local character and this server's character follow each other." : "Two-Way SSC Sync: waiting for the server to send your character.";
			}
			if (!Enabled)
			{
				return "Keep SSC Character is off.";
			}
			if (!OnSscServer)
			{
				return "Keep SSC Character is on (this server does not use server-side characters).";
			}
			return _loggedIn ? "Keep SSC Character: your local character follows this server's character." : "Keep SSC Character: waiting for the server to send your character (.ssc save forces it).";
		}
	}

	/// <summary>A new connection: wait for the login again.</summary>
	public static void OnConnectionStarting()
	{
		IsaClient.Items.ItemKeeper.OnConnectionStarting();
		_loggedIn = false;
		_announced = false;
		_backedUp = false;
		_slotsAfterLogin = 0;
		_uploadPending = false;
		_decided = false;
		_localSnapshot = null;
		_syncKey = null;
		if (!TwoWay)
		{
			return;
		}
		try
		{
			PlayerFileData file = Main.ActivePlayerFileData;
			if (file != null && !file.IsCloudSave && File.Exists(file.Path))
			{
				_localWriteUtc = File.GetLastWriteTimeUtc(file.Path);
				_localSnapshot = CharacterPresets.LoadFile(file.Path, out var _);
				_syncKey = (Netplay.ServerIPText + ":" + Netplay.ListenPort + "|" + Path.GetFileName(file.Path)).ToLowerInvariant();
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Your own /login or /register: what the server sent before it was the guest character.</summary>
	public static void OnChatSent(string text)
	{
		if (Main.netMode != 1 || string.IsNullOrEmpty(text))
		{
			return;
		}
		string t = text.TrimStart().ToLowerInvariant();
		if (t.StartsWith("/login") || t.StartsWith("/register") || t.StartsWith("/l ") || t.StartsWith("/reg "))
		{
			_loggedIn = false;
			_slotsAfterLogin = 0;
		}
	}

	/// <summary>The server set one of your inventory / equipment slots (server-side character).</summary>
	public static void OnServerSetMySlot()
	{
		_lastSlotTick = Main.GameUpdateCount;
		if (_loggedIn || Main.netMode != 1 || !Main.ServerSideCharacter)
		{
			return;
		}
		_slotsAfterLogin++;
		if (_slotsAfterLogin < SlotsNeeded)
		{
			return;
		}
		_loggedIn = true;
		if (TwoWay)
		{
			Decide();
		}
		else if (Enabled)
		{
			ClientChat.Info("Keep SSC Character: the server sent your character, your local character will follow it.");
		}
	}

	/// <summary>Two-way: upload the local character if it changed after it last followed this server, else follow the server.</summary>
	private static void Decide()
	{
		if (_decided)
		{
			return;
		}
		_decided = true;
		if (_localSnapshot == null || _syncKey == null)
		{
			ClientChat.Info("Two-Way SSC Sync: the server sent your character, your local character will follow it.");
			return;
		}
		ReadRecords().TryGetValue(_syncKey, out long recorded);
		bool localNewer = recorded == 0 || _localWriteUtc.Ticks > recorded + TimeSpan.TicksPerSecond * 2;
		if (localNewer)
		{
			_uploadPending = true;
			ClientChat.Info((recorded == 0) ? "Two-Way SSC Sync: first visit with this character, your local character will be uploaded to the server." : "Two-Way SSC Sync: your local character changed since your last visit, it will be uploaded to the server.");
		}
		else
		{
			ClientChat.Info("Two-Way SSC Sync: the server's character is the newest, your local character will follow it.");
		}
	}

	/// <summary>.ssc push: upload the local .plr now (as it was when you joined).</summary>
	public static void PushNow()
	{
		if (Main.gameMenu || !OnSscServer)
		{
			ClientChat.Error("You are not on a server with server-side characters.");
			return;
		}
		if (_localSnapshot == null)
		{
			PlayerFileData file = Main.ActivePlayerFileData;
			_localSnapshot = (file != null && File.Exists(file.Path)) ? CharacterPresets.LoadFile(file.Path, out var _) : null;
		}
		if (_localSnapshot == null)
		{
			ClientChat.Error("Your local character file could not be read.");
			return;
		}
		_loggedIn = true;
		Upload();
	}

	private static void Upload()
	{
		_uploadPending = false;
		Player me = Main.LocalPlayer;
		Player local = _localSnapshot;
		if (local == null || me == null)
		{
			return;
		}
		BackupServerCharacter(me);
		lock (me)
		{
			CharacterPresets.Apply(local, me, WearParts.All);
			CopyBank(local.bank, me.bank);
			CopyBank(local.bank2, me.bank2);
			CopyBank(local.bank3, me.bank3);
			CopyBank(local.bank4, me.bank4);
			me.anglerQuestsFinished = local.anglerQuestsFinished;
			me.golferScoreAccumulated = local.golferScoreAccumulated;
		}
		int myPlayer = Main.myPlayer;
		Task.Run(delegate
		{
			try
			{
				NetMessage.SendData(4, -1, -1, null, myPlayer);
				NetMessage.SendData(16, -1, -1, null, myPlayer);
				NetMessage.SendData(42, -1, -1, null, myPlayer);
				NetMessage.SendData(147, -1, -1, null, myPlayer, me.CurrentLoadoutIndex);
				for (int i = 0; i < PlayerItemSlotID.Count; i++)
				{
					if (i == PlayerItemSlotID.InventoryMouseItem)
					{
						continue;
					}
					NetMessage.SendData(5, -1, -1, null, myPlayer, i);
					if (i % 10 == 9)
					{
						Thread.Sleep(15);
					}
				}
				NetMessage.SendData(76, -1, -1, null, myPlayer);
				NetMessage.SendData(13, -1, -1, null, myPlayer);
				ClientChat.Info("Two-Way SSC Sync: your local character was uploaded to the server.");
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		});
	}

	private static void CopyBank(Chest from, Chest to)
	{
		if (from?.item == null || to?.item == null)
		{
			return;
		}
		for (int i = 0; i < to.item.Length && i < from.item.Length; i++)
		{
			to.item[i] = (from.item[i] ?? new Item()).Clone();
		}
	}

	/// <summary>The server's character, kept in Players/SSC Backups before the local one replaces it.</summary>
	private static void BackupServerCharacter(Player server)
	{
		try
		{
			string folder = Path.Combine(Main.PlayerPath, "SSC Backups");
			Directory.CreateDirectory(folder);
			Player copy = server.SafeSerializedClone();
			string path = Path.Combine(folder, MakeSafe(server.name) + "_server_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".plr");
			Player.IsaWritePlayerFile(PlayerFileData.IsaCreateCopyFile(copy, path));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static string MakeSafe(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name;
	}

	private static Dictionary<string, long> ReadRecords()
	{
		Dictionary<string, long> records = new Dictionary<string, long>();
		try
		{
			if (File.Exists(RecordsPath))
			{
				foreach (string line in File.ReadAllLines(RecordsPath))
				{
					int tab = line.LastIndexOf('\t');
					if (tab > 0 && long.TryParse(line.Substring(tab + 1), out long ticks))
					{
						records[line.Substring(0, tab)] = ticks;
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		return records;
	}

	/// <summary>Remembers the write time of the local file right after it followed this server.</summary>
	private static void WriteRecord(string path)
	{
		if (_syncKey == null || !File.Exists(path))
		{
			return;
		}
		try
		{
			Dictionary<string, long> records = ReadRecords();
			records[_syncKey] = File.GetLastWriteTimeUtc(path).Ticks;
			Directory.CreateDirectory(Path.GetDirectoryName(RecordsPath));
			File.WriteAllLines(RecordsPath, records.Select((KeyValuePair<string, long> r) => r.Key + "\t" + r.Value));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Update()
	{
		if (_uploadPending && !Main.gameMenu && OnSscServer && Main.GameUpdateCount - _lastSlotTick > 90)
		{
			Upload();
		}
		if (_announced || !Writes || !OnSscServer || Main.gameMenu)
		{
			return;
		}
		_announced = true;
		if (!_loggedIn)
		{
			ClientChat.Info(TwoWay ? "Two-Way SSC Sync: this server uses server-side characters. Waiting for the server to send yours." : "Keep SSC Character: this server uses server-side characters. Your local character is written once the server sends you yours.");
		}
	}

	/// <summary>Called by Player.InternalSavePlayer when the game would skip the save because the character is server-side.</summary>
	public static bool ShouldSync(PlayerFileData playerFile)
	{
		return Writes && _loggedIn && !_uploadPending && playerFile != null && playerFile == Main.ActivePlayerFileData && !playerFile.IsCloudSave && !string.IsNullOrEmpty(playerFile.Path) && playerFile.Player != null && !string.IsNullOrEmpty(playerFile.Player.name);
	}

	/// <summary>Writes the server's character into the local file (inside the game's save lock).</summary>
	public static void Save(PlayerFileData playerFile)
	{
		bool wasServerSide = playerFile.ServerSideCharacter;
		try
		{
			Backup(playerFile);
			playerFile.IsaSetServerSide(false);
			Player.IsaWritePlayerFile(playerFile);
			if (TwoWay)
			{
				WriteRecord(playerFile.Path);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			playerFile.IsaSetServerSide(wasServerSide);
		}
	}

	/// <summary>.ssc save: trust the current character and write it now.</summary>
	public static void SaveNow()
	{
		if (Main.gameMenu || !OnSscServer)
		{
			ClientChat.Error("You are not on a server with server-side characters.");
			return;
		}
		_loggedIn = true;
		bool was = Enabled;
		Enabled = true;
		try
		{
			Player.SavePlayer(Main.ActivePlayerFileData);
		}
		finally
		{
			Enabled = was;
		}
		ClientChat.Info("Saved this server's character to your local " + Path.GetFileName(Main.ActivePlayerFileData.Path) + ".");
	}

	private static void Backup(PlayerFileData playerFile)
	{
		if (_backedUp || !File.Exists(playerFile.Path))
		{
			return;
		}
		_backedUp = true;
		string folder = Path.Combine(Main.PlayerPath, "SSC Backups");
		Directory.CreateDirectory(folder);
		string name = Path.GetFileNameWithoutExtension(playerFile.Path);
		File.Copy(playerFile.Path, Path.Combine(folder, name + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".plr"), overwrite: true);
		foreach (FileInfo old in new DirectoryInfo(folder).GetFiles(name + "_*.plr").OrderByDescending((FileInfo f) => f.CreationTimeUtc).Skip(BackupsKept))
		{
			try
			{
				old.Delete();
			}
			catch
			{
			}
		}
	}

	#region Chat commands

	/// <summary>.ssc / .keepssc</summary>
	internal static void KeepSscCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && command.Parts[1].ToLower() == "save")
		{
			SaveNow();
			return;
		}
		if (command.Parts.Length > 1 && command.Parts[1].ToLower() == "status")
		{
			ClientChat.Info(StatusText);
			return;
		}
		if (command.Parts.Length > 1 && command.Parts[1].ToLower() == "push")
		{
			PushNow();
			return;
		}
		if (command.Parts.Length > 1 && (command.Parts[1].ToLower() == "twoway" || command.Parts[1].ToLower() == "2way"))
		{
			TwoWay = ((command.Parts.Length > 2) ? (command.Parts[2].ToLower() == "on" || command.Parts[2] == "1" || command.Parts[2].ToLower() == "true") : (!TwoWay));
			ClientSettings.Save();
			ClientChat.Info(StatusText + (TwoWay ? "  (applies from the next time you join)" : ""));
			return;
		}
		Enabled = ((command.Parts.Length > 1) ? (command.Parts[1].ToLower() == "on" || command.Parts[1] == "1" || command.Parts[1].ToLower() == "true") : (!Enabled));
		ClientSettings.Save();
		ClientChat.Info(StatusText);
	}

	#endregion
}
