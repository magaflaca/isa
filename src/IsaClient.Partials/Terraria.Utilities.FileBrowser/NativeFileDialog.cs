using System.IO;
using System.Linq;

namespace Terraria.Utilities.FileBrowser;

public partial class NativeFileDialog
{
	public string OpenFileSavePanel(string title, ExtensionFilter[] extensions)
	{
		string[] value = extensions.SelectMany((ExtensionFilter x) => x.Extensions).ToArray();
		string defaultPath = Path.Combine(Main.SavePath, "Schematics");
		Directory.CreateDirectory(defaultPath);
		if (nativefiledialog.NFD_SaveDialog(string.Join(",", value), defaultPath, out var outPath) == nativefiledialog.nfdresult_t.NFD_OKAY)
		{
			return outPath;
		}
		return null;
	}
}
