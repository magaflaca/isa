using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Plugins;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerraAngel.Graphics;
using TerraAngel.UI;
using TerraAngel.UI.ClientWindows.Console;
using Terraria;

namespace TerraAngel.Graphics
{
	/// <summary>
	/// The windows and textures of the plugin overlay (TerraAngel's ClientRenderer, reached through
	/// <c>ClientLoader.MainRenderer</c>).
	/// </summary>
	public class ClientRenderer
	{
		public List<ClientWindow> ClientWindows = new List<ClientWindow>();

		/// <summary>False hides every window that follows the overlay (the Insert key flips it).</summary>
		public bool GlobalToggle
		{
			get => PluginHost.OverlayVisible;
			set => PluginHost.OverlayVisible = value;
		}

		public ClientWindow AddWindow(ClientWindow window)
		{
			if (window != null && !ClientWindows.Contains(window))
			{
				window.IsEnabled = window.DefaultEnabled;
				ClientWindows.Add(window);
			}
			return window;
		}

		public void RemoveWindow(ClientWindow window)
		{
			ClientWindows.Remove(window);
		}

		/// <summary>An ImGui texture id for <paramref name="texture"/> (ImGui.Image, draw lists).</summary>
		public IntPtr BindTexture(Texture2D texture) => PluginHost.Renderer?.BindTexture(texture) ?? IntPtr.Zero;

		public void UnbindTexture(IntPtr textureId) => PluginHost.Renderer?.UnbindTexture(textureId);

		/// <summary>Runs <paramref name="action"/> on the game thread before the next ImGui frame.</summary>
		public void EnqueuePreDrawAction(Action action) => PluginHost.BeforeNextFrame(action);

		/// <summary>Builds the font atlas again after fonts were added to ImGui.GetIO().Fonts (do it in an EnqueuePreDrawAction).</summary>
		public void RebuildFontAtlas() => PluginHost.Renderer?.RebuildFontAtlas();
	}
}

namespace TerraAngel
{
	/// <summary>
	/// TerraAngel's entry point for plugins: the console, the chat, the overlay's windows and the client's folders.
	/// </summary>
	// A class (not static) like TerraAngel's, so the TerraAngel pack can add members to it (C# 14 extension members).
	public class ClientLoader
	{
		private ClientLoader()
		{
		}

		internal static readonly ConsoleWindow ConsoleWindow = new ConsoleWindow();

		public static readonly ClientRenderer MainRenderer = new ClientRenderer();

		public static bool ClientLoaded => PluginHost.Started;

		public static string SavePath => IsaClient.Core.ClientSettings.IsaClientFolder;

		public static string PluginsPath => PluginLoader.PluginsFolder;

		/// <summary>Where a plugin keeps its own files (created when asked for).</summary>
		public static string PluginDataPath(string pluginName)
		{
			string path = Path.Combine(PluginsPath, "Data", pluginName);
			Directory.CreateDirectory(path);
			return path;
		}

		public static string TerrariaPath => Path.GetDirectoryName(typeof(Main).Assembly.Location);

		public static string ContentFolder => Path.Combine(TerrariaPath, "Content");

		public static readonly Version TerraAngelVersion = new Version(IsaClient.Core.ClientInfo.Version);

		public static string ArchitectureString => Environment.Is64BitProcess ? "x64" : "x86";

		public static class Console
		{
			public static void WriteLine(string message) => ConsoleWindow.WriteLine(message);

			public static void WriteLine(string message, Color color) => ConsoleWindow.WriteLine(message, color);

			public static void WriteError(string message) => ConsoleWindow.WriteError(message);

			public static void AddCommand(string name, Action<ConsoleWindow.CmdStr> action, string description = "No Description Given") => ConsoleWindow.AddCommand(name, action, description);

			public static void RemoveCommand(string name) => ConsoleWindow.RemoveCommand(name);

			public static bool ExecuteCommand(string line) => ConsoleWindow.ExecuteCommand(line);
		}

		/// <summary>A chat that replaces the game's (the TerraAngel pack's chat window, when it is turned on).</summary>
		public interface IChat
		{
			bool IsChatting { get; }

			string ChatText { get; set; }

			void OpenChat();

			void CloseChat();

			void WriteLine(string message, Color color);

			void AppendText(string message);
		}

		/// <summary>The chat: TerraAngel's chat window when the pack's is on (<see cref="Replacement"/>), the game's otherwise.</summary>
		public static class Chat
		{
			public static IChat Replacement;

			public static bool IsChatting => Replacement?.IsChatting ?? Main.drawingPlayerChat;

			public static string ChatText
			{
				get => (Replacement != null) ? Replacement.ChatText : (Main.chatText ?? "");
				set
				{
					if (Replacement != null)
					{
						Replacement.ChatText = value ?? "";
					}
					else
					{
						Main.chatText = value ?? "";
					}
				}
			}

			public static void OpenChat()
			{
				Main.OpenPlayerChat();
			}

			public static void CloseChat()
			{
				if (Replacement != null)
				{
					Replacement.CloseChat();
				}
				else
				{
					Main.ClosePlayerChat();
				}
			}

			// Lines go to the game's chat, which the replacement shows too.
			public static void WriteLine(string message) => Main.NewText(IsaClient.Languages.L10n.T(message));

			public static void WriteLine(string message, Color color) => Main.NewText(IsaClient.Languages.L10n.T(message), color.R, color.G, color.B);

			public static void AppendText(string message)
			{
				if (Replacement != null)
				{
					Replacement.AppendText(message);
				}
				else
				{
					Main.chatText = (Main.chatText ?? "") + message;
				}
			}
		}
	}
}
