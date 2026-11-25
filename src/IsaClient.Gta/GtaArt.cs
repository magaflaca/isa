using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Win32;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Gta;

internal static class GtaArt
{
	public static readonly Color Pink = new Color(236, 72, 153);

	public static readonly Color Orange = new Color(251, 170, 92);

	public static readonly Color Violet = new Color(112, 76, 210);

	public static readonly Color Soft = new Color(214, 208, 228);

	private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

	public static Texture2D Cover => Load("GtaSix");

	public static Texture2D Card => Load("GtaCard");

	public static Texture2D Pad => Load("GtaPad");

	public static Texture2D Keys => Load("GtaKeys");

	public static Texture2D Logo => Load("GtaLogo");

	public static Texture2D KeyArt => Load("GtaKeyArt", ".jpg");

	private static Texture2D Load(string name, string extension = ".png")
	{
		if (_textures.TryGetValue(name, out Texture2D tex))
		{
			return tex;
		}
		try
		{
			using Stream stream = typeof(GtaArt).Assembly.GetManifestResourceStream("IsaClient.Assets." + name + extension);
			tex = (stream != null) ? Texture2D.FromStream(Main.instance.GraphicsDevice, stream) : null;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			tex = null;
		}
		_textures[name] = tex;
		return tex;
	}

	public static void Smooth(SpriteBatch sb, BlendState blend = null)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, blend ?? BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
	}

	public static void Fill(SpriteBatch sb, Rectangle r, Color color)
	{
		sb.Draw(TextureAssets.MagicPixel.Value, r, new Rectangle(0, 0, 1, 1), color);
	}

	public static void CoverFit(SpriteBatch sb, Texture2D tex, Rectangle area, Color color, float zoom = 1f, Vector2 drift = default(Vector2))
	{
		if (tex == null)
		{
			return;
		}
		float scale = Math.Max((float)area.Width / tex.Width, (float)area.Height / tex.Height) * zoom;
		Vector2 size = new Vector2(tex.Width, tex.Height) * scale;
		Vector2 pos = new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f) + drift;
		sb.Draw(tex, pos, null, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}

	public static void Fit(SpriteBatch sb, Texture2D tex, Rectangle area, Color color)
	{
		if (tex == null)
		{
			return;
		}
		float scale = Math.Min((float)area.Width / tex.Width, (float)area.Height / tex.Height);
		Vector2 size = new Vector2(tex.Width, tex.Height) * scale;
		sb.Draw(tex, new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f), null, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}

	public static void VerticalShade(SpriteBatch sb, Rectangle r, Color top, Color bottom)
	{
		int bands = Math.Max(1, Math.Min(48, r.Height / 4));
		for (int i = 0; i < bands; i++)
		{
			int y0 = r.Y + r.Height * i / bands;
			int y1 = r.Y + r.Height * (i + 1) / bands;
			Fill(sb, new Rectangle(r.X, y0, r.Width, y1 - y0), Color.Lerp(top, bottom, (i + 0.5f) / bands));
		}
	}

	public static void HorizontalShade(SpriteBatch sb, Rectangle r, Color left, Color right)
	{
		int bands = Math.Max(1, Math.Min(64, r.Width / 4));
		for (int i = 0; i < bands; i++)
		{
			int x0 = r.X + r.Width * i / bands;
			int x1 = r.X + r.Width * (i + 1) / bands;
			Fill(sb, new Rectangle(x0, r.Y, x1 - x0, r.Height), Color.Lerp(left, right, (i + 0.5f) / bands));
		}
	}

	private static DynamicSpriteFont Body => FontAssets.MouseText.Value;

	private static DynamicSpriteFont Title => FontAssets.DeathText.Value;

	public static Vector2 Measure(string text, float scale, bool big = false)
	{
		return (big ? Title : Body).MeasureString(text) * scale;
	}

	public static void Text(SpriteBatch sb, string text, Vector2 pos, Color color, float scale, bool big = false, float shadow = 0.55f)
	{
		if (string.IsNullOrEmpty(text) || color.A == 0)
		{
			return;
		}
		DynamicSpriteFont font = big ? Title : Body;
		float alpha = color.A / 255f;
		Vector2 offset = new Vector2(0f, big ? 3f : 2f) * Math.Max(0.6f, scale);
		sb.DrawString(font, text, pos + offset, Color.Black * (shadow * alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}

	public static void Centered(SpriteBatch sb, string text, float centerX, float y, Color color, float scale, bool big = false)
	{
		Vector2 size = Measure(text, scale, big);
		Text(sb, text, new Vector2((float)Math.Round(centerX - size.X / 2f), y), color, scale, big);
	}

	public static string Spaced(string text)
	{
		return string.Join(" ", text.ToUpperInvariant().ToCharArray());
	}

	public static string FitText(string text, float maxWidth, float scale)
	{
		if (Measure(text, scale).X <= maxWidth)
		{
			return text;
		}
		while (text.Length > 1 && Measure(text + "...", scale).X > maxWidth)
		{
			text = text.Substring(0, text.Length - 1);
		}
		return text.TrimEnd() + "...";
	}

	public static void Spinner(SpriteBatch sb, Vector2 center, float radius, Color color)
	{
		double t = GameArt.Now;
		const int dots = 12;
		float head = (float)(t * 1.6 % 1.0) * dots;
		for (int i = 0; i < dots; i++)
		{
			float angle = MathHelper.TwoPi * i / dots - MathHelper.PiOver2;
			float behind = (head - i + dots) % dots;
			float light = 0.18f + 0.82f * (float)Math.Pow(1f - behind / dots, 3.0);
			Vector2 at = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
			float size = radius * (0.3f + 0.12f * light);
			GameArt.Disc(sb, at, size, size, color * light);
		}
	}

	public static bool Button(SpriteBatch sb, string id, Rectangle r, string label, bool primary, bool enabled = true, Texture2D icon = null, float alpha = 1f)
	{
		bool hover = enabled && alpha > 0.6f && MusicPlayerWindow.MouseIn(r);
		if (hover)
		{
			MusicDrawing.Hover(id);
		}
		float lift = hover ? 1f : 0f;
		Color back = primary ? Color.Lerp(Pink, Color.White, 0.12f * lift) : (Color.White * (hover ? 0.16f : 0.08f));
		if (!enabled)
		{
			back = Color.White * 0.05f;
		}
		Fill(sb, r, back * alpha);
		if (!primary)
		{
			Color line = Color.White * ((hover ? 0.55f : 0.28f) * alpha);
			Fill(sb, new Rectangle(r.X, r.Y, r.Width, 1), line);
			Fill(sb, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), line);
			Fill(sb, new Rectangle(r.X, r.Y + 1, 1, r.Height - 2), line);
			Fill(sb, new Rectangle(r.Right - 1, r.Y + 1, 1, r.Height - 2), line);
		}
		else if (enabled)
		{
			Fill(sb, new Rectangle(r.X, r.Bottom - 3, r.Width, 3), Color.Black * (0.18f * alpha));
		}
		string text = IsaClient.Languages.L10n.T(label);
		const float scale = 0.92f;
		Vector2 size = Measure(text, scale);
		float iconW = (icon != null) ? (r.Height * 0.62f * icon.Width / icon.Height + 10f) : 0f;
		float x = r.X + (r.Width - size.X - iconW) / 2f;
		Color color = (enabled ? Color.White : (Color.White * 0.35f)) * alpha;
		if (icon != null)
		{
			float h = r.Height * 0.42f;
			float w = h * icon.Width / icon.Height;
			sb.Draw(icon, new Rectangle((int)x, (int)(r.Center.Y - h / 2f), (int)w, (int)h), color);
			x += iconW;
		}
		Text(sb, text, new Vector2((float)Math.Round(x), r.Y + (r.Height - size.Y) / 2f + 3f), color, scale, false, 0.35f);
		return hover && MusicPlayerWindow.Clicked;
	}

	public static Texture2D Blurred(Texture2D frame)
	{
		if (frame == null || frame.IsDisposed)
		{
			return null;
		}
		try
		{
			int w = frame.Width;
			int h = frame.Height;
			Color[] pixels = new Color[w * h];
			frame.GetData(pixels);
			int sw = Math.Max(16, w / 12);
			int sh = Math.Max(9, h / 12);
			float[] r = new float[sw * sh];
			float[] g = new float[sw * sh];
			float[] b = new float[sw * sh];
			for (int y = 0; y < sh; y++)
			{
				int y0 = y * h / sh;
				int y1 = Math.Max(y0 + 1, (y + 1) * h / sh);
				for (int x = 0; x < sw; x++)
				{
					int x0 = x * w / sw;
					int x1 = Math.Max(x0 + 1, (x + 1) * w / sw);
					float sr = 0f, sg = 0f, sbl = 0f;
					int n = 0;
					for (int yy = y0; yy < y1; yy += 2)
					{
						for (int xx = x0; xx < x1; xx += 2)
						{
							Color c = pixels[yy * w + xx];
							sr += c.R;
							sg += c.G;
							sbl += c.B;
							n++;
						}
					}
					int i = y * sw + x;
					r[i] = sr / n;
					g[i] = sg / n;
					b[i] = sbl / n;
				}
			}
			for (int pass = 0; pass < 3; pass++)
			{
				Box(r, sw, sh);
				Box(g, sw, sh);
				Box(b, sw, sh);
			}
			Color[] small = new Color[sw * sh];
			for (int i = 0; i < small.Length; i++)
			{
				small[i] = new Color((int)r[i], (int)g[i], (int)b[i], 255);
			}
			Texture2D tex = new Texture2D(Main.instance.GraphicsDevice, sw, sh, mipMap: false, SurfaceFormat.Color);
			tex.SetData(small);
			return tex;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	private static void Box(float[] v, int w, int h)
	{
		float[] tmp = new float[v.Length];
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				float s = 0f;
				int n = 0;
				for (int k = -2; k <= 2; k++)
				{
					int xx = Math.Max(0, Math.Min(w - 1, x + k));
					s += v[y * w + xx];
					n++;
				}
				tmp[y * w + x] = s / n;
			}
		}
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				float s = 0f;
				int n = 0;
				for (int k = -2; k <= 2; k++)
				{
					int yy = Math.Max(0, Math.Min(h - 1, y + k));
					s += tmp[yy * w + x];
					n++;
				}
				v[y * w + x] = s / n;
			}
		}
	}

	private static string _gpu;

	private static string _cpu;

	private static string _memory;

	public static string Gpu
	{
		get
		{
			if (_gpu == null)
			{
				try
				{
					_gpu = Clean(GraphicsAdapter.DefaultAdapter.Description);
				}
				catch
				{
					_gpu = "";
				}
				if (_gpu.Length == 0)
				{
					_gpu = IsaClient.Languages.L10n.T("Integrated graphics");
				}
			}
			return _gpu;
		}
	}

	public static string Cpu
	{
		get
		{
			if (_cpu == null)
			{
				try
				{
					using RegistryKey key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
					_cpu = Clean(key?.GetValue("ProcessorNameString") as string);
				}
				catch
				{
					_cpu = "";
				}
				if (_cpu.Length == 0)
				{
					_cpu = Environment.ProcessorCount + " cores";
				}
			}
			return _cpu;
		}
	}

	public static string Memory
	{
		get
		{
			if (_memory == null)
			{
				MemoryStatus status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
				double gb = GlobalMemoryStatusEx(ref status) ? status.TotalPhys / 1073741824.0 : 0.0;
				_memory = (gb > 0.0) ? (Math.Round(gb).ToString("0") + " GB") : "?";
			}
			return _memory;
		}
	}

	private static string Clean(string text)
	{
		text = (text ?? "").Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace(" CPU", "").Trim();
		while (text.Contains("  "))
		{
			text = text.Replace("  ", " ");
		}
		int at = text.IndexOf(" @ ", StringComparison.Ordinal);
		return (at > 0) ? text.Substring(0, at) : text;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatus
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhys;

		public ulong AvailPhys;

		public ulong TotalPageFile;

		public ulong AvailPageFile;

		public ulong TotalVirtual;

		public ulong AvailVirtual;

		public ulong AvailExtendedVirtual;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
