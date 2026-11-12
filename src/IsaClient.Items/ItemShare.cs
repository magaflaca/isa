using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IsaClient.Core;
using IsaClient.Identity;
using IsaClient.Net.Pipe;
using Terraria;

namespace IsaClient.Items;

/// <summary>
/// IsaClient items linked in chat. The chat line carries an ordinary item tag with one extra option, a code
/// ("[i/c1a2b3c4d:757]"): everyone else sees the plain item, and the item itself goes to IsaClient players through the
/// pipe, so they see it as it is (its name, tag, stats, text and who made it), can hover it and click it to keep it.
/// Someone who misses it asks for it by its code and whoever has it answers.
/// </summary>
public static class ItemShare
{
	public const byte Channel = 75;

	private const byte Offer = 1;

	private const byte Ask = 2;

	public sealed class Received
	{
		public ItemDesign Design;

		public string From;

		public DateTime When;
	}

	/// <summary>Designs by their code: yours linked in chat and the ones that arrived.</summary>
	private static readonly Dictionary<string, ItemDesign> _known = new Dictionary<string, ItemDesign>();

	private static readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>();

	private static readonly Dictionary<string, uint> _asked = new Dictionary<string, uint>();

	/// <summary>What others linked this session (newest first), for My Items.</summary>
	public static readonly List<Received> Inbox = new List<Received>();

	public static int Version { get; private set; }

	private static readonly Regex _codes = new Regex("\\[i/[^:\\]]*?c([0-9a-f]{8})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static bool _hooked;

	public static string CodeOf(byte[] bytes)
	{
		uint h = 2166136261u;
		foreach (byte b in bytes)
		{
			h = (h ^ b) * 16777619u;
		}
		return h.ToString("x8");
	}

	/// <summary>Registers an item to be linked: its code (it goes out when the line is sent).</summary>
	public static string Register(Item item)
	{
		ItemDesign d = ItemDesign.Capture(item);
		d.Stack = 1;
		return Register(d);
	}

	public static string Register(ItemDesign d)
	{
		byte[] bytes = d.ToBytes();
		string code = CodeOf(bytes);
		_bytes[code] = bytes;
		_known[code] = d;
		return code;
	}

	/// <summary>The chat tag of an IsaClient item.</summary>
	public static string Tag(ItemDesign d)
	{
		string code = Register(d);
		string text = "[i/c" + code;
		if (d.Prefix != 0)
		{
			text += ",p" + d.Prefix;
		}
		int net = (d.Mark != null && d.Mark.NetId > 0) ? d.Mark.NetId : d.Type;
		return text + ":" + IsaClient.Gameplay.LegacyItems.NetId(net) + "]";
	}

	public static bool TryGet(string code, out ItemDesign design)
	{
		return _known.TryGetValue(code.ToLowerInvariant(), out design);
	}

	/// <summary>A chat line going out: the items linked in it go to IsaClient players.</summary>
	public static void OnSend(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("[i/", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		foreach (Match m in _codes.Matches(text))
		{
			SendOffer(m.Groups[1].Value.ToLowerInvariant());
		}
	}

	private static void SendOffer(string code)
	{
		if (!PipeNet.CanSend || !_bytes.TryGetValue(code, out byte[] bytes))
		{
			return;
		}
		byte[] data = new byte[5 + bytes.Length];
		data[0] = Offer;
		BitConverter.GetBytes(Convert.ToUInt32(code, 16)).CopyTo(data, 1);
		bytes.CopyTo(data, 5);
		PipeNet.Broadcast(Channel, data);
	}

	/// <summary>A chat tag with a code we don't have: ask the others (once every few seconds).</summary>
	public static void Missing(string code)
	{
		code = code.ToLowerInvariant();
		uint now = Main.GameUpdateCount;
		if (_asked.TryGetValue(code, out uint at) && now - at < 600)
		{
			return;
		}
		_asked[code] = now;
		if (!PipeNet.CanSend)
		{
			return;
		}
		byte[] data = new byte[5];
		data[0] = Ask;
		BitConverter.GetBytes(Convert.ToUInt32(code, 16)).CopyTo(data, 1);
		PipeNet.Broadcast(Channel, data);
	}

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 5)
		{
			return;
		}
		try
		{
			string code = BitConverter.ToUInt32(m.Data, 1).ToString("x8");
			if (m.Data[0] == Ask)
			{
				if (_bytes.ContainsKey(code))
				{
					SendOffer(code);
				}
				return;
			}
			if (m.Data[0] != Offer || _known.ContainsKey(code))
			{
				return;
			}
			byte[] bytes = new byte[m.Data.Length - 5];
			Array.Copy(m.Data, 5, bytes, 0, bytes.Length);
			if (CodeOf(bytes) != code)
			{
				return;
			}
			ItemDesign d = ItemDesign.FromBytes(bytes);
			if (d == null)
			{
				return;
			}
			_known[code] = d;
			_bytes[code] = bytes;
			string from = (m.FromPlayer >= 0 && m.FromPlayer < Main.maxPlayers) ? Main.player[m.FromPlayer].name : "?";
			// The maker's own client vouches for the maker's current name.
			if (d.Mark.CreatorId != 0 && ClientIdentity.Of(m.FromPlayer) == d.Mark.CreatorId)
			{
				CreatorNames.Seen(d.Mark.CreatorId, from);
			}
			Inbox.RemoveAll(r => r.Design.Mark.Id == d.Mark.Id);
			Inbox.Insert(0, new Received { Design = d, From = from, When = DateTime.Now });
			if (Inbox.Count > 40)
			{
				Inbox.RemoveAt(Inbox.Count - 1);
			}
			Version++;
			// Lines already showing the plain item are drawn again with this one.
			IsaClient.ChatImages.ChatImageLayout.Relayout();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Shows a linked item in the panel (My Items), ready to keep.</summary>
	public static void Open(ItemDesign d)
	{
		IsaClient.Hub.HubMyItems.Show(d);
	}

	/// <summary>Puts the tag of an item into the chat line (opening the chat).</summary>
	public static void PutInChat(ItemDesign d)
	{
		string tag = Tag(d);
		if (!Main.drawingPlayerChat)
		{
			Main.OpenPlayerChat();
		}
		Terraria.UI.Chat.ChatManager.AddChatText(Terraria.GameContent.FontAssets.MouseText.Value, tag, Microsoft.Xna.Framework.Vector2.One);
	}

	/// <summary>.shareitem: the item in your hand, linked in the chat line.</summary>
	public static void ShareCommand(CommandContext command)
	{
		Item item = Main.LocalPlayer?.HeldItem;
		if (item == null || item.IsAir)
		{
			ClientChat.Error("Hold the item you want to link.");
			return;
		}
		ItemDesign d = ItemDesign.Capture(item);
		if (d.Mark == null)
		{
			d.Mark = ItemMark.New();
		}
		PutInChat(d);
	}
}
