using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using IsaClient.Core;
using IsaClient.Networking;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Color = Microsoft.Xna.Framework.Color;
using I = Terraria.ID.ItemID;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Hub;

/// <summary>
/// Real flags of every country (IsaClient.Assets.Flags.png: 48x32 cells in the order of Flags.txt), plus round shapes
/// the proxy window draws with (a soft disc and a ring), made once in code.
/// </summary>
internal static class CountryFlags
{
	private const int CellW = 48;

	private const int CellH = 32;

	private const int Columns = 16;

	private static Texture2D _atlas;

	private static bool _failed;

	private static Texture2D _disc;

	private static Texture2D _ring;

	/// <summary>A flag with a thin dark frame and a soft shine; "auto" draws a globe, unknown codes a question mark.</summary>
	public static void Draw(SpriteBatch sb, string code, Rectangle r, float alpha = 1f)
	{
		if (code == "auto")
		{
			HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(r.Center.X - r.Height / 2 - 2, r.Y - 2, r.Height + 4, r.Height + 4), Color.White * alpha);
			return;
		}
		int index = CountryNames.Index(code);
		Texture2D atlas = Atlas;
		HubKit.Fill(sb, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), new Color(8, 10, 18) * (0.8f * alpha));
		if (index < 0 || atlas == null)
		{
			HubKit.Fill(sb, r, new Color(60, 66, 90) * alpha);
			HubKit.TextCentered(sb, "?", r, Color.White * alpha, Math.Min(0.8f, r.Height / 26f));
			return;
		}
		Rectangle source = new Rectangle(index % Columns * CellW, index / Columns * CellH, CellW, CellH);
		sb.Draw(atlas, r, source, Color.White * alpha);
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height / 2)), Color.White * (0.07f * alpha));
	}

	private static Texture2D Atlas
	{
		get
		{
			if ((_atlas == null || _atlas.IsDisposed) && !_failed)
			{
				try
				{
					using Stream stream = typeof(CountryFlags).Assembly.GetManifestResourceStream("IsaClient.Assets.Flags.png");
					using Bitmap bitmap = new Bitmap(stream);
					_atlas = ToTexture(bitmap);
				}
				catch (Exception ex)
				{
					_failed = true;
					ClientLog.LogException(ex);
				}
			}
			return _atlas;
		}
	}

	/// <summary>A white disc with a soft edge (tint it), 128 px.</summary>
	public static Texture2D Disc => _disc ??= Round(128, 0f);

	/// <summary>A white ring, 128 px, about 7 px thick.</summary>
	public static Texture2D Ring => _ring ??= Round(128, 0.89f);

	private static Texture2D Round(int size, float hole)
	{
		Color[] data = new Color[size * size];
		float radius = size / 2f - 1f;
		float inner = radius * hole;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float d = (float)Math.Sqrt((x + 0.5f - size / 2f) * (x + 0.5f - size / 2f) + (y + 0.5f - size / 2f) * (y + 0.5f - size / 2f));
				float a = MathHelperClamp(radius - d + 0.5f);
				if (hole > 0f)
				{
					a = Math.Min(a, MathHelperClamp(d - inner + 0.5f));
				}
				byte v = (byte)(a * 255f);
				data[y * size + x] = new Color(v, v, v, v);
			}
		}
		Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, size, size);
		texture.SetData(data);
		return texture;
	}

	private static float MathHelperClamp(float value)
	{
		return (value < 0f) ? 0f : ((value > 1f) ? 1f : value);
	}

	/// <summary>Bitmap to texture with premultiplied alpha (what the sprite batch expects).</summary>
	private static Texture2D ToTexture(Bitmap bitmap)
	{
		BitmapData bits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
		System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, bgra, 0, bgra.Length);
		bitmap.UnlockBits(bits);
		Color[] data = new Color[bitmap.Width * bitmap.Height];
		for (int i = 0; i < data.Length; i++)
		{
			int a = bgra[i * 4 + 3];
			data[i] = new Color(bgra[i * 4 + 2] * a / 255, bgra[i * 4 + 1] * a / 255, bgra[i * 4] * a / 255, a);
		}
		Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, bitmap.Width, bitmap.Height);
		texture.SetData(data);
		return texture;
	}
}
