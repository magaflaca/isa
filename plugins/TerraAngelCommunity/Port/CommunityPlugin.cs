using System.Reflection;
using HarmonyLib;
using IsaClient.Plugins;
using TerraAngel.Graphics;
using TerraAngel.Tools.Developer;

namespace TerraAngel;

/// <summary>
/// Additions people made to their TerraAngel (forks), on top of the TerraAngel pack: the Player Editor (stats, looks,
/// presets with portraits), an Item Editor with saved weapon setups and multishot, a Buff Browser, and the game changes
/// they need. Their code is in Source/ as they wrote it.
/// </summary>
public class CommunityPlugin : ClientPlugin
{
    public override string Name => "TerraAngel Community";

    public override string Author => "TerraAngel fork authors, ported to IsaClient";

    public override string Version => "1.0";

    public override string Description => "Player Editor (stats, looks, presets), Item Editor with saved weapon setups and multishot, Buff Browser. Needs the TerraAngel pack; its tools are in TerraAngel's Main Window.";

    public override bool HasOwnToolWindow => true;

    private static ItemEditorTool _itemEditor;

    public override void Load()
    {
        ToolManager.AddTool<PlayerEditorTool>();
        ToolManager.AddTool<BuffBrowserTool>();
        // The fork's Item Editor replaces TerraAngel's in the Items tab (same name, more features).
        _itemEditor = new ItemEditorTool();
        Type packEditor = typeof(TerraAngelPack).Assembly.GetType("TerraAngel.Tools.Developer.ItemEditorTool");
        if (packEditor != null)
        {
            Harmony.Patch(AccessTools.Method(packEditor, "DrawUI"), prefix: new HarmonyMethod(typeof(CommunityPlugin).GetMethod(nameof(ItemEditorPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        }
        CommunityPatches.Apply(Harmony);
    }

    private static bool ItemEditorPrefix(ImGuiIOPtr io)
    {
        try
        {
            _itemEditor.DrawUI(io);
        }
        catch (Exception ex)
        {
            PluginLog.ErrorOnce("Community Item Editor: " + ex.Message, ex);
            return true;
        }
        return false;
    }

    public override void Update()
    {
        if (!Main.gameMenu)
        {
            CommunityPatches.SyncGhost(Main.LocalPlayer);
        }
    }

    public override void BeforeGameDraw()
    {
        PlayerPortraitCache.Render();
    }
}
