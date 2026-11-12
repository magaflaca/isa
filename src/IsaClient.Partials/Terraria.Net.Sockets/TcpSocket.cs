using System.Net.Sockets;

namespace Terraria.Net.Sockets;

public partial class TcpSocket
{
	/// <summary>IsaClient MultiClient: the live TcpClient, so the connection can be handed to a headless session without closing it.</summary>
	public TcpClient IsaGetClient()
	{
		return _connection;
	}
}
