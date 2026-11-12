using System;

namespace IsaClient.Music;

/// <summary>Samples made by the client for <see cref="AudioPlayer"/> (instead of a file Media Foundation decodes).</summary>
public interface IPcmSource : IDisposable
{
	int Channels { get; }

	int SampleRate { get; }

	/// <summary>Up to <paramref name="count"/> interleaved 16-bit samples (a multiple of the channels); 0 at the end.</summary>
	int Read(short[] buffer, int count);
}
