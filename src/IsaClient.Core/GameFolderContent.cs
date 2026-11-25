using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using ReLogic.Content;
using ReLogic.Content.Sources;

namespace IsaClient.Core;

/// <summary>
/// Content source under the game's own (lowest priority), for game folders older than this client: a texture the folder
/// doesn't have comes from here instead of crashing the game while it loads. The ones 1.4.6 added are a vanilla texture
/// of the old folder that does the same job (the trap bomb is drawn as the bomb it replaces) or, with no such texture, a
/// small icon drawn here. Any other missing texture (an older 1.4.5 folder) is transparent; the first one is logged.
/// A complete folder never reaches it.
/// </summary>
public sealed class GameFolderContent : IContentSource
{
	private static readonly Dictionary<string, string> StandIns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "Images\\Projectile_1136", "Images\\Projectile_28" },
		{ "Images\\UI\\MultiplayerLobby\\HeaderBacking", "Images\\UI\\PanelBackground" },
		{ "Images\\UI\\MultiplayerLobby\\Button_DropDown", "Images\\UI\\ButtonPlay" },
		{ "Images\\UI\\MultiplayerLobby\\Button_DropUp", "Images\\UI\\ButtonPlay" },
		{ "Images\\UI\\MultiplayerLobby\\MultiplayerFriends", "Images\\UI\\ButtonFavoriteActive" },
		{ "Images\\UI\\MultiplayerLobby\\MultiplayerLocal", "Images\\UI\\ButtonCloudInactive" },
		{ "Images\\UI\\MultiplayerLobby\\MultiplayerRemote", "Images\\UI\\ButtonCloudActive" }
	};

	private static readonly Dictionary<string, Func<byte[]>> Drawn = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase)
	{
		{ "Images\\UI\\Invite_0", () => Icon(Envelope, null) },
		{ "Images\\UI\\Invite_1", () => Icon(Envelope, Color.FromArgb(255, 222, 110)) },
		{ "Images\\UI\\MultiplayerLobby\\JoinHeader", () => Icon(Join, null) },
		{ "Images\\UI\\MultiplayerLobby\\HostHeader", () => Icon(Host, null) }
	};

	private static readonly string[] Envelope =
	{
		"................", "................", "................", ".oooooooooooooo.", ".owwwwwwwwwwwwo.", ".oowwwwwwwwwwoo.",
		".osowwwwwwwwoso.", ".owsowwwwwwoswo.", ".owwsowwwwoswwo.", ".owwwsooooswwwo.", ".owwwwssssswwwo.", ".owwwwwwwwwwwwo.",
		".oooooooooooooo.", "................", "................", "................"
	};

	private static readonly string[] Join =
	{
		"................", "........ooooooo.", "........owwwwwo.", "....o...owsssso.", "....oo..owsssso.", "ooooowo.owsssso.",
		"owwwwwwoowsssso.", "owwwwwwwowsssso.", "owwwwwwoowsssso.", "ooooowo.owsssso.", "....oo..owsssso.", "....o...owsssso.",
		"........owsssso.", "........owwwwwo.", "........ooooooo.", "................"
	};

	private static readonly string[] Host =
	{
		"................", ".......oo.......", "......owwo......", ".....owwwwo.....", "....owwwwwwo....", "...owwwwwwwwo...",
		"..owwwwwwwwwwo..", ".oooowwwwwwoooo.", "....owwwwwwo....", "....owoooowo....", "....owossowo....", "....owossowo....",
		"....owossowo....", "....oooooooo....", "................", "................"
	};

	private static readonly string[] Blank = Enumerable.Repeat("................", 16).ToArray();

	private readonly string _root;

	private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _blank = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static byte[] _blankPng;

	private readonly RejectedAssetCollection _rejections = new RejectedAssetCollection();

	public IContentValidator ContentValidator { get; set; }

	public string FileWatcherPath => null;

	public GameFolderContent(string rootDirectory)
	{
		_root = rootDirectory;
		Refresh();
	}

	public void Refresh()
	{
		_missing.Clear();
		foreach (string name in StandIns.Keys)
		{
			if (!HasXnb(name) && HasXnb(StandIns[name]))
			{
				_missing.Add(name);
			}
		}
		foreach (string name in Drawn.Keys)
		{
			if (!HasXnb(name))
			{
				_missing.Add(name);
			}
		}
	}

	public bool HasAsset(string assetName)
	{
		if (_rejections.IsRejected(assetName))
		{
			return false;
		}
		if (_missing.Contains(assetName) || IsBlank(assetName))
		{
			return true;
		}
		string name = assetName.Replace('/', '\\');
		if (!name.StartsWith("Images\\", StringComparison.OrdinalIgnoreCase) || HasXnb(name))
		{
			return false;
		}
		lock (_blank)
		{
			if (_blank.Count == 0)
			{
				ClientLog.LogOnce(new FileNotFoundException("The game folder is older than IsaClient: missing textures are drawn transparent, starting with " + assetName));
			}
			_blank.Add(assetName);
		}
		return true;
	}

	private bool IsBlank(string assetName)
	{
		lock (_blank)
		{
			return _blank.Contains(assetName);
		}
	}

	public List<string> GetAllAssetsStartingWith(string assetNameStart)
	{
		List<string> list = new List<string>();
		foreach (string name in _missing)
		{
			if (name.StartsWith(assetNameStart, StringComparison.OrdinalIgnoreCase))
			{
				list.Add(name);
			}
		}
		return list;
	}

	public string GetExtension(string assetName)
	{
		if (!Drawn.ContainsKey(assetName) && !IsBlank(assetName))
		{
			return ".xnb";
		}
		return ".png";
	}

	public Stream OpenStream(string assetName)
	{
		if (IsBlank(assetName))
		{
			return new MemoryStream(_blankPng ?? (_blankPng = Icon(Blank, null)));
		}
		if (!_missing.Contains(assetName))
		{
			throw AssetLoadException.FromMissingAsset(assetName);
		}
		if (Drawn.TryGetValue(assetName, out Func<byte[]> draw))
		{
			return new MemoryStream(draw());
		}
		return File.OpenRead(XnbPath(StandIns[assetName]));
	}

	public void RejectAsset(string assetName, IRejectionReason reason)
	{
		_rejections.Reject(assetName, reason);
	}

	public void ClearRejections()
	{
		_rejections.Clear();
	}

	public bool TryGetRejections(List<string> rejectionReasons)
	{
		return _rejections.TryGetRejections(rejectionReasons);
	}

	private string XnbPath(string name)
	{
		return Path.Combine(_root, name) + ".xnb";
	}

	private bool HasXnb(string name)
	{
		return File.Exists(XnbPath(name));
	}

	private static byte[] Icon(string[] grid, Color? outline)
	{
		const int scale = 2;
		using Bitmap bitmap = new Bitmap(grid[0].Length * scale, grid.Length * scale, PixelFormat.Format32bppArgb);
		for (int y = 0; y < grid.Length; y++)
		{
			for (int x = 0; x < grid[y].Length; x++)
			{
				Color color = Ink(grid[y][x], outline);
				for (int d = 0; d < scale * scale; d++)
				{
					bitmap.SetPixel(x * scale + d % scale, y * scale + d / scale, color);
				}
			}
		}
		using MemoryStream stream = new MemoryStream();
		bitmap.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	private static Color Ink(char c, Color? outline)
	{
		switch (c)
		{
		case 'o':
			return outline ?? Color.FromArgb(35, 37, 72);
		case 'w':
			return Color.FromArgb(232, 232, 240);
		case 's':
			return Color.FromArgb(160, 166, 210);
		default:
			return Color.Transparent;
		}
	}
}
