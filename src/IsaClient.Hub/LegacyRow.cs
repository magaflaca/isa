using System;
using IsaClient.Core;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace IsaClient.Hub;

/// <summary>
/// One row of the classic panel, driven by the new panel: the row's control is built by its category exactly like the
/// classic panel builds it (same getters, setters, sounds and side effects), but it is never drawn. The new panel reads
/// its text and state and sends it the clicks, so a feature behaves the same in both interfaces.
/// </summary>
internal sealed class LegacyRow
{
	private readonly CheatCategory _category;

	private UIElement _element;

	private bool _built;

	public LegacyRow(CheatCategory category, string name)
	{
		_category = category;
		Name = name;
	}

	/// <summary>The row name in the classic panel (also its tooltip key).</summary>
	public string Name { get; }

	public string CategoryTitle => _category.Title;

	private UIElement Element
	{
		get
		{
			if (!_built)
			{
				_built = true;
				try
				{
					_element = _category.Create(Name, _category.Tint);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
					_element = null;
				}
			}
			return _element;
		}
	}

	public FeatureKind Kind
	{
		get
		{
			switch (Element)
			{
			case MenuToggle toggle:
				return toggle.HasLeftClickHandler ? FeatureKind.Toggle : FeatureKind.State;
			case MenuSlider:
				return FeatureKind.Slider;
			case MenuListItem item:
				return (item.HasLeftClickHandler || item.HasRightClickHandler || item.HasLeftDoubleClickHandler) ? FeatureKind.Choice : FeatureKind.Info;
			case MenuSimpleItem:
				return FeatureKind.Action;
			default:
				return FeatureKind.None;
			}
		}
	}

	/// <summary>The text the classic row shows (toggle/slider/button label).</summary>
	public string Label
	{
		get
		{
			try
			{
				switch (Element)
				{
				case MenuToggle toggle:
					return toggle.LabelText;
				case MenuSlider slider:
					return slider.LabelText;
				case MenuSimpleItem button:
					return button.LabelText;
				case MenuListItem item:
					return item.Name;
				}
			}
			catch
			{
			}
			return Name;
		}
	}

	/// <summary>The value on the right of a value row (null for other rows).</summary>
	public string Value(out bool missing)
	{
		missing = false;
		if (Element is MenuListItem item)
		{
			try
			{
				return item.DisplayValue(out missing);
			}
			catch
			{
				missing = true;
				return "Error!";
			}
		}
		return null;
	}

	public bool IsOn
	{
		get
		{
			try
			{
				return Element is MenuToggle toggle && toggle.IsOn;
			}
			catch
			{
				return false;
			}
		}
	}

	public bool Disabled
	{
		get
		{
			try
			{
				switch (Element)
				{
				case MenuToggle toggle:
					return toggle.DisableCondition();
				case MenuSimpleItem button:
					return button.DisableCondition();
				case MenuListItem item:
					return item.DisableCondition();
				}
			}
			catch
			{
			}
			return false;
		}
	}

	/// <summary>Rows that act on a double click in the classic panel (delete or create a preset...).</summary>
	public bool NeedsConfirm => Element != null && Element.HasLeftDoubleClickHandler && !Element.HasLeftClickHandler;

	public bool HasRightClick => Element != null && Element.HasRightClickHandler;

	public float SliderValue
	{
		get
		{
			try
			{
				return (Element is MenuSlider slider) ? MathHelper.Clamp(slider.Value, 0f, 1f) : 0f;
			}
			catch
			{
				return 0f;
			}
		}
	}

	public void SetSlider(float value)
	{
		if (Element is MenuSlider slider)
		{
			Run(() => slider.SetValue(MathHelper.Clamp(value, 0f, 1f)));
		}
	}

	public void LeftClick()
	{
		UIElement element = Element;
		if (element == null)
		{
			return;
		}
		if (NeedsConfirm)
		{
			Run(() => element.LeftDoubleClick(Event(element)));
			return;
		}
		Run(() => element.LeftClick(Event(element)));
	}

	public void RightClick()
	{
		UIElement element = Element;
		if (element != null)
		{
			Run(() => element.RightClick(Event(element)));
		}
	}

	private static UIMouseEvent Event(UIElement element)
	{
		return new UIMouseEvent(element, new Vector2(Main.mouseX, Main.mouseY));
	}

	private static void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("That did not work: " + ex.Message);
		}
	}
}
