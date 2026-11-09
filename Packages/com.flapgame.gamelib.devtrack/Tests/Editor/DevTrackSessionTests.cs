using FlapGame.GameLib.DevTrack;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class DevTrackSessionTests
    {
        /// <summary>Stands in for <c>UnityEditor.SessionState</c>: the same "survives a reload, keeps JSON" contract.</summary>
        private sealed class MemoryStateStore : IDevTrackStateStore
        {
            public string Json;

            public string Read() => Json;

            public void Write(string json) => Json = json;
        }

        [Test]
        public void Start_Heartbeat_Stop_AreTheThreeEvents()
        {
            var store = new MemoryStateStore();
            var session = new DevTrackSession(store);

            session.PlayStarted("s-1", 100);
            Assert.That(session.IsOpen, Is.True);

            Assert.That(session.Heartbeat(100 + DevTrackSession.HeartbeatSeconds - 1), Is.Null, "not before the minute is up");
            DevTrackPending beat = session.Heartbeat(100 + DevTrackSession.HeartbeatSeconds);
            Assert.That(beat, Is.Not.Null);
            Assert.That(beat.durationSeconds, Is.EqualTo(60));

            DevTrackPending stop = session.PlayStopped(100 + 95);
            Assert.That(stop.durationSeconds, Is.EqualTo(95));
            Assert.That(session.IsOpen, Is.False);

            Assert.That(session.Queue.Count, Is.EqualTo(3));
            Assert.That(session.Queue.Items[0].ev, Is.EqualTo(DevTrackPayload.PlayStart));
            Assert.That(session.Queue.Items[1].ev, Is.EqualTo(DevTrackPayload.Heartbeat));
            Assert.That(session.Queue.Items[2].ev, Is.EqualTo(DevTrackPayload.PlayStop));
            Assert.That(session.Queue.Items[2].sessionId, Is.EqualTo("s-1"));
        }

        [Test]
        public void TheSecondHeartbeat_WaitsAnotherMinute()
        {
            var session = new DevTrackSession(new MemoryStateStore());
            session.PlayStarted("s-1", 0);

            Assert.That(session.Heartbeat(60), Is.Not.Null);
            Assert.That(session.Heartbeat(100), Is.Null);
            Assert.That(session.Heartbeat(120).durationSeconds, Is.EqualTo(120));
        }

        [Test]
        public void ADomainReloadInTheMiddle_KeepsTheSessionAndTheQueue()
        {
            var store = new MemoryStateStore();

            var before = new DevTrackSession(store);
            before.PlayStarted("s-1", 100);
            before.Heartbeat(160);

            // Entering Play, and a script change while playing, both reload the domain: the statics go, the store stays.
            var after = new DevTrackSession(store);

            Assert.That(after.IsOpen, Is.True);
            Assert.That(after.State.sessionId, Is.EqualTo("s-1"), "the same session, not a second one");
            Assert.That(after.Queue.Count, Is.EqualTo(2), "the queued events are still there");
            Assert.That(after.ElapsedSeconds(400), Is.EqualTo(300), "the start time survived");

            Assert.That(after.Heartbeat(200), Is.Null, "the last heartbeat survived too");
            Assert.That(after.Heartbeat(220).durationSeconds, Is.EqualTo(120));

            DevTrackPending stop = after.PlayStopped(300);
            Assert.That(stop.sessionId, Is.EqualTo("s-1"));
            Assert.That(stop.durationSeconds, Is.EqualTo(200));
        }

        [Test]
        public void StopWithoutStart_ReportsNothing()
        {
            var session = new DevTrackSession(new MemoryStateStore());

            Assert.That(session.PlayStopped(50), Is.Null);
            Assert.That(session.Queue.Count, Is.Zero);
        }

        [Test]
        public void HeartbeatWithoutStart_ReportsNothing()
        {
            var session = new DevTrackSession(new MemoryStateStore());

            Assert.That(session.Heartbeat(1000), Is.Null);
            Assert.That(session.Queue.Count, Is.Zero);
        }

        [Test]
        public void AStartWhileASessionIsStillOpen_ClosesTheOldOneFirst()
        {
            var session = new DevTrackSession(new MemoryStateStore());
            session.PlayStarted("s-1", 0);

            session.PlayStarted("s-2", 100);

            Assert.That(session.Queue.Items[0].ev, Is.EqualTo(DevTrackPayload.PlayStart));
            Assert.That(session.Queue.Items[0].sessionId, Is.EqualTo("s-1"));
            Assert.That(session.Queue.Items[1].ev, Is.EqualTo(DevTrackPayload.PlayStop));
            Assert.That(session.Queue.Items[1].sessionId, Is.EqualTo("s-1"));
            Assert.That(session.Queue.Items[2].ev, Is.EqualTo(DevTrackPayload.PlayStart));
            Assert.That(session.Queue.Items[2].sessionId, Is.EqualTo("s-2"));
            Assert.That(session.State.sessionId, Is.EqualTo("s-2"));
        }

        [Test]
        public void AnEmptySessionId_BecomesAGuid()
        {
            var session = new DevTrackSession(new MemoryStateStore());

            session.PlayStarted(null, 0);

            Assert.That(session.State.sessionId, Is.Not.Empty);
            Assert.That(session.State.sessionId.Length, Is.EqualTo(36));
        }

        [Test]
        public void OnlyTheFirstGiveUp_GetsAWarning()
        {
            var session = new DevTrackSession(new MemoryStateStore(), maxAttempts: 1);
            session.PlayStarted("s-1", 0);
            session.Heartbeat(60);

            Assert.That(session.SendFailed(0, "HTTP 500"), Is.Not.Null, "one warning per Editor session");
            Assert.That(session.SendFailed(0, "HTTP 500"), Is.Null, "no error wall");
            Assert.That(session.Queue.GaveUpCount, Is.EqualTo(2));
        }

        [Test]
        public void TheWarningFlag_SurvivesAReload()
        {
            var store = new MemoryStateStore();
            var before = new DevTrackSession(store, maxAttempts: 1);
            before.PlayStarted("s-1", 0);
            before.SendFailed(0, "HTTP 500");

            var after = new DevTrackSession(store, maxAttempts: 1);
            after.PlayStarted("s-2", 10);

            Assert.That(after.State.warned, Is.True);
            Assert.That(after.SendFailed(10, "HTTP 500"), Is.Null);
        }

        [Test]
        public void SetContext_IsWhatOpensTheSending()
        {
            var store = new MemoryStateStore();
            var session = new DevTrackSession(store);
            Assert.That(session.State.contextReady, Is.False);

            session.SetContext(new DevTrackContext { projectName = "P" });

            Assert.That(session.State.contextReady, Is.True);
            Assert.That(new DevTrackSession(store).State.context.projectName, Is.EqualTo("P"), "collected once per Editor session");
        }
    }
}
