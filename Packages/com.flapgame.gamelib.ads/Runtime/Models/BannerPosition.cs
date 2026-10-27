namespace FlapGame.GameLib.Ads
{
    public enum BannerPosition
    {
        Top = 0,
        Bottom = 1,
        TopLeft = 2,
        TopRight = 3,
        BottomLeft = 4,
        BottomRight = 5,
        Center = 6,
        /// <summary>Centred at <c>AdPlacement.CustomCenter</c> (a fraction of the safe area). Not for collapsible banners.</summary>
        Custom = 7,
    }
}
