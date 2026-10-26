
namespace FlapGame.GameLib.Ads
{
    /// <summary>
    /// Entry point of a provider assembly. Register an instance with <see cref="AdsProviderCatalog.Register"/> from a
    /// <c>[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]</c> method in that assembly,
    /// so installing the SDK (which compiles the assembly via its asmdef <c>versionDefines</c>) is all a game does.
    /// </summary>
    public interface IAdsProviderFactory
    {
        /// <summary>Provider id used by routes, the plan and analytics (lowercase, see <c>AdProviderIds</c>).</summary>
        string ProviderId { get; }

        /// <summary>Human name for the inspector and logs ("AppLovin MAX").</summary>
        string DisplayName { get; }

        /// <summary>Creates the adapter. Called once per ads stack.</summary>
        IAdsProviderAdapter CreateAdapter();

        /// <summary>
        /// Priority of this SDK's consent manager (CMP) when several providers are installed; the highest non-null
        /// <see cref="CreateConsentService"/> runs the one consent flow for all SDKs. Google UMP via AdMob = 100,
        /// MAX built-in UMP = 50, providers without a CMP = 0.
        /// </summary>
        int ConsentPriority { get; }

        /// <summary>This SDK's CMP, or null when it has none.</summary>
        IConsentService CreateConsentService(AdsModule settings);
    }
}
