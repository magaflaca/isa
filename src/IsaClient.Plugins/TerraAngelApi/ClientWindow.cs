using ImGuiNET;
using Microsoft.Xna.Framework.Input;

namespace TerraAngel.UI;

/// <summary>
/// An ImGui window (TerraAngel's API). Add it with <c>ClientLoader.MainRenderer.AddWindow(new MyWindow())</c>; it is drawn
/// while the plugin overlay is open (Insert), or always when <see cref="IsGlobalToggle"/> is false.
/// </summary>
public abstract class ClientWindow
{
	public virtual bool IsEnabled { get; set; }

	public virtual bool DefaultEnabled => true;

	/// <summary>Listed in the overlay's Windows menu so the player can show and hide it.</summary>
	public virtual bool IsToggleable => true;

	/// <summary>Hidden with the rest of the overlay; false keeps it on screen (a HUD) whenever it is enabled.</summary>
	public virtual bool IsGlobalToggle => true;

	public virtual string Title => GetType().Name;

	/// <summary>A key that shows and hides this window (Keys.None for none).</summary>
	public virtual Keys ToggleKey => Keys.None;

	public virtual void OnShow()
	{
	}

	public virtual void OnHide()
	{
	}

	public abstract void Draw(ImGuiIOPtr io);

	public virtual void Update()
	{
	}
}
