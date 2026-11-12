using IsaClient.Core;

namespace Terraria.ID;

public static partial class PaintID
{
	public static readonly byte Count = 32;

	public static readonly IdNameSearch Search = IdNameSearch.Create(typeof(PaintID), typeof(byte));
}
