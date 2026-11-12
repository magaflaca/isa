namespace Terraria.Utilities.FileBrowser;

public partial interface IFileBrowser
{
	string OpenFileSavePanel(string title, ExtensionFilter[] extensions);
}
