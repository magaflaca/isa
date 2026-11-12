using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// The IsaClient logo on the title screen (day and night versions, cross-faded like Terraria's own). It replaces the
/// modern, classic and boulder logos; [F4] in the main menu switches back to Terraria's original logo (saved as
/// "IsaClientLogo" in the client settings). The window icon follows it (IsaClient tree / Terraria's icon), and so does
/// the menu music (IsaClient.Music.TitleMusic / Terraria's title theme).
/// </summary>
public static class ClientLogo
{
	[Setting("IsaClientLogo", Remembered = true)]
	public static bool Enabled = true;

	private static Texture2D _day;

	private static Texture2D _night;

	private static bool _failed;

	private static Texture2D _tree;

	private static bool _treeFailed;

	private static bool? _iconShown;

	private static Icon _iconBig;

	private static Icon _iconSmall;

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

	/// <summary>Draws the logo where Terraria draws its own; false when the original logo should be drawn instead.</summary>
	public static bool TryDraw(SpriteBatch spriteBatch, Microsoft.Xna.Framework.Color dayColor, Microsoft.Xna.Framework.Color nightColor, float rotation, float scale)
	{
		UpdateWindowIcon();
		if (!Enabled || _failed || !EnsureLoaded())
		{
			return false;
		}
		Vector2 position = new Vector2(Main.screenWidth / 2, 100f);
		Vector2 origin = new Vector2(_day.Width / 2f, _day.Height / 2f);
		spriteBatch.Draw(_day, position, null, dayColor, rotation, origin, scale, SpriteEffects.None, 0f);
		spriteBatch.Draw(_night, position, null, nightColor, rotation, origin, scale, SpriteEffects.None, 0f);
		return true;
	}

	public static void Toggle()
	{
		Enabled = !Enabled;
		ClientSettings.Save();
		UpdateWindowIcon();
	}

	/// <summary>The window / taskbar icon follows the same choice: the IsaClient tree, or Terraria's original icon.</summary>
	private static void UpdateWindowIcon()
	{
		if (_iconShown == Enabled)
		{
			return;
		}
		_iconShown = Enabled;
		try
		{
			IntPtr window = Main.instance.Window.Handle;
			string resource = Enabled ? "IsaClient.Assets.IsaIcon.ico" : "IsaClient.Assets.TerrariaIcon.ico";
			Icon big;
			Icon small;
			using (Stream stream = typeof(ClientLogo).Assembly.GetManifestResourceStream(resource))
			{
				using Icon all = new Icon(stream);
				big = new Icon(all, 48, 48);
				small = new Icon(all, 16, 16);
			}
			SendMessage(window, 128, (IntPtr)1, big.Handle);
			SendMessage(window, 128, IntPtr.Zero, small.Handle);
			_iconBig?.Dispose();
			_iconSmall?.Dispose();
			_iconBig = big;
			_iconSmall = small;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static bool EnsureLoaded()
	{
		if (_day != null && !_day.IsDisposed && _night != null && !_night.IsDisposed)
		{
			return true;
		}
		try
		{
			// Same width as Terraria's logo, so it sits and sways exactly like the original.
			int width = Math.Max(64, TextureAssets.Logo.Width());
			_day = Load("IsaClient.Assets.IsaLogoDay.png", width);
			_night = Load("IsaClient.Assets.IsaLogoNight.png", width);
			return true;
		}
		catch (Exception ex)
		{
			_failed = true;
			ClientLog.LogException(ex);
			return false;
		}
	}

	/// <summary>Embedded PNG, smoothly resized to the given width, with premultiplied alpha (what the sprite batch expects).</summary>
	private static Texture2D Load(string resource, int width)
	{
		using Stream stream = typeof(ClientLogo).Assembly.GetManifestResourceStream(resource);
		using Bitmap source = new Bitmap(stream);
		return ToTexture(source, width);
	}

	/// <summary>The IsaClient tree (the window icon) as a small texture for buttons; null if it could not be loaded.</summary>
	public static Texture2D Tree
	{
		get
		{
			if ((_tree == null || _tree.IsDisposed) && !_treeFailed)
			{
				try
				{
					using Stream stream = typeof(ClientLogo).Assembly.GetManifestResourceStream("IsaClient.Assets.IsaIcon.ico");
					using Icon icon = new Icon(stream, 64, 64);
					using Bitmap source = icon.ToBitmap();
					_tree = ToTexture(source, source.Width);
				}
				catch (Exception ex)
				{
					_treeFailed = true;
					ClientLog.LogException(ex);
				}
			}
			return _tree;
		}
	}

	/// <summary>Smoothly resizes a bitmap to the given width and turns it into a texture with premultiplied alpha.</summary>
	private static Texture2D ToTexture(Bitmap source, int width)
	{
		int height = Math.Max(1, (int)Math.Round((double)source.Height * width / source.Width));
		using Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
		{
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			using ImageAttributes attributes = new ImageAttributes();
			attributes.SetWrapMode(WrapMode.TileFlipXY);
			graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
		}
		BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		byte[] bytes = new byte[data.Stride * height];
		Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
		bitmap.UnlockBits(data);
		Microsoft.Xna.Framework.Color[] pixels = new Microsoft.Xna.Framework.Color[width * height];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int i = y * data.Stride + x * 4;
				int a = bytes[i + 3];
				pixels[y * width + x] = new Microsoft.Xna.Framework.Color(bytes[i + 2] * a / 255, bytes[i + 1] * a / 255, bytes[i] * a / 255, a);
			}
		}
		Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, width, height);
		texture.SetData(pixels);
		return texture;
	}
}
