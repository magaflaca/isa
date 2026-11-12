using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Menu;

namespace IsaClient.Gameplay;

/// <summary>Auto clicker: holds or clicks the mouse buttons at a set speed, with optional hold/release cycles and a time limit.</summary>
public static class AutoClicker
{
	public enum AutoClickMode
	{
		Hold,
		Click,
		HoldAndRelease
	}

	public enum AutoClickButton
	{
		Left,
		Right,
		Both
	}

	public static bool Enabled;

	[Setting("AutoClickMode")]
	public static AutoClickMode Mode = AutoClickMode.Click;

	[Setting("AutoClickButton")]
	public static AutoClickButton Button = AutoClickButton.Left;

	[Setting("AutoClickCps", Min = 0.05f, Max = 30f)]
	public static float ClicksPerSecond = 10f;

	[Setting("AutoClickHoldSeconds", Min = 0.05f, Max = 3600f)]
	public static float HoldSeconds = 2f;

	[Setting("AutoClickReleaseSeconds", Min = 0.05f, Max = 3600f)]
	public static float ReleaseSeconds = 0.5f;

	/// <summary>0 = no limit.</summary>
	[Setting("AutoClickMinutes", Min = 0, Max = 100000)]
	public static int TimeLimitMinutes;

	[Setting("AutoClickPauseInInventory")]
	public static bool PauseInInventory;

	private static int _tick;

	private static long _clickCount;

	private static DateTime _startedAt;



	public static void SetEnabled(bool enabled)
	{
		Enabled = enabled;
		_tick = 0;
		_clickCount = 0L;
		_startedAt = DateTime.UtcNow;
		ClientChat.Info("Auto Clicker " + (enabled ? ("Enabled  (" + Describe() + ")") : "Disabled"));
	}

	public static string Describe()
	{
		string button = Button.ToString().ToLower() + " button";
		string limit = ((TimeLimitMinutes > 0) ? (", stops after " + TimeLimitMinutes + " min") : "");
		return Mode switch
		{
			AutoClickMode.Hold => "holding the " + button + limit,
			AutoClickMode.Click => ClicksPerSecond.ToString("0.##", CultureInfo.InvariantCulture) + " clicks/s with the " + button + limit,
			_ => "hold " + HoldSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s, release " + ReleaseSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s, " + button + limit,
		};
	}

	/// <summary>
	/// Called from Player.Update right after the controls are read: presses the use buttons directly on the player,
	/// so it keeps working with the window in the background or the chat open, and never clicks on the UI.
	/// </summary>
	public static void Apply(Player player)
	{
		if (!Enabled || Main.gameMenu || player.whoAmI != Main.myPlayer)
		{
			return;
		}
		if (TimeLimitMinutes > 0 && (DateTime.UtcNow - _startedAt).TotalMinutes >= (double)TimeLimitMinutes)
		{
			Enabled = false;
			ClientChat.Info("Auto Clicker stopped after " + TimeLimitMinutes + " minute" + ((TimeLimitMinutes == 1) ? "" : "s") + " (" + _clickCount + " clicks).");
			return;
		}
		if (player.dead || player.ghost || Main.mapFullscreen || !Main.mouseItem.IsAir || InputState.TildeTerminal || (PauseInInventory && Main.playerInventory) || BrushState.IsInAnyWorldEdit)
		{
			return;
		}
		bool press;
		switch (Mode)
		{
		case AutoClickMode.Hold:
			press = true;
			break;
		case AutoClickMode.Click:
		{
			// One tick pressed, then released until the next click.
			int period = Math.Max(2, (int)Math.Round(60f / ClicksPerSecond));
			press = _tick % period == 0;
			break;
		}
		default:
		{
			int hold = Math.Max(1, (int)Math.Round(HoldSeconds * 60f));
			int release = Math.Max(1, (int)Math.Round(ReleaseSeconds * 60f));
			press = _tick % (hold + release) < hold;
			break;
		}
		}
		bool newPress = Mode switch
		{
			AutoClickMode.Click => press,
			AutoClickMode.HoldAndRelease => _tick % Math.Max(2, (int)Math.Round((HoldSeconds + ReleaseSeconds) * 60f)) == 0,
			_ => _tick == 0,
		};
		if (newPress)
		{
			_clickCount++;
		}
		_tick++;
		if (!press)
		{
			return;
		}
		if (Button != AutoClickButton.Right)
		{
			player.controlUseItem = true;
		}
		if (Button != AutoClickButton.Left)
		{
			player.controlUseTile = true;
		}
	}

	/// <summary>.autoclick / .ac [on|off] [hold|click|cycle] [cps] [left|right|both] [hold=s] [release=s] [min=n]</summary>
	internal static void AutoClickerCommand(CommandContext command)
	{
		string[] array = command.Parts;
		if (array.Length == 1)
		{
			SetEnabled(!Enabled);
			return;
		}
		bool? enable = null;
		for (int i = 1; i < array.Length; i++)
		{
			string arg = array[i].ToLowerInvariant();
			if (arg.Length == 0)
			{
				continue;
			}
			float value;
			switch (arg)
			{
			case "on":
				enable = true;
				continue;
			case "off":
			case "stop":
				enable = false;
				continue;
			case "hold":
				Mode = AutoClickMode.Hold;
				continue;
			case "click":
			case "clicks":
				Mode = AutoClickMode.Click;
				continue;
			case "cycle":
			case "holdrelease":
				Mode = AutoClickMode.HoldAndRelease;
				continue;
			case "left":
				Button = AutoClickButton.Left;
				continue;
			case "right":
				Button = AutoClickButton.Right;
				continue;
			case "both":
				Button = AutoClickButton.Both;
				continue;
			case "inv":
				PauseInInventory = !PauseInInventory;
				ClientChat.Info("Auto Clicker pauses with the inventory open: " + (PauseInInventory ? "yes" : "no"));
				continue;
			}
			if (arg.StartsWith("hold=") && TryParseNumber(arg.Substring(5), out value))
			{
				HoldSeconds = MathHelper.Clamp(value, 0.05f, 3600f);
				Mode = AutoClickMode.HoldAndRelease;
			}
			else if (arg.StartsWith("release=") && TryParseNumber(arg.Substring(8), out value))
			{
				ReleaseSeconds = MathHelper.Clamp(value, 0.05f, 3600f);
				Mode = AutoClickMode.HoldAndRelease;
			}
			else if ((arg.StartsWith("min=") || arg.StartsWith("time=")) && TryParseNumber(arg.Substring(arg.IndexOf('=') + 1), out value))
			{
				TimeLimitMinutes = Math.Max(0, (int)value);
			}
			else if (TryParseNumber(arg.TrimEnd('s').Replace("cps", ""), out value))
			{
				ClicksPerSecond = MathHelper.Clamp(value, 0.05f, 30f);
				Mode = AutoClickMode.Click;
			}
			else
			{
				ClientChat.Error("Usage: .autoclick [on|off] [hold|click|cycle] [cps] [left|right|both] [hold=seconds] [release=seconds] [min=minutes] [inv]");
				ClientChat.Notice("Examples: .ac 8   .ac hold right   .ac cycle hold=3 release=0.5   .ac 20 min=60");
				return;
			}
		}
		SetEnabled(enable ?? true);
		ClientSettings.Save();
	}

	private static bool TryParseNumber(string text, out float value)
	{
		return float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	#region Chat commands

	#endregion
}
