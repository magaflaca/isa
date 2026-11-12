using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Assistant;
using IsaClient.Community;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// AI Assistant > Connection: the Groq API key (yours or the built-in one) with a connection test, the endpoint, the
/// optional Brave Search key of .ask, and the AI settings file. Keys are saved where they always were (IsaAI.json for
/// Groq, the client settings for Brave) and are never shown whole.
/// </summary>
internal static class HubAiSetup
{
	private const int LabelWidth = 130;

	private static string _key = "";

	private static string _endpoint;

	private static string _brave = "";

	private static bool _testing;

	private static string _test;

	private static bool _testOk;

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>"Your own key" / "Built-in key" for the card on the main page.</summary>
	public static string Summary()
	{
		if (!AiAssistant.HasKey)
		{
			return "No key";
		}
		return UsingBuiltIn ? "Built-in key (shared)" : "Your own key";
	}

	private static bool UsingBuiltIn => AiAssistant.ApiKey == AiAssistant.DefaultApiKey;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		HubIcon.Item(I.GoldenKey).Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, "Connection", x + 30, y + 4, Theme.Accent, 0.92f);
		string[] intro = HubKit.Wrap("The AI runs on Groq, which is free. IsaClient comes with a built-in key that every player shares, so its daily tokens run out fast; your own free key (console.groq.com/keys) is all yours.", width - 34, 0.72f, 3);
		for (int i = 0; i < intro.Length; i++)
		{
			HubKit.Text(sb, intro[i], x + 30, y + 28 + i * 18, Theme.Muted, 0.72f);
		}
		y += 36 + intro.Length * 18 + 8;

		y += DrawGroqKey(sb, x, y, width) + 14;
		// The Plugin Maker's providers: NVIDIA (its default) and Mistral (optional).
		IsaClient.Assistant.PluginMaker.PluginAiSettings.EnsureLoaded();
		y += DrawProviderKey(sb, x, y, width, IsaClient.Assistant.PluginMaker.AiProvider.Nvidia, "NVIDIA API key", "nvapi-...", "The Plugin Maker's models (DeepSeek, MiniMax, Mistral Large...). Free at build.nvidia.com: sign in, open any model, Get API Key.") + 14;
		y += DrawProviderKey(sb, x, y, width, IsaClient.Assistant.PluginMaker.AiProvider.Mistral, "Mistral API key", "optional", "Optional: Mistral's own models (Devstral, Codestral...) for the Plugin Maker. console.mistral.ai") + 14;
		y += DrawEndpoint(sb, x, y, width) + 14;
		y += DrawBrave(sb, x, y, width) + 14;

		// The file with the rest (prompt, about me, nicknames...).
		HubKit.Text(sb, "Settings file", x + 4, y + 8, Theme.Muted, 0.72f, LabelWidth - 8);
		int bx = x + LabelWidth;
		if (HubKit.Button(sb, "ai:setup:open", new Rectangle(bx, y, 120, 30), "Edit IsaAI.json", Theme.Slot, AiAssistant.ConfigPath))
		{
			AiAssistant.OpenConfigFile();
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, "ai:setup:reload", new Rectangle(bx + 128, y, 100, 30), "Reload", Theme.Slot, "Reads IsaAI.json again (after editing it by hand)"))
		{
			AiAssistant.Load();
			_endpoint = null;
			HubInput.Sound(12);
		}
		y += 42;
		return y - start;
	}

	private static int DrawGroqKey(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		HubKit.Text(sb, "Groq API key", x + 4, y + 8, Theme.Muted, 0.72f, LabelWidth - 8);
		int fx = x + LabelWidth;
		// The key in use, never whole.
		string current = !AiAssistant.HasKey ? "none" : (Mask(AiAssistant.ApiKey) + (UsingBuiltIn ? "  (built-in, shared)" : "  (yours)"));
		HubKit.Text(sb, current, fx + 2, y + 8, UsingBuiltIn ? Theme.Text : Theme.On, 0.74f, width - LabelWidth - 4);
		y += 34;

		Rectangle field = new Rectangle(fx, y, Math.Max(160, width - LabelWidth - 214), 30);
		_key = HubKit.Field(sb, "ai:setup:key", field, _key, "Paste your key (gsk_...)", 120, HubIcon.Item(I.GoldenKey));
		bool valid = _key.Trim().Length >= 20;
		if ((HubKit.Button(sb, "ai:setup:key:save", new Rectangle(field.Right + 8, y, 90, 30), "Save", Theme.On * 0.6f, "Use this key from now on", valid) || (HubInput.Focus == "ai:setup:key" && Main.inputTextEnter)) && valid)
		{
			AiAssistant.ApiKey = _key.Trim();
			AiAssistant.Save();
			_key = "";
			_test = null;
			Main.clrInput();
			HubInput.Sound(10);
			Test(AiAssistant.ApiKey);
		}
		if (HubKit.Button(sb, "ai:setup:key:default", new Rectangle(field.Right + 106, y, 100, 30), "Built-in", Theme.Slot, "Go back to the key that comes with IsaClient", !UsingBuiltIn))
		{
			AiAssistant.ApiKey = AiAssistant.DefaultApiKey;
			AiAssistant.Save();
			_test = null;
			HubInput.Sound(11);
		}
		y += 38;

		if (HubKit.Button(sb, "ai:setup:test", new Rectangle(fx, y, 140, 28), _testing ? "Testing..." : "Test connection", Theme.Slot, "Checks the key without spending tokens", !_testing && AiAssistant.HasKey))
		{
			Test(AiAssistant.ApiKey);
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, "ai:setup:get", new Rectangle(fx + 148, y, 150, 28), "Get a free key", Theme.Slot, "console.groq.com/keys"))
		{
			ChatLinks.Open("https://console.groq.com/keys");
			HubInput.Sound(12);
		}
		if (_test != null)
		{
			HubKit.Text(sb, _test, fx + 306, y + 6, _testOk ? Theme.On : Theme.Danger, 0.7f, width - LabelWidth - 310);
		}
		y += 34;
		return y - start;
	}

	private static int DrawEndpoint(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Endpoint", x + 4, y + 8, Theme.Muted, 0.72f, LabelWidth - 8);
		_endpoint ??= AiAssistant.Endpoint;
		Rectangle field = new Rectangle(x + LabelWidth, y, Math.Max(160, width - LabelWidth - 214), 30);
		_endpoint = HubKit.Field(sb, "ai:setup:endpoint", field, _endpoint, AiAssistant.DefaultEndpoint, 200, HubIcon.Item(I.Wire));
		bool changed = _endpoint.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase) && _endpoint.Trim() != AiAssistant.Endpoint;
		if (HubKit.Button(sb, "ai:setup:endpoint:save", new Rectangle(field.Right + 8, y, 90, 30), "Save", Theme.On * 0.6f, null, changed) && changed)
		{
			AiAssistant.Endpoint = _endpoint.Trim();
			AiAssistant.Save();
			HubInput.Sound(10);
		}
		if (HubKit.Button(sb, "ai:setup:endpoint:reset", new Rectangle(field.Right + 106, y, 100, 30), "Default", Theme.Slot, "Groq's own address", AiAssistant.Endpoint != AiAssistant.DefaultEndpoint))
		{
			AiAssistant.Endpoint = AiAssistant.DefaultEndpoint;
			_endpoint = AiAssistant.Endpoint;
			AiAssistant.Save();
			HubInput.Sound(11);
		}
		HubKit.Text(sb, "Any OpenAI-compatible chat address works for the AFK replies; web search in .ask needs Groq.", x + LabelWidth, y + 36, Theme.Muted * 0.9f, 0.62f, width - LabelWidth);
		return 54;
	}

	/// <summary>The optional Brave Search API key: the backup search of .ask never meets a captcha with it.</summary>
	private static int DrawBrave(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "Search API key", x + 4, y + 8, Theme.Muted, 0.72f, LabelWidth - 8);
		bool has = !string.IsNullOrWhiteSpace(AskAssistant.BraveKey);
		int fx = x + LabelWidth;
		HubKit.Text(sb, has ? (Mask(AskAssistant.BraveKey) + "  (Brave Search)") : "none (optional)", fx + 2, y + 8, has ? Theme.On : Theme.Muted, 0.74f);
		y += 34;
		Rectangle field = new Rectangle(fx, y, Math.Max(160, width - LabelWidth - 214), 30);
		_brave = HubKit.Field(sb, "ai:setup:brave", field, _brave, "Brave Search API key (free plan)", 80, HubIcon.Item(I.Binoculars));
		bool valid = _brave.Trim().Length >= 10;
		if ((HubKit.Button(sb, "ai:setup:brave:save", new Rectangle(field.Right + 8, y, 90, 30), "Save", Theme.On * 0.6f, null, valid) || (HubInput.Focus == "ai:setup:brave" && Main.inputTextEnter)) && valid)
		{
			AskAssistant.BraveKey = _brave.Trim();
			_brave = "";
			Main.clrInput();
			ClientSettings.Save();
			HubInput.Sound(10);
		}
		if (HubKit.Button(sb, "ai:setup:brave:clear", new Rectangle(field.Right + 106, y, 100, 30), "Remove", Theme.Slot, "Use only the free search engines", has))
		{
			AskAssistant.BraveKey = "";
			ClientSettings.Save();
			HubInput.Sound(11);
		}
		string[] note = HubKit.Wrap(".ask searches with Groq's browser; when Groq's daily tokens run out it searches DuckDuckGo, Brave and Wikipedia from here, which may ask for a captcha after many questions. A free key from brave.com/search/api avoids that.", width - LabelWidth, 0.62f, 3);
		for (int i = 0; i < note.Length; i++)
		{
			HubKit.Text(sb, note[i], fx, y + 36 + i * 15, Theme.Muted * 0.9f, 0.62f);
		}
		return 34 + 36 + note.Length * 15;
	}

	private static readonly Dictionary<string, string> _providerInput = new Dictionary<string, string>();

	private static readonly Dictionary<string, string> _providerTest = new Dictionary<string, string>();

	private static readonly Dictionary<string, bool> _providerOk = new Dictionary<string, bool>();

	private static readonly HashSet<string> _providerTesting = new HashSet<string>();

	/// <summary>A key of the Plugin Maker's providers (IsaPluginAI.json): masked, a field to paste it, save, test, remove.</summary>
	private static int DrawProviderKey(SpriteBatch sb, int x, int y, int width, IsaClient.Assistant.PluginMaker.AiProvider provider, string label, string placeholder, string note)
	{
		int start = y;
		string id = provider.Id;
		HubKit.Text(sb, label, x + 4, y + 8, Theme.Muted, 0.72f, LabelWidth - 8);
		int fx = x + LabelWidth;
		string current = provider.Key();
		bool has = !string.IsNullOrWhiteSpace(current);
		HubKit.Text(sb, has ? IsaClient.Assistant.PluginMaker.PluginAiSettings.Mask(current) : "none", fx + 2, y + 8, has ? Theme.On : Theme.Muted, 0.74f, width - LabelWidth - 4);
		y += 34;
		string input = _providerInput.TryGetValue(id, out string typed) ? typed : "";
		Rectangle field = new Rectangle(fx, y, Math.Max(160, width - LabelWidth - 214), 30);
		input = HubKit.Field(sb, "ai:setup:" + id, field, input, "Paste your key (" + placeholder + ")", 200, HubIcon.Item(I.GoldenKey));
		_providerInput[id] = input;
		bool valid = input.Trim().Length >= 16;
		if ((HubKit.Button(sb, "ai:setup:" + id + ":save", new Rectangle(field.Right + 8, y, 90, 30), "Save", Theme.On * 0.6f, "Use this key from now on", valid) || (HubInput.Focus == "ai:setup:" + id && Main.inputTextEnter)) && valid)
		{
			SetProviderKey(provider, input.Trim());
			_providerInput[id] = "";
			Main.clrInput();
			HubInput.Sound(10);
			TestProvider(provider);
		}
		if (HubKit.Button(sb, "ai:setup:" + id + ":clear", new Rectangle(field.Right + 106, y, 100, 30), "Remove", Theme.Slot, "Forget this key", has))
		{
			SetProviderKey(provider, "");
			_providerTest.Remove(id);
			HubInput.Sound(11);
		}
		y += 38;
		bool testing = _providerTesting.Contains(id);
		if (HubKit.Button(sb, "ai:setup:" + id + ":test", new Rectangle(fx, y, 140, 28), testing ? "Testing..." : "Test key", Theme.Slot, "Checks that the key works", !testing && has))
		{
			TestProvider(provider);
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, "ai:setup:" + id + ":get", new Rectangle(fx + 148, y, 150, 28), "Get a free key", Theme.Slot, provider.KeyPage))
		{
			ChatLinks.Open("https://" + provider.KeyPage);
			HubInput.Sound(12);
		}
		if (_providerTest.TryGetValue(id, out string result))
		{
			HubKit.Text(sb, result, fx + 306, y + 6, (_providerOk.TryGetValue(id, out bool ok) && ok) ? Theme.On : Theme.Danger, 0.7f, width - LabelWidth - 310);
		}
		y += 34;
		string[] lines = HubKit.Wrap(note, width - LabelWidth, 0.62f, 2);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.Text(sb, lines[i], fx, y + i * 15, Theme.Muted * 0.9f, 0.62f);
		}
		y += lines.Length * 15;
		return y - start;
	}

	private static void SetProviderKey(IsaClient.Assistant.PluginMaker.AiProvider provider, string key)
	{
		if (provider == IsaClient.Assistant.PluginMaker.AiProvider.Nvidia)
		{
			IsaClient.Assistant.PluginMaker.PluginAiSettings.NvidiaKey = key;
		}
		else if (provider == IsaClient.Assistant.PluginMaker.AiProvider.Mistral)
		{
			IsaClient.Assistant.PluginMaker.PluginAiSettings.MistralKey = key;
		}
		IsaClient.Assistant.PluginMaker.PluginAiSettings.Save();
	}

	private static void TestProvider(IsaClient.Assistant.PluginMaker.AiProvider provider)
	{
		string id = provider.Id;
		string key = provider.Key();
		_providerTesting.Add(id);
		_providerTest.Remove(id);
		Task.Run(delegate
		{
			string text;
			bool ok;
			try
			{
				IsaClient.Assistant.PluginMaker.AiCall.TestKey(provider, key);
				text = "Connected: the key works";
				ok = true;
			}
			catch (Exception ex)
			{
				text = "Failed: " + ex.Message;
				ok = false;
			}
			Main.QueueMainThreadAction(delegate
			{
				_providerTesting.Remove(id);
				_providerTest[id] = text;
				_providerOk[id] = ok;
			});
		});
	}

	private static void Test(string key)
	{
		_testing = true;
		_test = null;
		string endpoint = AiAssistant.Endpoint;
		Task.Run(delegate
		{
			string text;
			bool ok;
			try
			{
				List<string> models = GroqClient.TestKey(key, endpoint);
				bool browser = models.Any(m => m.Contains("gpt-oss"));
				text = "Connected: " + models.Count + " models" + (browser ? ", web search ready" : "");
				ok = true;
			}
			catch (Exception ex)
			{
				text = "Failed: " + ex.Message;
				ok = false;
			}
			Main.QueueMainThreadAction(delegate
			{
				_testing = false;
				_test = text;
				_testOk = ok;
			});
		});
	}

	/// <summary>"gsk_glHs...lvkm".</summary>
	private static string Mask(string key)
	{
		key = (key ?? "").Trim();
		return (key.Length <= 12) ? new string('*', key.Length) : (key.Substring(0, 8) + "..." + key.Substring(key.Length - 4));
	}
}
