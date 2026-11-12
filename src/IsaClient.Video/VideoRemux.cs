using System;
using System.IO;
using static IsaClient.Video.MediaInterop;

namespace IsaClient.Video;

/// <summary>
/// Puts a video's picture and sound into one .mp4 file with Media Foundation's sink writer, copying the streams as
/// they are (no encoding, so it takes seconds and loses nothing). YouTube serves them as two files.
/// </summary>
internal static class VideoRemux
{
	/// <summary>Writes <paramref name="output"/>; <paramref name="progress"/> gets 0..1 (by time written).</summary>
	public static void Write(MediaInput video, MediaInput audio, string output, TimeSpan duration, Action<float> progress, Func<bool> cancelled)
	{
		IMFSinkWriter writer = null;
		IMFSourceReader videoReader = null;
		IMFSourceReader audioReader = null;
		// The sink picks the container from the extension, so the temporary file ends in ".mp4" too.
		string temp = Path.ChangeExtension(output, ".part.mp4");
		Check(MFStartup(MfVersion, 0), "MFStartup");
		try
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
			Check(MFCreateAttributes(out var attributes, 1), "MFCreateAttributes");
			attributes.SetUINT32(SinkWriterDisableThrottling, 1);
			Check(MFCreateSinkWriterFromURL(temp, IntPtr.Zero, attributes, out writer), "Could not create the file");
			Release(attributes);
			int videoIndex = -1;
			int audioIndex = -1;
			if (video != null)
			{
				videoReader = OpenStream(video, FirstVideoStream, writer, out videoIndex);
			}
			if (audio != null)
			{
				audioReader = OpenStream(audio, FirstAudioStream, writer, out audioIndex);
			}
			Check(writer.BeginWriting(), "Could not start writing the file");
			long videoTime = (videoReader == null) ? long.MaxValue : 0L;
			long audioTime = (audioReader == null) ? long.MaxValue : 0L;
			while (videoTime != long.MaxValue || audioTime != long.MaxValue)
			{
				if (cancelled())
				{
					throw new OperationCanceledException();
				}
				// Always the stream that is behind, so the file comes out interleaved.
				bool takeVideo = videoTime <= audioTime;
				IMFSourceReader reader = takeVideo ? videoReader : audioReader;
				int stream = takeVideo ? FirstVideoStream : FirstAudioStream;
				int index = takeVideo ? videoIndex : audioIndex;
				Check(reader.ReadSample(stream, 0, out _, out var flags, out var timestamp, out var sample), "Could not read the video");
				if ((flags & EndOfStreamFlag) != 0)
				{
					Release(sample);
					if (takeVideo)
					{
						videoTime = long.MaxValue;
					}
					else
					{
						audioTime = long.MaxValue;
					}
					continue;
				}
				if ((flags & StreamTickFlag) != 0)
				{
					writer.SendStreamTick(index, timestamp);
				}
				if (sample != null)
				{
					Check(writer.WriteSample(index, sample), "Could not write the video");
					Release(sample);
				}
				if (takeVideo)
				{
					videoTime = Math.Max(videoTime, timestamp);
				}
				else
				{
					audioTime = Math.Max(audioTime, timestamp);
				}
				if (duration > TimeSpan.Zero)
				{
					long done = Math.Min(videoTime, audioTime);
					progress?.Invoke(Math.Min(1f, (float)((double)done / duration.Ticks)));
				}
			}
			Check(writer.DoFinalize(), "Could not finish the file");
			Release(writer);
			writer = null;
			if (File.Exists(output))
			{
				File.Delete(output);
			}
			File.Move(temp, output);
			progress?.Invoke(1f);
		}
		finally
		{
			Release(writer);
			Release(videoReader);
			Release(audioReader);
			MFShutdown();
			try
			{
				if (File.Exists(temp))
				{
					File.Delete(temp);
				}
			}
			catch
			{
			}
		}
	}

	private static IMFSourceReader OpenStream(MediaInput input, int stream, IMFSinkWriter writer, out int index)
	{
		IMFSourceReader reader = input.CreateReader(null);
		reader.SetStreamSelection(AllStreams, selected: false);
		Check(reader.SetStreamSelection(stream, selected: true), (stream == FirstVideoStream) ? "No picture to save" : "No sound to save");
		Check(reader.GetCurrentMediaType(stream, out var type), "GetCurrentMediaType");
		try
		{
			Check(writer.AddStream(type, out index), (stream == FirstVideoStream) ? "This picture format cannot go into an .mp4" : "This sound format cannot go into an .mp4");
			Check(writer.SetInputMediaType(index, type, IntPtr.Zero), "SetInputMediaType");
		}
		finally
		{
			Release(type);
		}
		return reader;
	}
}
