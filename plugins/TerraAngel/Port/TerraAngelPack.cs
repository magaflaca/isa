using TerraAngel.WorldEdits;
using System.Reflection;
using IsaClient.Plugins;
using TerraAngel.Inspector.Tools;
using TerraAngel.Tools.Inspector;
using TerraAngel.UI.ClientWindows;
using Terraria.GameContent;

namespace TerraAngel;

/// <summary>
/// TerraAngel inside IsaClient: registers its tools and windows, loads its fonts and settings, applies its changes to
/// the game (Harmony, see Patches) and does each frame what TerraAngel's renderer did.
/// </summary>
public class TerraAngelPack : ClientPlugin
{
#if ISA_FORK
    // Built by IsaClient from a TerraAngel fork (Plugins/Forks): its name shows which one.
    public override string Name => "TerraAngel (" + ForkBuild.Name + ")";
#else
    public override string Name => "TerraAngel";
#endif

    public override string Author => "CEO-Chair and contributors, ported to IsaClient";

    public override string Version => "1.4.5.8";

    public override string Description => "TerraAngel's tools and windows: inspector, item and NPC browsers, item editor, ESP, freecam, auto aim/fish, butcher, projectile prediction, world teleports and more. Its Main Window opens with the overlay (Insert).";

    public override bool HasOwnToolWindow => true;

    private static bool _fontsLoaded;

    public override void Load()
    {
        // The player's own TerraAngel settings, presets and weapon setups (TerraAngelImport).
        TerraAngelImport.OnLoad();
        ClientConfig.ReadFromFile();
        TileUtil.Init();
        foreach (Type type in typeof(TerraAngelPack).Assembly.GetTypes())
        {
            if (!type.IsAbstract && type.IsSubclassOf(typeof(Tool)) && type.GetConstructor(Type.EmptyTypes) != null)
            {
                ToolManager.AddTool(type);
                ClientConfig.SetDefaultCringeValues(ToolManager.GetTool(type));
            }
        }
        ClientRenderer renderer = ClientLoader.MainRenderer;
        renderer.AddWindow(new MainWindow());
        renderer.AddWindow(new DrawWindow());
        renderer.AddWindow(new StatsWindow());
        renderer.AddWindow(InspectorTool.InspectorWindow);
        renderer.AddWindow(new StyleEditorWindow());
        renderer.AddWindow(new TimingMetricsWindow());
        renderer.AddWindow(NetMessageWindow.Instance);
        renderer.AddWindow(new ChatWindow());
        // World edits (Main Window > World Edit): brush, copy/paste and pixel art, like TerraAngel's renderer had them.
        renderer.WorldEdits.Clear();
        renderer.WorldEdits.Add(new WorldEditBrush());
        renderer.WorldEdits.Add(new WorldEditCopyPaste());
        renderer.WorldEdits.Add(new WorldEditPixelArt());
        renderer.CurrentWorldEditIndex = -1;
        if (!_fontsLoaded)
        {
            // Fonts go into ImGui's atlas between frames.
            renderer.EnqueuePreDrawAction(() =>
            {
                ClientAssets.LoadFonts(ImGui.GetIO());
                renderer.RebuildFontAtlas();
                _fontsLoaded = true;
            });
        }
        Patches.Apply(Harmony);
        ApplyAttributePatches();
        // "TerraAngel Settings" and "TerraAngel Plugins" on the title screen (option Main Menu Buttons).
        TerraAngel.UI.TerrariaUI.MenuScreens.Add();
        // .clearchat empties TerraAngel's chat window too.
        IsaClient.Community.ChatTools.Cleared += ClearChatWindow;
#if ISA_FORK
        AddForkWindows();
#endif
        ClientConfig.AfterReadLater();
    }

    /// <summary>
    /// Classes marked [HarmonyPatch] (the pack has none; a fork's changes to game code go there) are applied; one that
    /// fails is reported in the console and the others still apply.
    /// </summary>
    private void ApplyAttributePatches()
    {
        foreach (Type type in HarmonyLib.AccessTools.GetTypesFromAssembly(typeof(TerraAngelPack).Assembly))
        {
            if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), inherit: false).Length == 0)
            {
                continue;
            }
            try
            {
                Harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                ClientLoader.Console.WriteError("Patch " + type.FullName + " failed: " + (ex.InnerException ?? ex).Message);
            }
        }
    }

#if ISA_FORK
    /// <summary>Windows a fork added (ForkBuild.NewWindows: classes that inherit ClientWindow in its new files).</summary>
    private static void AddForkWindows()
    {
        foreach (string name in ForkBuild.NewWindows)
        {
            Type? type = typeof(TerraAngelPack).Assembly.GetTypes().FirstOrDefault(t => t.Name == name && typeof(ClientWindow).IsAssignableFrom(t) && !t.IsAbstract);
            if (type == null || type.GetConstructor(Type.EmptyTypes) == null || ClientLoader.MainRenderer.ClientWindows.Any(w => w.GetType() == type))
            {
                continue;
            }
            try
            {
                ClientLoader.MainRenderer.AddWindow((ClientWindow)Activator.CreateInstance(type)!);
            }
            catch (Exception ex)
            {
                ClientLoader.Console.WriteError("Window " + name + " failed: " + (ex.InnerException ?? ex).Message);
            }
        }
    }
#endif

    private static void ClearChatWindow()
    {
        TerraAngel.UI.ClientWindows.ChatWindow.Instance?.ChatItems.Clear();
    }

    public override void Unload()
    {
        TerraAngel.UI.TerrariaUI.MenuScreens.Remove();
        IsaClient.Community.ChatTools.Cleared -= ClearChatWindow;
        ClientLoader.Chat.Replacement = null;
        ClientLoader.MainRenderer.CurrentWorldEditIndex = -1;
        ClientLoader.MainRenderer.WorldEdits.Clear();
        ChatWindow.Instance?.Dispose();
        Patches.FreecamOn = false;
        _freecam = false;
        // What the pack turned on in the client goes back off with it.
        if (_antiHurt)
        {
            IsaClient.Gameplay.PlayerCheats.GodMode = false;
            _antiHurt = false;
        }
        if (_fullBright)
        {
            IsaClient.Rendering.FullbrightLighting.Set(0f, save: false);
            _fullBright = false;
            _brightness = -1f;
        }
        if (_journey)
        {
            IsaClient.Gameplay.PlayerCheats.JourneyMenu = false;
            _journey = false;
        }
        ClientConfig.WriteToFile();
    }

    private bool _fullBright;

    private float _brightness = -1f;

    private bool _journey;

    private bool _antiHurt;

    private bool _freecam;

    private bool _dragging;

    private Microsoft.Xna.Framework.Vector2 _dragFrom;

    public override void Update()
    {
        Time.UpdateUpdate();
        ClientConfig.Update();
        SyncWithClient();
    }

    /// <summary>
    /// Tools IsaClient already has are driven by the client's own code: TerraAngel's Full Bright sets IsaClient's
    /// fullbright, its Journey Menu IsaClient's Journey menu anywhere, its Anti-hurt IsaClient's God Mode (only when the
    /// TerraAngel switch changes).
    /// </summary>
    private void SyncWithClient()
    {
        LightingModifierTool lighting = ToolManager.GetTool<LightingModifierTool>();
        if (lighting.FullBright != _fullBright || (lighting.FullBright && lighting.Brightness != _brightness))
        {
            _fullBright = lighting.FullBright;
            _brightness = lighting.Brightness;
            IsaClient.Rendering.FullbrightLighting.Set(_fullBright ? _brightness : 0f, save: false);
        }
        JourneyMenuTool journey = ToolManager.GetTool<JourneyMenuTool>();
        if (journey.Enabled != _journey)
        {
            _journey = journey.Enabled;
            IsaClient.Gameplay.PlayerCheats.JourneyMenu = _journey;
        }
        AntiHurtTool antiHurt = ToolManager.GetTool<AntiHurtTool>();
        if (antiHurt.Enabled != _antiHurt)
        {
            _antiHurt = antiHurt.Enabled;
            IsaClient.Gameplay.PlayerCheats.GodMode = _antiHurt;
        }
        SyncFreecam();
        ClientLoader.Chat.Replacement = ChatWindow.On ? ChatWindow.Instance : null;
    }

    /// <summary>
    /// TerraAngel's Freecam: the view stays where it is and is dragged with the right mouse button, while the player
    /// keeps playing (IsaClient's own free camera, .freecam, moves with the keys and holds the player instead).
    /// The camera position is applied after the game's camera update (Patches.UpdateCameraPostfix).
    /// </summary>
    private void SyncFreecam()
    {
        FreecamTool freecam = ToolManager.GetTool<FreecamTool>();
        bool on = freecam.Enabled && !Main.gameMenu;
        if (on && !_freecam)
        {
            Patches.FreecamPosition = Main.screenPosition;
        }
        _freecam = on;
        Patches.FreecamOn = on;
        if (!on || Main.mapFullscreen)
        {
            _dragging = false;
            return;
        }
        Microsoft.Xna.Framework.Vector2 mouse = new Microsoft.Xna.Framework.Vector2(InputSystem.MouseState.X, InputSystem.MouseState.Y);
        if (InputSystem.RightMousePressed && !ImGui.GetIO().WantCaptureMouse)
        {
            _dragging = true;
            _dragFrom = mouse;
        }
        if (!InputSystem.RightMouseDown)
        {
            _dragging = false;
        }
        if (_dragging)
        {
            // The right button moves the view, it doesn't use items or open chests meanwhile.
            IsaClient.Plugins.PluginHost.CaptureGameMouse = true;
            Patches.FreecamPosition -= (mouse - _dragFrom) / Main.GameViewMatrix.Zoom.X;
            _dragFrom = mouse;
        }
    }

    public override void BeforeGameDraw()
    {
        // The NPC inspector draws the NPC into its own texture (TerraAngel did it before the game drew too).
        if (_fontsLoaded)
        {
            ToolManager.GetTool<NPCInspectorTool>().PreDraw();
            // The copied section and the pixel art preview are drawn into their textures before the game draws.
            foreach (WorldEdit worldEdit in ClientLoader.MainRenderer.WorldEdits)
            {
                if (worldEdit is WorldEditCopyPaste copyPaste)
                {
                    copyPaste.PreUpdate();
                }
                else if (worldEdit is WorldEditPixelArt pixelArt)
                {
                    pixelArt.PreUpdate();
                }
            }
        }
    }

    public override void BeforeImGuiFrame()
    {
        Time.UpdateDraw();
        InputSystem.EndUpdateInput();
        InputSystem.UpdateInput();
    }

    public override void AfterImGuiFrame()
    {
        // Item and NPC pictures the windows asked for, a few per frame.
        for (int i = 0; i < 10 && ImGuiUtil.ItemIdsToLoad.Count > 0; i++)
        {
            int id = ImGuiUtil.ItemIdsToLoad.Dequeue();
            Main.instance.LoadItem(id);
            ImGuiUtil.ItemImages[id] = ClientLoader.MainRenderer.BindTexture(TextureAssets.Item[id].Value);
        }
        for (int i = 0; i < 10 && ImGuiUtil.NPCIdsToLoad.Count > 0; i++)
        {
            int id = ImGuiUtil.NPCIdsToLoad.Dequeue();
            if (id < 0)
            {
                continue;
            }
            Main.instance.LoadNPC(id);
            ImGuiUtil.NPCImages[id] = ClientLoader.MainRenderer.BindTexture(TextureAssets.Npc[id].Value);
        }
    }
}
