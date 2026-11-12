using System;
using System.Collections.Generic;
using IsaClient.Assistant;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → AI: the AI chat assistant settings.</summary>
public static class AiMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"AFK AI", "Reply to Everyone", "Lowercase Replies", "Remove Accents", "Remove Emojis", "Include Game Info",
		"AI Model", "AI Temperature", "AI Reply Delay", "AI Cooldown", "AI Context Messages", "Test AI",
		"Forget Conversation", "Edit AI Config (Prompt, About Me, Key...)", "Reload AI Config", "Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "AFK AI":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.Enabled, delegate
			{
				AiAssistant.Toggle();
				SoundEngine.PlaySound(AiAssistant.Enabled ? 10 : 11);
			});
		case "Reply to Everyone":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.ReplyToEveryone, delegate
			{
				AiAssistant.ReplyToEveryone = !AiAssistant.ReplyToEveryone;
				AiAssistant.Save();
				SoundEngine.PlaySound(AiAssistant.ReplyToEveryone ? 10 : 11);
			});
		case "Lowercase Replies":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.Lowercase, delegate
			{
				AiAssistant.Lowercase = !AiAssistant.Lowercase;
				AiAssistant.Save();
				SoundEngine.PlaySound(AiAssistant.Lowercase ? 10 : 11);
			});
		case "Remove Accents":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.RemoveAccents, delegate
			{
				AiAssistant.RemoveAccents = !AiAssistant.RemoveAccents;
				AiAssistant.Save();
				SoundEngine.PlaySound(AiAssistant.RemoveAccents ? 10 : 11);
			});
		case "Remove Emojis":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.RemoveEmojis, delegate
			{
				AiAssistant.RemoveEmojis = !AiAssistant.RemoveEmojis;
				AiAssistant.Save();
				SoundEngine.PlaySound(AiAssistant.RemoveEmojis ? 10 : 11);
			});
		case "Include Game Info":
			return MenuControls.MakeToggle(cheat, color, () => AiAssistant.IncludeGameInfo, delegate
			{
				AiAssistant.IncludeGameInfo = !AiAssistant.IncludeGameInfo;
				AiAssistant.Save();
				SoundEngine.PlaySound(AiAssistant.IncludeGameInfo ? 10 : 11);
			});
		case "AI Model":
			return MenuControls.MakeButton(cheat, color, () => "AI Model: " + AiAssistant.Model, delegate
			{
				SoundEngine.PlaySound(12);
				AiAssistant.CycleModel();
			});
		case "AI Temperature":
			return MenuControls.MakeSlider(cheat, color, () => "AI Creativity (Temperature): " + AiAssistant.Temperature.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), () => AiAssistant.Temperature / 1.5f, delegate(float f)
			{
				AiAssistant.Temperature = (float)Math.Round(1.5f * f, 2);
			}, 6915);
		case "AI Reply Delay":
			return MenuControls.MakeSlider(cheat, color, () => "AI Reply Delay: " + AiAssistant.ReplyDelaySeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s", () => AiAssistant.ReplyDelaySeconds / 15f, delegate(float f)
			{
				AiAssistant.ReplyDelaySeconds = (float)Math.Round(15f * f, 1);
			}, 6916);
		case "AI Cooldown":
			return MenuControls.MakeSlider(cheat, color, () => "AI Cooldown Between Replies: " + AiAssistant.CooldownSeconds + " s", () => AiAssistant.CooldownSeconds / 60f, delegate(float f)
			{
				AiAssistant.CooldownSeconds = (int)Math.Round(60f * f);
			}, 6917);
		case "AI Context Messages":
			return MenuControls.MakeSlider(cheat, color, () => "AI Memory: last " + AiAssistant.ContextMessages + " messages (" + AiAssistant.ContextMinutes + " min)", () => AiAssistant.ContextMessages / 100f, delegate(float f)
			{
				AiAssistant.ContextMessages = (int)Math.Round(100f * f);
			}, 6918);
		case "Test AI":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				AiAssistant.HandleCommand("ai", ".ai test", new string[2] { ".ai", "test" });
			});
		case "Forget Conversation":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				AiAssistant.HandleCommand("ai", ".ai forget", new string[2] { ".ai", "forget" });
			});
		case "Edit AI Config (Prompt, About Me, Key...)":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				AiAssistant.OpenConfigFile();
			});
		case "Reload AI Config":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(12);
				AiAssistant.Load();
				ClientChat.Info("Reloaded " + AiAssistant.ConfigPath);
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

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		AiAssistant.Enabled = false;
		AiAssistant.ReplyToEveryone = false;
		AiAssistant.Save();
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"AFK AI" => "Answers for you in the chat when someone mentions you (.ai / .afk, .afk --nick a,b).",
			"Reply to Everyone" => "Also answer messages that do not mention you, when the AI thinks they are meant for you.",
			"Lowercase Replies" => "Write the replies in lowercase.",
			"Remove Accents" => "Remove accents and the Spanish opening marks from the replies.",
			"Remove Emojis" => "Remove emojis and symbols from the replies.",
			"Include Game Info" => "Tell the AI the world, the players online and your character's state.",
			"AI Model" => "Click to cycle between Groq models (.ai model <name> for any other).",
			"AI Temperature" => "Lower = more predictable replies, higher = more creative.",
			"AI Reply Delay" => "Waits this long before sending, so it looks like a person typing.",
			"AI Cooldown" => "Minimum time between two replies.",
			"AI Context Messages" => "How many chat messages the AI remembers (also ContextMinutes in IsaAI.json).",
			"Test AI" => "Asks the AI something and shows the answer only to you (.ai test <message>).",
			"Forget Conversation" => "Clears what the AI remembers of the chat (.ai forget).",
			"Edit AI Config (Prompt, About Me, Key...)" => "Opens IsaAI.json: SystemPrompt (empty = automatic), AboutMe, Nicknames, ReplyLanguage, ApiKey...\nPress Reload AI Config after saving.",
			"Reload AI Config" => "Reloads IsaAI.json.",
			_ => null,
		};
	}
}
