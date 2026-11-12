using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Localization;

namespace IsaClient.Community;

/// <summary>A language the translator knows: the code you type (.translate es en), its names and MyMemory's code.</summary>
public sealed class ChatLanguage
{
	public readonly string Code;

	public readonly string Name;

	/// <summary>The name in the language itself, or null when Terraria's font can't draw it.</summary>
	public readonly string Native;

	public readonly string ServiceCode;

	public readonly string[] Aliases;

	public ChatLanguage(string code, string name, string native, string serviceCode = null, params string[] aliases)
	{
		Code = code;
		Name = name;
		Native = native;
		ServiceCode = serviceCode ?? code;
		Aliases = aliases;
	}

	/// <summary>"Spanish (Español)".</summary>
	public string Label => (Native == null || Native == Name) ? Name : (Name + " (" + Native + ")");
}

/// <summary>The languages of .translate, the most spoken ones first.</summary>
public static class ChatLanguages
{
	/// <summary>"auto": MyMemory detects the language you write in.</summary>
	public static readonly ChatLanguage Auto = new ChatLanguage("auto", "Detect", null, "Autodetect", "detect", "any", "autodetect");

	/// <summary>"game": whatever language the game is set to (<see cref="GameLanguage"/>).</summary>
	public static readonly ChatLanguage Game = new ChatLanguage("game", "Game Language", null, null, "client", "default", "juego");

	public static readonly IReadOnlyList<ChatLanguage> All = new[]
	{
		new ChatLanguage("en", "English", "English", null, "english", "ingles", "inglés", "eng"),
		new ChatLanguage("es", "Spanish", "Español", null, "spanish", "espanol", "español", "spa"),
		new ChatLanguage("pt", "Portuguese", "Português", null, "portuguese", "portugues", "português", "br", "pt-br"),
		new ChatLanguage("fr", "French", "Français", null, "french", "frances", "francés", "francais"),
		new ChatLanguage("de", "German", "Deutsch", null, "german", "aleman", "alemán", "deutsch", "ger"),
		new ChatLanguage("it", "Italian", "Italiano", null, "italian", "italiano", "ita"),
		new ChatLanguage("ru", "Russian", "Русский", null, "russian", "ruso", "rus"),
		new ChatLanguage("zh", "Chinese", null, "zh-CN", "chinese", "chino", "cn", "zh-cn"),
		new ChatLanguage("ja", "Japanese", null, null, "japanese", "japones", "japonés", "jp"),
		new ChatLanguage("ko", "Korean", null, null, "korean", "coreano", "kr"),
		new ChatLanguage("ar", "Arabic", null, null, "arabic", "arabe", "árabe"),
		new ChatLanguage("hi", "Hindi", null, null, "hindi"),
		new ChatLanguage("tr", "Turkish", "Türkçe", null, "turkish", "turco"),
		new ChatLanguage("pl", "Polish", "Polski", null, "polish", "polaco"),
		new ChatLanguage("nl", "Dutch", "Nederlands", null, "dutch", "holandes", "holandés", "neerlandes"),
		new ChatLanguage("uk", "Ukrainian", null, null, "ukrainian", "ucraniano", "ua"),
		new ChatLanguage("id", "Indonesian", "Indonesia", null, "indonesian", "indonesio", "bahasa"),
		new ChatLanguage("vi", "Vietnamese", null, null, "vietnamese", "vietnamita"),
		new ChatLanguage("sv", "Swedish", "Svenska", null, "swedish", "sueco")
	};

	/// <summary>The language the game is set to (Settings > Language), English when the translator doesn't know it.</summary>
	public static ChatLanguage GameLanguage
	{
		get
		{
			try
			{
				return Find(LanguageManager.Instance.ActiveCulture?.CultureInfo.TwoLetterISOLanguageName) ?? All[0];
			}
			catch
			{
				return All[0];
			}
		}
	}

	/// <summary>"es", "spanish", "español"... (any case). <paramref name="allowAuto"/> also accepts "auto", <paramref name="allowGame"/> "game".</summary>
	public static ChatLanguage Find(string text, bool allowAuto = false, bool allowGame = false)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		text = text.Trim();
		IEnumerable<ChatLanguage> candidates = allowAuto ? new[] { Auto }.Concat(All) : All;
		if (allowGame)
		{
			candidates = new[] { Game }.Concat(candidates);
		}
		foreach (ChatLanguage language in candidates)
		{
			if (language.Code.Equals(text, StringComparison.OrdinalIgnoreCase) || language.Name.Equals(text, StringComparison.OrdinalIgnoreCase) || language.Aliases.Any(a => a.Equals(text, StringComparison.OrdinalIgnoreCase)))
			{
				return language;
			}
		}
		return null;
	}

	/// <summary>"en, es, pt, ..." for help texts.</summary>
	public static string Codes()
	{
		return string.Join(", ", All.Select(l => l.Code + " " + l.Name));
	}
}
