using System.Text;
using UnityEditor;
using UnityEngine;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// <c>GameLib &gt; Dev Tracking</c>: the off switch, what the state is, and one test event for checking
    /// the server end. Three items, because nothing here needs a window.
    /// </summary>
    internal static class DevTrackMenu
    {
        private const string Root = "GameLib/Dev Tracking/";
        private const string EnabledItem = Root + "Enabled";

        [MenuItem(EnabledItem, false, 100)]
        private static void ToggleEnabled()
        {
            bool enabled = !DevTrackConfig.EnabledPref;
            DevTrackConfig.EnabledPref = enabled;
            Debug.Log($"{DevTrackConfig.LogTag} {(enabled ? "On" : "Off")} from the next domain reload (entering Play, or a script change). " +
                      $"The environment variable {DevTrackConfig.OffEnvironmentVariable} still wins over this.");
        }

        [MenuItem(EnabledItem, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(EnabledItem, DevTrackConfig.EnabledPref);
            return true;
        }

        [MenuItem(Root + "Show State", false, 101)]
        private static void ShowState()
        {
            var sb = new StringBuilder(512);
            sb.Append(DevTrackConfig.LogTag).Append(' ').Append(DevTrackHook.IsOn ? "On" : "Off")
              .Append(" (").Append(DevTrackHook.Decision.Reason).Append(')');
            sb.Append("\nEndpoint: ").Append(DevTrackHook.Endpoint);
            sb.Append("\nClient version: ").Append(DevTrackConfig.ClientVersion);

            DevTrackSession session = DevTrackHook.Session;
            if (session == null)
            {
                Debug.Log(sb.ToString());
                return;
            }

            DevTrackState state = session.State;
            sb.Append("\nSession: ").Append(state.open ? $"{state.sessionId}, {session.ElapsedSeconds(EditorApplication.timeSinceStartup)} s" : "none open");
            sb.Append("\nQueue: ").Append(session.Queue.Count).Append('/').Append(session.Queue.Capacity)
              .Append(", dropped ").Append(session.Queue.DroppedCount).Append(", given up ").Append(session.Queue.GaveUpCount);
            sb.Append("\nContext: ").Append(state.contextReady ? "ready" : "still being collected");
            if (state.contextReady)
            {
                DevTrackPending sample = new DevTrackPending
                {
                    ev = DevTrackPayload.PlayStart,
                    sessionId = state.open ? state.sessionId : "<next session>",
                };
                sb.Append("\nNext play_start: ").Append(DevTrackSender.Body(sample, state.context));
            }

            Debug.Log(sb.ToString());
        }

        [MenuItem(Root + "Send Test Event", false, 102)]
        private static void SendTestEvent() => Debug.Log(DevTrackHook.SendTestEvent());
    }
}
