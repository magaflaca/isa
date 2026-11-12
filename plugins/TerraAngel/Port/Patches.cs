using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Terraria.DataStructures;
using XVector2 = Microsoft.Xna.Framework.Vector2;

namespace TerraAngel;

/// <summary>
/// TerraAngel changes the game's source in many places to make its tools work. Here the same changes are Harmony
/// patches, made when the pack is turned on and removed when it is turned off.
/// Rules, so the pack never breaks the normal client:
/// - the game's own method always runs, except where a tool that is ON needs to replace it (auto aim, TerraAngel chat);
/// - only the local player is touched;
/// - every patch body is guarded: an error turns that patch off (logged) and the game's code runs as usual.
/// TerraAngel's hooks that wrap a game method get the original as "orig": where it is still needed, that original is
/// a Harmony reverse patch (a copy of the method as it was).
/// </summary>
public static partial class Patches
{
    public static void Apply(Harmony harmony)
    {
        Postfix(harmony, AccessTools.Method(typeof(Player), nameof(Player.ResetEffects)), nameof(ResetEffectsPostfix));
        Wrap(harmony, AccessTools.Method(typeof(Player), "ItemCheckWrapped"), nameof(ItemCheckOriginal), nameof(ItemCheckPrefix));
        Postfix(harmony, AccessTools.Method(typeof(Item), nameof(Item.AffixName)), nameof(AffixNamePostfix));
        Postfix(harmony, AccessTools.Method(typeof(Main), nameof(Main.MouseText_DrawItemTooltip_GetLinesInfo)), nameof(GetLinesInfoPostfix));
        Postfix(harmony, AccessTools.Method(typeof(Main), "DoDraw_UpdateCameraPosition"), nameof(UpdateCameraPostfix));

        // No patch on Player.Hurt: it returns a double, and Harmony's x86 glue for that broke the stack (native crash on
        // the next hit). Anti-hurt is IsaClient's God Mode instead (TerraAngelPack.SyncWithClient).
        Prefix(harmony, AccessTools.Method(typeof(Player), nameof(Player.KillMe)), nameof(KillMePrefix));
        Prefix(harmony, AccessTools.Method(typeof(Player), nameof(Player.Teleport)), nameof(TeleportPrefix));
        Postfix(harmony, AccessTools.Method(typeof(Player), nameof(Player.UpdateDead)), nameof(UpdateDeadPostfix));
        Postfix(harmony, AccessTools.Method(typeof(Player), nameof(Player.UpdateEquips)), nameof(UpdateEquipsPostfix));
        Postfix(harmony, AccessTools.Method(typeof(Player), nameof(Player.IsInTileInteractionRange)), nameof(InRangePostfix));
        Postfix(harmony, AccessTools.Method(typeof(TileReachCheckSettings), nameof(TileReachCheckSettings.GetRanges)), nameof(GetRangesPostfix));
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.Update)), new HarmonyMethod(Method(nameof(PlayerUpdatePrefix))), new HarmonyMethod(Method(nameof(PlayerUpdatePostfix))));
        Prefix(harmony, AccessTools.Method(typeof(Dust), nameof(Dust.NewDust)), nameof(NewDustPrefix));
        Prefix(harmony, AccessTools.Method(typeof(Gore), nameof(Gore.NewGore), new[] { typeof(XVector2), typeof(XVector2), typeof(int), typeof(float) }), nameof(NewGorePrefix));
        Prefix(harmony, AccessTools.Method(typeof(WorldGen), nameof(WorldGen.TileFrame)), nameof(TileFramePrefix));
        Postfix(harmony, AccessTools.Method(typeof(Projectile), "FishingCheck_RollItemDrop"), nameof(FishingPostfix));
        ApplyChat(harmony);
        ApplyNet(harmony);
        ApplyBuilder(harmony);
    }

    private static MethodInfo Method(string name) => typeof(Patches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    private static void Wrap(Harmony harmony, MethodBase original, string stub, string prefix)
    {
        harmony.CreateReversePatcher(original, new HarmonyMethod(Method(stub))).Patch();
        harmony.Patch(original, prefix: new HarmonyMethod(Method(prefix)));
    }

    private static void Prefix(Harmony harmony, MethodBase original, string prefix) => harmony.Patch(original, prefix: new HarmonyMethod(Method(prefix)));

    private static void Postfix(Harmony harmony, MethodBase original, string postfix) => harmony.Patch(original, postfix: new HarmonyMethod(Method(postfix)));

    private static bool Local(Player player) => player.whoAmI == Main.myPlayer && !Main.gameMenu;

    // ------------------------------------------------------------------ guards

    private static readonly HashSet<string> _failed = new HashSet<string>();

    /// <summary>Runs a prefix body; on an error the patch is turned off and the original method runs.</summary>
    private static bool Guard(string name, Func<bool> body)
    {
        if (_failed.Contains(name))
        {
            return true;
        }
        try
        {
            return body();
        }
        catch (Exception ex)
        {
            Fail(name, ex);
            return true;
        }
    }

    private static void Guard(string name, Action body)
    {
        if (_failed.Contains(name))
        {
            return;
        }
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Fail(name, ex);
        }
    }

    private static void Fail(string name, Exception ex)
    {
        _failed.Add(name);
        IsaClient.Plugins.PluginLog.Error("TerraAngel patch " + name + " was turned off after an error: " + ex.Message, ex);
    }

    // ------------------------------------------------------------------ hooks that add to the game's method

    private static void ResetEffectsPostfix(Player __instance)
    {
        if (!Local(__instance))
        {
            return;
        }
        // TerraAngel's hook runs the original first and then its own part: the original already ran.
        Guard("ResetEffects", () => PlayerHooks.PlayerResetEffectsHook(_ => { }, __instance));
    }

    /// <summary>
    /// Items that are not really items: the client's tooltips (credits, thanks...) are a stand-in item with its own
    /// name, which must not get TerraAngel's "[Name/id]" (IsaClient's Show IDs skips them the same way).
    /// </summary>
    private static bool IsDisplayOnly(Item item) => item == null || item.NameOverride != null;

    private static void AffixNamePostfix(Item __instance, ref string __result)
    {
        if (!ClientConfig.Settings.ShowDetailedTooltips || IsDisplayOnly(__instance))
        {
            return;
        }
        string original = __result;
        string name = original;
        Guard("AffixName", () => name = DrawHooks.AffixNameHook(_ => original, __instance));
        __result = name;
    }

    private static void GetLinesInfoPostfix(Item item, ref int numLines, string[] toolTipLine, Color[] lineColors)
    {
        if (!ClientConfig.Settings.ShowDetailedTooltips || IsDisplayOnly(item))
        {
            return;
        }
        int lines = numLines;
        Guard("GetLinesInfo", () =>
        {
            int yoyo = 0;
            DrawHooks.GetLinesInfoHook(NoLines, item, ref yoyo, 0f, ref lines, toolTipLine, lineColors);
        });
        numLines = lines;
    }

    private static void NoLines(Item item, ref int yoyoLogo, float oldKB, ref int numLines, string[] toolTipLine, Color[] lineColors)
    {
    }

    // ------------------------------------------------------------------ hooks that replace the method while a tool is on

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ItemCheckOriginal(Player self, int i) => throw new NotImplementedException("reverse patch");

    /// <summary>Auto aim moves the mouse around the local player's item use.</summary>
    private static bool ItemCheckPrefix(Player __instance, int i)
    {
        if (__instance.whoAmI != Main.myPlayer || Main.gameMenu || !ToolManager.GetTool<AutoAimTool>().Enabled)
        {
            return true;
        }
        return Guard("ItemCheck", () =>
        {
            PlayerHooks.PlayerItemCheckHook(ItemCheckOriginal, __instance, i);
            return false;
        });
    }

    /// <summary>TerraAngel's Freecam (TerraAngelPack.SyncFreecam moves it).</summary>
    public static bool FreecamOn;

    public static XVector2 FreecamPosition;

    /// <summary>The game placed the camera on the player: while Freecam is on it goes back where the player left it.</summary>
    private static void UpdateCameraPostfix()
    {
        if (!FreecamOn || Main.gameMenu)
        {
            return;
        }
        Guard("UpdateCamera", () =>
        {
            Main.screenPosition = new XVector2((int)FreecamPosition.X, (int)FreecamPosition.Y);
            Main.ClampScreenPositionToWorld();
            FreecamPosition = Main.screenPosition;
        });
    }

    // ------------------------------------------------------------------ small changes

    private static bool KillMePrefix(Player __instance)
    {
        if (!Local(__instance))
        {
            return true;
        }
        return Guard("KillMe", () =>
        {
            if (!ToolManager.GetTool<AntiHurtTool>().Enabled)
            {
                return true;
            }
            NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, __instance.whoAmI);
            return false;
        });
    }

    private static bool TeleportPrefix(Player __instance, XVector2 newPos, int Style, int extraInfo)
    {
        if (!Local(__instance))
        {
            return true;
        }
        return Guard("Teleport", () =>
        {
            TeleportControlTool control = ToolManager.GetTool<TeleportControlTool>();
            if (control.LogTargetPosition)
            {
                ClientLoader.Console.WriteLine(GetString($"Teleported to {newPos} (Style: {Style}, ExtraInfo: {extraInfo})"));
            }
            return control.AllowTeleport;
        });
    }

    private static void UpdateDeadPostfix(Player __instance)
    {
        if (__instance.whoAmI != Main.myPlayer)
        {
            return;
        }
        Guard("UpdateDead", () =>
        {
            if (ToolManager.GetTool<InstantRespawnTool>().Enabled)
            {
                __instance.respawnTimer = 0;
            }
        });
    }

    /// <summary>Infinite flight: what the Soaring Insignia does (TerraAngel checks the same item).</summary>
    private static void UpdateEquipsPostfix(Player __instance)
    {
        if (!Local(__instance))
        {
            return;
        }
        Guard("UpdateEquips", () =>
        {
            if (ToolManager.GetTool<InfiniteFlightTool>().Enabled)
            {
                __instance.empressBrooch = true;
            }
        });
    }

    private static void InRangePostfix(Player __instance, ref bool __result)
    {
        if (__result || __instance.whoAmI != Main.myPlayer)
        {
            return;
        }
        bool inRange = false;
        Guard("InRange", () => inRange = ToolManager.GetTool<InfiniteReachTool>().Enabled || BuilderModeTool.Enabled);
        __result = inRange;
    }

    private static void GetRangesPostfix(ref int x, ref int y)
    {
        bool infinite = false;
        Guard("GetRanges", () => infinite = ToolManager.GetTool<InfiniteReachTool>().Enabled);
        if (infinite)
        {
            x = Main.screenWidth / 32 + 8;
            y = Main.screenHeight / 32 + 8;
        }
    }

    /// <summary>No-clip: TerraAngel moves the player itself; the game's movement and collisions are undone after.</summary>
    private static void PlayerUpdatePrefix(Player __instance, int i, out XVector2? __state)
    {
        XVector2? state = null;
        if (i == Main.myPlayer && !Main.gameMenu)
        {
            Guard("NoClip", () => state = ToolManager.GetTool<NoClipTool>().Enabled ? __instance.position : null);
        }
        __state = state;
    }

    private static void PlayerUpdatePostfix(Player __instance, XVector2? __state)
    {
        if (__state.HasValue)
        {
            __instance.position = __state.Value;
            __instance.velocity = XVector2.Zero;
            __instance.fallStart = (int)(__instance.position.Y / 16f);
        }
    }

    private static bool NewDustPrefix(ref int __result)
    {
        if (OptimizationTool.OptimizationToolCache?.DisableDust ?? false)
        {
            __result = Main.maxDust;
            return false;
        }
        return true;
    }

    private static bool NewGorePrefix(ref int __result)
    {
        if (OptimizationTool.OptimizationToolCache?.DisableGore ?? false)
        {
            __result = Main.maxGore;
            return false;
        }
        return true;
    }

    private static bool TileFramePrefix() => !DisableFramingTool.FramingDisabledCache;

    private static void FishingPostfix(Projectile __instance, ref FishingAttempt fisher)
    {
        if (__instance.owner != Main.myPlayer)
        {
            return;
        }
        FishingAttempt attempt = fisher;
        Guard("Fishing", () => ToolManager.GetTool<AutoFishTool>().FishingCheck(__instance, attempt));
    }
}
