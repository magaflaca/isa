using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace IsaClient.Assistant;

/// <summary>A picture for the AI: its bytes (attached in the chat) or a link to it.</summary>
public sealed class AskImage
{
	public byte[] Data;

	public string Type;

	public string Url;
}

/// <summary>
/// Pictures for the AI. Groq's vision model (qwen3.8, without its thinking mode) looks at them and writes down what each
/// shows ([Image 1] ..., [Image 2] ...), and that text goes to the model that answers. When the answering model can see
/// by itself, it also gets the pictures. Pictures are shrunk and sent as JPEG (a GIF sends its first frame).
/// </summary>
internal static class AskVision
{
	public const string Model = "qwen/qwen3.8-27b";

	/// <summary>Pictures in one request (Groq's limit for vision models).</summary>
	private const int PerRequest = 5;

	/// <summary>Longest side of a picture sent, in pixels.</summary>
	private const int MaxSide = 1600;

	/// <summary>Picture links in a question (they are looked at too).</summary>
	private static readonly Regex ImageLink = new Regex(@"https?://\S+?\.(?:png|jpe?g|gif|webp)(?:\?\S*)?(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>True for models that take pictures.</summary>
	public static bool CanSee(string model)
	{
		return !string.IsNullOrEmpty(model) && (model.IndexOf("qwen3.8", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("llama-4", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	/// <summary>The picture links written in a question.</summary>
	public static List<AskImage> LinksIn(string question)
	{
		return ImageLink.Matches(question ?? "").Cast<Match>().Select(m => new AskImage { Url = m.Value }).ToList();
	}

	/// <summary>"[Image 1] [Image 2]" for the chat.</summary>
	public static string Tags(int count)
	{
		return string.Join(" ", Enumerable.Range(1, count).Select(i => "[" + IsaClient.Languages.L10n.F("Image {0}", i) + "]"));
	}

	/// <summary>
	/// What every picture shows, written for a model that can't see them, with an eye on the question: "[Image 1] ...".
	/// </summary>
	public static string Describe(List<AskImage> images, string question)
	{
		StringBuilder all = new StringBuilder();
		for (int start = 0; start < images.Count; start += PerRequest)
		{
			List<AskImage> part = images.Skip(start).Take(PerRequest).ToList();
			string instruction = "Another assistant that can't see will answer this question using only your description: \"" + question + "\".\n" +
				"For each picture, write \"[Image N]\" and then what it shows, in detail: objects, people, places, colors, numbers, and any text exactly as written. " +
				"If it is from a game (Terraria or another), name the items, enemies, bosses, biomes and interface you recognize. " +
				"Point out what matters for the question. Don't answer the question; just describe. Plain text, no markdown." +
				"\nPictures here are numbered from " + (start + 1) + ".";
			JArray content = new JArray { new JObject { ["type"] = "text", ["text"] = instruction } };
			foreach (AskImage image in part)
			{
				content.Add(ImagePart(image));
			}
			JArray messages = new JArray { new JObject { ["role"] = "user", ["content"] = content } };
			string text = GroqClient.CompleteMessages(Model, messages, 1500, 0.2f, "none").Trim();
			all.AppendLine(text);
		}
		return AnswerFormat.Clean(all.ToString()).Trim();
	}

	/// <summary>A user message with the pictures after its text (for a model that can see).</summary>
	public static JArray WithImages(string text, List<AskImage> images)
	{
		JArray content = new JArray { new JObject { ["type"] = "text", ["text"] = text } };
		foreach (AskImage image in images.Take(PerRequest))
		{
			content.Add(ImagePart(image));
		}
		return content;
	}

	private static JObject ImagePart(AskImage image)
	{
		string url = image.Url;
		if (image.Data != null)
		{
			byte[] data = Shrink(image.Data, out string type) ?? image.Data;
			url = "data:" + (type ?? image.Type ?? "image/png") + ";base64," + Convert.ToBase64String(data);
		}
		return new JObject
		{
			["type"] = "image_url",
			["image_url"] = new JObject { ["url"] = url }
		};
	}

	/// <summary>The picture as a JPEG no bigger than <see cref="MaxSide"/> (a GIF's first frame); null when it can't be read.</summary>
	private static byte[] Shrink(byte[] data, out string type)
	{
		type = null;
		try
		{
			using MemoryStream input = new MemoryStream(data);
			using Image source = Image.FromStream(input);
			float scale = Math.Min(1f, (float)MaxSide / Math.Max(source.Width, source.Height));
			int w = Math.Max(1, (int)(source.Width * scale));
			int h = Math.Max(1, (int)(source.Height * scale));
			using Bitmap bitmap = new Bitmap(w, h);
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.Clear(Color.White);
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				g.DrawImage(source, 0, 0, w, h);
			}
			ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
			using MemoryStream output = new MemoryStream();
			if (jpeg != null)
			{
				EncoderParameters parameters = new EncoderParameters(1);
				parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
				bitmap.Save(output, jpeg, parameters);
			}
			else
			{
				bitmap.Save(output, ImageFormat.Jpeg);
			}
			type = "image/jpeg";
			return output.ToArray();
		}
		catch
		{
			return null;
		}
	}
}
