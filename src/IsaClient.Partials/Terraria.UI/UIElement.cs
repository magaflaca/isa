namespace Terraria.UI;

public partial class UIElement
{
	/// <summary>IsaClient: which mouse events have handlers (the new panel drives old panel rows by hand).</summary>
	internal bool HasLeftClickHandler => this.OnLeftClick != null;

	internal bool HasRightClickHandler => this.OnRightClick != null;

	internal bool HasLeftDoubleClickHandler => this.OnLeftDoubleClick != null;
}
