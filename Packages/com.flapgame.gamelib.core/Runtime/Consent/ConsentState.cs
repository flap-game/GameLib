using System;

namespace FlapGame.GameLib
{
    /// <summary>
    /// Privacy state shared by every SDK. One CMP (MAX CMP, Google UMP, ...) produces it through
    /// <see cref="IConsentState.Update"/>; each module applies it the way its SDK expects
    /// (ads, Firebase consent mode, AppsFlyer DMA consent, ...).
    /// </summary>
    public sealed class ConsentState : IEquatable<ConsentState>
    {
        /// <summary>True once a CMP has reported. Before that every field holds its permissive default.</summary>
        public bool IsResolved { get; set; }

        /// <summary>True when the CMP says ads may be requested at all.</summary>
        public bool CanRequestAds { get; set; } = true;

        /// <summary>True when GDPR applies to this user (EEA/UK/CH). Unknown = false.</summary>
        public bool GdprApplies { get; set; }

        /// <summary>True when the user gave (personalised-ads) consent, or consent is not required.</summary>
        public bool HasUserConsent { get; set; } = true;

        /// <summary>US state privacy (CCPA) "do not sell". Null = not known / not applicable.</summary>
        public bool? DoNotSell { get; set; }

        /// <summary>COPPA / age-restricted user.</summary>
        public bool IsAgeRestrictedUser { get; set; }

        /// <summary>iOS App Tracking Transparency status. <see cref="AttStatus.NotApplicable"/> on other platforms.</summary>
        public AttStatus Att { get; set; } = AttStatus.NotApplicable;

        public ConsentState Clone() => (ConsentState)MemberwiseClone();

        public bool Equals(ConsentState other) =>
            other != null &&
            IsResolved == other.IsResolved &&
            CanRequestAds == other.CanRequestAds &&
            GdprApplies == other.GdprApplies &&
            HasUserConsent == other.HasUserConsent &&
            DoNotSell == other.DoNotSell &&
            IsAgeRestrictedUser == other.IsAgeRestrictedUser &&
            Att == other.Att;

        public override bool Equals(object obj) => obj is ConsentState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(IsResolved, CanRequestAds, GdprApplies, HasUserConsent, DoNotSell, IsAgeRestrictedUser, Att);

        public override string ToString() =>
            $"resolved={IsResolved} canRequest={CanRequestAds} gdpr={GdprApplies} consent={HasUserConsent} " +
            $"doNotSell={DoNotSell?.ToString() ?? "-"} ageRestricted={IsAgeRestrictedUser} att={Att}";
    }

    public enum AttStatus
    {
        NotApplicable = 0,
        NotDetermined = 1,
        Restricted = 2,
        Denied = 3,
        Authorized = 4,
    }
}
