using System;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>Whether dev tracking runs in this Editor, with the one line that says why.</summary>
    public readonly struct DevTrackDecision
    {
        public DevTrackDecision(bool on, string reason)
        {
            On = on;
            Reason = reason;
        }

        public bool On { get; }

        /// <summary>What decided it, for the log line and the menu.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// The endpoint, the client version and the off switches. The three <c>Decide</c> / <c>ResolveEndpoint</c> overloads
    /// that take their inputs are pure, so the precedence is unit tested; the no-argument ones read the Editor.
    /// </summary>
    public static class DevTrackConfig
    {
        public const string LogTag = "[DevTrack]";

        /// <summary>
        /// The internal dev-play endpoint: a <c>POST</c> of one JSON event, no auth, any 2xx is success (the server
        /// answers 204). Same server as the local tracking endpoint. Overridable per machine with
        /// <see cref="EndpointEnvironmentVariable"/> or <see cref="EndpointPrefKey"/>.
        /// </summary>
        public const string DefaultEndpoint = "https://tools.puzzleclassic.com/sdk/v1/dev-play";

        /// <summary>Sent as <c>clientVersion</c>. Tracks this package's version in package.json; keep the two in step.</summary>
        public const string ClientVersion = "0.9.1";

        /// <summary><c>GAMELIB_DEVTRACK=off</c> turns dev tracking off, <c>=on</c> turns it on whatever the menu says.</summary>
        public const string OffEnvironmentVariable = "GAMELIB_DEVTRACK";

        /// <summary><c>GAMELIB_DEVTRACK_ENDPOINT=&lt;url&gt;</c> replaces <see cref="DefaultEndpoint"/>.</summary>
        public const string EndpointEnvironmentVariable = "GAMELIB_DEVTRACK_ENDPOINT";

        /// <summary>The menu toggle, <c>Tools &gt; GameLib &gt; Dev Tracking &gt; Enabled</c>. Machine-wide, on by default.</summary>
        public const string EnabledPrefKey = "GameLib.DevTrack.Enabled";

        /// <summary>An endpoint set by hand on this machine; empty means <see cref="DefaultEndpoint"/>.</summary>
        public const string EndpointPrefKey = "GameLib.DevTrack.Endpoint";

        /// <summary>How long a <c>UnityWebRequest</c> may take before it counts as a failed attempt.</summary>
        public const int RequestTimeoutSeconds = 10;

        /// <summary>The budget the play_stop on <c>EditorApplication.quitting</c> gets, in milliseconds.</summary>
        public const int QuitTimeoutMilliseconds = 1500;

        public static bool EnabledPref
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        public static DevTrackDecision Decide() =>
            Decide(Application.isBatchMode, Environment.GetEnvironmentVariable(OffEnvironmentVariable), EnabledPref);

        /// <summary>
        /// The off switches, strongest first: batch mode (CI, Unity Cloud Build) always wins, then
        /// <c>GAMELIB_DEVTRACK</c>, then the menu toggle. A value of the environment variable that is neither on nor off
        /// is ignored, so a typo cannot silently turn tracking off.
        /// </summary>
        public static DevTrackDecision Decide(bool isBatchMode, string environmentValue, bool enabledPref)
        {
            if (isBatchMode)
                return new DevTrackDecision(false, "batch mode");

            bool? fromEnvironment = ReadSwitch(environmentValue);
            if (fromEnvironment.HasValue)
                return new DevTrackDecision(fromEnvironment.Value, OffEnvironmentVariable + "=" + environmentValue.Trim());

            return enabledPref
                ? new DevTrackDecision(true, "on")
                : new DevTrackDecision(false, "GameLib > Dev Tracking > Enabled is off");
        }

        public static string Endpoint() =>
            ResolveEndpoint(Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
                            EditorPrefs.GetString(EndpointPrefKey, string.Empty),
                            DefaultEndpoint);

        /// <summary>The environment variable wins over the EditorPrefs value, which wins over the const.</summary>
        public static string ResolveEndpoint(string environmentValue, string prefValue, string defaultEndpoint)
        {
            if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue.Trim();
            if (!string.IsNullOrWhiteSpace(prefValue)) return prefValue.Trim();
            return defaultEndpoint;
        }

        /// <summary>True / false for a recognised on / off value, null for anything else (including an unset variable).</summary>
        public static bool? ReadSwitch(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            switch (value.Trim().ToLowerInvariant())
            {
                case "off":
                case "0":
                case "false":
                case "no":
                    return false;
                case "on":
                case "1":
                case "true":
                case "yes":
                    return true;
                default:
                    return null;
            }
        }
    }
}
