using System.Reflection;

namespace TerraAngel.Plugin;

/// <summary>
/// Base class of a plugin, the same as TerraAngel's (TerraAngel.PluginAPI): a TerraAngel plugin's source builds against
/// IsaClient unchanged. IsaClient finds it in <c>*.TAPlugin.dll</c> and <c>*.IsaPlugin.dll</c> files in its Plugins folder.
/// </summary>
public abstract class Plugin
{
	public bool IsInited = false;

	public abstract string Name { get; }

	public readonly Assembly PluginAssembly;

	public readonly string PluginPath;

	public Plugin(string path)
	{
		PluginAssembly = GetType().Assembly;
		PluginPath = path;
	}

	/// <summary>Called once when the plugin is turned on (or the game starts with it on).</summary>
	public virtual void Load()
	{
	}

	/// <summary>Called once when the plugin is turned off or the game closes.</summary>
	public virtual void Unload()
	{
	}

	/// <summary>Called every frame (game update) while the plugin is loaded.</summary>
	public virtual void Update()
	{
	}
}
