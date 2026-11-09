using FlapGame.GameLib.Analytics.Local;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class LocalTrackingGateTests
    {
        [Test]
        public void FindMatch_ReturnsTheAddressOnAConfiguredNetwork()
        {
            string[] addresses = { "10.0.0.5", "192.25.20.113" };
            Assert.That(LocalTrackingGate.FindMatch(addresses, new[] { "192.25.20" }), Is.EqualTo("192.25.20.113"));
        }

        [Test]
        public void FindMatch_IsNullOffTheNetwork()
        {
            string[] addresses = { "192.168.1.14", "172.16.3.2" };
            Assert.That(LocalTrackingGate.FindMatch(addresses, new[] { "192.25.20" }), Is.Null);
        }

        [Test]
        public void FindMatch_MatchesOnlyTheStart_SoA192_168AddressIsNotOurLan()
        {
            Assert.That(LocalTrackingGate.FindMatch(new[] { "10.192.25.20" }, new[] { "192.25.20" }), Is.Null);
        }

        [Test]
        public void FindMatch_TakesSeveralPrefixes_AndIgnoresEmptyOnes()
        {
            string[] addresses = { "192.25.21.9" };
            Assert.That(LocalTrackingGate.FindMatch(addresses, new[] { "", null, "192.25.21" }), Is.EqualTo("192.25.21.9"));
        }

        [Test]
        public void FindMatch_WithoutPrefixesOrAddresses_IsNull()
        {
            Assert.That(LocalTrackingGate.FindMatch(new[] { "192.25.20.1" }, new string[0]), Is.Null);
            Assert.That(LocalTrackingGate.FindMatch(null, new[] { "192.25.20" }), Is.Null);
            Assert.That(LocalTrackingGate.FindMatch(new[] { "192.25.20.1" }, null), Is.Null);
        }

        [Test]
        public void IsLocalDevice_InTheEditor_FollowsTheToggleOnly()
        {
            // The tests run in the Editor, so this is the Editor branch - no LAN check at all.
            Assert.That(LocalTrackingGate.IsLocalDevice(trackInEditor: true, new[] { "203.0.113" }, out string on), Is.True);
            Assert.That(on, Does.Contain("Editor"));

            Assert.That(LocalTrackingGate.IsLocalDevice(trackInEditor: false, new[] { "203.0.113" }, out string off), Is.False);
            Assert.That(off, Does.Contain("Track In Editor is off"));
        }

        [Test]
        public void LocalIPv4Addresses_AreIPv4()
        {
            foreach (string address in LocalTrackingGate.LocalIPv4Addresses())
            {
                Assert.That(System.Net.IPAddress.Parse(address).AddressFamily,
                    Is.EqualTo(System.Net.Sockets.AddressFamily.InterNetwork), address);
            }
        }
    }
}
