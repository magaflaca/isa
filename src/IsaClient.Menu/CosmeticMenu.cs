using System.Collections.Generic;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Cosmetic: walking/death particles, death effects and sounds, Lucy, after-images, RGB map icons.</summary>
internal static class CosmeticMenu
{
	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Walking Particles":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.WalkParticleType = MenuControls.Cycle(Cosmetics.WalkParticleType, 0, 3, back);
			});
		case "Particle Cursor":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.ServerSideCursor, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.ServerSideCursor);
			});
		case "Enable Death Particle":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.DeathParticleCosmeticEnabled, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.DeathParticleCosmeticEnabled);
			});
		case "Death Particle":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.DeathParticleCosmeticId = Cosmetics.CycleParticle(Cosmetics.DeathParticles, Cosmetics.DeathParticleCosmeticId, back);
				SoundEngine.PlaySound(10);
			});
		case "Enable Death Effect":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.DeathGoreCosmeticEnabled, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.DeathGoreCosmeticEnabled);
			});
		case "Death Effect":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.DeathGoreCosmeticId = Cosmetics.CycleValue(Cosmetics.DeathEffects, Cosmetics.DeathGoreCosmeticId, back);
				SoundEngine.PlaySound(10);
			});
		case "Enable Death Sound":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.DeathSoundCosmeticEnabled, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.DeathSoundCosmeticEnabled);
			});
		case "Death Sound":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.DeathSoundCosmeticId = Cosmetics.CycleParticle(Cosmetics.DeathSounds, Cosmetics.DeathSoundCosmeticId, back);
				SoundEngine.PlaySound(10);
			});
		case "Death Cosmetics for Others":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.DeathCosmeticsForAll, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.DeathCosmeticsForAll);
			});
		case "Always Have After-Images":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.ArmorShadowCosmetic, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.ArmorShadowCosmetic);
			});
		case "Always Pulsate":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.ArmorOutlineCosmetic, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.ArmorOutlineCosmetic);
			});
		case "Lucy Messages":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.LucyMessagesMode = MenuControls.Cycle(Cosmetics.LucyMessagesMode, 0, 3, back);
			});
		case "Lucy Message on Death":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Cosmetics.LucyDeathMessageMode = MenuControls.Cycle(Cosmetics.LucyDeathMessageMode, 0, 3, back);
			});
		case "RGB Map Outline for Self":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.RgbPlayerOutline, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.RgbPlayerOutline);
			});
		case "RGB Map Icons for NPCs":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.RgbNpcIcons, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.RgbNpcIcons);
			});
		case "RGB Map Icons for Bosses":
			return MenuControls.MakeToggle(cheat, color, () => Cosmetics.RgbBossIcons, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Cosmetics.RgbBossIcons);
			});
		case "Disable & Reset All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Cosmetics.DisableAll();
			});
		default:
			return new UIElement();
		}
	}
	internal static readonly List<string> Entries = new List<string>
	{
		"Walking Particles", "Particle Cursor", "Enable Death Particle", "Death Particle", "Enable Death Effect", "Death Effect", "Enable Death Sound", "Death Sound", "Death Cosmetics for Others", "Always Have After-Images",
		"Always Pulsate", "Lucy Messages", "Lucy Message on Death", "RGB Map Outline for Self", "RGB Map Icons for NPCs", "RGB Map Icons for Bosses", "Disable & Reset All"
	};

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Walking Particles" => "Leave behind a trail of particles when you walk.\nFlames (Dyed) uses your mount dye.",
			"Particle Cursor" => "Displays a particle at your cursor to everyone.",
			"Enable Death Particle" => "Enables the Death Particle cosmetic.",
			"Death Particle" => "The particle to emit when you die. Some will also play sound effects.",
			"Enable Death Effect" => "Enables the Death Effect cosmetic.",
			"Death Effect" => "The effect to emit when you die. These are silent, but may last longer.",
			"Enable Death Sound" => "Enables the Death Sound cosmetic.",
			"Death Sound" => "The sound to play when you die.\nThe selection is limited because there aren't many \"sound only\" particles.",
			"Death Cosmetics for Others" => "Enable death cosmetics for everyone on a server.\nWill not sync 100% on their end, but they still work.",
			"Always Have After-Images" => "You will always have an after-image trailing behind you.\nOnly you see it.",
			"Always Pulsate" => "You will always appear to pulsate.\nOnly you see it.",
			"Lucy Messages" => "Become schizophrenic or silence Lucy.",
			"Lucy Message on Death" => "Be haunted by Lucy when you die.",
			"RGB Map Outline for Self" => "Your map icon cycles through colors. Because RGB is cool.\nOnly you see it.",
			"RGB Map Icons for NPCs" => "Because RGB is cool.",
			"RGB Map Icons for Bosses" => "Because RGB is cool.",
			_ => null,
		};
	}
}
