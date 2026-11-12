using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace IsaClient.Plugins.DevTools;

/// <summary>One compiled assembly, or why it did not compile.</summary>
public sealed class CompileResult
{
	public byte[] Assembly;

	public byte[] Symbols;

	public List<string> Errors = new List<string>();

	public bool Success => Assembly != null;
}

/// <summary>
/// Compiles C# with Roslyn against the running client: the game, IsaClient's plugin API, the libraries the executable
/// carries (ImGui, Harmony, Newtonsoft...) and the plugins that are loaded (the TerraAngel pack, for fork code).
/// Only call it after DevToolsInstaller.Ensure.
/// </summary>
public static class CodeCompiler
{
	internal static IEnumerable<string> EmbeddedLibraryNames => EmbeddedLibraries;

	private static readonly string[] EmbeddedLibraries = { "ImGui.NET", "System.Numerics.Vectors", "System.Runtime.CompilerServices.Unsafe", "System.Buffers", "Newtonsoft.Json", "ReLogic", "0Harmony" };

	private static readonly string[] FrameworkAssemblies =
	{
		"mscorlib", "System", "System.Core", "System.Numerics", "System.Drawing", "System.Windows.Forms", "System.Xml", "System.Xml.Linq",
		"System.Data", "System.Net.Http", "System.IO.Compression", "System.IO.Compression.FileSystem", "System.Runtime.Serialization", "Microsoft.CSharp"
	};

	private static readonly string[] Facades = { "netstandard", "System.Runtime", "System.Collections", "System.Linq", "System.Threading", "System.Threading.Tasks", "System.Runtime.Extensions", "System.Reflection", "System.Text.Encoding", "System.IO" };

	// object, not MetadataReference: this class must load (settings, startup) before Roslyn is downloaded.
	private static List<object> _base;

	private static readonly Regex ImGuiVectorGenerics = new Regex(@"\b(RangeAccessor|ImVector)<\s*(?:global::)?(?:TerraAngel\.Compat\.|System\.Numerics\.)?(Vector[234])\s*>", RegexOptions.Compiled);

	/// <summary>
	/// ImGui's own collections of vectors (style.Colors is a RangeAccessor&lt;System.Numerics.Vector4&gt;...): in
	/// TerraAngel's sources "Vector4" is that type, here it is the bridge vector, and a generic type doesn't convert by
	/// its argument. Their element type is put back to System.Numerics (the elements still convert to and from the
	/// bridge vectors).
	/// </summary>
	public static string FixVectorGenerics(string code)
	{
		return ImGuiVectorGenerics.Replace(code, "$1<System.Numerics.$2>");
	}

	/// <summary>Usings every TerraAngel-style source gets, like TerraAngel's own global usings.</summary>
	public static string TerraAngelUsings(IEnumerable<Assembly> plugins)
	{
		StringBuilder text = new StringBuilder();
		foreach (string ns in new[] { "System", "System.Collections.Generic", "System.Linq", "ImGuiNET", "Microsoft.Xna.Framework", "Microsoft.Xna.Framework.Graphics", "Terraria", "Terraria.ID", "TerraAngel", "TerraAngel.Tools", "TerraAngel.UI", "TerraAngel.Graphics", "TerraAngel.Compat" })
		{
			text.Append("global using ").Append(ns).Append(";\n");
		}
		// The TerraAngel pack's namespaces (its ImGuiUtil, config, tools...) when it is loaded.
		HashSet<string> extra = new HashSet<string>();
		foreach (Assembly plugin in plugins)
		{
			try
			{
				foreach (Type type in plugin.GetTypes())
				{
					if (type.IsPublic && type.Namespace != null && type.Namespace.StartsWith("TerraAngel", StringComparison.Ordinal) && type.Namespace != "TerraAngel.Compat")
					{
						extra.Add(type.Namespace);
					}
				}
			}
			catch
			{
			}
		}
		foreach (string ns in extra.OrderBy(n => n).Where(n => !n.StartsWith("TerraAngel.Plugin", StringComparison.Ordinal)))
		{
			text.Append("global using ").Append(ns).Append(";\n");
		}
		text.Append("global using Vector2 = TerraAngel.Compat.Vector2;\nglobal using Vector3 = TerraAngel.Compat.Vector3;\nglobal using Vector4 = TerraAngel.Compat.Vector4;\n");
		text.Append("global using Color = Microsoft.Xna.Framework.Color;\nglobal using Rectangle = Microsoft.Xna.Framework.Rectangle;\nglobal using Matrix = Microsoft.Xna.Framework.Matrix;\n");
		text.Append("global using NVector2 = System.Numerics.Vector2;\nglobal using NVector3 = System.Numerics.Vector3;\nglobal using NVector4 = System.Numerics.Vector4;\n");
		if (plugins.Any(p => p.GetType("TerraAngel.I18n") != null))
		{
			// TerraAngel's GetString(...) (its translations; the pack returns the text as it is).
			text.Append("global using static TerraAngel.I18n;\n");
		}
		return text.ToString();
	}

	/// <summary>The references every compilation gets, plus the files of the loaded plugins.</summary>
	public static List<MetadataReference> References(IEnumerable<string> pluginFiles)
	{
		if (_base == null)
		{
			List<MetadataReference> list = new List<MetadataReference>();
			foreach (string name in FrameworkAssemblies)
			{
				AddLoaded(list, name);
			}
			foreach (string name in Facades)
			{
				AddLoaded(list, name);
			}
			list.Add(MetadataReference.CreateFromFile(typeof(Terraria.Main).Assembly.Location));
			foreach (string name in EmbeddedLibraries)
			{
				byte[] bytes = Embedded(name);
				if (bytes != null)
				{
					list.Add(MetadataReference.CreateFromImage(bytes));
				}
			}
			string memory = Path.Combine(DevToolsInstaller.Folder, "System.Memory.dll");
			if (File.Exists(memory))
			{
				list.Add(MetadataReference.CreateFromFile(memory));
			}
			list.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Xna.Framework.Vector2).Assembly.Location));
			list.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly.Location));
			list.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Xna.Framework.Game).Assembly.Location));
			_base = list.Cast<object>().ToList();
		}
		List<MetadataReference> all = _base.Cast<MetadataReference>().ToList();
		foreach (string file in pluginFiles.Where(File.Exists))
		{
			all.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(file)));
		}
		return all;
	}

	private static void AddLoaded(List<MetadataReference> list, string name)
	{
		try
		{
#pragma warning disable 618
			Assembly assembly = Assembly.LoadWithPartialName(name);
#pragma warning restore 618
			if (assembly != null && !string.IsNullOrEmpty(assembly.Location))
			{
				list.Add(MetadataReference.CreateFromFile(assembly.Location));
			}
		}
		catch
		{
		}
	}

	internal static byte[] Embedded(string name)
	{
		Assembly exe = typeof(Terraria.Main).Assembly;
		string resource = exe.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("." + name + ".dll", StringComparison.OrdinalIgnoreCase));
		if (resource == null)
		{
			return null;
		}
		using Stream stream = exe.GetManifestResourceStream(resource);
		byte[] bytes = new byte[stream.Length];
		stream.Read(bytes, 0, bytes.Length);
		return bytes;
	}

	/// <summary>Compiles <paramref name="sources"/> (path, text) into a library called <paramref name="assemblyName"/>.</summary>
	public static CompileResult Compile(string assemblyName, IEnumerable<KeyValuePair<string, string>> sources, IEnumerable<string> pluginFiles)
	{
		return Compile(assemblyName, sources, pluginFiles, null, null);
	}

	/// <summary>The same, with preprocessor symbols and resources (name, bytes) carried by the library.</summary>
	public static CompileResult Compile(string assemblyName, IEnumerable<KeyValuePair<string, string>> sources, IEnumerable<string> pluginFiles, IEnumerable<string> symbolsDefined, IEnumerable<KeyValuePair<string, byte[]>> resources)
	{
		CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbolsDefined);
		List<SyntaxTree> trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.Value, parse, s.Key, Encoding.UTF8)).ToList();
		CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, trees, References(pluginFiles), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, platform: Platform.X86, optimizationLevel: OptimizationLevel.Release));
		List<ResourceDescription> manifest = (resources ?? Enumerable.Empty<KeyValuePair<string, byte[]>>()).Select(r =>
		{
			byte[] data = r.Value;
			return new ResourceDescription(r.Key, () => new MemoryStream(data), isPublic: true);
		}).ToList();
		using MemoryStream assembly = new MemoryStream();
		using MemoryStream symbols = new MemoryStream();
		EmitResult emit = compilation.Emit(assembly, symbols, manifestResources: manifest, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
		CompileResult result = new CompileResult();
		foreach (Diagnostic diagnostic in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
		{
			FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
			string where = string.IsNullOrEmpty(span.Path) ? "" : (Path.GetFileName(span.Path) + "(" + (span.StartLinePosition.Line + 1) + "): ");
			result.Errors.Add(where + diagnostic.Id + " " + diagnostic.GetMessage());
		}
		if (emit.Success)
		{
			result.Assembly = assembly.ToArray();
			result.Symbols = symbols.ToArray();
		}
		return result;
	}
}
