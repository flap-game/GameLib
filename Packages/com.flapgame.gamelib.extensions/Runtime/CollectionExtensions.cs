using System;
using System.Collections.Generic;

namespace FlapGame.GameLib.Extensions
{
    /// <summary>
    /// Lists, arrays and other collections. Random picks use <see cref="UnityEngine.Random"/> (main thread only); every one
    /// has an overload taking a <see cref="System.Random"/> for seeded (repeatable) results, e.g. level generation, or
    /// for use off the main thread.
    /// </summary>
    public static class CollectionExtensions
    {
        public static bool IsNullOrEmpty<T>(this ICollection<T> collection) => collection == null || collection.Count == 0;

        /// <summary>True when <paramref name="index"/> is inside the list.</summary>
        public static bool IsValidIndex<T>(this IList<T> list, int index) => list != null && index >= 0 && index < list.Count;

        /// <summary>The item at <paramref name="index"/>, or <paramref name="fallback"/> when the index is outside the list.</summary>
        public static T GetOrDefault<T>(this IList<T> list, int index, T fallback = default) =>
            list.IsValidIndex(index) ? list[index] : fallback;

        /// <summary>Adds every item of <paramref name="items"/> (null adds nothing). <c>List&lt;T&gt;</c> keeps its own AddRange.</summary>
        public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (items == null) return;
            foreach (T item in items) collection.Add(item);
        }

        /// <summary>A random item (with replacement). Throws on an empty list.</summary>
        public static T RandomItem<T>(this IList<T> list) => list[RandomIndex(list)];

        public static T RandomItem<T>(this IList<T> list, Random random) => list[RandomIndex(list, random)];

        /// <summary>A random item, or false for a null or empty list.</summary>
        public static bool TryRandomItem<T>(this IList<T> list, out T item)
        {
            if (list.IsNullOrEmpty())
            {
                item = default;
                return false;
            }
            item = list[UnityEngine.Random.Range(0, list.Count)];
            return true;
        }

        public static bool TryRandomItem<T>(this IList<T> list, Random random, out T item)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (list.IsNullOrEmpty())
            {
                item = default;
                return false;
            }
            item = list[random.Next(list.Count)];
            return true;
        }

        /// <summary>Removes a random item and returns it (without replacement). Throws on an empty list.</summary>
        public static T RemoveRandom<T>(this IList<T> list) => RemoveAtIndex(list, RandomIndex(list));

        public static T RemoveRandom<T>(this IList<T> list, Random random) => RemoveAtIndex(list, RandomIndex(list, random));

        /// <summary>Shuffles in place (Fisher-Yates).</summary>
        public static void Shuffle<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static void Shuffle<T>(this IList<T> list, Random random)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (random == null) throw new ArgumentNullException(nameof(random));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Grows (filling with <paramref name="fill"/>) or cuts the list to <paramref name="size"/> items.</summary>
        public static void Resize<T>(this List<T> list, int size, T fill = default)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            int count = list.Count;
            if (size < count)
            {
                list.RemoveRange(size, count - size);
                return;
            }
            if (size > list.Capacity) list.Capacity = size;
            for (int i = count; i < size; i++) list.Add(fill);
        }

        /// <summary>A copy of <paramref name="array"/> with <paramref name="size"/> items: cut, or grown with <paramref name="fill"/>. Null reads as empty.</summary>
        public static T[] Resized<T>(this T[] array, int size, T fill = default)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            var result = new T[size];
            int kept = array == null ? 0 : Math.Min(array.Length, size);
            if (kept > 0) Array.Copy(array, result, kept);
            for (int i = kept; i < size; i++) result[i] = fill;
            return result;
        }

        private static int RandomIndex<T>(IList<T> list)
        {
            ThrowIfEmpty(list);
            return UnityEngine.Random.Range(0, list.Count);
        }

        private static int RandomIndex<T>(IList<T> list, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            ThrowIfEmpty(list);
            return random.Next(list.Count);
        }

        private static T RemoveAtIndex<T>(IList<T> list, int index)
        {
            T item = list[index];
            list.RemoveAt(index);
            return item;
        }

        private static void ThrowIfEmpty<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new ArgumentException("The list is empty.", nameof(list));
        }
    }
}
