using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Music;

/// <summary>
/// The music library behind the music player: every song ever played (title, duration, file, plays), favorites and
/// playlists. Songs are saved in Music/Songs with their real title as file name ("Artist - Song.m4a"); the YouTube id
/// and link are kept in library.json and also in the file itself, in an NTFS stream ("file.m4a:IsaClient.Source"),
/// so a renamed or moved song can still be recognised. Favorites and playlists live in playlists.json.
/// </summary>
public static class MusicLibrary
{
	public sealed class Song
	{
		/// <summary>YouTube id, or "file:" + full path for local files.</summary>
		[JsonIgnore]
		public string Key;

		public string Title;

		public double Duration;

		public string File;

		public string Url;

		public DateTime Added;

		public int Plays;

		public DateTime LastPlayed;

		[JsonIgnore]
		public bool Downloaded => !string.IsNullOrEmpty(File) && System.IO.File.Exists(File);

		[JsonIgnore]
		public string Display => string.IsNullOrEmpty(Title) ? Key : Title;

		[JsonIgnore]
		public string Source
		{
			get
			{
				if (Key != null && Key.StartsWith("file:"))
				{
					return Key.Substring(5);
				}
				return string.IsNullOrEmpty(Url) ? ("https://www.youtube.com/watch?v=" + Key) : Url;
			}
		}
	}

	public sealed class Playlist
	{
		public string Name;

		public List<string> Songs = new List<string>();

		public DateTime Created;
	}

	private sealed class PlaylistFile
	{
		public List<string> Favorites = new List<string>();

		public List<Playlist> Playlists = new List<Playlist>();
	}

	private static readonly object _sync = new object();

	private static Dictionary<string, Song> _songs;

	private static PlaylistFile _lists;

	private static int[] _iconIndexes;

	/// <summary>Bumped on every change: the UI rebuilds its sorted lists only when this moves.</summary>
	public static int Version { get; private set; }

	private static string LibraryPath => Path.Combine(MusicPlayer.MusicFolder, "library.json");

	private static string PlaylistsPath => Path.Combine(MusicPlayer.MusicFolder, "playlists.json");

	#region Load / save

	private static void EnsureLoaded()
	{
		if (_songs != null)
		{
			return;
		}
		lock (_sync)
		{
			if (_songs != null)
			{
				return;
			}
			Dictionary<string, Song> songs = new Dictionary<string, Song>();
			PlaylistFile lists = new PlaylistFile();
			try
			{
				Directory.CreateDirectory(MusicPlayer.MusicFolder);
				if (System.IO.File.Exists(LibraryPath))
				{
					songs = JsonConvert.DeserializeObject<Dictionary<string, Song>>(System.IO.File.ReadAllText(LibraryPath)) ?? songs;
				}
				if (System.IO.File.Exists(PlaylistsPath))
				{
					lists = JsonConvert.DeserializeObject<PlaylistFile>(System.IO.File.ReadAllText(PlaylistsPath)) ?? lists;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			foreach (KeyValuePair<string, Song> pair in songs)
			{
				pair.Value.Key = pair.Key;
				if (pair.Value.Added == default(DateTime))
				{
					pair.Value.Added = DateTime.UtcNow;
				}
			}
			lists.Favorites = lists.Favorites ?? new List<string>();
			lists.Playlists = lists.Playlists ?? new List<Playlist>();
			_songs = songs;
			_lists = lists;
			Version++;
		}
		// Songs downloaded before titles were used as file names ("dQw4w9WgXcQ.m4a") get their real name now.
		System.Threading.Tasks.Task.Run(delegate
		{
			MigrateFileNames();
			ImportLooseFiles();
		});
	}

	public static void Save()
	{
		try
		{
			lock (_sync)
			{
				if (_songs == null)
				{
					return;
				}
				Directory.CreateDirectory(MusicPlayer.MusicFolder);
				System.IO.File.WriteAllText(LibraryPath, JsonConvert.SerializeObject(_songs, Formatting.Indented));
				System.IO.File.WriteAllText(PlaylistsPath, JsonConvert.SerializeObject(_lists, Formatting.Indented));
				Version++;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void MigrateFileNames()
	{
		bool changed = false;
		List<Song> songs;
		lock (_sync)
		{
			songs = _songs.Values.ToList();
		}
		foreach (Song song in songs)
		{
			try
			{
				if (!song.Downloaded || song.Key.StartsWith("file:") || string.IsNullOrEmpty(song.Title))
				{
					continue;
				}
				if (Path.GetFileNameWithoutExtension(song.File) == song.Key)
				{
					string renamed = MoveToTitle(song.File, song.Title);
					lock (_sync)
					{
						song.File = renamed;
					}
					WriteSourceTag(renamed, song.Key, song.Source);
					changed = true;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		if (changed)
		{
			Save();
		}
	}

	/// <summary>Audio files someone put in Songs/ by hand: added as local songs (their NTFS tag, when present, gives the YouTube id).</summary>
	private static void ImportLooseFiles()
	{
		try
		{
			if (!Directory.Exists(MusicPlayer.SongsFolder))
			{
				return;
			}
			HashSet<string> known;
			lock (_sync)
			{
				known = new HashSet<string>(_songs.Values.Where((Song s) => s.File != null).Select((Song s) => Path.GetFullPath(s.File)), StringComparer.OrdinalIgnoreCase);
			}
			bool changed = false;
			foreach (string file in Directory.GetFiles(MusicPlayer.SongsFolder))
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext == ".part" || ext == ".ytdl" || ext == ".json" || ext == ".tmp" || known.Contains(Path.GetFullPath(file)))
				{
					continue;
				}
				if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(file), "^[A-Za-z0-9_-]{11}$") || !System.IO.File.Exists(file))
				{
					continue;
				}
				string tagged = ReadSourceTag(file);
				string key = ((tagged != null && tagged.Length == 11) ? tagged : ("file:" + Path.GetFullPath(file)));
				lock (_sync)
				{
					if (_songs.TryGetValue(key, out var existing))
					{
						if (!existing.Downloaded)
						{
							existing.File = file;
							changed = true;
						}
						continue;
					}
					_songs[key] = new Song
					{
						Key = key,
						Title = Path.GetFileNameWithoutExtension(file),
						File = file,
						Added = System.IO.File.GetCreationTimeUtc(file)
					};
				}
				changed = true;
			}
			if (changed)
			{
				Save();
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region Songs

	public static Song Get(string key)
	{
		EnsureLoaded();
		if (key == null)
		{
			return null;
		}
		lock (_sync)
		{
			Song song;
			return _songs.TryGetValue(key, out song) ? song : null;
		}
	}

	public static List<Song> All()
	{
		EnsureLoaded();
		lock (_sync)
		{
			return _songs.Values.ToList();
		}
	}

	public static int Count
	{
		get
		{
			EnsureLoaded();
			lock (_sync)
			{
				return _songs.Count;
			}
		}
	}

	public static bool TryGetFile(string key, out string file)
	{
		file = null;
		Song song = Get(key);
		if (song != null && song.Downloaded)
		{
			file = song.File;
			return true;
		}
		return false;
	}

	/// <summary>
	/// A song was downloaded (or a search result is being saved for later): records it, gives the file its real title
	/// as name and tags it with its source. Returns the file's final path.
	/// </summary>
	public static string Register(string key, string title, double duration, string file, string url = null)
	{
		EnsureLoaded();
		if (string.IsNullOrEmpty(key))
		{
			return file;
		}
		string finalPath = file;
		try
		{
			if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file) && !key.StartsWith("file:") && !string.IsNullOrEmpty(title))
			{
				finalPath = MoveToTitle(file, title);
				WriteSourceTag(finalPath, key, string.IsNullOrEmpty(url) ? ("https://www.youtube.com/watch?v=" + key) : url);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			finalPath = file;
		}
		lock (_sync)
		{
			if (!string.IsNullOrEmpty(file) && !string.Equals(file, finalPath, StringComparison.OrdinalIgnoreCase))
			{
				foreach (string stale in _songs.Where((KeyValuePair<string, Song> p) => p.Key != key && p.Value.File != null && string.Equals(p.Value.File, file, StringComparison.OrdinalIgnoreCase)).Select((KeyValuePair<string, Song> p) => p.Key).ToList())
				{
					_songs.Remove(stale);
				}
			}
			if (!_songs.TryGetValue(key, out var song))
			{
				song = new Song
				{
					Key = key,
					Added = DateTime.UtcNow
				};
				_songs[key] = song;
			}
			if (!string.IsNullOrEmpty(title))
			{
				song.Title = title;
			}
			if (duration > 0.0)
			{
				song.Duration = duration;
			}
			if (!string.IsNullOrEmpty(finalPath))
			{
				song.File = finalPath;
			}
			if (!string.IsNullOrEmpty(url))
			{
				song.Url = url;
			}
		}
		Save();
		return finalPath;
	}

	public static void NotePlayed(string key)
	{
		Song song = Get(key);
		if (song == null)
		{
			return;
		}
		lock (_sync)
		{
			song.Plays++;
			song.LastPlayed = DateTime.UtcNow;
		}
		Save();
	}

	/// <summary>Removes a song from the library, every list, and deletes its file.</summary>
	public static void Delete(string key)
	{
		Song song = Get(key);
		if (song == null)
		{
			return;
		}
		lock (_sync)
		{
			_songs.Remove(key);
			_lists.Favorites.Remove(key);
			foreach (Playlist playlist in _lists.Playlists)
			{
				playlist.Songs.RemoveAll((string s) => s == key);
			}
		}
		try
		{
			if (song.Downloaded && !key.StartsWith("file:") && IsInsideSongs(song.File))
			{
				System.IO.File.Delete(song.File);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		Save();
	}

	/// <summary>Files that must survive the cache trimming: favorites and every playlist song.</summary>
	public static HashSet<string> ProtectedFiles()
	{
		EnsureLoaded();
		HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		lock (_sync)
		{
			foreach (string key in _lists.Favorites.Concat(_lists.Playlists.SelectMany((Playlist p) => p.Songs)))
			{
				if (_songs.TryGetValue(key, out var song) && song.File != null)
				{
					files.Add(Path.GetFullPath(song.File));
				}
			}
		}
		return files;
	}

	public static List<Song> Search(string query)
	{
		List<Song> all = All();
		if (string.IsNullOrWhiteSpace(query))
		{
			return all;
		}
		string[] words = query.ToLowerInvariant().Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		return all.Where((Song s) => words.All((string w) => (s.Title ?? "").ToLowerInvariant().Contains(w))).ToList();
	}

	#endregion

	#region Favorites and playlists

	public static List<string> Favorites
	{
		get
		{
			EnsureLoaded();
			lock (_sync)
			{
				return _lists.Favorites.ToList();
			}
		}
	}

	public static bool IsFavorite(string key)
	{
		EnsureLoaded();
		if (key == null)
		{
			return false;
		}
		lock (_sync)
		{
			return _lists.Favorites.Contains(key);
		}
	}

	public static void SetFavorite(string key, bool favorite)
	{
		EnsureLoaded();
		if (key == null)
		{
			return;
		}
		lock (_sync)
		{
			_lists.Favorites.Remove(key);
			if (favorite)
			{
				_lists.Favorites.Insert(0, key);
			}
		}
		Save();
	}

	public static List<Playlist> Playlists
	{
		get
		{
			EnsureLoaded();
			lock (_sync)
			{
				return _lists.Playlists.Select((Playlist p) => new Playlist
				{
					Name = p.Name,
					Songs = p.Songs.ToList(),
					Created = p.Created
				}).ToList();
			}
		}
	}

	public static Playlist GetPlaylist(string name)
	{
		return Playlists.FirstOrDefault((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	public static string CreatePlaylist(string name)
	{
		EnsureLoaded();
		name = (name ?? "").Trim();
		if (name.Length == 0)
		{
			name = "Playlist";
		}
		if (name.Length > 40)
		{
			name = name.Substring(0, 40);
		}
		lock (_sync)
		{
			string unique = name;
			int n = 2;
			while (_lists.Playlists.Any((Playlist p) => string.Equals(p.Name, unique, StringComparison.OrdinalIgnoreCase)))
			{
				unique = name + " " + n++;
			}
			_lists.Playlists.Add(new Playlist
			{
				Name = unique,
				Created = DateTime.UtcNow
			});
			name = unique;
		}
		Save();
		return name;
	}

	public static void DeletePlaylist(string name)
	{
		EnsureLoaded();
		lock (_sync)
		{
			_lists.Playlists.RemoveAll((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		}
		Save();
	}

	public static string RenamePlaylist(string name, string newName)
	{
		EnsureLoaded();
		newName = (newName ?? "").Trim();
		if (newName.Length == 0)
		{
			return name;
		}
		lock (_sync)
		{
			Playlist playlist = _lists.Playlists.FirstOrDefault((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (playlist == null || _lists.Playlists.Any((Playlist p) => p != playlist && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
			{
				return name;
			}
			playlist.Name = newName.Length > 40 ? newName.Substring(0, 40) : newName;
			newName = playlist.Name;
		}
		Save();
		return newName;
	}

	/// <summary>Adds songs to a playlist (no duplicates). Returns how many were new.</summary>
	public static int AddToPlaylist(string name, IEnumerable<string> keys)
	{
		EnsureLoaded();
		int added = 0;
		lock (_sync)
		{
			Playlist playlist = _lists.Playlists.FirstOrDefault((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (playlist == null)
			{
				return 0;
			}
			foreach (string key in keys)
			{
				if (key != null && !playlist.Songs.Contains(key))
				{
					playlist.Songs.Add(key);
					added++;
				}
			}
		}
		Save();
		return added;
	}

	public static void RemoveFromPlaylist(string name, string key)
	{
		EnsureLoaded();
		lock (_sync)
		{
			_lists.Playlists.FirstOrDefault((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Songs.Remove(key);
		}
		Save();
	}

	public static void MoveInPlaylist(string name, int from, int to)
	{
		EnsureLoaded();
		lock (_sync)
		{
			Playlist playlist = _lists.Playlists.FirstOrDefault((Playlist p) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
			if (playlist == null || from < 0 || from >= playlist.Songs.Count || to < 0 || to >= playlist.Songs.Count)
			{
				return;
			}
			string key = playlist.Songs[from];
			playlist.Songs.RemoveAt(from);
			playlist.Songs.Insert(to, key);
		}
		Save();
	}

	#endregion

	#region Thumbnails

	/// <summary>Every song gets an achievement icon as its cover, always the same one for the same song.</summary>
	public static int IconFor(string key)
	{
		if (_iconIndexes == null)
		{
			try
			{
				_iconIndexes = Main.Achievements.CreateAchievementsList().Select((Terraria.Achievements.Achievement a) => Main.Achievements.GetIconIndex(a.Name)).Distinct().ToArray();
			}
			catch
			{
				_iconIndexes = Enumerable.Range(0, 64).ToArray();
			}
			if (_iconIndexes.Length == 0)
			{
				_iconIndexes = Enumerable.Range(0, 64).ToArray();
			}
		}
		int hash = 17;
		foreach (char c in key ?? "")
		{
			hash = hash * 31 + c;
		}
		return _iconIndexes[(hash & 0x7FFFFFFF) % _iconIndexes.Length];
	}

	#endregion

	#region Files

	private static bool IsInsideSongs(string file)
	{
		try
		{
			return Path.GetFullPath(file).StartsWith(Path.GetFullPath(MusicPlayer.SongsFolder), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>A readable file name from a song title: "A: B" becomes "A - B", quotes and question marks go away.</summary>
	public static string SafeFileName(string title)
	{
		StringBuilder builder = new StringBuilder();
		char[] invalid = Path.GetInvalidFileNameChars();
		foreach (char c in title ?? "")
		{
			switch (c)
			{
			case ':':
				builder.Append(" - ");
				continue;
			case '/':
			case '\\':
			case '|':
				builder.Append('-');
				continue;
			case '"':
			case '?':
			case '*':
			case '<':
			case '>':
				continue;
			}
			if (!invalid.Contains(c) && !char.IsControl(c))
			{
				builder.Append(c);
			}
		}
		string name = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), "\\s{2,}", " ").Replace(" - - ", " - ").Trim().TrimEnd('.', ' ');
		if (name.Length > 120)
		{
			name = name.Substring(0, 120).Trim();
		}
		return (name.Length == 0) ? "Song" : name;
	}

	/// <summary>Renames a downloaded file to its song title (keeping the extension, never overwriting another file).</summary>
	private static string MoveToTitle(string file, string title)
	{
		string folder = Path.GetDirectoryName(file);
		string ext = Path.GetExtension(file);
		string baseName = SafeFileName(title);
		string target = Path.Combine(folder, baseName + ext);
		if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
		{
			return file;
		}
		int n = 2;
		while (System.IO.File.Exists(target))
		{
			target = Path.Combine(folder, baseName + " (" + n++ + ")" + ext);
		}
		System.IO.File.Move(file, target);
		return target;
	}

	private const string SourceStream = ":IsaClient.Source";

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

	/// <summary>Writes "id\nurl" into the file's NTFS stream (invisible, travels with the file on NTFS drives).</summary>
	public static void WriteSourceTag(string file, string id, string url)
	{
		try
		{
			using SafeFileHandle handle = CreateFileW(file + SourceStream, 0x40000000u, 0u, IntPtr.Zero, 2u, 0u, IntPtr.Zero);
			if (handle.IsInvalid)
			{
				return;
			}
			using FileStream stream = new FileStream(handle, FileAccess.Write);
			byte[] bytes = Encoding.UTF8.GetBytes(id + "\n" + url);
			stream.Write(bytes, 0, bytes.Length);
		}
		catch
		{
		}
	}

	/// <summary>The YouTube id stored in a file's NTFS stream, or null.</summary>
	public static string ReadSourceTag(string file)
	{
		try
		{
			using SafeFileHandle handle = CreateFileW(file + SourceStream, 0x80000000u, 1u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle.IsInvalid)
			{
				return null;
			}
			using FileStream stream = new FileStream(handle, FileAccess.Read);
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			string line = reader.ReadLine();
			return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
		}
		catch
		{
			return null;
		}
	}

	#endregion
}
