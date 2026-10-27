using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlapGame.GameLib.AppIcon
{
    /// <summary>
    /// Alternate app icons (iOS). The icons are listed on the <see cref="AppIconModule"/> asset; the default icon is the
    /// one in Player Settings and has the name null. Registered by <see cref="AppIconModule"/>.
    /// </summary>
    public interface IAppIconService
    {
        /// <summary>True when this device can change its icon (iOS 10.3+, and the Editor, which simulates it).</summary>
        bool IsSupported { get; }

        /// <summary>The alternate icon names, in the module's order (the default icon is not listed).</summary>
        IReadOnlyList<string> IconNames { get; }

        /// <summary>The icon in use: an alternate icon name, or null for the default icon.</summary>
        string CurrentIcon { get; }

        /// <summary>
        /// Switches to <paramref name="iconName"/> (null = the default icon). iOS shows its own "icon changed" alert.
        /// True when the icon changed (or already was that one).
        /// </summary>
        Task<bool> SetIconAsync(string iconName);
    }
}
