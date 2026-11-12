using Terraria.UI;
using IsaClient.Core;
using IsaClient.Rendering;

namespace IsaClient.Menu;

/// <summary>State of the client's own user interfaces (panel open, hidden menu UI, mouse over).</summary>
public static class MenuState
{
	internal static bool LastPanelOpen;

	internal static bool LastInMenu = true;

	public static bool UiRework = true;

	public static UserInterface CustomUI = new UserInterface();

	public static UserInterface CustomUI2 = new UserInterface();

	public static bool HideCustomUI;

	public static string CustomTextOverride = "";

	public static bool CommandPanelSelectedItemButton;

	public static bool CustomMouseText
	{
		get
		{
			if (InfoOverlays.SeeTileData)
			{
				return CustomTextOverride != "";
			}
			return false;
		}
	}

	public static bool CustomUIMousedOver
	{
		get
		{
			if (InputState.TildeTerminal && IsaClient.Hub.ClientHub.TakesPanel)
			{
				return IsaClient.Hub.ClientHub.MouseOver;
			}
			if (!InputState.TildeTerminal || CustomUI2.CurrentState != CheatPanel.UiStateNewCheatPanel || !CheatPanel.MousedOver)
			{
				if (InputState.TildeTerminal && CustomUI2.CurrentState is IsaClient.Menu.InventoryViewer)
				{
					return IsaClient.Menu.InventoryViewer.MousedOver;
				}
				if (InputState.TildeTerminal && CustomUI2.CurrentState is CharacterWindow)
				{
					return CharacterWindow.MousedOver;
				}
				return false;
			}
			return true;
		}
	}

	public static bool DraggingCustomUISlider
	{
		get
		{
			if (InputState.TildeTerminal && IsaClient.Hub.ClientHub.TakesPanel)
			{
				return IsaClient.Hub.ClientHub.Dragging;
			}
			if (InputState.TildeTerminal && CustomUI2.CurrentState == CheatPanel.UiStateNewCheatPanel)
			{
				if (!CheatPanel.DraggingSlider)
				{
					return CheatPanel.DraggingScrollbar;
				}
				return true;
			}
			return false;
		}
	}

	[Setting("HideMenuUuidUi", Remembered = true)]
	public static bool HideGameMenuUi;
}
