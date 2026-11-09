using System.Collections.Generic;
using FlapGame.GameLib.DevTrack;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class DevTrackQueueTests
    {
        private static DevTrackQueue Queue(int capacity = 3, int maxAttempts = 3) =>
            new DevTrackQueue(new List<DevTrackPending>(), capacity, maxAttempts);

        private static DevTrackPending Event(string sessionId) =>
            new DevTrackPending { ev = DevTrackPayload.Heartbeat, sessionId = sessionId };

        [Test]
        public void Due_ReturnsTheHead_AndSucceededTakesItOff()
        {
            DevTrackQueue queue = Queue();
            queue.Enqueue(Event("a"));
            queue.Enqueue(Event("b"));

            Assert.That(queue.Due(0).sessionId, Is.EqualTo("a"));
            queue.Succeeded();
            Assert.That(queue.Due(0).sessionId, Is.EqualTo("b"));
            queue.Succeeded();
            Assert.That(queue.Due(0), Is.Null);
        }

        [Test]
        public void AtTheCap_TheOldestEventIsDropped()
        {
            DevTrackQueue queue = Queue(capacity: 3);
            queue.Enqueue(Event("a"));
            queue.Enqueue(Event("b"));
            queue.Enqueue(Event("c"));

            Assert.That(queue.Enqueue(Event("d")), Is.False, "the cap drop is reported");
            Assert.That(queue.Count, Is.EqualTo(3));
            Assert.That(queue.DroppedCount, Is.EqualTo(1));
            Assert.That(queue.Due(0).sessionId, Is.EqualTo("b"));
        }

        [Test]
        public void AFailedEvent_WaitsOutItsBackoff()
        {
            DevTrackQueue queue = Queue();
            queue.Enqueue(Event("a"));

            Assert.That(queue.Failed(100), Is.True);
            Assert.That(queue.Due(100), Is.Null, "not before the backoff is over");
            Assert.That(queue.Due(100 + DevTrackQueue.BaseRetrySeconds).sessionId, Is.EqualTo("a"));
        }

        [Test]
        public void TheBackoffGrows_AndIsCapped()
        {
            Assert.That(DevTrackQueue.RetryDelay(1), Is.EqualTo(DevTrackQueue.BaseRetrySeconds));
            Assert.That(DevTrackQueue.RetryDelay(2), Is.EqualTo(DevTrackQueue.BaseRetrySeconds * 2));
            Assert.That(DevTrackQueue.RetryDelay(3), Is.EqualTo(DevTrackQueue.BaseRetrySeconds * 4));
            Assert.That(DevTrackQueue.RetryDelay(40), Is.EqualTo(DevTrackQueue.MaxRetrySeconds));
        }

        [Test]
        public void AfterTheLastAttempt_TheEventIsGivenUpOn_AndTheQueueMovesOn()
        {
            DevTrackQueue queue = Queue(maxAttempts: 3);
            queue.Enqueue(Event("a"));
            queue.Enqueue(Event("b"));

            Assert.That(queue.Failed(0), Is.True, "attempt 1");
            Assert.That(queue.Failed(10), Is.True, "attempt 2");
            Assert.That(queue.Failed(30), Is.False, "attempt 3 gives up");

            Assert.That(queue.GaveUpCount, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Due(30).sessionId, Is.EqualTo("b"), "the next event is not held up by the dead one");
        }

        [Test]
        public void FailedOnAnEmptyQueue_DoesNothing()
        {
            DevTrackQueue queue = Queue();
            Assert.That(queue.Failed(0), Is.False);
            Assert.That(queue.GaveUpCount, Is.Zero);
        }

        [Test]
        public void TheQueueIsTheListItWasGiven_SoItSurvivesAReload()
        {
            var items = new List<DevTrackPending>();
            new DevTrackQueue(items).Enqueue(Event("a"));

            // A new queue over the same list: this is what a domain reload does.
            Assert.That(new DevTrackQueue(items).Due(0).sessionId, Is.EqualTo("a"));
        }
    }
}
