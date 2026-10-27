using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using FlapGame.GameLib.AppIcon.Editor;
using UnityEngine;

namespace FlapGame.GameLib.AppIcon.Tests
{
    public class AppIconTests
    {
        [Test]
        public async Task SetIcon_OnlyListedNames_AndNullIsTheDefault()
        {
            var backend = new FakeBackend();
            var icons = new AppIconService(new[] { "gold", "dark" }, backend);

            Assert.That(await icons.SetIconAsync("gold"), Is.True);
            Assert.That(icons.CurrentIcon, Is.EqualTo("gold"));
            Assert.That(await icons.SetIconAsync("missing"), Is.False);
            Assert.That(icons.CurrentIcon, Is.EqualTo("gold"));
            Assert.That(await icons.SetIconAsync(" "), Is.True);
            Assert.That(icons.CurrentIcon, Is.Null);
            Assert.That(backend.Calls, Is.EqualTo(new[] { "gold", null }));
        }

        [Test]
        public async Task SameIcon_NoNativeCall()
        {
            var backend = new FakeBackend { Current = "gold" };
            var icons = new AppIconService(new[] { "gold" }, backend);
            Assert.That(await icons.SetIconAsync("gold"), Is.True);
            Assert.That(backend.Calls, Is.Empty);
        }

        [Test]
        public async Task NotSupported_ReturnsFalse()
        {
            var icons = new AppIconService(new[] { "gold" }, null);
            Assert.That(icons.IsSupported, Is.False);
            Assert.That(await icons.SetIconAsync("gold"), Is.False);
        }

        [Test]
        public async Task NativeError_ReturnsFalse()
        {
            var icons = new AppIconService(new[] { "gold" }, new FakeBackend { Error = "busy" });
            Assert.That(await icons.SetIconAsync("gold"), Is.False);
        }

        [Test]
        public void Validate_NamesAndImages()
        {
            var module = ScriptableObject.CreateInstance<AppIconModule>();
            try
            {
                module.Icons.Add(new AppIconEntry { Name = "gold icon" });
                module.Icons.Add(new AppIconEntry { Name = "dark" });
                module.Icons.Add(new AppIconEntry { Name = "Dark" });
                var issues = new List<GameLibIssue>();
                module.Validate(issues);
                Assert.That(issues.FindAll(i => i.Message.Contains("must be letters")).Count, Is.EqualTo(1));
                Assert.That(issues.FindAll(i => i.Message.Contains("two icons")).Count, Is.EqualTo(1));
                Assert.That(issues.FindAll(i => i.Message.Contains("no image")).Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(module);
            }
        }

        [Test]
        public void IconSet_Is1024_WithoutAlpha()
        {
            var source = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 0, 255);
            pixels[0] = new Color32(255, 255, 255, 0);
            source.SetPixels32(pixels);
            source.Apply();
            string dir = Path.Combine(Path.GetTempPath(), "gamelib-appicon-" + System.Guid.NewGuid().ToString("N"));
            string image = Path.Combine(dir, "src.png");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(image, source.EncodeToPNG());
                AppIconXcode.WriteIconSet(dir, "gold", image);

                string set = Path.Combine(dir, "gold.appiconset");
                StringAssert.Contains("\"size\" : \"1024x1024\"", File.ReadAllText(Path.Combine(set, "Contents.json")));
                var icon = new Texture2D(2, 2);
                Assert.That(icon.LoadImage(File.ReadAllBytes(Path.Combine(set, "icon.png"))), Is.True);
                Assert.That(icon.width, Is.EqualTo(1024));
                Assert.That(icon.height, Is.EqualTo(1024));
                Color32 middle = icon.GetPixel(512, 512);
                Assert.That(middle.r, Is.GreaterThan(240));
                Assert.That(middle.a, Is.EqualTo(255));
                Object.DestroyImmediate(icon);
            }
            finally
            {
                Object.DestroyImmediate(source);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void AppIconName_IsReserved()
        {
            Assert.That(AppIconModule.IsValidName("AppIcon"), Is.False);
            Assert.That(AppIconModule.IsValidName("appicon"), Is.False);
            Assert.That(AppIconModule.IsValidName("AppIcon2"), Is.True);
        }

        [Test]
        public void StaleSets_OnlyOursAreRemoved_AndNamesMerge()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gamelib-appicon-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "AppIcon.appiconset"));
                Directory.CreateDirectory(Path.Combine(dir, "old.appiconset"));
                File.WriteAllText(Path.Combine(dir, "old.appiconset", AppIconXcode.Marker), "x");
                Directory.CreateDirectory(Path.Combine(dir, "gold.appiconset"));
                File.WriteAllText(Path.Combine(dir, "gold.appiconset", AppIconXcode.Marker), "x");

                List<string> removed = AppIconXcode.RemoveStaleIconSets(dir, new[] { "gold" });
                Assert.That(removed, Is.EqualTo(new[] { "old" }));
                Assert.That(Directory.Exists(Path.Combine(dir, "AppIcon.appiconset")), Is.True, "not ours");
                Assert.That(Directory.Exists(Path.Combine(dir, "gold.appiconset")), Is.True);
                Assert.That(AppIconXcode.MergeIconNames("vendor old", new[] { "gold" }, removed), Is.EqualTo("vendor gold"));
                Assert.That(AppIconXcode.MergeIconNames(null, new[] { "gold", "gold" }), Is.EqualTo("gold"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public async Task IconChange_IsTheGamesOwnTrip()
        {
            var lifecycle = new RecordingLifecycle();
            var icons = new AppIconService(new[] { "gold" }, new FakeBackend(), lifecycle);
            Assert.That(await icons.SetIconAsync("gold"), Is.True);
            Assert.That(lifecycle.Marks, Is.EqualTo(new[] { "begin:app_icon", "mark:app_icon", "end:app_icon" }));
        }

        private sealed class RecordingLifecycle : IAppLifecycle
        {
            public readonly List<string> Marks = new();
            public bool IsPaused => false;
            public bool HasFocus => true;
            public event System.Action<bool> PauseChanged { add { } remove { } }
            public event System.Action<bool> FocusChanged { add { } remove { } }
            public event System.Action<AppResumeInfo> Resumed { add { } remove { } }
            public event System.Action Quitting { add { } remove { } }

            public System.IDisposable BeginExternalActivity(string reason)
            {
                Marks.Add("begin:" + reason);
                return new Scope(() => Marks.Add("end:" + reason));
            }

            public void MarkExternalActivity(string reason) => Marks.Add("mark:" + reason);

            private sealed class Scope : System.IDisposable
            {
                private readonly System.Action end;
                public Scope(System.Action end) => this.end = end;
                public void Dispose() => end();
            }
        }

        private sealed class FakeBackend : IAppIconBackend
        {
            public string Current;
            public string Error;
            public readonly List<string> Calls = new();

            public bool IsSupported => true;
            public string CurrentIcon => Current;

            public Task<string> SetIconAsync(string iconName)
            {
                Calls.Add(iconName);
                if (Error == null) Current = iconName;
                return Task.FromResult(Error);
            }
        }
    }
}
