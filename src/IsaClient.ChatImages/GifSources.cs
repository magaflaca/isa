using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IsaClient.ChatImages;

/// <summary>A GIF found: what is sent (<see cref="Url"/>) and a smaller one for the picker.</summary>
public sealed class GifItem
{
	public string Url;

	public string Preview;

	public int Width;

	public int Height;

	public string Title;
}

/// <summary>
/// Where GIFs come from: Klipy (trending and search, any GIF) and nekos.best (anime reactions by category: hug, pat,
/// kiss...; and search). Both answer with links the chat shows as pictures (see <see cref="ChatImages.IsImageLink"/>).
/// Called from background threads.
/// </summary>
internal static class GifSources
{
	/// <summary>The friends' project key.</summary>
	private const string KlipyKey = "";

	private const string KlipyApi = "https://api.klipy.com/api/v1/" + KlipyKey + "/gifs/";

	private const string NekosApi = "https://nekos.best/api/v2/";

	/// <summary>The friends' project key.</summary>
	private const string GiphyKey = "";

	private const string GiphyApi = "https://api.giphy.com/v1/gifs/";

	/// <summary>An id Klipy asks for (it tells users apart); random, one a session, nothing about you.</summary>
	private static readonly string _customer = "isaclient-" + Guid.NewGuid().ToString("N").Substring(0, 12);

	/// <summary>nekos.best's GIF categories, in the order the picker shows them.</summary>
	public static readonly string[] AnimeCategories =
	{
		"hug", "pat", "kiss", "cuddle", "poke", "slap", "bite", "highfive", "handhold", "handshake", "tickle", "feed",
		"bonk", "punch", "kick", "peck", "wave", "stare", "baka", "yeet", "lappillow", "carry", "kabedon", "blowkiss",
		"smile", "happy", "laugh", "cry", "dance", "blush", "pout", "shrug", "sleep", "think", "facepalm", "nod", "nope",
		"thumbsup", "clap", "salute", "angry", "bored", "yawn", "wink", "smug", "confused", "shocked", "run", "sip", "nom",
		"teehee", "wag", "tableflip", "spin", "shake", "nya", "lurk", "bleh", "shoot"
	};

	public static List<GifItem> KlipyTrending(int page, out bool more)
	{
		return Klipy("trending?page=" + page + "&per_page=24", out more);
	}

	public static List<GifItem> KlipySearch(string query, int page, out bool more)
	{
		return Klipy("search?q=" + Uri.EscapeDataString(query) + "&page=" + page + "&per_page=24", out more);
	}

	private static List<GifItem> Klipy(string path, out bool more)
	{
		JObject json = JObject.Parse(Get(KlipyApi + path + "&customer_id=" + _customer + "&locale=" + Locale()));
		JToken data = json["data"];
		more = (bool?)data?["has_next"] ?? false;
		List<GifItem> items = new List<GifItem>();
		foreach (JToken gif in (data?["data"] as JArray) ?? new JArray())
		{
			JToken file = gif["file"];
			string url = (string)file?["md"]?["gif"]?["url"] ?? (string)file?["hd"]?["gif"]?["url"];
			string preview = (string)file?["sm"]?["gif"]?["url"] ?? (string)file?["xs"]?["gif"]?["url"] ?? url;
			if (url == null || !ChatImages.IsImageLink(url))
			{
				continue;
			}
			items.Add(new GifItem
			{
				Url = url,
				Preview = preview,
				Width = (int?)file["md"]?["gif"]?["width"] ?? 0,
				Height = (int?)file["md"]?["gif"]?["height"] ?? 0,
				Title = (string)gif["title"] ?? ""
			});
		}
		return items;
	}

	public static List<GifItem> GiphyTrending(int page, out bool more)
	{
		return Giphy("trending?limit=24&offset=" + (page - 1) * 24, out more);
	}

	public static List<GifItem> GiphySearch(string query, int page, out bool more)
	{
		return Giphy("search?q=" + Uri.EscapeDataString(query) + "&limit=24&offset=" + (page - 1) * 24 + "&lang=" + Locale(), out more);
	}

	/// <summary>
	/// Giphy's links are long (with a token): the short ones of the same GIF are used, 200 px tall to send and 100 px wide
	/// for the picker.
	/// </summary>
	private static List<GifItem> Giphy(string path, out bool more)
	{
		JObject json = JObject.Parse(Get(GiphyApi + path + "&api_key=" + GiphyKey + "&rating=pg-13"));
		JToken pagination = json["pagination"];
		int total = (int?)pagination?["total_count"] ?? 0;
		int offset = (int?)pagination?["offset"] ?? 0;
		int count = (int?)pagination?["count"] ?? 0;
		more = offset + count < total;
		List<GifItem> items = new List<GifItem>();
		foreach (JToken gif in (json["data"] as JArray) ?? new JArray())
		{
			string id = (string)gif["id"];
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}
			string url = "https://i.giphy.com/media/" + id + "/200.gif";
			if (!ChatImages.IsImageLink(url))
			{
				continue;
			}
			items.Add(new GifItem
			{
				Url = url,
				Preview = "https://i.giphy.com/media/" + id + "/100w.gif",
				Width = (int?)gif["images"]?["fixed_height"]?["width"] ?? 0,
				Height = 200,
				Title = (string)gif["title"] ?? ""
			});
		}
		return items;
	}

	/// <summary>Random anime GIFs of a category (hug, pat...).</summary>
	public static List<GifItem> Anime(string category, int amount)
	{
		return Nekos(NekosApi + category + "?amount=" + Math.Max(1, Math.Min(20, amount)));
	}

	public static List<GifItem> AnimeSearch(string query, int amount)
	{
		return Nekos(NekosApi + "search?query=" + Uri.EscapeDataString(query) + "&type=2&amount=" + Math.Max(1, Math.Min(20, amount)));
	}

	/// <summary>One random GIF of a category (the action commands), or null.</summary>
	public static string RandomAnime(string category)
	{
		return Anime(category, 1).FirstOrDefault()?.Url;
	}

	private static List<GifItem> Nekos(string url)
	{
		JObject json = JObject.Parse(Get(url));
		List<GifItem> items = new List<GifItem>();
		foreach (JToken result in (json["results"] as JArray) ?? new JArray())
		{
			string gif = (string)result["url"];
			if (gif == null || !ChatImages.IsImageLink(gif))
			{
				continue;
			}
			items.Add(new GifItem
			{
				Url = gif,
				Preview = gif,
				Width = (int?)result["dimensions"]?["width"] ?? 0,
				Height = (int?)result["dimensions"]?["height"] ?? 0,
				Title = (string)result["anime_name"] ?? ""
			});
		}
		return items;
	}

	private static string Locale()
	{
		string code = IsaClient.Languages.L10n.CurrentPack?.Code ?? "en";
		return code.Split('-')[0];
	}

	private static string Get(string url)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = "IsaClient";
		request.Accept = "application/json";
		request.Timeout = 15000;
		request.ReadWriteTimeout = 15000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		return reader.ReadToEnd();
	}
}
