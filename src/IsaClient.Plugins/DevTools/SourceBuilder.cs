using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// Compiles a plugin made of C# files (Plugins/Sources/&lt;name&gt;/*.cs): code people took out of their TerraAngel fork
/// works as it is, because the files get TerraAngel's usings (and the TerraAngel pack's namespaces, the bridge vectors,
/// GetString...) and the .NET polyfills. Only call it after DevToolsInstaller.Ensure.
/// </summary>
public static class SourceBuilder
{
	public static CompileResult Build(PluginEntry entry, IEnumerable<string> references)
	{
		List<string> referenceList = references.ToList();
		SearchOption depth = (entry.Key == "Sources") ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
		List<KeyValuePair<string, string>> sources = Directory.GetFiles(entry.File, "*.cs", depth)
			.Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
			.Select(f => new KeyValuePair<string, string>(f, CodeCompiler.FixVectorGenerics(File.ReadAllText(f))))
			.ToList();
		// The plugins referenced, for their namespaces (the pack's TerraAngel.* ones).
		List<Assembly> plugins = PluginLoader.Entries.Where(e => e.Assembly != null && referenceList.Contains(e.LoadedFrom ?? e.File)).Select(e => e.Assembly).ToList();
		sources.Add(new KeyValuePair<string, string>("GlobalUsings.g.cs", CodeCompiler.TerraAngelUsings(plugins)));
		sources.Add(new KeyValuePair<string, string>("Polyfills.g.cs", PluginConverter.Polyfills()));
		CompileResult result = CodeCompiler.Compile(entry.Key + ".Source.TAPlugin", sources, referenceList);
		Directory.CreateDirectory(PluginLoader.CompiledFolder);
		File.WriteAllText(Path.Combine(PluginLoader.CompiledFolder, entry.Key + ".errors.txt"), result.Success ? "" : string.Join("\r\n", result.Errors));
		return result;
	}
}
