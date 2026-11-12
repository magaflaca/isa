using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.ChatImages;
using IsaClient.Core;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace IsaClient.Sharing;

/// <summary>
/// Characters and schematics as Imgur pictures, for when the pipe is off: a card (the character's look and name, the
/// schematic's map) with the file hidden in its pixels (<see cref="PixelVault"/>). Imgur serves a picture the moment
/// it's up (archive.org can take minutes), and the chat only needs its short code.
/// <list type="bullet">
/// <item>Every upload is read back from Imgur and opened before it's used: if Imgur changed it past repair, it's sent
/// again, and after that the sender falls back to archive.org (which also gets a copy every time, as a spare).</item>
/// <item>The picture is made big enough for three copies of the file when it can be, and kept far under the size
/// Imgur turns into JPEG.</item>
/// <item>Fetching asks for PNG only, tries a few times, and checks the file's own code before using it.</item>
/// </list>
/// </summary>
public static class ShareImage
{
	/// <summary>Kept far below the 5 MB past which Imgur turns a PNG into a JPEG.</summary>
	private const int MaxPng = 4 * 1024 * 1024;

	/// <summary>The biggest picture made (pixels).</summary>
	private const int MaxPixels = 1600 * 1000;

	private static readonly Dictionary<string, ArchiveStore.Job> _uploads = new Dictionary<string, ArchiveStore.Job>();

	private static readonly Dictionary<string, ArchiveStore.Job> _fetches = new Dictionary<string, ArchiveStore.Job>();

	private static string Key(ShareKind kind, string key) => (char)ShareKinds.Letter(kind) + ":" + key;

	/// <summary>The upload of one of yours (null when none was started).</summary>
	public static ArchiveStore.Job Uploaded(ShareKind kind, string key)
	{
		lock (_uploads)
		{
			_uploads.TryGetValue(Key(kind, key), out ArchiveStore.Job job);
			return job;
		}
	}

	/// <summary>Starts (once) putting a file on Imgur inside a card; the job's Link is set when it's up and checked.</summary>
	public static ArchiveStore.Job Upload(ShareKind kind, string key, Func<byte[]> payload, Func<Bitmap> cover)
	{
		lock (_uploads)
		{
			// A failed one stays failed for a while (the sender moves on to archive.org); linked again later, it's retried.
			if (_uploads.TryGetValue(Key(kind, key), out ArchiveStore.Job existing) && (!existing.Failed || (DateTime.Now - existing.Started).TotalMinutes < 2))
			{
				return existing;
			}
			ArchiveStore.Job job = new ArchiveStore.Job();
			_uploads[Key(kind, key)] = job;
			Task.Run(delegate
			{
				try
				{
					job.Link = Send(kind, payload(), cover, job);
					job.Done = job.Link != null;
					job.Failed = !job.Done;
				}
				catch (Exception ex)
				{
					job.Failed = true;
					job.Error = ex.Message;
					ClientLog.LogOnce(ex);
				}
				finally
				{
					job.Progress = 1f;
					job.Busy = false;
				}
			});
			return job;
		}
	}

	/// <summary>Makes the picture, uploads it and reads it back; twice if needed. The checked link, or null.</summary>
	private static string Send(ShareKind kind, byte[] payload, Func<Bitmap> cover, ArchiveStore.Job job)
	{
		if (payload == null || payload.Length == 0)
		{
			return null;
		}
		byte[] png;
		using (Bitmap art = cover())
		using (Bitmap sized = Fit(art, payload.Length))
		{
			png = (sized != null) ? PixelVault.Hide(sized, (byte)kind, payload) : null;
		}
		if (png == null || png.Length > MaxPng)
		{
			job.Error = "too big for a picture";
			return null;
		}
		for (int round = 0; round < 2; round++)
		{
			ImgurClient.Upload upload = ImgurClient.Send(png, "isaclient.png", "image/png", p => job.Progress = p * 0.85f, null);
			// Read back what Imgur serves: only a link that gives the same file is used.
			for (int attempt = 0; attempt < 4; attempt++)
			{
				if (attempt > 0)
				{
					Thread.Sleep(1500 * attempt);
				}
				try
				{
					byte[] back = PixelVault.Extract(ImgurClient.Download(upload.Link, MaxPng * 2, null, "image/png"), out _);
					if (back != null && back.SequenceEqual(payload))
					{
						return upload.Link;
					}
				}
				catch (Exception)
				{
				}
			}
			ClientLog.LogOnce(new InvalidOperationException("Imgur didn't give back a shared file intact (" + upload.Link + ")."));
		}
		job.Error = "Imgur changed the picture";
		return null;
	}

	/// <summary>The card scaled up (pixel art stays sharp) until three copies fit, or at least one; null when even the biggest is too small.</summary>
	private static Bitmap Fit(Bitmap art, int payload)
	{
		int w = art.Width;
		int h = art.Height;
		float scale = 1f;
		while (PixelVault.Capacity((int)(w * scale), (int)(h * scale)) < payload * 3 && (w * scale) * (h * scale) * 1.21f <= MaxPixels)
		{
			scale *= 1.1f;
		}
		int sw = (int)(w * scale);
		int sh = (int)(h * scale);
		if (PixelVault.Capacity(sw, sh) < payload)
		{
			return null;
		}
		Bitmap sized = new Bitmap(sw, sh, PixelFormat.Format24bppRgb);
		using Graphics g = Graphics.FromImage(sized);
		g.InterpolationMode = InterpolationMode.NearestNeighbor;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		g.DrawImage(art, new Rectangle(0, 0, sw, sh));
		return sized;
	}

	/// <summary>
	/// Fetches a file from its Imgur card (once per file): <paramref name="take"/> gets the bytes (off the main thread)
	/// and says whether they're the right ones; <paramref name="failed"/> runs when it can't be had this way.
	/// </summary>
	public static ArchiveStore.Job Fetch(ShareKind kind, string key, string link, Func<byte[], bool> take, Action failed)
	{
		lock (_fetches)
		{
			if (_fetches.TryGetValue(Key(kind, key), out ArchiveStore.Job existing))
			{
				return existing;
			}
			ArchiveStore.Job job = new ArchiveStore.Job();
			_fetches[Key(kind, key)] = job;
			Task.Run(delegate
			{
				try
				{
					for (int attempt = 0; attempt < 6 && !job.Done; attempt++)
					{
						if (attempt > 0)
						{
							Thread.Sleep(Math.Min(10000, 1500 * attempt));
						}
						try
						{
							byte[] image = ImgurClient.Download(link, MaxPng * 2, null, "image/png");
							job.Progress = 0.9f;
							byte[] file = PixelVault.Extract(image, out byte found);
							if (file != null && found == (byte)kind && take(file))
							{
								job.Done = true;
							}
						}
						catch (Exception)
						{
						}
					}
				}
				finally
				{
					job.Failed = !job.Done;
					job.Progress = 1f;
					job.Busy = false;
					if (job.Failed)
					{
						failed?.Invoke();
					}
				}
			});
			return job;
		}
	}

	#region Cards

	private static Color C(XnaColor c) => Color.FromArgb(c.R, c.G, c.B);

	private static Bitmap Canvas(int w, int h, Color top, Color bottom, out Graphics g)
	{
		Bitmap b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
		g = Graphics.FromImage(b);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		using (LinearGradientBrush back = new LinearGradientBrush(new Rectangle(0, 0, w, h), top, bottom, 90f))
		{
			g.FillRectangle(back, 0, 0, w, h);
		}
		using (Pen frame = new Pen(Color.FromArgb(73, 94, 171), 4f))
		{
			g.DrawRectangle(frame, 2, 2, w - 5, h - 5);
		}
		return b;
	}

	private static void Footer(Graphics g, int w, int h, string text)
	{
		using Font small = new Font("Segoe UI", 9f);
		using SolidBrush muted = new SolidBrush(Color.FromArgb(154, 167, 190));
		g.DrawString(text, small, muted, 14, h - 24);
	}

	/// <summary>A character's card: a little figure in its colors, its name and its palette.</summary>
	public static Bitmap CharacterCover(string name, StyleLook look)
	{
		look ??= new StyleLook();
		Bitmap b = Canvas(360, 200, Color.FromArgb(44, 58, 108), Color.FromArgb(22, 28, 56), out Graphics g);
		using (g)
		{
			g.SmoothingMode = SmoothingMode.None;
			using (SolidBrush slot = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
			{
				g.FillRectangle(slot, 16, 16, 112, 168);
			}
			// The figure, cell by cell (8 px): hair, face, eyes, shirt, sleeves, pants, shoes.
			string[] rows =
			{
				"..hhhh..", ".hhhhhh.", "hhsssshh", "hseseshh", ".hsssshh", "..ssss..", ".uttttu.", "uttttttu",
				"sttttttS", "sttttttS", "..pppp..", "..pppp..", "..p..p..", "..p..p..", ".oo..oo."
			};
			Dictionary<char, Color> colors = new Dictionary<char, Color>
			{
				['h'] = C(look.HairColor), ['s'] = C(look.SkinColor), ['S'] = C(look.SkinColor), ['e'] = C(look.EyeColor),
				['t'] = C(look.ShirtColor), ['u'] = C(look.UnderShirtColor), ['p'] = C(look.PantsColor), ['o'] = C(look.ShoeColor)
			};
			for (int y = 0; y < rows.Length; y++)
			{
				for (int x = 0; x < rows[y].Length; x++)
				{
					if (colors.TryGetValue(rows[y][x], out Color c))
					{
						using SolidBrush brush = new SolidBrush(c);
						g.FillRectangle(brush, 40 + x * 8, 32 + y * 9, 8, 9);
					}
				}
			}
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (Font title = new Font("Segoe UI", 17f, FontStyle.Bold))
			{
				g.DrawString(Short(name, 18), title, Brushes.White, 140, 22);
			}
			using (Font sub = new Font("Segoe UI", 10f))
			using (SolidBrush muted = new SolidBrush(Color.FromArgb(154, 167, 190)))
			{
				g.DrawString("IsaClient character", sub, muted, 142, 56);
			}
			XnaColor[] palette = { look.HairColor, look.SkinColor, look.EyeColor, look.ShirtColor, look.UnderShirtColor, look.PantsColor, look.ShoeColor };
			for (int i = 0; i < palette.Length; i++)
			{
				using SolidBrush swatch = new SolidBrush(C(palette[i]));
				g.FillEllipse(swatch, 144 + i * 28, 96, 20, 20);
				g.DrawEllipse(Pens.Black, 144 + i * 28, 96, 20, 20);
			}
			Footer(g, 360, 200, "Open it in IsaClient: the whole character is inside this picture.");
		}
		return b;
	}

	/// <summary>A schematic's card: its map, name and size.</summary>
	public static Bitmap SchematicCover(SchematicShare.Entry e)
	{
		Bitmap b = Canvas(480, 320, Color.FromArgb(34, 52, 84), Color.FromArgb(18, 24, 44), out Graphics g);
		using (g)
		{
			Rectangle area = new Rectangle(16, 16, 448, 240);
			using (SolidBrush slot = new SolidBrush(Color.FromArgb(255, 10, 14, 28)))
			{
				g.FillRectangle(slot, area);
			}
			XnaColor[] pixels = e?.Pixels;
			if (pixels != null && e.PreviewWidth > 0 && e.PreviewHeight > 0)
			{
				using Bitmap map = new Bitmap(e.PreviewWidth, e.PreviewHeight, PixelFormat.Format24bppRgb);
				for (int y = 0; y < e.PreviewHeight; y++)
				{
					for (int x = 0; x < e.PreviewWidth; x++)
					{
						XnaColor c = pixels[y * e.PreviewWidth + x];
						map.SetPixel(x, y, (c.A == 0) ? Color.FromArgb(10, 14, 28) : C(c));
					}
				}
				float fit = Math.Min((float)area.Width / map.Width, (float)area.Height / map.Height);
				int mw = (int)(map.Width * fit);
				int mh = (int)(map.Height * fit);
				g.InterpolationMode = InterpolationMode.NearestNeighbor;
				g.PixelOffsetMode = PixelOffsetMode.Half;
				g.DrawImage(map, new Rectangle(area.X + (area.Width - mw) / 2, area.Y + (area.Height - mh) / 2, mw, mh));
			}
			using (Font title = new Font("Segoe UI", 14f, FontStyle.Bold))
			{
				g.DrawString(Short(e?.Name ?? "Schematic", 34), title, Brushes.White, 16, 262);
			}
			Footer(g, 480, 320, ((e != null) ? (e.Width + "x" + e.Height + " tiles. ") : "") + "Open it in IsaClient: the schematic is inside this picture.");
		}
		return b;
	}

	private static string Short(string s, int max)
	{
		s = (s ?? "").Trim();
		return (s.Length > max) ? (s.Substring(0, max - 1) + "…") : s;
	}

	#endregion
}
