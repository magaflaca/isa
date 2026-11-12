using System;
using System.Linq;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Terraria;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// The game's own C# for one member (the Plugin Maker reads it before patching or calling it). Only call it after
/// DevToolsInstaller.Ensure; the decompiler is kept as an object so this class loads without it.
/// </summary>
public static class GameDecompiler
{
	private static object _decompiler;

	private static readonly object _lock = new object();

	public static string Member(string typeName, string memberName)
	{
		lock (_lock)
		{
			CSharpDecompiler decompiler = Decompiler();
			ITypeDefinition type = Find(decompiler, typeName);
			if (type == null)
			{
				return "No type '" + typeName + "' in the game.";
			}
			IEntity[] members = type.Members.Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)).Cast<IEntity>()
				.Concat(type.NestedTypes.Where(t => t.Name == memberName))
				.ToArray();
			if (members.Length == 0)
			{
				members = type.Members.Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase)).Cast<IEntity>().ToArray();
			}
			if (members.Length == 0)
			{
				string near = string.Join(", ", type.Members.Select(m => m.Name).Distinct().Where(n => n.IndexOf(memberName, StringComparison.OrdinalIgnoreCase) >= 0).Take(15));
				return "No member '" + memberName + "' in " + type.FullName + "." + (near.Length > 0 ? (" Similar: " + near) : "");
			}
			StringBuilder b = new StringBuilder();
			foreach (IEntity member in members.Take(6))
			{
				b.AppendLine(decompiler.DecompileAsString(member.MetadataToken));
			}
			string code = b.ToString();
			return (code.Length > 20000) ? (code.Substring(0, 20000) + "\n... (cut: the method is very long)") : code;
		}
	}

	private static CSharpDecompiler Decompiler()
	{
		if (_decompiler == null)
		{
			string exe = typeof(Main).Assembly.Location;
			DecompilerSettings settings = new DecompilerSettings(LanguageVersion.CSharp7_3)
			{
				ThrowOnAssemblyResolveErrors = false
			};
			_decompiler = new CSharpDecompiler(exe, new UniversalAssemblyResolver(exe, false, ".NETFramework,Version=v4.8"), settings);
		}
		return (CSharpDecompiler)_decompiler;
	}

	private static ITypeDefinition Find(CSharpDecompiler decompiler, string name)
	{
		name = (name ?? "").Trim();
		ITypeDefinition type = decompiler.TypeSystem.MainModule.GetTypeDefinition(new FullTypeName(name));
		if (type != null)
		{
			return type;
		}
		return decompiler.TypeSystem.MainModule.TypeDefinitions.FirstOrDefault(t => t.Name == name && t.Namespace == "Terraria")
			?? decompiler.TypeSystem.MainModule.TypeDefinitions.FirstOrDefault(t => t.Name == name)
			?? decompiler.TypeSystem.MainModule.TypeDefinitions.FirstOrDefault(t => t.FullName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
	}
}
