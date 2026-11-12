using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Plugins.DevTools;
using TerraAngel;
using TerraAngel.Tools;
using TerraAngel.UI;

namespace IsaClient.Plugins;

/// <summary>What a plugin entry is made of.</summary>
public enum PluginKind
{
	/// <summary>A .NET Framework library (*.IsaPlugin.dll / *.TAPlugin.dll built for this client).</summary>
	Library,
	/// <summary>A TerraAngel build for .NET 5-10: converted (decompiled and compiled again) before it loads.</summary>
	Converted,
	/// <summary>A folder of C# files in Plugins/Sources, compiled by the client.</summary>
	Source,
	/// <summary>A TerraAngel fork in Plugins/Forks, built on the TerraAngel pack into its own TerraAngel (ForkBuilder).</summary>
	Fork
}

/// <summary>One plugin in the Plugins folder and what happened to it.</summary>
public sealed class PluginEntry
{
	/// <summary>The library file, or the source folder of a Source plugin.</summary>
	public string File;

	/// <summary>The name without ".TAPlugin.dll" / ".IsaPlugin.dll" (the folder name for sources).</summary>
	public string Key;

	public PluginKind Kind;

	public TerraAngel.Plugin.Plugin Instance;

	public Assembly Assembly;

	/// <summary>The library that was loaded (the converted or compiled one for those kinds), referenced by other compiles.</summary>
	public string LoadedFrom;

	/// <summary>Why it failed to load; null when it is fine.</summary>
	public string Error;

	/// <summary>Compiler errors of a Source or Converted plugin (the first ones).</summary>
	public List<string> BuildErrors = new List<string>();

	/// <summary>Being converted or compiled right now.</summary>
	public bool Building;

	/// <summary>When the loaded copy was read from disk (to notice a newer build).</summary>
	public DateTime LoadedWriteTime;

	public bool Loaded => Instance != null && Instance.IsInited;

	public string Name => Instance?.Name ?? Key;

	public bool IsTerraAngel => Kind == PluginKind.Converted || File.EndsWith(".TAPlugin.dll", StringComparison.OrdinalIgnoreCase);

	public DateTime WriteTime => PluginLoader.WriteTimeOf(this);

	public bool ChangedOnDisk => Loaded && WriteTime != LoadedWriteTime;
}

/// <summary>
/// Finds plugins in Documents/My Games/Terraria/IsaClient/Plugins: <c>*.IsaPlugin.dll</c>, TerraAngel's <c>*.TAPlugin.dll</c>
/// (also builds for .NET 10, which are converted) and C# source folders in <c>Plugins/Sources</c> (compiled by the
/// client); loads the ones the player turned on and runs them. A plugin is read from memory (the file stays free, so a
/// new build can replace it and be reloaded without restarting); its own extra libraries are looked up next to it.
/// New plugins start turned off: they are code that runs inside the game, the player decides.
/// </summary>
public static class PluginLoader
{
	[Setting("PluginsEnabled")]
	public static string EnabledList = "";

	private static readonly List<PluginEntry> _entries = new List<PluginEntry>();

	/// <summary>Builds that finished off the game thread, loaded in the next frame.</summary>
	private static readonly List<KeyValuePair<PluginEntry, CompileResult>> _built = new List<KeyValuePair<PluginEntry, CompileResult>>();

	private static bool _resolverAdded;

	public static string PluginsFolder => Path.Combine(ClientSettings.IsaClientFolder, "Plugins");

	public static string SourcesFolder => Path.Combine(PluginsFolder, "Sources");

	/// <summary>TerraAngel forks, as people edit them (see ForkBuilder and Forks/README.txt).</summary>
	public static string ForksFolder => Path.Combine(PluginsFolder, "Forks");

	/// <summary>The TerraAngel pack's entry (key TerraAngel), the base of every fork.</summary>
	private static PluginEntry Pack => _entries.FirstOrDefault(e => e.Kind == PluginKind.Library && e.Key == "TerraAngel");

	/// <summary>A fork that is turned on (it takes the pack's place), or null.</summary>
	private static PluginEntry EnabledFork => _entries.FirstOrDefault(e => e.Kind == PluginKind.Fork && IsEnabled(e));

	public static string CompiledFolder => Path.Combine(PluginsFolder, "Compiled");

	public static IReadOnlyList<PluginEntry> Entries => _entries;

	public static bool AnyLoaded => _entries.Any(e => e.Loaded);

	private static HashSet<string> Enabled => new HashSet<string>(EnabledList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

	public static bool IsEnabled(PluginEntry entry) => Enabled.Contains(entry.Key);

	/// <summary>Looks at the folder again: new plugins are listed (off), removed ones forgotten (unloaded first).</summary>
	public static void Scan()
	{
		Directory.CreateDirectory(PluginsFolder);
		Directory.CreateDirectory(SourcesFolder);
		List<KeyValuePair<string, PluginKind>> found = Directory.GetFiles(PluginsFolder, "*.dll")
			.Where(f => f.EndsWith(".IsaPlugin.dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".TAPlugin.dll", StringComparison.OrdinalIgnoreCase))
			.Select(f => new KeyValuePair<string, PluginKind>(f, IsModernDotNet(f) ? PluginKind.Converted : PluginKind.Library))
			.ToList();
		foreach (string folder in Directory.GetDirectories(SourcesFolder))
		{
			if (Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length > 0)
			{
				found.Add(new KeyValuePair<string, PluginKind>(folder, PluginKind.Source));
			}
		}
		if (Directory.GetFiles(SourcesFolder, "*.cs").Length > 0)
		{
			// Loose files in Sources make one plugin together.
			found.Add(new KeyValuePair<string, PluginKind>(SourcesFolder, PluginKind.Source));
		}
		Directory.CreateDirectory(ForksFolder);
		ForkBuilder.WriteReadme(ForksFolder);
		foreach (string folder in Directory.GetDirectories(ForksFolder))
		{
			found.Add(new KeyValuePair<string, PluginKind>(folder, PluginKind.Fork));
		}
		foreach (PluginEntry gone in _entries.Where(e => !found.Any(f => string.Equals(f.Key, e.File, StringComparison.OrdinalIgnoreCase))).ToList())
		{
			Unload(gone);
			_entries.Remove(gone);
		}
		foreach (KeyValuePair<string, PluginKind> item in found.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
		{
			PluginEntry entry = _entries.FirstOrDefault(e => string.Equals(e.File, item.Key, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				_entries.Add(new PluginEntry { File = item.Key, Kind = item.Value, Key = KeyOf(item.Key, item.Value) });
			}
			else if (!entry.Loaded)
			{
				entry.Kind = item.Value;
			}
		}
	}

	private static string KeyOf(string file, PluginKind kind)
	{
		if (kind == PluginKind.Fork)
		{
			// Never the pack's own name (or Sources'): the fork turns the pack off by its key.
			string folderName = Path.GetFileName(file.TrimEnd('\\', '/'));
			return (folderName.Equals("TerraAngel", StringComparison.OrdinalIgnoreCase) || folderName.Equals("Sources", StringComparison.OrdinalIgnoreCase)) ? (folderName + "-fork") : folderName;
		}
		if (kind == PluginKind.Source)
		{
			return string.Equals(file.TrimEnd('\\', '/'), SourcesFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ? "Sources" : Path.GetFileName(file.TrimEnd('\\', '/'));
		}
		string name = Path.GetFileName(file);
		foreach (string suffix in new[] { ".IsaPlugin.dll", ".TAPlugin.dll" })
		{
			if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return name.Substring(0, name.Length - suffix.Length);
			}
		}
		return Path.GetFileNameWithoutExtension(name);
	}

	/// <summary>The newest change: the library's, or the newest .cs of a source folder.</summary>
	internal static DateTime WriteTimeOf(PluginEntry entry)
	{
		try
		{
			if (entry.Kind == PluginKind.Fork)
			{
				// A fork has thousands of files: looked at again every few seconds, not every frame.
				if (_forkTimes.TryGetValue(entry.File, out KeyValuePair<DateTime, DateTime> known) && (DateTime.UtcNow - known.Key).TotalSeconds < 3.0)
				{
					return known.Value;
				}
				DateTime newest = Directory.GetFiles(entry.File, "*.cs", SearchOption.AllDirectories).Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 && f.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
				_forkTimes[entry.File] = new KeyValuePair<DateTime, DateTime>(DateTime.UtcNow, newest);
				return newest;
			}
			if (entry.Kind == PluginKind.Source)
			{
				SearchOption depth = entry.Key == "Sources" ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
				return Directory.GetFiles(entry.File, "*.cs", depth).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
			}
			return File.Exists(entry.File) ? File.GetLastWriteTimeUtc(entry.File) : default;
		}
		catch
		{
			return default;
		}
	}

	private static readonly Dictionary<string, KeyValuePair<DateTime, DateTime>> _forkTimes = new Dictionary<string, KeyValuePair<DateTime, DateTime>>(StringComparer.OrdinalIgnoreCase);

	/// <summary>True for a library built for .NET 5 or newer (it references System.Runtime 5+): a TerraAngel build.</summary>
	private static bool IsModernDotNet(string file)
	{
		DateTime time = File.GetLastWriteTimeUtc(file);
		if (_modernCache.TryGetValue(file, out KeyValuePair<DateTime, bool> known) && known.Key == time)
		{
			return known.Value;
		}
		bool modern = ReadIsModernDotNet(file);
		_modernCache[file] = new KeyValuePair<DateTime, bool>(time, modern);
		return modern;
	}

	private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> _modernCache = new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);

	private static bool ReadIsModernDotNet(string file)
	{
		try
		{
			// Read without loading it for running (the metadata is enough).
			AssemblyName[] references = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(file)).GetReferencedAssemblies();
			return references.Any(r => r.Name == "System.Runtime" && r.Version.Major >= 5);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Scans and loads every plugin that is turned on.</summary>
	public static void LoadEnabled()
	{
		AddResolver();
		Scan();
		// Libraries first: sources may use them (fork code needs the TerraAngel pack).
		foreach (PluginEntry entry in _entries.OrderBy(e => e.Kind == PluginKind.Library ? 0 : 1).ToList())
		{
			if (IsEnabled(entry) && !entry.Loaded)
			{
				Load(entry);
			}
		}
	}

	public static PluginEntry Find(string name)
	{
		return _entries.FirstOrDefault(e => string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase))
			?? _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
			?? _entries.FirstOrDefault(e => e.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	/// <summary>Turns a plugin on (loads it now) or off (unloads it now) and remembers the choice.</summary>
	public static void SetEnabled(PluginEntry entry, bool enabled)
	{
		HashSet<string> set = Enabled;
		if (enabled)
		{
			set.Add(entry.Key);
			// A fork and the pack are both TerraAngel: only one at a time.
			foreach (PluginEntry other in _entries.Where(e => e != entry && ((entry.Kind == PluginKind.Fork && (e.Kind == PluginKind.Fork || e == Pack)) || (entry == Pack && e.Kind == PluginKind.Fork))).ToList())
			{
				if (set.Remove(other.Key) && other.Loaded)
				{
					PluginLog.Info("Turning off " + other.Key + ": " + entry.Key + " takes its place (only one TerraAngel at a time)");
					Unload(other);
				}
			}
		}
		else
		{
			set.Remove(entry.Key);
		}
		EnabledList = string.Join(",", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
		ClientSettings.Save();
		if (enabled && !entry.Loaded)
		{
			Load(entry);
		}
		else if (!enabled && entry.Loaded)
		{
			Unload(entry);
		}
	}

	/// <summary>A server turned the client's cheats off (ServerModes): unloads every plugin, without changing which are on.</summary>
	public static List<PluginEntry> UnloadAllForServer()
	{
		List<PluginEntry> loaded = _entries.Where(e => e.Loaded).ToList();
		foreach (PluginEntry entry in loaded)
		{
			try
			{
				Unload(entry);
			}
			catch (Exception ex)
			{
				PluginLog.Error("Could not unload " + entry.Key + ": " + ex.Message, ex);
			}
		}
		if (loaded.Count > 0)
		{
			PluginLog.Info("Plugins off on this server (" + loaded.Count + "): they come back when you leave");
		}
		return loaded;
	}

	/// <summary>Leaving that server: the plugins it turned off load again (those still on).</summary>
	public static void LoadAgain(IEnumerable<PluginEntry> entries)
	{
		foreach (PluginEntry entry in entries.Distinct())
		{
			if (!entry.Loaded && IsEnabled(entry) && _entries.Contains(entry))
			{
				try
				{
					Load(entry);
				}
				catch (Exception ex)
				{
					PluginLog.Error("Could not load " + entry.Key + " again: " + ex.Message, ex);
				}
			}
		}
	}

	/// <summary>Unloads a plugin and loads its file again (after building a new version).</summary>
	public static void Reload(PluginEntry entry)
	{
		// .NET Framework keeps answering a full assembly name with the copy it gave first, so plugins built on this one
		// (and C# console lines) still see the old copy until the game restarts.
		Assembly before = entry.Assembly;
		string name = before?.GetName().Name;
		List<string> users = (name == null) ? new List<string>() : _entries.Where(e => e != entry && e.Loaded && e.Assembly != null && e.Assembly.GetReferencedAssemblies().Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))).Select(e => e.Name).ToList();
		Unload(entry);
		Load(entry);
		if (name != null && entry.Assembly != before)
		{
			string who = (users.Count > 0) ? string.Join(", ", users) + " and the C# console" : "The C# console";
			PluginLog.Info(entry.Name + " was reloaded. " + who + " keep using the copy loaded first until the game restarts.");
		}
	}

	/// <summary>Loads a library now; converts or compiles the other kinds in the background first.</summary>
	public static void Load(PluginEntry entry)
	{
		entry.Error = null;
		if (entry.Kind == PluginKind.Library)
		{
			if (entry.Assembly != null && entry.LoadedFrom == entry.File && entry.LoadedWriteTime == entry.WriteTime)
			{
				// The same file as last time: the same copy again (a second copy would split the types other plugins
				// and the C# console see, see Reload).
				LoadAssembly(entry, null, null, entry.File);
				return;
			}
			byte[] bytes;
			byte[] symbols;
			try
			{
				bytes = File.ReadAllBytes(entry.File);
				string pdb = Path.ChangeExtension(entry.File, ".pdb");
				symbols = File.Exists(pdb) ? File.ReadAllBytes(pdb) : null;
			}
			catch (Exception ex)
			{
				entry.Error = ex.Message;
				return;
			}
			LoadAssembly(entry, bytes, symbols, entry.File);
			return;
		}
		if (entry.Building)
		{
			return;
		}
		entry.Building = true;
		entry.BuildErrors.Clear();
		string what = (entry.Kind == PluginKind.Converted) ? "converting " + entry.Key + " (a TerraAngel build for .NET 10)" : ((entry.Kind == PluginKind.Fork) ? "building the TerraAngel fork " + entry.Key : "compiling the sources of " + entry.Key);
		DevToolsInstaller.Ensure(what, delegate
		{
			PluginLog.Info(char.ToUpper(what[0]) + what.Substring(1) + "...");
			List<string> references = _entries.Where(e => e.Loaded && e.LoadedFrom != null).Select(e => e.LoadedFrom).ToList();
			// The TerraAngel pack is referenced even when it is not on: fork code and converted builds use its library
			// (unless a TerraAngel fork is on: then it is the TerraAngel library, and the pack would clash with it).
			PluginEntry pack = Pack;
			if (pack != null && EnabledFork == null && !references.Contains(pack.File))
			{
				references.Add(pack.File);
			}
			Task.Run(delegate
			{
				CompileResult result;
				try
				{
					if (entry.Kind == PluginKind.Fork)
					{
						// The fork is TerraAngel itself: built on the pack's source, referencing no other plugin.
						result = ForkBuilder.Build(entry.Key, entry.File, pack?.File, new string[0], out ForkBuilder.Report report);
						PluginLog.Info(entry.Key + ": " + report.Summary + ". Report: Plugins/Compiled/" + entry.Key + ".fork.txt");
					}
					else
					{
						result = (entry.Kind == PluginKind.Converted) ? PluginConverter.Convert(entry.File, references) : SourceBuilder.Build(entry, references);
					}
				}
				catch (Exception ex)
				{
					result = new CompileResult();
					result.Errors.Add(ex.GetType().Name + ": " + ex.Message);
					PluginLog.Error("Building " + entry.Key + ": " + ex.Message, ex);
				}
				lock (_built)
				{
					_built.Add(new KeyValuePair<PluginEntry, CompileResult>(entry, result));
				}
			});
		});
	}

	/// <summary>The plugin system's frame: plugins whose build finished are loaded now.</summary>
	internal static void LoadBuilt()
	{
		List<KeyValuePair<PluginEntry, CompileResult>> built;
		lock (_built)
		{
			if (_built.Count == 0)
			{
				return;
			}
			built = _built.ToList();
			_built.Clear();
		}
		foreach (KeyValuePair<PluginEntry, CompileResult> item in built)
		{
			PluginEntry entry = item.Key;
			CompileResult result = item.Value;
			entry.Building = false;
			if (!result.Success)
			{
				entry.BuildErrors = result.Errors.Take(30).ToList();
				entry.Error = (entry.Kind == PluginKind.Converted ? "could not be converted: " : "does not compile: ") + (result.Errors.FirstOrDefault() ?? "unknown error") + (result.Errors.Count > 1 ? " (+" + (result.Errors.Count - 1) + " more, see Plugins/" + (entry.Kind == PluginKind.Converted ? "Converted" : "Compiled") + ")" : "");
				PluginLog.Error(entry.Key + " " + entry.Error);
				continue;
			}
			if (!IsEnabled(entry))
			{
				continue;
			}
			// The result is kept on disk: other compiles reference it, and it can be looked at.
			Directory.CreateDirectory(CompiledFolder);
			string file = Path.Combine(CompiledFolder, (entry.Kind == PluginKind.Converted ? Path.GetFileNameWithoutExtension(entry.File) : entry.Key + ((entry.Kind == PluginKind.Fork) ? ".Fork.TAPlugin" : ".Source.TAPlugin")) + ".dll");
			try
			{
				File.WriteAllBytes(file, result.Assembly);
				File.WriteAllBytes(Path.ChangeExtension(file, ".pdb"), result.Symbols);
			}
			catch
			{
				// Another copy of the game may hold the file: loading from memory still works.
			}
			LoadAssembly(entry, result.Assembly, result.Symbols, file);
			if (entry.Kind == PluginKind.Fork && entry.Loaded)
			{
				// Plugins made for the pack (TerraAngel Community...) failed while the fork was building: now they find it.
				foreach (PluginEntry waiting in _entries.Where(e => e != entry && IsEnabled(e) && !e.Loaded && !e.Building && e.Error != null).ToList())
				{
					Load(waiting);
				}
			}
		}
	}

	private static void LoadAssembly(PluginEntry entry, byte[] bytes, byte[] symbols, string loadedFrom)
	{
		_loading.Add(entry);
		try
		{
			Assembly assembly = (bytes == null) ? entry.Assembly : ((symbols != null) ? Assembly.Load(bytes, symbols) : Assembly.Load(bytes));
			// Set now: plugins that need this one find it while its types are read.
			entry.Assembly = assembly;
			Type type = PluginTypes(assembly).FirstOrDefault();
			if (type == null)
			{
				throw new InvalidOperationException("no public class that inherits TerraAngel.Plugin.Plugin or IsaClient.Plugins.ClientPlugin");
			}
			TerraAngel.Plugin.Plugin plugin = Create(type, entry.File);
			entry.Instance = plugin;
			entry.LoadedFrom = loadedFrom;
			entry.LoadedWriteTime = entry.WriteTime;
			PluginLog.Info("Loading " + plugin.Name);
			plugin.Load();
			plugin.IsInited = true;
			if (plugin is ClientPlugin client)
			{
				client.Enabled = true;
			}
			PluginLog.Info("Loaded " + plugin.Name);
		}
		catch (Exception ex)
		{
			Exception inner = (ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex;
			entry.Error = Describe(inner);
			PluginLog.Error("Could not load " + entry.Key + ": " + entry.Error, inner);
			if (entry.Instance != null)
			{
				CleanUp(entry);
			}
			entry.Instance = null;
		}
		finally
		{
			_loading.Remove(entry);
		}
	}

	public static void Unload(PluginEntry entry)
	{
		if (entry.Instance == null)
		{
			return;
		}
		TerraAngel.Plugin.Plugin plugin = entry.Instance;
		try
		{
			if (plugin.IsInited)
			{
				plugin.Unload();
			}
			PluginLog.Info("Unloaded " + plugin.Name);
		}
		catch (Exception ex)
		{
			PluginLog.Error("Error unloading " + plugin.Name + ": " + ex.Message, ex);
		}
		CleanUp(entry);
	}

	/// <summary>Takes away what the plugin added: its tools, windows, console commands and Harmony patches.</summary>
	private static void CleanUp(PluginEntry entry)
	{
		if (entry.Instance is ClientPlugin client)
		{
			client.Enabled = false;
		}
		entry.Instance.IsInited = false;
		if (entry.Assembly != null)
		{
			ToolManager.RemoveToolsOf(entry.Assembly);
			PluginPatches.RemovePatchesOf(entry.Assembly);
			ClientLoader.MainRenderer.ClientWindows.RemoveAll(w => w.GetType().Assembly == entry.Assembly);
			foreach (string command in ClientLoader.ConsoleWindow.ConsoleCommands.Where(c => c.Value.CommandAction.Method.DeclaringType?.Assembly == entry.Assembly).Select(c => c.Key).ToList())
			{
				ClientLoader.ConsoleWindow.RemoveCommand(command);
			}
		}
		entry.Instance = null;
	}

	private static IEnumerable<Type> PluginTypes(Assembly assembly)
	{
		Type[] types;
		try
		{
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			string missing = ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message;
			throw new InvalidOperationException("a library it needs is missing or was built for another game version (" + missing + ")");
		}
		return types.Where(t => typeof(TerraAngel.Plugin.Plugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
	}

	/// <summary>TerraAngel plugins take the file path in their constructor; IsaClient ones have none (the path is set after).</summary>
	private static TerraAngel.Plugin.Plugin Create(Type type, string path)
	{
		ConstructorInfo withPath = type.GetConstructor(new[] { typeof(string) });
		if (withPath != null)
		{
			return (TerraAngel.Plugin.Plugin)withPath.Invoke(new object[] { path });
		}
		ConstructorInfo empty = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (empty == null)
		{
			throw new InvalidOperationException(type.Name + " needs a constructor with no arguments or one that takes the plugin path (string)");
		}
		TerraAngel.Plugin.Plugin plugin = (TerraAngel.Plugin.Plugin)empty.Invoke(null);
		typeof(TerraAngel.Plugin.Plugin).GetField(nameof(TerraAngel.Plugin.Plugin.PluginPath)).SetValue(plugin, path);
		return plugin;
	}

	private static string Describe(Exception ex)
	{
		if (ex is BadImageFormatException)
		{
			return "not a .NET library this client can load";
		}
		if (ex is FileNotFoundException file)
		{
			return "missing library " + (file.FileName ?? file.Message);
		}
		if (ex is TypeLoadException || ex is MissingMethodException || ex is MissingFieldException)
		{
			return "uses something this client does not have (" + ex.Message + ")";
		}
		return ex.Message;
	}

	/// <summary>
	/// What plugins reference besides the client: another plugin (the TerraAngel pack, say), which is turned on first if it
	/// is off, or one of their own extra libraries in the Plugins folder.
	/// </summary>
	private static void AddResolver()
	{
		if (_resolverAdded)
		{
			return;
		}
		_resolverAdded = true;
		DevToolsInstaller.AddResolver();
		Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
		{
			string name = new AssemblyName(args.Name).Name;
			Assembly plugin = PluginAssembly(name);
			if (plugin != null)
			{
				return plugin;
			}
			lock (loaded)
			{
				if (loaded.TryGetValue(name, out Assembly cached))
				{
					return cached;
				}
				foreach (string folder in new[] { PluginsFolder, Path.Combine(PluginsFolder, "Libraries") })
				{
					string path = Path.Combine(folder, name + ".dll");
					if (File.Exists(path))
					{
						Assembly assembly = Assembly.Load(File.ReadAllBytes(path));
						loaded[name] = assembly;
						return assembly;
					}
				}
			}
			return null;
		};
	}

	/// <summary>The assembly of the plugin called <paramref name="name"/>, loading (and turning on) that plugin if needed.</summary>
	private static Assembly PluginAssembly(string name)
	{
		PluginEntry fork = EnabledFork;
		if (fork != null && string.Equals(name, ForkBuilder.AssemblyName, StringComparison.OrdinalIgnoreCase))
		{
			// A fork is the TerraAngel library now (null while it builds: what needs it loads again after).
			return fork.Loaded ? fork.Assembly : null;
		}
		PluginEntry entry = _entries.FirstOrDefault(e => e.Loaded && e.Assembly != null && string.Equals(e.Assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
			?? _entries.FirstOrDefault(e => e.Assembly != null && string.Equals(e.Assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
			?? _entries.FirstOrDefault(e => e.Kind == PluginKind.Library && string.Equals(Path.GetFileNameWithoutExtension(e.File), name, StringComparison.OrdinalIgnoreCase));
		if (entry == null)
		{
			return null;
		}
		if (!entry.Loaded && entry.Error == null && !_loading.Contains(entry) && entry.Kind == PluginKind.Library)
		{
			PluginLog.Info("Turning on " + entry.Key + ": another plugin needs it");
			SetEnabled(entry, true);
		}
		return entry.Assembly;
	}

	private static readonly HashSet<PluginEntry> _loading = new HashSet<PluginEntry>();

	/// <summary>Every loaded plugin's Update; one that throws is reported once and keeps its place.</summary>
	public static void Update()
	{
		LoadBuilt();
		for (int i = 0; i < _entries.Count; i++)
		{
			PluginEntry entry = _entries[i];
			if (!entry.Loaded)
			{
				continue;
			}
			try
			{
				entry.Instance.Update();
			}
			catch (Exception ex)
			{
				PluginLog.ErrorOnce(entry.Name + " (update): " + ex.Message, ex);
			}
		}
	}

	public static void UnloadAll()
	{
		foreach (PluginEntry entry in _entries)
		{
			Unload(entry);
		}
	}

	public static IEnumerable<ClientPlugin> LoadedClientPlugins()
	{
		foreach (PluginEntry entry in _entries)
		{
			if (entry.Loaded && entry.Instance is ClientPlugin client)
			{
				yield return client;
			}
		}
	}
}
