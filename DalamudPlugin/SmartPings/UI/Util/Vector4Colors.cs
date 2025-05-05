using System.Numerics;

namespace SmartPings.UI.Util
{
    public sealed class Vector4Colors
    {
        public static Vector4 Red => new(1, 0, 0, 1);
        public static Vector4 Green => new(0, 1, 0, 1);
        public static Vector4 Blue => new(0, 0, 1, 1);
        public static Vector4 Orange => new(1, 0.65f, 0, 1);
        public static Vector4 White => new(1, 1, 1, 1);
        public static Vector4 Gray => new(0.2f, 0.2f, 0.2f, 1);
        public static Vector4 Black => new(0, 0, 0, 1);
    }
}