using System;
using System.Collections.Generic;

namespace FlapGame.GameLib.Extensions
{
    public static class RandomHelper
    {
        /// <summary>True with probability <paramref name="probability"/> (0..1): <c>Chance(0.25f)</c> is true one time in four.</summary>
        public static bool Chance(float probability) => UnityEngine.Random.value < probability;

        public static bool Chance(float probability, Random random) =>
            (random ?? throw new ArgumentNullException(nameof(random))).NextDouble() < probability;

        /// <summary>
        /// A random index, each one picked in proportion to its weight. Negative weights count as 0.
        /// Returns -1 when the list is empty or every weight is 0.
        /// </summary>
        public static int WeightedIndex(IList<float> weights) => WeightedIndex(weights, UnityEngine.Random.value);

        public static int WeightedIndex(IList<float> weights, Random random) =>
            WeightedIndex(weights, (float)(random ?? throw new ArgumentNullException(nameof(random))).NextDouble());

        /// <summary>The item picked in proportion to its weight. Throws when the lists differ in size or no weight is above 0.</summary>
        public static T WeightedItem<T>(IList<T> items, IList<float> weights) => PickWeighted(items, weights, null);

        public static T WeightedItem<T>(IList<T> items, IList<float> weights, Random random) =>
            PickWeighted(items, weights, random ?? throw new ArgumentNullException(nameof(random)));

        private static T PickWeighted<T>(IList<T> items, IList<float> weights, Random random)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (weights == null || weights.Count != items.Count)
                throw new ArgumentException("There must be one weight per item.", nameof(weights));
            int index = random == null ? WeightedIndex(weights) : WeightedIndex(weights, random);
            if (index < 0) throw new ArgumentException("No weight is above 0.", nameof(weights));
            return items[index];
        }

        /// <param name="roll">A value in [0, 1).</param>
        private static int WeightedIndex(IList<float> weights, float roll)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0f) total += weights[i];
            if (total <= 0f) return -1;

            float target = roll * total;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                last = i;
                target -= weights[i];
                if (target < 0f) return i;
            }
            return last; // float rounding left a remainder: the last item with a weight
        }
    }
}
