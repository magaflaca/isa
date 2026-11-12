using Microsoft.Xna.Framework;

namespace Terraria.Map;

public partial struct MapOverlayDrawContext
{
	/// <summary>IsaClient: where a map position (in tiles) is on the screen, for layers that draw shapes of their own.</summary>
	public Vector2 ToScreen(Vector2 position)
	{
		return (position - _mapPosition) * _mapScale + _mapOffset;
	}

	/// <summary>IsaClient: screen pixels per tile.</summary>
	public float MapScale => _mapScale;

	/// <summary>IsaClient: the minimap's area (null on the full map and the overlay).</summary>
	public Rectangle? ClippingRect => _clippingRect;

	public float Opacity => _opacity;
}
