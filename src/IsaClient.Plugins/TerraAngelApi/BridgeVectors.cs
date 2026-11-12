using System;
using System.Globalization;
using System.Runtime.InteropServices;
using XVector2 = Microsoft.Xna.Framework.Vector2;
using XVector3 = Microsoft.Xna.Framework.Vector3;
using XVector4 = Microsoft.Xna.Framework.Vector4;
using XMatrix = Microsoft.Xna.Framework.Matrix;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace TerraAngel.Compat;

// TerraAngel runs on an FNA fork where the game's vectors ARE System.Numerics vectors, so its code (and the code of its
// forks and plugins) passes player positions straight to ImGui and back. Here the game has XNA vectors and ImGui has
// System.Numerics ones. These vectors convert to and from both by themselves; TerraAngel code (the pack, .TAPlugin
// projects, fork sources) gets them as "Vector2/3/4" through global using aliases and builds almost unchanged.
// Operators exist for every mix (bridge, XNA, Numerics) so a mixed expression is never ambiguous.

/// <summary>A 2D vector that converts implicitly to and from XNA's and System.Numerics' Vector2.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector2 : IEquatable<Vector2>, IFormattable
{
	public float X;

	public float Y;

	public Vector2(float value)
	{
		X = value;
		Y = value;
	}

	public Vector2(float x, float y)
	{
		X = x;
		Y = y;
	}

	public static Vector2 Zero => default;

	public static Vector2 One => new Vector2(1f);

	public static Vector2 UnitX => new Vector2(1f, 0f);

	public static Vector2 UnitY => new Vector2(0f, 1f);

	public static implicit operator XVector2(Vector2 v) => new XVector2(v.X, v.Y);

	public static implicit operator Vector2(XVector2 v) => new Vector2(v.X, v.Y);

	public static implicit operator NVector2(Vector2 v) => new NVector2(v.X, v.Y);

	public static implicit operator Vector2(NVector2 v) => new Vector2(v.X, v.Y);

	public float Length() => (float)Math.Sqrt(X * X + Y * Y);

	public float LengthSquared() => X * X + Y * Y;

	public void Normalize()
	{
		float length = Length();
		if (length > 0f)
		{
			X /= length;
			Y /= length;
		}
	}

	// The game's extension methods on XNA's Vector2 (Terraria.Utils), which don't apply to this type by themselves.

	public float ToRotation() => Terraria.Utils.ToRotation(this);

	public Vector2 RotatedBy(double radians, Vector2 center = default) => Terraria.Utils.RotatedBy(this, radians, center);

	public Vector2 RotatedByRandom(double maxRadians) => Terraria.Utils.RotatedByRandom(this, maxRadians);

	public Vector2 Floor() => new Vector2((float)Math.Floor(X), (float)Math.Floor(Y));

	public bool HasNaNs() => float.IsNaN(X) || float.IsNaN(Y);

	public Vector3 ToVector3() => new Vector3(X, Y, 0f);

	public Vector2 MoveTowards(Vector2 target, float maxAmountAllowedToMove) => Terraria.Utils.MoveTowards(this, target, maxAmountAllowedToMove);

	public Terraria.DataStructures.Point16 ToTileCoordinates16() => Terraria.Utils.ToTileCoordinates16(this);

	public Microsoft.Xna.Framework.Point ToTileCoordinates() => Terraria.Utils.ToTileCoordinates(this);

	public Vector2 ToWorldCoordinates(float autoAddX = 8f, float autoAddY = 8f) => new Vector2(X * 16f + autoAddX, Y * 16f + autoAddY);

	public Microsoft.Xna.Framework.Point ToPoint() => new Microsoft.Xna.Framework.Point((int)X, (int)Y);

	public Vector2 SafeNormalize(Vector2 defaultValue) => Terraria.Utils.SafeNormalize(this, defaultValue);

	public Vector2 ClosestPointOnLine(Vector2 a, Vector2 b) => Terraria.Utils.ClosestPointOnLine(this, a, b);

	public float Distance(Vector2 target) => Distance(this, target);

	public float DistanceSQ(Vector2 target) => DistanceSquared(this, target);

	public Vector2 DirectionTo(Vector2 target) => Normalize(target - this);

	public Vector2 DirectionFrom(Vector2 target) => Normalize(this - target);

	public static float Distance(Vector2 a, Vector2 b) => (a - b).Length();

	public static float DistanceSquared(Vector2 a, Vector2 b) => (a - b).LengthSquared();

	public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

	public static Vector2 Normalize(Vector2 v)
	{
		float length = v.Length();
		return (length > 0f) ? new Vector2(v.X / length, v.Y / length) : v;
	}

	public static Vector2 Lerp(Vector2 a, Vector2 b, float amount) => a + (b - a) * amount;

	public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

	public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

	public static Vector2 Clamp(Vector2 v, Vector2 min, Vector2 max) => Min(Max(v, min), max);

	public static Vector2 Abs(Vector2 v) => new Vector2(Math.Abs(v.X), Math.Abs(v.Y));

	public static Vector2 SquareRoot(Vector2 v) => new Vector2((float)Math.Sqrt(v.X), (float)Math.Sqrt(v.Y));

	public static Vector2 Negate(Vector2 v) => -v;

	public static Vector2 Add(Vector2 a, Vector2 b) => a + b;

	public static Vector2 Subtract(Vector2 a, Vector2 b) => a - b;

	public static Vector2 Multiply(Vector2 a, Vector2 b) => a * b;

	public static Vector2 Multiply(Vector2 a, float b) => a * b;

	public static Vector2 Divide(Vector2 a, Vector2 b) => a / b;

	public static Vector2 Divide(Vector2 a, float b) => a / b;

	public static Vector2 Reflect(Vector2 v, Vector2 normal) => v - normal * (2f * Dot(v, normal));

	public static Vector2 Transform(Vector2 v, XMatrix m) => XVector2.Transform(v, m);

	public static Vector2 Transform(Vector2 v, System.Numerics.Matrix4x4 m) => NVector2.Transform(v, m);

	public static Vector2 Transform(Vector2 v, System.Numerics.Matrix3x2 m) => NVector2.Transform(v, m);

	public static Vector2 TransformNormal(Vector2 v, XMatrix m) => XVector2.TransformNormal(v, m);

	public static Vector2 operator -(Vector2 v) => new Vector2(-v.X, -v.Y);

	public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

	public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

	public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.X * b.X, a.Y * b.Y);

	public static Vector2 operator /(Vector2 a, Vector2 b) => new Vector2(a.X / b.X, a.Y / b.Y);

	public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.X * b, a.Y * b);

	public static Vector2 operator *(float a, Vector2 b) => new Vector2(a * b.X, a * b.Y);

	public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.X / b, a.Y / b);

	public static Vector2 operator +(Vector2 a, XVector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

	public static Vector2 operator +(XVector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

	public static Vector2 operator -(Vector2 a, XVector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

	public static Vector2 operator -(XVector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

	public static Vector2 operator *(Vector2 a, XVector2 b) => new Vector2(a.X * b.X, a.Y * b.Y);

	public static Vector2 operator *(XVector2 a, Vector2 b) => new Vector2(a.X * b.X, a.Y * b.Y);

	public static Vector2 operator /(Vector2 a, XVector2 b) => new Vector2(a.X / b.X, a.Y / b.Y);

	public static Vector2 operator /(XVector2 a, Vector2 b) => new Vector2(a.X / b.X, a.Y / b.Y);

	public static Vector2 operator +(Vector2 a, NVector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

	public static Vector2 operator +(NVector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

	public static Vector2 operator -(Vector2 a, NVector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

	public static Vector2 operator -(NVector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

	public static Vector2 operator *(Vector2 a, NVector2 b) => new Vector2(a.X * b.X, a.Y * b.Y);

	public static Vector2 operator *(NVector2 a, Vector2 b) => new Vector2(a.X * b.X, a.Y * b.Y);

	public static Vector2 operator /(Vector2 a, NVector2 b) => new Vector2(a.X / b.X, a.Y / b.Y);

	public static Vector2 operator /(NVector2 a, Vector2 b) => new Vector2(a.X / b.X, a.Y / b.Y);

	public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;

	public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

	public static bool operator ==(Vector2 a, XVector2 b) => a.X == b.X && a.Y == b.Y;

	public static bool operator !=(Vector2 a, XVector2 b) => !(a == b);

	public static bool operator ==(XVector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;

	public static bool operator !=(XVector2 a, Vector2 b) => !(a == b);

	public static bool operator ==(Vector2 a, NVector2 b) => a.X == b.X && a.Y == b.Y;

	public static bool operator !=(Vector2 a, NVector2 b) => !(a == b);

	public static bool operator ==(NVector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;

	public static bool operator !=(NVector2 a, Vector2 b) => !(a == b);

	public bool Equals(Vector2 other) => this == other;

	public override bool Equals(object obj) => (obj is Vector2 v && this == v) || (obj is XVector2 x && this == x) || (obj is NVector2 n && this == n);

	public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();

	public override string ToString() => ToString("G", CultureInfo.CurrentCulture);

	public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

	public string ToString(string format, IFormatProvider provider) => "<" + X.ToString(format, provider) + ", " + Y.ToString(format, provider) + ">";

	public void Deconstruct(out float x, out float y)
	{
		x = X;
		y = Y;
	}
}

/// <summary>A 3D vector that converts implicitly to and from XNA's and System.Numerics' Vector3.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector3 : IEquatable<Vector3>
{
	public float X;

	public float Y;

	public float Z;

	public Vector3(float value)
	{
		X = value;
		Y = value;
		Z = value;
	}

	public Vector3(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public Vector3(Vector2 xy, float z)
	{
		X = xy.X;
		Y = xy.Y;
		Z = z;
	}

	public static Vector3 Zero => default;

	public static Vector3 One => new Vector3(1f);

	public static implicit operator XVector3(Vector3 v) => new XVector3(v.X, v.Y, v.Z);

	public static implicit operator Vector3(XVector3 v) => new Vector3(v.X, v.Y, v.Z);

	public static implicit operator NVector3(Vector3 v) => new NVector3(v.X, v.Y, v.Z);

	public static implicit operator Vector3(NVector3 v) => new Vector3(v.X, v.Y, v.Z);

	public float Length() => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

	public float LengthSquared() => X * X + Y * Y + Z * Z;

	public static Vector3 Lerp(Vector3 a, Vector3 b, float amount) => a + (b - a) * amount;

	public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

	public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

	public static Vector3 Clamp(Vector3 v, Vector3 min, Vector3 max) => Min(Max(v, min), max);

	public static Vector3 operator -(Vector3 v) => new Vector3(-v.X, -v.Y, -v.Z);

	public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

	public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

	public static Vector3 operator *(Vector3 a, Vector3 b) => new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

	public static Vector3 operator /(Vector3 a, Vector3 b) => new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

	public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.X * b, a.Y * b, a.Z * b);

	public static Vector3 operator *(float a, Vector3 b) => b * a;

	public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.X / b, a.Y / b, a.Z / b);

	public static Vector3 operator +(Vector3 a, XVector3 b) => a + (Vector3)b;

	public static Vector3 operator +(XVector3 a, Vector3 b) => (Vector3)a + b;

	public static Vector3 operator -(Vector3 a, XVector3 b) => a - (Vector3)b;

	public static Vector3 operator -(XVector3 a, Vector3 b) => (Vector3)a - b;

	public static Vector3 operator *(Vector3 a, XVector3 b) => a * (Vector3)b;

	public static Vector3 operator *(XVector3 a, Vector3 b) => (Vector3)a * b;

	public static Vector3 operator +(Vector3 a, NVector3 b) => a + (Vector3)b;

	public static Vector3 operator +(NVector3 a, Vector3 b) => (Vector3)a + b;

	public static Vector3 operator -(Vector3 a, NVector3 b) => a - (Vector3)b;

	public static Vector3 operator -(NVector3 a, Vector3 b) => (Vector3)a - b;

	public static Vector3 operator *(Vector3 a, NVector3 b) => a * (Vector3)b;

	public static Vector3 operator *(NVector3 a, Vector3 b) => (Vector3)a * b;

	public static bool operator ==(Vector3 a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

	public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

	public bool Equals(Vector3 other) => this == other;

	public override bool Equals(object obj) => obj is Vector3 v && this == v;

	public override int GetHashCode() => (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();

	public override string ToString() => "<" + X + ", " + Y + ", " + Z + ">";
}

/// <summary>A 4D vector (colors in ImGui) that converts implicitly to and from XNA's and System.Numerics' Vector4.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector4 : IEquatable<Vector4>
{
	public float X;

	public float Y;

	public float Z;

	public float W;

	public Vector4(float value)
	{
		X = value;
		Y = value;
		Z = value;
		W = value;
	}

	public Vector4(float x, float y, float z, float w)
	{
		X = x;
		Y = y;
		Z = z;
		W = w;
	}

	public Vector4(Vector3 xyz, float w)
	{
		X = xyz.X;
		Y = xyz.Y;
		Z = xyz.Z;
		W = w;
	}

	public static Vector4 Zero => default;

	public static Vector4 One => new Vector4(1f);

	public static implicit operator XVector4(Vector4 v) => new XVector4(v.X, v.Y, v.Z, v.W);

	public static implicit operator Vector4(XVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

	public static implicit operator NVector4(Vector4 v) => new NVector4(v.X, v.Y, v.Z, v.W);

	public static implicit operator Vector4(NVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

	public static Vector4 Lerp(Vector4 a, Vector4 b, float amount) => a + (b - a) * amount;

	public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);

	public static Vector4 operator -(Vector4 v) => new Vector4(-v.X, -v.Y, -v.Z, -v.W);

	public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);

	public static Vector4 operator *(Vector4 a, Vector4 b) => new Vector4(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);

	public static Vector4 operator *(Vector4 a, float b) => new Vector4(a.X * b, a.Y * b, a.Z * b, a.W * b);

	public static Vector4 operator *(float a, Vector4 b) => b * a;

	public static Vector4 operator /(Vector4 a, float b) => new Vector4(a.X / b, a.Y / b, a.Z / b, a.W / b);

	public static Vector4 operator +(Vector4 a, XVector4 b) => a + (Vector4)b;

	public static Vector4 operator +(XVector4 a, Vector4 b) => (Vector4)a + b;

	public static Vector4 operator -(Vector4 a, XVector4 b) => a - (Vector4)b;

	public static Vector4 operator -(XVector4 a, Vector4 b) => (Vector4)a - b;

	public static Vector4 operator *(Vector4 a, XVector4 b) => a * (Vector4)b;

	public static Vector4 operator *(XVector4 a, Vector4 b) => (Vector4)a * b;

	public static Vector4 operator +(Vector4 a, NVector4 b) => a + (Vector4)b;

	public static Vector4 operator +(NVector4 a, Vector4 b) => (Vector4)a + b;

	public static Vector4 operator -(Vector4 a, NVector4 b) => a - (Vector4)b;

	public static Vector4 operator -(NVector4 a, Vector4 b) => (Vector4)a - b;

	public static Vector4 operator *(Vector4 a, NVector4 b) => a * (Vector4)b;

	public static Vector4 operator *(NVector4 a, Vector4 b) => (Vector4)a * b;

	public static bool operator ==(Vector4 a, Vector4 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;

	public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);

	public bool Equals(Vector4 other) => this == other;

	public override bool Equals(object obj) => obj is Vector4 v && this == v;

	public override int GetHashCode() => ((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode()) * 397 ^ W.GetHashCode();

	public override string ToString() => "<" + X + ", " + Y + ", " + Z + ", " + W + ">";
}
