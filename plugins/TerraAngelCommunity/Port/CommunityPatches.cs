using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Terraria.GameInput;
using TerraAngel.Tools.Developer;
using XVector2 = Microsoft.Xna.Framework.Vector2;

namespace TerraAngel;

/// <summary>
/// What the fork changed in Player.cs, as Harmony patches (only for the local player, every body guarded):
/// - stats the Player Editor sets (Mod* fields): each one follows the game's value (Natural*) until it is edited in the
///   editor, then the edited value is kept until "Reset to Base Values" (the fork overwrote them always);
/// - life regen, applied where the game computes it;
/// - weapons that shoot several times (Item Editor, WeaponConfigManager.ItemTimesToShoot), with a small spread;
/// - Ghost / Functional Ghost: IsaClient's ghost (.ghost), see SyncGhost (the fork only copied the controls).
/// </summary>
public static class CommunityPatches
{
    public static void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.Update)), transpiler: new HarmonyMethod(Method(nameof(UpdateTranspiler))));
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.UpdateLifeRegen)), prefix: new HarmonyMethod(Method(nameof(LifeRegenPrefix))));
        MethodInfo shoot = AccessTools.Method(typeof(Player), "ItemCheck_Shoot");
        harmony.CreateReversePatcher(shoot, new HarmonyMethod(Method(nameof(ShootOriginal)))).Patch();
        harmony.Patch(shoot, postfix: new HarmonyMethod(Method(nameof(ShootPostfix))));
    }

    private static MethodInfo Method(string name) => typeof(CommunityPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    private static bool _off;

    private static readonly AccessTools.FieldRef<Player, bool> _shimmerImmune = AccessTools.FieldRefAccess<Player, bool>("shimmerImmune");

    private static void Guard(Action body)
    {
        if (_off)
        {
            return;
        }
        try
        {
            body();
        }
        catch (Exception ex)
        {
            _off = true;
            IsaClient.Plugins.PluginLog.Error("TerraAngel Community patches were turned off after an error: " + ex.Message, ex);
        }
    }

    // ------------------------------------------------------------------ stats

    /// <summary>Calls ApplyStats right after the game stores lastTileRangeX (after armor halving, before movement).</summary>
    private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo marker = AccessTools.Field(typeof(Player), nameof(Player.lastTileRangeX));
        bool done = false;
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;
            if (!done && instruction.StoresField(marker))
            {
                done = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, Method(nameof(ApplyStats)));
            }
        }
    }

    // What was last copied into each Mod field of the local player: a different value means the player edited it.
    // Kept for one Player object: another character or world starts again from the game's values.
    private static readonly Dictionary<string, object> _synced = new Dictionary<string, object>();

    private static Player? _syncedPlayer;

    private static void SyncedFor(Player player)
    {
        if (!ReferenceEquals(_syncedPlayer, player))
        {
            _syncedPlayer = player;
            _synced.Clear();
        }
    }

    public static void ApplyStats(Player player)
    {
        if (player.whoAmI != Main.myPlayer || Main.gameMenu)
        {
            return;
        }
        Guard(delegate
        {
            SyncedFor(player);
            player.NaturalStatDefense = player.statDefense;
            player.NaturalDashType = player.dashType;
            player.NaturalThorns = player.thorns;
            player.NaturalAggro = player.aggro;
            player.NaturalArmorPenetration = (int)player.armorPenetration;
            player.NaturalNoKnockback = player.noKnockback;
            player.NaturalShimmerImmune = _shimmerImmune(player);
            player.NaturalLavaImmune = player.lavaImmune;

            player.statDefense = Follow("Defense", ref player.ModStatDefense, ref player.UseModStatDefense, player.NaturalStatDefense);
            player.dashType = Follow("Dash", ref player.ModDashType, ref player.UseModDashType, player.NaturalDashType);
            player.thorns = Follow("Thorns", ref player.ModThorns, ref player.UseModThorns, player.NaturalThorns);
            player.aggro = Follow("Aggro", ref player.ModAggro, ref player.UseModAggro, player.NaturalAggro);
            player.armorPenetration = Follow("ArmorPen", ref player.ModArmorPenetration, ref player.UseModArmorPenetration, player.NaturalArmorPenetration);
            _shimmerImmune(player) = Follow("Shimmer", ref player.ModShimmerImmune, ref player.UseModShimmerImmune, player.NaturalShimmerImmune) || player.NaturalShimmerImmune;
            player.lavaImmune = Follow("Lava", ref player.ModLavaImmune, ref player.UseModLavaImmune, player.NaturalLavaImmune) || player.NaturalLavaImmune;
            if (player.ModNoKnockback)
            {
                player.noKnockback = true;
            }
        });
    }

    /// <summary>The value to use: the edited one once the player changed it, the game's one before.</summary>
    private static T Follow<T>(string key, ref T mod, ref bool use, T natural)
    {
        if (!use && _synced.TryGetValue(key, out object last) && !Equals(last, mod))
        {
            use = true;
        }
        if (!use)
        {
            mod = natural;
            _synced[key] = natural;
            return natural;
        }
        return mod;
    }

    private static void LifeRegenPrefix(Player __instance)
    {
        if (__instance.whoAmI != Main.myPlayer || Main.gameMenu)
        {
            return;
        }
        Guard(delegate
        {
            SyncedFor(__instance);
            __instance.NaturalLifeRegen = __instance.lifeRegen;
            __instance.lifeRegen = Follow("LifeRegen", ref __instance.ModLifeRegen, ref __instance.UseModLifeRegen, __instance.NaturalLifeRegen);
        });
    }

    // ------------------------------------------------------------------ multishot

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShootOriginal(Player self, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback) => throw new NotImplementedException("reverse patch");

    private static bool _shooting;

    private static void ShootPostfix(Player __instance, int i, Item sItem, int weaponDamage)
    {
        if (_shooting || __instance.whoAmI != Main.myPlayer || sItem == null)
        {
            return;
        }
        int times = WeaponConfigManager.ItemTimesToShoot.TryGetValue(sItem.type, out int value) ? value : 1;
        if (times <= 1)
        {
            return;
        }
        _shooting = true;
        int mouseX = Main.mouseX;
        int mouseY = Main.mouseY;
        try
        {
            Guard(delegate
            {
                XVector2 origin = __instance.MountedCenter;
                XVector2 mouseWorld = new XVector2(mouseX, mouseY) + Main.screenPosition;
                for (int shot = 1; shot < times; shot++)
                {
                    float angle = 0.05f * (shot - (times - 1) / 2f);
                    XVector2 aim = origin + (mouseWorld - origin).RotatedBy(angle);
                    Main.mouseX = (int)(aim.X - Main.screenPosition.X);
                    Main.mouseY = (int)(aim.Y - Main.screenPosition.Y);
                    ShootOriginal(__instance, i, sItem, weaponDamage, false);
                }
            });
        }
        finally
        {
            Main.mouseX = mouseX;
            Main.mouseY = mouseY;
            _shooting = false;
        }
    }

    // ------------------------------------------------------------------ ghost

    private static bool _lastGhost;

    private static bool _lastFunctional;

    /// <summary>
    /// The editor's Ghost / Functional Ghost switches are IsaClient's ghost (.ghost): a functional ghost keeps its
    /// inventory, hotbar, items and HUD, the server is told, mounts and hooks are let go. .ghost shows in the switches too.
    /// </summary>
    public static void SyncGhost(Player player)
    {
        if (Main.gameMenu || player.whoAmI != Main.myPlayer)
        {
            return;
        }
        Guard(delegate
        {
            bool ghost = player.ghost;
            bool functional = player.functionalGhost;
            if (ghost != _lastGhost || (ghost && functional != _lastFunctional))
            {
                if (ghost != _lastGhost && IsaClient.Gameplay.PlayerTools.GhostCheat == ghost)
                {
                    // .ghost (or the client's menu) did it: the switches follow.
                    player.functionalGhost = functional = IsaClient.Gameplay.PlayerTools.GhostCheat;
                }
                else
                {
                    // A switch in the editor changed: undo its bare change and let the client do it properly.
                    player.ghost = _lastGhost;
                    IsaClient.Gameplay.PlayerTools.SetGhost(ghost, functional);
                }
            }
            _lastGhost = player.ghost;
            _lastFunctional = player.functionalGhost;
        });
    }
}
