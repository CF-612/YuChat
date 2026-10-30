using System;

namespace LingChatXR.Core
{
    /// <summary>Position in meters. Unity conversion belongs to the scene adapter.</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object value) => value is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z}) m";
    }
}
