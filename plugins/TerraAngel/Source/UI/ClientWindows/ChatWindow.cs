using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.GameInput;
using Terraria.UI.Chat;
using ImGuiUtil = TerraAngel.Graphics.ImGuiUtil;

namespace TerraAngel.UI.ClientWindows;

// Port: TerraAngel's chat replaces the game's only while "TerraAngel chat" is on (Misc tools / Settings). The game
// still keeps every message (Patches feed them here), so turning it off brings the normal chat back as it was; typed
// lines go through Main.SubmitChatLine, so IsaClient's commands work the same in both.
public class ChatWindow : ClientWindow, ClientLoader.IChat
{
    public static ChatWindow? Instance { get; private set; }

    public static bool On => ClientConfig.Settings.UseTerraAngelChat && Instance != null && !Main.gameMenu && !Main.dedServ;

    public override bool DefaultEnabled => false;

    public override bool IsEnabled
    {
        get => On;
        set
        {
        }
    }

    public override bool IsToggleable => false;

    public override bool IsGlobalToggle => false;

    public override string Title => GetString("Chat");

    public override Keys ToggleKey => Keys.Enter;

    public ChatWindow()
    {
        Instance = this;
        Main.instance.Activated += OnActivated;
    }

    public void Dispose()
    {
        Main.instance.Activated -= OnActivated;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnActivated(object? sender, EventArgs args)
    {
        if (IsChatting)
        {
            ReclaimFocus = true;
        }
    }

    public string ChatText = "";

    public bool IsChatting = false;

    bool ClientLoader.IChat.IsChatting => IsChatting;

    string ClientLoader.IChat.ChatText
    {
        get => GetText();
        set => SetText(value);
    }

    // Port: opening happens in the game update (the game's Enter), the window's Update runs later in the ImGui frame.
    private bool OpenRequested = false;

    public bool ScrollToBottom = false;

    public bool ReclaimFocus = false;

    private bool IsLocked = true;

    private bool OpenedThisFrame = false;

    private bool ClosedThisFrame = false;

    private bool ResetPosition = false;

    private bool AutoScrollFix = false;

    private float AutoScrollFixMaxY = 0;

    private float AutoScrollFixPrevMaxY = 0;

    private string TextToAppend = "";

    private string TextToSet = "";

    private object ChatLock = new object();

    public List<ChatItem> ChatItems = new List<ChatItem>(ClientConfig.Settings.ChatMessageLimit);

    public List<string> ChatHistory = new List<string>(100);

    private int HistoryPosition = -1;

    public override void Draw(ImGuiIOPtr io)
    {
        if (IsChatting)
        {
            if (Main.CurrentInputTextTakerOverride != null)
            {
                CloseChat();
            }

            if (Main.editSign)
            {
                CloseChat();
            }

            if (PlayerInput.UsingGamepad)
            {
                CloseChat();
            }

            if (InputSystem.IsKeyDownRaw(Keys.Escape))
            {
                CloseChat();

                if (ClientConfig.Settings.ChatVanillaInvetoryBehavior)
                {
                    Main.playerInventory = !Main.playerInventory;
                    if (Main.playerInventory)
                    {
                        SoundEngine.PlaySound(SoundID.MenuOpen);
                    }
                }
            }
        }

        ImGui.PushFont(ClientAssets.GetTerrariaFont(22f));

        ImGuiStylePtr style = ImGui.GetStyle();

        var colors = style.Colors;

        Color colorWithAlpha(ImGuiCol col, float a)
        {
            // Port: blue was read from X (the theme's colors came out green).
            return new Color(colors[(int)col].X, colors[(int)col].Y, colors[(int)col].Z, a);
        }

        float transperency = IsChatting ? ClientConfig.Settings.ChatWindowTransperencyActive : ClientConfig.Settings.ChatWindowTransperencyInactive;
        Color bgColor = colorWithAlpha(ImGuiCol.WindowBg, transperency);
        Color titleColorActive = colorWithAlpha(ImGuiCol.TitleBgActive, transperency);
        Color borderColor = colorWithAlpha(ImGuiCol.Border, transperency);
        Color scrollBgColor = colorWithAlpha(ImGuiCol.ScrollbarBg, transperency);
        Color scrollGrabColor = colorWithAlpha(ImGuiCol.ScrollbarGrab, transperency);
        Color scrollGrabActiveColor = colorWithAlpha(ImGuiCol.ScrollbarGrabActive, transperency);
        Color scrollGrabHoveredColor = colorWithAlpha(ImGuiCol.ScrollbarGrabHovered, transperency);

        Vector2 windowSize = new Vector2(io.DisplaySize.X - 690, 240);
        Vector2 windowPosition = new Vector2(88f, io.DisplaySize.Y - windowSize.Y);

        ImGui.SetNextWindowPos(windowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);

        if (ResetPosition)
        {
            ResetPosition = false;
            ImGui.SetNextWindowPos(windowPosition);
            ImGui.SetNextWindowSize(windowSize);
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, bgColor.PackedValue);
        // Port: ImGui draws the menu bar's background even with NoBackground; closed, it's not there either.
        ImGui.PushStyleColor(ImGuiCol.MenuBarBg, IsChatting ? titleColorActive.PackedValue : 0u);
        ImGui.PushStyleColor(ImGuiCol.Border, borderColor.PackedValue);

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, scrollBgColor.PackedValue);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, scrollGrabColor.PackedValue);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, scrollGrabActiveColor.PackedValue);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, scrollGrabHoveredColor.PackedValue);
        // Port: the message area is a child window, which IsaClient's theme gives a background of its own.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.MenuBar;
        // Port: closed, only the fading lines show (no box, border or scrollbar).
        if (!IsChatting) flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoBackground;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, IsChatting ? 1f : 0f);
        if (IsLocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        ImGui.Begin("ChatWindow", flags);

        if (IsChatting && ImGui.BeginMenuBar())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 1f);
            ImGui.TextUnformatted(GetString("Chat"));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 1f));

            if (ImGui.Button($"{(IsLocked ? Icon.Lock : Icon.Unlock)}")) IsLocked = !IsLocked;
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(GetString($"Chat {(IsLocked ? GetString("Locked") : GetString("Unlocked"))}"));
                ImGui.EndTooltip();
            }

            if (ImGui.Button($"{Icon.ClearAll}")) ChatItems.Clear();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(GetString("Clear Chat"));
                ImGui.EndTooltip();
            }

            if (ImGui.Button($"{Icon.Refresh}")) ResetPosition = true;
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(GetString("Reset Position"));
                ImGui.EndTooltip();
            }

            ImGui.PopStyleVar();
            ImGui.EndMenuBar();
        }

        float footerHeight = style.ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();

        bool childStarted = ImGui.BeginChild("##ChatScrolling", new Vector2(0, -footerHeight), ImGuiChildFlags.None, IsChatting ? ImGuiWindowFlags.None : (ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar));

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(drawList.GetClipRectMin(), drawList.GetClipRectMax() + new Vector2(0, 5f));
        Vector2 tip = style.ItemSpacing;
        float wrapWidth = ImGui.GetContentRegionAvail().X;

        lock (ChatLock)
        {
            for (int i = 0; i < ChatItems.Count; i++)
            {
                ChatItem item = ChatItems[i];

                if (item.CountAbove > 0)
                {
                    item.TextSnippets[^1].Text = $" ({item.CountAbove})";
                }

                style.ItemSpacing = new Vector2(4f, 1f);

                if (item.LastWrapWidth != wrapWidth)
                {
                    item.LastWrapWidth = wrapWidth;
                    item.CachedSize = ImGuiUtil.CalcTextSizeWithTags(item.TextSnippets, wrapWidth);
                }

                bool showMessageFade = false;
                if (item.TimeMessageHasBeenVisible < ClientConfig.Settings.framesForMessageToBeVisible + 60)
                {
                    showMessageFade = true;
                    item.TimeMessageHasBeenVisible++;
                }

                if (ImGui.IsRectVisible(item.CachedSize) && (IsChatting || showMessageFade))
                {
                    if (!IsChatting && showMessageFade && item.TimeMessageHasBeenVisible > (ClientConfig.Settings.framesForMessageToBeVisible))
                    {
                        float alpha = 1.0f - ((float)(item.TimeMessageHasBeenVisible - ClientConfig.Settings.framesForMessageToBeVisible) / 60f);
                        if (ImGuiUtil.WrappedSelectableWithTextBorderWithTags($"CHID{i}", item.TextSnippets, wrapWidth, new Color(0f, 0f, 0f), item.CachedSize, alpha))
                        {
                            ImGui.SetClipboardText(item.OriginalText);
                        }
                    }
                    else
                    {
                        if (ImGuiUtil.WrappedSelectableWithTextBorderWithTags($"CHID{i}", item.TextSnippets, wrapWidth, Color.Black, item.CachedSize))
                        {
                            ImGui.SetClipboardText(item.OriginalText);
                        }
                    }
                }
                else
                {
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + item.CachedSize.Y + (i + 1 < ChatItems.Count ? style.ItemSpacing.Y : 0f));
                    style.ItemSpacing.Y = 0f;
                    ImGui.Dummy(Vector2.Zero);
                }
            }
        }

        AutoScrollFixPrevMaxY = AutoScrollFixMaxY;
        AutoScrollFixMaxY = ImGui.GetScrollMaxY();
        if (ScrollToBottom || (ClientConfig.Settings.ChatAutoScroll && (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() || (AutoScrollFix && AutoScrollFixMaxY > AutoScrollFixPrevMaxY))) || !IsChatting || OpenedThisFrame)
        {
            AutoScrollFix = false;
            if (ClientConfig.Settings.ChatAutoScroll && (ImGui.GetScrollY() >= ImGui.GetScrollMaxY()))
                AutoScrollFix = true;
            ImGui.SetScrollY(ImGui.GetScrollMaxY());
        }
        else
        {
            AutoScrollFix = false;
        }
        style.ItemSpacing = tip;
        ScrollToBottom = false;
        drawList.PopClipRect();

        ImGui.EndChild();

        bool chatBoxFocus = false;
        if (IsChatting)
        {
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X);
            unsafe
            {
                ImGui.InputText("##chatInput", ref ChatText, 512, ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackHistory,
                    (x) =>
                    {
                        ImGuiInputTextCallbackDataPtr data = x;
                        switch (data.EventFlag)
                        {
                            case ImGuiInputTextFlags.CallbackHistory:
                                {
                                    int previousHistoryPosition = HistoryPosition;
                                    if (data.EventKey == ImGuiKey.UpArrow)
                                    {
                                        if (HistoryPosition == -1)
                                        {
                                            HistoryPosition = ChatHistory.Count - 1;
                                        }
                                        else if (HistoryPosition > 0)
                                        {
                                            HistoryPosition--;
                                        }
                                    }
                                    else if (data.EventKey == ImGuiKey.DownArrow)
                                    {
                                        if (HistoryPosition != -1)
                                        {
                                            HistoryPosition++;
                                            if (HistoryPosition >= ChatHistory.Count)
                                            {
                                                HistoryPosition = -1;
                                            }
                                        }
                                    }

                                    if (previousHistoryPosition != HistoryPosition)
                                    {
                                        data.SetText((HistoryPosition >= 0) ? ChatHistory[HistoryPosition] : "");
                                    }
                                }
                                break;
                            case ImGuiInputTextFlags.CallbackAlways:
                                if (TextToAppend.Length > 0)
                                {
                                    lock (TextToAppend)
                                    {
                                        data.InsertChars(data.CursorPos, TextToAppend);
                                        TextToAppend = "";
                                    }
                                }
                                if (TextToSet.Length > 0)
                                {
                                    lock (TextToSet)
                                    {
                                        data.SetText(TextToSet);
                                        TextToSet = "";
                                    }
                                }
                                break;
                        }
                        chatBoxFocus = true;
                        return 0;
                    });
            }
            ImGui.PopItemWidth();

            if (OpenedThisFrame || ReclaimFocus)
            {
                ReclaimFocus = false;
                ImGui.SetItemDefaultFocus();
                ImGui.SetKeyboardFocusHere(-1);
            }
        }

        ImGui.End();
        ImGui.PopStyleColor(8);
        ImGui.PopStyleVar();

        ImGui.PopFont();

        if (IsChatting)
        {
            if (!OpenedThisFrame && InputSystem.IsKeyPressedRaw(Keys.Enter))
            {
                if (chatBoxFocus)
                {
                    Submit();
                    CloseChat();
                }
                else
                {
                    ReclaimFocus = true;
                }
            }
        }
    }

    public override void Update()
    {
        // Port: the game's Enter opens the chat (Main.OpenPlayerChat comes here while this chat is on).
        OpenedThisFrame = OpenRequested;
        OpenRequested = false;
        ClosedThisFrame = false;
        if (IsChatting && !On)
        {
            IsChatting = false;
        }
    }

    public void OpenChat()
    {
        if (IsChatting)
        {
            return;
        }
        IsChatting = true;
        OpenRequested = true;
        SoundEngine.PlaySound(SoundID.MenuOpen);
    }

    public void CloseChat()
    {
        IsChatting = false;
        ClosedThisFrame = true;
        SoundEngine.PlaySound(SoundID.MenuClose);

        if (ClientConfig.Settings.ClearChatInputOnClose)
        {
            ChatText = "";
        }
    }

    public void Submit()
    {
        if (string.IsNullOrEmpty(ChatText))
        {
            return;
        }

        // Port: the client's commands (.help...) and its chat options (translation, colors) like the game's chat.
        Main.SubmitChatLine(ChatText);

        HistoryPosition = -1;
        for (int i = ChatHistory.Count - 1; i >= 0; i--)
        {
            if (ChatHistory[i] == ChatText)
            {
                ChatHistory.RemoveAt(i);
                break;
            }
        }

        if (ChatHistory.Count > ClientConfig.Settings.ChatHistoryLimit)
            ChatHistory.RemoveRange(0, ChatHistory.Count - ClientConfig.Settings.ChatHistoryLimit);

        ChatHistory.Add(ChatText);

        ChatText = "";

        ScrollToBottom = true;
    }

    public void WriteLine(string message)
    {
        WriteLine(message, Color.White);
    }

    public void WriteLine(string message, Color color)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }
        List<TextSnippet> snippets = ChatManager.ParseMessage(message, color);

        lock (ChatLock)
        {
            if (ChatItems.Count > 0)
            {
                ChatItem above = ChatItems[^1];
                if (message == above.OriginalText && above.Color == color)
                {
                    above.CountAbove++;
                    above.TimeMessageHasBeenVisible = 0;
                }
                else
                {
                    ChatItems.Add(new ChatItem(message, snippets, color, 0));
                }
            }
            else
            {
                ChatItems.Add(new ChatItem(message, snippets, color, 0));
            }

            if (ChatItems.Count > ClientConfig.Settings.ChatMessageLimit)
            {
                ChatItems.RemoveRange(0, ChatItems.Count - ClientConfig.Settings.ChatMessageLimit);
            }
        }
    }

    public void AppendText(string message)
    {
        if (IsChatting)
        {
            OpenRequested = true;
            lock (TextToAppend)
            {
                TextToAppend += message;
            }
        }
    }

    public void SetText(string message)
    {
        if (IsChatting)
        {
            OpenRequested = true;
            lock (TextToSet)
            {
                TextToSet = message;
            }
        }
    }

    public string GetText()
    {
        lock (TextToSet)
        {
            return TextToSet.Length > 0 ? TextToSet : ChatText;
        }
    }

    public class ChatItem
    {
        public string OriginalText;

        public List<TextSnippet> TextSnippets;

        public Color Color;

        public uint CountAbove;

        public uint TimeMessageHasBeenVisible = 0;

        public Vector2 CachedSize = Vector2.Zero;

        public float LastWrapWidth = float.MinValue;

        public ChatItem(string text, List<TextSnippet> snippets, Color color, int CountAboue)
        {
            OriginalText = text;

            Color = color;

            CountAbove = (uint)(CountAboue);

            TextSnippets = snippets;

            TextSnippets.Add(new TextSnippet(""));
        }
    }
}
