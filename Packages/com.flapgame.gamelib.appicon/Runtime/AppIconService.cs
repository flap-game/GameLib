using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon
{
    /// <summary><see cref="IAppIconService"/> over a platform backend.</summary>
    public sealed class AppIconService : IAppIconService
    {
        private readonly List<string> names;
        private readonly IAppIconBackend backend;
        private readonly IAppLifecycle lifecycle;
        private bool changing;

        /// <param name="backend">Null = not supported on this platform.</param>
        /// <param name="lifecycle">Marks the system "icon changed" alert as the game's own, so it is not a return (app open ad). Optional.</param>
        public AppIconService(IReadOnlyList<string> iconNames, IAppIconBackend backend, IAppLifecycle lifecycle = null)
        {
            names = new List<string>(iconNames ?? Array.Empty<string>());
            this.backend = backend;
            this.lifecycle = lifecycle;
        }

        public bool IsSupported => backend != null && backend.IsSupported;

        public IReadOnlyList<string> IconNames => names;

        public string CurrentIcon => IsSupported ? Normalize(backend.CurrentIcon) : null;

        public async Task<bool> SetIconAsync(string iconName)
        {
            iconName = Normalize(iconName);
            if (!IsSupported)
            {
                Debug.LogWarning("[AppIcon] Alternate icons are not supported on this device.");
                return false;
            }
            if (iconName != null && !names.Contains(iconName))
            {
                Debug.LogWarning($"[AppIcon] '{iconName}' is not an icon of the App Icon module.");
                return false;
            }
            if (iconName == CurrentIcon) return true;
            if (changing)
            {
                Debug.LogWarning("[AppIcon] An icon change is already running.");
                return false;
            }
            changing = true;
            IDisposable away = lifecycle?.BeginExternalActivity("app_icon");
            try
            {
                string error = await backend.SetIconAsync(iconName);
                if (error == null) return true;
                Debug.LogWarning($"[AppIcon] Icon '{iconName ?? "default"}' not set: {error}");
                return false;
            }
            finally
            {
                lifecycle?.MarkExternalActivity("app_icon"); // the alert's pause may reach Unity after the answer
                away?.Dispose();
                changing = false;
            }
        }

        private static string Normalize(string iconName) => string.IsNullOrWhiteSpace(iconName) ? null : iconName.Trim();
    }
}
