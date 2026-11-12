using System.Reflection;
using HarmonyLib;
using Terraria.GameContent.UI.Chat;
using Terraria.UI;
using Terraria.UI.Chat;

namespace TerraAngel;

/// <summary>
/// TerraAngel's chat window (see ChatWindow): while "TerraAngel Chat" is on, the game's Enter opens it, the game's
/// chat lines are not drawn, and items shared with the favorite key go into its text box. Every line the game writes
/// is also given to it, so it has the history when it is turned on. Off, all of this lets the game's code run.
/// </summary>
public static partial class Patches
{
    private static readonly FieldInfo? _chatDrawThread = AccessTools.Field(typeof(RemadeChatMonitor), "_drawThread");

    private static void ApplyChat(Harmony harmony)
    {
        Prefix(harmony, AccessTools.Method(typeof(Main), nameof(Main.OpenPlayerChat)), nameof(OpenPlayerChatPrefix));
        Prefix(harmony, AccessTools.Method(typeof(RemadeChatMonitor), nameof(RemadeChatMonitor.DrawChat)), nameof(DrawChatPrefix));
        Prefix(harmony, AccessTools.Method(typeof(RemadeChatMonitor), nameof(RemadeChatMonitor.AddNewMessage)), nameof(AddNewMessagePrefix));
        Prefix(harmony, AccessTools.Method(typeof(ChatManager), nameof(ChatManager.AddChatText)), nameof(AddChatTextPrefix));
        Type[] slot = { typeof(Item[]), typeof(int), typeof(int) };
        foreach (string hover in new[] { nameof(ItemSlot.OverrideHover), nameof(ItemSlot.OverrideHoverFavoriteAndShift) })
        {
            harmony.Patch(AccessTools.Method(typeof(ItemSlot), hover, slot), new HarmonyMethod(Method(nameof(ShareHoverPrefix))), new HarmonyMethod(Method(nameof(ShareHoverPostfix))));
        }
    }

    private static bool Chatting => ChatWindow.On && ChatWindow.Instance!.IsChatting;

    private static bool OpenPlayerChatPrefix()
    {
        if (!ChatWindow.On)
        {
            return true;
        }
        return Guard("OpenPlayerChat", () =>
        {
            if (Main.CurrentInputTextTakerOverride == null)
            {
                ChatWindow.Instance!.OpenChat();
            }
            return false;
        });
    }

    private static bool DrawChatPrefix() => !ChatWindow.On;

    private static void AddNewMessagePrefix(string text, Color color)
    {
        if (ChatWindow.Instance == null || Main.dedServ)
        {
            return;
        }
        // Lines written from another thread are written again from the game thread (the game queues them).
        int drawThread = (_chatDrawThread?.GetValue(null) as int?) ?? 0;
        if (drawThread != 0 && Environment.CurrentManagedThreadId != drawThread)
        {
            return;
        }
        Guard("AddNewMessage", () => ChatWindow.Instance.WriteLine(text, color));
    }

    private static bool AddChatTextPrefix(string text, ref bool __result)
    {
        if (!Chatting)
        {
            return true;
        }
        bool handled = false;
        Guard("AddChatText", () =>
        {
            ChatWindow.Instance!.AppendText(text);
            handled = true;
        });
        if (handled)
        {
            __result = true;
        }
        return !handled;
    }

    /// <summary>The game only offers sharing an item while its own chat is open: it is told so while this one is.</summary>
    private static void ShareHoverPrefix(out bool __state)
    {
        __state = !Main.drawingPlayerChat && Chatting;
        if (__state)
        {
            Main.drawingPlayerChat = true;
        }
    }

    private static void ShareHoverPostfix(bool __state)
    {
        if (__state)
        {
            Main.drawingPlayerChat = false;
        }
    }
}
