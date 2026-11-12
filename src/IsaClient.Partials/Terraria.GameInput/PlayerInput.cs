namespace Terraria.GameInput;

public partial class PlayerInput
{
	/// <summary>The real mouse position on the screen, whatever zoom transform Main.mouseX/Y currently has (the fullscreen map uses it).</summary>
	public static int UnscaledMouseX => _originalMouseX;

	public static int UnscaledMouseY => _originalMouseY;

	private static void AddClientDefaultKeys(KeyConfiguration c)
	{
		c.KeyStatus["Open Menu"].Add("OemTilde");
		c.KeyStatus["Open Menu"].Add("F9");
		c.KeyStatus["Use Brush"].Add("Mouse1");
		c.KeyStatus["Use Brush (Map)"].Add("Mouse2");
		c.KeyStatus["Mouse TP"].Add("F");
		c.KeyStatus["Hold for Activator"].Add("LeftControl");
		c.KeyStatus["Hold for Rapidfire"].Add("LeftControl");
		c.KeyStatus["Hold for Activator (Map)"].Add("LeftShift");
		c.KeyStatus["Map TP"].Add("Mouse3");
		c.KeyStatus["Noclip"].Add("X");
		c.KeyStatus["Noclip Speed Modifier"].Add("B");
		c.KeyStatus["Undo Paste"].Add("Z");
		c.KeyStatus["Wiki Lookup"].Add("LeftShift");
	}
}
