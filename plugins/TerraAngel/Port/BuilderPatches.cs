using HarmonyLib;
using Terraria.DataStructures;

namespace TerraAngel;

/// <summary>
/// Builder Mode (Ctrl+B), as TerraAngel's game source does it: blocks and walls go anywhere (in the air, without
/// anything next to them), one per frame while the button is held, and the placed item isn't used up. Reach was
/// already infinite (InRangePostfix); Quick Kill (right mouse) and Quick Slope (Q) are TerraAngel's own tools.
/// The "anywhere" part is IsaClient's own Place Anywhere, on only while the game places the item (so the client's
/// setting is never changed or saved).
/// </summary>
public static partial class Patches
{
    private static void ApplyBuilder(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.PlaceThing)), new HarmonyMethod(Method(nameof(PlaceThingPrefix))), new HarmonyMethod(Method(nameof(PlaceThingPostfix))));
        Prefix(harmony, AccessTools.Method(typeof(Player), "PlaceThing_Walls"), nameof(PlaceWallsPrefix));
    }

    private struct BuilderState
    {
        public bool On;

        public bool PlaceAnywhere;

        public int Slot;

        public int Type;

        public int Stack;

        public int Prefix;
    }

    private static bool BuilderOn(Player player)
    {
        bool on = false;
        if (Local(player))
        {
            Guard("BuilderMode", () => on = BuilderModeTool.Enabled);
        }
        return on;
    }

    private static void PlaceThingPrefix(Player __instance, out BuilderState __state)
    {
        BuilderState state = default;
        if (BuilderOn(__instance))
        {
            Guard("BuilderPlace", () =>
            {
                Item item = __instance.inventory[__instance.selectedItem];
                state.On = true;
                state.PlaceAnywhere = IsaClient.Building.Placement.PlaceAnywhere;
                state.Slot = __instance.selectedItem;
                state.Type = item.type;
                state.Stack = item.stack;
                state.Prefix = item.prefix;
                IsaClient.Building.Placement.PlaceAnywhere = true;
                // one block per frame while the button is held, like TerraAngel
                if ((item.createTile >= 0 || item.createWall >= 0) && __instance.controlUseItem && __instance.itemAnimation > 0)
                {
                    __instance.itemTime = 0;
                }
            });
        }
        __state = state;
    }

    private static void PlaceThingPostfix(Player __instance, BuilderState __state)
    {
        if (!__state.On)
        {
            return;
        }
        IsaClient.Building.Placement.PlaceAnywhere = __state.PlaceAnywhere;
        Guard("BuilderKeep", () =>
        {
            // what was placed stays in the hand
            if (__state.Slot < 0 || __state.Slot >= __instance.inventory.Length || __state.Type <= 0)
            {
                return;
            }
            Item item = __instance.inventory[__state.Slot];
            if (item.IsAir)
            {
                item.SetDefaults(__state.Type);
                item.Prefix(__state.Prefix);
                item.stack = __state.Stack;
            }
            else if (item.type == __state.Type && item.stack < __state.Stack)
            {
                item.stack = __state.Stack;
            }
        });
    }

    /// <summary>Walls in the air: the game wants a block or a wall next to it; Builder Mode doesn't.</summary>
    private static bool PlaceWallsPrefix(Player __instance)
    {
        if (!BuilderOn(__instance))
        {
            return true;
        }
        return Guard("BuilderWalls", () =>
        {
            Item item = __instance.inventory[__instance.selectedItem];
            int x = Player.tileTargetX;
            int y = Player.tileTargetY;
            if (item.createWall < 0 || !WorldGen.InWorld(x, y, 2))
            {
                return true;
            }
            Tile tile = Main.tile[x, y];
            if (tile == null || tile.wall != 0)
            {
                // replacing a wall: the game's own code (with the reach and item time Builder Mode already gives)
                return true;
            }
            __instance.cursorItemIconEnabled = true;
            if (!__instance.controlUseItem || __instance.itemAnimation <= 0)
            {
                return false;
            }
            WorldGen.PlaceWall(x, y, item.createWall);
            if (tile.wall == item.createWall && Main.netMode == 1)
            {
                NetMessage.SendData(17, -1, -1, null, 3, x, y, item.createWall);
            }
            return false;
        });
    }
}
