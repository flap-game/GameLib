using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FlapGame.GameLib.Analytics.Local;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class LocalTrackingJsonTests
    {
        [Test]
        public void BuildBody_HasTheServerFields_InOrder()
        {
            var trackingEvent = new LocalTrackingEvent("level_complete", new Dictionary<string, object> { ["level"] = 3, ["eventCount"] = 7L });

            string body = LocalTrackingJson.BuildBody("com.flapgame.game", "1.2.3", "idfa-1", trackingEvent);

            Assert.That(body, Is.EqualTo(
                "{\"bundleId\":\"com.flapgame.game\",\"appVersion\":\"1.2.3\",\"idfa\":\"idfa-1\"," +
                "\"eventName\":\"level_complete\",\"params\":{\"level\":3,\"eventCount\":7}}"));
        }

        [Test]
        public void BuildBody_WithoutParameters_LeavesParamsOut()
        {
            string body = LocalTrackingJson.BuildBody("b", "1.0", "id", new LocalTrackingEvent("app_open", new Dictionary<string, object>()));

            Assert.That(body, Is.EqualTo("{\"bundleId\":\"b\",\"appVersion\":\"1.0\",\"idfa\":\"id\",\"eventName\":\"app_open\"}"));
        }

        [Test]
        public void BuildBody_NullFields_BecomeEmptyStrings()
        {
            string body = LocalTrackingJson.BuildBody(null, null, null, new LocalTrackingEvent("e", null));

            Assert.That(body, Is.EqualTo("{\"bundleId\":\"\",\"appVersion\":\"\",\"idfa\":\"\",\"eventName\":\"e\"}"));
        }

        [Test]
        public void Values_AreEscaped()
        {
            string json = LocalTrackingJson.ToJson(new Dictionary<string, object>
            {
                ["quote\""] = "line\nbreak\\and\ttab",
                ["control"] = "\u0001",
            });

            Assert.That(json, Is.EqualTo("{\"quote\\\"\":\"line\\nbreak\\\\and\\ttab\",\"control\":\"\\u0001\"}"));
        }

        [Test]
        public void Numbers_UseTheInvariantCulture_EvenWhereTheCommaIsTheDecimalSeparator()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string json = LocalTrackingJson.ToJson(new Dictionary<string, object>
                {
                    ["double"] = 1.5,
                    ["float"] = 0.25f,
                    ["decimal"] = 9.99m,
                    ["long"] = 42L,
                });

                Assert.That(json, Is.EqualTo("{\"double\":1.5,\"float\":0.25,\"decimal\":9.99,\"long\":42}"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Null_Bool_NestedObjectsAndLists_AreWritten()
        {
            string json = LocalTrackingJson.ToJson(new Dictionary<string, object>
            {
                ["missing"] = null,
                ["flag"] = true,
                ["nested"] = new Dictionary<string, object> { ["a"] = 1 },
                ["list"] = new object[] { 1, "two" },
                ["kind"] = PurchaseProductKind.Subscription,
            });

            Assert.That(json, Is.EqualTo("{\"missing\":null,\"flag\":true,\"nested\":{\"a\":1},\"list\":[1,\"two\"],\"kind\":\"Subscription\"}"));
        }

        [Test]
        public void NaNAndInfinity_BecomeNull_BecauseJsonHasNoSuchNumber()
        {
            string json = LocalTrackingJson.ToJson(new Dictionary<string, object>
            {
                ["nan"] = double.NaN,
                ["inf"] = float.PositiveInfinity,
            });

            Assert.That(json, Is.EqualTo("{\"nan\":null,\"inf\":null}"));
        }
    }
}
