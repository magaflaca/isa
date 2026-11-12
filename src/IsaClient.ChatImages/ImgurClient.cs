using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IsaClient.ChatImages;

/// <summary>
/// Imgur's anonymous API: pictures sent in the chat go there and the message carries the link (i.imgur.com/ID.png),
/// so every IsaClient user can show it and anyone else still gets a link. Imgur only answers browsers (a request
/// without a browser User-Agent gets 429).
/// </summary>
internal static class ImgurClient
{
	private const string ClientId = "";

	private const string ApiUrl = "https://api.imgur.com/3/image";

	internal const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0";

	/// <summary>What Imgur gives back for an upload.</summary>
	internal sealed class Upload
	{
		public string Link;

		public string Id;

		public string DeleteHash;
	}

	static ImgurClient()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	/// <summary>Uploads one file (slow: off the main thread). <paramref name="progress"/> gets 0..1 of the bytes sent.</summary>
	public static Upload Send(byte[] data, string fileName, string contentType, Action<float> progress, Func<bool> cancelled)
	{
		string boundary = "----IsaClient" + Guid.NewGuid().ToString("N");
		byte[] head = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"image\"; filename=\"" + fileName.Replace("\"", "") + "\"\r\nContent-Type: " + contentType + "\r\n\r\n");
		byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ApiUrl);
		request.Method = "POST";
		request.UserAgent = UserAgent;
		request.Headers["Authorization"] = "Client-ID " + ClientId;
		request.ContentType = "multipart/form-data; boundary=" + boundary;
		request.ContentLength = head.Length + data.Length + tail.Length;
		request.Timeout = 120000;
		request.ReadWriteTimeout = 120000;
		request.AllowWriteStreamBuffering = false;
		try
		{
			using (Stream body = request.GetRequestStream())
			{
				body.Write(head, 0, head.Length);
				for (int at = 0; at < data.Length; at += 65536)
				{
					if (cancelled != null && cancelled())
					{
						request.Abort();
						throw new OperationCanceledException();
					}
					int count = Math.Min(65536, data.Length - at);
					body.Write(data, at, count);
					progress?.Invoke((float)(at + count) / data.Length);
				}
				body.Write(tail, 0, tail.Length);
			}
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			JObject json = JObject.Parse(ReadAll(response));
			JObject result = json["data"] as JObject;
			string link = (string)result?["link"];
			if (string.IsNullOrEmpty(link))
			{
				throw new InvalidOperationException("Imgur did not give a link back.");
			}
			return new Upload
			{
				Link = link.Replace("http://", "https://"),
				Id = (string)result["id"],
				DeleteHash = (string)result["deletehash"]
			};
		}
		catch (WebException ex)
		{
			throw new InvalidOperationException(Describe(ex), ex);
		}
	}

	/// <summary>Takes an image of yours off Imgur (the link stops working for everyone).</summary>
	public static void Delete(string deleteHash)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ApiUrl + "/" + deleteHash);
		request.Method = "DELETE";
		request.UserAgent = UserAgent;
		request.Headers["Authorization"] = "Client-ID " + ClientId;
		request.Timeout = 30000;
		try
		{
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		}
		catch (WebException ex)
		{
			throw new InvalidOperationException(Describe(ex), ex);
		}
	}

	/// <summary>Downloads a picture (up to <paramref name="maxBytes"/>); throws with a short message.</summary>
	/// <param name="accept">The formats to ask for (a file hidden in a picture asks for PNG only: never a lossy copy).</param>
	public static byte[] Download(string url, int maxBytes, Func<bool> cancelled, string accept = null)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		// The GIF sites (nekos.best, Klipy) are APIs for apps: IsaClient says who it is.
		request.UserAgent = (url.IndexOf("imgur.com", StringComparison.OrdinalIgnoreCase) >= 0) ? UserAgent : "IsaClient";
		request.Accept = accept ?? "image/avif,image/webp,image/png,image/*;q=0.8,*/*;q=0.5";
		request.Timeout = 30000;
		request.ReadWriteTimeout = 30000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		try
		{
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			// A removed image redirects to imgur.com/removed.png.
			if (response.ResponseUri != null && response.ResponseUri.AbsolutePath.IndexOf("removed", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				throw new InvalidOperationException("The image was removed.");
			}
			if (response.ContentLength > maxBytes)
			{
				throw new InvalidOperationException("The image is too big to show.");
			}
			using Stream stream = response.GetResponseStream();
			using MemoryStream memory = new MemoryStream();
			byte[] buffer = new byte[65536];
			int read;
			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				if (cancelled != null && cancelled())
				{
					throw new OperationCanceledException();
				}
				memory.Write(buffer, 0, read);
				if (memory.Length > maxBytes)
				{
					throw new InvalidOperationException("The image is too big to show.");
				}
			}
			return memory.ToArray();
		}
		catch (WebException ex)
		{
			throw new InvalidOperationException(Describe(ex), ex);
		}
	}

	private static string ReadAll(WebResponse response)
	{
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		return reader.ReadToEnd();
	}

	/// <summary>Imgur's own error text when there is one ("File is over the size limit"...).</summary>
	private static string Describe(WebException ex)
	{
		if (ex.Response is HttpWebResponse response)
		{
			int status = (int)response.StatusCode;
			string text = null;
			try
			{
				JToken error = JObject.Parse(ReadAll(response))["data"]?["error"];
				text = (error is JObject detail) ? ((string)detail["message"]) : ((string)error);
			}
			catch
			{
			}
			if (status == 429)
			{
				return "Imgur says too many uploads for now: try again in a while.";
			}
			if (status == 404)
			{
				return "The image is not on Imgur any more.";
			}
			return "Imgur: " + (string.IsNullOrEmpty(text) ? ("HTTP " + status) : text);
		}
		return (ex.Status == WebExceptionStatus.Timeout) ? "Imgur did not answer in time." : ("Could not reach Imgur: " + ex.Message);
	}
}
