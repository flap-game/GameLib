using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlapGame.GameLib.Extensions
{
    public static class TransformExtensions
    {
        public static void SetPositionX(this Transform t, float x)
        {
            Vector3 p = t.position;
            p.x = x;
            t.position = p;
        }

        public static void SetPositionY(this Transform t, float y)
        {
            Vector3 p = t.position;
            p.y = y;
            t.position = p;
        }

        public static void SetPositionZ(this Transform t, float z)
        {
            Vector3 p = t.position;
            p.z = z;
            t.position = p;
        }

        public static void SetLocalPositionX(this Transform t, float x)
        {
            Vector3 p = t.localPosition;
            p.x = x;
            t.localPosition = p;
        }

        public static void SetLocalPositionY(this Transform t, float y)
        {
            Vector3 p = t.localPosition;
            p.y = y;
            t.localPosition = p;
        }

        public static void SetLocalPositionZ(this Transform t, float z)
        {
            Vector3 p = t.localPosition;
            p.z = z;
            t.localPosition = p;
        }

        /// <summary>Same scale on every axis.</summary>
        public static void SetLocalScale(this Transform t, float scale) => t.localScale = new Vector3(scale, scale, scale);

        /// <summary>Local position 0, no rotation, scale 1.</summary>
        public static void ResetLocal(this Transform t)
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        /// <summary>
        /// Destroys every child: <c>Destroy</c> in Play mode, <c>DestroyImmediate</c> in Edit mode.
        /// In Play mode the children are gone at the end of the frame, so <c>childCount</c> and <c>GetChild</c> still see
        /// them until then: count the new children you add, not <c>childCount</c>. (Not detached first: changing the parent
        /// fails while the hierarchy is being activated, e.g. a list refilled in OnEnable.)
        /// </summary>
        public static void DestroyChildren(this Transform t)
        {
            bool playing = Application.isPlaying;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                if (playing) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }
        }

        /// <summary>Activates or deactivates the direct children; the parent itself is left as it is.</summary>
        public static void SetActiveChildren(this Transform t, bool active)
        {
            for (int i = 0; i < t.childCount; i++) t.GetChild(i).gameObject.SetActive(active);
        }

        /// <summary>Number of direct children whose own active flag is on (<c>activeSelf</c>).</summary>
        public static int ActiveChildCount(this Transform t)
        {
            int count = 0;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) count++;
            return count;
        }

        /// <summary>
        /// The first descendant (any depth, inactive ones included, depth-first in hierarchy order) named <paramref name="name"/>.
        /// Case-insensitive by default. Null when there is none. Unlike <c>Transform.Find</c>, no path: just the name.
        /// </summary>
        public static Transform FindDeep(this Transform t, string name, bool ignoreCase = true) =>
            FindDeep(t, name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, NameMatch.Exact);

        /// <summary>The first descendant whose name starts with <paramref name="prefix"/> (case-insensitive by default).</summary>
        public static Transform FindDeepStartingWith(this Transform t, string prefix, bool ignoreCase = true) =>
            FindDeep(t, prefix, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, NameMatch.StartsWith);

        /// <summary>The first descendant whose name contains <paramref name="part"/> (case-insensitive by default).</summary>
        public static Transform FindDeepContaining(this Transform t, string part, bool ignoreCase = true) =>
            FindDeep(t, part, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, NameMatch.Contains);

        private enum NameMatch
        {
            Exact,
            StartsWith,
            Contains,
        }

        private static Transform FindDeep(Transform parent, string text, StringComparison comparison, NameMatch match)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string name = child.name;
                bool hit = match switch
                {
                    NameMatch.Exact => string.Equals(name, text, comparison),
                    NameMatch.StartsWith => name.StartsWith(text, comparison),
                    _ => name.IndexOf(text, comparison) >= 0,
                };
                if (hit) return child;
                Transform found = FindDeep(child, text, comparison, match);
                if (found != null) return found;
            }
            return null;
        }
    }
}
