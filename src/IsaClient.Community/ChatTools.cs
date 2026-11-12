using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.UI.Chat;
using IsaClient.Core;

namespace IsaClient.Community;

/// <summary>Chat tools: chat spammer, anti chat spam, enhanced chat box and keeping chat between worlds.</summary>
public static class ChatTools
{
	public static bool ChatSpammer;

	public static string ChatSpammerString = "";

	[Setting("ChatSpamDelay", Min = 1, Max = 3600)]
	public static int ChatSpamDelay = 30;

	private static int _chatSpamTimer;

	/// <summary>Finds an active player by exact or partial (accent-insensitive) name.</summary>
	public static Player FindPlayer(string name, out string error)
	{
		error = null;
		string wanted = RainbowText.Normalize(name);
		List<Player> partial = new List<Player>();
		for (int i = 0; i < 255; i++)
		{
			Player player = Main.player[i];
			if (!player.active)
			{
				continue;
			}
			string candidate = RainbowText.Normalize(player.name);
			if (candidate == wanted)
			{
				return player;
			}
			if (candidate.Contains(wanted))
			{
				partial.Add(player);
			}
		}
		if (partial.Count == 1)
		{
			return partial[0];
		}
		error = ((partial.Count == 0) ? ("No player matches '" + name + "'.") : ("Several players match '" + name + "': " + string.Join(", ", partial.Select((Player p) => p.name))));
		return null;
	}

	/// <summary>.spamchat</summary>
	internal static void SpamChatCommand(CommandContext command)
	{
		string chatText = command.Text;
		string[] array = command.Parts;
		if (array.Length > 1)
		{
			ChatSpammerString = chatText.Substring(array[0].Length + 2);
			ChatSpammer = true;
			_chatSpamTimer = 0;
		}
		else
		{
			ChatSpammer = false;
		}
		ClientChat.Info("Chat Spammer " + (ChatSpammer ? "En" : "Dis") + "abled" + (ChatSpammer ? ("  (every " + ChatSpamDelay + " ticks, .spamdelay to change, .spamchat to stop)") : ""));
	}

	internal static void UpdateChatSpammer()
	{
		if (!ChatSpammer || Main.gameMenu || (Main.netMode != 1 && !ChatCommands.IsLocalCommand(ChatSpammerString)))
		{
			return;
		}
		if (++_chatSpamTimer < ChatSpamDelay)
		{
			return;
		}
		_chatSpamTimer = 0;
		string message = ChatSpammerString switch
		{
			".nochat" => "\n\n\n\n\n\n\n\n\n\n",
			".fakechat" => "\n\n\n\n\n\n\n\n\n Stop spamming!",
			_ => ChatSpammerString,
		};
		if (message.Length > 1 && (message[0] == ChatCommands.CommandSpecifier || message[0] == '.') && ChatCommands.IsLocalCommand(message))
		{
			ChatCommands.RunLocalCommand(message);
			return;
		}
		ChatHelper.SendChatMessageFromClient(new ChatMessage(message));
	}

	[Setting("KeepChatBetweenWorlds")]
	public static bool KeepChat;

	[Setting("EnhancedChatBox")]
	public static bool EnhancedChat = true;

	[Setting("AntiChatSpam", Remembered = true)]
	public static bool AntiChatSpam;

	internal static void UpdateSpamCounters(Player p, int index)
	{
		if (p.spammedProjectileCount > 0)
		{
			p.spammedProjectileCount -= 4;
		}
		if (p.spammedSoundCount > 0)
		{
			p.spammedSoundCount -= 4;
		}
		if (p.spammedParticleCount > 0)
		{
			p.spammedParticleCount -= 8;
		}
		if (p.spammedNebulaCount > 0)
		{
			p.spammedNebulaCount -= 4;
		}
		if (index == Main.myPlayer || p.spammedNebulaCount < 0)
		{
			p.spammedNebulaCount = 0;
		}
		if (p.blockNebulaRecieveTime > 0)
		{
			p.blockNebulaRecieveTime--;
		}
		if (index == Main.myPlayer || p.spammedProjectileCount < 0)
		{
			p.spammedProjectileCount = 0;
		}
		if (index == Main.myPlayer || p.spammedSoundCount < 0)
		{
			p.spammedSoundCount = 0;
		}
		if (index == Main.myPlayer || p.spammedParticleCount < 0)
		{
			p.spammedParticleCount = 0;
		}
		if (p.blockProjectileRecieveTime > 0)
		{
			p.blockProjectileRecieveTime--;
		}
		if (p.blockSoundRecieveTime > 0)
		{
			p.blockSoundRecieveTime--;
		}
		if (p.blockParticleRecieveTime > 0)
		{
			p.blockParticleRecieveTime--;
		}
		if (p.spawned)
		{
			p.joining = false;
		}
	}

	/// <summary>"Anti-Chat Spam": repeated messages are merged into the previous line as "text xN".</summary>
	public static bool TryMergeSpamMessage(List<ChatMessageContainer> messages, string text, Color color, int widthLimitInPixels)
	{
		if (!AntiChatSpam || messages.Count < 2 || string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < 2; i++)
		{
			ChatMessageContainer container = messages[i];
			if (container.OriginalText == null || !container.OriginalText.StartsWith(text))
			{
				continue;
			}
			string rest = container.OriginalText.Substring(text.Length);
			int count = 1;
			if (rest.Length > 0 && (!rest.StartsWith(" x") || !int.TryParse(rest.Substring(2), out count)))
			{
				continue;
			}
			string shown = container.ShowsTranslation ? container.ShownText : null;
			container.SetContents(text + " x" + (count + 1), color, widthLimitInPixels);
			if (shown != null)
			{
				// Keep showing the translation: "hello x2" becomes "hello x3".
				if (rest.Length > 0 && shown.EndsWith(rest, StringComparison.Ordinal))
				{
					shown = shown.Substring(0, shown.Length - rest.Length);
				}
				container.ShowInstead(shown + " x" + (count + 1));
			}
			return true;
		}
		return false;
	}

	#region Chat commands

	/// <summary>.spamdelay</summary>
	internal static void SpamDelayCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && int.TryParse(command.Parts[1], out var delay))
		{
			ChatSpamDelay = Utils.Clamp(delay, 1, 3600);
		}
		ClientChat.Info("Chat spam delay: " + ChatSpamDelay + " ticks (" + ((float)ChatSpamDelay / 60f).ToString("0.##") + " s).");
	}

	/// <summary>.keepchat</summary>
	internal static void KeepChatCommand(CommandContext command)
	{
		KeepChat = ((command.Parts.Length > 1) ? (command.Parts[1].ToLower() == "on" || command.Parts[1] == "1" || command.Parts[1].ToLower() == "true") : (!KeepChat));
		ClientChat.Info("Keep Chat Between Worlds " + (KeepChat ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>The chat was emptied (.clearchat): chat windows of plugins can empty theirs too.</summary>
	public static event Action Cleared;

	/// <summary>.clearchat / .clear: empties the chat (only on your screen).</summary>
	internal static void ClearChatCommand(CommandContext command)
	{
		Terraria.Main.chatMonitor.Clear();
		try
		{
			Cleared?.Invoke();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>.chatbox</summary>
	internal static void ChatBoxCommand(CommandContext command)
	{
		EnhancedChat = ((command.Parts.Length > 1) ? (command.Parts[1].ToLower() == "on" || command.Parts[1] == "1" || command.Parts[1].ToLower() == "true") : (!EnhancedChat));
		ClientChat.Info("Enhanced Chat Box " + (EnhancedChat ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.antispam / .antichatspam</summary>
	internal static void AntiChatSpamCommand(CommandContext command)
	{
		AntiChatSpam = !AntiChatSpam;
		ClientChat.Info("Anti-Chat Spam " + (AntiChatSpam ? "En" : "Dis") + "abled");
	}

	#endregion
}
