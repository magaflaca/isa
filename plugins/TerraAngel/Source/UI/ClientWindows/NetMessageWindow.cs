using System.Text;
using Microsoft.Xna.Framework.Input;
using MessageID = Terraria.ID.MessageID;

namespace TerraAngel.UI.ClientWindows;

// Port: TerraAngel described packets with TrProtocol (a .NET 10 library); here a packet shows its bytes ("Packets with
// details") and, for sent ones, where it was sent from ("Packets with traces"). Logs are kept per type up to a limit, and
// nothing is recorded while "Log Traffic" is off. The client's socket feeds it (Port/NetPatches.cs).
public class NetMessageWindow : ClientWindow
{
    public static NetMessageWindow Instance { get; } = new NetMessageWindow();

    public override bool IsToggleable => true;

    public override bool DefaultEnabled => false;

    public override string Title => GetString("Net Debugger");

    public override Keys ToggleKey => ClientConfig.Settings.ToggleNetDebugger;

    public override bool IsGlobalToggle => false;

    private const int MaxLogsPerType = 500;

    private const int MaxRawLogs = 5000;

    private const int HexPreview = 250;

    private readonly object LogLock = new object();

    public List<NetPacketLog>[] SentNetPacketLogs = new List<NetPacketLog>[MessageID.Count];

    public List<NetPacketLog>[] ReceivedNetPacketLogs = new List<NetPacketLog>[MessageID.Count];

    public List<NetPacketLog>[] AllNetPacketLogs = new List<NetPacketLog>[MessageID.Count];

    public bool FancyLoggingEnabled = false;

    public string FancyLogsFilter = "";

    public string FancyLogsTracesFilter = "";

    public string FancyLogsDetailsFilter = "";

    public HashSet<int> MessagesTypesToLogTraces = new HashSet<int>();

    public HashSet<int> MessagesTypesToLogDetails = new HashSet<int>();

    public bool FancyLogsShowSent = true;

    public bool FancyLogsShowReceived = true;

    private readonly bool[] TreeMessageTypesOpen = new bool[MessageID.Count];

    public List<RawNetPacketLog> RawNetPacketLogs = new List<RawNetPacketLog>();

    public bool RawLoggingEnabled = false;

    public bool RawLogsShowSent = true;

    public bool RawLogsShowReceived = true;

    public NetMessageWindow()
    {
        for (int i = 0; i < MessageID.Count; i++)
        {
            SentNetPacketLogs[i] = new List<NetPacketLog>();
            ReceivedNetPacketLogs[i] = new List<NetPacketLog>();
            AllNetPacketLogs[i] = new List<NetPacketLog>();
        }
    }

    // ------------------------------------------------------------------ logging (game and socket threads)

    public bool Logging => FancyLoggingEnabled || RawLoggingEnabled;

    // What the server sent that does not make a whole packet yet (the stream is read in pieces).
    private byte[] Pending = new byte[0];

    /// <summary>Bytes the client's socket sends: one or more packets, each starting with its 2-byte length.</summary>
    public void Sent(byte[] data, int offset, int size)
    {
        if (offset < 0 || size <= 0 || offset + size > data.Length)
        {
            return;
        }
        ForEachPacket(data, offset, offset + size, (at, length) => Log(data, at + 2, length - 2, sent: true));
    }

    /// <summary>A piece of what the server sent.</summary>
    public void Received(byte[] bytes, int streamLength)
    {
        if (streamLength <= 0 || streamLength > bytes.Length)
        {
            return;
        }
        lock (LogLock)
        {
            byte[] stream = new byte[Pending.Length + streamLength];
            Buffer.BlockCopy(Pending, 0, stream, 0, Pending.Length);
            Buffer.BlockCopy(bytes, 0, stream, Pending.Length, streamLength);
            int used = ForEachPacket(stream, 0, stream.Length, (at, length) => Log(stream, at + 2, length - 2, sent: false));
            // A broken length would keep everything after it pending forever: start again from the next piece.
            Pending = (used < 0 || stream.Length - used > 1 << 20) ? new byte[0] : Copy(stream, used, stream.Length - used);
        }
    }

    /// <summary>Calls <paramref name="packet"/> for each whole packet; returns the bytes used, or -1 for a bad length.</summary>
    private static int ForEachPacket(byte[] data, int start, int end, Action<int, int> packet)
    {
        int at = start;
        while (at + 2 <= end)
        {
            int length = BitConverter.ToUInt16(data, at);
            if (length < 3)
            {
                return -1;
            }
            if (at + length > end)
            {
                break;
            }
            packet(at, length);
            at += length;
        }
        return at - start;
    }

    /// <summary>One packet: <paramref name="data"/>[<paramref name="offset"/>] is its type, the rest its contents.</summary>
    private void Log(byte[] data, int offset, int length, bool sent)
    {
        int type = data[offset];
        lock (LogLock)
        {
            if (RawLoggingEnabled && (sent ? RawLogsShowSent : RawLogsShowReceived))
            {
                RawNetPacketLogs.Add(new RawNetPacketLog { Type = type, Data = Copy(data, offset, length), Sent = sent });
                if (RawNetPacketLogs.Count > MaxRawLogs)
                {
                    RawNetPacketLogs.RemoveRange(0, RawNetPacketLogs.Count - MaxRawLogs);
                }
            }
            if (!FancyLoggingEnabled || type >= MessageID.Count || !(sent ? FancyLogsShowSent : FancyLogsShowReceived))
            {
                return;
            }
            NetPacketLog log = sent ? new SentNetPacketLog() : new ReceivedNetPacketLog();
            log.Type = type;
            if (MessagesTypesToLogDetails.Contains(type))
            {
                log.Data = Copy(data, offset, length);
            }
            if (sent && MessagesTypesToLogTraces.Contains(type))
            {
                ((SentNetPacketLog)log).StackTrace = GameStack();
            }
            Add(sent ? SentNetPacketLogs[type] : ReceivedNetPacketLogs[type], log);
            Add(AllNetPacketLogs[type], log);
        }
    }

    /// <summary>Where a packet was sent from: the stack below the socket (this window and its patch left out).</summary>
    private static string GameStack()
    {
        string[] lines = new System.Diagnostics.StackTrace(false).ToString().Split('\n');
        int start = Array.FindLastIndex(lines, l => l.Contains("AsyncSend"));
        return string.Join("\n", lines.Skip(start + 1)).TrimEnd();
    }

    private static void Add(List<NetPacketLog> list, NetPacketLog log)
    {
        list.Add(log);
        if (list.Count > MaxLogsPerType)
        {
            list.RemoveRange(0, list.Count - MaxLogsPerType);
        }
    }

    private static byte[] Copy(byte[] data, int offset, int size)
    {
        byte[] copy = new byte[size];
        Buffer.BlockCopy(data, offset, copy, 0, size);
        return copy;
    }

    private static string Hex(byte[] data, int max)
    {
        if (data.Length == 0)
        {
            return "";
        }
        string hex = BitConverter.ToString(data, 0, Math.Min(data.Length, max)).Replace("-", "");
        return data.Length > max ? hex + ".." : hex;
    }

    private static string RawLine(RawNetPacketLog log, int max) => $"T: {log.Type,3} L: {log.Data.Length,5} B: {Hex(log.Data, max)}";

    // ------------------------------------------------------------------ window

    public override void Draw(ImGuiIOPtr io)
    {
        ImGui.PushFont(ClientAssets.GetMonospaceFont(16f));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(760f, 520f), ImGuiCond.FirstUseEver);
        bool open = IsEnabled;
        if (!ImGui.Begin(GetString("Net Debugger"), ref open, ImGuiWindowFlags.MenuBar))
        {
            IsEnabled = open;
            ImGui.End();
            ImGui.PopFont();
            return;
        }
        IsEnabled = open;

        bool rawMessagesOpen = false;

        lock (LogLock)
        {
            if (ImGui.BeginTabBar("NetDebuggerTabBar"))
            {
                if (ImGui.BeginTabItem(GetString("Net Message Logs")))
                {
                    DrawFancyLogs();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(GetString("Raw Message Logs")))
                {
                    rawMessagesOpen = true;
                    DrawRawLogs();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            DrawMenu(rawMessagesOpen);
        }

        ImGui.End();
        ImGui.PopFont();
    }

    private void DrawFancyLogs()
    {
        ImGui.Text(GetString("Search:"));
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 2.8f);
        ImGui.InputText("##FancyLogsFilter", ref FancyLogsFilter, 512);
        ImGui.PopItemWidth();

        ImGui.Checkbox(GetString("Log Traffic"), ref FancyLoggingEnabled);
        ImGui.SameLine();
        ImGui.Checkbox($"{Icon.ArrowUp}", ref FancyLogsShowSent);
        ImGui.SameLine();
        ImGui.Checkbox($"{Icon.ArrowDown}", ref FancyLogsShowReceived);

        ImGui.Text(GetString("Packets with traces:"));
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 2.8f);
        ImGui.InputText("##TraceFilter", ref FancyLogsTracesFilter, 512, ImGuiInputTextFlags.CharsNoBlank);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGuiUtil.HelpMarker(GetString("Message types (like 13, 17) whose sent packets keep where they were sent from; * for all."));
        MessagesTypesToLogTraces = ParseTypes(FancyLogsTracesFilter);

        ImGui.Text(GetString("Packets with details:"));
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 2.8f);
        ImGui.InputText("##DetailFilter", ref FancyLogsDetailsFilter, 512, ImGuiInputTextFlags.CharsNoBlank);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGuiUtil.HelpMarker(GetString("Message types whose bytes are kept; * for all."));
        MessagesTypesToLogDetails = ParseTypes(FancyLogsDetailsFilter);

        if (!ImGui.BeginChild("##MessageLogScrolling"))
        {
            ImGui.EndChild();
            return;
        }

        string filterLower = FancyLogsFilter.ToLower();
        bool filterIsNumber = int.TryParse(FancyLogsFilter, out int filterType);
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < MessageID.Count; i++)
        {
            string displayName = $"{InternalRepresentation.GetMessageIDName(i)}/{i}";

            if (FancyLogsFilter.Length > 0 && (filterIsNumber ? i != filterType : !displayName.ToLower().Contains(filterLower)))
            {
                continue;
            }

            List<NetPacketLog> packetLogProvider = AllNetPacketLogs[i];
            if (FancyLogsShowSent && !FancyLogsShowReceived)
            {
                packetLogProvider = SentNetPacketLogs[i];
            }
            else if (FancyLogsShowReceived && !FancyLogsShowSent)
            {
                packetLogProvider = ReceivedNetPacketLogs[i];
            }

            string count = packetLogProvider.Count == 0 ? "" : packetLogProvider.Count.ToString();

            if (ImGui.Selectable($"{(TreeMessageTypesOpen[i] ? Icon.TriangleDown : Icon.TriangleRight)} {displayName,-35} {count}###{displayName}"))
            {
                TreeMessageTypesOpen[i] = !TreeMessageTypesOpen[i];
            }

            if (!TreeMessageTypesOpen[i])
            {
                continue;
            }

            ImGui.Indent(20f);
            for (int j = 0; j < packetLogProvider.Count; j++)
            {
                ImGui.PushID(j);
                builder.Clear();
                if (packetLogProvider[j] is SentNetPacketLog sent)
                {
                    builder.Append(sent.Data.Length == 0 ? GetString("Sent packet") : $"L: {sent.Data.Length,5} B: {Hex(sent.Data, HexPreview)}");
                    if (sent.StackTrace is not null)
                    {
                        builder.AppendLine();
                        builder.Append(sent.StackTrace);
                    }
                    string text = builder.ToString();
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.LimeGreen.PackedValue);
                    if (ImGui.Selectable($"{Icon.ArrowUp} {text}"))
                    {
                        ImGui.SetClipboardText(text);
                    }
                    ImGui.PopStyleColor();
                }
                else if (packetLogProvider[j] is ReceivedNetPacketLog received)
                {
                    string text = received.Data.Length == 0 ? GetString("Received packet") : $"L: {received.Data.Length,5} B: {Hex(received.Data, HexPreview)}";
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.Crimson.PackedValue);
                    if (ImGui.Selectable($"{Icon.ArrowDown} {text}"))
                    {
                        ImGui.SetClipboardText(received.Data.Length == 0 ? text : Hex(received.Data, int.MaxValue));
                    }
                    ImGui.PopStyleColor();
                }
                ImGui.PopID();
            }
            ImGui.Unindent(20f);
        }

        ImGui.EndChild();
    }

    private void DrawRawLogs()
    {
        ImGui.Checkbox(GetString("Log Traffic"), ref RawLoggingEnabled);
        ImGui.SameLine();
        ImGui.Checkbox($"{Icon.ArrowUp}", ref RawLogsShowSent);
        ImGui.SameLine();
        ImGui.Checkbox($"{Icon.ArrowDown}", ref RawLogsShowReceived);
        ImGui.SameLine();
        ImGui.TextDisabled($"{RawNetPacketLogs.Count}/{MaxRawLogs}");

        if (!ImGui.BeginChild("##RawLogScrolling"))
        {
            ImGui.EndChild();
            return;
        }
        List<RawNetPacketLog> shown = RawNetPacketLogs.Where(ShowRaw).ToList();
        // Only the visible rows are built: the raw log can hold thousands of packets.
        unsafe
        {
            ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(shown.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    RawNetPacketLog log = shown[i];
                    ImGui.PushID(i);
                    if (ImGui.Selectable($"{(log.Sent ? Icon.ArrowUp : Icon.ArrowDown)} {RawLine(log, HexPreview)}"))
                    {
                        ImGui.SetClipboardText(RawLine(log, int.MaxValue));
                    }
                    ImGui.PopID();
                }
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
    }

    private bool ShowRaw(RawNetPacketLog log) => log.Sent ? RawLogsShowSent : RawLogsShowReceived;

    private void DrawMenu(bool rawMessagesOpen)
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }
        if (ImGui.BeginMenu(rawMessagesOpen ? GetString("Raw Message Logs") : GetString("Message Logs")))
        {
            if (ImGui.MenuItem(GetString("Clear")))
            {
                if (rawMessagesOpen)
                {
                    RawNetPacketLogs.Clear();
                }
                else
                {
                    for (int i = 0; i < MessageID.Count; i++)
                    {
                        SentNetPacketLogs[i].Clear();
                        ReceivedNetPacketLogs[i].Clear();
                        AllNetPacketLogs[i].Clear();
                    }
                }
            }
            if (rawMessagesOpen && ImGui.MenuItem(GetString("Copy All")))
            {
                StringBuilder builder = new StringBuilder();
                foreach (RawNetPacketLog log in RawNetPacketLogs.Where(ShowRaw))
                {
                    builder.Append(log.Sent ? "> " : "< ").Append(RawLine(log, int.MaxValue)).AppendLine();
                }
                ImGui.SetClipboardText(builder.ToString());
            }
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();
    }

    private static HashSet<int> ParseTypes(string filter)
    {
        if (filter.Contains('*'))
        {
            return new HashSet<int>(Enumerable.Range(0, MessageID.Count));
        }
        return new HashSet<int>(filter.Split(',').Select(x => int.TryParse(x.Trim(), out int a) ? a : -1).Where(x => x != -1));
    }
}

public abstract class NetPacketLog
{
    public int Type;

    public byte[] Data = Array.Empty<byte>();
}

public class ReceivedNetPacketLog : NetPacketLog
{
}

public class SentNetPacketLog : NetPacketLog
{
    public string? StackTrace;
}

public class RawNetPacketLog : NetPacketLog
{
    public bool Sent;
}
