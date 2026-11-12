using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IsaClient.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;

namespace IsaClient.Items;

/// <summary>
/// IsaClient items as files (.isaitem): export some of My Items to share them outside the game, import them back.
/// A file holds one or more designs; plain design files (the ones in IsaClient/Items) import too.
/// </summary>
public static class ItemFiles
{
	public const string Extension = ".isaitem";

	private static bool _dialogOpen;

	private sealed class Pack
	{
		public string Format = "IsaClient items";

		public int Version = 1;

		public List<ItemDesign> Items = new List<ItemDesign>();
	}

	/// <summary>Asks where to save <paramref name="designs"/> and writes them in one file.</summary>
	public static void Export(IList<ItemDesign> designs, string suggestedName)
	{
		if (designs == null || designs.Count == 0 || _dialogOpen)
		{
			return;
		}
		List<ItemDesign> copies = designs.Select(d => d.Copy()).ToList();
		string name = Safe(suggestedName);
		Dialog(delegate(IWin32Window owner)
		{
			using SaveFileDialog dialog = new SaveFileDialog
			{
				Title = "Export IsaClient items",
				Filter = "IsaClient items (*" + Extension + ")|*" + Extension,
				FileName = name + Extension,
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return null;
			}
			string path = dialog.FileName;
			File.WriteAllText(path, JsonConvert.SerializeObject(new Pack { Items = copies }, Formatting.Indented), new UTF8Encoding(false));
			return () => ClientChat.Info(IsaClient.Languages.L10n.F("Exported {0} items to {1}", copies.Count, Path.GetFileName(path)));
		});
	}

	/// <summary>Asks for item files and adds what they hold to My Items.</summary>
	public static void Import(Action<ItemDesign> added = null)
	{
		if (_dialogOpen)
		{
			return;
		}
		Dialog(delegate(IWin32Window owner)
		{
			using OpenFileDialog dialog = new OpenFileDialog
			{
				Title = "Import IsaClient items",
				Multiselect = true,
				Filter = "IsaClient items (*" + Extension + ";*.json)|*" + Extension + ";*.json|All files|*.*",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return null;
			}
			List<ItemDesign> found = new List<ItemDesign>();
			int bad = 0;
			foreach (string file in dialog.FileNames)
			{
				try
				{
					found.AddRange(Read(File.ReadAllText(file)));
				}
				catch (Exception ex)
				{
					bad++;
					ClientLog.LogException(ex);
				}
			}
			return delegate
			{
				ItemDesign last = null;
				foreach (ItemDesign d in found)
				{
					ItemLibrary.Save(d);
					last = d;
				}
				if (last != null)
				{
					added?.Invoke(last);
				}
				if (found.Count > 0)
				{
					ClientChat.Info(IsaClient.Languages.L10n.F("Imported {0} items into My Items.", found.Count));
				}
				if (bad > 0 || found.Count == 0)
				{
					ClientChat.Error(IsaClient.Languages.L10n.T("Some files had no IsaClient items in them."));
				}
			};
		});
	}

	/// <summary>The designs in a file's text: an export (several) or a single design.</summary>
	public static List<ItemDesign> Read(string json)
	{
		List<ItemDesign> result = new List<ItemDesign>();
		JToken root = JToken.Parse(json);
		IEnumerable<JToken> items = (root is JObject obj && obj["Items"] is JArray list) ? list : ((root is JArray array) ? array : new JToken[] { root });
		foreach (JToken token in items)
		{
			ItemDesign d = token.ToObject<ItemDesign>();
			if (d == null || d.Type <= 0 || d.Type >= ItemID.Count)
			{
				continue;
			}
			if (d.Mark == null)
			{
				d.Mark = ItemMark.New();
			}
			if (string.IsNullOrEmpty(d.Mark.Id))
			{
				d.Mark.Id = ItemMark.NewId();
			}
			if (d.Mark.Description != null && d.Mark.Description.Length > ItemMark.MaxDescription)
			{
				d.Mark.Description = d.Mark.Description.Substring(0, ItemMark.MaxDescription);
			}
			if (d.Mark.Info != null && d.Mark.Info.Length > ItemMark.MaxInfo)
			{
				d.Mark.Info = d.Mark.Info.Substring(0, ItemMark.MaxInfo);
			}
			d.Mark.Tag = (byte)Math.Min((int)d.Mark.Tag, TagArt.Count - 1);
			result.Add(d);
		}
		return result;
	}

	/// <summary>Runs a Windows file dialog on its own thread (the game keeps drawing); what it returns runs on the game's thread.</summary>
	internal static void Dialog(Func<IWin32Window, Action> show)
	{
		_dialogOpen = true;
		IntPtr owner = Main.instance.Window.Handle;
		Thread thread = new Thread((ThreadStart)delegate
		{
			Action then = null;
			try
			{
				then = show(new WindowOwner(owner));
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				string message = ex.Message;
				then = () => ClientChat.Error(IsaClient.Languages.L10n.F("Could not do it: {0}", message));
			}
			Main.QueueMainThreadAction(delegate
			{
				_dialogOpen = false;
				then?.Invoke();
			});
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	private static string Safe(string name)
	{
		name = string.IsNullOrWhiteSpace(name) ? "IsaClient items" : name.Trim();
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return (name.Length > 60) ? name.Substring(0, 60) : name;
	}

	private sealed class WindowOwner : IWin32Window
	{
		public WindowOwner(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}
}
