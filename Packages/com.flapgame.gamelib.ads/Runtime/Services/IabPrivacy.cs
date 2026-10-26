using System;
using UnityEngine;

namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Where the IAB privacy strings live. A CMP (Google UMP, MAX's flow) writes them to the platform's default store:
    /// Android default <c>SharedPreferences</c> (NOT the file Unity's PlayerPrefs uses), iOS <c>NSUserDefaults</c>.
    /// </summary>
    public interface IIabPrivacyStore
    {
        /// <summary>The value of <paramref name="key"/> as a string (ints are converted), or null when absent.</summary>
        string Get(string key);
    }

    /// <summary>What the IAB strings say. Null = the string is absent (no CMP ran, or not applicable).</summary>
    public readonly struct IabPrivacySignals
    {
        public readonly bool? GdprApplies;
        public readonly bool? PersonalisedAdsConsent;
        public readonly bool? DoNotSell;

        public IabPrivacySignals(bool? gdprApplies, bool? personalisedAdsConsent, bool? doNotSell)
        {
            GdprApplies            = gdprApplies;
            PersonalisedAdsConsent = personalisedAdsConsent;
            DoNotSell              = doNotSell;
        }
    }

    /// <summary>
    /// Reads the IAB TCF v2 / US Privacy strings a CMP wrote, and maps a consent platform's answer onto the
    /// <see cref="ConsentState"/> handed to every adapter. A CMP status alone cannot tell consent from refusal (UMP
    /// reports Obtained and CanRequestAds after "Do not consent"), so the purposes the user accepted are read from
    /// <c>IABTCF_PurposeConsents</c>.
    /// </summary>
    public static class IabPrivacy
    {
        public const string KeyGdprApplies     = "IABTCF_gdprApplies";
        public const string KeyPurposeConsents = "IABTCF_PurposeConsents";
        public const string KeyUsPrivacy       = "IABUSPrivacy_String";

        // TCF purposes a personalised ad needs: 1 store/access information, 3 create ads profile, 4 use ads profile.
        private static readonly int[] PersonalisedAdsPurposes = { 1, 3, 4 };

        private static IIabPrivacyStore platformStore;

        /// <summary>The store of the running platform (Android default SharedPreferences, PlayerPrefs elsewhere).</summary>
        public static IIabPrivacyStore PlatformStore => platformStore ??= CreatePlatformStore();

        /// <summary>Reads the signals from <paramref name="store"/> (null = <see cref="PlatformStore"/>). Never throws.</summary>
        public static IabPrivacySignals Read(IIabPrivacyStore store = null)
        {
            store ??= PlatformStore;
            string gdpr, purposes, usp;
            try
            {
                gdpr     = store.Get(KeyGdprApplies);
                purposes = store.Get(KeyPurposeConsents);
                usp      = store.Get(KeyUsPrivacy);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Consent] Reading the IAB privacy strings failed: {e.Message}");
                return default;
            }
            return Parse(gdpr, purposes, usp);
        }

        /// <summary>Parses raw values (null = absent). Exposed for tests.</summary>
        public static IabPrivacySignals Parse(string gdprApplies, string purposeConsents, string usPrivacy)
        {
            bool? gdpr = gdprApplies?.Trim() switch
            {
                "1" => true,
                "0" => false,
                _   => null,
            };

            bool? personalised = null;
            if (purposeConsents != null)
            {
                string bits = purposeConsents.Trim();
                personalised = true;
                foreach (int purpose in PersonalisedAdsPurposes)
                    if (bits.Length < purpose || bits[purpose - 1] != '1') personalised = false;
            }

            // US Privacy string "1YNN": version, notice given, opted out of sale, LSPA. '-' = not applicable.
            bool? doNotSell = null;
            if (usPrivacy != null && usPrivacy.Length >= 3 && usPrivacy[0] == '1')
            {
                char optOut = char.ToUpperInvariant(usPrivacy[2]);
                if (optOut == 'Y') doNotSell = true;
                else if (optOut == 'N') doNotSell = false;
            }
            return new IabPrivacySignals(gdpr, personalised, doNotSell);
        }

        /// <summary>
        /// The state handed to every adapter. GDPR applies when the TCF string says so (else when the CMP asked for
        /// consent); where it applies, consent = TCF purposes 1, 3 and 4 accepted, and NO consent when the string is
        /// missing. Outside GDPR consent is true. Unknown status with no TCF string = no consent (nothing was asked yet).
        /// </summary>
        public static ConsentState Map(ConsentStatus status, bool canRequestAds, IIabPrivacyStore store = null) =>
            Map(status, canRequestAds, Read(store));

        /// <inheritdoc cref="Map(ConsentStatus, bool, IIabPrivacyStore)"/>
        public static ConsentState Map(ConsentStatus status, bool canRequestAds, IabPrivacySignals signals)
        {
            bool gdprApplies = signals.GdprApplies
                               ?? (status == ConsentStatus.Required || status == ConsentStatus.Obtained);
            bool hasConsent;
            if (gdprApplies) hasConsent = signals.PersonalisedAdsConsent ?? false;
            else if (signals.GdprApplies == false || status == ConsentStatus.NotRequired) hasConsent = true;
            else hasConsent = false; // Unknown, nothing stored: never claim a consent nobody gave.

            return new ConsentState
            {
                CanRequestAds  = canRequestAds,
                GdprApplies    = gdprApplies,
                HasUserConsent = hasConsent,
                DoNotSell      = signals.DoNotSell,
            };
        }

        private static IIabPrivacyStore CreatePlatformStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidDefaultPreferencesStore();
#else
            return new PlayerPrefsStore();
#endif
        }

        /// <summary>iOS (NSUserDefaults standard defaults = PlayerPrefs) and the Editor.</summary>
        private sealed class PlayerPrefsStore : IIabPrivacyStore
        {
            public string Get(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return null;
                // TCF ints are stored as numbers, the strings as strings; PlayerPrefs returns the default on a type mismatch.
                string s = PlayerPrefs.GetString(key, null);
                if (!string.IsNullOrEmpty(s)) return s;
                return PlayerPrefs.GetInt(key, int.MinValue) is var i && i != int.MinValue ? i.ToString() : null;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>The app's default SharedPreferences (<c>&lt;package&gt;_preferences</c>), where CMPs write TCF.</summary>
        private sealed class AndroidDefaultPreferencesStore : IIabPrivacyStore
        {
            public string Get(string key)
            {
                using var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                string name = activity.Call<string>("getPackageName") + "_preferences";
                using var prefs = activity.Call<AndroidJavaObject>("getSharedPreferences", name, 0);
                if (!prefs.Call<bool>("contains", key)) return null;
                using var all = prefs.Call<AndroidJavaObject>("getAll");
                using var value = all.Call<AndroidJavaObject>("get", key);
                return value?.Call<string>("toString");
            }
        }
#endif
    }
}
