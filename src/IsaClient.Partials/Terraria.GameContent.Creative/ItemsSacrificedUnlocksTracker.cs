namespace Terraria.GameContent.Creative;

public partial class ItemsSacrificedUnlocksTracker
{
	/// <summary>Forces the creative UI to rebuild its item lists (used when toggling the Journey menu override).</summary>
	public void MarkDirty()
	{
		LastEditId++;
	}
}
