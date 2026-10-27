namespace FlapGame.GameLib.Ads
{
    /// <summary>The most mature ad content allowed (Google's <c>MaxAdContentRating</c>, digital content labels).</summary>
    public enum AdContentRating
    {
        /// <summary>No limit set here: the AdMob console's blocking controls decide.</summary>
        Unspecified = 0,
        /// <summary>General audiences.</summary>
        G = 1,
        /// <summary>Parental guidance.</summary>
        PG = 2,
        /// <summary>Teen.</summary>
        T = 3,
        /// <summary>Mature audiences.</summary>
        MA = 4,
    }
}
