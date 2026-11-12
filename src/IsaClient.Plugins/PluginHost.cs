using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ImGuiNET;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TerraAngel;
using TerraAngel.Tools;
using TerraAngel.UI;
using Terraria;
using Terraria.GameInput;

namespace IsaClient.Plugins;

/// <summary>
/// Runs the plugin system: starts it once the game has loaded (native ImGui, renderer, plugins), updates plugins and
/// tools every frame, keeps the game from reacting to the mouse and keys ImGui is using, and draws the ImGui frame
/// after the game (Main.OnPostDraw). The overlay (the plugin windows) opens and closes with Insert or .plugins.
/// </summary>
public static class PluginHost
{
	[Setting("PluginOverlayKey")]
	public static string OverlayKey = "Insert";

	/// <summary>Plugins and ImGui off entirely (nothing is loaded at start). Change it with <see cref="SetSystemOn"/>.</summary>
	[Setting("PluginsOff")]
	public static bool SystemOff;

	/// <summary>
	/// Everything about plugins, TerraAngel and ImGui on or off, right away: off unloads every plugin (their Harmony
	/// patches, tools, windows and commands go with them), closes the overlay and stops drawing ImGui, so the client is
	/// what it is without plugins. On, the plugins that were on load again. The choice is saved.
	/// </summary>
	public static void SetSystemOn(bool on)
	{
		if (on == !SystemOff)
		{
			return;
		}
		SystemOff = !on;
		if (!on)
		{
			OverlayVisible = false;
			WantsMouse = false;
			WantsText = false;
			CaptureGameMouse = false;
			if (Started)
			{
				// The enabled list is kept: they come back when the system is turned on again.
				PluginLoader.UnloadAll();
				_pluginsLoaded = false;
			}
		}
		else if (!Started)
		{
			// Never started this session (it was off at start): it starts on the next frame.
			_tried = false;
		}
		ClientSettings.Save();
	}

	public static bool OverlayVisible;

	public static bool Started { get; private set; }

	/// <summary>Why the plugin system could not start; null when it started (or has not tried yet).</summary>
	public static string StartError { get; private set; }

	internal static ImGuiRenderer Renderer { get; private set; }

	/// <summary>ImGui had the mouse / a focused text box in the last frame: the game must not use them.</summary>
	public static bool WantsMouse { get; private set; }

	public static bool WantsText { get; private set; }

	/// <summary>Set during the ImGui frame by a plugin that uses the mouse over the game itself (selecting an area on
	/// the map, dragging the camera): the game ignores the mouse until the next frame.</summary>
	public static bool CaptureGameMouse;

	private static readonly Queue<Action> _beforeFrame = new Queue<Action>();

	private static readonly Stopwatch _clock = Stopwatch.StartNew();

	private static double _lastFrame;

	private static bool _tried;

	private static KeyboardState _oldKeys;

	private static int _frameErrors;

	private static bool _pluginsLoaded;

	public static void BeforeNextFrame(Action action)
	{
		lock (_beforeFrame)
		{
			_beforeFrame.Enqueue(action);
		}
	}

	/// <summary>Main.DoUpdate, right after the game read the input.</summary>
	public static void AfterInput()
	{
		if (Main.dedServ || SystemOff)
		{
			return;
		}
		if (!Started)
		{
			if (_tried || !Main._isAsyncLoadComplete)
			{
				return;
			}
			Start();
			if (!Started)
			{
				return;
			}
		}
		HandleKeys();
		BlockGameInput();
	}

	/// <summary>
	/// Plugin, tool and window updates run inside the ImGui frame, like in TerraAngel (its tools draw from Update):
	/// calling ImGui outside a frame crashes the game in native code, where no exception can be caught.
	/// </summary>
	private static void UpdateInFrame()
	{
		if (!_pluginsLoaded)
		{
			// The plugins that are on load inside the first frame: their Load may already use ImGui.
			_pluginsLoaded = true;
			PluginLoader.LoadEnabled();
		}
		PluginLoader.Update();
		ToolManager.Update(ToolUpdatedHere);
		foreach (ClientWindow window in ClientLoader.MainRenderer.ClientWindows.ToArray())
		{
			try
			{
				window.Update();
			}
			catch (Exception ex)
			{
				PluginLog.ErrorOnce(window.Title + " (update): " + ex.Message, ex);
			}
		}
	}

	/// <summary>A plugin with its own tool window (the TerraAngel pack) updates its tools itself, from that window.</summary>
	private static bool ToolUpdatedHere(TerraAngel.Tools.Tool tool)
	{
		System.Reflection.Assembly assembly = tool.GetType().Assembly;
		foreach (ClientPlugin plugin in PluginLoader.LoadedClientPlugins())
		{
			if (plugin.HasOwnToolWindow && plugin.GetType().Assembly == assembly)
			{
				return false;
			}
		}
		return true;
	}

	private static void Start()
	{
		_tried = true;
		if (!ImGuiNativeLoader.EnsureLoaded())
		{
			StartError = "ImGui could not start: " + ImGuiNativeLoader.Error;
			return;
		}
		try
		{
			Renderer = new ImGuiRenderer(Main.instance.GraphicsDevice, Path.Combine(ClientSettings.IsaClientFolder, "imgui.ini"));
			ImGuiInput.Listen();
			ClientLoader.MainRenderer.AddWindow(ClientLoader.ConsoleWindow);
			PluginConsoleCommands.Register();
			Main.OnPostDraw += Draw;
			Main.instance.Exiting += delegate
			{
				PluginLoader.UnloadAll();
			};
			Started = true;
		}
		catch (Exception ex)
		{
			StartError = "ImGui could not start: " + ex.Message;
			ClientLog.LogException(ex);
			Started = false;
		}
	}

	/// <summary>The overlay key (Insert by default) and the windows' own keys, never while something is being typed.</summary>
	private static void HandleKeys()
	{
		KeyboardState keys = Keyboard.GetState();
		KeyboardState old = _oldKeys;
		_oldKeys = keys;
		if (!Main.hasFocus || WantsText || Main.drawingPlayerChat || Main.editSign || Main.editChest || PlayerInput.WritingText || Main.CurrentInputTextTakerOverride != null)
		{
			return;
		}
		bool modifier = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl) || keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) || keys.IsKeyDown(Keys.LeftAlt);
		if (modifier)
		{
			return;
		}
		if (Enum.TryParse(OverlayKey, true, out Keys toggle) && toggle != Keys.None && keys.IsKeyDown(toggle) && !old.IsKeyDown(toggle))
		{
			ToggleOverlay();
		}
		foreach (ClientWindow window in ClientLoader.MainRenderer.ClientWindows.ToArray())
		{
			if (window.ToggleKey != Keys.None && keys.IsKeyDown(window.ToggleKey) && !old.IsKeyDown(window.ToggleKey))
			{
				SetWindowShown(window, !window.IsEnabled);
			}
		}
	}

	public static void ToggleOverlay()
	{
		OverlayVisible = !OverlayVisible;
	}

	public static void SetWindowShown(ClientWindow window, bool shown)
	{
		if (window.IsEnabled == shown)
		{
			return;
		}
		window.IsEnabled = shown;
		try
		{
			if (shown)
			{
				window.OnShow();
			}
			else
			{
				window.OnHide();
			}
		}
		catch (Exception ex)
		{
			PluginLog.ErrorOnce(window.Title + ": " + ex.Message, ex);
		}
	}

	/// <summary>The mouse over an ImGui window and keys typed into ImGui stay away from the game (like the music window does).</summary>
	private static void BlockGameInput()
	{
		if (WantsMouse)
		{
			Main.mouseLeft = false;
			Main.mouseRight = false;
			Main.mouseLeftRelease = false;
			Main.mouseRightRelease = false;
			PlayerInput.Triggers.Current.MouseLeft = false;
			PlayerInput.Triggers.Current.MouseRight = false;
			PlayerInput.Triggers.JustPressed.MouseLeft = false;
			PlayerInput.Triggers.JustPressed.MouseRight = false;
			PlayerInput.ScrollWheelDelta = 0;
			PlayerInput.ScrollWheelDeltaForUI = 0;
			if (Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}
		if (WantsText)
		{
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
	}

	/// <summary>Main.OnPostDraw: one ImGui frame over everything the game drew.</summary>
	private static void Draw(GameTime gameTime)
	{
		if (!Started || Renderer == null || SystemOff)
		{
			return;
		}
		bool run = OverlayVisible || !_pluginsLoaded || PluginLoader.AnyLoaded || HudWindowShown();
		if (!run || Main.instance.GraphicsDevice.GetRenderTargets().Length != 0)
		{
			WantsMouse = false;
			WantsText = false;
			return;
		}
		double now = _clock.Elapsed.TotalSeconds;
		float delta = (float)Math.Min(0.25, now - _lastFrame);
		_lastFrame = now;
		try
		{
			RunQueued();
			ForEachClientPlugin("before frame", plugin => plugin.BeforeImGuiFrame());
			ImGuiTheme.Refresh();
			ImGuiInput.Update();
			Renderer.BeginFrame(delta);
			ImGuiIOPtr io = ImGui.GetIO();
			UpdateInFrame();
			foreach (ClientPlugin plugin in PluginLoader.LoadedClientPlugins())
			{
				try
				{
					plugin.DrawOverlay(io);
				}
				catch (Exception ex)
				{
					PluginLog.ErrorOnce(plugin.Name + " (overlay): " + ex.Message, ex);
				}
			}
			if (OverlayVisible)
			{
				PluginOverlay.Draw(io);
			}
			foreach (ClientWindow window in ClientLoader.MainRenderer.ClientWindows.ToArray())
			{
				if (!window.IsEnabled || (window.IsGlobalToggle && !OverlayVisible))
				{
					continue;
				}
				try
				{
					window.Draw(io);
				}
				catch (Exception ex)
				{
					PluginLog.Error(window.Title + " was closed after an error: " + ex.Message, ex);
					window.IsEnabled = false;
				}
			}
			io.MouseDrawCursor = io.WantCaptureMouse;
			Renderer.EndFrame();
			ForEachClientPlugin("after frame", plugin => plugin.AfterImGuiFrame());
			WantsMouse = io.WantCaptureMouse || CaptureGameMouse;
			CaptureGameMouse = false;
			WantsText = io.WantTextInput;
			if (WantsText)
			{
				PlayerInput.WritingText = true;
			}
			_frameErrors = 0;
		}
		catch (Exception ex)
		{
			WantsMouse = false;
			WantsText = false;
			PluginLog.Error("ImGui frame: " + ex.Message, ex);
			try
			{
				ImGuiRenderer.RecoverFromErrors();
				ImGui.EndFrame();
			}
			catch
			{
			}
			// Something keeps breaking every frame: close the overlay instead of filling the log.
			if (++_frameErrors >= 3)
			{
				OverlayVisible = false;
				ClientChat.Error("The plugin overlay was closed after repeated errors (see Plugins/plugins.log).");
				_frameErrors = 0;
			}
		}
	}

	private static void ForEachClientPlugin(string what, Action<ClientPlugin> action)
	{
		foreach (ClientPlugin plugin in PluginLoader.LoadedClientPlugins())
		{
			try
			{
				action(plugin);
			}
			catch (Exception ex)
			{
				PluginLog.ErrorOnce(plugin.Name + " (" + what + "): " + ex.Message, ex);
			}
			RestoreGraphics(plugin.Name + " (" + what + ")");
		}
	}

	private static readonly System.Reflection.FieldInfo _spriteBatchBegun = typeof(SpriteBatch).GetField("inBeginEndPair", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

	/// <summary>
	/// A plugin that drew and failed half way (or forgot) can leave the game's SpriteBatch open or a render target set:
	/// the game would crash on its next Begin. Both are put back, and the plugin is named in the log.
	/// </summary>
	private static void RestoreGraphics(string who)
	{
		try
		{
			if (_spriteBatchBegun != null && (bool)_spriteBatchBegun.GetValue(Main.spriteBatch))
			{
				Main.spriteBatch.End();
				PluginLog.ErrorOnce(who + " left the SpriteBatch open: it was closed.");
			}
			GraphicsDevice device = Main.instance.GraphicsDevice;
			if (device.GetRenderTargets().Length != 0)
			{
				device.SetRenderTarget(null);
			}
		}
		catch (Exception ex)
		{
			PluginLog.ErrorOnce("Restoring the graphics state after " + who + ": " + ex.Message, ex);
		}
	}

	private static bool HudWindowShown()
	{
		foreach (ClientWindow window in ClientLoader.MainRenderer.ClientWindows)
		{
			if (window.IsEnabled && !window.IsGlobalToggle)
			{
				return true;
			}
		}
		return false;
	}

	private static void RunQueued()
	{
		while (true)
		{
			Action action;
			lock (_beforeFrame)
			{
				if (_beforeFrame.Count == 0)
				{
					return;
				}
				action = _beforeFrame.Dequeue();
			}
			try
			{
				action();
			}
			catch (Exception ex)
			{
				PluginLog.Error("Queued plugin action: " + ex.Message, ex);
			}
		}
	}

	/// <summary>Main.Draw, before the game draws: plugins render into their own targets (see ClientPlugin.BeforeGameDraw).</summary>
	public static void BeforeGameDraw()
	{
		if (!Started || !PluginLoader.AnyLoaded)
		{
			return;
		}
		ForEachClientPlugin("before game draw", plugin => plugin.BeforeGameDraw());
	}

	/// <summary>ClientLifecycle.OnEnterWorld.</summary>
	public static void OnEnterWorld()
	{
		if (!Started)
		{
			return;
		}
		foreach (ClientPlugin plugin in PluginLoader.LoadedClientPlugins())
		{
			try
			{
				plugin.OnEnterWorld();
			}
			catch (Exception ex)
			{
				PluginLog.Error(plugin.Name + " (enter world): " + ex.Message, ex);
			}
		}
	}
}
