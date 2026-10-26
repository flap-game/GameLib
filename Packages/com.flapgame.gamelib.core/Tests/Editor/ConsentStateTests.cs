using System.Collections.Generic;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class ConsentStateTests
    {
        [Test]
        public void SetAtt_ChangesOnlyAtt_AndDoesNotResolveTheCmpAnswer()
        {
            var store = new ConsentStateStore();
            var seen = new List<ConsentState>();
            store.Changed += seen.Add;

            store.SetAtt(AttStatus.Denied);
            store.SetAtt(AttStatus.Denied);

            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(store.Current.Att, Is.EqualTo(AttStatus.Denied));
            Assert.That(store.Current.IsResolved, Is.False);
        }

        [Test]
        public void Update_ResolvesTheCmpAnswer()
        {
            var store = new ConsentStateStore();
            store.Update(new ConsentState { GdprApplies = true, HasUserConsent = false });

            Assert.That(store.Current.IsResolved, Is.True);
            Assert.That(store.Current.GdprApplies, Is.True);
        }
    }
}
