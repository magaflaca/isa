using System.IO;
using ImGuiNET;
using IsaClient.Plugins;
using Microsoft.Xna.Framework;
using TerraAngel;
using TerraAngel.Tools;
using TerraAngel.UI;
using Terraria;
using Terraria.ID;
using NVector2 = System.Numerics.Vector2;

namespace HelloImGui;

/// <summary>
/// A tour of what a plugin can do: its own tab in the overlay (Insert), boxes drawn over the world, a tool in the
/// Main tab, a window of its own, a chat command and a console command.
/// </summary>
public class HelloPlugin : ClientPlugin
{
	public override string Name => "Hello ImGui";

	public override string Author => "IsaClient";

	public override string Version => "1.0";

	public override string Description => "Example plugin: player info tab, NPC boxes, a heal tool, .hello and a small window.";

	public static bool ShowNpcBoxes;

	private static Color _boxColor = new Color(255, 120, 200);

	private int _clicks;

	public override void Load()
	{
		ToolManager.AddTool<QuickHealTool>();
		ClientLoader.MainRenderer.AddWindow(new HelloWindow());
		ClientLoader.Console.AddCommand("hello", cmd => ClientLoader.Console.WriteLine("Hello, " + (cmd.Args.Count > 0 ? cmd.FullArgs : "world") + "!"), "Says hello");
		AddChatCommand("hello", "[name]", "Says hello in the chat.", args => Chat("Hello, " + (args.Length > 0 ? string.Join(" ", args) : Main.LocalPlayer.name) + "!"));
		// Settings and other files of the plugin go in its own folder.
		File.WriteAllText(Path.Combine(DataFolder, "last-load.txt"), System.DateTime.Now.ToString());
	}

	public override void DrawTab(ImGuiIOPtr io)
	{
		Player player = Main.LocalPlayer;
		if (Main.gameMenu || player == null)
		{
			ImGui.TextDisabled("Join a world to see your player.");
			return;
		}
		ImGui.Text(player.name);
		ImGui.SameLine();
		ImGuiExt.TextColored("HP " + player.statLife + "/" + player.statLifeMax2, new Color(255, 90, 90));
		ImGui.SameLine();
		ImGuiExt.TextColored("Mana " + player.statMana + "/" + player.statManaMax2, new Color(90, 150, 255));
		ImGui.Text("Tile " + (int)(player.Center.X / 16f) + ", " + (int)(player.Center.Y / 16f));
		ImGui.Separator();

		ImGui.Text("Held item:");
		ImGuiExt.ItemButton(player.HeldItem.type, "held", new NVector2(36f, 36f));
		ImGui.SameLine();
		ImGui.TextWrapped(player.HeldItem.Name);

		ImGui.Separator();
		ImGui.Checkbox("Boxes around NPCs", ref ShowNpcBoxes);
		ImGui.SameLine();
		ImGuiExt.ColorEdit4("##boxcolor", ref _boxColor);
		if (ImGui.Button("Click me (" + _clicks + ")"))
		{
			_clicks++;
		}
	}

	public override void DrawOverlay(ImGuiIOPtr io)
	{
		if (!ShowNpcBoxes || Main.gameMenu)
		{
			return;
		}
		ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
		foreach (NPC npc in Main.npc)
		{
			if (!npc.active)
			{
				continue;
			}
			Vector2 min = Screen.WorldToScreenWorld(npc.TopLeft);
			Vector2 max = Screen.WorldToScreenWorld(npc.BottomRight);
			draw.AddRect(min.ToNumerics(), max.ToNumerics(), _boxColor.PackedValue, 2f, ImDrawFlags.None, 1.5f);
			draw.AddText(new NVector2(min.X, min.Y - 16f), 0xFFFFFFFFu, npc.GivenOrTypeName + " " + npc.life);
		}
	}

	public override void OnEnterWorld()
	{
		Chat("Hello ImGui is on: press Insert for the plugin overlay.");
	}
}

/// <summary>A TerraAngel style tool: shown in the overlay's Main tab, updated every frame.</summary>
public class QuickHealTool : Tool
{
	public override string Name => "Quick heal";

	public override ToolTabs Tab => ToolTabs.MainTools;

	public bool Enabled;

	public override void DrawUI(ImGuiIOPtr io)
	{
		ImGui.Checkbox(Name, ref Enabled);
		ImGuiExt.HelpMarker("Fills your life when it drops under a third (single player only).");
	}

	public override void Update()
	{
		if (Enabled && !Main.gameMenu && Main.netMode == 0 && Main.LocalPlayer.statLife < Main.LocalPlayer.statLifeMax2 / 3)
		{
			Main.LocalPlayer.statLife = Main.LocalPlayer.statLifeMax2;
		}
	}
}

/// <summary>A window of its own: listed in the overlay's Windows menu.</summary>
public class HelloWindow : ClientWindow
{
	public override string Title => "Hello window";

	public override bool DefaultEnabled => false;

	public override void Draw(ImGuiIOPtr io)
	{
		ImGui.SetNextWindowSize(new NVector2(260f, 120f), ImGuiCond.FirstUseEver);
		bool open = true;
		if (ImGui.Begin(Title, ref open))
		{
			ImGui.Text("FPS: " + Main.fpsCount);
			ImGui.Text("Time: " + (Main.dayTime ? "day" : "night"));
		}
		ImGui.End();
		IsEnabled = open;
	}
}
