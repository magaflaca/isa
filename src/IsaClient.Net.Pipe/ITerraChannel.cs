#nullable enable
using System;

namespace IsaClient.Net.Pipe;

/// <summary>
/// A carrier that moves frames between two Terraria clients through whatever the server will relay (a ping, a particle,
/// ...). The transfer engine (<see cref="PipeSession"/>) only ever sees frames, so the carrier can be swapped without
/// touching the protocol. <see cref="FrameSize"/> is the biggest frame the carrier can carry at once.
/// </summary>
public interface ITerraChannel
{
	/// <summary>The largest frame, in bytes, the carrier can send in one call.</summary>
	int FrameSize { get; }

	/// <summary>A short name, for logs.</summary>
	string Name { get; }

	/// <summary>Sends one frame (an exact-length array, never larger than <see cref="FrameSize"/>).</summary>
	void SendFrame(byte[] frame);

	/// <summary>Raised when a whole frame has arrived (already checked for carrier-level corruption).</summary>
	event Action<byte[]>? OnFrame;
}
