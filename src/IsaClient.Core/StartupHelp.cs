using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace IsaClient.Core;

/// <summary>
/// Startup errors with a known cause, explained to the player in their own language (Windows' language: the game's
/// isn't loaded yet) instead of a stack trace. The crash log and report still happen as for any crash.
/// </summary>
public static class StartupHelp
{
	/// <summary>True when the error was explained (then the usual error box isn't shown).</summary>
	public static bool TryExplain(Exception e)
	{
		try
		{
			if (!IsSteamMissing(e))
			{
				return false;
			}
			string folder = AppDomain.CurrentDomain.BaseDirectory;
			MessageBox.Show(SteamText(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) + Environment.NewLine + Environment.NewLine + folder, "IsaClient", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool RelocateIfOutside(string[] args)
	{
		try
		{
			string here = AppDomain.CurrentDomain.BaseDirectory;
			if (IsTerrariaFolder(here) || HasArg(args, "-server") || HasArg(args, "-isahere"))
			{
				return false;
			}
			string exe = Process.GetCurrentProcess().MainModule.FileName;
			string folder = FindTerrariaFolder();
			if (folder == null)
			{
				return false;
			}
			string target = Path.Combine(folder, "IsaClient.exe");
			if (!File.Exists(target) || File.GetLastWriteTimeUtc(exe) > File.GetLastWriteTimeUtc(target))
			{
				try
				{
					File.Copy(exe, target, overwrite: true);
				}
				catch (Exception) when (File.Exists(target))
				{
				}
			}
			MakeDesktopShortcut(target, folder);
			Process.Start(new ProcessStartInfo(target, JoinArgs(args))
			{
				WorkingDirectory = folder,
				UseShellExecute = false
			});
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasArg(string[] args, string arg)
	{
		return args != null && Array.Exists(args, (string a) => string.Equals(a, arg, StringComparison.OrdinalIgnoreCase));
	}

	private static string JoinArgs(string[] args)
	{
		if (args == null || args.Length == 0)
		{
			return "";
		}
		return string.Join(" ", Array.ConvertAll(args, (string a) => (a.Length == 0 || a.IndexOfAny(new char[2] { ' ', '"' }) >= 0) ? ("\"" + a.Replace("\"", "\\\"") + "\"") : a));
	}

	private static bool IsTerrariaFolder(string folder)
	{
		try
		{
			return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "steam_api.dll")) && Directory.Exists(Path.Combine(folder, "Content"));
		}
		catch
		{
			return false;
		}
	}

	private static string FindTerrariaFolder()
	{
		List<string> steams = new List<string>();
		AddRegistry(steams, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
		foreach (RegistryView view in new RegistryView[2] { RegistryView.Registry32, RegistryView.Registry64 })
		{
			try
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				AddRegistry(steams, root, @"SOFTWARE\Valve\Steam", "InstallPath");
				AddRegistry(steams, root, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
				string installed = ReadRegistry(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 105600", "InstallLocation");
				if (IsTerrariaFolder(installed))
				{
					return installed;
				}
			}
			catch
			{
			}
		}
		List<string> libraries = new List<string>();
		foreach (string steam in steams)
		{
			libraries.Add(steam);
			string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
			try
			{
				if (File.Exists(vdf))
				{
					foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
					{
						libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
					}
				}
			}
			catch
			{
			}
		}
		foreach (DriveInfo drive in DriveInfo.GetDrives())
		{
			try
			{
				if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
				{
					continue;
				}
				string d = drive.RootDirectory.FullName;
				libraries.Add(Path.Combine(d, "Program Files (x86)", "Steam"));
				libraries.Add(Path.Combine(d, "Program Files", "Steam"));
				libraries.Add(Path.Combine(d, "Steam"));
				libraries.Add(Path.Combine(d, "SteamLibrary"));
				libraries.Add(Path.Combine(d, "Games", "Steam"));
				libraries.Add(Path.Combine(d, "Games", "SteamLibrary"));
				libraries.Add(Path.Combine(d, "Juegos", "Steam"));
				libraries.Add(Path.Combine(d, "Juegos", "SteamLibrary"));
			}
			catch
			{
			}
		}
		foreach (string library in libraries)
		{
			string folder = Path.Combine(library, "steamapps", "common", "Terraria");
			if (IsTerrariaFolder(folder))
			{
				return folder;
			}
		}
		return null;
	}

	private static void AddRegistry(List<string> list, RegistryKey root, string key, string value)
	{
		string path = ReadRegistry(root, key, value);
		if (!string.IsNullOrEmpty(path))
		{
			list.Add(path.Replace('/', '\\'));
		}
	}

	private static string ReadRegistry(RegistryKey root, string key, string value)
	{
		try
		{
			using RegistryKey k = root.OpenSubKey(key);
			return k?.GetValue(value) as string;
		}
		catch
		{
			return null;
		}
	}

	private static void MakeDesktopShortcut(string target, string folder)
	{
		try
		{
			string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IsaClient.lnk");
			Type type = Type.GetTypeFromProgID("WScript.Shell");
			if (type == null)
			{
				return;
			}
			object shell = Activator.CreateInstance(type);
			object shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[1] { link });
			Type st = shortcut.GetType();
			st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[1] { target });
			st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[1] { folder });
			st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[1] { target + ",0" });
			st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
		}
		catch
		{
		}
	}

	/// <summary>Steam couldn't start the game: steam_api.dll isn't here, or the exe isn't in Terraria's folder.</summary>
	public static bool IsSteamMissing(Exception e)
	{
		for (Exception x = e; x != null; x = x.InnerException)
		{
			string text = x.ToString();
			if (text.Contains("SteamAPI_RestartAppIfNecessary") || (x is DllNotFoundException && text.IndexOf("steam_api", StringComparison.OrdinalIgnoreCase) >= 0))
			{
				return true;
			}
		}
		return !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_api.dll")) && e.ToString().Contains("Steamworks");
	}

	private static string SteamText(string language)
	{
		switch (language)
		{
		case "es":
			return "IsaClient no pudo conectarse con Steam.\n\nColoca el ejecutable de IsaClient dentro de la carpeta de tu Terraria, al lado del Terraria.exe original (por ejemplo: Steam\\steamapps\\common\\Terraria), y ábrelo desde ahí.\n\nAsegúrate también de que Steam esté abierto y de que tengas Terraria en tu biblioteca.\n\nAhora mismo está en:";
		case "pt":
			return "O IsaClient não conseguiu se conectar ao Steam.\n\nColoque o executável do IsaClient dentro da pasta do seu Terraria, ao lado do Terraria.exe original (por exemplo: Steam\\steamapps\\common\\Terraria), e abra-o de lá.\n\nVerifique também se o Steam está aberto e se você tem o Terraria na sua biblioteca.\n\nAgora ele está em:";
		case "fr":
			return "IsaClient n'a pas pu se connecter à Steam.\n\nPlacez l'exécutable d'IsaClient dans le dossier de votre Terraria, à côté du Terraria.exe d'origine (par exemple : Steam\\steamapps\\common\\Terraria), et lancez-le depuis là.\n\nVérifiez aussi que Steam est ouvert et que Terraria est dans votre bibliothèque.\n\nIl se trouve actuellement ici :";
		case "de":
			return "IsaClient konnte keine Verbindung zu Steam herstellen.\n\nLege die IsaClient-Datei in deinen Terraria-Ordner, neben die originale Terraria.exe (zum Beispiel: Steam\\steamapps\\common\\Terraria), und starte sie von dort.\n\nAchte auch darauf, dass Steam geöffnet ist und Terraria in deiner Bibliothek ist.\n\nIm Moment liegt sie hier:";
		case "it":
			return "IsaClient non è riuscito a collegarsi a Steam.\n\nMetti l'eseguibile di IsaClient nella cartella del tuo Terraria, accanto al Terraria.exe originale (per esempio: Steam\\steamapps\\common\\Terraria), e avvialo da lì.\n\nAssicurati anche che Steam sia aperto e che Terraria sia nella tua libreria.\n\nAdesso si trova qui:";
		case "ru":
			return "IsaClient не смог подключиться к Steam.\n\nПоложите исполняемый файл IsaClient в папку вашей Terraria, рядом с оригинальным Terraria.exe (например: Steam\\steamapps\\common\\Terraria), и запускайте его оттуда.\n\nТакже убедитесь, что Steam запущен и Terraria есть в вашей библиотеке.\n\nСейчас он находится здесь:";
		case "pl":
			return "IsaClient nie mógł połączyć się ze Steamem.\n\nUmieść plik IsaClient w folderze swojej Terrarii, obok oryginalnego Terraria.exe (na przykład: Steam\\steamapps\\common\\Terraria), i uruchamiaj go stamtąd.\n\nUpewnij się też, że Steam jest włączony, a Terraria jest w twojej bibliotece.\n\nTeraz znajduje się tutaj:";
		case "zh":
			return "IsaClient 无法连接到 Steam。\n\n请把 IsaClient 的程序放到你的 Terraria 文件夹里，和原版 Terraria.exe 放在一起（例如：Steam\\steamapps\\common\\Terraria），然后从那里打开。\n\n同时请确认 Steam 已经打开，并且你的库里有 Terraria。\n\n它现在的位置是：";
		default:
			return "IsaClient couldn't connect to Steam.\n\nPut the IsaClient executable inside your Terraria folder, next to the original Terraria.exe (for example: Steam\\steamapps\\common\\Terraria), and open it from there.\n\nAlso make sure Steam is open and Terraria is in your library.\n\nRight now it is in:";
		}
	}
}
