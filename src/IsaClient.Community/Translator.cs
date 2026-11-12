using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IsaClient.Community;

/// <summary>Why a translation failed (shown in the chat).</summary>
public sealed class TranslatorException : Exception
{
	public TranslatorException(string message)
		: base(message)
	{
	}
}

/// <summary>
/// The free MyMemory translation service (api.mymemory.translated.net). Blocking calls: run them off the game thread.
/// The quota belongs to <see cref="TranslatorAccounts.Email"/>, which changes by itself when it runs out.
/// </summary>
public static class Translator
{
	private const string Endpoint = "https://api.mymemory.translated.net/get";

	/// <summary>MyMemory takes up to 500 bytes of text per request.</summary>
	private const int MaxBytes = 480;

	/// <summary>Translates <paramref name="text"/>; the result keeps its line breaks. Same language in and out = the text as is.</summary>
	public static string Translate(string text, ChatLanguage from, ChatLanguage to)
	{
		if (string.IsNullOrWhiteSpace(text) || from == to)
		{
			return text;
		}
		if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
		{
			return Request(text, from, to);
		}
		StringBuilder result = new StringBuilder();
		foreach (string chunk in Chunks(text))
		{
			if (result.Length > 0)
			{
				result.Append(' ');
			}
			result.Append(Request(chunk, from, to));
		}
		return result.ToString();
	}

	private static string Request(string text, ChatLanguage from, ChatLanguage to)
	{
		for (int attempt = 0; attempt < 6; attempt++)
		{
			JObject reply = Call(text, from.ServiceCode + "|" + to.ServiceCode, TranslatorAccounts.Email);
			string status = reply["responseStatus"]?.ToString() ?? "";
			string translated = (string)reply["responseData"]?["translatedText"] ?? "";
			string details = reply["responseDetails"]?.ToString() ?? "";
			bool quota = status == "429" || (bool?)reply["quotaFinished"] == true || translated.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase);
			if (quota || details.IndexOf("EMAIL", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (!TranslatorAccounts.Rotate())
				{
					throw new TranslatorException("the free daily translations ran out, try again tomorrow");
				}
				continue;
			}
			if (details.IndexOf("DISTINCT LANGUAGES", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return text; // already in the target language
			}
			if (status != "200")
			{
				throw new TranslatorException(string.IsNullOrEmpty(details) ? ("service error " + status) : details.ToLowerInvariant());
			}
			return WebUtility.HtmlDecode(translated);
		}
		throw new TranslatorException("the translation service keeps refusing, try again later");
	}

	private static JObject Call(string text, string pair, string email)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		string url = Endpoint + "?q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(pair) + "&de=" + Uri.EscapeDataString(email);
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.Timeout = 12000;
		request.UserAgent = "IsaClient";
		try
		{
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			return Read(response);
		}
		catch (WebException ex) when (ex.Response is HttpWebResponse error)
		{
			// Errors (like the quota one) still come with the usual JSON.
			using (error)
			{
				try
				{
					return Read(error);
				}
				catch
				{
					throw new TranslatorException("service error " + (int)error.StatusCode);
				}
			}
		}
		catch (WebException ex)
		{
			throw new TranslatorException("can't reach the translation service (" + ex.Status + ")");
		}
	}

	private static JObject Read(HttpWebResponse response)
	{
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		return JObject.Parse(reader.ReadToEnd());
	}

	/// <summary>Long text in pieces under the limit, cut between words.</summary>
	private static IEnumerable<string> Chunks(string text)
	{
		StringBuilder chunk = new StringBuilder();
		foreach (string word in text.Split(' '))
		{
			string piece = word;
			while (Encoding.UTF8.GetByteCount(piece) > MaxBytes)
			{
				int cut = Math.Min(piece.Length, MaxBytes / 4);
				if (chunk.Length > 0)
				{
					yield return chunk.ToString();
					chunk.Clear();
				}
				yield return piece.Substring(0, cut);
				piece = piece.Substring(cut);
			}
			if (chunk.Length > 0 && Encoding.UTF8.GetByteCount(chunk + " " + piece) > MaxBytes)
			{
				yield return chunk.ToString();
				chunk.Clear();
			}
			if (chunk.Length > 0)
			{
				chunk.Append(' ');
			}
			chunk.Append(piece);
		}
		if (chunk.Length > 0)
		{
			yield return chunk.ToString();
		}
	}
}
