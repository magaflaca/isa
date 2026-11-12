using ImGuiNET;
using IsaClient.Hub;
using Microsoft.Xna.Framework;
using NVector4 = System.Numerics.Vector4;

namespace IsaClient.Plugins;

/// <summary>ImGui colors taken from the panel's theme (HubTheme), so plugin windows look like the rest of IsaClient.</summary>
public static class ImGuiTheme
{
	private static string _applied;

	/// <summary>TerraAngel's own look (its colors, square corners) instead of the panel's theme.</summary>
	[IsaClient.Core.Setting("ImGuiTerraAngelTheme")]
	public static bool TerraAngelTheme;

	/// <summary>Applies the current panel theme if it changed since the last call.</summary>
	public static void Refresh()
	{
		if (_applied != Key)
		{
			Apply();
		}
	}

	private static string Key => TerraAngelTheme ? "TerraAngel" : HubTheme.Current.Name;

	public static void Apply()
	{
		HubTheme theme = HubTheme.Current;
		_applied = Key;
		if (TerraAngelTheme)
		{
			ApplyTerraAngel();
			return;
		}
		ImGuiStylePtr style = ImGui.GetStyle();
		style.WindowRounding = 8f;
		style.ChildRounding = 6f;
		style.FrameRounding = 5f;
		style.PopupRounding = 6f;
		style.GrabRounding = 4f;
		style.TabRounding = 5f;
		style.ScrollbarRounding = 6f;
		style.WindowBorderSize = 1f;
		style.FrameBorderSize = 0f;
		style.WindowPadding = new System.Numerics.Vector2(10f, 10f);
		style.FramePadding = new System.Numerics.Vector2(8f, 4f);
		style.ItemSpacing = new System.Numerics.Vector2(8f, 6f);
		style.WindowTitleAlign = new System.Numerics.Vector2(0.02f, 0.5f);

		RangeAccessor<NVector4> c = style.Colors;
		c[(int)ImGuiCol.Text] = V(theme.Text);
		c[(int)ImGuiCol.TextDisabled] = V(theme.Muted);
		c[(int)ImGuiCol.WindowBg] = V(theme.Window, 0.96f);
		// Transparent, as in TerraAngel's style: TA plugins fade their windows out and expect their children to have no background.
		c[(int)ImGuiCol.ChildBg] = V(theme.Sidebar, 0f);
		c[(int)ImGuiCol.PopupBg] = V(theme.Sidebar, 0.98f);
		c[(int)ImGuiCol.Border] = V(theme.Slot, 0.8f);
		c[(int)ImGuiCol.FrameBg] = V(theme.Card);
		c[(int)ImGuiCol.FrameBgHovered] = V(theme.CardHover);
		c[(int)ImGuiCol.FrameBgActive] = V(theme.Slot);
		c[(int)ImGuiCol.TitleBg] = V(theme.Sidebar);
		c[(int)ImGuiCol.TitleBgActive] = V(theme.Card);
		c[(int)ImGuiCol.TitleBgCollapsed] = V(theme.Sidebar, 0.8f);
		c[(int)ImGuiCol.MenuBarBg] = V(theme.Sidebar);
		c[(int)ImGuiCol.ScrollbarBg] = V(theme.Sidebar, 0.5f);
		c[(int)ImGuiCol.ScrollbarGrab] = V(theme.Slot);
		c[(int)ImGuiCol.ScrollbarGrabHovered] = V(theme.CardHover);
		c[(int)ImGuiCol.ScrollbarGrabActive] = V(theme.Accent, 0.8f);
		c[(int)ImGuiCol.CheckMark] = V(theme.Accent);
		c[(int)ImGuiCol.SliderGrab] = V(theme.Accent, 0.85f);
		c[(int)ImGuiCol.SliderGrabActive] = V(theme.Accent);
		c[(int)ImGuiCol.Button] = V(theme.Slot);
		c[(int)ImGuiCol.ButtonHovered] = V(theme.CardHover);
		c[(int)ImGuiCol.ButtonActive] = V(theme.Accent, 0.6f);
		c[(int)ImGuiCol.Header] = V(theme.Card);
		c[(int)ImGuiCol.HeaderHovered] = V(theme.CardHover);
		c[(int)ImGuiCol.HeaderActive] = V(theme.Slot);
		c[(int)ImGuiCol.Separator] = V(theme.Slot, 0.7f);
		c[(int)ImGuiCol.SeparatorHovered] = V(theme.Accent, 0.7f);
		c[(int)ImGuiCol.SeparatorActive] = V(theme.Accent);
		c[(int)ImGuiCol.ResizeGrip] = V(theme.Slot, 0.5f);
		c[(int)ImGuiCol.ResizeGripHovered] = V(theme.Accent, 0.6f);
		c[(int)ImGuiCol.ResizeGripActive] = V(theme.Accent);
		c[(int)ImGuiCol.Tab] = V(theme.Card);
		c[(int)ImGuiCol.TabHovered] = V(theme.CardHover);
		c[(int)ImGuiCol.TabSelected] = V(theme.Slot);
		c[(int)ImGuiCol.TabSelectedOverline] = V(theme.Accent);
		c[(int)ImGuiCol.TabDimmed] = V(theme.Card, 0.8f);
		c[(int)ImGuiCol.TabDimmedSelected] = V(theme.Slot, 0.8f);
		c[(int)ImGuiCol.PlotLines] = V(theme.Accent);
		c[(int)ImGuiCol.PlotHistogram] = V(theme.Accent);
		c[(int)ImGuiCol.TextSelectedBg] = V(theme.Accent, 0.35f);
		c[(int)ImGuiCol.NavHighlight] = V(theme.Accent);
	}

	/// <summary>TerraAngel's style: ImGui's default sizes and TerraAngel's colors.</summary>
	private static void ApplyTerraAngel()
	{
		ImGuiStylePtr style = ImGui.GetStyle();
		style.WindowRounding = 0f;
		style.ChildRounding = 0f;
		style.FrameRounding = 0f;
		style.PopupRounding = 0f;
		style.GrabRounding = 0f;
		style.TabRounding = 4f;
		style.ScrollbarRounding = 9f;
		style.WindowBorderSize = 1f;
		style.FrameBorderSize = 0f;
		style.WindowPadding = new System.Numerics.Vector2(8f, 8f);
		style.FramePadding = new System.Numerics.Vector2(4f, 3f);
		style.ItemSpacing = new System.Numerics.Vector2(8f, 4f);
		style.WindowTitleAlign = new System.Numerics.Vector2(0f, 0.5f);
		RangeAccessor<NVector4> c = style.Colors;
		c[(int)ImGuiCol.Text] = new NVector4(1.00f, 1.00f, 1.00f, 1.00f);
		c[(int)ImGuiCol.TextDisabled] = new NVector4(0.50f, 0.50f, 0.50f, 1.00f);
		c[(int)ImGuiCol.WindowBg] = new NVector4(0.10f, 0.10f, 0.10f, 1.00f);
		c[(int)ImGuiCol.ChildBg] = new NVector4(0.00f, 0.00f, 0.00f, 0.00f);
		c[(int)ImGuiCol.PopupBg] = new NVector4(0.19f, 0.19f, 0.19f, 0.92f);
		c[(int)ImGuiCol.Border] = new NVector4(0.19f, 0.19f, 0.19f, 0.29f);
		c[(int)ImGuiCol.BorderShadow] = new NVector4(0.00f, 0.00f, 0.00f, 0.24f);
		c[(int)ImGuiCol.FrameBg] = new NVector4(0.05f, 0.05f, 0.05f, 0.54f);
		c[(int)ImGuiCol.FrameBgHovered] = new NVector4(0.19f, 0.19f, 0.19f, 0.54f);
		c[(int)ImGuiCol.FrameBgActive] = new NVector4(0.20f, 0.22f, 0.23f, 1.00f);
		c[(int)ImGuiCol.TitleBg] = new NVector4(0.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.TitleBgActive] = new NVector4(0.06f, 0.06f, 0.06f, 1.00f);
		c[(int)ImGuiCol.TitleBgCollapsed] = new NVector4(0.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.MenuBarBg] = new NVector4(0.14f, 0.14f, 0.14f, 1.00f);
		c[(int)ImGuiCol.ScrollbarBg] = new NVector4(0.05f, 0.05f, 0.05f, 0.54f);
		c[(int)ImGuiCol.ScrollbarGrab] = new NVector4(0.34f, 0.34f, 0.34f, 0.54f);
		c[(int)ImGuiCol.ScrollbarGrabHovered] = new NVector4(0.40f, 0.40f, 0.40f, 0.54f);
		c[(int)ImGuiCol.ScrollbarGrabActive] = new NVector4(0.56f, 0.56f, 0.56f, 0.54f);
		c[(int)ImGuiCol.CheckMark] = new NVector4(0.33f, 0.67f, 0.86f, 1.00f);
		c[(int)ImGuiCol.SliderGrab] = new NVector4(0.34f, 0.34f, 0.34f, 0.54f);
		c[(int)ImGuiCol.SliderGrabActive] = new NVector4(0.56f, 0.56f, 0.56f, 0.54f);
		c[(int)ImGuiCol.Button] = new NVector4(0.05f, 0.05f, 0.05f, 0.54f);
		c[(int)ImGuiCol.ButtonHovered] = new NVector4(0.19f, 0.19f, 0.19f, 0.54f);
		c[(int)ImGuiCol.ButtonActive] = new NVector4(0.20f, 0.22f, 0.23f, 1.00f);
		c[(int)ImGuiCol.Header] = new NVector4(0.00f, 0.00f, 0.00f, 0.52f);
		c[(int)ImGuiCol.HeaderHovered] = new NVector4(0.00f, 0.00f, 0.00f, 0.36f);
		c[(int)ImGuiCol.HeaderActive] = new NVector4(0.20f, 0.22f, 0.23f, 0.33f);
		c[(int)ImGuiCol.Separator] = new NVector4(0.28f, 0.28f, 0.28f, 0.29f);
		c[(int)ImGuiCol.SeparatorHovered] = new NVector4(0.44f, 0.44f, 0.44f, 0.29f);
		c[(int)ImGuiCol.SeparatorActive] = new NVector4(0.40f, 0.44f, 0.47f, 1.00f);
		c[(int)ImGuiCol.ResizeGrip] = new NVector4(0.28f, 0.28f, 0.28f, 0.29f);
		c[(int)ImGuiCol.ResizeGripHovered] = new NVector4(0.44f, 0.44f, 0.44f, 0.29f);
		c[(int)ImGuiCol.ResizeGripActive] = new NVector4(0.40f, 0.44f, 0.47f, 1.00f);
		c[(int)ImGuiCol.Tab] = new NVector4(0.00f, 0.00f, 0.00f, 0.52f);
		c[(int)ImGuiCol.TabHovered] = new NVector4(0.14f, 0.14f, 0.14f, 1.00f);
		c[(int)ImGuiCol.TabSelected] = new NVector4(0.20f, 0.20f, 0.20f, 0.36f);
		c[(int)ImGuiCol.TabDimmed] = new NVector4(0.00f, 0.00f, 0.00f, 0.52f);
		c[(int)ImGuiCol.TabDimmedSelected] = new NVector4(0.14f, 0.14f, 0.14f, 1.00f);
		c[(int)ImGuiCol.PlotLines] = new NVector4(1.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.PlotLinesHovered] = new NVector4(1.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.PlotHistogram] = new NVector4(1.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.PlotHistogramHovered] = new NVector4(1.00f, 0.00f, 0.00f, 1.00f);
		c[(int)ImGuiCol.TableHeaderBg] = new NVector4(0.00f, 0.00f, 0.00f, 0.52f);
		c[(int)ImGuiCol.TableBorderStrong] = new NVector4(0.00f, 0.00f, 0.00f, 0.52f);
		c[(int)ImGuiCol.TableBorderLight] = new NVector4(0.28f, 0.28f, 0.28f, 0.29f);
		c[(int)ImGuiCol.TableRowBg] = new NVector4(0.00f, 0.00f, 0.00f, 0.00f);
		c[(int)ImGuiCol.TableRowBgAlt] = new NVector4(1.00f, 1.00f, 1.00f, 0.06f);
		c[(int)ImGuiCol.TextSelectedBg] = new NVector4(0.20f, 0.22f, 0.23f, 1.00f);
		c[(int)ImGuiCol.DragDropTarget] = new NVector4(0.33f, 0.67f, 0.86f, 1.00f);
		c[(int)ImGuiCol.NavWindowingHighlight] = new NVector4(1.00f, 0.00f, 0.00f, 0.70f);
		c[(int)ImGuiCol.NavWindowingDimBg] = new NVector4(1.00f, 0.00f, 0.00f, 0.20f);
		c[(int)ImGuiCol.ModalWindowDimBg] = new NVector4(0f, 0.00f, 0.00f, 0f);
		c[(int)ImGuiCol.NavHighlight] = new NVector4(1.00f, 0.00f, 0.00f, 1.00f);
	}

	private static NVector4 V(Color color, float alpha = 1f)
	{
		return new NVector4(color.R / 255f, color.G / 255f, color.B / 255f, alpha);
	}
}
