using FlapGame.GameLib.DevTrack;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class DevTrackConfigTests
    {
        [Test]
        public void BatchMode_IsOff_WhateverElseSays()
        {
            Assert.That(DevTrackConfig.Decide(isBatchMode: true, environmentValue: "on", enabledPref: true).On, Is.False);
            Assert.That(DevTrackConfig.Decide(isBatchMode: true, environmentValue: null, enabledPref: true).Reason,
                        Is.EqualTo("batch mode"), "CI and Unity Cloud Build report nothing");
        }

        [Test]
        public void TheEnvironmentVariable_WinsOverTheMenuToggle()
        {
            Assert.That(DevTrackConfig.Decide(false, "off", enabledPref: true).On, Is.False);
            Assert.That(DevTrackConfig.Decide(false, "on", enabledPref: false).On, Is.True);
        }

        [Test]
        public void TheEnvironmentVariable_NamesItselfInTheReason()
        {
            Assert.That(DevTrackConfig.Decide(false, "off", true).Reason, Is.EqualTo("GAMELIB_DEVTRACK=off"));
        }

        [Test]
        public void WithoutTheEnvironmentVariable_TheMenuToggleDecides()
        {
            Assert.That(DevTrackConfig.Decide(false, null, enabledPref: true).On, Is.True);
            Assert.That(DevTrackConfig.Decide(false, string.Empty, enabledPref: false).On, Is.False);
            Assert.That(DevTrackConfig.Decide(false, "   ", enabledPref: false).Reason, Does.Contain("Dev Tracking"));
        }

        [Test]
        public void AValueThatIsNeitherOnNorOff_IsIgnored_SoATypoCannotTurnTrackingOff()
        {
            Assert.That(DevTrackConfig.Decide(false, "maybe", enabledPref: true).On, Is.True);
            Assert.That(DevTrackConfig.Decide(false, "maybe", enabledPref: false).On, Is.False);
        }

        [Test]
        public void TheSwitchTakesTheUsualSpellings()
        {
            Assert.That(DevTrackConfig.ReadSwitch("OFF"), Is.False);
            Assert.That(DevTrackConfig.ReadSwitch(" 0 "), Is.False);
            Assert.That(DevTrackConfig.ReadSwitch("false"), Is.False);
            Assert.That(DevTrackConfig.ReadSwitch("no"), Is.False);
            Assert.That(DevTrackConfig.ReadSwitch("On"), Is.True);
            Assert.That(DevTrackConfig.ReadSwitch("1"), Is.True);
            Assert.That(DevTrackConfig.ReadSwitch("true"), Is.True);
            Assert.That(DevTrackConfig.ReadSwitch("yes"), Is.True);
            Assert.That(DevTrackConfig.ReadSwitch(null), Is.Null);
            Assert.That(DevTrackConfig.ReadSwitch("something"), Is.Null);
        }

        [Test]
        public void TheEndpoint_FallsBackToTheConst()
        {
            Assert.That(DevTrackConfig.ResolveEndpoint(null, null, DevTrackConfig.DefaultEndpoint),
                        Is.EqualTo("https://tools.puzzleclassic.com/sdk/v1/dev-play"));
            Assert.That(DevTrackConfig.ResolveEndpoint(string.Empty, "   ", DevTrackConfig.DefaultEndpoint),
                        Is.EqualTo(DevTrackConfig.DefaultEndpoint));
        }

        [Test]
        public void TheEndpointOverrides_HaveTheSamePrecedenceAsTheOffSwitch()
        {
            Assert.That(DevTrackConfig.ResolveEndpoint(" http://localhost:8080/dev-play ", "https://pref/", DevTrackConfig.DefaultEndpoint),
                        Is.EqualTo("http://localhost:8080/dev-play"), "the environment variable wins");
            Assert.That(DevTrackConfig.ResolveEndpoint(null, "https://staging.example.com/sdk/v1/dev-play", DevTrackConfig.DefaultEndpoint),
                        Is.EqualTo("https://staging.example.com/sdk/v1/dev-play"), "then EditorPrefs");
        }

        [Test]
        public void TheClientVersion_IsThePackageVersion()
        {
            Assert.That(DevTrackConfig.ClientVersion, Is.EqualTo("0.9.1"));
        }
    }
}
