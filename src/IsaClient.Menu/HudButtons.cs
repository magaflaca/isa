using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using IsaClient.Core;
using IsaClient.Music;

namespace IsaClient.Menu;

/// <summary>
/// The small screen buttons, each one can be dragged anywhere and turned off (panel or .hud):
/// in game, left to right just left of the life hearts: the IsaClient tree (opens/closes the client panel), your map head
/// (My Character) and the music note (music window); on the main menu, the music note at the end of the credits icons.
/// </summary>
public static class HudButtons
{
	/// <summary>Size of the in-game buttons (the main menu ones use <see cref="MenuIcon.Size"/>).</summary>
	public const float GameSize = 26f;

	private const float Gap = 6f;

	[Setting("MusicNoteButton")]
	public static bool MusicNote = true;

	[Setting("ClientMenuButton")]
	public static bool ClientMenu = true;

	[Setting("MyCharacterButton")]
	public static bool MyCharacter = true;

	/// <summary>Where the user left each button ("note.game", "note.menu", ...): center in UI coordinates.</summary>
	[Setting("HudButtonPositions")]
	public static Dictionary<string, float[]> Positions = new Dictionary<string, float[]>();

	private static readonly HudButton _note = new HudButton("note", MusicPlayerWindow.ToggleWindow,
		() => MusicPlayerWindow.WindowOpen ? "Close the music window" : "Open the music window",
		delegate(SpriteBatch sb, Vector2 center, float size, bool hover)
		{
			Color color = MusicPlayerWindow.WindowOpen ? MusicPlayerWindow.AccentColor : Color.White;
			MenuIcon.DrawTexture(sb, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, center, color * (hover ? 1f : 0.85f), hover, size);
		});

	private static readonly HudButton _client = new HudButton("client", ToggleClientPanel,
		() => InputState.TildeTerminal ? "Close the IsaClient panel" : "Open the IsaClient panel",
		delegate(SpriteBatch sb, Vector2 center, float size, bool hover)
		{
			Texture2D tree = ClientLogo.Tree;
			if (tree != null)
			{
				MenuIcon.DrawTexture(sb, tree, null, center, Color.White * ((hover || InputState.TildeTerminal) ? 1f : 0.85f), hover, size);
			}
		});

	private static readonly HudButton _character = new HudButton("character", ToggleMyCharacter,
		() => "My Character",
		delegate(SpriteBatch sb, Vector2 center, float size, bool hover)
		{
			// The head the map shows (84 px target, the head about 30 px of it), a little up so it lines up with the others.
			Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, Main.LocalPlayer, center - new Vector2(0f, 3f), hover ? 1f : 0.9f, size / 30f * (hover ? 1.12f : 1f), Color.Transparent);
		});

	/// <summary>In game, with the interface layers (UI scale): tree, head and note just left of the life hearts.</summary>
	public static void DrawInGame(SpriteBatch spriteBatch)
	{
		if (Main.gameMenu || Main.hideUI || Main.mapFullscreen || Main.ingameOptionsWindow)
		{
			return;
		}
		// The game draws the life hearts from screen width - 296, y 15.
		Vector2 center = new Vector2(HudButton.UiScreenSize.X - 296f - GameSize / 2f - Gap, 15f + GameSize / 2f);
		const float step = GameSize + Gap;
		if (MusicNote)
		{
			_note.Draw(spriteBatch, center, GameSize);
		}
		if (MyCharacter)
		{
			_character.Draw(spriteBatch, center - new Vector2(step, 0f), GameSize);
		}
		if (ClientMenu)
		{
			_client.Draw(spriteBatch, center - new Vector2(step * 2f, 0f), GameSize);
		}
	}

	/// <summary>Main menu: the music note, last of the icons under the credits.</summary>
	/// <summary>True when a button was dragged away from its place in the main menu's icon row.</summary>
	public static bool MovedInMenu(string key)
	{
		return Positions.TryGetValue(key + ".menu", out float[] saved) && saved != null && saved.Length == 2;
	}

	public static void DrawMusicNoteInMenu(SpriteBatch spriteBatch, Vector2 defaultCenter)
	{
		if (MusicNote)
		{
			_note.Draw(spriteBatch, defaultCenter, MenuIcon.Size);
		}
	}

	private static void ToggleClientPanel()
	{
		InputState.TildeTerminal = !InputState.TildeTerminal;
		SoundEngine.PlaySound(InputState.TildeTerminal ? 10 : 11);
	}

	/// <summary>
	/// My Character is a page of the client panel (it is only drawn while the panel is open): open the panel on it, or put
	/// the panel back on its normal page and close it.
	/// </summary>
	private static void ToggleMyCharacter()
	{
		if (InputState.TildeTerminal && MenuState.CustomUI2.CurrentState is CharacterWindow)
		{
			MenuState.CustomUI2.SetState(CheatPanel.UiStateNewCheatPanel);
			InputState.TildeTerminal = false;
			SoundEngine.PlaySound(11);
			return;
		}
		InputState.TildeTerminal = true;
		CharacterWindow.Open();
	}

	/// <summary>Turns a button on or off ("note", "client", "character") and saves it.</summary>
	public static void SetEnabled(string key, bool enabled)
	{
		switch (key)
		{
		case "note":
			MusicNote = enabled;
			break;
		case "client":
			ClientMenu = enabled;
			break;
		case "character":
			MyCharacter = enabled;
			break;
		}
		HudButton.Release();
		ClientSettings.Save();
	}

	/// <summary>Puts every button back in its default place.</summary>
	public static void ResetPositions()
	{
		Positions.Clear();
		ClientSettings.Save();
	}

	#region Chat commands

	/// <summary>.hud [note|menu|character|all] [on|off] | .hud reset</summary>
	internal static void HudCommand(CommandContext command)
	{
		string target = (command.ArgumentCount > 0) ? command.Parts[1].ToLowerInvariant() : "";
		if (target == "reset")
		{
			ResetPositions();
			ClientChat.Info("Screen buttons moved back to their default places.");
			return;
		}
		string key = target switch
		{
			"note" => "note",
			"music" => "note",
			"menu" => "client",
			"tree" => "client",
			"client" => "client",
			"character" => "character",
			"char" => "character",
			"head" => "character",
			"all" => "all",
			_ => null,
		};
		if (key == null)
		{
			ClientChat.Info("Screen buttons: tree " + OnOff(ClientMenu) + ", head " + OnOff(MyCharacter) + ", music note " + OnOff(MusicNote) + ".");
			ClientChat.Info("Usage: .hud <menu|character|note|all> [on|off]   .hud reset (default places). Hold a button to move it.");
			return;
		}
		string state = (command.ArgumentCount > 1) ? command.Parts[2].ToLowerInvariant() : "";
		bool? wanted = state switch
		{
			"on" => true,
			"off" => false,
			_ => null,
		};
		if (key == "all")
		{
			bool all = wanted ?? !(MusicNote && ClientMenu && MyCharacter);
			MusicNote = ClientMenu = MyCharacter = all;
			SetEnabled("note", all);
		}
		else
		{
			SetEnabled(key, wanted ?? !IsEnabled(key));
		}
		ClientChat.Info("Screen buttons: tree " + OnOff(ClientMenu) + ", head " + OnOff(MyCharacter) + ", music note " + OnOff(MusicNote) + ".");
	}

	private static bool IsEnabled(string key)
	{
		return key switch
		{
			"note" => MusicNote,
			"client" => ClientMenu,
			_ => MyCharacter,
		};
	}

	private static string OnOff(bool on)
	{
		return on ? "on" : "off";
	}

	#endregion
}
