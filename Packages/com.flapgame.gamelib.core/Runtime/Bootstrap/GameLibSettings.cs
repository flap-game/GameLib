using System.Collections.Generic;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Root GameLib asset, loaded from <c>Resources/GameLibSettings</c>. Lists the module assets bootstrap installs.
    /// Created and kept in sync by GameLib > Setup; editing the list by hand is fine.
    /// </summary>
    public sealed class GameLibSettings : ScriptableObject
    {
        public const string ResourcesPath = "GameLibSettings";

        [Tooltip("Start GameLib automatically before the first scene loads. Turn off to call GameLibBootstrap.InitializeAsync yourself " +
                 "(e.g. after your own loading screen).")]
        [SerializeField] private bool autoInitialize = true;

        [Tooltip("Log every analytics event to the Console. Editor and development builds only.")]
        [SerializeField] private bool logAnalyticsInDevelopment = true;

        [SerializeField] private List<GameLibModule> modules = new();

        public bool AutoInitialize
        {
            get => autoInitialize;
            set => autoInitialize = value;
        }

        public bool LogAnalyticsInDevelopment
        {
            get => logAnalyticsInDevelopment;
            set => logAnalyticsInDevelopment = value;
        }

        public IReadOnlyList<GameLibModule> Modules => modules;

        /// <summary>Adds <paramref name="module"/> once. Returns false when it was already listed or is null.</summary>
        public bool AddModule(GameLibModule module)
        {
            if (module == null || modules.Contains(module))
                return false;
            modules.Add(module);
            return true;
        }

        public bool RemoveModule(GameLibModule module) => modules.Remove(module);

        public static GameLibSettings Load() => Resources.Load<GameLibSettings>(ResourcesPath);
    }
}
