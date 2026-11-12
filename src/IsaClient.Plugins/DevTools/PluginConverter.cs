using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// Turns a TerraAngel plugin built for .NET 10 into one for this client: the build is decompiled back to C# (with the
/// game, this client and the loaded plugins as references, so names come out right), the .NET 10-only parts are covered
/// by small polyfills and by the bridge vectors (TerraAngel.Compat), and Roslyn compiles it again for .NET Framework.
/// The source is kept next to the result (Plugins/Converted/&lt;name&gt;/) so it can be read or fixed by hand.
/// Only call it after DevToolsInstaller.Ensure.
/// </summary>
public static class PluginConverter
{
	public static string Folder => Path.Combine(PluginLoader.PluginsFolder, "Converted");

	/// <summary>The C# polyfills added to every plugin the client compiles.</summary>
	public static string Polyfills()
	{
		using Stream stream = typeof(PluginConverter).Assembly.GetManifestResourceStream("IsaClient.Plugins.DevTools.NetPolyfills.cs");
		using StreamReader reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	/// <summary>Converts <paramref name="file"/>; the result is null and <paramref name="errors"/> says why when it fails.</summary>
	public static CompileResult Convert(string file, IEnumerable<string> pluginFiles)
	{
		string name = Path.GetFileNameWithoutExtension(file);
		DecompilerSettings settings = new DecompilerSettings(LanguageVersion.CSharp12_0)
		{
			ThrowOnAssemblyResolveErrors = false
		};
		CSharpDecompiler decompiler = new CSharpDecompiler(file, new Resolver(pluginFiles), settings);
		string code = Clean(decompiler.DecompileWholeModuleAsString());
		string folder = Path.Combine(Folder, name);
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, name + ".cs"), code);
		string usings = "global using TerraAngel.Compat;\nglobal using Vector2 = TerraAngel.Compat.Vector2;\nglobal using Vector3 = TerraAngel.Compat.Vector3;\nglobal using Vector4 = TerraAngel.Compat.Vector4;\n";
		CompileResult result = CodeCompiler.Compile(name, new[]
		{
			new KeyValuePair<string, string>("Usings.cs", usings),
			new KeyValuePair<string, string>(name + ".cs", code),
			new KeyValuePair<string, string>("Polyfills.cs", Polyfills())
		}, pluginFiles);
		File.WriteAllText(Path.Combine(folder, "errors.txt"), result.Success ? "" : string.Join(Environment.NewLine, result.Errors));
		return result;
	}

	/// <summary>What the decompiler writes that does not build here.</summary>
	private static string Clean(string code)
	{
		IEnumerable<string> lines = code.Replace("\r\n", "\n").Split('\n')
			// .NET 10's assembly attributes (target framework, ref safety rules...).
			.Where(l => !l.TrimStart().StartsWith("[assembly:", StringComparison.Ordinal) && !l.TrimStart().StartsWith("[module:", StringComparison.Ordinal))
			// A parameterless base constructor call left at the end of a constructor (it is implicit anyway).
			.Where(l => !Regex.IsMatch(l, @"^\s*(base|\(\([\w.]+\)this\))\.\.ctor\(\);\s*$"));
		code = string.Join("\n", lines);
		// The game's vectors and ImGui's are one type in TerraAngel: here that is the bridge vector.
		code = code.Replace("System.Numerics.Vector2", "TerraAngel.Compat.Vector2").Replace("System.Numerics.Vector3", "TerraAngel.Compat.Vector3").Replace("System.Numerics.Vector4", "TerraAngel.Compat.Vector4");
		// ImGui's collections of vectors keep their real element type.
		return CodeCompiler.FixVectorGenerics(code);
	}

	/// <summary>Gives the decompiler this client, its libraries, the loaded plugins and .NET Framework's assemblies.</summary>
	private sealed class Resolver : IAssemblyResolver
	{
		private readonly List<string> _plugins;

		public Resolver(IEnumerable<string> plugins)
		{
			_plugins = plugins.ToList();
		}

		public MetadataFile Resolve(IAssemblyReference reference)
		{
			try
			{
				if (reference.Name == "Terraria")
				{
					return new PEFile(typeof(Terraria.Main).Assembly.Location);
				}
				string plugin = _plugins.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), reference.Name, StringComparison.OrdinalIgnoreCase));
				if (plugin != null)
				{
					return new PEFile(plugin);
				}
				byte[] embedded = CodeCompiler.Embedded(reference.Name);
				if (embedded != null)
				{
					return new PEFile(reference.Name + ".dll", new MemoryStream(embedded));
				}
#pragma warning disable 618
				Assembly assembly = Assembly.LoadWithPartialName(reference.Name);
#pragma warning restore 618
				if (assembly != null && !string.IsNullOrEmpty(assembly.Location))
				{
					return new PEFile(assembly.Location);
				}
			}
			catch
			{
			}
			return null;
		}

		public MetadataFile ResolveModule(MetadataFile mainModule, string moduleName) => null;

		public Task<MetadataFile> ResolveAsync(IAssemblyReference reference) => Task.FromResult(Resolve(reference));

		public Task<MetadataFile> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => Task.FromResult<MetadataFile>(null);

		public IDisposable BeginSnapshot() => null;
	}
}
