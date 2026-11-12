using System;
using IsaClient.Building;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Building blocks of panel rows: toggles, buttons, sliders, cycles, click sounds and tooltips.</summary>
public static class MenuControls
{
	internal static UIElement MakeToggle(string cheat, Color color, Func<bool> getter, Action<UIMouseEvent> onClick)
	{
		MenuToggle element = new MenuToggle(() => cheat, getter, color);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		element.OnLeftClick += delegate(UIMouseEvent evt, UIElement el)
		{
			onClick(evt);
		};
		return element;
	}

	internal static UIElement MakeButton(string cheat, Color color, Func<string> getText, Action onClick, Action onRightClick = null)
	{
		MenuSimpleItem element = new MenuSimpleItem(getText, color);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		element.OnLeftClick += delegate
		{
			onClick();
		};
		if (onRightClick != null)
		{
			element.OnRightClick += delegate
			{
				onRightClick();
			};
		}
		return element;
	}

	internal static UIElement MakeSlider(string cheat, Color color, Func<string> getText, Func<float> get, Action<float> set, int id)
	{
		float current = get();
		UIElement slider = new MenuSlider(getText, get, delegate(float f)
		{
			current = f;
			set(f);
		}, delegate
		{
			current = UILinksInitializer.HandleSliderHorizontalInput(current, 0f, 1f, 1f, 0.35f);
			set(current);
		}, id, color);
		slider.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		slider.OnUpdate += delegate(UIElement element)
		{
			ShowTooltip(element, cheat);
		};
		return slider;
	}

	internal static float NextStep(float value, float[] steps)
	{
		foreach (float step in steps)
		{
			if (step > value + 0.001f)
			{
				return step;
			}
		}
		return steps[0];
	}

	internal static void OnToggleClicked(UIMouseEvent evt, string buttonName, ref bool boolean, bool oneSound = false)
	{
		if (!(evt.Target is MenuToggle) && !(evt.Target is MenuListItem))
		{
			return;
		}
		boolean = !boolean;
		if (!(buttonName == "Selection Mode"))
		{
			if (buttonName == "Paste Mode")
			{
				Selection.SelectionMode = false;
				Selection.CopyMode = false;
				Selection.CancelPaste = false;
			}
		}
		else
		{
			Selection.PasteMode = false;
			Selection.CopyMode = false;
			Selection.CancelAction = false;
		}
		if (!oneSound)
		{
			SoundEngine.PlaySound(boolean ? 10 : 11);
		}
		else
		{
			SoundEngine.PlaySound(11);
		}
	}

	internal static void OnCycleClicked(UIMouseEvent evt, string buttonName, int minCount, int maxCount, ref int integer, bool goBack = false)
	{
		if (!(evt.Target is MenuListItem))
		{
			return;
		}
		if (!goBack)
		{
			integer++;
			if (buttonName == "Force Dash Type" && integer == 4)
			{
				integer++;
			}
			if (integer > maxCount)
			{
				integer = minCount;
			}
		}
		else
		{
			integer--;
			if (buttonName == "Force Dash Type" && integer == 4)
			{
				integer--;
			}
			if (integer < minCount)
			{
				integer = maxCount;
			}
		}
		SoundEngine.PlaySound((integer != 0) ? 10 : 11);
	}

	internal static void ShowTooltip(UIElement element, string buttonName)
	{
		if (!element.IsMouseHovering)
		{
			return;
		}
		string tooltip = TooltipFor(buttonName);
		if (tooltip != null)
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T(tooltip), 0, 0);
		}
	}

	/// <summary>The hover text of a row (null: none).</summary>
	internal static string TooltipFor(string buttonName)
	{
		return HeldItemMenu.Tooltip(buttonName) ?? CategoryTooltip(buttonName) ?? SharedTooltip(buttonName);
	}

	private static readonly System.Func<string, string>[] CategoryTooltips =
	{
		RenderMenu.Tooltip,
		PlayerMenu.Tooltip,
		AiMenu.Tooltip,
		WorldEditMenu.Tooltip,
		MusicMenu.Tooltip,
		MiscMenu.Tooltip,
		InfoMenu.Tooltip,
		TravelMenu.Tooltip,
		CosmeticMenu.Tooltip,
		BiomeSpooferMenu.Tooltip,
		AudioMenu.Tooltip,
		WorldInfoMenu.Tooltip,
	};

	private static string CategoryTooltip(string name)
	{
		foreach (System.Func<string, string> tooltip in CategoryTooltips)
		{
			string text = tooltip(name);
			if (text != null)
			{
				return text;
			}
		}
		return null;
	}

	/// <summary>Texts shared by several categories, and rows no category lists.</summary>
	private static string SharedTooltip(string name)
	{
		return name switch
		{
			"Disable & Reset All" => "Disables everything in this category and resets its values to default.",
			"Disable All" => "Disables everything in this category and resets its values to default.",
			"Load Tiles from Schematic" => "Load tiles to paste from a TEdit Schematic",
			_ => null,
		};
	}

	internal static UIElement MakeCycle(string cheat, Color color, Action<bool> cycle, Func<bool> disabled = null)
	{
		MenuListItem element = new MenuListItem(cheat, color, disabled);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		element.OnLeftClick += delegate(UIMouseEvent evt, UIElement el)
		{
			if (evt.Target is MenuListItem && (disabled == null || !disabled()))
			{
				cycle(false);
			}
		};
		element.OnRightClick += delegate(UIMouseEvent evt, UIElement el)
		{
			if (evt.Target is MenuListItem && (disabled == null || !disabled()))
			{
				cycle(true);
			}
		};
		return element;
	}

	internal static UIElement MakeInfo(string cheat, Color color, Action onClick = null, Action onRightClick = null, Func<bool> disabled = null)
	{
		MenuListItem element = new MenuListItem(cheat, color, disabled);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		if (onClick != null)
		{
			element.OnLeftClick += delegate
			{
				onClick();
			};
		}
		if (onRightClick != null)
		{
			element.OnRightClick += delegate
			{
				onRightClick();
			};
		}
		return element;
	}

	/// <summary>A toggle for any bool that has a getter and a setter (fields, properties, world flags).</summary>
	internal static UIElement MakeFlag(string cheat, Color color, Func<bool> get, Action<bool> set, Func<bool> canChange = null, Func<string> label = null)
	{
		MenuToggle element = new MenuToggle(label ?? ((Func<string>)(() => cheat)), get, color);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		element.OnLeftClick += delegate(UIMouseEvent evt, UIElement el)
		{
			if (!(evt.Target is MenuToggle) || (canChange != null && !canChange()))
			{
				return;
			}
			bool value = !get();
			set(value);
			SoundEngine.PlaySound(value ? 10 : 11);
		};
		return element;
	}

	/// <summary>A display-only toggle (shows a world state you can't flip from here).</summary>
	internal static UIElement MakeState(string cheat, Color color, Func<bool> get)
	{
		MenuToggle element = new MenuToggle(() => cheat, get, color);
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		return element;
	}

	internal static UIElement MakeAction(string cheat, Color color, Func<string> getText, Action onClick, Func<bool> disabled = null)
	{
		MenuSimpleItem element = new MenuSimpleItem(getText, color, disabled);
		element.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		element.OnUpdate += delegate(UIElement el)
		{
			ShowTooltip(el, cheat);
		};
		element.OnLeftClick += delegate
		{
			if (disabled == null || !disabled())
			{
				onClick();
			}
		};
		return element;
	}

	private static void PlayCycleSound(int value)
	{
		SoundEngine.PlaySound((value != 0) ? 10 : 11);
	}

	internal static int Cycle(int value, int min, int max, bool goBack)
	{
		value += goBack ? (-1) : 1;
		if (value > max)
		{
			value = min;
		}
		if (value < min)
		{
			value = max;
		}
		PlayCycleSound(value);
		return value;
	}

	private static MenuSimpleItem SmallButton(string label, float widthPx, float hAlign, Color color, Action onClick, Func<string> dynamicLabel = null)
	{
		MenuSimpleItem button = new MenuSimpleItem(dynamicLabel ?? ((Func<string>)(() => label)), color, () => false);
		button.Width.Set(widthPx, 0f);
		button.Height.Set(28f, 0f);
		button.VAlign = 0.5f;
		button.HAlign = hAlign;
		button.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		if (onClick != null)
		{
			button.OnLeftClick += delegate
			{
				onClick();
			};
		}
		return button;
	}

	private static UIElement Header(string text)
	{
		UIText header = new UIText(IsaClient.Languages.L10n.T(text), 0.9f);
		header.Width.Set(0f, 1f);
		header.Height.Set(26f, 0f);
		return header;
	}

	/// <summary>Places a right/left aligned button at a pixel offset from its edge (HAlign already 0 or 1).</summary>
	private static MenuSimpleItem Offset(MenuSimpleItem button, float xPixels)
	{
		if (button != null)
		{
			button.Left.Set(xPixels, 0f);
		}
		return button;
	}
}
