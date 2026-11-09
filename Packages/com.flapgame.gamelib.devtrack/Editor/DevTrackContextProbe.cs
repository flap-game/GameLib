using System;
using System.Threading;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>
    /// Collects <see cref="DevTrackContext"/> once per Editor session. The Editor part is read on the main thread when
    /// the hook loads; <c>git</c> and the network stack go to a worker thread, and the pump picks the result up on a
    /// later frame (<see cref="TryTake"/>). Nothing here blocks the main thread and nothing throws out of it.
    /// </summary>
    internal static class DevTrackContextProbe
    {
        private static DevTrackContext _context;
        private static int _ready;
        private static int _started;

        /// <summary>True once the worker has finished (whether or not it found a git checkout).</summary>
        public static bool IsReady => Volatile.Read(ref _ready) != 0;

        /// <summary>Reads the Editor fields now and starts the worker for the rest. Safe to call more than once.</summary>
        public static void Begin()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;

            DevTrackContext context;
            try { context = DevTrackContext.FromEditor(); }
            catch (Exception) { context = new DevTrackContext(); }

            _context = context;
            string projectPath = context.projectPath;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { DevTrackContext.FillMachineAndGit(context, projectPath); }
                catch (Exception) { }
                finally { Volatile.Write(ref _ready, 1); }
            });
        }

        /// <summary>
        /// The finished context, once, on the main thread; false while the worker still runs or the result has already
        /// been taken.
        /// </summary>
        public static bool TryTake(out DevTrackContext context)
        {
            context = null;
            if (Volatile.Read(ref _ready) == 0) return false;

            context = _context;
            if (context == null) return false;

            _context = null;
            return true;
        }
    }
}
