namespace Pumpkin.DotNet.Api.Math;

using System;

public readonly record struct Vector3(double X, double Y, double Z)
{
    public static readonly Vector3 Zero = new(0, 0, 0);
    public static readonly Vector3 One = new(1, 1, 1);
    public static readonly Vector3 Up = new(0, 1, 0);
    public static readonly Vector3 Down = new(0, -1, 0);
    public static readonly Vector3 North = new(0, 0, -1);
    public static readonly Vector3 South = new(0, 0, 1);
    public static readonly Vector3 East = new(1, 0, 0);
    public static readonly Vector3 West = new(-1, 0, 0);

    public int BlockX => (int)Math.Floor(X);
    public int BlockY => (int)Math.Floor(Y);
    public int BlockZ => (int)Math.Floor(Z);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vector3 Normalize()
    {
        double len = Length;
        return len > 1e-9 ? new Vector3(X / len, Y / len, Z / len) : Zero;
    }

    public Vector3 Add(double x, double y, double z) => new(X + x, Y + y, Z + z);
    public Vector3 Subtract(double x, double y, double z) => new(X - x, Y - y, Z - z);

    public double DistanceSquared(Vector3 other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        double dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public double Distance(Vector3 other) => Math.Sqrt(DistanceSquared(other));

    public double Dot(Vector3 other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vector3 Cross(Vector3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X
    );

    public Vector3 ToBlockCenter() => new(Math.Floor(X) + 0.5, Math.Floor(Y), Math.Floor(Z) + 0.5);

    // --- Operators ---
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 v) => new(-v.X, -v.Y, -v.Z);
    public static Vector3 operator *(Vector3 v, double scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vector3 operator *(double scalar, Vector3 v) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vector3 operator /(Vector3 v, double scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    // --- Implicit conversion from a tuple (useful for WIT pos3d) ---
    public static implicit operator Vector3((double X, double Y, double Z) tuple) => new(tuple.X, tuple.Y, tuple.Z);
    public static implicit operator (double X, double Y, double Z)(Vector3 v) => (v.X, v.Y, v.Z);

    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
}