using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlapGame.GameLib.Firebase
{
    /// <summary>
    /// The one Firebase app start every Firebase module awaits. Firebase refuses other calls while its dependency check
    /// runs, so Analytics, Crashlytics and Remote Config - which start together - share this task instead of starting
    /// their own. A start that failed is retried by the next caller.
    /// <para>The Firebase SDK code lives in <c>FlapGame.GameLib.Firebase.Sdk</c>, which compiles only once the Firebase
    /// App package is installed and then sets <see cref="Initializer"/> before the first scene loads.</para>
    /// </summary>
    public static class FirebaseStartup
    {
        /// <summary>Checks/fixes dependencies and creates the default app; throws when Firebase cannot run. Null = no SDK.</summary>
        public static Func<CancellationToken, Task> Initializer { get; set; }

        /// <summary>True when the Firebase App package is compiled into this build.</summary>
        public static bool IsSdkInstalled => Initializer != null;

        /// <summary>Completes once Firebase is usable; faults with the reason otherwise (no SDK, no config file, ...).</summary>
        public static Task EnsureReadyAsync(CancellationToken cancellationToken = default)
        {
            Func<CancellationToken, Task> initializer = Initializer;
            if (initializer == null)
                return Task.FromException(new InvalidOperationException("the Firebase App package is not installed"));
            try
            {
                return initializer(cancellationToken) ?? Task.CompletedTask;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }
    }
}
