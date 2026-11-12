using System;
using System.Collections.Generic;
using TerraAngel.Compat;
using Terraria;

namespace TerraAngel;

// What TerraAngel code expects from .NET 10 and from TerraAngel's own game changes, for code inside TerraAngel.*
// namespaces (TerraAngel, its forks and plugins) to build unchanged on .NET Framework 4.8 and the original game.
// C# 14 extension members add static members to System types (Math.Clamp, Array.Fill...) and properties to the game's.

/// <summary>.NET's MathF (float math), missing on .NET Framework.</summary>
public static class MathF
{
	public const float PI = (float)Math.PI;

	public const float Tau = (float)(Math.PI * 2.0);

	public const float E = (float)Math.E;

	public static float Abs(float x) => Math.Abs(x);

	public static float Acos(float x) => (float)Math.Acos(x);

	public static float Asin(float x) => (float)Math.Asin(x);

	public static float Atan(float x) => (float)Math.Atan(x);

	public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);

	public static float Ceiling(float x) => (float)Math.Ceiling(x);

	public static float Cos(float x) => (float)Math.Cos(x);

	public static float Exp(float x) => (float)Math.Exp(x);

	public static float Floor(float x) => (float)Math.Floor(x);

	public static float Log(float x) => (float)Math.Log(x);

	public static float Log(float x, float newBase) => (float)Math.Log(x, newBase);

	public static float Log10(float x) => (float)Math.Log10(x);

	public static float Log2(float x) => (float)(Math.Log(x) / Math.Log(2.0));

	public static float Max(float x, float y) => Math.Max(x, y);

	public static float Min(float x, float y) => Math.Min(x, y);

	public static float Pow(float x, float y) => (float)Math.Pow(x, y);

	public static float Round(float x) => (float)Math.Round(x);

	public static float Round(float x, int digits) => (float)Math.Round(x, digits);

	public static float Round(float x, MidpointRounding mode) => (float)Math.Round(x, mode);

	public static int Sign(float x) => Math.Sign(x);

	public static float Sin(float x) => (float)Math.Sin(x);

	public static float Sqrt(float x) => (float)Math.Sqrt(x);

	public static float Tan(float x) => (float)Math.Tan(x);

	public static float Truncate(float x) => (float)Math.Truncate(x);

	public static float CopySign(float x, float y) => Math.Abs(x) * (y < 0f || (y == 0f && 1f / y < 0f) ? -1f : 1f);
}

/// <summary>Static members .NET added after .NET Framework, on the same types (C# 14 extension members).</summary>
public static class NetCompatExtensions
{
	private static readonly Random _sharedRandom = new Random();

	extension(Math)
	{
		public static int Clamp(int value, int min, int max) => (value < min) ? min : ((value > max) ? max : value);

		public static float Clamp(float value, float min, float max) => (value < min) ? min : ((value > max) ? max : value);

		public static double Clamp(double value, double min, double max) => (value < min) ? min : ((value > max) ? max : value);

		public static long Clamp(long value, long min, long max) => (value < min) ? min : ((value > max) ? max : value);

		public static byte Clamp(byte value, byte min, byte max) => (value < min) ? min : ((value > max) ? max : value);

		public static short Clamp(short value, short min, short max) => (value < min) ? min : ((value > max) ? max : value);

		public static ushort Clamp(ushort value, ushort min, ushort max) => (value < min) ? min : ((value > max) ? max : value);

		public static uint Clamp(uint value, uint min, uint max) => (value < min) ? min : ((value > max) ? max : value);
	}

	extension(Array)
	{
		public static void Fill<T>(T[] array, T value)
		{
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = value;
			}
		}
	}

	extension(float)
	{
		public static bool IsNormal(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f != 0f && Math.Abs(f) >= 1.17549435E-38f;

		public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
	}

	extension(System.Reflection.MethodInfo method)
	{
		public T CreateDelegate<T>() where T : Delegate => (T)method.CreateDelegate(typeof(T));

		public T CreateDelegate<T>(object target) where T : Delegate => (T)method.CreateDelegate(typeof(T), target);
	}

	extension(Enum)
	{
		public static string[] GetNames<T>() where T : struct, Enum => Enum.GetNames(typeof(T));

		public static T[] GetValues<T>() where T : struct, Enum => (T[])Enum.GetValues(typeof(T));

		public static bool IsDefined<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value);
	}

	extension(Random)
	{
		public static Random Shared => _sharedRandom;
	}

	extension(Vector2)
	{
		public static Vector2 NaN => new Vector2(float.NaN);
	}

	extension<TKey, TValue>(IDictionary<TKey, TValue> dictionary)
	{
		public TValue GetValueOrDefault(TKey key, TValue defaultValue = default) => dictionary.TryGetValue(key, out TValue value) ? value : defaultValue;
	}
}

/// <summary>
/// TerraAngel's tile map API (it stores tiles its own way) on the game's Tile[,]: Main.tile.InWorld(x, y),
/// Main.tile.Width, IsTileSectionLoaded... and Tile.HasValue / Tile.Value for its nullable tiles.
/// </summary>
public static class TileMapExtensions
{
	extension(Tile[,] map)
	{
		public uint Width => (uint)map.GetLength(0);

		public uint Height => (uint)map.GetLength(1);

		public bool InWorld(int x, int y) => (uint)x < (uint)map.GetLength(0) && (uint)y < (uint)map.GetLength(1);

		public bool InWorld(Microsoft.Xna.Framework.Point position) => map.InWorld(position.X, position.Y);

		public bool InWorld(Vector2 worldPosition) => map.InWorld((int)(worldPosition.X / 16f), (int)(worldPosition.Y / 16f));

		public bool IsTileSectionLoaded(int sectionX, int sectionY)
		{
			if (sectionX < 0 || sectionY < 0 || sectionX >= Main.maxSectionsX || sectionY >= Main.maxSectionsY)
			{
				return false;
			}
			return Main.netMode != 1 || Main.sectionManager == null || Main.sectionManager.SectionLoaded(sectionX, sectionY);
		}

		public bool IsTileInLoadedSection(int x, int y) => Main.netMode != 1 || map.IsTileSectionLoaded(x / Main.sectionWidth, y / Main.sectionHeight);

		public bool InWorldAndLoaded(int x, int y) => map.InWorld(x, y) && map.IsTileInLoadedSection(x, y);

		/// <summary>The tile at x, y, created if the slot was empty (TerraAngel returns a reference to its tile data).</summary>
		public Tile GetTileRef(int x, int y) => map[x, y] ??= new Tile();
	}

	extension(Terraria.Graphics.SpriteViewMatrix matrix)
	{
		public Microsoft.Xna.Framework.Matrix InverseZoomMatrix => Microsoft.Xna.Framework.Matrix.Invert(matrix.ZoomMatrix);
	}

	extension(Tile tile)
	{
		public bool HasValue => tile != null;

		public Tile Value => tile;
	}
}
