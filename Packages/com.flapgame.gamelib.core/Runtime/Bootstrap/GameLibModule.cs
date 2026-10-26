using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib
{
    /// <summary>
    /// One GameLib module: its settings asset and its installer in one ScriptableObject. Each package ships one
    /// subclass (HapticsModule, AdsModule, ...); GameLib > Setup creates the asset and lists it in
    /// <see cref="GameLibSettings"/>. Bootstrap calls <see cref="InstallAsync"/> in ascending <see cref="Order"/>.
    /// </summary>
    public abstract class GameLibModule : ScriptableObject
    {
        [Tooltip("When on, an exception thrown by this module aborts bootstrap (fail-fast, registry disposed). " +
                 "When off (default), the error is logged with the module name and bootstrap continues with the other modules.")]
        [SerializeField] private bool required;

        /// <summary>
        /// True when a failure of this module must abort bootstrap. Default false: a throwing module is logged
        /// and skipped so one broken optional module (ads, analytics, ...) cannot take the whole registry down.
        /// </summary>
        public bool Required
        {
            get => required;
            set => required = value;
        }

        /// <summary>Install position; lower runs first. See <see cref="GameLibModuleOrder"/> for the values packages use.</summary>
        public virtual int Order => GameLibModuleOrder.Default;

        /// <summary>Registers this module's services into <paramref name="services"/>.</summary>
        public abstract ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports configuration problems (missing keys, files, ...). Shown by GameLib > Setup; errors also fail the build.
        /// </summary>
        public virtual void Validate(ICollection<GameLibIssue> issues)
        {
        }
    }

    /// <summary>Install order of the GameLib modules. Games adding their own modules can slot between these.</summary>
    public static class GameLibModuleOrder
    {
        public const int Consent = -300;
        public const int Firebase = -200;
        public const int Analytics = -100;
        public const int Default = 0;
        public const int Iap = 100;
        public const int Ads = 200;
    }

    public enum GameLibIssueSeverity
    {
        Warning = 0,
        Error = 1,
    }

    public readonly struct GameLibIssue
    {
        public GameLibIssue(GameLibIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public GameLibIssueSeverity Severity { get; }

        public string Message { get; }

        public static GameLibIssue Error(string message) => new(GameLibIssueSeverity.Error, message);

        public static GameLibIssue Warning(string message) => new(GameLibIssueSeverity.Warning, message);

        public override string ToString() => $"{Severity}: {Message}";
    }
}
