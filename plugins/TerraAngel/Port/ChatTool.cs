namespace TerraAngel.Tools;

/// <summary>Turns TerraAngel's chat window on and off (it is also in TerraAngel's settings).</summary>
public class TerraAngelChatTool : Tool
{
    public override string Name => GetString("TerraAngel Chat");

    public override ToolTabs Tab => ToolTabs.MiscTools;

    public override void DrawUI(ImGuiIOPtr io)
    {
        ImGui.Checkbox(Name, ref ClientConfig.Settings.UseTerraAngelChat);
        ImGui.SameLine();
        ImGuiUtil.HelpMarker(GetString("Replaces the game's chat with TerraAngel's: Enter opens it, Up/Down go through what you sent, clicking a line copies it, and it can be moved and resized from its lock button. Client commands work the same."));
    }
}
