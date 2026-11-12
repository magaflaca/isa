using System;
using System.Collections.Generic;
using ImGuiNET;
using IsaClient.Core;
using TerraAngel;

namespace IsaClient.Plugins;

/// <summary>
/// Base class for IsaClient plugins: a TerraAngel <see cref="TerraAngel.Plugin.Plugin"/> (so it gets Load, Unload and
/// Update) plus a tab of its own in the plugin overlay, a layer drawn over the game, chat commands and a data folder.
/// Plugins that only use TerraAngel's API also work; this adds what IsaClient has on top.
/// </summary>
public abstract class ClientPlugin : TerraAngel.Plugin.Plugin
{
	protected ClientPlugin()
		: base(null)
	{
	}

	public virtual string Author => "";

	public virtual string Version => "1.0";

	public virtual string Description => "";

	/// <summary>True while the plugin is on (its commands only run then).</summary>
	public bool Enabled { get; internal set; }

	/// <summary>A folder for the plugin's own files: Plugins/Data/&lt;Name&gt;.</summary>
	public string DataFolder => ClientLoader.PluginDataPath(Name);

	private HarmonyLib.Harmony _harmony;

	/// <summary>
	/// The plugin's Harmony instance, to change game methods: <c>Harmony.PatchAll()</c> in Load applies every
	/// [HarmonyPatch] class of the plugin. Its patches are removed by themselves when the plugin is turned off.
	/// </summary>
	protected HarmonyLib.Harmony Harmony => _harmony ??= new HarmonyLib.Harmony("isaclient.plugin." + GetType().FullName);

	/// <summary>The plugin's tab in the overlay (only shown when a plugin overrides it).</summary>
	public virtual void DrawTab(ImGuiIOPtr io)
	{
	}

	/// <summary>
	/// Drawn every frame over the game, also while the overlay is closed: ESP boxes, markers, HUD text. Use the background
	/// draw list (<c>ImGui.GetBackgroundDrawList()</c>) or your own window without decorations.
	/// </summary>
	public virtual void DrawOverlay(ImGuiIOPtr io)
	{
	}

	/// <summary>The player joined a world (single player or a server).</summary>
	public virtual void OnEnterWorld()
	{
	}

	/// <summary>
	/// Every drawn frame, before the game draws: the place to draw into your own render targets (a preview texture for
	/// an ImGui.Image). After the game drew, switching render targets would wipe its frame.
	/// </summary>
	public virtual void BeforeGameDraw()
	{
	}

	/// <summary>Every drawn frame, before the ImGui frame starts (after the game drew): add fonts, prepare ImGui state.</summary>
	public virtual void BeforeImGuiFrame()
	{
	}

	/// <summary>Every drawn frame, after ImGui drew: load textures it asked for, and similar work.</summary>
	public virtual void AfterImGuiFrame()
	{
	}

	/// <summary>
	/// True when the plugin lists its tools in a main window of its own (the TerraAngel pack does): the overlay's
	/// tool tabs then leave them out.
	/// </summary>
	public virtual bool HasOwnToolWindow => false;

	/// <summary>
	/// A chat command (<c>.name args</c>) listed in .help under Plugins. Names already taken by the client are skipped.
	/// <paramref name="run"/> gets the arguments after the name. After a reload the new copy of the plugin takes the name over.
	/// </summary>
	protected void AddChatCommand(string name, string usage, string summary, Action<string[]> run)
	{
		name = name.TrimStart('.').ToLowerInvariant();
		bool registered = _chatCommands.ContainsKey(name);
		_chatCommands[name] = new KeyValuePair<ClientPlugin, Action<string[]>>(this, run);
		if (!registered)
		{
			CommandRegistry.Register(command => RunChatCommand(name, command), name).Help(CommandSection.Plugins, usage ?? "", "[" + Name + "] " + summary);
		}
	}

	private static readonly Dictionary<string, KeyValuePair<ClientPlugin, Action<string[]>>> _chatCommands = new Dictionary<string, KeyValuePair<ClientPlugin, Action<string[]>>>();

	private static void RunChatCommand(string name, CommandContext command)
	{
		ClientPlugin plugin = _chatCommands[name].Key;
		if (!plugin.Enabled)
		{
			ClientChat.Error(plugin.Name + " is turned off (.plugins on " + plugin.Name + ")");
			return;
		}
		string[] args = new string[Math.Max(0, command.Parts.Length - 1)];
		Array.Copy(command.Parts, 1, args, 0, args.Length);
		try
		{
			_chatCommands[name].Value(args);
		}
		catch (Exception ex)
		{
			ClientChat.Error(plugin.Name + ": " + ex.Message);
			PluginLog.Error(plugin.Name + " ." + name + ": " + ex.Message, ex);
		}
	}

	/// <summary>A line in the chat, in the client's color.</summary>
	protected static void Chat(string text) => ClientChat.Info(text);

	protected static void ChatError(string text) => ClientChat.Error(text);
}
