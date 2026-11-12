using Terraria.UI.Chat;

namespace Terraria.GameContent.UI.Chat;

public partial class ItemTagHandler
{
	/// <summary>IsaClient: a linked IsaClient item, shown as it is; clicking it opens it in My Items to keep.</summary>
	private class IsaItemSnippet : ItemSnippet
	{
		private readonly IsaClient.Items.ItemDesign _design;

		public IsaItemSnippet(Item item, IsaClient.Items.ItemDesign design)
			: base(item)
		{
			_design = design;
		}

		public override void OnClick()
		{
			IsaClient.Items.ItemShare.Open(_design);
		}
	}

	private static TextSnippet ParseClientOptions(string options)
	{
		// IsaClient: "c<code>" is a linked IsaClient item (others ignore the option); "k", "y" and "w" are a shared
		// character, style and schematic, drawn as a card.
		if (options != null)
		{
			foreach (string option in options.Split(','))
			{
				if (option.Length > 8 && (option[0] == 'k' || option[0] == 'y' || option[0] == 'w' || option[0] == IsaClient.Sharing.FileShare.Letter))
				{
					TextSnippet card = IsaClient.Sharing.ShareChat.Snippet(option);
					if (card != null)
					{
						return card;
					}
				}
				if (option.Length == 9 && option[0] == 'c')
				{
					string code = option.Substring(1);
					if (IsaClient.Items.ItemShare.TryGet(code, out IsaClient.Items.ItemDesign design))
					{
						Item isaItem = design.Make(1);
						if (!isaItem.IsAir)
						{
							return new IsaItemSnippet(isaItem, design);
						}
					}
					else
					{
						IsaClient.Items.ItemShare.Missing(code);
					}
				}
			}
		}
		return null;
	}
}
