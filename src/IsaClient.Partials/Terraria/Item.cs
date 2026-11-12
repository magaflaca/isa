namespace Terraria;

public partial class Item
{
	/// <summary>IsaClient: the custom name set with SetNameOverride (null when none).</summary>
	public string NameOverride => _nameOverride;

	/// <summary>IsaClient Held Item: the shoot style (IsaShootBehaviors), how many times one use shoots, and hitting invincible NPCs.</summary>
	public int itemShootBehavior;

	public ushort timesToShoot = 1;

	public bool hitsInvincibleNpc;

	/// <summary>The server changed this item's stats (packet 88) / an item mod preset did.</summary>
	public bool modifiedByServer;

	public bool modifiedByItemMods;

	public string itemModPresetName;

	/// <summary>IsaClient item: maker, tag, text, network id (see IsaClient.Items.ItemMark). Null for plain items.</summary>
	public IsaClient.Items.ItemMark isaMark;
}
