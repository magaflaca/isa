using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using IsaClient.Core;
using Microsoft.Xna.Framework;

namespace IsaClient.Gameplay;

/// <summary>A stroke of a rainbow letter, in glyph units (a letter is about 100 wide and 120 tall, y down).</summary>
public sealed class RainbowShape
{
	public enum ShapeKind
	{
		Line,
		Arc,
		Point
	}

	public ShapeKind Kind;

	/// <summary>Line: start. Arc: center. Point: where it is.</summary>
	public Vector2 A;

	/// <summary>Line: end.</summary>
	public Vector2 B;

	public float Radius;

	/// <summary>Arc: from and to (radians, y down, either way round). Point: its direction.</summary>
	public float Start;

	public float End;

	public RainbowShape Clone() => (RainbowShape)MemberwiseClone();

	public static RainbowShape Line(Vector2 a, Vector2 b) => new RainbowShape { Kind = ShapeKind.Line, A = a, B = b };

	public static RainbowShape Arc(Vector2 center, float radius, float start, float end) => new RainbowShape { Kind = ShapeKind.Arc, A = center, Radius = radius, Start = start, End = end };

	public static RainbowShape Dot(Vector2 at, float direction = 0f) => new RainbowShape { Kind = ShapeKind.Point, A = at, Start = direction };

	/// <summary>Where an arc's angle lands.</summary>
	public Vector2 At(float angle) => A + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Radius;
}

/// <summary>One character's strokes, and how much room it takes in a line of text.</summary>
public sealed class RainbowGlyph
{
	public char Char;

	public List<RainbowShape> Shapes = new List<RainbowShape>();

	/// <summary>Distance to the next letter (glyph units).</summary>
	public float Advance = RainbowFont.DefaultAdvance;

	public RainbowGlyph Clone() => new RainbowGlyph { Char = Char, Advance = Advance, Shapes = Shapes.Select(s => s.Clone()).ToList() };
}

/// <summary>
/// The letters .rainbow writes with: the built-in ones (A-Z, 0-9) and yours (IsaClient/RainbowFont.xml), which
/// replace or add to them (any character: symbols, lowercase, accents). Both use the RainbowLetters XML format
/// (&lt;char&gt;&lt;type&gt;A&lt;/type&gt;&lt;body&gt; lines, arcs and points &lt;/body&gt;&lt;/char&gt;), so fonts can be
/// shared and imported. <see cref="Points"/> turns a letter into the rainbows that draw it.
/// </summary>
public static class RainbowFont
{
	public const float DefaultAdvance = 150f;

	/// <summary>One rainbow every this many units along a stroke.</summary>
	private const float Step = 9f;

	private static Dictionary<char, RainbowGlyph> _builtIn;

	private static Dictionary<char, RainbowGlyph> _mine;

	/// <summary>Changes whenever a letter changes (previews rebuild).</summary>
	public static int Version;

	public static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "RainbowFont.xml");

	public static string ExportFolder => Path.Combine(ClientSettings.IsaClientFolder, "RainbowFonts");

	#region Letters

	private static void Load()
	{
		if (_builtIn != null)
		{
			return;
		}
		_builtIn = new Dictionary<char, RainbowGlyph>();
		_mine = new Dictionary<char, RainbowGlyph>();
		try
		{
			using Stream stream = typeof(RainbowFont).Assembly.GetManifestResourceStream("IsaClient.Assets.RainbowLetters.xml");
			if (stream != null)
			{
				using StreamReader reader = new StreamReader(stream);
				foreach (RainbowGlyph g in Parse(reader.ReadToEnd()))
				{
					_builtIn[g.Char] = g;
				}
			}
			if (File.Exists(FilePath))
			{
				foreach (RainbowGlyph g in Parse(File.ReadAllText(FilePath)))
				{
					_mine[g.Char] = g;
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Every character there's a letter for (yours first where both have one), in a tidy order.</summary>
	public static List<char> Characters
	{
		get
		{
			Load();
			return _builtIn.Keys.Union(_mine.Keys).OrderBy(c => char.IsLetter(c) ? 0 : (char.IsDigit(c) ? 1 : 2)).ThenBy(c => c).ToList();
		}
	}

	/// <summary>The letter used for a character (yours, else the built-in one; letters also match the other case).</summary>
	public static RainbowGlyph Get(char c)
	{
		Load();
		if (_mine.TryGetValue(c, out RainbowGlyph g) || _builtIn.TryGetValue(c, out g))
		{
			return g;
		}
		char other = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
		return (_mine.TryGetValue(other, out g) || _builtIn.TryGetValue(other, out g)) ? g : null;
	}

	public static bool IsMine(char c)
	{
		Load();
		return _mine.ContainsKey(c);
	}

	public static bool HasBuiltIn(char c)
	{
		Load();
		return _builtIn.ContainsKey(c);
	}

	/// <summary>Saves your version of a letter (and your font file).</summary>
	public static void Set(RainbowGlyph glyph)
	{
		Load();
		_mine[glyph.Char] = glyph.Clone();
		Save();
	}

	/// <summary>Drops your version of a letter: the built-in one is used again (or there's none).</summary>
	public static void Forget(char c)
	{
		Load();
		if (_mine.Remove(c))
		{
			Save();
		}
	}

	private static void Save()
	{
		Version++;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			File.WriteAllText(FilePath, Write(_mine.Values.OrderBy(g => g.Char)));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not save your rainbow letters: " + ex.Message);
		}
	}

	/// <summary>Letters from a font file or text: each becomes yours (replacing what was there). Returns how many.</summary>
	public static int Import(string xml)
	{
		Load();
		List<RainbowGlyph> glyphs = Parse(xml);
		foreach (RainbowGlyph g in glyphs)
		{
			_mine[g.Char] = g;
		}
		if (glyphs.Count > 0)
		{
			Save();
		}
		return glyphs.Count;
	}

	/// <summary>The whole font as it's used now (yours where you made one, the built-in ones elsewhere), or some letters.</summary>
	public static string Export(IEnumerable<char> only = null)
	{
		return Write((only ?? Characters).Select(Get).Where(g => g != null));
	}

	#endregion

	#region XML (RainbowLetters format)

	private static float F(XmlElement e, string name) => float.Parse(e.GetAttribute(name).Trim(), CultureInfo.InvariantCulture);

	private static Vector2 P(XmlElement e, string name)
	{
		string[] parts = e.GetAttribute(name).Split(',');
		return new Vector2(float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
	}

	public static List<RainbowGlyph> Parse(string xml)
	{
		List<RainbowGlyph> glyphs = new List<RainbowGlyph>();
		XmlDocument document = new XmlDocument();
		document.LoadXml(xml);
		foreach (XmlNode node in document.DocumentElement.ChildNodes)
		{
			if (!(node is XmlElement element) || element["type"] == null || element["body"] == null || element["type"].InnerText.Length == 0)
			{
				continue;
			}
			RainbowGlyph glyph = new RainbowGlyph { Char = element["type"].InnerText[0] };
			if (element.HasAttribute("advance"))
			{
				glyph.Advance = F(element, "advance");
			}
			foreach (XmlNode shapeNode in element["body"].ChildNodes)
			{
				if (!(shapeNode is XmlElement s))
				{
					continue;
				}
				try
				{
					switch (s.Name)
					{
					case "line":
						glyph.Shapes.Add(RainbowShape.Line(P(s, "start"), P(s, "end")));
						break;
					case "arc":
						glyph.Shapes.Add(RainbowShape.Arc(P(s, "center"), F(s, "radius"), F(s, "start_radian"), F(s, "end_radian")));
						break;
					case "arcf":
						glyph.Shapes.Add(RainbowShape.Arc(P(s, "center"), F(s, "radius"), F(s, "start_radian_factor") * (float)Math.PI, F(s, "end_radian_factor") * (float)Math.PI));
						break;
					case "point":
						glyph.Shapes.Add(RainbowShape.Dot(P(s, "location"), s.HasAttribute("direction") ? F(s, "direction") : 0f));
						break;
					}
				}
				catch (FormatException)
				{
				}
			}
			glyphs.Add(glyph);
		}
		return glyphs;
	}

	private static string N(float v) => v.ToString("0.#######", CultureInfo.InvariantCulture);

	public static string Write(IEnumerable<RainbowGlyph> glyphs)
	{
		StringBuilder b = new StringBuilder();
		b.AppendLine("<data>");
		foreach (RainbowGlyph g in glyphs)
		{
			b.Append("\t<char").Append((Math.Abs(g.Advance - DefaultAdvance) > 0.01f) ? (" advance = \"" + N(g.Advance) + "\"") : "").AppendLine(">");
			b.Append("\t\t<type>").Append(SecurityElementEscape(g.Char.ToString())).AppendLine("</type>");
			b.AppendLine("\t\t<body>");
			foreach (RainbowShape s in g.Shapes)
			{
				switch (s.Kind)
				{
				case RainbowShape.ShapeKind.Line:
					b.Append("\t\t\t<line start = \"").Append(N(s.A.X)).Append(',').Append(N(s.A.Y)).Append("\" end = \"").Append(N(s.B.X)).Append(',').Append(N(s.B.Y)).AppendLine("\"/>");
					break;
				case RainbowShape.ShapeKind.Arc:
					b.Append("\t\t\t<arc center = \"").Append(N(s.A.X)).Append(',').Append(N(s.A.Y)).Append("\" radius = \"").Append(N(s.Radius)).Append("\" start_radian = \"").Append(N(s.Start)).Append("\" end_radian = \"").Append(N(s.End)).AppendLine("\"/>");
					break;
				default:
					b.Append("\t\t\t<point location = \"").Append(N(s.A.X)).Append(',').Append(N(s.A.Y)).Append("\" direction = \"").Append(N(s.Start)).AppendLine("\"/>");
					break;
				}
			}
			b.AppendLine("\t\t</body>");
			b.AppendLine("\t</char>");
		}
		b.AppendLine("</data>");
		return b.ToString();
	}

	private static string SecurityElementEscape(string s) => System.Security.SecurityElement.Escape(s);

	#endregion

	#region Rainbows

	/// <summary>
	/// The rainbows that draw a letter: (x, y, velocity x, velocity y), one every few units along each stroke, each
	/// facing along it. Centered sideways on the letter's own box; up and down it stays where it was drawn (y 0 is the
	/// middle of a line), so a dot or a comma sits low and an apostrophe high.
	/// </summary>
	public static List<Vector4> Points(RainbowGlyph glyph, bool center = true)
	{
		List<Vector4> points = new List<Vector4>();
		foreach (RainbowShape s in glyph.Shapes)
		{
			Emit(s, points);
		}
		if (points.Count == 0 || !center)
		{
			return points;
		}
		float middle = Middle(points).X;
		return points.Select(p => new Vector4(p.X - middle, p.Y, p.Z, p.W)).ToList();
	}

	/// <summary>The middle of a set of rainbows' box (what <see cref="Points"/> centers on).</summary>
	public static Vector2 Middle(List<Vector4> points)
	{
		float minX = points.Min(p => p.X);
		float maxX = points.Max(p => p.X);
		float minY = points.Min(p => p.Y);
		float maxY = points.Max(p => p.Y);
		return new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
	}

	private static void Add(List<Vector4> points, Vector2 location, float direction)
	{
		Vector2 unit = new Vector2((float)Math.Cos(direction), (float)Math.Sin(direction));
		Vector2 position = location + unit * 13f;
		Vector2 velocity = unit * 0.02f;
		points.Add(new Vector4(position.X, position.Y, velocity.X, velocity.Y));
	}

	private static void Emit(RainbowShape s, List<Vector4> points)
	{
		switch (s.Kind)
		{
		case RainbowShape.ShapeKind.Line:
		{
			Vector2 delta = s.B - s.A;
			float length = delta.Length();
			if (length <= 0.001f)
			{
				break;
			}
			int steps = Math.Max(1, (int)Math.Ceiling(length / Step));
			float direction = (float)Math.Atan2(delta.Y, delta.X);
			for (int i = 0; i <= steps; i++)
			{
				Add(points, s.A + delta * ((float)i / steps), direction);
			}
			break;
		}
		case RainbowShape.ShapeKind.Arc:
		{
			float total = s.End - s.Start;
			if (s.Radius <= 0.001f || Math.Abs(total) <= 1E-06f)
			{
				break;
			}
			float sign = (total >= 0f) ? 1f : -1f;
			float step = sign * (Step / s.Radius);
			for (float r = s.Start; (sign > 0f) ? (r < s.End) : (r > s.End); r += step)
			{
				Add(points, s.At(r), r + sign * ((float)Math.PI / 2f));
			}
			Add(points, s.At(s.End), s.End + sign * ((float)Math.PI / 2f));
			break;
		}
		default:
			Add(points, s.A, s.Start);
			break;
		}
	}

	#endregion
}
