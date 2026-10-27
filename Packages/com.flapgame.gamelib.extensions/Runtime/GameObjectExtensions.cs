using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component =>
            go.TryGetComponent(out T component) ? component : go.AddComponent<T>();

        public static T GetOrAddComponent<T>(this Component component) where T : Component =>
            component.gameObject.GetOrAddComponent<T>();

        /// <summary>Sets the layer of the object and every child, inactive ones included.</summary>
        public static void SetLayerRecursively(this GameObject go, int layer)
        {
            go.layer = layer;
            Transform transform = go.transform;
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetLayerRecursively(layer);
        }

        /// <summary>Activates or deactivates the direct children; the object itself is left as it is.</summary>
        public static void SetActiveChildren(this GameObject go, bool active) => go.transform.SetActiveChildren(active);

        /// <summary>
        /// Unity's "fake null" made real: a destroyed object returns null, so <c>?.</c> and <c>??</c> work.
        /// <c>target.OrNull()?.Play()</c>.
        /// </summary>
        public static T OrNull<T>(this T obj) where T : Object => obj ? obj : null;
    }
}
