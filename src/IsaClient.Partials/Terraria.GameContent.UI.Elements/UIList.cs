using System.Collections.Generic;
using Terraria.UI;

namespace Terraria.GameContent.UI.Elements;

public partial class UIList
{
	/// <summary>IsaClient: adds many items with one sort and one layout pass (Add does both per item).</summary>
	public virtual void AddRange(IEnumerable<UIElement> items)
	{
		foreach (UIElement item in items)
		{
			_items.Add(item);
			_innerList.Append(item);
		}
		UpdateOrder();
		_innerList.Recalculate();
	}
}
