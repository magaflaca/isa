using System.Collections.Generic;
using ImGuiNET;

namespace IsaClient.Plugins;

/// <summary>
/// Fonts plugins added to ImGui's atlas, by a key of their own. The atlas outlives a plugin: after a reload, a plugin
/// finds its fonts here instead of adding them again (the atlas would grow past the 2048 px XNA allows).
/// </summary>
public static class ImGuiFonts
{
	private static readonly Dictionary<string, ImFontPtr> _fonts = new Dictionary<string, ImFontPtr>();

	public static bool TryGet(string key, out ImFontPtr font) => _fonts.TryGetValue(key, out font);

	public static void Remember(string key, ImFontPtr font) => _fonts[key] = font;
}
