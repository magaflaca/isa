using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using IsaClient.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria.IO;

namespace IsaClient.Assistant.PluginMaker;

/// <summary>An OpenAI-style chat API: NVIDIA (the default), Groq (the shared key, last resort) or Mistral (optional).</summary>
public sealed class AiProvider
{
	public string Id;

	public string Name;

	public string Endpoint;

	/// <summary>Where people get a key.</summary>
	public string KeyPage;

	public Func<string> Key;

	public bool HasKey => !string.IsNullOrWhiteSpace(Key());

	public static readonly AiProvider Nvidia = new AiProvider
	{
		Id = "nvidia",
		Name = "NVIDIA",
		Endpoint = "https://integrate.api.nvidia.com/v1/chat/completions",
		KeyPage = "build.nvidia.com",
		Key = () => PluginAiSettings.NvidiaKey
	};

	public static readonly AiProvider Groq = new AiProvider
	{
		Id = "groq",
		Name = "Groq",
		Endpoint = AiAssistant.DefaultEndpoint,
		KeyPage = "console.groq.com/keys",
		Key = () => AiAssistant.ApiKey
	};

	public static readonly AiProvider Mistral = new AiProvider
	{
		Id = "mistral",
		Name = "Mistral",
		Endpoint = "https://api.mistral.ai/v1/chat/completions",
		KeyPage = "console.mistral.ai",
		Key = () => PluginAiSettings.MistralKey
	};

	public static readonly AiProvider[] All = { Nvidia, Groq, Mistral };

	public static AiProvider Find(string id)
	{
		return All.FirstOrDefault(p => p.Id == id);
	}
}

/// <summary>A model of a provider ("nvidia:deepseek-ai/deepseek-v4.1-flash").</summary>
public sealed class AiModel
{
	public AiProvider Provider;

	public string Id;

	/// <summary>Short name for buttons.</summary>
	public string Label;

	/// <summary>What it is like, for the tooltip.</summary>
	public string Note;

	/// <summary>Seconds a step usually takes (measured on the free tiers, October 2026), for the model window.</summary>
	public string Speed = "";

	/// <summary>Answers in about 10 seconds or less (by its measured <see cref="Speed"/>).</summary>
	public bool Quick
	{
		get
		{
			System.Text.RegularExpressions.Match first = System.Text.RegularExpressions.Regex.Match(Speed ?? "", "[0-9]+");
			return first.Success && int.Parse(first.Value) <= 10;
		}
	}

	/// <summary>Sees pictures.</summary>
	public bool Sees = true;

	/// <summary>Has a thinking effort setting the API takes (Groq's gpt-oss and qwen).</summary>
	public bool HasEffort;

	public string Key => Provider.Id + ":" + Id;

	public override string ToString() => Key;

	private static AiModel M(AiProvider provider, string id, string label, string note, string speed = "", bool sees = true, bool effort = false)
	{
		return new AiModel { Provider = provider, Id = id, Label = label, Note = note, Speed = speed, Sees = sees, HasEffort = effort };
	}

	/// <summary>Every model it can use (the ones that call tools well, checked in October 2026).</summary>
	public static readonly AiModel[] Catalog =
	{
		M(AiProvider.Nvidia, "minimaxai/minimax-m3.1-flash", "MiniMax M3.1 Flash", "The default: fast and good at code.", "~10 s"),
		M(AiProvider.Nvidia, "deepseek-ai/deepseek-v4.1-flash", "DeepSeek V4.1 Flash", "Very strong at code; thinks a lot before answering, so it's slow on the free tier.", "35-110 s"),
		M(AiProvider.Nvidia, "mistralai/mistral-large-4-1t-a52b", "Mistral Large 4", "Big and careful.", "~10 s"),
		M(AiProvider.Nvidia, "stepfun-ai/step-5-preview", "Step 5 (preview)", "Quick.", "3-20 s"),
		M(AiProvider.Nvidia, "z-ai/glm-5.3-flash", "GLM 5.3 Flash", "Good at code, slow on the free tier.", "10-60 s"),
		M(AiProvider.Groq, "qwen/qwen3.8-27b", "Qwen 3.8 27B (Groq)", "Groq: fast, sees pictures, thinks (Effort: low turns its thinking off). The shared key runs out often.", "2-8 s", true, true),
		M(AiProvider.Groq, "openai/gpt-oss-120b", "GPT-OSS 120B (Groq)", "Groq: very fast, doesn't see pictures. The shared key runs out often.", "2-6 s", false, true),
		M(AiProvider.Groq, "openai/gpt-oss-20b", "GPT-OSS 20B (Groq)", "Groq: fastest, smaller, doesn't see pictures.", "1-4 s", false, true),
		M(AiProvider.Mistral, "devstral-latest", "Devstral (Mistral)", "Mistral's coding agent model.", "1-3 s"),
		M(AiProvider.Mistral, "deepseek-v4-1-flash-1017", "DeepSeek V4.1 Flash (Mistral)", "DeepSeek, much faster than on NVIDIA.", "2-5 s"),
		M(AiProvider.Mistral, "mistral-large-latest", "Mistral Large (Mistral)", "", "2-4 s"),
		M(AiProvider.Mistral, "codestral-latest", "Codestral (Mistral)", "", "1-3 s")
	};

	/// <summary>What "Auto" tries, in order: NVIDIA's models, then Groq's shared key.</summary>
	public static readonly string[] AutoChain =
	{
		"nvidia:minimaxai/minimax-m3.1-flash",
		"nvidia:deepseek-ai/deepseek-v4.1-flash",
		"nvidia:mistralai/mistral-large-4-1t-a52b",
		"nvidia:stepfun-ai/step-5-preview",
		"nvidia:z-ai/glm-5.3-flash",
		"groq:qwen/qwen3.8-27b",
		"groq:openai/gpt-oss-120b"
	};

	/// <summary>Quick models for the router (deciding how to handle a message), best first.</summary>
	public static readonly string[] RouterChain =
	{
		"nvidia:stepfun-ai/step-5-preview",
		"nvidia:minimaxai/minimax-m3.1-flash",
		"mistral:ministral-8b-latest",
		"groq:openai/gpt-oss-20b"
	};

	/// <summary>A model by key; one not in the catalog is made from its provider ("nvidia:some/model").</summary>
	public static AiModel Get(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		AiModel known = Catalog.FirstOrDefault(m => m.Key == key);
		if (known != null)
		{
			return known;
		}
		int colon = key.IndexOf(':');
		AiProvider provider = (colon > 0) ? AiProvider.Find(key.Substring(0, colon)) : null;
		return (provider == null) ? null : M(provider, key.Substring(colon + 1), key.Substring(colon + 1), "");
	}

	/// <summary>The models to try for the main work, in order (the chosen one first, then the rest of Auto), with a key.</summary>
	public static List<AiModel> MainChain()
	{
		List<string> keys = new List<string>();
		if (PluginAiSettings.Model != "auto")
		{
			keys.Add(PluginAiSettings.Model);
		}
		if (PluginAiSettings.Model == "auto" || PluginAiSettings.Fallback)
		{
			keys.AddRange(AutoChain);
		}
		return keys.Distinct().Select(Get).Where(m => m != null && m.Provider.HasKey).ToList();
	}

	public static List<AiModel> RouterModels()
	{
		return RouterChain.Select(Get).Where(m => m != null && m.Provider.HasKey).ToList();
	}
}

/// <summary>Plugin Maker settings (Terraria/IsaClient/IsaPluginAI.json): keys, model, how it works. Keys are only stored here.</summary>
public static class PluginAiSettings
{
	public static string NvidiaKey = "";

	public static string MistralKey = "";

	/// <summary>"auto" or a model key ("nvidia:deepseek-ai/deepseek-v4.1-flash").</summary>
	public static string Model = "auto";

	/// <summary>When the chosen model fails, the others of Auto take over.</summary>
	public static bool Fallback = true;

	/// <summary>A quick model reads each message first and decides how to handle it (quick answer or work with tools).</summary>
	public static bool Router = true;

	/// <summary>Plugins it builds are turned on (and reloaded) by themselves.</summary>
	public static bool AutoEnable = true;

	/// <summary>Most tool steps for one message.</summary>
	public static int MaxSteps = 40;

	public static float Temperature = 0.3f;

	/// <summary>"low", "medium" or "high": how thorough it is (and the thinking of the models that have a setting for it).</summary>
	public static string Effort = "medium";

	/// <summary>Longest wait for one answer of a model before trying the next one.</summary>
	public static int TimeoutSeconds = 150;

	/// <summary>.pluginai writes your message in the chat too.</summary>
	public static bool EchoToChat = true;

	/// <summary>The panel's chat scrolls down by itself with new messages while you're at its bottom.</summary>
	public static bool FollowChat = true;

	public static readonly string[] Efforts = { "low", "medium", "high" };

	public static bool HasNvidia => !string.IsNullOrWhiteSpace(NvidiaKey);

	private static Preferences _file;

	private static bool _loaded;

	public static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "IsaPluginAI.json");

	public static void EnsureLoaded()
	{
		if (!_loaded)
		{
			Load();
		}
	}

	public static void Load()
	{
		_loaded = true;
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_file = new Preferences(FilePath);
			if (!_file.Load())
			{
				Save();
				return;
			}
			NvidiaKey = _file.Get("NvidiaKey", "") ?? "";
			MistralKey = _file.Get("MistralKey", "") ?? "";
			Model = _file.Get("Model", "auto") ?? "auto";
			Fallback = _file.Get("Fallback", true);
			Router = _file.Get("Router", true);
			AutoEnable = _file.Get("AutoEnable", true);
			MaxSteps = Math.Max(5, Math.Min(200, _file.Get("MaxSteps", 40)));
			Temperature = Math.Max(0f, Math.Min(1.5f, _file.Get("Temperature", 0.3f)));
			Effort = _file.Get("Effort", "medium") ?? "medium";
			if (!Efforts.Contains(Effort))
			{
				Effort = "medium";
			}
			TimeoutSeconds = Math.Max(30, Math.Min(600, _file.Get("TimeoutSeconds", 150)));
			EchoToChat = _file.Get("EchoToChat", true);
			FollowChat = _file.Get("FollowAtBottom", true);
			if (AiModel.Get(Model) == null)
			{
				Model = "auto";
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Save()
	{
		_loaded = true;
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_file = _file ?? new Preferences(FilePath);
			_file.Clear();
			_file.Put("NvidiaKey", NvidiaKey);
			_file.Put("MistralKey", MistralKey);
			_file.Put("Model", Model);
			_file.Put("Fallback", Fallback);
			_file.Put("Router", Router);
			_file.Put("AutoEnable", AutoEnable);
			_file.Put("MaxSteps", MaxSteps);
			_file.Put("Temperature", Temperature);
			_file.Put("Effort", Effort);
			_file.Put("TimeoutSeconds", TimeoutSeconds);
			_file.Put("EchoToChat", EchoToChat);
			_file.Put("FollowAtBottom", FollowChat);
			_file.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>"nvapi-AbCd...xYz": enough to recognize a key, never all of it.</summary>
	public static string Mask(string key)
	{
		key = (key ?? "").Trim();
		return (key.Length <= 12) ? new string('*', key.Length) : (key.Substring(0, 9) + "..." + key.Substring(key.Length - 3));
	}
}

/// <summary>One call to a chat API with tools. Blocking: call it off the game thread.</summary>
public static class AiCall
{
	private static volatile HttpWebRequest _current;

	/// <summary>Cuts the answer being waited for (a hard stop).</summary>
	public static void AbortCurrent()
	{
		try
		{
			_current?.Abort();
		}
		catch
		{
		}
	}

	public static JObject Send(AiModel model, JArray messages, JArray tools, int maxTokens, int timeoutMs, Func<bool> cancelled = null)
	{
		if (!model.Sees)
		{
			JArray plain = new JArray();
			foreach (JToken m in messages)
			{
				JObject copy = (JObject)m.DeepClone();
				if (copy["content"] is JArray parts)
				{
					copy["content"] = PluginAgent.PlainText(parts) + " (this model can't see pictures)";
				}
				plain.Add(copy);
			}
			messages = plain;
		}
		if (model.Provider == AiProvider.Groq)
		{
			messages = Shorter(messages, 24000);
			maxTokens = Math.Min(maxTokens, 4096);
		}
		JObject body = new JObject
		{
			["model"] = model.Id,
			["messages"] = messages,
			["temperature"] = PluginAiSettings.Temperature,
			["max_tokens"] = maxTokens
		};
		if (tools != null && tools.Count > 0)
		{
			body["tools"] = tools;
			body["tool_choice"] = "auto";
		}
		if (model.Provider == AiProvider.Groq && model.Id.Contains("gpt-oss"))
		{
			body["reasoning_effort"] = PluginAiSettings.Effort;
		}
		else if (model.Provider == AiProvider.Groq && model.Id.Contains("qwen"))
		{
			body["reasoning_format"] = "hidden";
			body["reasoning_effort"] = (PluginAiSettings.Effort == "low") ? "none" : "default";
		}
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				JObject reply = Post(model.Provider, body, timeoutMs);
				int tokens = (int?)reply["usage"]?["completion_tokens"] ?? 0;
				AskStatus.AddTokens(tokens);
				PluginAgent.AddTokens(tokens);
				JObject message = reply["choices"]?[0]?["message"] as JObject;
				if (message == null)
				{
					throw new AssistantException(model.Label + " answered nothing");
				}
				return message;
			}
			catch (WebException ex) when (ex.Response is HttpWebResponse response && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < 2 && !(cancelled?.Invoke() ?? false))
			{
				response.Dispose();
				System.Threading.Thread.Sleep(3000 * (attempt + 1));
			}
			catch (WebException ex)
			{
				throw new AssistantException(Describe(model, ex));
			}
		}
	}

	/// <summary>
	/// The conversation cut to about <paramref name="maxChars"/> (Groq's free tier refuses big requests): the system
	/// messages, then the newest messages that fit, with long tool outputs shortened. It starts at a user message so
	/// every tool answer keeps its call.
	/// </summary>
	private static JArray Shorter(JArray messages, int maxChars)
	{
		List<JObject> system = messages.OfType<JObject>().Where(m => (string)m["role"] == "system").Select(m => (JObject)m.DeepClone()).ToList();
		List<JObject> rest = messages.OfType<JObject>().Where(m => (string)m["role"] != "system").Select(m => (JObject)m.DeepClone()).ToList();
		foreach (JObject m in rest)
		{
			string content = (m["content"] as JValue)?.ToString() ?? "";
			if ((string)m["role"] == "tool" && content.Length > 2500)
			{
				m["content"] = content.Substring(0, 2500) + "\n... (cut)";
			}
		}
		int size = system.Sum(m => m.ToString(Formatting.None).Length);
		int start = rest.Count;
		while (start > 0 && size + rest[start - 1].ToString(Formatting.None).Length < maxChars)
		{
			start--;
			size += rest[start].ToString(Formatting.None).Length;
		}
		while (start < rest.Count && (string)rest[start]["role"] != "user")
		{
			start++;
		}
		if (start >= rest.Count)
		{
			// Not even the last exchange fits: the last user message alone.
			int last = rest.FindLastIndex(m => (string)m["role"] == "user");
			start = Math.Max(0, last);
		}
		JArray result = new JArray();
		foreach (JObject m in system)
		{
			result.Add(m);
		}
		foreach (JObject m in rest.Skip(start))
		{
			result.Add(m);
		}
		return result;
	}

	private static JObject Post(AiProvider provider, JObject body, int timeout)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(provider.Endpoint);
		_current = request;
		request.Method = "POST";
		request.ContentType = "application/json";
		request.Accept = "application/json";
		request.Headers[HttpRequestHeader.Authorization] = "Bearer " + provider.Key().Trim();
		request.Timeout = timeout;
		request.ReadWriteTimeout = timeout;
		byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
		using (Stream stream = request.GetRequestStream())
		{
			stream.Write(payload, 0, payload.Length);
		}
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		return JObject.Parse(reader.ReadToEnd());
	}

	private static string Describe(AiModel model, WebException ex)
	{
		if (ex.Response is HttpWebResponse response)
		{
			int code = (int)response.StatusCode;
			string detail = "";
			try
			{
				using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
				string text = reader.ReadToEnd();
				JObject json = JObject.Parse(text);
				detail = (string)(json["error"]?["message"] ?? json["detail"] ?? json["message"]) ?? "";
			}
			catch
			{
			}
			return code switch
			{
				401 or 403 => "the " + model.Provider.Name + " key was refused",
				404 => model.Label + " isn't available for this key",
				413 => "the conversation is too long for " + model.Label + "'s free limit",
				429 => model.Provider.Name + " is busy (rate limit)",
				_ => model.Provider.Name + " error " + code + ((detail.Length > 0) ? (": " + Cut(detail, 160)) : "")
			};
		}
		return (ex.Status == WebExceptionStatus.Timeout) ? (model.Label + " took too long") : ("can't reach " + model.Provider.Name + " (" + ex.Status + ")");
	}

	private static string Cut(string text, int max) => (text.Length > max) ? (text.Substring(0, max) + "...") : text;

	/// <summary>
	/// Checks a key; throws why it doesn't work. NVIDIA lists its models to anyone, so its key is tried with a one-token
	/// answer of a quick model; the others list their models (no tokens spent).
	/// </summary>
	public static void TestKey(AiProvider provider, string key)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		if (provider == AiProvider.Nvidia)
		{
			AiModel quick = AiModel.Get("nvidia:minimaxai/minimax-m3.1-flash");
			JObject body = new JObject
			{
				["model"] = quick.Id,
				["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = "Say ok." } },
				["max_tokens"] = 1
			};
			HttpWebRequest post = (HttpWebRequest)WebRequest.Create(provider.Endpoint);
			post.Method = "POST";
			post.ContentType = "application/json";
			post.Headers[HttpRequestHeader.Authorization] = "Bearer " + (key ?? "").Trim();
			post.Timeout = 60000;
			try
			{
				byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
				using (Stream stream = post.GetRequestStream())
				{
					stream.Write(payload, 0, payload.Length);
				}
				using HttpWebResponse ok = (HttpWebResponse)post.GetResponse();
				return;
			}
			catch (WebException ex)
			{
				throw new AssistantException(Describe(quick, ex));
			}
		}
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(provider.Endpoint.Replace("/chat/completions", "/models"));
		request.Headers[HttpRequestHeader.Authorization] = "Bearer " + (key ?? "").Trim();
		request.Timeout = 20000;
		try
		{
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		}
		catch (WebException ex)
		{
			throw new AssistantException(Describe(new AiModel { Provider = provider, Id = "", Label = provider.Name }, ex));
		}
	}
}
