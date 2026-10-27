using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class MathHelper
    {
        /// <summary>Maps <paramref name="value"/> from [fromMin, fromMax] to [toMin, toMax]; outside values extrapolate. An empty source range returns toMin.</summary>
        public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            float range = fromMax - fromMin;
            if (range == 0f) return toMin;
            return toMin + (value - fromMin) * (toMax - toMin) / range;
        }

        /// <summary><see cref="Remap"/>, then kept inside [toMin, toMax].</summary>
        public static float RemapClamped(float value, float fromMin, float fromMax, float toMin, float toMax) =>
            Mathf.Clamp(Remap(value, fromMin, fromMax, toMin, toMax), Mathf.Min(toMin, toMax), Mathf.Max(toMin, toMax));

        public static bool Approximately(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

        /// <summary>
        /// <paramref name="index"/> wrapped into [0, count): <c>Wrap(current + 1, count)</c> goes from the last item back to 0,
        /// <c>Wrap(current - 1, count)</c> from 0 to the last. 0 when the list is empty.
        /// </summary>
        public static int Wrap(int index, int count)
        {
            if (count <= 0) return 0;
            int r = index % count;
            return r < 0 ? r + count : r;
        }
    }
}
