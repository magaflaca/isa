using Microsoft.Xna.Framework;

namespace Terraria.UI.Chat;

public partial class ChatMessageContainer
{
	/// <summary>IsaClient: drawn instead of <see cref="OriginalText"/> (a translation); null = the text as it came.</summary>
	private string _shownText;

	/// <summary>IsaClient: the chat picture settings this message was laid out with (ChatImageLayout.Version).</summary>
	private int _imageLayout;

	/// <summary>IsaClient: picture links that came through the pipe with this message (shown under it).</summary>
	private string _pipePictures;

	public bool HasPipePictures => _pipePictures != null;

	public Color Color => _color;

	/// <summary>IsaClient: pictures sent with this message through the pipe; they show under it like pictures in the chat.</summary>
	public void AddPipePictures(string links)
	{
		_pipePictures = links;
		_prepared = false;
		Refresh();
	}

	public string ShownText => _shownText ?? OriginalText;

	public bool ShowsTranslation => _shownText != null;

	/// <summary>Draws <paramref name="text"/> in place of the received text (null goes back to it); copying still gives the received one.</summary>
	public void ShowInstead(string text)
	{
		_shownText = text;
		_prepared = false;
		Refresh();
	}
}
