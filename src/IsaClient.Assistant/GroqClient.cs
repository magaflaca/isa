using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Assistant;

/// <summary>Why a model call failed, in words for the chat.</summary>
public sealed class AssistantException : Exception
{
	public AssistantException(string message)
		: base(message)
	{
	}
}

/// <summary>
/// Chat completions on Groq (the key and endpoint of IsaAI.json). Blocking: call it off the game thread. Reasoning
/// models keep their thinking out of the reply; a rate limit is waited out once.
/// </summary>
internal static class GroqClient
{
	/// <summary>Models that hit a long limit (tokens per day), and until when: they are skipped instead of waited for.</summary>
	private static readonly Dictionary<string, DateTime> _restingUntil = new Dictionary<string, DateTime>();

	/// <summary>False while <paramref name="model"/> is out of its daily tokens.</summary>
	public static bool IsAvailable(string model)
	{
		lock (_restingUntil)
		{
			return !_restingUntil.TryGetValue(model, out DateTime until) || DateTime.UtcNow >= until;
		}
	}

	public static string Complete(string model, string system, string user, int maxTokens, float temperature, bool json = false)
	{
		JObject body = Body(model, system, user, maxTokens, temperature);
		if (model.Contains("gpt-oss"))
		{
			body["reasoning_effort"] = "low";
		}
		else if (model.Contains("qwen"))
		{
			body["reasoning_format"] = "hidden";
		}
		if (json)
		{
			body["response_format"] = new JObject { ["type"] = "json_object" };
		}
		return (string)Send(model, body, 45000)["choices"]?[0]?["message"]?["content"] ?? "";
	}

	/// <summary>
	/// A call with messages already made (pictures in them, for a model that sees). <paramref name="reasoningEffort"/>:
	/// "none" turns qwen's thinking off; null leaves the model's default (hidden for qwen).
	/// </summary>
	public static string CompleteMessages(string model, JArray messages, int maxTokens, float temperature, string reasoningEffort = null)
	{
		if (!AiAssistant.HasKey)
		{
			throw new AssistantException("no Groq API key: put yours in IsaAI.json (AI Assistant > Edit AI Settings)");
		}
		JObject body = new JObject
		{
			["model"] = model,
			["messages"] = messages,
			["temperature"] = temperature,
			["max_completion_tokens"] = maxTokens
		};
		if (reasoningEffort != null)
		{
			body["reasoning_effort"] = reasoningEffort;
		}
		else if (model.Contains("gpt-oss"))
		{
			body["reasoning_effort"] = "low";
		}
		else if (model.Contains("qwen"))
		{
			body["reasoning_format"] = "hidden";
		}
		return (string)Send(model, body, 60000)["choices"]?[0]?["message"]?["content"] ?? "";
	}

	/// <summary>
	/// A gpt-oss model with Groq's built-in browser: it searches the web and opens pages by itself on Groq's servers
	/// (no captchas, nothing from this computer), as many times as it needs. <paramref name="mustSearch"/> false lets it
	/// answer small talk without searching. Returns the reply and the tools it ran.
	/// </summary>
	public static string CompleteWithBrowser(string model, string system, string user, int maxTokens, string effort, bool mustSearch, out JArray executedTools)
	{
		JObject body = Body(model, system, user, maxTokens, 0.2f);
		body["tools"] = new JArray { new JObject { ["type"] = "browser_search" } };
		body["tool_choice"] = mustSearch ? "required" : "auto";
		body["reasoning_effort"] = effort;
		JToken message = Send(model, body, 120000)["choices"]?[0]?["message"];
		executedTools = (message?["executed_tools"] as JArray) ?? new JArray();
		return (string)message?["content"] ?? "";
	}

	private static JObject Body(string model, string system, string user, int maxTokens, float temperature)
	{
		if (!AiAssistant.HasKey)
		{
			throw new AssistantException("no Groq API key: put yours in IsaAI.json (AI Assistant > Edit AI Settings)");
		}
		return new JObject
		{
			["model"] = model,
			["messages"] = new JArray
			{
				new JObject { ["role"] = "system", ["content"] = system },
				new JObject { ["role"] = "user", ["content"] = user }
			},
			["temperature"] = temperature,
			["max_completion_tokens"] = maxTokens
		};
	}

	/// <summary>
	/// Posts, waiting out short rate limits (tokens per minute). A long one (tokens per day) isn't waited for: the
	/// model rests until Groq says and the caller moves on to another one.
	/// </summary>
	private static JObject Send(string model, JObject body, int timeout)
	{
		if (!IsAvailable(model))
		{
			throw new AssistantException(model + " is out of its daily tokens for now");
		}
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				JObject reply = PostRaw(body, timeout);
				AskStatus.AddTokens((int?)reply["usage"]?["completion_tokens"] ?? 0);
				return reply;
			}
			catch (WebException ex) when (ex.Response is HttpWebResponse response && (int)response.StatusCode == 429)
			{
				double.TryParse(response.Headers["retry-after"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double wait);
				string detail = ReadError(response);
				response.Dispose();
				bool daily = detail.IndexOf("per day", StringComparison.OrdinalIgnoreCase) >= 0 || wait > 30.0;
				if (daily || attempt >= 2)
				{
					if (daily)
					{
						lock (_restingUntil)
						{
							_restingUntil[model] = DateTime.UtcNow.AddSeconds(Math.Max(wait, 60.0));
						}
					}
					throw new AssistantException(daily ? (model + " used its free tokens for today (back in " + Minutes(wait) + ")") : "Groq is busy right now (rate limit), try again in a few seconds");
				}
				Thread.Sleep((int)(Math.Min(Math.Max(wait, 1.5), 20.0) * 1000.0));
			}
			catch (WebException ex)
			{
				throw new AssistantException(Describe(ex));
			}
		}
	}

	private static string ReadError(HttpWebResponse response)
	{
		try
		{
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			return (string)JObject.Parse(reader.ReadToEnd())["error"]?["message"] ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static string Minutes(double seconds)
	{
		if (seconds <= 0)
		{
			return "a while";
		}
		TimeSpan span = TimeSpan.FromSeconds(seconds);
		return (span.TotalHours >= 1) ? ((int)span.TotalHours + " h " + span.Minutes + " min") : (Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)) + " min");
	}

	private static JObject PostRaw(JObject body, int timeout)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(AiAssistant.Endpoint);
		request.Method = "POST";
		request.ContentType = "application/json";
		request.Headers[HttpRequestHeader.Authorization] = "Bearer " + AiAssistant.ApiKey;
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

	private static string Describe(WebException ex)
	{
		if (ex.Response is HttpWebResponse response)
		{
			int code = (int)response.StatusCode;
			string detail = "";
			try
			{
				using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
				detail = (string)JObject.Parse(reader.ReadToEnd())["error"]?["message"] ?? "";
			}
			catch
			{
			}
			return code switch
			{
				401 => "the Groq API key was refused (check IsaAI.json)",
				429 => "Groq is busy right now (rate limit), try again in a few seconds",
				_ => "Groq error " + code + (detail.Length > 0 ? ": " + detail : "")
			};
		}
		return "can't reach Groq (" + ex.Status + ")";
	}

	/// <summary>Checks a key without spending tokens (lists the models it can use); throws with the reason when it fails.</summary>
	public static List<string> TestKey(string key, string endpoint)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		string url = (endpoint ?? "").Contains("/chat/completions") ? endpoint.Replace("/chat/completions", "/models") : "https://api.groq.com/openai/v1/models";
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.Headers[HttpRequestHeader.Authorization] = "Bearer " + (key ?? "").Trim();
		request.Timeout = 15000;
		try
		{
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			JArray models = JObject.Parse(reader.ReadToEnd())["data"] as JArray ?? new JArray();
			List<string> ids = new List<string>();
			foreach (JToken model in models)
			{
				ids.Add((string)model["id"]);
			}
			ids.Sort(StringComparer.Ordinal);
			return ids;
		}
		catch (WebException ex)
		{
			throw new AssistantException(Describe(ex));
		}
	}

	/// <summary>The JSON object inside a reply (models sometimes wrap it in text or code fences); null if there is none.</summary>
	public static JObject ParseJson(string reply)
	{
		if (string.IsNullOrEmpty(reply))
		{
			return null;
		}
		int start = reply.IndexOf('{');
		int end = reply.LastIndexOf('}');
		if (start < 0 || end <= start)
		{
			return null;
		}
		try
		{
			return JObject.Parse(reply.Substring(start, end - start + 1));
		}
		catch
		{
			return null;
		}
	}
}
