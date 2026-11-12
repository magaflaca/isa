using Terraria.UI.Chat;

namespace Terraria.GameContent.UI.Chat;

public partial class RemadeChatMonitor
{
	private static int _drawThread;

	/// <summary>IsaClient: the newest message (the one NewText just added).</summary>
	public ChatMessageContainer Newest => (_messages.Count > 0) ? _messages[0] : null;

	/// <summary>IsaClient: takes one message out (the .ask status line, which moves and goes away).</summary>
	public void Remove(ChatMessageContainer message)
	{
		_messages.Remove(message);
	}
}
