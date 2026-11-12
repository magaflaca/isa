using System.Reflection;

namespace IsaClient.Core;

/// <summary>Client name and version (Version + Hotfix) used by the updater, the title and the credits.</summary>
public static class ClientInfo
{
	public const string Name = "IsaClient";

	/// <summary>Version compared against the update server; bump it on every release (and reset <see cref="Hotfix"/> to 0).</summary>
	public const string Version = "1.5.4";

	/// <summary>Bug-fix builds of the same version: 1 publishes "1.3.9+hotfix.1". 0 = the release itself.</summary>
	public const int Hotfix = 0;

	public static string FullVersion => FormatVersion(Version, Hotfix);

	public static string DisplayName => Name + " v" + FullVersion;

	public static string FormatVersion(string version, int hotfix)
	{
		if (hotfix <= 0)
		{
			return version;
		}
		return version + "+hotfix." + hotfix;
	}

	#region Chat commands

	/// <summary>.asm</summary>
	internal static void AssemblyCommand(CommandContext command)
	{
		ClientChat.Info(Assembly.GetExecutingAssembly().GetName().Name + " (" + DisplayName + ")");
	}

	#endregion
}
