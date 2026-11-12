using System;
using System.IO;
using IsaClient.Core;
using IsaClient.Menu;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Music;

/// <summary>
/// The IsaClient title music, as a real Terraria music track: it takes the place of the title theme (track 51) in the
/// game's own audio system, so the game plays it exactly like its own (from the Re-Logic splash on, in every menu, at
/// the music volume, faded out by songs, videos and the music box silence, back when they stop). The menus pick it in
/// place of the other title themes. It goes with the IsaClient logo: [F4] in the main menu puts Terraria's logo, icon
/// and title theme back. Resource packs still see (and may replace) Terraria's own track underneath.
/// </summary>
public static class TitleMusic
{
	/// <summary>The title theme's track (Journey's End title), the one replaced.</summary>
	public const int Slot = 51;

	private const string Resource = "IsaClient.Assets.TitleMusic.m4a";

	private static IAudioTrack _ours;

	private static IAudioTrack _original;

	private static bool _failed;

	/// <summary>True while our track is in the title slot: the menus play the title slot (Main.UpdateAudio).</summary>
	public static bool Active => _ours != null && Main.audioSystem is LegacyAudioSystem audio && audio.AudioTracks != null && audio.AudioTracks[Slot] == _ours;

	/// <summary>Main.UpdateAudio, every tick before the music is chosen: our track in or out, as the logo setting says.</summary>
	public static void Update()
	{
		if (_failed || !(Main.audioSystem is LegacyAudioSystem audio) || audio.AudioTracks == null || audio.AudioTracks[Slot] == null)
		{
			return;
		}
		// Only in the menus (in a world the title music box plays Terraria's theme); out once it has faded away.
		bool want = ClientLogo.Enabled && (Main.gameMenu || (Active && (_ours.IsPlaying || !_ours.IsStopped)));
		if (want == Active)
		{
			return;
		}
		try
		{
			if (want)
			{
				_ours ??= Create();
				_original = audio.AudioTracks[Slot];
				Put(audio, _ours);
			}
			else
			{
				Put(audio, _original);
				_original = null;
			}
		}
		catch (Exception ex)
		{
			_failed = true;
			ClientLog.LogException(ex);
		}
	}

	/// <summary>LegacyAudioSystem.LoadFromSources (resource packs changed): Terraria's track back first; ours returns next tick.</summary>
	public static void BeforeSources(LegacyAudioSystem audio)
	{
		if (_original != null && _ours != null && audio.AudioTracks[Slot] == _ours)
		{
			Put(audio, _original);
		}
		_original = null;
	}

	private static void Put(LegacyAudioSystem audio, IAudioTrack track)
	{
		IAudioTrack current = audio.AudioTracks[Slot];
		if (current != null && (current.IsPlaying || !current.IsStopped))
		{
			current.Stop(AudioStopOptions.Immediate);
		}
		audio.AudioTracks[Slot] = track;
	}

	/// <summary>The track, out of the client into IsaClient/Sounds (again when it changed), played by StreamedTrack.</summary>
	private static IAudioTrack Create()
	{
		using Stream stream = typeof(TitleMusic).Assembly.GetManifestResourceStream(Resource);
		string folder = Path.Combine(ClientSettings.IsaClientFolder, "Sounds");
		string path = Path.Combine(folder, "TitleMusic.m4a");
		if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
		{
			Directory.CreateDirectory(folder);
			string temp = path + ".tmp";
			using (FileStream file = File.Create(temp))
			{
				stream.CopyTo(file);
			}
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(temp, path);
		}
		return new StreamedTrack(path);
	}
}
