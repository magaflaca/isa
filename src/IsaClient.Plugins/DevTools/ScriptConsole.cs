using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using TerraAngel;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// The C# console (TerraAngel's REPL): a line that is not a console command runs as C# against the game, like
/// <c>Main.LocalPlayer.statLife = 500</c> or <c>Main.npc.Count(n =&gt; n.active)</c>. Variables stay between lines.
/// Compiling happens off the game thread; running happens on it (between frames).
/// </summary>
public static class ScriptConsole
{
	// object, not the Roslyn types: this class must load (settings, startup) before Roslyn is downloaded.
	private static object _state;

	private static object _options;

	// The script loader in use (an InteractiveAssemblyLoader): disposing it removes its assembly resolver.
	private static IDisposable _loader;

	private static bool _busy;

	/// <summary>The plugin assemblies the options and variables were made with: a reloaded plugin is a new assembly.</summary>
	private static string _plugins;

	/// <summary>Set by .pc: the next line's result and errors also go to the chat.</summary>
	internal static bool EchoNextToChat;

	private static readonly string[] Imports =
	{
		"System", "System.Collections.Generic", "System.Linq", "System.Text", "Microsoft.Xna.Framework", "Terraria", "Terraria.ID",
		"Terraria.DataStructures", "ImGuiNET", "TerraAngel", "TerraAngel.Tools", "IsaClient.Plugins", "IsaClient.Core"
	};

	public static void Run(string code)
	{
		if (_busy)
		{
			ClientLoader.Console.WriteError("The previous line is still running.");
			return;
		}
		bool echo = EchoNextToChat;
		EchoNextToChat = false;
		DevToolsInstaller.Ensure("the C# console", () => Start(code, echo));
	}

	private static void Write(string text, Microsoft.Xna.Framework.Color color, bool echo)
	{
		ClientLoader.Console.WriteLine(text, color);
		if (echo)
		{
			Terraria.Main.NewText(IsaClient.Languages.L10n.T(text), color.R, color.G, color.B);
		}
	}

	private static void WriteError(string text, bool echo) => Write(text, new Microsoft.Xna.Framework.Color(255, 110, 110), echo);

	private static string LoadedPlugins()
	{
		return string.Join("|", PluginLoader.Entries.Where(e => e.Loaded && e.Assembly != null).Select(e => e.Assembly.FullName + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e.Assembly)));
	}

	public static void Reset()
	{
		DropLoader();
		_state = null;
		_options = null;
		ClientLoader.Console.WriteLine("C# console variables cleared.");
	}

	private static void Start(string code, bool echo)
	{
		string plugins = LoadedPlugins();
		if (_options != null && plugins != _plugins)
		{
			// Variables made with the old copy of a plugin can't be mixed with the new one.
			bool hadState = _state != null;
			DropLoader();
			_state = null;
			_options = null;
			if (hadState)
			{
				Write("Plugins changed: the C# console variables were cleared.", new Microsoft.Xna.Framework.Color(255, 220, 120), echo);
			}
		}
		_plugins = plugins;
		_busy = true;
		ScriptState<object> state = (ScriptState<object>)_state;
		ScriptOptions options = (ScriptOptions)(_options ??= MakeOptions());
		Task.Run(delegate
		{
			Script<object> script;
			try
			{
				script = (state == null) ? CSharpScript.Create(code, options, assemblyLoader: MakeLoader()) : state.Script.ContinueWith(code, options);
				IReadOnlyList<Diagnostic> errors = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
				if (errors.Count > 0)
				{
					Finish(() => errors.Take(5).ToList().ForEach(e => WriteError(e.GetMessage(), echo)));
					return;
				}
			}
			catch (Exception ex)
			{
				Finish(() => WriteError(ex.Message, echo));
				return;
			}
			// The game is not thread safe: the compiled script runs on the game thread.
			Finish(delegate
			{
				try
				{
					Task<ScriptState<object>> run = (state == null) ? script.RunAsync() : script.RunFromAsync(state);
					ScriptState<object> result = run.GetAwaiter().GetResult();
					_state = result;
					if (result.ReturnValue != null)
					{
						Write(Describe(result.ReturnValue), new Microsoft.Xna.Framework.Color(140, 220, 255), echo);
					}
				}
				catch (Exception ex)
				{
					WriteError((ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message, echo);
				}
			});
		});
	}

	private static void Finish(Action action)
	{
		PluginHost.BeforeNextFrame(delegate
		{
			_busy = false;
			action();
		});
	}

	private static string Describe(object value)
	{
		if (value is string text)
		{
			return "\"" + text + "\"";
		}
		if (value is System.Collections.IEnumerable list)
		{
			List<string> items = list.Cast<object>().Take(20).Select(o => o?.ToString() ?? "null").ToList();
			return "[" + string.Join(", ", items) + (items.Count == 20 ? ", ..." : "") + "]";
		}
		return value.ToString();
	}

	/// <summary>
	/// Scripts use the assemblies the game is running: Roslyn would otherwise load its own copy of a plugin (or of
	/// ImGui.NET) from the reference's bytes, and a line like <c>SomeTool.Enabled = true</c> would change that copy.
	/// </summary>
	private static void DropLoader()
	{
		try
		{
			_loader?.Dispose();
		}
		catch
		{
		}
		_loader = null;
	}

	private static InteractiveAssemblyLoader MakeLoader()
	{
		// One loader for a chain of lines; an old one would keep answering with the assemblies it was given.
		DropLoader();
		InteractiveAssemblyLoader loader = new InteractiveAssemblyLoader();
		_loader = loader;
		HashSet<string> names = new HashSet<string>(CodeCompiler.EmbeddedLibraryNames, StringComparer.OrdinalIgnoreCase);
		IEnumerable<System.Reflection.Assembly> plugins = PluginLoader.Entries.Where(e => e.Loaded && e.Assembly != null).Select(e => e.Assembly);
		IEnumerable<System.Reflection.Assembly> libraries = AppDomain.CurrentDomain.GetAssemblies().Where(a => names.Contains(a.GetName().Name));
		foreach (System.Reflection.Assembly assembly in plugins.Concat(libraries).Concat(new[] { typeof(Terraria.Main).Assembly }).Distinct())
		{
			try
			{
				loader.RegisterDependency(assembly);
			}
			catch
			{
			}
		}
		return loader;
	}

	private static ScriptOptions MakeOptions()
	{
		IEnumerable<string> plugins = PluginLoader.Entries.Where(e => e.Loaded).Select(e => e.LoadedFrom).Where(f => f != null);
		return ScriptOptions.Default
			.WithReferences(CodeCompiler.References(plugins))
			.WithImports(Imports)
			.WithAllowUnsafe(true)
			.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
	}
}
