using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>One queued event, in the shape the tracking server reads it.</summary>
    public sealed class LocalTrackingEvent
    {
        public LocalTrackingEvent(string name, IDictionary<string, object> parameters)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parameters = parameters;
        }

        public string Name { get; }

        /// <summary>The event's own parameters plus <c>eventCount</c> (the sequence number of this session).</summary>
        public IDictionary<string, object> Parameters { get; }
    }

    /// <summary>How the events leave the device. Swapped in the tests for a fake that never touches the network.</summary>
    public interface ILocalTrackingTransport
    {
        /// <summary>POSTs <paramref name="body"/> as <c>application/json</c>. True only on a 2xx answer; false retries.</summary>
        Task<bool> PostAsync(string endpoint, string body, TimeSpan timeout, CancellationToken cancellationToken);
    }

    /// <summary>The id the server joins the rows of one device on (the advertising id, where the platform still has one).</summary>
    public interface ILocalDeviceIdSource
    {
        Task<string> GetAsync(CancellationToken cancellationToken);
    }

    /// <summary>What <see cref="LocalAnalyticsModule"/> hands to <see cref="LocalAnalyticsSink"/>.</summary>
    public sealed class LocalTrackingOptions
    {
        public string Endpoint { get; set; }

        /// <summary><c>Application.identifier</c>, read on the main thread while the module installs.</summary>
        public string BundleId { get; set; }

        /// <summary><c>Application.version</c>, read on the main thread while the module installs.</summary>
        public string AppVersion { get; set; }

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Wait between two posts after a failed one, and between two polls of an empty queue.</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Failed posts in a row before local tracking gives up for the session (aframework: 10).</summary>
        public int MaxConsecutiveFailures { get; set; } = 10;

        /// <summary>Events held while the server is slow or unreachable; the oldest is dropped when it is full.</summary>
        public int MaxQueuedEvents { get; set; } = 500;

        /// <summary>How long iOS may take to answer the ATT prompt before local tracking gives up (no IDFA, no events).</summary>
        public TimeSpan AttWait { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// True where there is no advertising id without ATT consent (iOS), so a denied prompt turns local tracking off.
        /// Set by <see cref="LocalAnalyticsModule"/> from the running platform.
        /// </summary>
        public bool RequireAttConsent { get; set; }
    }
}
