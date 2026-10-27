using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Analytics.Firebase
{
    /// <summary>
    /// The Firebase Analytics calls GameLib makes, behind an interface so the sink logic is testable without the SDK.
    /// The real one lives in <c>FlapGame.GameLib.Analytics.Firebase.Sdk</c> and registers itself in
    /// <see cref="FirebaseAnalyticsBackend.Current"/> when the Firebase Analytics package is installed.
    /// Every input is already sanitized (<see cref="FirebaseAnalyticsSanitizer"/>).
    /// </summary>
    public interface IFirebaseAnalyticsBackend
    {
        /// <summary>Starts Firebase (shared start) and enables collection; throws when Firebase cannot run.</summary>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>Values are string, long, double or a sequence of item dictionaries.</summary>
        void LogEvent(string name, IReadOnlyDictionary<string, object> parameters);

        void SetUserProperty(string name, string value);

        void SetUserId(string userId);

        void SetConsent(FirebaseConsent consent);

        /// <summary>The app instance id (GA4 <c>user_pseudo_id</c>); null when Firebase has none (analytics storage denied).</summary>
        Task<string> GetAppInstanceIdAsync();
    }

    public static class FirebaseAnalyticsBackend
    {
        /// <summary>Set before the first scene loads when the SDK is compiled in; null otherwise.</summary>
        public static IFirebaseAnalyticsBackend Current { get; set; }
    }

    /// <summary>Google consent mode v2 signals (true = granted).</summary>
    public readonly struct FirebaseConsent
    {
        public FirebaseConsent(bool analyticsStorage, bool adStorage, bool adUserData, bool adPersonalization)
        {
            AnalyticsStorage = analyticsStorage;
            AdStorage = adStorage;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
        }

        public bool AnalyticsStorage { get; }

        public bool AdStorage { get; }

        public bool AdUserData { get; }

        public bool AdPersonalization { get; }

        /// <summary>
        /// Consent mode from the shared CMP answer; null while no CMP has reported (Firebase keeps the defaults set in
        /// the app manifest / Info.plist). Where GDPR applies everything follows the user's consent; elsewhere storage is
        /// granted and a CCPA "do not sell" or a child user denies ad user data and personalization.
        /// </summary>
        public static FirebaseConsent? From(ConsentState state)
        {
            if (state == null || !state.IsResolved) return null;
            if (state.GdprApplies)
            {
                bool granted = state.HasUserConsent;
                return new FirebaseConsent(granted, granted, granted, granted && !state.IsAgeRestrictedUser);
            }

            bool adsAllowed = state.DoNotSell != true && !state.IsAgeRestrictedUser;
            return new FirebaseConsent(true, true, adsAllowed, adsAllowed);
        }

        public override string ToString() =>
            $"analytics_storage={AnalyticsStorage} ad_storage={AdStorage} ad_user_data={AdUserData} ad_personalization={AdPersonalization}";
    }
}
