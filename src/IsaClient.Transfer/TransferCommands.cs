using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using ReLogic.OS;
using ReLogic.OS.Base;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Transfer;

/// <summary>
/// .sendfile / .getfile / .delfile: any file through Imgur pictures (<see cref="ImageTransfer"/>). The code goes to the
/// clipboard to paste anywhere; received files land in IsaClient/Received/Files. What was sent is listed in
/// IsaClient/Transfer/Sent.txt (with what takes it down again).
/// </summary>
internal static class TransferCommands
{
	private static int _busy;

	public static string ReceivedFolder => Path.Combine(ClientSettings.IsaClientFolder, "Received", "Files");

	private static string SentList => Path.Combine(ClientSettings.IsaClientFolder, "Transfer", "Sent.txt");

	/// <summary>.sendfile [path]: picks a file (or takes the path) and sends it.</summary>
	internal static void SendFileCommand(CommandContext command)
	{
		string path = (command.Parts.Length > 1) ? string.Join(" ", command.Parts, 1, command.Parts.Length - 1).Trim('"') : null;
		if (Interlocked.Exchange(ref _busy, 1) == 1)
		{
			ClientChat.Error("A file is already being sent or received, wait for it to finish.");
			return;
		}
		Task.Run(delegate
		{
			try
			{
				if (path == null)
				{
					path = FileBrowser.OpenFilePanel("Send a file", new ExtensionFilter[1]
					{
						new ExtensionFilter("All Files", "*")
					});
				}
				if (string.IsNullOrEmpty(path))
				{
					return;
				}
				if (!File.Exists(path))
				{
					ClientChat.Error("No file at " + path);
					return;
				}
				long size = new FileInfo(path).Length;
				if (size > ImageTransfer.MaxFileBytes)
				{
					ClientChat.Error(IsaClient.Languages.L10n.F("Files up to {0} MB.", ImageTransfer.MaxFileBytes >> 20));
					return;
				}
				string name = Path.GetFileName(path);
				ClientChat.Info(IsaClient.Languages.L10n.F("Sending {0} ({1})...", name, Size(size)));
				int shown = 0;
				ImageTransferResult result = ImageTransfer.Upload(File.ReadAllBytes(path), name, delegate(float p)
				{
					int step = (int)(p * 4f);
					if (step > shown && step < 4)
					{
						shown = step;
						ClientChat.Info(IsaClient.Languages.L10n.F("Sending {0}: {1}%", name, step * 25));
					}
				});
				Remember(result);
				Terraria.Main.QueueMainThreadAction(delegate
				{
					try
					{
						Platform.Get<IClipboard>().Value = result.Code;
					}
					catch
					{
					}
				});
				ClientChat.Info(IsaClient.Languages.L10n.F("Sent {0} in {1} pictures. Code (copied): {2}", name, result.Pictures, result.Code));
				ClientChat.Info("Whoever has it gets the file with .getfile <code>. .delfile <code> takes it down.");
			}
			catch (Exception ex)
			{
				ClientChat.Error("Could not send the file: " + ex.Message);
				ClientLog.LogException(ex);
			}
			finally
			{
				_busy = 0;
			}
		});
	}

	/// <summary>.getfile &lt;code&gt;: downloads a sent file into Received/Files.</summary>
	internal static void GetFileCommand(CommandContext command)
	{
		string code = string.Join(" ", command.Parts.Skip(1));
		if (!ImageTransfer.IsCode(code))
		{
			ClientChat.Error("Usage: .getfile isaf:...  (the code .sendfile gave)");
			return;
		}
		if (Interlocked.Exchange(ref _busy, 1) == 1)
		{
			ClientChat.Error("A file is already being sent or received, wait for it to finish.");
			return;
		}
		Task.Run(delegate
		{
			try
			{
				ClientChat.Info("Downloading the file...");
				ImageTransferFile file = ImageTransfer.Download(code);
				Directory.CreateDirectory(ReceivedFolder);
				string path = Free(Path.Combine(ReceivedFolder, string.IsNullOrEmpty(file.Name) ? "file" : file.Name));
				File.WriteAllBytes(path, file.Data);
				ClientChat.Info(IsaClient.Languages.L10n.F("Received {0} ({1}): {2}", file.Name, Size(file.Data.LongLength), path));
			}
			catch (Exception ex)
			{
				ClientChat.Error("Could not get the file: " + ex.Message);
				ClientLog.LogException(ex);
			}
			finally
			{
				_busy = 0;
			}
		});
	}

	/// <summary>.delfile &lt;code&gt;: takes down a file this client sent (its pictures stop working).</summary>
	internal static void DeleteFileCommand(CommandContext command)
	{
		string code = string.Join(" ", command.Parts.Skip(1)).Trim();
		Task.Run(delegate
		{
			try
			{
				List<string> lines = File.Exists(SentList) ? File.ReadAllLines(SentList).ToList() : new List<string>();
				string line = lines.FirstOrDefault(l => l.Split('\t')[0] == code);
				if (line == null)
				{
					ClientChat.Error("This client didn't send " + code + " (only the sender can take it down).");
					return;
				}
				string[] parts = line.Split('\t');
				ImageTransfer.Delete(new ImageTransferResult
				{
					DeleteHashes = (parts.Length > 2) ? parts[2].Split(',') : new string[0]
				});
				lines.Remove(line);
				File.WriteAllLines(SentList, lines);
				ClientChat.Info("Took down " + ((parts.Length > 1) ? parts[1] : code) + ".");
			}
			catch (Exception ex)
			{
				ClientChat.Error("Could not take it down: " + ex.Message);
			}
		});
	}

	private static void Remember(ImageTransferResult result)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SentList));
			File.AppendAllText(SentList, result.Code + "\t" + result.Name + "\t" + string.Join(",", result.DeleteHashes) + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static string Free(string path)
	{
		if (!File.Exists(path))
		{
			return path;
		}
		string folder = Path.GetDirectoryName(path);
		string name = Path.GetFileNameWithoutExtension(path);
		string ext = Path.GetExtension(path);
		for (int i = 2; ; i++)
		{
			string next = Path.Combine(folder, name + " (" + i + ")" + ext);
			if (!File.Exists(next))
			{
				return next;
			}
		}
	}

	private static string Size(long bytes)
	{
		return (bytes >= 1 << 20) ? ((bytes / 1048576.0).ToString("0.0") + " MB") : ((bytes / 1024.0).ToString("0") + " KB");
	}
}
