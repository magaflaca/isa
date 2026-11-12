using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IsaClient.Networking;

/// <summary>
/// Opens a tunnel through a proxy (SOCKS5, SOCKS4 or HTTP CONNECT) and runs the real test: a tunnel to a TCP port like
/// Terraria's that sends Terraria's binary hello and must get an answer back. Many free proxies answer the greeting but
/// never carry data, or only carry HTTP: only this test tells them apart.
/// </summary>
internal static class ProxyTunnel
{
	/// <summary>Test server: portquiz.net listens on every port and answers anything ending in a blank line.</summary>
	private const string TestHost = "portquiz.net";

	private const int TestPort = 7777;

	private static IPAddress _testAddress;

	private static DateTime _resolvedAt;

	/// <summary>Terraria's hello ("Terraria279"), then a blank line so the web server answers.</summary>
	private static readonly byte[] Hello = BuildHello();

	private static byte[] BuildHello()
	{
		byte[] text = Encoding.ASCII.GetBytes("Terraria279");
		byte[] packet = new byte[4 + text.Length + 4];
		packet[0] = (byte)(4 + text.Length);
		packet[2] = 1;
		packet[3] = (byte)text.Length;
		Buffer.BlockCopy(text, 0, packet, 4, text.Length);
		Buffer.BlockCopy(new byte[] { 13, 10, 13, 10 }, 0, packet, 4 + text.Length, 4);
		return packet;
	}

	public static IPAddress TestAddress
	{
		get
		{
			if (_testAddress == null || (DateTime.Now - _resolvedAt).TotalMinutes > 30)
			{
				try
				{
					foreach (IPAddress address in Dns.GetHostAddresses(TestHost))
					{
						if (address.AddressFamily == AddressFamily.InterNetwork)
						{
							_testAddress = address;
							_resolvedAt = DateTime.Now;
							break;
						}
					}
				}
				catch
				{
				}
			}
			return _testAddress;
		}
	}

	/// <summary>A TCP connection to the proxy itself, or an exception after <paramref name="timeoutMs"/>.</summary>
	public static TcpClient ConnectTo(ProxyServer proxy, int timeoutMs)
	{
		TcpClient client = new TcpClient { NoDelay = true };
		try
		{
			IAsyncResult result = client.BeginConnect(proxy.Host, proxy.Port, null, null);
			if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
			{
				throw new IOException("the proxy did not answer");
			}
			client.EndConnect(result);
			return client;
		}
		catch
		{
			client.Close();
			throw;
		}
	}

	/// <summary>Asks the proxy to connect to <paramref name="target"/>; afterwards the stream talks to the target.</summary>
	public static void Open(NetworkStream stream, ProxyServer proxy, IPAddress target, int port, int timeoutMs)
	{
		stream.ReadTimeout = timeoutMs;
		stream.WriteTimeout = timeoutMs;
		switch (proxy.Protocol)
		{
		case ProxyProtocol.Socks4:
			Socks4(stream, proxy, target, port);
			break;
		case ProxyProtocol.Http:
			HttpConnect(stream, proxy, target, port);
			break;
		default:
			Socks5(stream, proxy, target, port);
			break;
		}
		stream.ReadTimeout = -1;
		stream.WriteTimeout = -1;
	}

	/// <summary>The real test; returns the round trip through the proxy in ms, or -1 when it does not carry Terraria traffic.</summary>
	public static int Test(ProxyServer proxy, int timeoutMs = 5000)
	{
		IPAddress target = TestAddress;
		if (target == null)
		{
			return -1;
		}
		try
		{
			using TcpClient client = ConnectTo(proxy, Math.Min(timeoutMs, 4000));
			NetworkStream stream = client.GetStream();
			Open(stream, proxy, target, TestPort, timeoutMs);
			stream.ReadTimeout = timeoutMs;
			Stopwatch watch = Stopwatch.StartNew();
			stream.Write(Hello, 0, Hello.Length);
			byte[] buffer = new byte[64];
			int read = 0;
			while (read < 12)
			{
				int n = stream.Read(buffer, read, buffer.Length - read);
				if (n <= 0)
				{
					break;
				}
				read += n;
			}
			int ms = (int)watch.ElapsedMilliseconds;
			return (read >= 5 && Encoding.ASCII.GetString(buffer, 0, Math.Min(read, 16)).Contains("HTTP/")) ? Math.Max(1, ms) : -1;
		}
		catch
		{
			return -1;
		}
	}

	/// <summary>Can this PC reach the test server at all (without a proxy)? If not, tests would fail every proxy.</summary>
	public static bool TestServerReachable()
	{
		IPAddress target = TestAddress;
		if (target == null)
		{
			return false;
		}
		try
		{
			using TcpClient client = new TcpClient();
			IAsyncResult result = client.BeginConnect(target, TestPort, null, null);
			if (!result.AsyncWaitHandle.WaitOne(5000))
			{
				return false;
			}
			client.EndConnect(result);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void Socks5(NetworkStream stream, ProxyServer proxy, IPAddress target, int port)
	{
		bool auth = !string.IsNullOrEmpty(proxy.Username);
		if (auth)
		{
			stream.Write(new byte[] { 5, 2, 0, 2 }, 0, 4);
		}
		else
		{
			stream.Write(new byte[] { 5, 1, 0 }, 0, 3);
		}
		byte[] reply = ReadExact(stream, 2);
		if (reply[0] != 5)
		{
			throw new IOException("not a SOCKS5 proxy");
		}
		if (reply[1] == 2)
		{
			byte[] user = Encoding.UTF8.GetBytes(proxy.Username ?? "");
			byte[] pass = Encoding.UTF8.GetBytes(proxy.Password ?? "");
			byte[] login = new byte[3 + user.Length + pass.Length];
			login[0] = 1;
			login[1] = (byte)user.Length;
			Buffer.BlockCopy(user, 0, login, 2, user.Length);
			login[2 + user.Length] = (byte)pass.Length;
			Buffer.BlockCopy(pass, 0, login, 3 + user.Length, pass.Length);
			stream.Write(login, 0, login.Length);
			if (ReadExact(stream, 2)[1] != 0)
			{
				throw new IOException("the proxy rejected the username or password");
			}
		}
		else if (reply[1] != 0)
		{
			throw new IOException("the proxy wants a login it does not support");
		}
		byte[] address = target.GetAddressBytes();
		byte[] request = new byte[6 + address.Length];
		request[0] = 5;
		request[1] = 1;
		request[3] = (byte)((target.AddressFamily == AddressFamily.InterNetworkV6) ? 4 : 1);
		Buffer.BlockCopy(address, 0, request, 4, address.Length);
		request[4 + address.Length] = (byte)(port >> 8);
		request[5 + address.Length] = (byte)(port & 0xFF);
		stream.Write(request, 0, request.Length);
		byte[] response = ReadExact(stream, 4);
		if (response[1] != 0)
		{
			throw new IOException("the proxy could not reach the server (SOCKS5 code " + response[1] + ")");
		}
		int skip = response[3] switch
		{
			1 => 4,
			4 => 16,
			3 => ReadExact(stream, 1)[0],
			_ => 0
		};
		ReadExact(stream, skip + 2);
	}

	private static void Socks4(NetworkStream stream, ProxyServer proxy, IPAddress target, int port)
	{
		if (target.AddressFamily != AddressFamily.InterNetwork)
		{
			throw new IOException("SOCKS4 only reaches IPv4 servers");
		}
		byte[] user = Encoding.ASCII.GetBytes(proxy.Username ?? "");
		byte[] request = new byte[9 + user.Length];
		request[0] = 4;
		request[1] = 1;
		request[2] = (byte)(port >> 8);
		request[3] = (byte)(port & 0xFF);
		Buffer.BlockCopy(target.GetAddressBytes(), 0, request, 4, 4);
		Buffer.BlockCopy(user, 0, request, 8, user.Length);
		stream.Write(request, 0, request.Length);
		byte[] response = ReadExact(stream, 8);
		if (response[1] != 0x5A)
		{
			throw new IOException("the proxy could not reach the server (SOCKS4 code " + response[1] + ")");
		}
	}

	private static void HttpConnect(NetworkStream stream, ProxyServer proxy, IPAddress target, int port)
	{
		string host = ((target.AddressFamily == AddressFamily.InterNetworkV6) ? ("[" + target + "]") : target.ToString()) + ":" + port;
		StringBuilder request = new StringBuilder("CONNECT " + host + " HTTP/1.1\r\nHost: " + host + "\r\n");
		if (!string.IsNullOrEmpty(proxy.Username))
		{
			request.Append("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(proxy.Username + ":" + proxy.Password)) + "\r\n");
		}
		request.Append("\r\n");
		byte[] bytes = Encoding.ASCII.GetBytes(request.ToString());
		stream.Write(bytes, 0, bytes.Length);
		// Read the answer byte by byte up to the blank line, so no byte of the game's data is eaten.
		StringBuilder head = new StringBuilder();
		while (head.Length < 8192)
		{
			int b = stream.ReadByte();
			if (b < 0)
			{
				throw new IOException("the proxy closed the connection");
			}
			head.Append((char)b);
			if (head.Length >= 4 && head[head.Length - 1] == '\n' && head.ToString(head.Length - 4, 4) == "\r\n\r\n")
			{
				break;
			}
		}
		string status = head.ToString();
		if (!status.StartsWith("HTTP/1.") || status.Length < 12 || status.Substring(9, 3) != "200")
		{
			int end = status.IndexOf('\r');
			throw new IOException("the proxy refused: " + ((end > 0) ? status.Substring(0, end) : "no answer"));
		}
	}

	private static byte[] ReadExact(NetworkStream stream, int count)
	{
		byte[] buffer = new byte[count];
		int read = 0;
		while (read < count)
		{
			int n = stream.Read(buffer, read, count - read);
			if (n <= 0)
			{
				throw new IOException("the proxy closed the connection");
			}
			read += n;
		}
		return buffer;
	}
}
