using System;
using System.IO;

namespace IsaClient.Core;

/// <summary>
/// The game folder the exe runs from. It can be a 1.4.6.1 install or an older one, which lacks the files 1.4.6 added
/// (msvcr100, d3dx9_41 and X3DAudio next to the exe, GNS_Wrapper for crossplay): those are only used when present.
/// </summary>
public static class GameFolder
{
	public static string Path => AppDomain.CurrentDomain.BaseDirectory;

	public static bool HasFile(string name)
	{
		return File.Exists(System.IO.Path.Combine(Path, name));
	}
}
