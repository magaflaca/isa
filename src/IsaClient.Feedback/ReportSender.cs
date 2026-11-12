using System;
using System.IO;
using System.Net;
using System.Text;
using IsaClient.Core;

namespace IsaClient.Feedback;

/// <summary>Posts a report to the developers' Discord (a webhook into a private thread), with an optional text file.</summary>
internal static class ReportSender
{
	// The webhook, XOR'd with a key so it is not a plain string in the exe.
	private const string Hook = "";

	private static string Url
	{
		get
		{
			byte[] data = Convert.FromBase64String(Hook);
			byte[] key = Encoding.ASCII.GetBytes("IsaFeedback");
			for (int i = 0; i < data.Length; i++)
			{
				data[i] ^= key[i % key.Length];
			}
			return Encoding.UTF8.GetString(data);
		}
	}

	/// <summary>Sends it and waits; returns null when Discord took it, else a short reason.</summary>
	public static string Send(FeedbackReport report, int timeoutMs)
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
			request.Method = "POST";
			request.Timeout = timeoutMs;
			request.ReadWriteTimeout = timeoutMs;
			request.UserAgent = ClientInfo.Name + "/" + ClientInfo.FullVersion;
			byte[] body;
			if (report.Attachment == null)
			{
				request.ContentType = "application/json";
				body = Encoding.UTF8.GetBytes(report.Payload);
			}
			else
			{
				string boundary = "----IsaClient" + Guid.NewGuid().ToString("N");
				request.ContentType = "multipart/form-data; boundary=" + boundary;
				using MemoryStream stream = new MemoryStream();
				Write(stream, "--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + report.Payload + "\r\n");
				Write(stream, "--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"" + (report.AttachmentName ?? "report.txt") + "\"\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n" + report.Attachment + "\r\n");
				Write(stream, "--" + boundary + "--\r\n");
				body = stream.ToArray();
			}
			request.ContentLength = body.Length;
			using (Stream stream = request.GetRequestStream())
			{
				stream.Write(body, 0, body.Length);
			}
			using (request.GetResponse())
			{
			}
			return null;
		}
		catch (WebException ex)
		{
			if (ex.Response is HttpWebResponse response)
			{
				int code = (int)response.StatusCode;
				response.Dispose();
				return code switch
				{
					429 => "Too many reports right now. Try again in a minute.",
					404 => "The report service is not available. Try again later.",
					_ => "The server refused it (" + code + ")."
				};
			}
			return (ex.Status == WebExceptionStatus.Timeout) ? "No answer (timed out). Check your internet." : "No connection. Check your internet.";
		}
		catch (Exception ex)
		{
			return ex.GetType().Name + ": " + ex.Message;
		}
	}

	private static void Write(Stream stream, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		stream.Write(bytes, 0, bytes.Length);
	}
}
