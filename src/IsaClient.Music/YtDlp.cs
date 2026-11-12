using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Music;

/// <summary>Installs, updates and runs yt-dlp for the music player.</summary>
public static class YtDlp
{
	/// <summary>Downloads yt-dlp the first time it is needed, then runs <paramref name="then"/>.</summary>
	internal static void EnsureYtDlp(Action then)
	{
		if (File.Exists(MusicPlayer.YtDlpPath))
		{
			then?.Invoke();
			return;
		}
		if (MusicPlayer.Installing)
		{
			MusicPlayer.Say("yt-dlp is still downloading, the song will start when it finishes.");
			if (then != null)
			{
				Task.Run(delegate
				{
					while (MusicPlayer.Installing)
					{
						Thread.Sleep(250);
					}
					if (File.Exists(MusicPlayer.YtDlpPath))
					{
						then();
					}
				});
			}
			return;
		}
		MusicPlayer.Installing = true;
		MusicPlayer.Say("Downloading yt-dlp (first time only, ~18 MB)...");
		Task.Run(delegate
		{
			try
			{
				Directory.CreateDirectory(MusicPlayer.BinFolder);
				string temp = MusicPlayer.YtDlpPath + ".download";
				Download(MusicPlayer.YtDlpUrl, temp);
				if (File.Exists(MusicPlayer.YtDlpPath))
				{
					File.Delete(MusicPlayer.YtDlpPath);
				}
				File.Move(temp, MusicPlayer.YtDlpPath);
				MusicPlayer.Say("yt-dlp installed.");
			}
			catch (Exception ex)
			{
				MusicPlayer.SayError("Could not download yt-dlp: " + ex.Message);
				ClientLog.LogException(ex);
			}
			finally
			{
				MusicPlayer.Installing = false;
			}
			if (File.Exists(MusicPlayer.YtDlpPath))
			{
				then?.Invoke();
			}
		});
	}

	private static void Download(string url, string path)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		using WebClient webClient = new WebClient();
		webClient.Headers[HttpRequestHeader.UserAgent] = "IsaClient";
		webClient.DownloadFile(url, path);
	}

	internal static void UpdateYtDlp()
	{
		if (!File.Exists(MusicPlayer.YtDlpPath))
		{
			EnsureYtDlp(null);
			return;
		}
		MusicPlayer.Say("Updating yt-dlp...");
		Task.Run(delegate
		{
			RunYtDlp(new List<string> { "-U" }, 180000, out var output, out var error, addCommon: false);
			string result = (output + "\n" + error).Split('\n').Select((string l) => l.Trim()).LastOrDefault((string l) => l.Length > 0) ?? "done";
			MusicPlayer.Say("yt-dlp: " + result);
		});
	}

	internal static void InstallDeno()
	{
		if (File.Exists(MusicPlayer.DenoPath))
		{
			MusicPlayer.Say("deno is already installed (yt-dlp uses it for YouTube).");
			return;
		}
		MusicPlayer.Say("Downloading deno (JavaScript runtime some YouTube videos need, ~45 MB)...");
		Task.Run(delegate
		{
			string zip = Path.Combine(MusicPlayer.BinFolder, "deno.zip");
			try
			{
				Directory.CreateDirectory(MusicPlayer.BinFolder);
				Download(MusicPlayer.DenoUrl, zip);
				using (Ionic.Zip.ZipFile archive = Ionic.Zip.ZipFile.Read(zip))
				{
					foreach (Ionic.Zip.ZipEntry entry in archive)
					{
						if (entry.FileName.EndsWith("deno.exe", StringComparison.OrdinalIgnoreCase))
						{
							using FileStream output = File.Create(MusicPlayer.DenoPath);
							entry.Extract(output);
						}
					}
				}
				MusicPlayer.Say(File.Exists(MusicPlayer.DenoPath) ? "deno installed." : "deno.exe was not found in the download.");
			}
			catch (Exception ex)
			{
				MusicPlayer.SayError("Could not install deno: " + ex.Message);
			}
			finally
			{
				try
				{
					File.Delete(zip);
				}
				catch
				{
				}
			}
		});
	}

	internal static void LoadCookies(string args)
	{
		string[] browsers = new string[8] { "chrome", "firefox", "edge", "brave", "opera", "vivaldi", "chromium", "safari" };
		if (args.Length > 0 && browsers.Contains(args.Split(':')[0].ToLowerInvariant()))
		{
			MusicPlayer.CookiesFromBrowser = args.Trim();
			MusicPlayer.CookiesFile = "";
			MusicPlayer.Save();
			MusicPlayer.Say("yt-dlp will read the cookies from " + MusicPlayer.CookiesFromBrowser + " (the browser may need to be closed).");
			return;
		}
		if (args.ToLowerInvariant() == "off" || args.ToLowerInvariant() == "clear")
		{
			MusicPlayer.CookiesFile = "";
			MusicPlayer.CookiesFromBrowser = "";
			MusicPlayer.Save();
			MusicPlayer.Say("Cookies removed.");
			return;
		}
		if (args.Length > 0)
		{
			SetCookiesFile(args.Trim('"'));
			return;
		}
		Task.Run(delegate
		{
			string path = FileBrowser.OpenFilePanel("Select cookies.txt (Netscape format)", new ExtensionFilter[1]
			{
				new ExtensionFilter("Cookies", "txt")
			});
			if (path != null)
			{
				SetCookiesFile(path);
			}
		});
	}

	private static void SetCookiesFile(string path)
	{
		if (!File.Exists(path))
		{
			MusicPlayer.SayError("File not found: " + path);
			return;
		}
		try
		{
			Directory.CreateDirectory(MusicPlayer.MusicFolder);
			string target = Path.Combine(MusicPlayer.MusicFolder, "cookies.txt");
			if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(path, target, overwrite: true);
			}
			MusicPlayer.CookiesFile = target;
			MusicPlayer.CookiesFromBrowser = "";
			MusicPlayer.Save();
			MusicPlayer.Say("Cookies loaded (copied to Music/cookies.txt).");
		}
		catch (Exception ex)
		{
			MusicPlayer.SayError("Could not load the cookies: " + ex.Message);
		}
	}

	public static void OpenFolder()
	{
		Directory.CreateDirectory(MusicPlayer.SongsFolder);
		Utils.OpenFolder(MusicPlayer.MusicFolder);
	}

	internal static int RunYtDlp(List<string> arguments, int timeoutMs, out string output, out string error, bool addCommon = true, Func<bool> stop = null)
	{
		List<string> all = new List<string>();
		if (addCommon)
		{
			all.AddRange(new string[5] { "--no-warnings", "--no-progress", "--encoding", "utf-8", "--ignore-config" });
			if (!string.IsNullOrEmpty(MusicPlayer.CookiesFile) && File.Exists(MusicPlayer.CookiesFile))
			{
				all.Add("--cookies");
				all.Add(MusicPlayer.CookiesFile);
			}
			else if (!string.IsNullOrEmpty(MusicPlayer.CookiesFromBrowser))
			{
				all.Add("--cookies-from-browser");
				all.Add(MusicPlayer.CookiesFromBrowser);
			}
			if (File.Exists(MusicPlayer.DenoPath))
			{
				all.Add("--js-runtimes");
				all.Add("deno:" + MusicPlayer.DenoPath);
			}
		}
		all.AddRange(arguments);
		StringBuilder commandLine = new StringBuilder();
		foreach (string argument in all)
		{
			commandLine.Append(Quote(argument)).Append(' ');
		}
		if (addCommon && !string.IsNullOrWhiteSpace(MusicPlayer.ExtraYtDlpArguments))
		{
			commandLine.Insert(0, MusicPlayer.ExtraYtDlpArguments.Trim() + " ");
		}
		ProcessStartInfo info = new ProcessStartInfo(MusicPlayer.YtDlpPath, commandLine.ToString())
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			WorkingDirectory = MusicPlayer.BinFolder
		};
		info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
		info.EnvironmentVariables["PYTHONUTF8"] = "1";
		StringBuilder stdout = new StringBuilder();
		StringBuilder stderr = new StringBuilder();
		try
		{
			using Process process = new Process
			{
				StartInfo = info
			};
			process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (stdout)
					{
						stdout.AppendLine(e.Data);
					}
				}
			};
			process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
			{
				if (e.Data != null)
				{
					lock (stderr)
					{
						stderr.AppendLine(e.Data);
					}
				}
			};
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			// Checked a few times a second when it can be stopped (the video it downloads was closed).
			Stopwatch waited = Stopwatch.StartNew();
			bool exited;
			while (!(exited = process.WaitForExit((stop == null) ? timeoutMs : 200)) && stop != null && !stop() && waited.ElapsedMilliseconds < timeoutMs)
			{
			}
			if (!exited)
			{
				KillTree(process);
				output = stdout.ToString();
				error = stderr.ToString();
				return -1;
			}
			process.WaitForExit();
			output = stdout.ToString();
			error = stderr.ToString();
			return process.ExitCode;
		}
		catch (Exception ex)
		{
			output = stdout.ToString();
			error = ex.Message;
			return -2;
		}
	}

	/// <summary>yt-dlp.exe starts a second process that does the work: both go.</summary>
	private static void KillTree(Process process)
	{
		try
		{
			using Process taskkill = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + process.Id)
			{
				UseShellExecute = false,
				CreateNoWindow = true
			});
			taskkill?.WaitForExit(5000);
		}
		catch
		{
		}
		try
		{
			if (!process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
	}

	private static string Quote(string argument)
	{
		if (argument.Length > 0 && argument.IndexOfAny(new char[3] { ' ', '"', '\t' }) < 0)
		{
			return argument;
		}
		StringBuilder builder = new StringBuilder("\"");
		int backslashes = 0;
		foreach (char c in argument)
		{
			if (c == '\\')
			{
				backslashes++;
				continue;
			}
			if (c == '"')
			{
				builder.Append('\\', backslashes * 2 + 1);
			}
			else
			{
				builder.Append('\\', backslashes);
			}
			backslashes = 0;
			builder.Append(c);
		}
		builder.Append('\\', backslashes * 2);
		builder.Append('"');
		return builder.ToString();
	}
}
