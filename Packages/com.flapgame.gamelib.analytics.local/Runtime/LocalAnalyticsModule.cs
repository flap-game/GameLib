using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// Local tracking for the test devices: on the office LAN (or in the Editor with <see cref="TrackInEditor"/> on) every
    /// tracked event is posted to the internal tracking server, and the online analytics sinks stay off, so a test session
    /// never shows up in the production dashboards. On a player's device nothing matches and the module does nothing -
    /// it can stay in a shipped build.
    /// <para>Installs before <see cref="GameLibModuleOrder.Analytics"/> and registers <see cref="ILocalTracking"/>, which
    /// the online sinks ask (<see cref="LocalTracking.SuppressesOnline"/>) before installing themselves.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "GameLib/Modules/Local Analytics", fileName = "LocalAnalyticsModule")]
    public sealed class LocalAnalyticsModule : GameLibModule, ILocalTracking
    {
        public const string LogTag = "[LocalAnalytics]";

        /// <summary>The internal tracking server aframework posts to.</summary>
        public const string DefaultEndpoint = "https://tools.puzzleclassic.com/sdk/v1/tracking";

        /// <summary>The office LAN aframework recognizes its test devices by.</summary>
        public const string DefaultLanAddressPrefix = "192.25.20";

        [Tooltip("The tracking server every event is POSTed to as JSON.")]
        public string Endpoint = DefaultEndpoint;

        [Tooltip("IPv4 prefixes of the networks local tracking runs on. A device whose own address starts with one of " +
                 "these is a test device; anything else (a player's phone) leaves the module off.")]
        public string[] LanAddressPrefixes = { DefaultLanAddressPrefix };

        [Tooltip("Also track from the Editor, where there is no LAN check. Off by default so Play mode does not post.")]
        public bool TrackInEditor;

        [Tooltip("While local tracking is active, keep the online analytics sinks (Firebase Analytics, ...) off - the " +
                 "aframework behaviour. Turn off to send to both.")]
        public bool SuppressOnlineTracking = true;

        [Tooltip("Also keep the attribution SDKs (AppsFlyer) off. Off by default, as in aframework: a test install still " +
                 "has to attribute like a real one.")]
        public bool SuppressAttribution;

        [Tooltip("Seconds one POST may take.")]
        [Min(1f)] public float RequestTimeoutSeconds = 10f;

        [Tooltip("Seconds between a failed post and the retry (also how often an empty queue is checked).")]
        [Min(0.1f)] public float RetryDelaySeconds = 1f;

        [Tooltip("Failed posts in a row before local tracking gives up for the session.")]
        [Min(1)] public int MaxConsecutiveFailures = 10;

        [Tooltip("Events held while the server is slow or unreachable; the oldest is dropped when the queue is full.")]
        [Min(1)] public int MaxQueuedEvents = 500;

        [Tooltip("Seconds iOS may take to answer the ATT prompt. Without consent there is no IDFA and local tracking " +
                 "stays off for the session (0 = wait forever).")]
        [Min(0f)] public float AttWaitSeconds = 60f;

        /// <summary>Just before <see cref="GameLibModuleOrder.Analytics"/>, so the online sinks can ask whether to install.</summary>
        public override int Order => GameLibModuleOrder.Analytics - 1;

        /// <summary>True once local tracking took over this session; see <see cref="ILocalTracking.IsActive"/>.</summary>
        public bool IsActive { get; private set; }

        public bool SuppressesOnlineTracking => SuppressOnlineTracking;

        public bool SuppressesAttribution => SuppressAttribution;

        /// <summary>The sink of the running session, or null when local tracking is off. For tests and debug screens.</summary>
        public LocalAnalyticsSink Sink { get; private set; }

        public override ValueTask InstallAsync(IServiceRegistry services, CancellationToken cancellationToken = default)
        {
            IsActive = false;
            Sink = null;

            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                Debug.LogError($"{LogTag} Endpoint is empty on the Local Analytics module - local tracking is off.");
                return default;
            }

            if (!LocalTrackingGate.IsLocalDevice(TrackInEditor, LanAddressPrefixes, out string reason))
            {
                // The normal case in a shipped build; a log line, not a warning.
                Debug.Log($"{LogTag} Off: {reason}.");
                return default;
            }

            var options = new LocalTrackingOptions
            {
                Endpoint = Endpoint.Trim(),
                BundleId = Application.identifier,
                AppVersion = Application.version,
                RequestTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
                RetryDelay = TimeSpan.FromSeconds(RetryDelaySeconds),
                MaxConsecutiveFailures = MaxConsecutiveFailures,
                MaxQueuedEvents = MaxQueuedEvents,
                AttWait = AttWaitSeconds > 0f ? TimeSpan.FromSeconds(AttWaitSeconds) : Timeout.InfiniteTimeSpan,
                RequireAttConsent = Application.platform == RuntimePlatform.IPhonePlayer,
            };

            var sink = new LocalAnalyticsSink(options);
            Sink = sink;
            IsActive = true;
            services.Register<ILocalTracking>(this);
            services.Resolve<IAnalyticsSinkRegistry>().AddSink(sink);

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            services.RegisterDisposable(Disposable.Create(() =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }));

            services.TryResolve(out IAttService att);
            // Resolves the device id and then drains the queue for the rest of the session; never throws.
            _ = sink.RunAsync(att, cancellation.Token);

            string online = SuppressOnlineTracking ? "; the online analytics sinks stay off" : "; the online sinks stay on too";
            Debug.Log($"{LogTag} On ({reason}) -> {options.Endpoint}{online}.");
            return default;
        }

        public override void Validate(ICollection<GameLibIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                issues.Add(GameLibIssue.Error("Local Analytics: Endpoint is empty - no events can be posted."));
            }
            else if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                issues.Add(GameLibIssue.Error($"Local Analytics: Endpoint '{Endpoint}' is not an http(s) URL."));
            }

            if (CountPrefixes() == 0 && !TrackInEditor)
            {
                issues.Add(GameLibIssue.Warning("Local Analytics: no LAN Address Prefixes and Track In Editor is off - the module can never turn on."));
            }
        }

        private int CountPrefixes()
        {
            if (LanAddressPrefixes == null) return 0;
            int count = 0;
            foreach (string prefix in LanAddressPrefixes)
            {
                if (!string.IsNullOrWhiteSpace(prefix)) count++;
            }
            return count;
        }
    }
}
