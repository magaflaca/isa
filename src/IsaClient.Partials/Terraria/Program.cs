using System;
using System.Reflection;

namespace Terraria;

public static partial class Program
{
	/// <summary>
	/// The types that can be loaded now. Some IsaClient parts use libraries that are downloaded only when a player uses
	/// them (the plugin developer tools): until then their types and methods are skipped here and prepared when used.
	/// </summary>
	private static Type[] LoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return Array.FindAll(ex.Types, (Type t) => t != null);
		}
	}
}
