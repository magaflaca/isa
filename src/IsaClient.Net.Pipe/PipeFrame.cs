#nullable enable
namespace IsaClient.Net.Pipe;

/// <summary>The kinds of frame the sliding-window transfer uses.</summary>
public enum FrameType : byte
{
	Open = 0x01,
	Data = 0x02,
	Ack = 0x03,
	Fin = 0x04,
	Done = 0x05,
	Abort = 0x06,
}

/// <summary>Offsets inside a frame: [type][senderTag][streamId][body...].</summary>
public static class PipeFrame
{
	public const int HeaderSize = 3;

	public const int TypeOffset = 0;

	public const int SenderOffset = 1;

	public const int StreamOffset = 2;

	public const int BodyOffset = 3;

	/// <summary>A blob larger than this is refused outright (guards against a bogus Open asking for a huge buffer).</summary>
	public const int MaxBlobBytes = 1 << 20;
}
