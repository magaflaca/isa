using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Hub;

/// <summary>An icon of the new panel: an item sprite (animated like in the inventory) or a texture of the game.</summary>
internal readonly struct HubIcon
{
	private readonly int _item;

	private readonly string _texture;

	private readonly Rectangle? _frame;

	/// <summary>Pixel art drawn sharp (the games' sprites).</summary>
	private readonly Func<Texture2D> _sprite;

	private HubIcon(int item, string texture, Rectangle? frame, Func<Texture2D> sprite = null)
	{
		_item = item;
		_texture = texture;
		_frame = frame;
		_sprite = sprite;
	}

	public static HubIcon Item(int type)
	{
		return new HubIcon(type, null, null);
	}

	/// <summary>A texture path under Content/Images ("Images/UI/ButtonFavoriteActive"), optionally one frame of it.</summary>
	public static HubIcon Texture(string path, Rectangle? frame = null)
	{
		return new HubIcon(0, path, frame);
	}

	/// <summary>One frame of a texture of ours, pixel art kept sharp when it's scaled.</summary>
	public static HubIcon Sprite(Func<Texture2D> texture, Rectangle frame)
	{
		return new HubIcon(0, null, frame, texture);
	}

	/// <summary>A game's icon: its sprite or its item.</summary>
	public static HubIcon Game(IsaClient.Games.PipeGame game)
	{
		return game.Sprite.HasValue ? Sprite(() => IsaClient.Games.GameArt.Atlas, game.Sprite.Value) : Item(game.Icon);
	}

	public bool IsEmpty => _item <= 0 && _texture == null && _sprite == null;

	public int ItemType => _item;

	/// <summary>Draws the icon fitted in the box (never scaled above <paramref name="maxScale"/>).</summary>
	public void Draw(SpriteBatch sb, Rectangle area, Color color, float maxScale = 2f, float rotation = 0f)
	{
		Texture2D texture;
		Rectangle source;
		if (!Resolve(out texture, out source))
		{
			return;
		}
		float scale = Math.Min(Math.Min((float)area.Width / source.Width, (float)area.Height / source.Height), maxScale);
		Vector2 center = new Vector2(area.X + area.Width / 2f, area.Y + area.Height / 2f);
		if (_sprite != null)
		{
			// Small pixel art blurs when scaled up: whole sizes, nearest pixel.
			scale = Math.Min((float)area.Width / source.Width, (float)area.Height / source.Height);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, HubInput.Clipping ? new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			} : RasterizerState.CullCounterClockwise, null, HubInput.Matrix);
			sb.Draw(texture, center, source, color, rotation, new Vector2(source.Width, source.Height) / 2f, scale, SpriteEffects.None, 0f);
			HubInput.Restart(sb, HubInput.Clipping);
			return;
		}
		sb.Draw(texture, center, source, color, rotation, new Vector2(source.Width, source.Height) / 2f, scale, SpriteEffects.None, 0f);
	}

	private bool Resolve(out Texture2D texture, out Rectangle source)
	{
		texture = null;
		source = Rectangle.Empty;
		try
		{
			if (_sprite != null)
			{
				texture = _sprite();
				source = _frame ?? texture.Bounds;
				return texture != null;
			}
			if (_item > 0 && _item < TextureAssets.Item.Length)
			{
				Main.instance.LoadItem(_item);
				texture = TextureAssets.Item[_item].Value;
				source = (Main.itemAnimations[_item] != null) ? Main.itemAnimations[_item].GetFrame(texture) : texture.Frame();
				return true;
			}
			if (_texture != null)
			{
				texture = Main.Assets.Request<Texture2D>(_texture, AssetRequestMode.ImmediateLoad).Value;
				source = _frame ?? texture.Bounds;
				return true;
			}
		}
		catch
		{
		}
		return false;
	}
}
