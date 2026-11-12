using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// Secret chat: what you write goes through the server's chat as made-up words ("Kaibo tuemi rasofe."), with no mark,
/// so everyone else reads nonsense; IsaClient turns them back into your words when it shows the message, in a soft
/// violet. Each byte of the text is one syllable (one of 16 consonants and a vowel sound), scrambled with a random start
/// and closed with a 16-bit check. A message is only read as a secret when all of it is such syllables (any w, y, z,
/// q, digit or two consonants together rules it out), at least four, with the check right (1 in 65536 by chance) and
/// proper text inside, so ordinary chat is never taken for one. It hides the text from people without
/// IsaClient, not from someone who reads this code.
/// <list type="bullet">
/// <item>.secret &lt;message&gt; sends one; .secret alone switches what you type to secret (and back).</item>
/// </list>
/// </summary>
public static class SecretChat
{

	/// <summary>Everything you type goes out secret (until .secret again).</summary>
	public static bool On;

	/// <summary>Plain text per message at most; longer text goes in several.</summary>
	private const int MaxChunk = 120;

	private const string Consonants = "bcdfghjklmnprstv";

	private static readonly string[] Vowels = { "a", "e", "i", "o", "u", "ai", "au", "ea", "ei", "ia", "ie", "io", "oa", "ou", "ua", "ue" };

	private static readonly Dictionary<string, int> _vowel = new Dictionary<string, int>();

	/// <summary>The end of a chat line made only of letters, spaces and . , ! ? (the server's format before it stops it).</summary>
	private static readonly Regex _tail = new Regex("[A-Za-z ,.!?']{8,}$", RegexOptions.Compiled);

	private static readonly Color Shown = new Color(200, 160, 255);

	private const uint Key = 0x5EC12E7u;

	private static readonly Random _random = new Random();

	static SecretChat()
	{
		for (int i = 0; i < Vowels.Length; i++)
		{
			_vowel[Vowels[i]] = i;
		}
	}

	#region Commands

	/// <summary>.secret [message]</summary>
	public static void SecretCommand(CommandContext command)
	{
		string text = command.Rest.Trim();
		if (text.Length == 0)
		{
			On = !On;
			ClientChat.Info(On ? "Secret chat on: what you type goes out as made-up words; players with IsaClient read it. .secret again turns it off." : "Secret chat off.");
			return;
		}
		Send(text);
	}

	/// <summary>Sends a message as secret, straight to the server's chat (in pieces when it's long; never translated).</summary>
	public static void Send(string text)
	{
		for (int i = 0; i < text.Length; i += MaxChunk)
		{
			Main.SendRawChat(Encode(text.Substring(i, Math.Min(MaxChunk, text.Length - i))));
		}
	}

	/// <summary>A typed chat line about to be sent: with the mode on it goes secret (server commands never do). True when it was sent here.</summary>
	public static bool TrySendTyped(string text)
	{
		if (!On || string.IsNullOrWhiteSpace(text) || text[0] == '/')
		{
			return false;
		}
		Send(text);
		return true;
	}

	#endregion

	#region Code

	public static string Encode(string text)
	{
		byte[] plain = Encoding.UTF8.GetBytes(text);
		byte nonce = (byte)_random.Next(256);
		byte[] data = new byte[plain.Length + 3];
		data[0] = nonce;
		uint state = Key ^ (uint)(nonce * 2654435761u);
		for (int i = 0; i < plain.Length; i++)
		{
			data[i + 1] = (byte)(plain[i] ^ Next(ref state));
		}
		ushort check = Check(plain, nonce);
		data[data.Length - 2] = (byte)(check & 0xFF);
		data[data.Length - 1] = (byte)(check >> 8);
		// One syllable per byte, in made-up words of 2 to 4 syllables.
		StringBuilder words = new StringBuilder();
		int inWord = 0;
		int wordLength = 2 + _random.Next(3);
		bool sentenceStart = true;
		for (int i = 0; i < data.Length; i++)
		{
			string syllable = Consonants[data[i] >> 4] + Vowels[data[i] & 15];
			if (sentenceStart)
			{
				syllable = char.ToUpperInvariant(syllable[0]) + syllable.Substring(1);
				sentenceStart = false;
			}
			words.Append(syllable);
			if (++inWord >= wordLength && i < data.Length - 1)
			{
				inWord = 0;
				wordLength = 2 + _random.Next(3);
				if (_random.Next(7) == 0)
				{
					words.Append(_random.Next(3) == 0 ? "," : ".");
					sentenceStart = words[words.Length - 1] == '.';
				}
				words.Append(' ');
			}
		}
		words.Append('.');
		return words.ToString();
	}

	/// <summary>The words back into text, or null when they aren't a secret (bad syllables or check).</summary>
	public static string Decode(string words)
	{
		List<byte> data = new List<byte>();
		string letters = words.ToLowerInvariant();
		int i = 0;
		while (i < letters.Length)
		{
			char c = letters[i];
			int consonant = Consonants.IndexOf(c);
			if (consonant < 0)
			{
				if (char.IsLetter(c))
				{
					return null;
				}
				i++;
				continue;
			}
			int j = i + 1;
			while (j < letters.Length && "aeiou".IndexOf(letters[j]) >= 0)
			{
				j++;
			}
			if (!_vowel.TryGetValue(letters.Substring(i + 1, j - i - 1), out int vowel))
			{
				return null;
			}
			data.Add((byte)((consonant << 4) | vowel));
			i = j;
		}
		if (data.Count < 4)
		{
			return null;
		}
		byte nonce = data[0];
		uint state = Key ^ (uint)(nonce * 2654435761u);
		byte[] plain = new byte[data.Count - 3];
		for (int k = 0; k < plain.Length; k++)
		{
			plain[k] = (byte)(data[k + 1] ^ Next(ref state));
		}
		if (Check(plain, nonce) != (data[data.Count - 2] | (data[data.Count - 1] << 8)))
		{
			return null;
		}
		try
		{
			string text = new UTF8Encoding(false, true).GetString(plain);
			foreach (char c in text)
			{
				if (char.IsControl(c))
				{
					return null;
				}
			}
			return text;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>16-bit check of the text, mixed with the start so it differs every time.</summary>
	private static ushort Check(byte[] plain, byte nonce)
	{
		uint h = 2166136261u ^ nonce;
		foreach (byte b in plain)
		{
			h = (h ^ b) * 16777619u;
		}
		return (ushort)(h ^ (h >> 16));
	}

	private static byte Next(ref uint state)
	{
		state ^= state << 13;
		state ^= state >> 17;
		state ^= state << 5;
		return (byte)(state >> 8);
	}

	/// <summary>
	/// Where a line's secret starts, or -1: the end of the line made only of letters and punctuation, from its first
	/// word or a later one (a format like "Name message" has the name in front), read as a secret.
	/// </summary>
	private static int Find(string text, out string plain)
	{
		plain = null;
		if (string.IsNullOrEmpty(text))
		{
			return -1;
		}
		Match m = _tail.Match(text);
		if (!m.Success)
		{
			return -1;
		}
		for (int at = m.Index; at < text.Length; at++)
		{
			if (at > m.Index && text[at - 1] != ' ')
			{
				continue;
			}
			if (text[at] == ' ')
			{
				continue;
			}
			plain = Decode(text.Substring(at));
			if (plain != null)
			{
				return at;
			}
		}
		return -1;
	}

	/// <summary>True when the line ends in a secret.</summary>
	public static bool IsSecret(string text)
	{
		return Find(text, out _) >= 0;
	}

	/// <summary>A chat line to show with its secret read (in violet); the same text when it has none.</summary>
	public static string Expand(string text)
	{
		int at = Find(text, out string plain);
		return (at < 0) ? text : (text.Substring(0, at) + ChatColor.Colorize(plain, Shown));
	}

	#endregion
}
