using System;
using System.Collections.Generic;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Music: the music module and player.</summary>
public static class MusicMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Open Music Player", "Open Video Player", "Mini Player", "Music Note Button", "Music Chat Messages", "Music Module", "Autoplay", "Play In Background", "Now Playing",
		"Pause / Resume", "Skip", "Stop & Clear Queue", "Loop", "Music Volume", "Mute Terraria Music", "Quiet Game While Playing", "Open Music Folder",
		"Load Cookies", "Update yt-dlp", "Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Open Music Player":
			return MenuControls.MakeButton(cheat, color, () => MusicPlayerWindow.WindowOpen ? "Close Music Player" : "Open Music Player", delegate
			{
				MusicPlayerWindow.ToggleWindow();
			});
		case "Open Video Player":
			return MenuControls.MakeButton(cheat, color, () => IsaClient.Video.VideoWindow.Visible ? "Close Video Player" : "Open Video Player", delegate
			{
				IsaClient.Video.VideoWindow.Toggle();
			});
		case "Quiet Game While Playing":
			return MenuControls.MakeToggle(cheat, color, () => MediaFocus.QuietGame, delegate
			{
				MediaFocus.QuietGame = !MediaFocus.QuietGame;
				IsaClient.Core.ClientSettings.Save();
				SoundEngine.PlaySound(MediaFocus.QuietGame ? 10 : 11);
			});
		case "Mini Player":
			return MenuControls.MakeToggle(cheat, color, () => MusicPlayerWindow.MiniVisible, delegate
			{
				MusicPlayerWindow.ToggleMini();
				SoundEngine.PlaySound(MusicPlayerWindow.MiniVisible ? 10 : 11);
			});
		case "Music Note Button":
			return MenuControls.MakeToggle(cheat, color, () => HudButtons.MusicNote, delegate
			{
				HudButtons.SetEnabled("note", !HudButtons.MusicNote);
				SoundEngine.PlaySound(HudButtons.MusicNote ? 10 : 11);
			});
		case "Music Chat Messages":
			return MenuControls.MakeButton(cheat, color, () => "Music Chat Messages: " + MusicPlayer.ChatMessages, delegate
			{
				CycleChatMessages(1);
			}, delegate
			{
				CycleChatMessages(-1);
			});
		case "Music Module":
			return MenuControls.MakeToggle(cheat, color, () => MusicPlayer.Enabled, delegate
			{
				MusicPlayer.SetEnabled(!MusicPlayer.Enabled);
				SoundEngine.PlaySound(MusicPlayer.Enabled ? 10 : 11);
			});
		case "Play In Background":
			return MenuControls.MakeToggle(cheat, color, () => MusicPlayer.PlayInBackground, delegate
			{
				MusicPlayer.SetPlayInBackground(!MusicPlayer.PlayInBackground);
				SoundEngine.PlaySound(MusicPlayer.PlayInBackground ? 10 : 11);
			});
		case "Autoplay":
			return MenuControls.MakeToggle(cheat, color, () => MusicPlayer.AutoplayEnabled, delegate
			{
				MusicCommands.TryHandleChat(".autoplay");
				SoundEngine.PlaySound(MusicPlayer.AutoplayEnabled ? 10 : 11);
			});
		case "Now Playing":
			return MenuControls.MakeButton(cheat, color, () => MusicPlayer.NowPlayingText, delegate
			{
				SoundEngine.PlaySound(12);
				MusicCommands.TryHandleChat(".queue");
			});
		case "Pause / Resume":
			return MenuControls.MakeButton(cheat, color, () => MusicPlayer.IsPaused ? "Resume" : "Pause", delegate
			{
				SoundEngine.PlaySound(12);
				MusicPlayer.TogglePause();
			});
		case "Skip":
			return MenuControls.MakeButton(cheat, color, () => "Skip" + ((MusicPlayer.QueueCount > 0) ? (" (" + MusicPlayer.QueueCount + " queued)") : ""), delegate
			{
				SoundEngine.PlaySound(12);
				MusicPlayer.Skip();
			});
		case "Stop & Clear Queue":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				MusicCommands.TryHandleChat(".stop");
			});
		case "Loop":
			return MenuControls.MakeButton(cheat, color, () => "Loop: " + MusicPlayer.Loop, delegate
			{
				SoundEngine.PlaySound(12);
				MusicCommands.TryHandleChat(".loop");
			});
		case "Music Volume":
		{
			float volumeFloat = MusicPlayer.Volume;
			UIElement volumeSlider = new MenuSlider(() => "Music Volume: " + (int)Math.Round(MusicPlayer.Volume * 100f) + "%", () => MusicPlayer.Volume, delegate(float f)
			{
				volumeFloat = f;
				MusicPlayer.SetVolume(f);
			}, delegate
			{
				volumeFloat = UILinksInitializer.HandleSliderHorizontalInput(volumeFloat, 0f, 1f, 1f, 0.35f);
				MusicPlayer.SetVolume(volumeFloat);
			}, 6912, color);
			volumeSlider.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			volumeSlider.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			return volumeSlider;
		}
		case "Mute Terraria Music":
			return MenuControls.MakeToggle(cheat, color, () => MusicPlayer.MuteGameMusic, delegate
			{
				MusicPlayer.MuteGameMusic = !MusicPlayer.MuteGameMusic;
				MusicPlayer.Save();
				SoundEngine.PlaySound(MusicPlayer.MuteGameMusic ? 10 : 11);
			});
		case "Open Music Folder":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				YtDlp.OpenFolder();
			});
		case "Load Cookies":
			return MenuControls.MakeButton(cheat, color, () => (string.IsNullOrEmpty(MusicPlayer.CookiesFile) && string.IsNullOrEmpty(MusicPlayer.CookiesFromBrowser)) ? cheat : (cheat + " (loaded)"), delegate
			{
				SoundEngine.PlaySound(12);
				MusicCommands.TryHandleChat(".loadcookies");
			}, delegate
			{
				SoundEngine.PlaySound(11);
				MusicCommands.TryHandleChat(".loadcookies off");
			});
		case "Update yt-dlp":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				MusicCommands.TryHandleChat(".ytdlp");
			});
		case "Disable All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				DisableAll();
			});
		default:
			return new UIElement();
		}
	}

	private static void CycleChatMessages(int step)
	{
		int count = Enum.GetValues(typeof(MusicPlayer.ChatMessageMode)).Length;
		MusicPlayer.ChatMessages = (MusicPlayer.ChatMessageMode)(((int)MusicPlayer.ChatMessages + step + count) % count);
		IsaClient.Core.ClientSettings.Save();
		SoundEngine.PlaySound(12);
	}

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		if (MusicPlayer.Enabled)
		{
			MusicPlayer.SetEnabled(enabled: false);
		}
		else
		{
			MusicPlayer.Stop(clearQueue: true);
		}
		MusicPlayer.AutoplayEnabled = false;
		MusicPlayer.Save();
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Music Module" => "Plays music with .play <song or link> and fades out Terraria's music.\nThe first time it downloads yt-dlp. Settings: Terraria/IsaClient/Music/IsaMusic.json",
			"Play In Background" => "Songs keep playing while the game is minimized or behind other windows (.background).",
			"Autoplay" => "When the queue ends, keeps playing songs related to the last one (.autoplay).",
			"Now Playing" => "Click to show the queue (.queue).",
			"Pause / Resume" => "Pause or resume the current song (.pause).",
			"Skip" => "Skip to the next song (.skip [count]).",
			"Stop & Clear Queue" => "Stop the music and empty the queue (.stop).",
			"Loop" => "Off, repeat the current song, or repeat the whole queue (.loop).",
			"Music Volume" => "Volume of the Music module, separate from Terraria's volume (.volume 0-100).",
			"Mute Terraria Music" => "Fade out Terraria's music while a song or a video is playing.",
			"Quiet Game While Playing" => "The game's sounds and ambience fade out while a song or a video plays, so you only hear it.",
			"Open Video Player" => "Watch YouTube (or any video link) in a window, a mini player or full screen (.video <name or link>).\nVolume, speed, quality, loop, and a button to save the video as an .mp4.",
			"Open Music Player" => "The music window: now playing, queue, your library, favorites, playlists and music search (.musicui).",
			"Music Note Button" => "The small note that opens the music window, on the main menu and in game (top right).\nHold it and drag to put it anywhere; each place remembers its spot (.hud reset puts it back).",
			"Music Chat Messages" => "Which [Music] messages go to the chat (.music chat):\nCommands: answers to the music commands you type, and errors (buttons stay quiet).\nAll: also what the buttons do. Off: none. Right click: previous.",
			"Mini Player" => "A small floating player while a song plays: drag it anywhere, collapse it or close it (.miniplayer).",
			"Open Music Folder" => "Opens Terraria/IsaClient/Music (downloaded songs, config, yt-dlp).",
			"Load Cookies" => "Load a cookies.txt (Netscape format) for yt-dlp, for videos that need a login.\nOr .loadcookies firefox / chrome / edge.  Right click: remove the cookies.",
			"Update yt-dlp" => "Updates yt-dlp to the latest version (.ytdlp). Use .ytdlp deno if YouTube asks for a JavaScript runtime.",
			_ => null,
		};
	}
}
