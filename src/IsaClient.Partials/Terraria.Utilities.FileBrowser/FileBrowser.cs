namespace Terraria.Utilities.FileBrowser;

public partial class FileBrowser
{
	public static string OpenFileSavePanel(string title, string extension)
	{
		ExtensionFilter[] extensions = (string.IsNullOrEmpty(extension) ? null : new ExtensionFilter[1]
		{
			new ExtensionFilter("", extension)
		});
		return OpenFileSavePanel(title, extensions);
	}

	public static string OpenFileSavePanel(string title, ExtensionFilter[] extensions)
	{
		return _platformWrapper.OpenFileSavePanel(title, extensions);
	}
}
