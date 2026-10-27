using UnityEngine;

namespace FlapGame.GameLib.Extensions
{
    public static class NetworkHelper
    {
        /// <summary>
        /// Wi-Fi or mobile data is on. It does not prove the internet answers (a captive portal or a dead router still
        /// reads as on): use it to hide an offline-only button or show a "no connection" hint, not to decide a purchase.
        /// </summary>
        public static bool HasConnection => Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>On Wi-Fi or cable (not metered), e.g. before a large download.</summary>
        public static bool IsOnWifi => Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork;
    }
}
