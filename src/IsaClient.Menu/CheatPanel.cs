using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.Localization;
using Terraria.UI;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// The in-game panel window (opened with the "Open Menu" key): the tabs, the scrolling list and one box per category
/// of <see cref="CheatCategories"/>. The rows themselves live in the category classes (TravelMenu, PlayerMenu, ...).
/// </summary>
public class CheatPanel : UIState
{
	private List<UIElement> _cheatCategories = new List<UIElement>();

	private readonly List<UIElement> _worldInfoCategories = new List<UIElement>();

	private UIElement _outerContainer;

	private UIList _uilist;

	public static CheatPanel UiStateNewCheatPanel = new CheatPanel();

	public static bool MousedOver;

	public static bool DraggingSlider;

	public static bool DraggingScrollbar;

	public UIElement ModUIPanel;

	private UIScrollbar _uiScrollbarCommon;

	private UIScrollbar _uiScrollbarCheats;

	private Color _uiBackgroundColor;

	private int _activePanel;

	/// <summary>Which tab page is shown: 0 Cheats, 1 Player List, 2 Bots.</summary>
	private int _activePage;

	private int _pageRefreshTimer;

	public static int PrevPanel = -1;

	private UITextPanel<LocalizedText> _uiTextPanel;

	public override void OnInitialize()
	{
		if (PrevPanel != -1)
		{
			_activePanel = PrevPanel;
		}
		UIElement uIElement = new UIElement();
		uIElement.Width.Set(0f, 0.8f);
		uIElement.MaxWidth.Set(600f, 0f);
		uIElement.Top.Set(220f, 0f);
		uIElement.Height.Set(-220f, 1f);
		uIElement.HAlign = 0.5f;
		_outerContainer = uIElement;
		UIPanel uIPanel = new UIPanel();
		uIPanel.Width.Set(0f, 1f);
		uIPanel.Height.Set(-90f, 1f);
		uIPanel.BackgroundColor = new Color(33, 43, 79) * 0.8f;
		_uiBackgroundColor = uIPanel.BackgroundColor;
		ModUIPanel = uIPanel;
		uIElement.Append(uIPanel);
		// Tab row: Player List / World Information / Bots / Cheats. The proxy lives in the main menu.
		string[] tabNames = new string[4] { "Player List", "World Information", "Bots", "Cheats" };
		int[] tabPages = new int[4] { 1, 3, 2, 0 };
		for (int t = 0; t < 4; t++)
		{
			int page = tabPages[t];
			MenuSimpleItem tab = CreateTabButton(tabNames[t], t / 3f, page);
			uIPanel.Append(tab);
		}
		_uilist = new UIList();
		_uilist.Width.Set(-25f, 1f);
		_uilist.Height.Set(-50f, 1f);
		_uilist.VAlign = 1f;
		_uilist.PaddingBottom = 5f;
		_uilist.ListPadding = 10f;
		uIPanel.Append(_uilist);
		AssembleCategories();
		FillList();
		UIScrollbar uIScrollbar = new UIScrollbar();
		uIScrollbar.SetView(100f, 1000f);
		uIScrollbar.Height.Set(-67f, 1f);
		uIScrollbar.HAlign = 1f;
		uIScrollbar.VAlign = 1f;
		uIScrollbar.MarginBottom = 11f;
		uIPanel.Append(uIScrollbar);
		_uiScrollbarCheats = uIScrollbar;
		_uiScrollbarCommon = _uiScrollbarCheats;
		_uilist.SetScrollbar(uIScrollbar);
		_botsPanel = new IsaClient.MultiClient.MultiClientPanel();
		UITextPanel<LocalizedText> uITextPanel = new UITextPanel<LocalizedText>(Language.GetText("IsaClient: Cheats"), 0.7f, large: true);
		uITextPanel.HAlign = 0.5f;
		uITextPanel.Top.Set(-45f, 0f);
		uITextPanel.Left.Set(-10f, 0f);
		uITextPanel.SetPadding(15f);
		uITextPanel.BackgroundColor = new Color(73, 94, 171);
		_uiTextPanel = uITextPanel;
		uIElement.Append(uITextPanel);
		Append(uIElement);
	}

	private MenuSimpleItem CreateTabButton(string name, float hAlign, int page)
	{
		bool available = page >= 0;
		MenuSimpleItem button = new MenuSimpleItem(() => (available && _activePage == page) ? ("[c/FFE645:" + name + "]") : (available ? name : ("[c/808080:" + name + "]")), available ? (new Color(73, 94, 171, 255) * 0.9f) : (new Color(50, 60, 100, 255) * 0.8f), () => true);
		button.VAlign = 0f;
		button.HAlign = hAlign;
		button.Width.Set(140f, 0f);
		button.Height.Set(30f, 0f);
		button.MarginRight = 0f;
		button.Left.Set(0f, 0f);
		button.Top.Set(8f, 0f);
		if (!available)
		{
			button.OnUpdate += delegate(UIElement element)
			{
				if (element.IsMouseHovering)
				{
					Main.instance.MouseText(IsaClient.Languages.L10n.T(name + " is not available yet."), 0, 0);
				}
			};
		}
		else
		{
			button.OnLeftClick += delegate
			{
				if (_activePage != page)
				{
					SoundEngine.PlaySound(12);
					_activePage = page;
					RenderPage();
				}
			};
		}
		return button;
	}

	/// <summary>Builds one box per category of both tabs (see <see cref="CheatCategories"/>).</summary>
	private void AssembleCategories()
	{
		for (int i = 0; i < CheatCategories.CheatsTab.Length; i++)
		{
			_cheatCategories.Add(CreateCategoryBox(CheatCategories.CheatsTab[i], i));
		}
		for (int i = 0; i < CheatCategories.WorldInfoTab.Length; i++)
		{
			_worldInfoCategories.Add(CreateCategoryBox(CheatCategories.WorldInfoTab[i], i));
		}
	}

	private UISortableElement CreateCategoryBox(CheatCategory category, int order)
	{
		UISortableElement box = new UISortableElement(order);
		box.HAlign = 0.5f;
		box.Width.Set(0f, 1f);
		box.Height.Set(2000f, 0f);
		UIPanel panel = new UIPanel();
		panel.Width.Set(0f, 1f);
		panel.Height.Set(-16f, 1f);
		panel.VAlign = 1f;
		panel.BackgroundColor = Color.Lerp(new Color(33, 43, 79) * 0.8f, category.Tint, 0.18f);
		box.Append(panel);
		UIList rows = new UIList();
		rows.OverflowHidden = false;
		rows.Width.Set(0f, 1f);
		rows.Height.Set(-8f, 1f);
		rows.VAlign = 1f;
		rows.ListPadding = 5f;
		panel.Append(rows);
		AddRows(rows, category, panel.BackgroundColor);
		panel.BackgroundColor = panel.BackgroundColor.MultiplyRGBA(new Color(111, 111, 111));
		UITextPanel<LocalizedText> header = new UITextPanel<LocalizedText>(Language.GetText(category.Title), 0.7f)
		{
			VAlign = 0f,
			HAlign = 0.5f
		};
		category.DecorateHeader?.Invoke(header);
		box.Append(header);
		box.Recalculate();
		box.Width.Set(0f, 1f);
		box.Height.Set(rows.GetTotalHeight() + 30f + 16f, 0f);
		return box;
	}

	/// <summary>Rows go two per line unless a row is listed as full-width or centered half-width.</summary>
	private static void AddRows(UIList parent, CheatCategory category, Color color)
	{
		IReadOnlyList<string> entries = category.Entries;
		for (int i = 0; i < entries.Count; i++)
		{
			string name = entries[i];
			UISortableElement line = new UISortableElement(i);
			line.Width.Set(0f, 1f);
			line.Height.Set(30f, 0f);
			line.HAlign = 0.5f;
			parent.Add(line);
			UIElement element = category.Create(name, color);
			if (PanelLayout.IsHalfWidthCentered(name))
			{
				element.Width.Set(0f, 0.5f);
				element.HAlign = 0.5f;
				element.Height.Set(0f, 1f);
				line.Append(element);
				continue;
			}
			if (PanelLayout.IsFullWidth(name))
			{
				element.Width.Set(0f, 1f);
				element.Height.Set(0f, 1f);
				line.Append(element);
				continue;
			}
			element.Width.Set(-5f, 0.5f);
			element.Height.Set(0f, 1f);
			line.Append(element);
			if (i + 1 < entries.Count && !PanelLayout.HasOwnLine(entries[i + 1]))
			{
				i++;
				UIElement second = category.Create(entries[i], color);
				second.Width.Set(-5f, 0.5f);
				second.Height.Set(0f, 1f);
				second.HAlign = 1f;
				line.Append(second);
			}
			else
			{
				// Alone in its row (next one has its own line, or it is the last): use the whole row, no empty half.
				element.Width.Set(0f, 1f);
			}
		}
	}

	public override void OnActivate()
	{
		_outerContainer.Top.Set(120f, 0f);
		_outerContainer.Height.Set(-120f, 1f);
	}

	private void FillList()
	{
		RenderPage();
	}

	private static readonly string[] _pageTitles = new string[4] { "IsaClient: Cheats", "IsaClient: Player List", "IsaClient: Bots", "IsaClient: World Information" };

	/// <summary>Rebuilds the scrolling list for the active tab page.</summary>
	private IsaClient.MultiClient.MultiClientPanel _botsPanel;

	/// <summary>The Bots page draws itself (its own layout and scrolling) in place of the shared list + scrollbar.</summary>
	private void ShowBotsPanel(bool show)
	{
		if (ModUIPanel == null || _botsPanel == null)
		{
			return;
		}
		if (show)
		{
			if ((_uilist.Parent == ModUIPanel))
			{
				ModUIPanel.RemoveChild(_uilist);
			}
			if ((_uiScrollbarCheats.Parent == ModUIPanel))
			{
				ModUIPanel.RemoveChild(_uiScrollbarCheats);
			}
			if (!(_botsPanel.Parent == ModUIPanel))
			{
				ModUIPanel.Append(_botsPanel);
			}
		}
		else
		{
			if ((_botsPanel.Parent == ModUIPanel))
			{
				ModUIPanel.RemoveChild(_botsPanel);
			}
			if (!(_uilist.Parent == ModUIPanel))
			{
				ModUIPanel.Append(_uilist);
			}
			if (!(_uiScrollbarCheats.Parent == ModUIPanel))
			{
				ModUIPanel.Append(_uiScrollbarCheats);
			}
		}
		ModUIPanel.Recalculate();
	}

	public void RenderPage()
	{
		_uilist.Clear();
		_pageSignature = PageSignature();
		ShowBotsPanel(_activePage == 2);
		switch (_activePage)
		{
		case 1:
			BuildPlayerListPage();
			break;
		case 2:
			break;
		case 3:
			foreach (UIElement item in _worldInfoCategories)
			{
				_uilist.Add(item);
			}
			break;
		default:
			foreach (UIElement item in _cheatCategories)
			{
				_uilist.Add(item);
			}
			break;
		}
		if (_uiTextPanel != null)
		{
			_uiTextPanel.SetText(Language.GetText(_pageTitles[Utils.Clamp(_activePage, 0, 3)]));
		}
	}

	public override void Draw(SpriteBatch spriteBatch)
	{
		base.Draw(spriteBatch);
	}

	public override void Update(GameTime gameTime)
	{
		try
		{
			if (MenuState.CustomUI2.CurrentState != UiStateNewCheatPanel)
			{
				MousedOver = false;
			}
			base.Update(gameTime);
			if (ModUIPanel.IsMouseHovering)
			{
				Main.LocalPlayer.mouseInterface = true;
				MousedOver = true;
			}
			DraggingScrollbar = _uiScrollbarCommon._isDragging;
			_uilist.ListPadding = (_activePage == 1) ? 5f : 10f;
			if (!DraggingSlider)
			{
				UpdateSliders();
			}
			if (_activePage != 0 && !DraggingScrollbar)
			{
				if (--_pageRefreshTimer <= 0)
				{
					_pageRefreshTimer = 30;
					RefreshPageIfChanged();
				}
			}
		}
		catch (Exception ex)
		{
			if (_refreshCooldown <= 0)
			{
				_refreshCooldown = 120;
				ClientLog.LogException(ex);
				RefreshUI();
			}
		}
		if (_refreshCooldown > 0)
		{
			_refreshCooldown--;
		}
	}

	private static int _refreshCooldown;

	public static void RefreshUI()
	{
		PrevPanel = UiStateNewCheatPanel._activePanel;
		UiStateNewCheatPanel = new CheatPanel();
		IsaClient.Menu.InventoryViewer.Reset();
		IsaClient.Menu.CharacterWindow.Reset();
		if (MenuState.CustomUI2 != null)
		{
			MenuState.CustomUI2.SetState(UiStateNewCheatPanel);
		}
	}

	private void UpdateSliders()
	{
		if (WorldEditMenu.SyncSliders())
		{
			MiscMenu.SyncSliders();
		}
	}

	private string _pageSignature;

	/// <summary>Cheap fingerprint of what the active page lists; the page is rebuilt only when it changes.</summary>
	private string PageSignature()
	{
		switch (_activePage)
		{
		default:
			return "";
		}
	}

	/// <summary>Called periodically from Update: rebuilds the page if the listed things changed (never while typing).</summary>
	private void RefreshPageIfChanged()
	{
		string signature = PageSignature();
		if (signature != _pageSignature)
		{
			RenderPage();
		}
	}

	#region Player List page

	private List<UIElement> _listOfPlayers;

	/// <summary>
	/// Player List: one row per player index (0-255, 255 = the server), in index order. The rows are built once and
	/// redraw themselves from the live players, so the page never needs rebuilding.
	/// </summary>
	private void BuildPlayerListPage()
	{
		if (_listOfPlayers == null)
		{
			_listOfPlayers = new List<UIElement>();
			for (int i = 0; i < Main.player.Length; i++)
			{
				Terraria.GameContent.UI.States.UISortableElement holder = new Terraria.GameContent.UI.States.UISortableElement(i);
				holder.HAlign = 0.5f;
				holder.Width.Set(0f, 1f);
				holder.Height.Set(70f, 0f);
				if (i == 0)
				{
					holder.PaddingTop = 4f;
					holder.Height.Set(74f, 0f);
				}
				holder.Append(new CharacterListItem(i));
				_listOfPlayers.Add(holder);
			}
		}
		_uilist.AddRange(_listOfPlayers);
	}
	#endregion
}
