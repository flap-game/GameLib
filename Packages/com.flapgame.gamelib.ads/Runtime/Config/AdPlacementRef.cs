using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>Drag-drop handle for a placement id. Optionally points at the settings asset so a typo is caught in the Editor.</summary>
    [CreateAssetMenu(menuName = "Game/GameLib/Ad Placement", fileName = "AdPlacement")]
    public sealed class AdPlacementRef : ScriptableObject
    {
        [SerializeField] private string id;
        [Tooltip("Optional. When set, OnValidate warns if Id is not one of its Placements.")]
        [SerializeField] private AdsModule settings;

        public string Id => id;

        /// <summary>True when the id is non-empty and, if a settings asset is linked, exists in its Placements.</summary>
        public bool IsValid => settings == null ? !string.IsNullOrEmpty(id) : settings.TryGet(id, out _);

        public static implicit operator string(AdPlacementRef placement) =>
            placement != null ? placement.id : string.Empty;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
                Debug.LogWarning($"[Ads] AdPlacementRef '{name}' has an empty Id.", this);
            else if (settings != null && !settings.TryGet(id, out _))
                Debug.LogWarning($"[Ads] AdPlacementRef '{name}': id '{id}' is not in {settings.name}.Placements.", this);
        }
#endif
    }
}
