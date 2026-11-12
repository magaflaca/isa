using System;
using Microsoft.Xna.Framework.Audio;
using Terraria.Audio;

namespace IsaClient.Music;

/// <summary>
/// A music track for Terraria's audio system played by the Music module's own player (Media Foundation decoding,
/// waveOut on its own thread). The game drives it like its own tracks (play, fades, pause when unfocused, stop), but
/// the sound doesn't depend on the game's frames, so hitches don't break it up, and the client's sound-effect fades
/// (SoundEffect.MasterVolume) don't touch it, like Terraria's own music. Loops at the end.
/// </summary>
public sealed class StreamedTrack : IAudioTrack
{
	private enum State
	{
		Stopped,
		Playing,
		Paused
	}

	private readonly string _path;

	private AudioPlayer _player;

	private State _state;

	private float _volume;

	private volatile bool _ended;

	private bool _failed;

	public StreamedTrack(string path)
	{
		_path = path;
	}

	public bool IsPlaying => _state == State.Playing;

	public bool IsStopped => _state == State.Stopped;

	public bool IsPaused => _state == State.Paused;

	public void Play()
	{
		_state = State.Playing;
		if (_player == null)
		{
			Open();
		}
		else
		{
			_player.Resume();
		}
	}

	public void Stop(AudioStopOptions options)
	{
		_state = State.Stopped;
		Close();
	}

	public void Pause()
	{
		if (_state == State.Playing)
		{
			_state = State.Paused;
			_player?.Pause();
		}
	}

	public void Resume()
	{
		if (_state == State.Paused)
		{
			_state = State.Playing;
			_player?.Resume();
		}
	}

	/// <summary>From the start next time it plays.</summary>
	public void Reuse()
	{
		_player?.Seek(TimeSpan.Zero);
	}

	public void SetVariable(string variableName, float value)
	{
		if (variableName != "Volume")
		{
			return;
		}
		// The same loudness Terraria gives resource-pack tracks for a volume (matched to its own music). The player
		// squares its volume, so the root of that amplitude.
		_volume = (float)Math.Sqrt(Math.Pow(10.0, (31.0 * value - 25.0 - 11.94) / 20.0));
		if (value <= 0f)
		{
			_volume = 0f;
		}
		if (_player != null)
		{
			_player.Volume = _volume;
		}
	}

	public void Update()
	{
		if (_ended)
		{
			// Reached its end: again from the start (looping).
			_ended = false;
			Close();
			if (_state == State.Playing)
			{
				Open();
			}
		}
	}

	private void Open()
	{
		if (_failed)
		{
			return;
		}
		AudioPlayer player = new AudioPlayer(_path, _volume);
		player.Finished += () => _ended = true;
		player.Failed += error =>
		{
			_failed = true;
			IsaClient.Core.ClientLog.LogException(new Exception("Title music: " + error));
		};
		_player = player;
		player.Start();
	}

	private void Close()
	{
		AudioPlayer player = _player;
		_player = null;
		player?.Stop();
	}

	public void Dispose()
	{
		_state = State.Stopped;
		Close();
	}
}
