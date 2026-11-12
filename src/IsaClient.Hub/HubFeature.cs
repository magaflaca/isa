using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

using IsaClient.Languages;

namespace IsaClient.Hub;

/// <summary>How a feature is controlled (switch, value, slider, button...).</summary>
internal enum FeatureKind
{
	None,
	/// <summary>On/off switch.</summary>
	Toggle,
	/// <summary>On/off shown but not clickable (a world state).</summary>
	State,
	/// <summary>A value that changes with left click (next) and right click (previous).</summary>
	Choice,
	/// <summary>A bar from 0 to 1.</summary>
	Slider,
	/// <summary>A button that does something.</summary>
	Action,
	/// <summary>A value to read (clickable when it has an action).</summary>
	Info,
	/// <summary>A color: a swatch that opens the color picker.</summary>
	Color
}

/// <summary>
/// One feature of the new panel: what it is called, where it lives, how it looks and how it is controlled. Most features
/// drive a row of the classic panel (<see cref="LegacyRow"/>); the panel's own settings use delegates.
/// </summary>
internal sealed class HubFeature
{
	public string Id;

	public string Title;

	/// <summary>One short line under the title.</summary>
	public string Description;

	/// <summary>Extra text for the hover box (the classic panel's tooltip when null).</summary>
	public string Details;

	public HubSection Section;

	public HubGroup Group;

	public HubIcon Icon;

	/// <summary>Extra words the search knows this feature by.</summary>
	public string Keywords = "";

	/// <summary>The chat command that does the same (shown as a hint, searchable).</summary>
	public string Command;

	/// <summary>Label of an action button ("Run" when null).</summary>
	public string Verb;

	/// <summary>Takes the whole width of the content (long values).</summary>
	public bool Wide;

	/// <summary>Counts in "active features" and can be turned off by "Turn all off".</summary>
	public bool Cheat = true;

	/// <summary>A second button on the same card (e.g. Summon next to a boss).</summary>
	public HubFeature Companion;

	public LegacyRow Row;

	// Native controls (when there is no classic row)
	public FeatureKind NativeKind;

	public Func<bool> GetOn;

	public Action<bool> SetOn;

	public Func<string> GetValue;

	public Action<bool> Cycle;

	public Func<float> GetSlider;

	public Action<float> SetSlider;

	public Action Run;

	public Func<bool> IsDisabled;

	public Func<Color> GetColor;

	/// <summary>Called on every change while the picker is open (keep it cheap).</summary>
	public Action<Color> SetColor;

	/// <summary>Called once when the picker closes (save settings here).</summary>
	public Action ColorDone;

	/// <summary>The color the picker's Default button puts back (none when null).</summary>
	public Color? DefaultColor;

	/// <summary>Native action that needs a second click.</summary>
	public bool Confirm;

	public FeatureKind Kind => (Row != null) ? Row.Kind : NativeKind;

	public bool IsOn
	{
		get
		{
			if (Row != null)
			{
				return Row.IsOn;
			}
			try
			{
				return GetOn != null && GetOn();
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
			if (Section != null && IsaClient.Networking.ServerModes.BlocksHubSection(Section.Id))
			{
				return true;
			}
			if (Row != null)
			{
				return Row.Disabled;
			}
			try
			{
				return IsDisabled != null && IsDisabled();
			}
			catch
			{
				return false;
			}
		}
	}

	public bool NeedsConfirm => (Row != null) ? Row.NeedsConfirm : Confirm;

	public bool HasSecondary => (Row != null) ? Row.HasRightClick : (Cycle != null);

	/// <summary>The value text of a choice, slider, info or dynamic button.</summary>
	public string ValueText(out bool missing)
	{
		missing = false;
		try
		{
			if (Row == null)
			{
				return GetValue?.Invoke();
			}
			switch (Row.Kind)
			{
			case FeatureKind.Choice:
			case FeatureKind.Info:
				return Row.Value(out missing);
			case FeatureKind.Slider:
				return AfterColon(Row.Label);
			case FeatureKind.Action:
			{
				string label = Row.Label;
				return (label == Row.Name) ? null : label;
			}
			case FeatureKind.Toggle:
			case FeatureKind.State:
			{
				string label = Row.Label;
				return (label == Row.Name) ? null : AfterColon(label);
			}
			}
		}
		catch
		{
			missing = true;
		}
		return null;
	}

	/// <summary>"Kill Aura Size: 20 blocks" → "20 blocks".</summary>
	private static string AfterColon(string label)
	{
		if (string.IsNullOrEmpty(label))
		{
			return label;
		}
		int colon = label.LastIndexOf(": ", StringComparison.Ordinal);
		return (colon >= 0) ? label.Substring(colon + 2) : label;
	}

	public float Slider
	{
		get
		{
			if (Row != null)
			{
				return Row.SliderValue;
			}
			try
			{
				return GetSlider?.Invoke() ?? 0f;
			}
			catch
			{
				return 0f;
			}
		}
	}

	public void Slide(float value)
	{
		if (Row != null)
		{
			Row.SetSlider(value);
		}
		else
		{
			SetSlider?.Invoke(value);
		}
	}

	/// <summary>Left click on the card (toggle, next value, run).</summary>
	public void Primary()
	{
		if (Row != null)
		{
			Row.LeftClick();
			return;
		}
		switch (NativeKind)
		{
		case FeatureKind.Toggle:
			SetOn?.Invoke(!IsOn);
			break;
		case FeatureKind.Choice:
			Cycle?.Invoke(false);
			break;
		case FeatureKind.Color:
			HubColorPicker.Open(this);
			break;
		default:
			Run?.Invoke();
			break;
		}
	}

	/// <summary>Right click on the control (previous value, the row's second action).</summary>
	public void Secondary()
	{
		if (Row != null)
		{
			Row.RightClick();
		}
		else
		{
			Cycle?.Invoke(true);
		}
	}

	/// <summary>The hover text: the feature's own details, else the classic tooltip.</summary>
	public string Tooltip
	{
		get
		{
			string text = Details;
			if (text == null && Row != null)
			{
				text = IsaClient.Menu.MenuControls.TooltipFor(Row.Name);
			}
			if (text == null)
			{
				text = Description;
			}
			if (!string.IsNullOrEmpty(Command) && (text == null || !text.Contains(Command)))
			{
				text = (text == null) ? ("Command: " + Command) : (text + "\nCommand: " + Command);
			}
			return text;
		}
	}

	/// <summary>Everything the search looks at, lower case: in English and in the player's language.</summary>
	public string SearchText
	{
		get
		{
			if (_searchText == null || _searchLanguage != L10n.Version)
			{
				List<string> parts = new List<string> { Title, Description, Keywords, Command, Row?.Name, Group?.Title, Section?.Title };
				parts.AddRange(new[] { Title, Description, Group?.Title, Section?.Title }.Where(p => !string.IsNullOrEmpty(p)).Select(L10n.T));
				_searchText = string.Join(" | ", parts.FindAll(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
				_searchLanguage = L10n.Version;
			}
			return _searchText;
		}
	}

	private int _searchLanguage = -1;

	private string _searchText;
}
