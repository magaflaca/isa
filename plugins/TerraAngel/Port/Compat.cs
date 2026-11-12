using System.IO;
using System.Reflection;
using HarmonyLib;
using TerraAngel.Graphics;
using TerraAngel.Vectors;
using TerraAngel.WorldEdits;
using Terraria.Graphics.Light;
using Terraria.UI.Chat;

namespace TerraAngel;

// What TerraAngel's own game patches added (or made public) and its code uses, rebuilt on the original game: extension
// members with the same names, private members reached through Harmony's AccessTools.

/// <summary>TerraAngel's ClientLoader members that belong to the pack (its folders, its renderer's world edits, its chat).</summary>
public static class ClientLoaderCompat
{
    private static string _assetPath;

    extension(ClientLoader)
    {
        /// <summary>Plugins/Data/TerraAngel: the pack's config and fonts.</summary>
        public static string DataPath => ClientLoader.PluginDataPath("TerraAngel");

        public static string ConfigPath => Path.Combine(ClientLoader.DataPath, "clientConfig.json");

        public static string WindowConfigPath => Path.Combine(ClientLoader.DataPath, "clientWindowConfig.json");

        /// <summary>The fonts TerraAngel ships, written from the plugin to Plugins/Data/TerraAngel/Assets the first time.</summary>
        public static string AssetPath => _assetPath ??= ExtractAssets();

        public static ChatWindowCompat ChatWindow => ChatWindowCompat.Instance;
    }

    private static string ExtractAssets()
    {
        string folder = Path.Combine(ClientLoader.DataPath, "Assets");
        Directory.CreateDirectory(folder);
        Assembly assembly = typeof(ClientLoaderCompat).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            const string prefix = "TerraAngel.Assets.";
            if (!resource.StartsWith(prefix))
            {
                continue;
            }
            string path = Path.Combine(folder, resource.Substring(prefix.Length));
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            if (File.Exists(path) && new FileInfo(path).Length == stream.Length)
            {
                continue;
            }
            using FileStream file = File.Create(path);
            stream.CopyTo(file);
        }
        return folder;
    }
}

/// <summary>TerraAngel had its own chat window; here the game's chat is the one that is "chatting".</summary>
public sealed class ChatWindowCompat
{
    public static readonly ChatWindowCompat Instance = new ChatWindowCompat();

    public bool IsChatting => Main.drawingPlayerChat;
}

/// <summary>The world edits TerraAngel's renderer kept (brush, copy/paste, pixel art) and which one is in use.</summary>
public static class ClientRendererCompat
{
    private static readonly List<WorldEdit> _worldEdits = new List<WorldEdit>();

    private static int _currentWorldEditIndex = -1;

    extension(ClientRenderer renderer)
    {
        public List<WorldEdit> WorldEdits => _worldEdits;

        public int CurrentWorldEditIndex
        {
            get => _currentWorldEditIndex;
            set => _currentWorldEditIndex = value;
        }

        public WorldEdit? CurrentWorldEdit => (_currentWorldEditIndex >= 0 && _currentWorldEditIndex < _worldEdits.Count) ? _worldEdits[_currentWorldEditIndex] : null;
    }
}

/// <summary>Game members TerraAngel made public or added, by the names its code uses.</summary>
public static class GameCompat
{
    private static readonly AccessTools.FieldRef<Player, Microsoft.Xna.Framework.Vector2?> _netCameraTarget = AccessTools.FieldRefAccess<Player, Microsoft.Xna.Framework.Vector2?>("netCameraTarget");

    private static readonly MethodInfo _fishingGetBait = AccessTools.Method(typeof(Player), "Fishing_GetBait");

    private static readonly FieldInfo _activeEngine = AccessTools.Field(typeof(Lighting), "_activeEngine");

    private static readonly FieldInfo _newEngine = AccessTools.Field(typeof(Lighting), "NewEngine");

    private static readonly FullbrightEngine _fullbright = new FullbrightEngine();

    extension(Player player)
    {
        public Microsoft.Xna.Framework.Vector2? netCameraTarget
        {
            get => _netCameraTarget(player);
            set => _netCameraTarget(player) = value;
        }

        /// <summary>Players running TerraAngel (their presence mark, read by IsaClient: Player.terraAngelDetected).</summary>
        public bool TerraAngelUser => player.terraAngelDetected;

        public void Fishing_GetBait(out int baitPower, out int baitType)
        {
            object[] args = { 0, 0 };
            _fishingGetBait.Invoke(player, args);
            baitPower = (int)args[0];
            baitType = (int)args[1];
        }
    }

    extension(NPC npc)
    {
        public string FullNameDefault => npc.FullName;
    }

    extension(Lighting)
    {
        public static ILightingEngine _activeEngine
        {
            get => (ILightingEngine)GameCompat._activeEngine.GetValue(null);
            set => GameCompat._activeEngine.SetValue(null, value);
        }

        public static LightingEngine NewEngine => (LightingEngine)_newEngine.GetValue(null);

        public static FullbrightEngine FullbrightEngine => _fullbright;
    }

    extension(MessageID)
    {
        public static byte PaintTile => MessageID.SyncTilePaintOrCoating;

        public static byte PaintWall => MessageID.SyncWallPaintOrCoating;

        public static byte UpdateSign => 47;
    }

    extension(Tile[,] map)
    {
        public bool InWorld(Vector2i position) => map.InWorld(position.X, position.Y);

        public Tile this_Get(Vector2i position) => map[position.X, position.Y];
    }

    /// <summary>Sets a property whose setter is private (TerraAngel made them public).</summary>
    public static void SetPrivate(object target, string property, object value)
    {
        AccessTools.Property(target.GetType(), property).SetValue(target, value);
    }
}

/// <summary>The game's item tag in the chat ([i:...]) is a private class: recognized and read by reflection.</summary>
public static class ItemSnippetAccess
{
    private static readonly System.Type _type = AccessTools.Inner(typeof(Terraria.GameContent.UI.Chat.ItemTagHandler), "ItemSnippet");

    private static readonly FieldInfo _item = AccessTools.Field(_type, "_item");

    public static bool Is(TextSnippet snippet) => snippet != null && snippet.GetType() == _type;

    public static Item ItemOf(TextSnippet snippet) => (Item)_item.GetValue(snippet);
}
