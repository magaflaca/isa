using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using IsaClient.Core;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// The plugin developer tools: Roslyn (the C# compiler, for plugin sources and the C# console) and ICSharpCode's
/// decompiler (to convert TerraAngel builds made for .NET 10). They are not in the executable: the first time something
/// needs them they are downloaded from nuget.org (~15 MB) into IsaClient/Libraries/DevTools and stay there.
/// </summary>
public static class DevToolsInstaller
{
	/// <summary>Package, version and the library folder to take (the closest to .NET Framework 4.8).</summary>
	private static readonly (string Id, string Version, string Folder)[] Packages =
	{
		("Microsoft.CodeAnalysis.Common", "5.0.0", "netstandard2.0"),
		("Microsoft.CodeAnalysis.CSharp", "5.0.0", "netstandard2.0"),
		("Microsoft.CodeAnalysis.Scripting.Common", "5.0.0", "netstandard2.0"),
		("Microsoft.CodeAnalysis.CSharp.Scripting", "5.0.0", "netstandard2.0"),
		("ICSharpCode.Decompiler", "11.1.0.9782", "netstandard2.0"),
		("System.Collections.Immutable", "9.0.0", "net462"),
		("System.Reflection.Metadata", "9.0.0", "net462"),
		("System.Memory", "4.6.0", "net462"),
		("System.Text.Encoding.CodePages", "8.0.0", "net462"),
		("System.Threading.Tasks.Extensions", "4.6.0", "net462")
	};

	private static readonly string[] RequiredFiles =
	{
		"Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.Scripting.dll",
		"Microsoft.CodeAnalysis.CSharp.Scripting.dll", "ICSharpCode.Decompiler.dll", "System.Collections.Immutable.dll",
		"System.Reflection.Metadata.dll", "System.Memory.dll", "System.Text.Encoding.CodePages.dll", "System.Threading.Tasks.Extensions.dll"
	};

	private const string VersionFile = "devtools.version";

	private const string CurrentVersion = "roslyn-5.0.0+decompiler-11.1";

	private static readonly object _lock = new object();

	private static readonly List<Action> _waiting = new List<Action>();

	private static bool _resolverAdded;

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Libraries", "DevTools");

	public static bool Installing { get; private set; }

	public static bool Installed
	{
		get
		{
			try
			{
				return RequiredFiles.All(f => File.Exists(Path.Combine(Folder, f))) && File.Exists(Path.Combine(Folder, VersionFile)) && File.ReadAllText(Path.Combine(Folder, VersionFile)).Trim() == CurrentVersion;
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>
	/// Runs <paramref name="then"/> on the game thread once the tools are there, downloading them the first time.
	/// <paramref name="reason"/> says in the chat what they are for.
	/// </summary>
	public static void Ensure(string reason, Action then)
	{
		AddResolver();
		if (Installed)
		{
			then?.Invoke();
			return;
		}
		lock (_lock)
		{
			if (then != null)
			{
				_waiting.Add(then);
			}
			if (Installing)
			{
				return;
			}
			Installing = true;
		}
		Say("Downloading the plugin developer tools (C# compiler, first time only, ~15 MB) for " + reason + "...");
		Task.Run(delegate
		{
			string error = null;
			try
			{
				Install();
			}
			catch (Exception ex)
			{
				error = ex.Message;
				PluginLog.Error("Developer tools download: " + ex.Message, ex);
			}
			Terraria.Main.QueueMainThreadAction(delegate
			{
				List<Action> waiting;
				lock (_lock)
				{
					Installing = false;
					waiting = _waiting.ToList();
					_waiting.Clear();
				}
				if (error != null)
				{
					ClientChat.Error("Could not download the developer tools: " + error);
					return;
				}
				Say("Developer tools installed.");
				foreach (Action action in waiting)
				{
					try
					{
						action();
					}
					catch (Exception ex)
					{
						PluginLog.Error("After installing the developer tools: " + ex.Message, ex);
					}
				}
			});
		});
	}

	private static void Install()
	{
		Directory.CreateDirectory(Folder);
		string temp = Path.Combine(Folder, "download");
		Directory.CreateDirectory(temp);
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		foreach ((string id, string version, string folder) in Packages)
		{
			string lower = id.ToLowerInvariant();
			string url = "https://api.nuget.org/v3-flatcontainer/" + lower + "/" + version + "/" + lower + "." + version + ".nupkg";
			string package = Path.Combine(temp, lower + ".nupkg");
			using (WebClient client = new WebClient())
			{
				client.Headers[HttpRequestHeader.UserAgent] = "IsaClient";
				client.DownloadFile(url, package);
			}
			using (Ionic.Zip.ZipFile archive = Ionic.Zip.ZipFile.Read(package))
			{
				string prefix = "lib/" + folder + "/";
				foreach (Ionic.Zip.ZipEntry entry in archive)
				{
					string name = entry.FileName.Replace('\\', '/');
					if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && name.IndexOf('/', prefix.Length) < 0)
					{
						using FileStream output = File.Create(Path.Combine(Folder, name.Substring(prefix.Length)));
						entry.Extract(output);
					}
				}
			}
			File.Delete(package);
		}
		Directory.Delete(temp, recursive: true);
		File.WriteAllText(Path.Combine(Folder, VersionFile), CurrentVersion);
		if (!Installed)
		{
			throw new InvalidOperationException("some files are missing after the download");
		}
	}

	/// <summary>The tools' libraries are loaded from their folder when something asks for them.</summary>
	public static void AddResolver()
	{
		if (_resolverAdded)
		{
			return;
		}
		_resolverAdded = true;
		Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
		{
			AssemblyName name = new AssemblyName(args.Name);
			lock (loaded)
			{
				if (loaded.TryGetValue(name.Name, out Assembly cached))
				{
					return cached;
				}
				string path = Path.Combine(Folder, name.Name + ".dll");
				if (!File.Exists(path))
				{
					return null;
				}
				Assembly assembly = Assembly.LoadFrom(path);
				loaded[name.Name] = assembly;
				return assembly;
			}
		};
	}

	private static void Say(string text)
	{
		ClientChat.Info(text);
		PluginLog.Info(text);
	}
}
