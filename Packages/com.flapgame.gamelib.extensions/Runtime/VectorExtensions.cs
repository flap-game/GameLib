using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class VectorExtensions
    {
        public static Vector3 WithX(this Vector3 v, float x) => new(x, v.y, v.z);
        public static Vector3 WithY(this Vector3 v, float y) => new(v.x, y, v.z);
        public static Vector3 WithZ(this Vector3 v, float z) => new(v.x, v.y, z);
        public static Vector2 WithX(this Vector2 v, float x) => new(x, v.y);
        public static Vector2 WithY(this Vector2 v, float y) => new(v.x, y);

        /// <summary>The vector on the ground plane: y = 0.</summary>
        public static Vector3 Flat(this Vector3 v) => new(v.x, 0f, v.z);

        public static Vector2 XY(this Vector3 v) => new(v.x, v.y);
        public static Vector2 XZ(this Vector3 v) => new(v.x, v.z);

        /// <summary>(x, y) -> (x, y, z).</summary>
        public static Vector3 ToVector3XY(this Vector2 v, float z = 0f) => new(v.x, v.y, z);

        /// <summary>(x, y) -> (x, height, y): a 2D point on the ground plane.</summary>
        public static Vector3 ToVector3XZ(this Vector2 v, float height = 0f) => new(v.x, height, v.y);

        /// <summary>Angle of the vector in degrees, counter-clockwise from +X: (0, 1) is 90.</summary>
        public static float AngleDegrees(this Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

        /// <summary>Angle in degrees of the direction from <paramref name="from"/> to <paramref name="to"/>, counter-clockwise from +X.</summary>
        public static float AngleDegreesTo(this Vector2 from, Vector2 to) => (to - from).AngleDegrees();

        /// <summary>The vector turned counter-clockwise by <paramref name="degrees"/>.</summary>
        public static Vector2 Rotate(this Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        /// <summary>A unit vector at <paramref name="degrees"/> counter-clockwise from +X.</summary>
        public static Vector2 DirectionFromDegrees(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// <summary>No NaN or infinity: safe to assign to a Transform.</summary>
        public static bool IsFinite(this Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);

        public static bool IsFinite(this Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public static bool IsFinite(this Quaternion q) =>
            float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);

        /// <summary>The point of the line through <paramref name="pointOnLine"/> along <paramref name="direction"/> closest to <paramref name="point"/>.</summary>
        public static Vector3 ClosestPointOnLine(this Vector3 point, Vector3 pointOnLine, Vector3 direction)
        {
            Vector3 d = direction.normalized;
            return pointOnLine + d * Vector3.Dot(point - pointOnLine, d);
        }
    }
}
