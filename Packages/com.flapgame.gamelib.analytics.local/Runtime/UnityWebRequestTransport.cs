using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FlapGame.GameLib.Analytics.Local
{
    /// <summary>
    /// The default transport: one <c>UnityWebRequest</c> POST per event, awaited without a coroutine. Must be called from
    /// the main thread (as <see cref="LocalAnalyticsSink"/> does - its loop starts on the bootstrap thread and
    /// <c>Task.Delay</c> continuations stay on Unity's context).
    /// </summary>
    public sealed class UnityWebRequestTransport : ILocalTrackingTransport
    {
        public const string ContentType = "application/json";

        public async Task<bool> PostAsync(string endpoint, string body, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? string.Empty)) { contentType = ContentType },
                downloadHandler = new DownloadHandlerBuffer(),
            };
            if (timeout > TimeSpan.Zero) request.timeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            if (operation.isDone) completion.TrySetResult(true);
            else operation.completed += _ => completion.TrySetResult(true);

            // Shutdown must not wait for the server: abort the request and let the loop see the cancellation.
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try { request.Abort(); }
                catch (Exception) { }
                completion.TrySetResult(true);
            });

            try { await completion.Task; }
            finally { registration.Dispose(); }

            cancellationToken.ThrowIfCancellationRequested();

            long code = request.responseCode;
            if (code >= 200 && code <= 299) return true;

            Debug.LogWarning($"{LocalAnalyticsModule.LogTag} POST {endpoint} failed: {Describe(request)}.");
            return false;
        }

        private static string Describe(UnityWebRequest request)
        {
            string error = string.IsNullOrEmpty(request.error) ? request.result.ToString() : request.error;
            return request.responseCode > 0 ? $"HTTP {request.responseCode} ({error})" : error;
        }
    }
}
