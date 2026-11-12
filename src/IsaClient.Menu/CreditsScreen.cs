using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Chat;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// The credit icons under the main menu credits, shown as item tooltips:
/// the heart ("Thanks": the people who helped IsaClient, Claude and you) and the Z statue (the credits of the client
/// that inspired this one: its people and the ones they thanked). Each name has its own color.
/// </summary>
public static class CreditsScreen
{
	private static readonly Microsoft.Xna.Framework.Color _paleOrange = new Microsoft.Xna.Framework.Color(255, 205, 150);

	/// <summary>People thanked by IsaClient, alphabetical.</summary>
	private static readonly (string Name, Microsoft.Xna.Framework.Color Color)[] _thanks =
	{
		("Ateoz", new Microsoft.Xna.Framework.Color(255, 160, 50)),
		("Caru", new Microsoft.Xna.Framework.Color(70, 105, 255)),
		("Fraku", new Microsoft.Xna.Framework.Color(150, 210, 255)),
		("Merlín", new Microsoft.Xna.Framework.Color(250, 128, 114)),
		("Moha", new Microsoft.Xna.Framework.Color(255, 255, 255)),
		("Vlad", new Microsoft.Xna.Framework.Color(110, 220, 110))
	};

	/// <summary>Everyone in the original client's credits and special thanks, in that order, each with their own color.</summary>
	private static readonly (string Name, Microsoft.Xna.Framework.Color Color)[] _originCredits =
	{
		("bryld", Microsoft.Xna.Framework.Color.Orange),
		("Benzoyl", new Microsoft.Xna.Framework.Color(129, 216, 208)),
		(".3", new Microsoft.Xna.Framework.Color(190, 150, 255)),
		("Bun/Cooki", new Microsoft.Xna.Framework.Color(255, 150, 200)),
		("Will", new Microsoft.Xna.Framework.Color(150, 230, 110)),
		("yay", new Microsoft.Xna.Framework.Color(255, 215, 90)),
		("Async", new Microsoft.Xna.Framework.Color(110, 170, 255)),
		("tML Team", new Microsoft.Xna.Framework.Color(225, 225, 240))
	};

	/// <summary>TerraAngel's contributors on GitHub (github.com/UnrealMultiple/TerraAngel), most contributions first.</summary>
	private static readonly (string Name, Microsoft.Xna.Framework.Color Color)[] _terraAngelCredits =
	{
		("chairclr", new Microsoft.Xna.Framework.Color(255, 120, 120)),
		("LaoSparrow", new Microsoft.Xna.Framework.Color(255, 190, 90)),
		("ACaiCat", new Microsoft.Xna.Framework.Color(255, 230, 110)),
		("THEXN", new Microsoft.Xna.Framework.Color(150, 230, 110)),
		("SharpTheNightmare", new Microsoft.Xna.Framework.Color(110, 220, 210)),
		("Controllerdestiny", new Microsoft.Xna.Framework.Color(110, 170, 255)),
		("punchready", new Microsoft.Xna.Framework.Color(170, 140, 255)),
		("steviegt6", new Microsoft.Xna.Framework.Color(240, 140, 230)),
		("Duck15", new Microsoft.Xna.Framework.Color(225, 225, 240))
	};

	private static bool _registered;

	private static string _terraAngelNotes;

	private static string _thanksNotes;

	private static string _originNotes;

	/// <summary>The heart: hovering it shows "Thanks".</summary>
	public static void DrawThanks(SpriteBatch spriteBatch, Vector2 center, Microsoft.Xna.Framework.Point mouse)
	{
		EnsureRegistered();
		if (MenuIcon.DrawItem(spriteBatch, ItemID.Heart, center, mouse, Microsoft.Xna.Framework.Color.White))
		{
			ShowTooltip("Thanks", _thanksNotes ??= BuildThanks());
		}
	}

	/// <summary>The Z statue: hovering it shows the original client's credits.</summary>
	public static void DrawOriginCredits(SpriteBatch spriteBatch, Vector2 center, Microsoft.Xna.Framework.Point mouse)
	{
		EnsureRegistered();
		if (MenuIcon.DrawItem(spriteBatch, ItemID.AlphabetStatueZ, center, mouse, Microsoft.Xna.Framework.Color.White))
		{
			ShowTooltip("Zaza Credits", _originNotes ??= BuildOriginCredits());
		}
	}

	/// <summary>The T statue, left of the Z: hovering it shows TerraAngel's credits.</summary>
	public static void DrawTerraAngelCredits(SpriteBatch spriteBatch, Vector2 center, Microsoft.Xna.Framework.Point mouse)
	{
		EnsureRegistered();
		if (MenuIcon.DrawItem(spriteBatch, ItemID.AlphabetStatueT, center, mouse, Microsoft.Xna.Framework.Color.White))
		{
			ShowTooltip("TerraAngel Credits", _terraAngelNotes ??= BuildList(_terraAngelCredits));
		}
	}

	private static string BuildList((string Name, Microsoft.Xna.Framework.Color Color)[] names)
	{
		StringBuilder text = new StringBuilder();
		foreach (var (name, color) in names)
		{
			if (text.Length > 0)
			{
				text.AppendLine();
			}
			text.Append("• " + ClientChat.Tint(name, color));
		}
		return text.ToString();
	}

	private static void ShowTooltip(string title, string notes)
	{
		Main.LocalPlayer.mouseInterface = true;
		Item item = Main.DisplayAndGetFakeItem((Terraria.Enums.ItemRarityColor)(-12));
		item.SetNameOverride(title);
		item.BestiaryNotes = notes;
	}

	private static string BuildThanks()
	{
		StringBuilder text = new StringBuilder();
		foreach (var (name, color) in _thanks)
		{
			text.AppendLine("• " + ClientChat.Tint(name, color));
		}
		text.AppendLine("• [isarainbow:You]");
		text.AppendLine("[isaline/" + Hex(new Microsoft.Xna.Framework.Color(120, 120, 150)) + ":-]");
		text.Append("[isasym/" + Hex(_paleOrange) + ":❋] " + ClientChat.Tint("Claude", _paleOrange));
		return text.ToString();
	}

	private static string BuildOriginCredits()
	{
		StringBuilder text = new StringBuilder();
		text.Append("• [isarand/" + Hex(Microsoft.Xna.Framework.Color.Crimson) + ":676767]");
		foreach (var (name, color) in _originCredits)
		{
			text.AppendLine();
			text.Append("• " + ClientChat.Tint(name, color));
		}
		return text.ToString();
	}

	private static string Hex(Microsoft.Xna.Framework.Color color)
	{
		return color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}

	private static void EnsureRegistered()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		ChatManager.Register<ScrambledTagHandler>("isarand");
		ChatManager.Register<SymbolTagHandler>("isasym");
		ChatManager.Register<RuleTagHandler>("isaline");
		ChatManager.Register<RainbowTagHandler>("isarainbow");
	}

	private static Microsoft.Xna.Framework.Color ParseColor(string options, Microsoft.Xna.Framework.Color fallback)
	{
		if (!int.TryParse(options, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
		{
			return fallback;
		}
		return new Microsoft.Xna.Framework.Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
	}

	/// <summary>[isarand/RRGGBB:text] — random letters of about the same length, new ones every time it is drawn (zMod's hidden name).</summary>
	private class ScrambledTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Microsoft.Xna.Framework.Color baseColor, string options)
		{
			return new ScrambledSnippet(text.Length, ParseColor(options, baseColor));
		}
	}

	private class ScrambledSnippet : TextSnippet
	{
		private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

		private readonly string _measure;

		public ScrambledSnippet(int length, Microsoft.Xna.Framework.Color color)
			: base(Randomize(length), color)
		{
			// Fixed width, so the tooltip does not jump around while the letters change.
			_measure = new string('0', length + 2 + (length - 2) / 8);
		}

		private static string Randomize(int length)
		{
			int count = Math.Max(1, length + Main.rand.Next(-1, 2));
			char[] chars = new char[count];
			for (int i = 0; i < count; i++)
			{
				chars[i] = Chars[Main.rand.Next(Chars.Length)];
			}
			return new string(chars);
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Microsoft.Xna.Framework.Color color = default(Microsoft.Xna.Framework.Color), float scale = 1f)
		{
			size = Vector2.Zero;
			if (!justCheckingSize)
			{
				return false;
			}
			size = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(_measure)) * scale;
			return true;
		}
	}

	/// <summary>[isasym/RRGGBB:❋] — a symbol Terraria's font does not have, drawn from a Windows font.</summary>
	private class SymbolTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Microsoft.Xna.Framework.Color baseColor, string options)
		{
			return new SymbolSnippet(text, ParseColor(options, baseColor));
		}
	}

	private class SymbolSnippet : TextSnippet
	{
		private const float Height = 17f;

		public SymbolSnippet(string text, Microsoft.Xna.Framework.Color color)
			: base(text, color)
		{
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Microsoft.Xna.Framework.Color color = default(Microsoft.Xna.Framework.Color), float scale = 1f)
		{
			Texture2D texture = SymbolTextures.Get(Text);
			if (texture == null)
			{
				size = Vector2.Zero;
				return false;
			}
			float drawScale = Height / texture.Height * scale;
			size = new Vector2(texture.Width * drawScale + 3f * scale, FontAssets.MouseText.Value.LineSpacing * scale);
			if (!justCheckingSize)
			{
				spriteBatch.Draw(texture, position + new Vector2(0f, 2f * scale), null, color, 0f, Vector2.Zero, drawScale, SpriteEffects.None, 0f);
			}
			return true;
		}
	}

	/// <summary>[isaline/RRGGBB:-] — a real horizontal line (a separator inside the tooltip).</summary>
	private class RuleTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Microsoft.Xna.Framework.Color baseColor, string options)
		{
			return new RuleSnippet(ParseColor(options, baseColor));
		}
	}

	private class RuleSnippet : TextSnippet
	{
		public RuleSnippet(Microsoft.Xna.Framework.Color color)
			: base(" ", color)
		{
			UseRawColor = true;
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Microsoft.Xna.Framework.Color color = default(Microsoft.Xna.Framework.Color), float scale = 1f)
		{
			// As wide as the widest line ("❋ Mythos 5/5.1").
			float width = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T("   Mythos 5/5.1")).X * scale;
			size = new Vector2(width, FontAssets.MouseText.Value.LineSpacing * scale);
			if (!justCheckingSize)
			{
				bool shadow = color.R == 0 && color.G == 0 && color.B == 0;
				if (!shadow)
				{
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle((int)position.X, (int)(position.Y + size.Y / 2f - 2f), (int)width, 2), color);
				}
			}
			return true;
		}
	}

	/// <summary>[isarainbow:text] — every letter in its own color, the colors slowly moving along the word.</summary>
	private class RainbowTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Microsoft.Xna.Framework.Color baseColor, string options)
		{
			return new RainbowSnippet(text);
		}
	}

	private class RainbowSnippet : TextSnippet
	{
		public RainbowSnippet(string text)
			: base(text)
		{
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Microsoft.Xna.Framework.Color color = default(Microsoft.Xna.Framework.Color), float scale = 1f)
		{
			ReLogic.Graphics.DynamicSpriteFont font = FontAssets.MouseText.Value;
			size = font.MeasureString(IsaClient.Languages.L10n.T(Text)) * scale;
			if (justCheckingSize)
			{
				return true;
			}
			bool shadow = color.R == 0 && color.G == 0 && color.B == 0;
			float x = position.X;
			for (int i = 0; i < Text.Length; i++)
			{
				string letter = Text[i].ToString();
				Microsoft.Xna.Framework.Color tint = shadow ? color : Main.hslToRgb((Main.GlobalTimeWrappedHourly * 0.35f + i * 0.17f) % 1f, 1f, 0.68f);
				ReLogic.Graphics.DynamicSpriteFontExtensionMethods.DrawString(spriteBatch, font, IsaClient.Languages.L10n.T(letter), new Vector2(x, position.Y), tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				x += font.MeasureString(IsaClient.Languages.L10n.T(letter)).X * scale;
			}
			return true;
		}
	}

	private static class SymbolTextures
	{
		private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _cache = new System.Collections.Generic.Dictionary<string, Texture2D>();

		public static Texture2D Get(string text)
		{
			if (_cache.TryGetValue(text, out var texture) && (texture == null || !texture.IsDisposed))
			{
				return texture;
			}
			try
			{
				texture = Render(text);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				texture = null;
			}
			_cache[text] = texture;
			return texture;
		}

		/// <summary>White glyph on transparency, cropped to its pixels, premultiplied (it is tinted when drawn).</summary>
		private static Texture2D Render(string text)
		{
			const int canvas = 96;
			using Bitmap bitmap = new Bitmap(canvas, canvas, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
			using (Font font = new Font("Segoe UI Symbol", 48f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
			using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
			{
				graphics.Clear(System.Drawing.Color.Transparent);
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
				graphics.DrawString(text, font, Brushes.White, new RectangleF(0, 0, canvas, canvas), format);
			}
			BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, canvas, canvas), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			byte[] bytes = new byte[data.Stride * canvas];
			Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
			bitmap.UnlockBits(data);
			int left = canvas, top = canvas, right = -1, bottom = -1;
			for (int y = 0; y < canvas; y++)
			{
				for (int x = 0; x < canvas; x++)
				{
					if (bytes[y * data.Stride + x * 4 + 3] > 8)
					{
						left = Math.Min(left, x);
						right = Math.Max(right, x);
						top = Math.Min(top, y);
						bottom = Math.Max(bottom, y);
					}
				}
			}
			if (right < 0)
			{
				return null;
			}
			int width = right - left + 1;
			int height = bottom - top + 1;
			Microsoft.Xna.Framework.Color[] pixels = new Microsoft.Xna.Framework.Color[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int a = bytes[(y + top) * data.Stride + (x + left) * 4 + 3];
					pixels[y * width + x] = new Microsoft.Xna.Framework.Color(a, a, a, a);
				}
			}
			Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, width, height);
			texture.SetData(pixels);
			return texture;
		}
	}
}
