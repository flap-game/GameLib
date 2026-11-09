using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// Posts the queue, one <c>UnityWebRequest</c> at a time, polled from <c>EditorApplication.update</c>: no coroutine,
    /// no <c>yield</c>, nothing that blocks the main thread. <see cref="Pump"/> does at most one thing per call - start
    /// the head's request, or finish the one in flight - so a server that hangs costs the Editor nothing but the request
    /// timeout.
    /// </summary>
    public sealed class DevTrackSender : IDisposable
    {
        private UnityWebRequest _request;

        /// <summary>True while a POST is in flight.</summary>
        public bool IsSending => _request != null;

        public void Pump(DevTrackSession session, string endpoint, double now)
        {
            if (session == null || string.IsNullOrEmpty(endpoint)) return;

            if (_request != null)
            {
                if (!_request.isDone) return;

                long code = _request.responseCode;
                bool ok = code >= 200 && code <= 299;
                string detail = Describe(_request);
                _request.Dispose();
                _request = null;

                if (ok) session.Sent();
                else Warn(session.SendFailed(now, detail));
                return;
            }

            // An event without the git and machine fields is not worth a row: the probe is normally done long before
            // the first Play.
            if (!session.State.contextReady) return;

            DevTrackPending next = session.Queue.Due(now);
            if (next == null) return;

            string body = Body(next, session.State.context);
            try
            {
                _request = Post(endpoint, body);
            }
            catch (Exception e)
            {
                _request = null;
                Warn(session.SendFailed(now, e.GetType().Name));
            }
        }

        public static string Body(DevTrackPending pending, DevTrackContext context) =>
            DevTrackPayload.BuildBody(pending.ev, pending.sessionId, context, pending.durationSeconds, DevTrackConfig.ClientVersion);

        private static UnityWebRequest Post(string endpoint, string body)
        {
            var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = DevTrackPayload.ContentType },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = DevTrackConfig.RequestTimeoutSeconds,
            };
            request.SendWebRequest();
            return request;
        }

        /// <summary>
        /// The one send that cannot be polled: the play_stop on <c>EditorApplication.quitting</c>. A
        /// <c>UnityWebRequest</c> would need the Editor loop, which is already stopping, so this goes out over
        /// <c>HttpClient</c> on a worker thread and the quit waits <see cref="DevTrackConfig.QuitTimeoutMilliseconds"/>
        /// for it at most. Best effort: a failure is simply lost, as the rest of the queue is.
        /// </summary>
        public static void PostBlocking(string endpoint, string body, int timeoutMilliseconds)
        {
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(body)) return;

            try
            {
                Task task = Task.Run(async () =>
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds) };
                    using var content = new StringContent(body, Encoding.UTF8, DevTrackPayload.ContentType);
                    await client.PostAsync(endpoint, content);
                });
                task.Wait(timeoutMilliseconds);
            }
            catch (Exception)
            {
                // Quitting is not the moment to report anything.
            }
        }

        private static void Warn(string warning)
        {
            if (!string.IsNullOrEmpty(warning)) Debug.LogWarning(warning);
        }

        private static string Describe(UnityWebRequest request)
        {
            string error = string.IsNullOrEmpty(request.error) ? request.result.ToString() : request.error;
            return request.responseCode > 0 ? $"HTTP {request.responseCode}, {error}" : error;
        }

        public void Dispose()
        {
            if (_request == null) return;
            try
            {
                _request.Abort();
                _request.Dispose();
            }
            catch (Exception) { }
            _request = null;
        }
    }
}
