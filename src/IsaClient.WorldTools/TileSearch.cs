using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Map;
using Terraria.ObjectData;

namespace IsaClient.WorldTools;

/// <summary>
/// Finds every tile of a kind in the world (.searchtile, World > Find Tiles): the whole map is read in the background
/// and the hits are grouped into places (near hits together). On the full map the found tiles are painted and the
/// places get a pinging ring when they're small, a frame when they're big. The marks show the next time the full map
/// opens and go away when it's closed. Only what this client has of the world can be read (all of it in single
/// player; on a server, what was loaded or revealed).
/// </summary>
public static class TileSearch
{
	/// <summary>
	/// A kind of tile to look for: a tile type (any of it), or one variant of it (a style, a map option, or a frame
	/// like the jungle spores on jungle grass plants), with its name, the item for its icon and words to find it by.
	/// </summary>
	public sealed class Kind
	{
		public int Type;

		/// <summary>-1: every tile of the type; else the variant's number (its style, map option or frame).</summary>
		public int Style = -1;

		public string Name;

		public string Internal;

		public int Item;

		/// <summary>A liquid instead of a tile: 0 water, 1 lava, 2 honey, 3 shimmer (-1: a tile).</summary>
		public int Liquid = -1;

		/// <summary>For a variant: whether a tile of <see cref="Type"/> is this one (null: any of the type).</summary>
		public Func<Tile, int, int, bool> Match;

		public string Id => (Liquid >= 0) ? IsaClient.Languages.L10n.T("liquid") : ((Style < 0) ? ("#" + Type) : ("#" + Type + ":" + Style));
	}

	/// <summary>Near hits together.</summary>
	public sealed class Place
	{
		public Rectangle Bounds;

		public int Count;

		/// <summary>The hit nearest the middle: where TP and the map go.</summary>
		public Point Spot;

		public readonly List<int> Cells = new List<int>();
	}

	public sealed class Result
	{
		public Kind Kind;

		public int Total;

		/// <summary>Nearest to you first (when the search ended).</summary>
		public List<Place> Places = new List<Place>();

		/// <summary>Hits by grid cell (at most <see cref="MaxKept"/> kept, for painting them on the map).</summary>
		public Dictionary<int, List<Point>> Hits = new Dictionary<int, List<Point>>();

		/// <summary>Hits per grid cell (all of them).</summary>
		public Dictionary<int, int> CellCounts = new Dictionary<int, int>();

		/// <summary>How much of the world this client had (0..1).</summary>
		public float Loaded;

		public double Seconds;
	}

	/// <summary>Size of a grid cell in tiles: hits in touching cells are one place.</summary>
	public const int CellSize = 24;

	private const int MaxKept = 250000;

	private static List<Kind> _kinds;

	public static Result Last { get; private set; }

	public static bool Searching { get; private set; }

	/// <summary>0..1 while searching.</summary>
	public static float Progress { get; private set; }

	public static Kind SearchingFor { get; private set; }

	/// <summary>The marks show on the full map (until it's opened and closed).</summary>
	public static bool Marking { get; private set; }

	private static bool _seenOnMap;

	private static int _searchId;

	/// <summary>A search that ended (in the background), to report on the game thread.</summary>
	private static Result _finished;

	private static bool _fromCommand;

	private static bool _layerAdded;

	/// <summary>Every kind of tile, by type.</summary>
	public static List<Kind> Kinds
	{
		get
		{
			if (_kinds == null)
			{
				_kinds = BuildKinds();
			}
			return _kinds;
		}
	}

	private static List<Kind> BuildKinds()
	{
		// The items that place each tile, by style.
		Dictionary<int, SortedDictionary<int, int>> placers = new Dictionary<int, SortedDictionary<int, int>>();
		foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
		{
			Item item = pair.Value;
			if (item.createTile < 0)
			{
				continue;
			}
			if (!placers.TryGetValue(item.createTile, out SortedDictionary<int, int> styles))
			{
				styles = new SortedDictionary<int, int>();
				placers[item.createTile] = styles;
			}
			if (!styles.ContainsKey(item.placeStyle))
			{
				styles[item.placeStyle] = pair.Key;
			}
		}
		List<Kind> kinds = new List<Kind>
		{
			// Liquids first: they're found the same way, in the water, lava, honey or shimmer of each tile.
			new Kind { Type = -1, Liquid = 0, Name = IsaClient.Languages.L10n.T("Water"), Internal = "Water liquid", Item = ItemID.WaterBucket },
			new Kind { Type = -1, Liquid = 1, Name = IsaClient.Languages.L10n.T("Lava"), Internal = "Lava liquid", Item = ItemID.LavaBucket },
			new Kind { Type = -1, Liquid = 2, Name = IsaClient.Languages.L10n.T("Honey"), Internal = "Honey liquid", Item = ItemID.HoneyBucket },
			new Kind { Type = -1, Liquid = 3, Name = IsaClient.Languages.L10n.T("Shimmer"), Internal = "Shimmer liquid", Item = ItemID.BottomlessShimmerBucket }
		};
		for (int type = 0; type < TileID.Count; type++)
		{
			placers.TryGetValue(type, out SortedDictionary<int, int> styles);
			int itemType = (styles != null && styles.Count > 0) ? styles.Values.First() : 0;
			string name = MapName(type, 0);
			if (string.IsNullOrWhiteSpace(name) && itemType > 0)
			{
				name = Lang.GetItemNameValue(itemType);
			}
			string internalName = TileID.Search.TryGetName(type, out string n) ? n : ("Tile" + type);
			if (string.IsNullOrWhiteSpace(name))
			{
				name = Spaced(internalName);
			}
			Kind all = new Kind { Type = type, Name = name, Internal = internalName, Item = itemType };
			kinds.Add(all);
			List<Kind> variants = Variants(type, internalName, styles, all);
			// One variant only is the tile itself.
			if (variants.Count > 1)
			{
				all.Name = IsaClient.Languages.L10n.F("{0} (any)", name);
				kinds.AddRange(variants);
			}
		}
		// Special ones: what grows as a frame of a plant and no item places.
		Add(kinds, TileID.JunglePlants, 8, ItemID.JungleSpores, (t, x, y) => t.frameX == 144);
		return kinds;
	}

	private static void Add(List<Kind> kinds, int type, int style, int item, Func<Tile, int, int, bool> match)
	{
		if (kinds.Any(k => k.Type == type && k.Style == style))
		{
			return;
		}
		string internalName = TileID.Search.TryGetName(type, out string n) ? n : ("Tile" + type);
		kinds.Add(new Kind { Type = type, Style = style, Name = Lang.GetItemNameValue(item), Internal = internalName, Item = item, Match = match });
	}

	private static string MapName(int type, int option)
	{
		try
		{
			return MapHelper.HasOption(type, option) ? Lang.GetMapObjectName(MapHelper.TileToLookup(type, option)) : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// A type's variants: a style per item that places it (chests, chairs, statues, herbs...) when the tile has its
	/// object data, else the full map's options with their own names.
	/// </summary>
	private static List<Kind> Variants(int type, string internalName, SortedDictionary<int, int> styles, Kind all)
	{
		List<Kind> variants = new List<Kind>();
		TileObjectData data = null;
		try
		{
			data = TileObjectData.GetTileData(type, 0);
		}
		catch
		{
		}
		if (data != null && styles != null && styles.Count > 1)
		{
			foreach (KeyValuePair<int, int> style in styles)
			{
				int wanted = style.Key;
				TileObjectData d = data;
				variants.Add(new Kind
				{
					Type = type,
					Style = wanted,
					Name = Lang.GetItemNameValue(style.Value),
					Internal = internalName,
					Item = style.Value,
					Match = (t, x, y) => StyleOf(d, t) == wanted
				});
			}
			return variants;
		}
		int options = (MapHelper.tileOptionCounts != null && type < MapHelper.tileOptionCounts.Length) ? MapHelper.tileOptionCounts[type] : 0;
		if (options > 1)
		{
			// Options with the same name are one variant.
			Dictionary<string, List<int>> byName = new Dictionary<string, List<int>>();
			List<string> order = new List<string>();
			for (int option = 0; option < options; option++)
			{
				string name = MapName(type, option);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}
				if (!byName.TryGetValue(name, out List<int> list))
				{
					list = new List<int>();
					byName[name] = list;
					order.Add(name);
				}
				list.Add(option);
			}
			if (order.Count > 1)
			{
				foreach (string name in order)
				{
					HashSet<int> wanted = new HashSet<int>(byName[name]);
					int tileType = type;
					variants.Add(new Kind
					{
						Type = type,
						Style = byName[name][0],
						Name = name,
						Internal = internalName,
						Item = all.Item,
						Match = delegate(Tile t, int x, int y)
						{
							int option = 0;
							MapHelper.GetTileBaseOption(x, y, tileType, t, ref option);
							return wanted.Contains(option);
						}
					});
				}
			}
		}
		return variants;
	}

	/// <summary>The style of a placed object from its frame (as tModLoader's TileObjectData.GetTileStyle).</summary>
	private static int StyleOf(TileObjectData data, Tile tile)
	{
		int fullWidth = Math.Max(1, data.CoordinateFullWidth);
		int fullHeight = Math.Max(1, data.CoordinateFullHeight);
		int row = tile.frameY / fullHeight;
		int col = tile.frameX / fullWidth;
		int wrap = Math.Max(1, data.StyleWrapLimit);
		int skip = Math.Max(1, data.StyleLineSkip);
		int sub = data.StyleHorizontal ? (row / skip * wrap + col) : (col / skip * wrap + row);
		return sub / Math.Max(1, data.StyleMultiplier);
	}

	/// <summary>"CopperCoinPile" → "Copper Coin Pile".</summary>
	private static string Spaced(string name)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
			{
				sb.Append(' ');
			}
			sb.Append(name[i]);
		}
		return sb.ToString();
	}

	/// <summary>Kinds whose name, internal name or id match, best first (exact, then starts with, then contains).</summary>
	public static List<Kind> Find(string query)
	{
		query = (query ?? "").Trim();
		if (query.Length == 0)
		{
			return Kinds.ToList();
		}
		string bare = query.TrimStart('#');
		int colon = bare.IndexOf(':');
		if (colon > 0 && int.TryParse(bare.Substring(0, colon), out int typeId) && int.TryParse(bare.Substring(colon + 1), out int styleId))
		{
			return Kinds.Where(k => k.Type == typeId && k.Style == styleId).ToList();
		}
		if (int.TryParse(bare, out int id))
		{
			// The tile, then its variants.
			return Kinds.Where(k => k.Type == id).OrderBy(k => k.Style).ToList();
		}
		string q = query.ToLowerInvariant();
		string squashed = q.Replace(" ", "");
		int Rank(Kind k)
		{
			string name = k.Name.ToLowerInvariant();
			string inner = k.Internal.ToLowerInvariant();
			if (name == q || inner == squashed)
			{
				return 0;
			}
			if (name.StartsWith(q) || inner.StartsWith(squashed))
			{
				return 1;
			}
			if (name.Contains(q) || inner.Contains(squashed))
			{
				return 2;
			}
			return 3;
		}
		return Kinds.Select(k => (k, r: Rank(k))).Where(p => p.r < 3).OrderBy(p => p.r).ThenBy(p => p.k.Name.Length).Select(p => p.k).ToList();
	}

	#region Searching

	/// <summary>Reads the whole world for <paramref name="kind"/> in the background (a new search stops the last one).</summary>
	public static void Start(Kind kind, bool fromCommand = false)
	{
		if (Main.gameMenu || kind == null)
		{
			return;
		}
		int id = ++_searchId;
		Searching = true;
		Progress = 0f;
		SearchingFor = kind;
		_fromCommand = fromCommand;
		int width = Main.maxTilesX;
		int height = Main.maxTilesY;
		Point you = Main.LocalPlayer.Center.ToTileCoordinates();
		float loaded = LoadedPart();
		Task.Run(delegate
		{
			try
			{
				Result result = Scan(kind, width, height, you, id);
				if (result != null && id == _searchId)
				{
					result.Loaded = loaded;
					_finished = result;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				if (id == _searchId)
				{
					Searching = false;
				}
			}
		});
	}

	private static Result Scan(Kind kind, int width, int height, Point you, int id)
	{
		Stopwatch watch = Stopwatch.StartNew();
		Result result = new Result { Kind = kind };
		int cellsX = width / CellSize + 1;
		int kept = 0;
		ushort type = (ushort)kind.Type;
		Func<Tile, int, int, bool> match = kind.Match;
		int liquid = kind.Liquid;
		for (int x = 0; x < width; x++)
		{
			if (id != _searchId)
			{
				return null;
			}
			for (int y = 0; y < height; y++)
			{
				Tile tile = Main.tile[x, y];
				if (tile == null)
				{
					continue;
				}
				if (liquid >= 0)
				{
					if (tile.liquid == 0 || tile.liquidType() != liquid)
					{
						continue;
					}
				}
				else if (!tile.active() || tile.type != type || (match != null && !match(tile, x, y)))
				{
					continue;
				}
				result.Total++;
				int cell = (y / CellSize) * cellsX + x / CellSize;
				result.CellCounts.TryGetValue(cell, out int count);
				result.CellCounts[cell] = count + 1;
				if (kept < MaxKept)
				{
					if (!result.Hits.TryGetValue(cell, out List<Point> list))
					{
						list = new List<Point>();
						result.Hits[cell] = list;
					}
					list.Add(new Point(x, y));
					kept++;
				}
			}
			Progress = (float)(x + 1) / width;
		}
		Group(result, cellsX, you);
		result.Seconds = watch.Elapsed.TotalSeconds;
		return result;
	}

	/// <summary>Touching cells with hits become places.</summary>
	private static void Group(Result result, int cellsX, Point you)
	{
		HashSet<int> left = new HashSet<int>(result.CellCounts.Keys);
		Queue<int> queue = new Queue<int>();
		while (left.Count > 0)
		{
			int first = left.First();
			left.Remove(first);
			queue.Enqueue(first);
			Place place = new Place();
			int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
			while (queue.Count > 0)
			{
				int cell = queue.Dequeue();
				place.Cells.Add(cell);
				place.Count += result.CellCounts[cell];
				int cx = cell % cellsX;
				int cy = cell / cellsX;
				if (result.Hits.TryGetValue(cell, out List<Point> hits))
				{
					foreach (Point p in hits)
					{
						minX = Math.Min(minX, p.X);
						minY = Math.Min(minY, p.Y);
						maxX = Math.Max(maxX, p.X);
						maxY = Math.Max(maxY, p.Y);
					}
				}
				else
				{
					minX = Math.Min(minX, cx * CellSize);
					minY = Math.Min(minY, cy * CellSize);
					maxX = Math.Max(maxX, cx * CellSize + CellSize - 1);
					maxY = Math.Max(maxY, cy * CellSize + CellSize - 1);
				}
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int next = (cy + dy) * cellsX + cx + dx;
						if (cx + dx >= 0 && cx + dx < cellsX && left.Remove(next))
						{
							queue.Enqueue(next);
						}
					}
				}
			}
			place.Bounds = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
			// The hit nearest the middle.
			Point middle = place.Bounds.Center;
			Point spot = middle;
			long best = long.MaxValue;
			foreach (int cell in place.Cells)
			{
				if (!result.Hits.TryGetValue(cell, out List<Point> hits))
				{
					continue;
				}
				foreach (Point p in hits)
				{
					long d = (long)(p.X - middle.X) * (p.X - middle.X) + (long)(p.Y - middle.Y) * (p.Y - middle.Y);
					if (d < best)
					{
						best = d;
						spot = p;
					}
				}
			}
			place.Spot = spot;
			result.Places.Add(place);
		}
		result.Places.Sort((a, b) => Distance(a.Spot, you).CompareTo(Distance(b.Spot, you)));
	}

	public static double Distance(Point a, Point b)
	{
		return Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));
	}

	/// <summary>The part of the world this client has (1 alone).</summary>
	public static float LoadedPart()
	{
		if (Main.netMode != 1 || Main.sectionManager == null)
		{
			return 1f;
		}
		int total = Main.maxSectionsX * Main.maxSectionsY;
		return (total <= 0) ? 1f : 1f - (float)IsaClient.WorldTools.MapTools.DetermineUnloadedSections / total;
	}

	public static void Cancel()
	{
		_searchId++;
		Searching = false;
	}

	/// <summary>Forgets the results and the marks.</summary>
	public static void Clear()
	{
		Cancel();
		Last = null;
		Marking = false;
	}

	#endregion

	/// <summary>Every tick in a world: reports a search that ended, and takes the marks away once the full map was seen and closed.</summary>
	public static void Update()
	{
		if (!_layerAdded)
		{
			_layerAdded = true;
			Main.MapIcons.AddLayer(new MarksLayer());
		}
		if (Main.gameMenu)
		{
			if (Last != null || Searching)
			{
				Clear();
			}
			return;
		}
		Result done = _finished;
		if (done != null)
		{
			_finished = null;
			Searching = false;
			Last = done;
			Marking = done.Total > 0;
			_seenOnMap = false;
			Report(done);
		}
		if (Marking && Main.mapFullscreen)
		{
			_seenOnMap = true;
		}
		else if (Marking && _seenOnMap && !Main.mapFullscreen)
		{
			Marking = false;
			_seenOnMap = false;
		}
	}

	private static void Report(Result r)
	{
		bool chat = _fromCommand;
		_fromCommand = false;
		if (!chat)
		{
			return;
		}
		if (r.Total == 0)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("No {0} ({1}) in the world.", r.Kind.Name, r.Kind.Id));
		}
		else
		{
			Place near = r.Places[0];
			Point you = Main.LocalPlayer.Center.ToTileCoordinates();
			ClientChat.Info(IsaClient.Languages.L10n.F("Found {0} {1} ({2}) in {3} places. Nearest: {4}, {5} ({6} tiles away). Open the map to see them; TP and more in World > Find Tiles.", r.Total.ToString("N0"), r.Kind.Name, r.Kind.Id, r.Places.Count, near.Spot.X, near.Spot.Y, (int)Distance(near.Spot, you)));
		}
		if (r.Loaded < 0.99f)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Only {0}% of the world is loaded here: .revmap loads the rest, then search again.", (int)(r.Loaded * 100f)));
		}
	}

	#region Actions

	/// <summary>Opens the full map on a place, with the marks (the panel closes).</summary>
	public static void ShowOnMap(Point tile)
	{
		if (Main.gameMenu || Last == null)
		{
			return;
		}
		Marking = true;
		_seenOnMap = false;
		IsaClient.Menu.InputState.TildeTerminal = false;
		Main.playerInventory = false;
		Main.LocalPlayer.SetTalkNPC(-1);
		SoundEngine.PlaySound(10);
		Main.mapFullscreen = true;
		Main.resetMapFull = false;
		Main.PanTargetMapFullscreen = false;
		Main.mapFullscreenScale = Math.Max(Main.mapFullscreenScale, 2.5f);
		Main.mapFullscreenPos = new Vector2(tile.X, tile.Y);
	}

	/// <summary>Teleports you standing on top of the found tile (or the nearest free spot above it).</summary>
	public static void TeleportTo(Point tile)
	{
		if (Main.gameMenu)
		{
			return;
		}
		int y = tile.Y;
		for (int up = 0; up < 60 && y > 5; up++, y--)
		{
			if (Free(tile.X, y - 1) && Free(tile.X, y - 2) && Free(tile.X, y - 3))
			{
				break;
			}
		}
		IsaClient.Movement.TeleportTools.TeleportToTile(tile.X, y);
		SoundEngine.PlaySound(SoundID.Item6);
	}

	private static bool Free(int x, int y)
	{
		if (!WorldGen.InWorld(x, y, 1))
		{
			return false;
		}
		Tile t = Main.tile[x, y];
		return t == null || !t.active() || !Main.tileSolid[t.type] || Main.tileSolidTop[t.type];
	}

	/// <summary>.searchtile &lt;id | name&gt; | clear</summary>
	internal static void Command(CommandContext command)
	{
		string arg = command.Rest.Trim();
		if (Main.gameMenu)
		{
			return;
		}
		if (arg.Length == 0)
		{
			ClientChat.Info("Usage: .searchtile <id | name> (or clear). Also in the panel: World > Find Tiles.");
			return;
		}
		if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase) || arg.Equals("off", StringComparison.OrdinalIgnoreCase))
		{
			Clear();
			ClientChat.Info("Tile search cleared.");
			return;
		}
		List<Kind> found = Find(arg);
		if (found.Count == 0)
		{
			ClientChat.Error(IsaClient.Languages.L10n.F("No tile called \"{0}\". Try part of its name or its id.", arg));
			return;
		}
		Kind kind = found[0];
		if (found.Count > 1)
		{
			string others = string.Join(", ", found.Skip(1).Take(5).Select(k => k.Name + " " + k.Id));
			ClientChat.Info(IsaClient.Languages.L10n.F("Searching {0} ({1}). Also matching: {2}", kind.Name, kind.Id, others));
		}
		else
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Searching {0} ({1})...", kind.Name, kind.Id));
		}
		Start(kind, fromCommand: true);
	}

	#endregion

	#region The marks on the map

	/// <summary>Color of the found tiles on the map.</summary>
	public static readonly Color TileColor = new Color(255, 60, 220);

	/// <summary>Color of the rings and frames around places.</summary>
	public static readonly Color RingColor = new Color(60, 255, 230);

	private sealed class MarksLayer : IMapLayer
	{
		public void Draw(ref MapOverlayDrawContext context, ref string text)
		{
			Result r = Last;
			if (!Marking || r == null || !Main.mapFullscreen || context.ClippingRect.HasValue || context.Opacity <= 0f)
			{
				return;
			}
			SpriteBatch sb = Main.spriteBatch;
			Texture2D pixel = TextureAssets.MagicPixel.Value;
			Rectangle pixelSource = new Rectangle(0, 0, 1, 1);
			float scale = context.MapScale;
			Rectangle screen = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
			double now = Main.timeForVisualEffects / 60.0;
			float pulse = 0.65f + 0.35f * (float)Math.Sin(now * 5.0);
			int cellsX = Main.maxTilesX / CellSize + 1;
			// The tiles themselves: each one when they're big enough, else a cell's worth as a soft square.
			foreach (KeyValuePair<int, int> pair in r.CellCounts)
			{
				int cx = pair.Key % cellsX;
				int cy = pair.Key / cellsX;
				Vector2 a = context.ToScreen(new Vector2(cx * CellSize, cy * CellSize));
				float size = CellSize * scale;
				Rectangle cellRect = new Rectangle((int)a.X, (int)a.Y, (int)Math.Ceiling(size), (int)Math.Ceiling(size));
				if (!cellRect.Intersects(screen))
				{
					continue;
				}
				if (scale >= 1f && r.Hits.TryGetValue(pair.Key, out List<Point> hits))
				{
					int px = Math.Max(1, (int)Math.Ceiling(scale));
					foreach (Point p in hits)
					{
						Vector2 at = context.ToScreen(new Vector2(p.X, p.Y));
						sb.Draw(pixel, new Rectangle((int)at.X, (int)at.Y, px, px), pixelSource, TileColor * pulse);
					}
				}
				else
				{
					float density = Math.Min(1f, 0.35f + pair.Value / 40f);
					sb.Draw(pixel, cellRect, pixelSource, TileColor * (density * pulse));
				}
			}
			// The places: a ring that pings when small, a frame when big.
			Texture2D ring = IsaClient.Hub.CountryFlags.Ring;
			Vector2 mouse = Main.MouseScreen;
			for (int i = 0; i < r.Places.Count; i++)
			{
				Place place = r.Places[i];
				Vector2 tl = context.ToScreen(new Vector2(place.Bounds.X, place.Bounds.Y));
				Vector2 br = context.ToScreen(new Vector2(place.Bounds.Right, place.Bounds.Bottom));
				Rectangle box = new Rectangle((int)tl.X, (int)tl.Y, Math.Max(1, (int)(br.X - tl.X)), Math.Max(1, (int)(br.Y - tl.Y)));
				Rectangle near = box;
				near.Inflate(40, 40);
				if (!near.Intersects(screen))
				{
					continue;
				}
				Vector2 center = new Vector2(box.Center.X, box.Center.Y);
				bool small = box.Width < 34 && box.Height < 34;
				if (small)
				{
					float d = 30f;
					sb.Draw(ring, center, null, RingColor * 0.95f, 0f, new Vector2(ring.Width, ring.Height) / 2f, d / ring.Width, SpriteEffects.None, 0f);
					// The ping: a wave going out again and again (places start out of step).
					float t = (float)((now * 0.7 + i * 0.13) % 1.0);
					float wave = 30f + 46f * t;
					sb.Draw(ring, center, null, RingColor * (0.7f * (1f - t)), 0f, new Vector2(ring.Width, ring.Height) / 2f, wave / ring.Width, SpriteEffects.None, 0f);
				}
				else
				{
					Rectangle frame = box;
					frame.Inflate(3, 3);
					Color c = RingColor * 0.9f;
					sb.Draw(pixel, new Rectangle(frame.X, frame.Y, frame.Width, 2), pixelSource, c);
					sb.Draw(pixel, new Rectangle(frame.X, frame.Bottom - 2, frame.Width, 2), pixelSource, c);
					sb.Draw(pixel, new Rectangle(frame.X, frame.Y, 2, frame.Height), pixelSource, c);
					sb.Draw(pixel, new Rectangle(frame.Right - 2, frame.Y, 2, frame.Height), pixelSource, c);
				}
				Rectangle hover = small ? new Rectangle((int)center.X - 16, (int)center.Y - 16, 32, 32) : box;
				if (hover.Contains(mouse.ToPoint()))
				{
					text = IsaClient.Languages.L10n.F("{0} ×{1}  ({2}, {3})", r.Kind.Name, place.Count.ToString("N0"), place.Spot.X, place.Spot.Y);
				}
			}
		}
	}

	#endregion
}
