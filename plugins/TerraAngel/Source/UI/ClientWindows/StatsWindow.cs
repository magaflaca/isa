using System;
using System.Drawing;
using Microsoft.Xna.Framework.Input;

namespace TerraAngel.UI.ClientWindows;

public class StatsWindow : ClientWindow
{
    public override Keys ToggleKey => Keys.None;

    // Port: listed in the Windows menu, and its saved state is the default (AddWindow applies DefaultEnabled).
    public override bool IsToggleable => true;

    public override bool DefaultEnabled => ClientConfig.Settings.ShowStatsBar;

    public override bool IsEnabled { get => ClientConfig.Settings.ShowStatsBar; set => ClientConfig.Settings.ShowStatsBar = value; }

    public override string Title => GetString("Stat Window");

    public override bool IsGlobalToggle => false;

    public override void Draw(ImGuiIOPtr io)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();

        ImGui.PushFont(ClientAssets.GetMonospaceFont(16f));

        drawList.AddRectFilled(Vector2.Zero, new Vector2(io.DisplaySize.X, 16f + style.ItemSpacing.Y * 2f), ImGui.GetColorU32(ImGuiCol.WindowBg, 0.5f));
        drawList.AddRect(Vector2.Zero, new Vector2(io.DisplaySize.X, 16f + style.ItemSpacing.Y * 2f), ImGui.GetColorU32(ImGuiCol.WindowBg, 0.9f));

        string versionText = ClientLoader.TerraAngelVersion is null ? "" : $"v{ClientLoader.TerraAngelVersion} ";
        string fpsText = $"{Math.Round(1f / TimeMetrics.FramerateDeltaTimeSlices.Average.TotalSeconds),2:F0}";
        string upsText = $"{Math.Round(1f / TimeMetrics.UpdateDeltaTimeSlices.Average.TotalSeconds),2:F0}";


        drawList.AddText(Vector2.Zero + style.ItemSpacing, ImGui.GetColorU32(ImGuiCol.Text), $"{versionText}");

        string rightText = $"{(BuilderModeTool.Enabled ? GetString("Builder Mode Active ") : "")}{fpsText} FPS {upsText} UPS";

        Vector2 rightTextSize = ImGui.CalcTextSize(rightText);

        drawList.AddText(new Vector2(io.DisplaySize.X - rightTextSize.X - style.ItemSpacing.X, style.ItemSpacing.Y), ImGui.GetColorU32(ImGuiCol.Text), rightText);

        ImGui.PopFont();
    }
}