using System;
using System.Globalization;
using System.Linq;
using IsaClient.Core;

namespace IsaClient.Music;

/// <summary>Music chat commands (.play, .skip, .queue, ...).</summary>
public static class MusicCommands
{
	public static bool IsMusicCommand(string text)
	{
		if (string.IsNullOrEmpty(text) || text.Length < 2)
		{
			return false;
		}
		string head = text.Substring(1).Split(' ')[0].ToLowerInvariant();
		return MusicPlayer.CommandNames.Contains(head);
	}

	/// <summary>A music command typed in the chat: like <see cref="TryHandleChat"/>, and its answers show in the chat.</summary>
	public static bool TryHandleTyped(string chatText)
	{
		if (!IsMusicCommand(chatText))
		{
			return false;
		}
		MusicPlayer.MarkTypedCommand();
		return TryHandleChat(chatText);
	}

	/// <summary>Chat hook for the music commands (".play x"; "/" commands always go to the server). Buttons call it too.</summary>
	public static bool TryHandleChat(string chatText)
	{
		if (string.IsNullOrEmpty(chatText) || chatText.Length < 2)
		{
			return false;
		}
		char prefix = chatText[0];
		if (prefix != ChatCommands.CommandSpecifier && prefix != '.')
		{
			return false;
		}
		if (!IsMusicCommand(chatText))
		{
			return false;
		}
		try
		{
			HandleCommand(chatText.Substring(1));
		}
		catch (Exception ex)
		{
			MusicPlayer.SayError("Error: " + ex.Message);
			ClientLog.LogException(ex);
		}
		return true;
	}

	private static void HandleCommand(string line)
	{
		int space = line.IndexOf(' ');
		string command = ((space < 0) ? line : line.Substring(0, space)).ToLowerInvariant();
		string args = ((space < 0) ? "" : line.Substring(space + 1).Trim());
		switch (command)
		{
		case "play":
		case "p":
			if (args.Length == 0)
			{
				if (MusicPlayer.IsPaused)
				{
					MusicPlayer.Resume();
				}
				else
				{
					MusicPlayer.SayError("Usage: .play <song name | link | file>");
				}
			}
			else
			{
				MusicPlayer.Request(args, MusicPlayer.QueuePosition.End);
			}
			break;
		case "firstplay":
		case "playfirst":
		case "pf":
		case "playnext":
			if (args.Length == 0)
			{
				MusicPlayer.SayError("Usage: .firstplay <song name | link>  (puts it first in the queue)");
			}
			else
			{
				MusicPlayer.Request(args, MusicPlayer.QueuePosition.Next);
			}
			break;
		case "playnow":
			if (args.Length == 0)
			{
				MusicPlayer.SayError("Usage: .playnow <song name | link>  (skips the current song)");
			}
			else
			{
				MusicPlayer.Request(args, MusicPlayer.QueuePosition.Now);
			}
			break;
		case "skip":
		case "next":
		{
			int count = 1;
			if (args.Length > 0)
			{
				int.TryParse(args, out count);
			}
			MusicPlayer.Skip(Math.Max(1, count));
			break;
		}
		case "stop":
			MusicPlayer.Stop(clearQueue: true);
			MusicPlayer.Say("Stopped and cleared the queue.");
			break;
		case "pause":
			if (MusicPlayer.IsPaused)
			{
				MusicPlayer.Resume();
			}
			else
			{
				MusicPlayer.Pause();
			}
			break;
		case "resume":
		case "unpause":
			MusicPlayer.Resume();
			break;
		case "queue":
		case "q":
			MusicPlayer.ShowQueue();
			break;
		case "np":
		case "nowplaying":
			MusicPlayer.Say(MusicPlayer.NowPlayingText);
			break;
		case "volume":
		case "vol":
		{
			if (args.Length > 0 && float.TryParse(args.TrimEnd('%').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
			{
				MusicPlayer.SetVolume(volume / 100f);
			}
			MusicPlayer.Say("Volume: " + (int)Math.Round(MusicPlayer.Volume * 100f) + "%");
			break;
		}
		case "autoplay":
			MusicPlayer.SetAutoplay(ParseToggle(args, MusicPlayer.AutoplayEnabled));
			MusicPlayer.Say("Autoplay " + (MusicPlayer.AutoplayEnabled ? "on: related songs keep playing when the queue ends." : "off."));
			break;
		case "background":
		case "bgplay":
			MusicPlayer.SetPlayInBackground(ParseToggle(args, MusicPlayer.PlayInBackground));
			MusicPlayer.Say("Play in background " + (MusicPlayer.PlayInBackground ? "on: songs keep playing while the game is minimized or in the background." : "off: songs pause like the game's music when the window loses focus."));
			break;
		case "loop":
			MusicPlayer.Loop = args.ToLowerInvariant() switch
			{
				"song" => MusicPlayer.LoopMode.Song,
				"one" => MusicPlayer.LoopMode.Song,
				"queue" => MusicPlayer.LoopMode.Queue,
				"all" => MusicPlayer.LoopMode.Queue,
				"off" => MusicPlayer.LoopMode.Off,
				_ => (MusicPlayer.LoopMode)((int)(MusicPlayer.Loop + 1) % 3),
			};
			MusicPlayer.Save();
			MusicPlayer.Say("Loop: " + MusicPlayer.Loop);
			break;
		case "shuffle":
			MusicPlayer.ToggleShuffle();
			MusicPlayer.Say("Shuffle " + (MusicPlayer.Shuffle ? "on (the queue was shuffled)." : "off."));
			break;
		case "musicui":
		case "player":
		case "mplayer":
			MusicPlayerWindow.ToggleWindow();
			break;
		case "miniplayer":
		case "mini":
			MusicPlayerWindow.ToggleMini();
			break;
		case "prev":
			MusicPlayer.Previous();
			break;
		case "fav":
			if (MusicPlayer.CurrentTrack?.Id == null)
			{
				MusicPlayer.Say("Nothing playing.");
			}
			else
			{
				bool favorite = !MusicLibrary.IsFavorite(MusicPlayer.CurrentTrack.Id);
				MusicLibrary.SetFavorite(MusicPlayer.CurrentTrack.Id, favorite);
				MusicPlayer.Say((favorite ? "Added to favorites: " : "Removed from favorites: ") + MusicPlayer.CurrentTrack.Display);
			}
			break;
		case "qremove":
		{
			if (int.TryParse(args, out var index))
			{
				lock (MusicPlayer.Sync)
				{
					if (index >= 1 && index <= MusicPlayer.TrackQueue.Count)
					{
						MusicPlayer.Say("Removed " + MusicPlayer.TrackQueue[index - 1].Display);
						MusicPlayer.TrackQueue.RemoveAt(index - 1);
						break;
					}
				}
			}
			MusicPlayer.SayError("Usage: .qremove <position in .queue>");
			break;
		}
		case "qclear":
			lock (MusicPlayer.Sync)
			{
				MusicPlayer.TrackQueue.Clear();
			}
			MusicPlayer.Say("Queue cleared.");
			break;
		case "seek":
			MusicPlayer.Seek(args);
			break;
		case "loadcookies":
		case "cookies":
			YtDlp.LoadCookies(args);
			break;
		case "ytdlp":
			if (args.ToLowerInvariant() == "deno")
			{
				YtDlp.InstallDeno();
			}
			else
			{
				YtDlp.UpdateYtDlp();
			}
			break;
		case "songs":
			MusicPlayer.ShowLibrary(args);
			break;
		case "musicfolder":
			YtDlp.OpenFolder();
			break;
		case "music":
			MusicCommand(args);
			break;
		}
	}

	private static bool ParseToggle(string args, bool current)
	{
		switch (args.ToLowerInvariant())
		{
		case "on":
		case "true":
		case "1":
			return true;
		case "off":
		case "false":
		case "0":
			return false;
		default:
			return !current;
		}
	}

	private static void MusicCommand(string args)
	{
		string[] parts = args.Split(new char[1] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
		string sub = ((parts.Length != 0) ? parts[0].ToLowerInvariant() : "");
		switch (sub)
		{
		case "on":
			MusicPlayer.SetEnabled(enabled: true);
			return;
		case "off":
			MusicPlayer.SetEnabled(enabled: false);
			return;
		case "folder":
			YtDlp.OpenFolder();
			return;
		case "mute":
			MusicPlayer.MuteGameMusic = ((parts.Length > 1) ? ParseToggle(parts[1], MusicPlayer.MuteGameMusic) : (!MusicPlayer.MuteGameMusic));
			MusicPlayer.Save();
			MusicPlayer.Say("Mute Terraria music while a song plays: " + (MusicPlayer.MuteGameMusic ? "on" : "off"));
			return;
		case "reload":
			MusicPlayer.Load();
			MusicPlayer.Say("IsaMusic.json reloaded.");
			return;
		case "chat":
		{
			string mode = (parts.Length > 1) ? parts[1].Trim().ToLowerInvariant() : "";
			MusicPlayer.ChatMessageMode? wanted = mode switch
			{
				"off" => MusicPlayer.ChatMessageMode.Off,
				"commands" => MusicPlayer.ChatMessageMode.Commands,
				"command" => MusicPlayer.ChatMessageMode.Commands,
				"all" => MusicPlayer.ChatMessageMode.All,
				"on" => MusicPlayer.ChatMessageMode.All,
				_ => null,
			};
			if (wanted.HasValue)
			{
				MusicPlayer.ChatMessages = wanted.Value;
				ClientSettings.Save();
			}
			// Always answered, even with the messages off.
			ClientChat.Info("Music chat messages: " + MusicPlayer.ChatMessages + ".  .music chat off | commands (answers to typed commands and errors) | all");
			return;
		}
		}
		MusicPlayer.Say("Music module " + (MusicPlayer.Enabled ? "on" : "off") + ". " + MusicPlayer.NowPlayingText + "  Queue: " + MusicPlayer.QueueCount + "  Volume: " + (int)Math.Round(MusicPlayer.Volume * 100f) + "%  Autoplay: " + (MusicPlayer.AutoplayEnabled ? "on" : "off") + "  Loop: " + MusicPlayer.Loop);
		MusicPlayer.Say(".play <song|link|file>  .firstplay <song>  .playnow <song>  .skip [n]  .pause  .stop  .queue  .np  .seek 1:30  .volume 0-100", ClientChat.InfoColor);
		MusicPlayer.Say(".autoplay  .loop [song|queue|off]  .shuffle  .prev  .fav  .qremove <n>  .qclear  .songs [search]  .loadcookies [file|browser]  .ytdlp [deno]  .music on|off|folder|mute|chat", ClientChat.InfoColor);
		MusicPlayer.Say(".musicui (music window)  .miniplayer (floating player)", ClientChat.InfoColor);
	}
}
