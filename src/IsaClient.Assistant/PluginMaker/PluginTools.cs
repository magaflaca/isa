using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using IsaClient.Plugins;
using IsaClient.Plugins.DevTools;
using Newtonsoft.Json.Linq;
using Terraria;

namespace IsaClient.Assistant.PluginMaker;

/// <summary>
/// What the Plugin Maker can do: read and write the C# of plugins in Plugins/Sources (nowhere else), build and load
/// them, look the game's API up (reflection, and the decompiler for method bodies) and read the plugin log. Runs on the
/// agent's thread; whatever touches the game goes to the game thread and is waited for.
/// </summary>
internal static class PluginTools
{
	/// <summary>A tool step for the transcript: what it did, in a few words.</summary>
	public sealed class Step
	{
		public string Title;

		public string Detail;

		public bool Failed;

		/// <summary>The plugin it built or changed (for the transcript's buttons).</summary>
		public string Plugin;
	}

	private const int MaxRead = 24000;

	public static JArray Schemas()
	{
		return new JArray
		{
			Tool("list_plugins", "Lists every plugin: source plugins you can edit (Plugins/Sources/<name>) and libraries you can't, with whether each is on, loaded or failing.", new JObject()),
			Tool("list_files", "Lists the files of a source plugin with their sizes.", Props(("plugin", "string", "The plugin's folder name."))),
			Tool("read_file", "Reads a file of a source plugin, with line numbers. Long files: ask for a range of lines.", Props(("plugin", "string", "The plugin's folder name."), ("path", "string", "File path inside the plugin folder, e.g. MyPlugin.cs or UI/Window.cs."), ("from_line", "integer", "First line (optional)."), ("to_line", "integer", "Last line (optional).")), "plugin", "path"),
			Tool("write_file", "Creates or replaces a whole file of a source plugin (the folder is created for a new plugin). Use it for new files or full rewrites.", Props(("plugin", "string", "The plugin's folder name (letters, digits, - and _)."), ("path", "string", "File path inside the plugin folder, ending in .cs (or .md/.json/.txt)."), ("content", "string", "The whole file.")), "plugin", "path", "content"),
			Tool("edit_file", "Replaces an exact piece of a file with new text. old_text must appear exactly once (copy it from read_file without the line numbers, with enough lines around to be unique) unless replace_all is true.", Props(("plugin", "string", "The plugin's folder name."), ("path", "string", "File path inside the plugin folder."), ("old_text", "string", "The exact text to replace."), ("new_text", "string", "What goes in its place."), ("replace_all", "boolean", "Replace every occurrence.")), "plugin", "path", "old_text", "new_text"),
			Tool("delete_file", "Deletes a file of a source plugin.", Props(("plugin", "string", "The plugin's folder name."), ("path", "string", "File path inside the plugin folder.")), "plugin", "path"),
			Tool("build_plugin", "Compiles a source plugin and, when it compiles, loads it into the running game (reloading the old copy). Returns the compiler errors, or what happened when it loaded (errors it logged). Always build after changing code and fix every error.", Props(("plugin", "string", "The plugin's folder name.")), "plugin"),
			Tool("set_plugin_enabled", "Turns a plugin on (loads it) or off (unloads it).", Props(("plugin", "string", "The plugin's name."), ("enabled", "boolean", "On or off.")), "plugin", "enabled"),
			Tool("find_api", "Searches the game and the plugin APIs (Terraria, IsaClient, TerraAngel, ImGui.NET, Harmony, XNA) for types and members whose name contains the words. Use it instead of guessing names.", Props(("query", "string", "Words of a name, e.g. 'statLife', 'Player Hurt', 'NPC.StrikeNPC', 'ItemID Zenith', 'ImGui Slider'.")), "query"),
			Tool("describe_type", "Lists the members of a type (fields, properties, methods with their parameters). Non-public ones are marked; reach them with Harmony's AccessTools or reflection.", Props(("type", "string", "Full or short type name, e.g. Terraria.Player, NPC, ImGuiNET.ImGui."), ("filter", "string", "Only members whose name contains this (optional).")), "type"),
			Tool("decompile", "Shows the C# of a game method (or all overloads of it) as the game has it: to understand what it does before patching it or calling it.", Props(("type", "string", "Type, e.g. Terraria.Player."), ("member", "string", "Method/property name, e.g. Hurt.")), "type", "member"),
			Tool("read_log", "The plugin log's latest lines: load messages and the exceptions plugins threw while running.", Props(("lines", "integer", "How many lines (default 40).")))
		};
	}

	private static JObject Tool(string name, string description, JObject properties, params string[] required)
	{
		return new JObject
		{
			["type"] = "function",
			["function"] = new JObject
			{
				["name"] = name,
				["description"] = description,
				["parameters"] = new JObject
				{
					["type"] = "object",
					["properties"] = properties,
					["required"] = new JArray(required)
				}
			}
		};
	}

	private static JObject Props(params (string name, string type, string description)[] props)
	{
		JObject result = new JObject();
		foreach ((string name, string type, string description) in props)
		{
			result[name] = new JObject { ["type"] = type, ["description"] = description };
		}
		return result;
	}

	/// <summary>Runs a tool; the text goes back to the model, <paramref name="step"/> to the transcript.</summary>
	public static string Run(string name, JObject args, Func<bool> cancelled, out Step step)
	{
		step = new Step { Title = name };
		try
		{
			switch (name)
			{
			case "list_plugins":
				step.Title = "Looked at the plugins";
				return ListPlugins();
			case "list_files":
				return ListFiles(Str(args, "plugin"), step);
			case "read_file":
				return ReadFile(Str(args, "plugin"), Str(args, "path"), Int(args, "from_line"), Int(args, "to_line"), step);
			case "write_file":
				return WriteFile(Str(args, "plugin"), Str(args, "path"), Str(args, "content"), step);
			case "edit_file":
				return EditFile(Str(args, "plugin"), Str(args, "path"), Str(args, "old_text"), Str(args, "new_text"), (bool?)args["replace_all"] ?? false, step);
			case "delete_file":
				return DeleteFile(Str(args, "plugin"), Str(args, "path"), step);
			case "build_plugin":
				return Build(Str(args, "plugin"), cancelled, step);
			case "set_plugin_enabled":
				return SetEnabled(Str(args, "plugin"), (bool?)args["enabled"] ?? true, step);
			case "find_api":
				step.Title = "Searched the API: " + Str(args, "query");
				return ApiSearch.Find(Str(args, "query"));
			case "describe_type":
				step.Title = "Read the members of " + Str(args, "type");
				return ApiSearch.Describe(Str(args, "type"), Str(args, "filter"));
			case "decompile":
				step.Title = "Read the game's code of " + Str(args, "type") + "." + Str(args, "member");
				return Decompile(Str(args, "type"), Str(args, "member"), cancelled);
			case "read_log":
				step.Title = "Read the plugin log";
				return ReadLog(Int(args, "lines") ?? 40);
			default:
				step.Failed = true;
				return "Unknown tool " + name + ".";
			}
		}
		catch (ToolError ex)
		{
			step.Failed = true;
			step.Detail = ex.Message;
			return "Error: " + ex.Message;
		}
		catch (Exception ex)
		{
			step.Failed = true;
			step.Detail = ex.Message;
			return "Error: " + ex.GetType().Name + ": " + ex.Message;
		}
	}

	private sealed class ToolError : Exception
	{
		public ToolError(string message)
			: base(message)
		{
		}
	}

	private static string Str(JObject args, string name)
	{
		return (string)args?[name] ?? "";
	}

	private static int? Int(JObject args, string name)
	{
		JToken token = args?[name];
		if (token == null || token.Type == JTokenType.Null)
		{
			return null;
		}
		return int.TryParse(token.ToString(), out int value) ? value : (int?)null;
	}

	#region Files

	private static readonly Regex _pluginName = new Regex("^[A-Za-z0-9][A-Za-z0-9_.\\- ]{0,60}$");

	private static string PluginFolder(string plugin, bool mustExist)
	{
		plugin = (plugin ?? "").Trim();
		if (!_pluginName.IsMatch(plugin) || plugin.Contains(".."))
		{
			throw new ToolError("'" + plugin + "' is not a valid plugin folder name (letters, digits, - and _).");
		}
		string folder = Path.Combine(PluginLoader.SourcesFolder, plugin);
		if (mustExist && !Directory.Exists(folder))
		{
			string[] existing = Directory.Exists(PluginLoader.SourcesFolder) ? Directory.GetDirectories(PluginLoader.SourcesFolder).Select(Path.GetFileName).ToArray() : new string[0];
			throw new ToolError("There is no source plugin '" + plugin + "'. Source plugins: " + (existing.Length > 0 ? string.Join(", ", existing) : "none") + ".");
		}
		return folder;
	}

	/// <summary>A file inside the plugin's folder (never outside it).</summary>
	private static string FileOf(string plugin, string path, bool folderMustExist)
	{
		string folder = PluginFolder(plugin, folderMustExist);
		path = (path ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		if (path.Length == 0)
		{
			throw new ToolError("No file path given.");
		}
		string full = Path.GetFullPath(Path.Combine(folder, path));
		if (!full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new ToolError("The path must stay inside the plugin's folder.");
		}
		string ext = Path.GetExtension(full).ToLowerInvariant();
		if (ext != ".cs" && ext != ".md" && ext != ".json" && ext != ".txt")
		{
			throw new ToolError("Only .cs, .md, .json and .txt files.");
		}
		return full;
	}

	private static string Relative(string plugin, string full)
	{
		return full.Substring(PluginFolder(plugin, false).Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
	}

	private static string ListPlugins()
	{
		string text = OnGame(delegate
		{
			PluginLoader.Scan();
			StringBuilder b = new StringBuilder();
			foreach (PluginEntry e in PluginLoader.Entries)
			{
				b.Append("- ").Append(e.Key);
				b.Append(e.Kind == PluginKind.Source ? "  [source, editable: Plugins/Sources/" + Path.GetFileName(e.File.TrimEnd('\\')) + "]" : ("  [" + e.Kind.ToString().ToLowerInvariant() + ", not editable]"));
				b.Append(PluginLoader.IsEnabled(e) ? " on" : " off");
				if (e.Loaded)
				{
					b.Append(", loaded as \"").Append(e.Name).Append('"');
				}
				if (e.Building)
				{
					b.Append(", building");
				}
				if (e.Error != null)
				{
					b.Append(", error: ").Append(e.Error);
				}
				if (e.BuildErrors.Count > 0)
				{
					b.Append(", ").Append(e.BuildErrors.Count).Append(" build errors");
				}
				b.AppendLine();
			}
			return (b.Length == 0) ? "No plugins yet." : b.ToString();
		});
		return text;
	}

	private static string ListFiles(string plugin, Step step)
	{
		string folder = PluginFolder(plugin, true);
		step.Title = "Looked at the files of " + plugin;
		step.Plugin = plugin;
		StringBuilder b = new StringBuilder();
		foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			int lines = 0;
			try
			{
				lines = File.ReadAllLines(file).Length;
			}
			catch
			{
			}
			b.Append(Relative(plugin, file)).Append("  (").Append(lines).AppendLine(" lines)");
		}
		return (b.Length == 0) ? "The folder is empty." : b.ToString();
	}

	private static string ReadFile(string plugin, string path, int? from, int? to, Step step)
	{
		string file = FileOf(plugin, path, true);
		step.Plugin = plugin;
		if (!File.Exists(file))
		{
			throw new ToolError(path + " doesn't exist in " + plugin + ". Files: " + string.Join(", ", Directory.GetFiles(PluginFolder(plugin, true), "*.cs", SearchOption.AllDirectories).Select(f => Relative(plugin, f))));
		}
		string[] lines = File.ReadAllLines(file);
		int first = Math.Max(1, from ?? 1);
		int last = Math.Min(lines.Length, to ?? lines.Length);
		step.Title = "Read " + Relative(plugin, file) + ((from.HasValue || to.HasValue) ? (" (lines " + first + "-" + last + ")") : "");
		StringBuilder b = new StringBuilder();
		for (int i = first; i <= last; i++)
		{
			string line = i.ToString().PadLeft(4) + "  " + lines[i - 1];
			if (b.Length + line.Length > MaxRead)
			{
				b.AppendLine("... (cut at line " + (i - 1) + " of " + lines.Length + ": ask for from_line " + i + ")");
				break;
			}
			b.AppendLine(line);
		}
		return (lines.Length == 0) ? "(empty file)" : b.ToString();
	}

	private static string WriteFile(string plugin, string path, string content, Step step)
	{
		string file = FileOf(plugin, path, false);
		bool existed = File.Exists(file);
		int before = existed ? File.ReadAllLines(file).Length : 0;
		PluginAgent.Checkpoint(file);
		Directory.CreateDirectory(Path.GetDirectoryName(file));
		content = (content ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
		File.WriteAllText(file, content, new UTF8Encoding(false));
		int after = content.Split('\n').Length;
		step.Plugin = plugin;
		step.Title = (existed ? "Rewrote " : "Created ") + plugin + "/" + Relative(plugin, file);
		step.Detail = existed ? (before + " -> " + after + " lines") : (after + " lines");
		return (existed ? "Replaced " : "Created ") + Relative(plugin, file) + " (" + after + " lines).";
	}

	private static string EditFile(string plugin, string path, string oldText, string newText, bool all, Step step)
	{
		string file = FileOf(plugin, path, true);
		step.Plugin = plugin;
		if (!File.Exists(file))
		{
			throw new ToolError(path + " doesn't exist in " + plugin + ".");
		}
		string text = File.ReadAllText(file);
		bool crlf = text.Contains("\r\n");
		string norm = text.Replace("\r\n", "\n");
		string find = (oldText ?? "").Replace("\r\n", "\n");
		string put = (newText ?? "").Replace("\r\n", "\n");
		if (find.Length == 0)
		{
			throw new ToolError("old_text is empty.");
		}
		int count = Count(norm, find);
		if (count == 0)
		{
			// Models often get the indentation wrong: try again ignoring the whitespace at line starts.
			string loose = Loose(norm, find, out int at, out int length);
			if (loose == null)
			{
				throw new ToolError("old_text was not found in " + path + ". Read the file again and copy the text exactly (without the line numbers).");
			}
			norm = norm.Substring(0, at) + put + norm.Substring(at + length);
			count = 1;
		}
		else if (count > 1 && !all)
		{
			throw new ToolError("old_text appears " + count + " times in " + path + ": add more lines around it so it is unique, or set replace_all.");
		}
		else
		{
			norm = all ? norm.Replace(find, put) : ReplaceFirst(norm, find, put);
		}
		PluginAgent.Checkpoint(file);
		File.WriteAllText(file, crlf ? norm.Replace("\n", "\r\n") : norm, new UTF8Encoding(false));
		int removed = find.Split('\n').Length;
		int added = put.Split('\n').Length;
		step.Title = "Edited " + plugin + "/" + Relative(plugin, file);
		step.Detail = "+" + added + " -" + removed + ((count > 1) ? (" (x" + count + ")") : "");
		return "Edited " + Relative(plugin, file) + ((count > 1) ? (" (" + count + " places)") : "") + ".";
	}

	private static int Count(string text, string find)
	{
		int count = 0;
		for (int i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal))
		{
			count++;
		}
		return count;
	}

	private static string ReplaceFirst(string text, string find, string put)
	{
		int i = text.IndexOf(find, StringComparison.Ordinal);
		return text.Substring(0, i) + put + text.Substring(i + find.Length);
	}

	/// <summary>Finds <paramref name="find"/> line by line ignoring leading/trailing spaces; returns null unless exactly once.</summary>
	private static string Loose(string text, string find, out int at, out int length)
	{
		at = length = 0;
		string[] lines = text.Split('\n');
		string[] want = find.Trim('\n').Split('\n').Select(l => l.Trim()).ToArray();
		if (want.Length == 0)
		{
			return null;
		}
		int found = -1;
		for (int i = 0; i + want.Length <= lines.Length; i++)
		{
			bool same = true;
			for (int j = 0; j < want.Length && same; j++)
			{
				same = lines[i + j].Trim() == want[j];
			}
			if (same)
			{
				if (found >= 0)
				{
					return null;
				}
				found = i;
			}
		}
		if (found < 0)
		{
			return null;
		}
		for (int i = 0; i < found; i++)
		{
			at += lines[i].Length + 1;
		}
		for (int j = 0; j < want.Length; j++)
		{
			length += lines[found + j].Length + ((j < want.Length - 1) ? 1 : 0);
		}
		return text.Substring(at, length);
	}

	private static string DeleteFile(string plugin, string path, Step step)
	{
		string file = FileOf(plugin, path, true);
		step.Plugin = plugin;
		if (!File.Exists(file))
		{
			throw new ToolError(path + " doesn't exist.");
		}
		PluginAgent.Checkpoint(file);
		File.Delete(file);
		step.Title = "Deleted " + plugin + "/" + Relative(plugin, file);
		return "Deleted.";
	}

	#endregion

	#region Building and loading

	private static string Build(string plugin, Func<bool> cancelled, Step step)
	{
		string folder = PluginFolder(plugin, true);
		step.Plugin = plugin;
		step.Title = "Built " + plugin;
		if (!WaitDevTools(cancelled))
		{
			throw new ToolError("The developer tools (C# compiler) could not be installed; see the plugin console.");
		}
		int logFrom = PluginLog.Count;
		PluginEntry entry = OnGame(delegate
		{
			PluginLoader.Scan();
			PluginEntry e = PluginLoader.Entries.FirstOrDefault(x => x.Kind == PluginKind.Source && string.Equals(x.File.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
			if (e == null)
			{
				return null;
			}
			if (PluginLoader.IsEnabled(e))
			{
				PluginLoader.Reload(e);
			}
			else if (PluginAiSettings.AutoEnable)
			{
				PluginLoader.SetEnabled(e, true);
			}
			else
			{
				PluginLoader.Load(e);
			}
			return e;
		});
		if (entry == null)
		{
			throw new ToolError(plugin + " has no .cs files to build.");
		}
		// The build runs in the background; the game loads it on its next frame.
		DateTime until = DateTime.UtcNow.AddMinutes(3);
		Thread.Sleep(300);
		while (DateTime.UtcNow < until && !(cancelled?.Invoke() ?? false))
		{
			bool done = OnGame(() => !entry.Building && (entry.Loaded || entry.Error != null || entry.BuildErrors.Count > 0));
			if (done)
			{
				break;
			}
			Thread.Sleep(250);
		}
		// A moment for Load() and the first frames to log what they throw.
		Thread.Sleep(600);
		StringBuilder b = new StringBuilder();
		string errorsFile = Path.Combine(PluginLoader.CompiledFolder, entry.Key + ".errors.txt");
		string errors = File.Exists(errorsFile) ? File.ReadAllText(errorsFile).Trim() : "";
		List<string> log = PluginLog.Since(logFrom);
		if (errors.Length > 0)
		{
			string[] list = errors.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
			step.Failed = true;
			step.Detail = list.Length + " error" + ((list.Length == 1) ? "" : "s");
			b.AppendLine("It does not compile (" + list.Length + " errors):");
			foreach (string line in list.Take(30))
			{
				b.AppendLine(ShortPath(line));
			}
			if (list.Length > 30)
			{
				b.AppendLine("... and " + (list.Length - 30) + " more.");
			}
			return b.ToString();
		}
		bool loaded = OnGame(() => entry.Loaded);
		string name = OnGame(() => entry.Name);
		string error = OnGame(() => entry.Error);
		if (error != null)
		{
			step.Failed = true;
			step.Detail = "compiled, failed to load";
			b.AppendLine("It compiled but did not load: " + error);
		}
		else if (loaded)
		{
			step.Detail = "loaded as \"" + name + "\"";
			b.AppendLine("Compiled with no errors and loaded into the game as \"" + name + "\".");
		}
		else
		{
			step.Detail = "compiled";
			b.AppendLine("Compiled with no errors" + (PluginAiSettings.AutoEnable ? " (it did not load yet: is there a class deriving from ClientPlugin or TerraAngel.Plugin.Plugin?)." : " (it is off; turn it on with set_plugin_enabled)."));
		}
		List<string> problems = log.Where(l => l.Contains("ERROR") || l.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		if (problems.Count > 0)
		{
			step.Failed = true;
			step.Detail += ", logged errors";
			b.AppendLine("The plugin log has errors since the build:");
			foreach (string line in problems.Take(12))
			{
				b.AppendLine(Cut(line, 600));
			}
		}
		return b.ToString();
	}

	private static string ShortPath(string line)
	{
		int sources = line.IndexOf("Sources" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		return (sources >= 0) ? line.Substring(sources + 8) : line;
	}

	private static string SetEnabled(string plugin, bool enabled, Step step)
	{
		step.Plugin = plugin;
		step.Title = (enabled ? "Turned on " : "Turned off ") + plugin;
		int logFrom = PluginLog.Count;
		string result = OnGame(delegate
		{
			PluginLoader.Scan();
			PluginEntry e = PluginLoader.Find(plugin);
			if (e == null)
			{
				return "There is no plugin '" + plugin + "'.";
			}
			PluginLoader.SetEnabled(e, enabled);
			return null;
		});
		if (result != null)
		{
			throw new ToolError(result);
		}
		Thread.Sleep(1500);
		List<string> log = PluginLog.Since(logFrom);
		return "Done." + ((log.Count > 0) ? ("\nLog:\n" + string.Join("\n", log.Take(15).Select(l => Cut(l, 400)))) : "");
	}

	private static string ReadLog(int lines)
	{
		List<string> all = PluginLog.Since(0);
		return (all.Count == 0) ? "The log is empty." : string.Join("\n", all.Skip(Math.Max(0, all.Count - Math.Max(1, Math.Min(150, lines)))).Select(l => Cut(l, 800)));
	}

	private static bool WaitDevTools(Func<bool> cancelled)
	{
		if (DevToolsInstaller.Installed)
		{
			DevToolsInstaller.AddResolver();
			return true;
		}
		ManualResetEventSlim ready = new ManualResetEventSlim();
		Main.QueueMainThreadAction(() => DevToolsInstaller.Ensure("the Plugin Maker", ready.Set));
		DateTime until = DateTime.UtcNow.AddMinutes(5);
		while (!ready.Wait(500))
		{
			if (DateTime.UtcNow > until || (cancelled?.Invoke() ?? false) || (!DevToolsInstaller.Installing && !DevToolsInstaller.Installed && DateTime.UtcNow > until.AddMinutes(-4.5)))
			{
				return DevToolsInstaller.Installed;
			}
		}
		return true;
	}

	private static string Decompile(string type, string member, Func<bool> cancelled)
	{
		if (!WaitDevTools(cancelled))
		{
			throw new ToolError("The decompiler isn't installed.");
		}
		return GameDecompiler.Member(type, member);
	}

	#endregion

	/// <summary>Runs <paramref name="work"/> on the game thread and waits for it.</summary>
	public static T OnGame<T>(Func<T> work)
	{
		T result = default(T);
		Exception error = null;
		using ManualResetEventSlim done = new ManualResetEventSlim();
		Main.QueueMainThreadAction(delegate
		{
			try
			{
				result = work();
			}
			catch (Exception ex)
			{
				error = ex;
			}
			finally
			{
				done.Set();
			}
		});
		if (!done.Wait(60000))
		{
			throw new ToolError("The game didn't answer (is it paused or loading?).");
		}
		if (error != null)
		{
			throw error;
		}
		return result;
	}

	public static string Cut(string text, int max)
	{
		return (text == null || text.Length <= max) ? text : (text.Substring(0, max) + "...");
	}
}

/// <summary>Looks the APIs a plugin uses up by reflection: the game, IsaClient, TerraAngel's API, ImGui.NET, Harmony, XNA.</summary>
internal static class ApiSearch
{
	private static List<Type> _types;

	private static List<Type> Types
	{
		get
		{
			if (_types == null)
			{
				HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ImGui.NET", "0Harmony", "Microsoft.Xna.Framework", "Microsoft.Xna.Framework.Game", "Microsoft.Xna.Framework.Graphics", "ReLogic" };
				List<Assembly> assemblies = new List<Assembly> { typeof(Main).Assembly };
				assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(a => wanted.Contains(a.GetName().Name) || a.GetName().Name.StartsWith("TerraAngel", StringComparison.OrdinalIgnoreCase)));
				_types = assemblies.Distinct().SelectMany(Loadable).Where(t => !t.Name.Contains("<") && !t.Name.StartsWith("__")).ToList();
			}
			return _types;
		}
	}

	private static IEnumerable<Type> Loadable(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(t => t != null);
		}
		catch
		{
			return new Type[0];
		}
	}

	private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	public static string Find(string query)
	{
		string[] words = (query ?? "").Split(new[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0)
		{
			return "Give some words of a name.";
		}
		List<string> hits = new List<string>();
		// "Player Hurt": a type matching the first words, members matching the last one.
		foreach (Type type in Types)
		{
			bool typeMatch = words.All(w => type.FullName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
			if (typeMatch)
			{
				hits.Add("type " + type.FullName + Kind(type));
			}
			if (hits.Count > 60)
			{
				break;
			}
		}
		string last = words[words.Length - 1];
		string[] typeWords = words.Take(words.Length - 1).ToArray();
		foreach (Type type in Types)
		{
			if (typeWords.Length > 0 && !typeWords.All(w => type.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || type.FullName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
			{
				continue;
			}
			MemberInfo[] members;
			try
			{
				members = type.GetMembers(All);
			}
			catch
			{
				continue;
			}
			foreach (MemberInfo m in members)
			{
				if (m is MethodInfo mi && mi.IsSpecialName)
				{
					continue;
				}
				if (m.Name.IndexOf(last, StringComparison.OrdinalIgnoreCase) >= 0 && !(m is Type))
				{
					hits.Add(Signature(m));
					if (hits.Count >= 80)
					{
						break;
					}
				}
			}
			if (hits.Count >= 80)
			{
				break;
			}
		}
		if (hits.Count == 0)
		{
			return "Nothing found for '" + query + "'. Try fewer or other words.";
		}
		return string.Join("\n", hits.Distinct().Take(80)) + ((hits.Count >= 80) ? "\n(more results: be more precise, e.g. 'Player statLife')" : "");
	}

	private static string Kind(Type t)
	{
		return t.IsEnum ? " (enum)" : (t.IsInterface ? " (interface)" : (t.IsValueType ? " (struct)" : ((t.IsAbstract && t.IsSealed) ? " (static class)" : "")));
	}

	public static Type FindType(string name)
	{
		name = (name ?? "").Trim();
		return Types.FirstOrDefault(t => t.FullName == name)
			?? Types.FirstOrDefault(t => t.Name == name && t.Namespace == "Terraria")
			?? Types.FirstOrDefault(t => t.Name == name)
			?? Types.FirstOrDefault(t => t.FullName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
			?? Types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	public static string Describe(string typeName, string filter)
	{
		Type type = FindType(typeName);
		if (type == null)
		{
			return "No type '" + typeName + "'. Use find_api to search.";
		}
		StringBuilder b = new StringBuilder();
		b.Append(type.IsEnum ? "enum " : (type.IsInterface ? "interface " : (type.IsValueType ? "struct " : "class "))).Append(type.FullName);
		if (type.BaseType != null && type.BaseType != typeof(object) && !type.IsEnum && !type.IsValueType)
		{
			b.Append(" : ").Append(Name(type.BaseType));
		}
		b.AppendLine();
		if (type.IsEnum)
		{
			string[] names = Enum.GetNames(type);
			foreach (string n in names.Where(n => string.IsNullOrEmpty(filter) || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).Take(300))
			{
				b.AppendLine("  " + n + " = " + Convert.ToInt64(Enum.Parse(type, n)));
			}
			return PluginTools.Cut(b.ToString(), 14000);
		}
		IEnumerable<MemberInfo> members = type.GetMembers(All).Where(m => !(m is MethodInfo mi && mi.IsSpecialName) && !m.Name.Contains("<"));
		if (!string.IsNullOrEmpty(filter))
		{
			members = members.Where(m => m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
		}
		List<MemberInfo> list = members.OrderBy(m => m.MemberType).ThenBy(m => m.Name).ToList();
		foreach (MemberInfo m in list.Take(400))
		{
			b.AppendLine("  " + Signature(m, withType: false));
		}
		if (list.Count > 400)
		{
			b.AppendLine("  ... " + (list.Count - 400) + " more: use filter.");
		}
		if (list.Count == 0)
		{
			b.AppendLine("  (no members" + (string.IsNullOrEmpty(filter) ? "" : (" matching '" + filter + "'")) + ")");
		}
		return PluginTools.Cut(b.ToString(), 16000);
	}

	private static string Signature(MemberInfo m, bool withType = true)
	{
		string owner = withType ? (m.DeclaringType?.FullName + ".") : "";
		switch (m)
		{
		case FieldInfo f:
			return (f.IsPublic ? "" : "[non-public] ") + (f.IsStatic ? "static " : "") + (f.IsLiteral ? "const " : "") + Name(f.FieldType) + " " + owner + f.Name;
		case PropertyInfo p:
		{
			MethodInfo get = p.GetGetMethod(true);
			bool isPublic = (get ?? p.GetSetMethod(true))?.IsPublic ?? false;
			bool isStatic = (get ?? p.GetSetMethod(true))?.IsStatic ?? false;
			return (isPublic ? "" : "[non-public] ") + (isStatic ? "static " : "") + Name(p.PropertyType) + " " + owner + p.Name + " { " + (p.CanRead ? "get; " : "") + (p.CanWrite ? "set; " : "") + "}";
		}
		case MethodInfo mi:
			return (mi.IsPublic ? "" : "[non-public] ") + (mi.IsStatic ? "static " : "") + (mi.IsVirtual && !mi.IsFinal ? "virtual " : "") + Name(mi.ReturnType) + " " + owner + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(Param)) + ")";
		case ConstructorInfo c:
			return (c.IsPublic ? "" : "[non-public] ") + "new " + Name(c.DeclaringType) + "(" + string.Join(", ", c.GetParameters().Select(Param)) + ")";
		case EventInfo e:
			return "event " + Name(e.EventHandlerType) + " " + owner + e.Name;
		default:
			return m.MemberType + " " + owner + m.Name;
		}
	}

	private static string Param(ParameterInfo p)
	{
		string prefix = p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : "");
		Type type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
		return prefix + Name(type) + " " + p.Name + (p.HasDefaultValue ? (" = " + (p.DefaultValue ?? "null")) : "");
	}

	private static string Name(Type t)
	{
		if (t == null)
		{
			return "?";
		}
		if (t.IsArray)
		{
			return Name(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
		}
		if (t.IsGenericType)
		{
			string n = t.Name;
			int tick = n.IndexOf('`');
			return ((tick > 0) ? n.Substring(0, tick) : n) + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
		}
		switch (t.FullName)
		{
		case "System.Int32":
			return "int";
		case "System.Single":
			return "float";
		case "System.Boolean":
			return "bool";
		case "System.String":
			return "string";
		case "System.Void":
			return "void";
		case "System.Double":
			return "double";
		case "System.Byte":
			return "byte";
		case "System.Int16":
			return "short";
		case "System.Int64":
			return "long";
		case "System.UInt32":
			return "uint";
		case "System.Object":
			return "object";
		}
		return t.Name;
	}
}
