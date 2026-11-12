using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Assistant;
using IsaClient.Building;
using IsaClient.Gameplay;
using IsaClient.Menu;
using IsaClient.Movement;
using IsaClient.MultiClient;
using IsaClient.Music;
using IsaClient.Networking;
using IsaClient.Rendering;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.IO;

namespace IsaClient.Core;

/// <summary>
/// Loads and saves IsaConfig.json. Every module declares its saved fields with <see cref="SettingAttribute"/>; this class loads and
/// saves them all, plus the few values that need more than a plain field (abbreviations, network version) and the
/// modules that keep their own files (AI, music, bots, proxy, item mod presets).
/// </summary>
public static class ClientSettings
{
	public static string IsaClientFolder => Path.Combine(Main.SavePath, "IsaClient");

	public static Preferences Store = OpenStore();

	/// <summary>Loads everything. Cheat toggles are only restored with "Remember Enabled Cheats" (and not <paramref name="noCheats"/>).</summary>
	public static void Load(bool noCheats = false)
	{
		if (!Store.Load())
		{
			Save();
		}
		SettingFields.Read(Store, remembered: false);
		if (Selection.RememberEnabled && !noCheats)
		{
			SettingFields.Read(Store, remembered: true);
		}
		LoadSpecialValues();
		AiAssistant.Load();
		MusicPlayer.Load();
		MultiClientManager.Load();
		Proxy.Load();
		ItemModPresets.RefreshAllPresets();
	}

	public static void Save()
	{
		Store.Clear();
		SettingFields.Write(Store);
		Store.Put("NetworkVersion", ProtocolCompat.VersionName);
		Store.Put("CommandAbbreviations", ChatCommands.CommandAbbreviations);
		MusicPlayer.Save();
		Store.Save();
	}

	/// <summary>Settings are written when the panel closes and when leaving a world (plus on exit).</summary>
	internal static void AutoSaveSettings()
	{
		bool panelOpen = InputState.TildeTerminal;
		bool inMenu = Main.gameMenu;
		if (!MenuState.LastInMenu && inMenu)
		{
			MultiClientManager.OnLeftWorld();
		}
		if ((MenuState.LastPanelOpen && !panelOpen) || (!MenuState.LastInMenu && inMenu))
		{
			try
			{
				Save();
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		MenuState.LastPanelOpen = panelOpen;
		MenuState.LastInMenu = inMenu;
	}

	/// <summary>Values that are not one field, and checks that are more than a clamp.</summary>
	private static void LoadSpecialValues()
	{
		string network = Store.Get("NetworkVersion", "1.4.5.8");
		ProtocolCompat.Legacy = network == "1.4.5.6";
		Dictionary<string, string> abbreviations = Store.Get<Dictionary<string, string>>("CommandAbbreviations", null);
		ChatCommands.CommandAbbreviations = (abbreviations != null) ? new Dictionary<string, string>(abbreviations, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!ChatCommands.IsValidSpecifier(ChatCommands.CommandSpecifier))
		{
			ChatCommands.CommandSpecifier = '.';
		}
		ClientLifecycle.ShowWelcomeMessage = !ClientLifecycle.HideWelcomeMessage;
		if (Noclip.Speed != 9 && Noclip.Speed != 25 && Noclip.Speed != 64)
		{
			Noclip.Speed = 25;
		}
		if (PlayerCheats.ForceDashType == 4)
		{
			PlayerCheats.ForceDashType = 0;
		}
		if (Array.IndexOf(Cosmetics.DeathParticles, (ParticleOrchestraType)Utils.Clamp(Cosmetics.DeathParticleCosmeticId, 0, 255)) < 0)
		{
			Cosmetics.DeathParticleCosmeticId = (int)Cosmetics.DeathParticles[0];
		}
		if (Array.IndexOf(Cosmetics.DeathEffects, Cosmetics.DeathGoreCosmeticId) < 0)
		{
			Cosmetics.DeathGoreCosmeticId = Cosmetics.DeathEffects[0];
		}
		if (Array.IndexOf(Cosmetics.DeathSounds, (ParticleOrchestraType)Utils.Clamp(Cosmetics.DeathSoundCosmeticId, 0, 255)) < 0)
		{
			Cosmetics.DeathSoundCosmeticId = (int)Cosmetics.DeathSounds[0];
		}
		FullbrightLighting.OnSettingsLoaded();
	}

	/// <summary>
	/// Terraria/IsaClient/IsaConfig.json. An older IsaClient/Config.json is renamed once; without either, the config file
	/// of the client IsaClient started from is copied once (legacy file name).
	/// </summary>
	private static Preferences OpenStore()
	{
		string path = Path.Combine(IsaClientFolder, "IsaConfig.json");
		try
		{
			Directory.CreateDirectory(IsaClientFolder);
			string previous = Path.Combine(IsaClientFolder, "Config.json");
			string legacy = Path.Combine(Main.SavePath, "ZazaWorldEdit Config.json");
			if (!File.Exists(path) && File.Exists(previous))
			{
				File.Move(previous, path);
			}
			else if (!File.Exists(path) && File.Exists(legacy))
			{
				File.Copy(legacy, path);
			}
		}
		catch
		{
		}
		return new Preferences(path);
	}
}
