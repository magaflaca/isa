using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using IsaClient.Core;
using IsaClient.Gba;

namespace IsaClient.Psp;

/// <summary>
/// The game's end of the emulator's process (<see cref="PspHost"/>): starts it, sends it the buttons and commands, takes
/// its pictures, and plays its sound on a thread of its own.
/// </summary>
internal sealed class PspRemote
{
	private readonly PspLink _link;

	private readonly Process _process;

	private readonly Queue<(int kind, string path, Action<bool> done)> _waiting = new Queue<(int, string, Action<bool>)>();

	private Action<bool> _pendingDone;

	private int _pendingSerial;

	private int _serial;

	private volatile bool _closing;

	private GbaAudio _audio;

	private Thread _sound;

	private volatile int _volume;

	/// <summary>Why it stopped by itself (null while it's fine, or when it was asked to).</summary>
	public string Error { get; private set; }

	public bool Running => !_closing && (_link.State == PspLink.Starting || _link.State == PspLink.Running) && !_process.HasExited;

	/// <summary>The game is in the emulator (its rates are known).</summary>
	public bool Loaded => _link.State == PspLink.Running;

	public int FrameSerial => _link.FrameSerial;

	public long FrameCount => _link.FrameCount;

	public double Fps => Math.Max(1.0, _link.FpsMilli / 1000.0);

	public bool OnGraphicsCard => _link.OnGraphicsCard;

	public bool Paused { get => _link.Paused; set => _link.Paused = value; }

	public int Speed { get => _link.Speed; set => _link.Speed = value; }

	private PspRemote(PspLink link, Process process)
	{
		_link = link;
		_process = process;
	}

	/// <summary>Starts the emulator's process on a game. <paramref name="statePath"/> is the moment to start from (null: the start).</summary>
	public static PspRemote Start(string game, string statePath, string logPath, int resolution, string language, int volume)
	{
		int pid = Process.GetCurrentProcess().Id;
		string name = "IsaClientPsp-" + pid + "-" + Environment.TickCount;
		PspLink link = PspLink.Create(name);
		link.Config = new[] { PspEmulator.CorePath, PspEmulator.SystemFolder, PspGame.SaveFolder, game, statePath ?? "", logPath, language };
		link.Resolution = resolution;
		link.Speed = 1;
		link.QueuedMs = -1000;
		Process process = new Process
		{
			StartInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, PspHost.Argument + " " + name + " " + pid)
			{
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};
		process.Start();
		PspRemote remote = new PspRemote(link, process) { _volume = volume };
		return remote;
	}

	public void Input(int buttons, int analogX, int analogY)
	{
		_link.Buttons = buttons;
		_link.AnalogX = analogX;
		_link.AnalogY = analogY;
	}

	public bool TakeFrame(ref uint[] into, ref int serial, out int width, out int height) => _link.TakeFrame(ref into, ref serial, out width, out height);

	public void SetResolution(int times) => _link.Resolution = times;

	public void SetVolume(int volume)
	{
		_volume = volume;
		if (_audio != null)
		{
			_audio.Volume = volume / 100f;
		}
	}

	public void SaveTo(string path, Action<bool> done) => _waiting.Enqueue((PspLink.CommandSave, path, done));

	public void LoadFrom(string path, Action<bool> done) => _waiting.Enqueue((PspLink.CommandLoad, path, done));

	public void Reset() => _waiting.Enqueue((PspLink.CommandReset, "", null));

	private void Send(int kind, string path)
	{
		_link.CommandPath = path;
		_link.CommandKind = kind;
		_pendingSerial = ++_serial;
		_link.CommandSerial = _pendingSerial;
	}

	/// <summary>Every frame on the main thread: the sound once the game is in, and the commands, one at a time.</summary>
	public void Update()
	{
		if (_sound == null && Loaded)
		{
			StartSound();
		}
		if (_pendingSerial != 0)
		{
			if (_link.DoneSerial != _pendingSerial)
			{
				return;
			}
			_pendingSerial = 0;
			Action<bool> done = _pendingDone;
			_pendingDone = null;
			done?.Invoke(_link.DoneOk);
		}
		if (_waiting.Count > 0)
		{
			(int kind, string path, Action<bool> done) next = _waiting.Dequeue();
			_pendingDone = next.done;
			Send(next.kind, next.path);
		}
	}

	private void StartSound()
	{
		try
		{
			_audio = new GbaAudio(Math.Max(8000, _link.SampleRate), Fps) { Volume = _volume / 100f };
		}
		catch (Exception ex)
		{
			// no sound device: play without sound
			ClientLog.LogException(ex);
			_audio = null;
		}
		_sound = new Thread(PlaySound) { IsBackground = true, Name = "IsaClient PSP sound", Priority = ThreadPriority.AboveNormal };
		_sound.Start();
	}

	/// <summary>A frame's worth at a time: <see cref="GbaAudio"/> counts its buffers as frames.</summary>
	private void PlaySound()
	{
		int chunk = Math.Max(256, (int)(_link.SampleRate / Fps));
		short[] samples = new short[chunk * 2];
		while (!_closing)
		{
			GbaAudio audio = _audio;
			if (audio == null)
			{
				// nowhere to play it: the ring is emptied so the emulator goes on by its clock
				_link.TakeAudio(samples, chunk);
				Thread.Sleep(10);
				continue;
			}
			_link.QueuedMs = (int)(audio.Queued * 1000.0);
			int got = _link.TakeAudio(samples, chunk);
			if (got > 0)
			{
				audio.Submit(samples, got);
			}
			else
			{
				Thread.Sleep(2);
			}
		}
	}

	/// <summary>The process ended by itself: what it said, or that it crashed.</summary>
	public string Why()
	{
		string said = _link.Error;
		return (said.Length > 0) ? said : "The emulator closed unexpectedly. What it said is in IsaClient/PSP/emulator-log.txt.";
	}

	/// <summary>Turns it off, keeping the moment in <paramref name="statePath"/> when given. Waits for it: not for the main thread, except closing.</summary>
	public void Stop(string statePath)
	{
		_closing = true;
		_sound?.Join(3000);
		_audio?.Dispose();
		_audio = null;
		try
		{
			if (!_process.HasExited)
			{
				// a command still running (a save slot) finishes first
				for (int i = 0; i < 500 && _pendingSerial != 0 && _link.DoneSerial != _pendingSerial && !_process.HasExited; i++)
				{
					Thread.Sleep(10);
				}
				_link.Paused = false;
				Send(PspLink.CommandStop, statePath ?? "");
				if (!_process.WaitForExit(15000))
				{
					_process.Kill();
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		_process.Dispose();
		_link.Dispose();
	}
}
