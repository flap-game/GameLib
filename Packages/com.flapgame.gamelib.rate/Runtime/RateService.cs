using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Rate
{
    /// <summary>
    /// <see cref="IRateService"/> with its counters in PlayerPrefs (keys <c>gamelib.rate.*</c>, kept across updates).
    /// </summary>
    public sealed class RateService : IRateService
    {
        public const string TriggersKey   = "gamelib.rate.triggers_since_prompt";
        public const string PromptsKey    = "gamelib.rate.prompts";
        public const string LastPromptKey = "gamelib.rate.last_prompt_utc";
        public const string DoneKey       = "gamelib.rate.done";

        private const string LogPrefix = "[Rate]";

        private readonly RateSettings settings;
        private readonly IReviewBackend backend;
        private readonly IAppLifecycle lifecycle;
        private readonly Func<DateTime> utcNow;
        private readonly Action<string> openUrl;
        private bool reviewing;

        /// <param name="backend">Native review sheet. Null = none (the store page is opened instead).</param>
        /// <param name="lifecycle">Marks the trip to the store as the game's own, so it is not a "return" (app open ad). Optional.</param>
        /// <param name="utcNow">Clock for <see cref="RateSettings.MinHoursBetweenPrompts"/>. Null = <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="openUrl">Opens the store page. Null = <see cref="Application.OpenURL"/>.</param>
        public RateService(RateSettings settings, IReviewBackend backend = null, IAppLifecycle lifecycle = null,
                           Func<DateTime> utcNow = null, Action<string> openUrl = null)
        {
            this.settings  = settings ?? new RateSettings();
            this.backend   = backend;
            this.lifecycle = lifecycle;
            this.utcNow    = utcNow ?? (() => DateTime.UtcNow);
            this.openUrl   = openUrl ?? Application.OpenURL;
        }

        public bool IsDone => PlayerPrefs.GetInt(DoneKey, 0) == 1;

        public int PromptCount => PlayerPrefs.GetInt(PromptsKey, 0);

        public bool NotifyTrigger()
        {
            if (settings.StopAfterRated && IsDone) return false;
            int triggers = PlayerPrefs.GetInt(TriggersKey, 0) + 1;
            PlayerPrefs.SetInt(TriggersKey, triggers);

            if (triggers < Math.Max(1, settings.PromptEveryTriggers)) return false;
            if (settings.MaxPrompts > 0 && PromptCount >= settings.MaxPrompts) return false;
            DateTime now = utcNow();
            if (settings.MinHoursBetweenPrompts > 0f && TryGetLastPrompt(out DateTime last)
                && now >= last && (now - last).TotalHours < settings.MinHoursBetweenPrompts)
                return false;

            PlayerPrefs.SetInt(TriggersKey, 0);
            PlayerPrefs.SetInt(PromptsKey, PromptCount + 1);
            PlayerPrefs.SetString(LastPromptKey, new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString());
            PlayerPrefs.Save();
            return true;
        }

        public async Task RequestReviewAsync()
        {
            MarkDone();
            if (reviewing) return;
            reviewing = true;
            // The Play review sheet is its own activity: coming back from it is not a return to the app.
            IDisposable away = lifecycle?.BeginExternalActivity("review");
            try
            {
                bool shown = false;
                if (backend != null)
                {
                    try { shown = await backend.RequestReviewAsync(); }
                    catch (Exception e) { Debug.LogWarning($"{LogPrefix} Review sheet failed: {e.Message}"); }
                }
                if (!shown) OpenStorePage();
            }
            finally
            {
                // The iOS sheet answers at once, before its resign-active reaches Unity: the timed mark covers that pause.
                lifecycle?.MarkExternalActivity("review");
                away?.Dispose();
                reviewing = false;
            }
        }

        public void OpenStorePage()
        {
            MarkDone();
            string url = StoreUrl(Application.platform, Application.identifier, settings.AppStoreId);
            if (url == null)
            {
                Debug.LogWarning($"{LogPrefix} No store page for {Application.platform} (set App Store Id on the Rate module for iOS).");
                return;
            }
            lifecycle?.MarkExternalActivity("store_page");
            openUrl(url);
        }

        public void StopPrompting() => MarkDone();

        public void ResetState()
        {
            PlayerPrefs.DeleteKey(TriggersKey);
            PlayerPrefs.DeleteKey(PromptsKey);
            PlayerPrefs.DeleteKey(LastPromptKey);
            PlayerPrefs.DeleteKey(DoneKey);
            PlayerPrefs.Save();
        }

        /// <summary>The store page that takes a review, or null (no App Store id on iOS, other platforms).</summary>
        public static string StoreUrl(RuntimePlatform platform, string bundleId, string appStoreId)
        {
            switch (platform)
            {
                case RuntimePlatform.Android:
                    // https opens the Play app when it is there, else the browser (market:// does nothing without Play).
                    return string.IsNullOrEmpty(bundleId) ? null : "https://play.google.com/store/apps/details?id=" + bundleId;
                case RuntimePlatform.IPhonePlayer:
                    string id = appStoreId?.Trim();
                    return IsAppStoreId(id) ? $"itms-apps://itunes.apple.com/app/id{id}?action=write-review" : null;
                default:
                    return null;
            }
        }

        public static bool IsAppStoreId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private void MarkDone()
        {
            if (IsDone) return;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
        }

        private static bool TryGetLastPrompt(out DateTime at)
        {
            at = default;
            if (!long.TryParse(PlayerPrefs.GetString(LastPromptKey, string.Empty), out long seconds)) return false;
            at = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            return true;
        }
    }
}
