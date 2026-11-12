using System;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Hub;

/// <summary>
/// The color picker of a <see cref="FeatureKind.Color"/> feature: a popup with a preview, a hex field, hue / saturation /
/// lightness bars (like the character creator) and the named presets. Changes apply live; closing it (Done, or a
/// click outside) saves.
/// </summary>
internal static class HubColorPicker
{
	private const int Width = 316;

	private const int SwatchSize = 30;

	private const int SwatchesPerRow = 8;

	private static HubFeature _feature;

	private static Vector2 _at;

	private static bool _openedNow;

	private static float _hue;

	private static float _saturation;

	private static float _lightness;

	private static string _hex = "";

	private static HubTheme Theme => HubTheme.Current;

	public static bool IsOpen => _feature != null;

	public static Rectangle Area { get; private set; }

	public static void Open(HubFeature feature)
	{
		if (_feature != null)
		{
			Close();
		}
		_feature = feature;
		_at = new Vector2(HubInput.Mouse.X - Width + 20, HubInput.Mouse.Y + 8);
		_openedNow = true;
		Load(Current());
	}

	public static void Close()
	{
		if (_feature == null)
		{
			return;
		}
		HubFeature feature = _feature;
		_feature = null;
		Area = Rectangle.Empty;
		if (HubInput.Focus == "colorhex")
		{
			HubInput.Focus = null;
		}
		try
		{
			feature.ColorDone?.Invoke();
		}
		catch
		{
		}
	}

	private static Color Current()
	{
		try
		{
			return _feature.GetColor?.Invoke() ?? Color.White;
		}
		catch
		{
			return Color.White;
		}
	}

	/// <summary>Takes a color from outside the bars (preset, hex, default): the bars follow it.</summary>
	private static void Load(Color color)
	{
		Vector3 hsl = Main.rgbToHsl(color);
		if (hsl.Y > 0.001f)
		{
			_hue = hsl.X;
		}
		_saturation = hsl.Y;
		_lightness = hsl.Z;
		_hex = NamedColors.Hex(color);
	}

	private static void Set(Color color, bool fromBars)
	{
		color.A = 255;
		if (!fromBars)
		{
			Load(color);
		}
		else if (HubInput.Focus != "colorhex")
		{
			_hex = NamedColors.Hex(color);
		}
		try
		{
			_feature.SetColor?.Invoke(color);
		}
		catch
		{
		}
	}

	private static Color FromBars()
	{
		return Main.hslToRgb(_hue, _saturation, _lightness);
	}

	public static void Draw(SpriteBatch sb)
	{
		if (_feature == null)
		{
			Area = Rectangle.Empty;
			return;
		}
		int rows = (NamedColors.All.Count + SwatchesPerRow - 1) / SwatchesPerRow;
		int height = 64 + 3 * 34 + 8 + rows * (SwatchSize + 4) + 46;
		Rectangle r = new Rectangle((int)_at.X, (int)_at.Y, Width, height);
		float uiW = Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale / HubInput.Scale;
		float uiH = Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale / HubInput.Scale;
		r.X = (int)MathHelper.Clamp(r.X, 4f, uiW - r.Width - 4);
		r.Y = (int)MathHelper.Clamp(r.Y, 4f, uiH - r.Height - 4);
		Area = r;
		HubInput.Popup = r;
		HubInput.DrawingPopup(true);
		bool close = false;
		try
		{
			HubKit.Panel(sb, r, Theme.Sidebar, Color.Black);
			if (HubInput.In(r))
			{
				HubInput.Hover("colorpicker");
			}
			Color color = Current();

			// Preview, title and hex
			Rectangle preview = new Rectangle(r.X + 12, r.Y + 12, 44, 44);
			HubKit.Slot(sb, preview, Theme.Slot);
			HubKit.Fill(sb, new Rectangle(preview.X + 7, preview.Y + 7, preview.Width - 14, preview.Height - 14), color);
			HubKit.Text(sb, _feature.Title, preview.Right + 10, r.Y + 12, Color.White, 0.86f, r.Right - preview.Right - 44);
			HubKit.RichText(sb, ClientChat.Tint("Sample text", color), preview.Right + 10, r.Y + 36, Color.White, 0.76f, 110f);
			if (HubKit.BareIcon(sb, "colorpicker:close", new Rectangle(r.Right - 28, r.Y + 12, 16, 16), HubIcon.Texture("Images/UI/SearchCancel"), "Close", Color.White))
			{
				close = true;
			}
			Rectangle hexField = new Rectangle(r.Right - 132, r.Y + 32, 120, 26);
			string typed = HubKit.Field(sb, "colorhex", hexField, _hex, "RRGGBB", 7);
			if (typed != _hex)
			{
				_hex = typed;
				if (ClientChat.TryParseColor(typed, out Color parsed))
				{
					Set(parsed, fromBars: false);
					_hex = typed;
				}
			}

			// Hue, saturation, lightness
			int y = r.Y + 66;
			float? hue = Bar(sb, "Hue", "colorpicker:h", r, y, _hue, p => Main.hslToRgb(p, 1f, 0.5f));
			y += 34;
			float? saturation = Bar(sb, "Saturation", "colorpicker:s", r, y, _saturation, p => Main.hslToRgb(_hue, p, _lightness));
			y += 34;
			float? lightness = Bar(sb, "Lightness", "colorpicker:l", r, y, _lightness, p => Main.hslToRgb(_hue, _saturation, p));
			y += 34;
			if (hue.HasValue || saturation.HasValue || lightness.HasValue)
			{
				_hue = hue ?? _hue;
				_saturation = saturation ?? _saturation;
				_lightness = lightness ?? _lightness;
				Set(FromBars(), fromBars: true);
			}

			// Presets
			y += 8;
			int gap = (r.Width - 24 - SwatchesPerRow * SwatchSize) / (SwatchesPerRow - 1);
			for (int i = 0; i < NamedColors.All.Count; i++)
			{
				(string name, Color preset) = NamedColors.All[i];
				Rectangle swatch = new Rectangle(r.X + 12 + (i % SwatchesPerRow) * (SwatchSize + gap), y + (i / SwatchesPerRow) * (SwatchSize + 4), SwatchSize, SwatchSize);
				bool hover = HubInput.In(swatch);
				bool chosen = preset.R == color.R && preset.G == color.G && preset.B == color.B;
				HubKit.Slot(sb, swatch, chosen ? Theme.Accent : (hover ? Theme.CardHover : Theme.Slot));
				HubKit.Fill(sb, new Rectangle(swatch.X + 6, swatch.Y + 6, swatch.Width - 12, swatch.Height - 12), preset);
				if (hover && HubInput.Hover("colorpicker:preset:" + name, ClientChat.Tint(name, preset) + "\nCommand name: " + name.ToLowerInvariant()))
				{
					Set(preset, fromBars: false);
					HubInput.Sound(12);
				}
			}
			y += rows * (SwatchSize + 4) + 6;

			// Buttons
			Rectangle done = new Rectangle(r.Right - 92, y, 80, 28);
			if (HubKit.Button(sb, "colorpicker:done", done, "Done", Color.Lerp(Theme.Slot, Theme.Accent, 0.35f)))
			{
				close = true;
			}
			if (_feature.DefaultColor.HasValue)
			{
				Rectangle reset = new Rectangle(done.X - 92, y, 84, 28);
				if (HubKit.Button(sb, "colorpicker:default", reset, "Default", Theme.Slot, "Back to " + ClientChat.Tint("#" + NamedColors.Hex(_feature.DefaultColor.Value), _feature.DefaultColor.Value)))
				{
					Set(_feature.DefaultColor.Value, fromBars: false);
				}
			}
		}
		finally
		{
			HubInput.DrawingPopup(false);
		}
		bool outside = (HubInput.Clicked || HubInput.RightClicked) && !r.Contains(HubInput.Mouse) && ClientHub.SliderDrag == null;
		if (close || (outside && !_openedNow))
		{
			Close();
			HubInput.Sound(11);
			if (outside)
			{
				HubInput.Consume();
			}
		}
		_openedNow = false;
	}

	private static float? Bar(SpriteBatch sb, string label, string id, Rectangle r, int y, float value, Func<float, Color> gradient)
	{
		HubKit.Text(sb, label, r.X + 12, y + 4, Theme.Muted, 0.72f);
		Rectangle bar = new Rectangle(r.X + 92, y + 2, r.Width - 104, 18);
		return HubKit.Slider(sb, id, bar, value, true, ref ClientHub.SliderDrag, gradient);
	}
}
