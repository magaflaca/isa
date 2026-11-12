using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// Builds a TerraAngel fork (Plugins/Forks/&lt;name&gt;: the fork's repository, its src folder, or just its TerraAngel
/// folder, as people edit them) into its own TerraAngel for IsaClient, in place of the TerraAngel pack:
///  - the pack carries its own source (adapted to IsaClient) and the fingerprints of every file of the TerraAngel it was
///    made from (ForkSupport, see plugins/TerraAngel/tools/make_upstream.py);
///  - a TerraAngel file the fork did not change keeps the pack's version; one it changed gets the fork's changes merged
///    into the pack's version (three-way, like git); a new file is added;
///  - files IsaClient has its own version of (the loader, the renderer...) and changed game files (Terraria/*.cs) can't
///    be used: the game is already compiled. They are listed in the report so their changes can become Harmony patches.
/// The result is called TerraAngel.IsaPlugin like the pack, so plugins made for the pack use it as they are.
/// </summary>
public static class ForkBuilder
{
    public const string AssemblyName = "TerraAngel.IsaPlugin";

    /// <summary>What a build did, for the report and the log.</summary>
    public sealed class Report
    {
        public string TerraAngelFolder;

        public List<string> Merged = new List<string>();

        public List<string> Taken = new List<string>();

        public List<string> Added = new List<string>();

        public List<string> Conflicts = new List<string>();

        public List<string> NotUsed = new List<string>();

        public List<string> GameFiles = new List<string>();

        public List<string> GameFilesAdded = new List<string>();

        public string Summary
        {
            get
            {
                List<string> parts = new List<string>();
                int used = Merged.Count + Taken.Count;
                parts.Add(used + " TerraAngel file" + (used == 1 ? "" : "s") + " changed" + (Merged.Count > 0 ? " (" + Merged.Count + " merged)" : ""));
                if (Added.Count > 0)
                {
                    parts.Add(Added.Count + " new");
                }
                if (Conflicts.Count > 0)
                {
                    parts.Add(Conflicts.Count + " taken as they are (they clash with IsaClient's changes)");
                }
                if (NotUsed.Count + GameFiles.Count > 0)
                {
                    parts.Add((NotUsed.Count + GameFiles.Count) + " can't be used (see the report)");
                }
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>Line ends and trailing spaces don't count (the text comes without its first BOM, see ReadText).</summary>
    public static string Normalize(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(l => l.TrimEnd())).TrimEnd('\n');
    }

    public static string Fingerprint(string text)
    {
        using SHA1 sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(text)));
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    /// <summary>The fork's TerraAngel folder: a folder called TerraAngel with Tools and UI in it (the shallowest one).</summary>
    public static string FindTerraAngelFolder(string root)
    {
        if (IsTerraAngelFolder(root))
        {
            return root;
        }
        Queue<string> queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            string folder = queue.Dequeue();
            string[] children;
            try
            {
                children = Directory.GetDirectories(folder);
            }
            catch
            {
                continue;
            }
            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (Skipped(name))
                {
                    continue;
                }
                if (string.Equals(name, "TerraAngel", StringComparison.OrdinalIgnoreCase) && IsTerraAngelFolder(child))
                {
                    return child;
                }
                queue.Enqueue(child);
            }
        }
        return null;
    }

    private static bool IsTerraAngelFolder(string folder)
    {
        return Directory.Exists(Path.Combine(folder, "Tools")) && Directory.Exists(Path.Combine(folder, "UI"));
    }

    private static bool Skipped(string folderName)
    {
        return folderName.Equals("obj", StringComparison.OrdinalIgnoreCase) || folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) || folderName.StartsWith(".", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles(string folder, string except)
    {
        Stack<string> stack = new Stack<string>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (except != null && SamePath(current, except))
            {
                continue;
            }
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(current, "*.cs");
                folders = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }
            foreach (string file in files)
            {
                yield return file;
            }
            foreach (string child in folders)
            {
                if (!Skipped(Path.GetFileName(child)))
                {
                    stack.Push(child);
                }
            }
        }
    }

    /// <summary>Reads a source file, also past Windows' 260 character path limit (deep repositories).</summary>
    private static string ReadText(string file)
    {
        if (file.Length >= 240)
        {
            string full = Path.GetFullPath(file);
            file = full.StartsWith(@"\\", StringComparison.Ordinal) ? (@"\\?\UNC\" + full.Substring(2)) : (@"\\?\" + full);
        }
        // Decoded as it is (File.ReadAllText would drop a BOM): Normalize drops exactly one, like make_upstream.py
        // (some generated TerraAngel files start with two).
        string text = new UTF8Encoding(false).GetString(File.ReadAllBytes(file));
        return (text.Length > 0 && text[0] == '\uFEFF') ? text.Substring(1) : text;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Relative(string root, string file)
    {
        return file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
    }

    /// <summary>Text resources of the pack, by name (paths with '/').</summary>
    private static Dictionary<string, string> PackResources(string packFile, out List<KeyValuePair<string, byte[]>> binary)
    {
        Assembly pack = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(packFile));
        Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        binary = new List<KeyValuePair<string, byte[]>>();
        foreach (string name in pack.GetManifestResourceNames())
        {
            using Stream stream = pack.GetManifestResourceStream(name);
            using MemoryStream copy = new MemoryStream();
            stream.CopyTo(copy);
            string key = name.Replace('\\', '/');
            if (key.StartsWith("PackSource/", StringComparison.Ordinal) || key.StartsWith("ForkSupport/", StringComparison.Ordinal))
            {
                texts[key] = Encoding.UTF8.GetString(copy.ToArray());
            }
            else
            {
                // Fonts and the like: the fork's build carries them too.
                binary.Add(new KeyValuePair<string, byte[]>(name, copy.ToArray()));
            }
        }
        return texts;
    }

    /// <summary>Builds the fork in <paramref name="folder"/> on the pack <paramref name="packFile"/>.</summary>
    public static CompileResult Build(string key, string folder, string packFile, IEnumerable<string> references, out Report report)
    {
        report = new Report();
        CompileResult failed = new CompileResult();
        if (packFile == null || !File.Exists(packFile))
        {
            failed.Errors.Add("A fork is built on the TerraAngel pack: put TerraAngel.IsaPlugin.dll in the Plugins folder.");
            return failed;
        }
        string terraAngel = FindTerraAngelFolder(folder);
        if (terraAngel == null)
        {
            failed.Errors.Add("No TerraAngel folder (with Tools and UI in it) in " + folder + ".");
            return failed;
        }
        report.TerraAngelFolder = terraAngel;
        Dictionary<string, string> pack = PackResources(packFile, out List<KeyValuePair<string, byte[]>> resources);
        if (!pack.TryGetValue("ForkSupport/index.txt", out string indexText))
        {
            failed.Errors.Add("This TerraAngel pack is too old to build forks: update IsaClient's TerraAngel.IsaPlugin.dll.");
            return failed;
        }
        Dictionary<string, string> upstream = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in indexText.Split('\n'))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            {
                upstream[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
            }
        }

        // The pack's sources: Source/ by its path inside TerraAngel, Port/ and the usings as they are.
        Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> item in pack)
        {
            if (item.Key.StartsWith("PackSource/Source/", StringComparison.Ordinal))
            {
                sources["TerraAngel/" + item.Key.Substring("PackSource/Source/".Length)] = item.Value;
            }
            else if (item.Key.StartsWith("PackSource/", StringComparison.Ordinal) || item.Key.StartsWith("ForkSupport/polyfills/", StringComparison.Ordinal))
            {
                sources[item.Key] = item.Value;
            }
        }

        // The fork's TerraAngel files.
        foreach (string file in SourceFiles(terraAngel, null))
        {
            string path = "TerraAngel/" + Relative(terraAngel, file);
            string text = ReadText(file);
            string fingerprint = Fingerprint(text);
            text = text.TrimStart((char)0xFEFF);
            bool known = upstream.TryGetValue(path, out string original);
            if (known && original == fingerprint)
            {
                continue;
            }
            if (known && !sources.ContainsKey(path))
            {
                report.NotUsed.Add(path + "  (IsaClient has its own version of this file)");
                continue;
            }
            if (!known && !sources.ContainsKey(path))
            {
                sources[path] = text;
                report.Added.Add(path);
                continue;
            }
            if (pack.TryGetValue("ForkSupport/originals/" + path, out string baseText))
            {
                string merged = TextMerge.Merge(baseText, sources[path], text);
                if (merged != null)
                {
                    sources[path] = merged;
                    report.Merged.Add(path);
                }
                else
                {
                    sources[path] = text;
                    report.Conflicts.Add(path);
                }
            }
            else
            {
                sources[path] = text;
                report.Taken.Add(path);
            }
        }

        // Game files next to it (Terraria/*.cs): changed ones can't be used; new ones are compiled with it.
        string game = Directory.GetParent(terraAngel)?.FullName;
        if (game != null && string.Equals(Path.GetFileName(game), "Terraria", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string file in SourceFiles(game, terraAngel))
            {
                string path = Relative(game, file);
                string text = ReadText(file);
                string fingerprint = Fingerprint(text);
                text = text.TrimStart((char)0xFEFF);
                if (upstream.TryGetValue(path, out string original))
                {
                    if (original != fingerprint)
                    {
                        report.GameFiles.Add(path);
                    }
                }
                else if (text.IndexOf("partial class", StringComparison.Ordinal) < 0 && text.IndexOf("partial struct", StringComparison.Ordinal) < 0)
                {
                    sources["Game/" + path] = text;
                    report.GameFilesAdded.Add(path);
                }
                else
                {
                    report.GameFiles.Add(path + "  (adds to a game class)");
                }
            }
        }

        // Windows in the new files: the pack adds its own windows, a fork's new ones are added after them.
        List<string> windows = new List<string>();
        foreach (string path in report.Added)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sources[path], @"class\s+(\w+)\s*:\s*(?:[\w.]+\.)?ClientWindow\b"))
            {
                windows.Add(match.Groups[1].Value);
            }
        }
        sources["ForkBuild.g.cs"] = "namespace TerraAngel\n{\n    internal static class ForkBuild\n    {\n        public const string Name = " + Literal(key) + ";\n\n        public static readonly string[] NewWindows = { " + string.Join(", ", windows.Select(Literal)) + " };\n    }\n}\n";
        CompileResult result = CodeCompiler.Compile(AssemblyName, sources, references, new[] { "ISA_FORK" }, resources);
        WriteReport(key, report, result);
        return result;
    }

    private static string Literal(string text)
    {
        return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static void WriteReport(string key, Report report, CompileResult result)
    {
        try
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("TerraAngel fork \"" + key + "\" built " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + (result.Success ? "" : " - it does NOT compile"));
            text.AppendLine("TerraAngel folder: " + report.TerraAngelFolder);
            text.AppendLine();
            Section(text, "Changed files, merged with IsaClient's version", report.Merged);
            Section(text, "Changed files, taken as they are (IsaClient did not change them)", report.Taken);
            Section(text, "New files", report.Added);
            Section(text, "Changed files that clash with IsaClient's changes: your version is used, IsaClient's adaptations of it are lost (it may not compile)", report.Conflicts);
            Section(text, "New game files, compiled with it", report.GameFilesAdded);
            Section(text, "Not used: IsaClient has its own loader, renderer and plugin system", report.NotUsed);
            Section(text, "Changed game files: NOT used. The game is already compiled; move these changes into a Harmony patch in one of your TerraAngel files (see Plugins/Forks/README.txt)", report.GameFiles);
            if (!result.Success)
            {
                Section(text, "Compiler errors", result.Errors);
            }
            Directory.CreateDirectory(PluginLoader.CompiledFolder);
            File.WriteAllText(Path.Combine(PluginLoader.CompiledFolder, key + ".fork.txt"), text.ToString());
        }
        catch
        {
        }
    }

    private static void Section(StringBuilder text, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        text.AppendLine(title + ":");
        foreach (string item in items)
        {
            text.AppendLine("  " + item);
        }
        text.AppendLine();
    }

    /// <summary>Plugins/Forks/README.txt: what the folder is for.</summary>
    public static void WriteReadme(string forksFolder)
    {
        string path = Path.Combine(forksFolder, "README.txt");
        if (File.Exists(path))
        {
            return;
        }
        try
        {
            File.WriteAllText(path, string.Join(Environment.NewLine, new[]
            {
                "TerraAngel forks",
                "================",
                "",
                "Put your TerraAngel here as you edit it: one folder per fork (the whole repository, its src folder, or just",
                "the TerraAngel folder with Tools, UI, Graphics... in it). Then turn it on in the plugin overlay (Insert).",
                "",
                "IsaClient builds it into its own TerraAngel, in place of the TerraAngel pack:",
                "  - TerraAngel files you did not change keep IsaClient's version;",
                "  - files you changed get your changes merged into IsaClient's version (like git);",
                "  - new files (tools, windows...) are added. Tools and windows are found by themselves.",
                "",
                "What can't be used:",
                "  - changes to game files (Terraria/Player.cs...): the game is already compiled. Move them into a Harmony",
                "    patch in one of your TerraAngel files; classes marked [HarmonyPatch] are applied by themselves:",
                "        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.Update))]",
                "        static class MyPatch { static void Postfix(Player __instance) { /* your change */ } }",
                "  - changes to the loader, the renderer and the plugin system (IsaClient has its own).",
                "",
                "After every build, Plugins/Compiled/<name>.fork.txt says what was merged, added, not used, and the errors.",
                "Change a file and use .plugins reload <name> (or the overlay) to build it again."
            }));
        }
        catch
        {
        }
    }
}
