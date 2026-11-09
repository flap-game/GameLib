using FlapGame.GameLib.DevTrack;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    public class DevTrackPayloadTests
    {
        private static DevTrackContext FullContext() => new DevTrackContext
        {
            projectName = "Word Jigsort",
            projectPath = @"D:\Funnii\UnityProjects\word-jigsort",
            bundleId = "com.flapgame.wordjigsort",
            unityVersion = "6000.4.1f1",
            buildTarget = "Android",
            gitUrl = "git@github.com:flap-game/word-jigsort.git",
            gitBranch = "feat/devtrack",
            gitCommit = "5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1",
            gitUserName = "Tuan Anh Tran",
            gitUserEmail = "tuananh@flapgame.com",
            pcUser = "TuanAnhTran",
            pcName = "FLAP-PC-07",
            localIp = "192.25.20.113",
        };

        [Test]
        public void PlayStart_HasEveryFieldInTheContractOrder_AndNoDuration()
        {
            string body = DevTrackPayload.BuildBody(DevTrackPayload.PlayStart, "2f1c4b0e-7a3d-4a1b-9c2e-8d6f5a4b3c2d",
                                                    FullContext(), DevTrackPayload.NoDuration, "0.9.1");

            Assert.That(body, Is.EqualTo(
                @"{""event"":""play_start"",""sessionId"":""2f1c4b0e-7a3d-4a1b-9c2e-8d6f5a4b3c2d""," +
                @"""projectName"":""Word Jigsort"",""projectPath"":""D:\\Funnii\\UnityProjects\\word-jigsort""," +
                @"""bundleId"":""com.flapgame.wordjigsort"",""unityVersion"":""6000.4.1f1"",""buildTarget"":""Android""," +
                @"""gitUrl"":""git@github.com:flap-game/word-jigsort.git"",""gitBranch"":""feat/devtrack""," +
                @"""gitCommit"":""5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1"",""gitUserName"":""Tuan Anh Tran""," +
                @"""gitUserEmail"":""tuananh@flapgame.com"",""pcUser"":""TuanAnhTran"",""pcName"":""FLAP-PC-07""," +
                @"""localIp"":""192.25.20.113"",""clientVersion"":""0.9.1""}"));
        }

        [Test]
        public void Heartbeat_CarriesTheElapsedSeconds()
        {
            string body = DevTrackPayload.BuildBody(DevTrackPayload.Heartbeat, "s-1",
                                                    new DevTrackContext { projectName = "P" }, 180, "0.9.1");

            Assert.That(body, Is.EqualTo(@"{""event"":""heartbeat"",""sessionId"":""s-1"",""projectName"":""P""," +
                                         @"""durationSeconds"":180,""clientVersion"":""0.9.1""}"));
        }

        [Test]
        public void PlayStop_WithAZeroDuration_StillSendsTheMember()
        {
            string body = DevTrackPayload.BuildBody(DevTrackPayload.PlayStop, "s-1",
                                                    new DevTrackContext { projectName = "P" }, 0, "0.9.1");

            Assert.That(body, Does.Contain(@"""durationSeconds"":0"));
        }

        [Test]
        public void EmptyOptionalFields_AreLeftOut()
        {
            var context = new DevTrackContext
            {
                projectName = "P",
                projectPath = null,
                bundleId = string.Empty,
                gitBranch = "main",
            };

            string body = DevTrackPayload.BuildBody(DevTrackPayload.PlayStart, "s-1", context, DevTrackPayload.NoDuration, "0.9.1");

            Assert.That(body, Is.EqualTo(@"{""event"":""play_start"",""sessionId"":""s-1"",""projectName"":""P""," +
                                         @"""gitBranch"":""main"",""clientVersion"":""0.9.1""}"));
        }

        [Test]
        public void TheRequiredFields_AreSentEmptyRatherThanMissing()
        {
            string body = DevTrackPayload.BuildBody(DevTrackPayload.PlayStart, null, null, DevTrackPayload.NoDuration, null);

            Assert.That(body, Is.EqualTo(@"{""event"":""play_start"",""sessionId"":"""",""projectName"":""""}"));
        }

        [Test]
        public void Values_AreEscaped()
        {
            var context = new DevTrackContext { projectName = "a\"b\\c\nd\te", gitBranch = "f\u0001g" };

            string body = DevTrackPayload.BuildBody(DevTrackPayload.PlayStart, "s", context, DevTrackPayload.NoDuration, null);

            Assert.That(body, Is.EqualTo("{\"event\":\"play_start\",\"sessionId\":\"s\"," +
                                         "\"projectName\":\"a\\\"b\\\\c\\nd\\te\",\"gitBranch\":\"f\\u0001g\"}"));
        }
    }
}
