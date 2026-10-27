using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FlapGame.GameLib.Extensions.Tests
{
    public class CollectionTests
    {
        [Test]
        public void IsNullOrEmpty_ListsArraysSetsAndStrings()
        {
            Assert.That(((List<int>)null).IsNullOrEmpty(), Is.True);
            Assert.That(new List<int>().IsNullOrEmpty(), Is.True);
            Assert.That(new[] { 1 }.IsNullOrEmpty(), Is.False);
            Assert.That(new HashSet<int> { 1 }.IsNullOrEmpty(), Is.False);
            Assert.That(new Dictionary<int, int>().IsNullOrEmpty(), Is.True);
            Assert.That(((string)null).IsNullOrEmpty(), Is.True);
            Assert.That(" ".IsNullOrWhiteSpace(), Is.True);
        }

        [Test]
        public void RandomItem_SeededIsRepeatable_AndCoversEveryItem()
        {
            var list = new List<int> { 1, 2, 3, 4 };
            var r1 = new Random(7);
            var r2 = new Random(7);
            for (int i = 0; i < 50; i++) Assert.That(list.RandomItem(r1), Is.EqualTo(list.RandomItem(r2)));

            var seen = new HashSet<int>();
            for (int i = 0; i < 200; i++) seen.Add(list.RandomItem());
            Assert.That(seen, Is.EquivalentTo(list));
            Assert.Throws<ArgumentException>(() => new List<int>().RandomItem());
            Assert.That(new int[0].TryRandomItem(out _), Is.False);
            Assert.That(list.TryRandomItem(new Random(2), out int picked), Is.True);
            Assert.That(list, Does.Contain(picked));
            Assert.That(RandomHelper.WeightedItem(new[] { "x", "y" }, new[] { 1f, 0f }, new Random(5)), Is.EqualTo("x"));
            Assert.Throws<ArgumentNullException>(() => RandomHelper.Chance(0.5f, null));
            Assert.Throws<ArgumentNullException>(() => CoroutineHelper.WaitUntilRoutine(null, null));
        }

        [Test]
        public void RemoveRandom_TakesEachItemOnce()
        {
            var list = new List<string> { "a", "b", "c" };
            var taken = new List<string>();
            while (list.Count > 0) taken.Add(list.RemoveRandom(new Random(1)));
            Assert.That(taken, Is.EquivalentTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Shuffle_KeepsTheItems()
        {
            int[] array = Enumerable.Range(0, 20).ToArray();
            array.Shuffle(new Random(3));
            Assert.That(array, Is.EquivalentTo(Enumerable.Range(0, 20)));
            Assert.That(array, Is.Not.EqualTo(Enumerable.Range(0, 20)));
        }

        [Test]
        public void Resize_And_Indexes()
        {
            var list = new List<int> { 1, 2, 3 };
            list.Resize(5, 9);
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 9, 9 }));
            list.Resize(2);
            Assert.That(list, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(new[] { 1, 2 }.Resized(3, 7), Is.EqualTo(new[] { 1, 2, 7 }));
            Assert.That(((int[])null).Resized(2), Is.EqualTo(new[] { 0, 0 }));

            Assert.That(list.IsValidIndex(1), Is.True);
            Assert.That(list.IsValidIndex(2), Is.False);
            Assert.That(list.IsValidIndex(-1), Is.False);
            Assert.That(list.GetOrDefault(5, -1), Is.EqualTo(-1));

            ICollection<int> set = new HashSet<int>();
            set.AddRange(new[] { 1, 1, 2 });
            set.AddRange(null);
            Assert.That(set.Count, Is.EqualTo(2));
        }

        [Test]
        public void Weighted_FollowsTheWeights_AndSkipsZero()
        {
            var weights = new List<float> { 1f, 0f, 3f, -2f };
            var counts = new int[4];
            var random = new Random(11);
            for (int i = 0; i < 4000; i++) counts[RandomHelper.WeightedIndex(weights, random)]++;
            Assert.That(counts[1], Is.Zero);
            Assert.That(counts[3], Is.Zero);
            Assert.That(counts[2] / (float)counts[0], Is.InRange(2.5f, 3.5f));

            Assert.That(RandomHelper.WeightedIndex(new List<float> { 0f, 0f }), Is.EqualTo(-1));
            Assert.That(RandomHelper.WeightedIndex(new List<float>()), Is.EqualTo(-1));
            Assert.That(RandomHelper.WeightedItem(new[] { "x", "y" }, new[] { 0f, 1f }), Is.EqualTo("y"));
            Assert.Throws<ArgumentException>(() => RandomHelper.WeightedItem(new[] { "x" }, new[] { 1f, 2f }));
        }
    }

    public class FormattingTests
    {
        [TestCase(0, "0")]
        [TestCase(999, "999")]
        [TestCase(999.4, "999")]
        [TestCase(1000, "1K")]
        [TestCase(1500, "1.5K")]
        [TestCase(1960, "1.9K")]
        [TestCase(1100, "1.1K")]
        [TestCase(999_950, "999.9K")]
        [TestCase(999_999, "999.9K")]
        [TestCase(1_000_000, "1M")]
        [TestCase(2_340_000, "2.3M")]
        [TestCase(2_999_999_999, "2.9B")]
        [TestCase(1.5e15, "1500T")]
        [TestCase(1_000_000_000, "1B")]
        [TestCase(-1500, "-1.5K")]
        [TestCase(-0.4, "0")]
        public void Compact(double value, string expected) => Assert.That(NumberFormatter.Compact(value), Is.EqualTo(expected));

        [Test]
        public void SeparatorsAndPercent()
        {
            Assert.That(NumberFormatter.WithSeparators(1234567), Is.EqualTo("1,234,567"));
            Assert.That(NumberFormatter.Percent(0.256f), Is.EqualTo("26%"));
            Assert.That(NumberFormatter.Percent(0.256f, 1), Is.EqualTo("25.6%"));
        }

        [Test]
        public void Clocks_RoundUp_AndNeverGoNegative()
        {
            Assert.That(TimeFormatter.MinutesSeconds(65), Is.EqualTo("01:05"));
            Assert.That(TimeFormatter.MinutesSeconds(0.2), Is.EqualTo("00:01"));
            Assert.That(TimeFormatter.MinutesSeconds(59.00001f), Is.EqualTo("00:59"));
            Assert.That(TimeFormatter.MinutesSeconds(-5), Is.EqualTo("00:00"));
            Assert.That(TimeFormatter.MinutesSeconds(3725), Is.EqualTo("62:05"));
            Assert.That(TimeFormatter.HoursMinutesSeconds(3725), Is.EqualTo("01:02:05"));
            Assert.That(TimeFormatter.Clock(125), Is.EqualTo("02:05"));
            Assert.That(TimeFormatter.Clock(3600), Is.EqualTo("01:00:00"));
            Assert.That(TimeFormatter.Clock(TimeSpan.FromMinutes(90)), Is.EqualTo("01:30:00"));

            Assert.That(TimeFormatter.MinutesSeconds(0.9, roundUp: false), Is.EqualTo("00:00"));
            Assert.That(TimeFormatter.MinutesSeconds(65.7, roundUp: false), Is.EqualTo("01:05"));
            Assert.That(TimeFormatter.MinutesSeconds(59.99999, roundUp: false), Is.EqualTo("01:00"));
            Assert.That(TimeFormatter.Clock(3599.5, roundUp: false), Is.EqualTo("59:59"));
            Assert.That(TimeFormatter.Short(119.9, roundUp: false), Is.EqualTo("1m 59s"));
        }

        [TestCase(0, 2, "0s")]
        [TestCase(45, 2, "45s")]
        [TestCase(330, 2, "5m 30s")]
        [TestCase(7200, 2, "2h")]
        [TestCase(8100, 2, "2h 15m")]
        [TestCase(273600, 2, "3d 4h")]
        [TestCase(273600, 1, "3d")]
        [TestCase(90061, 4, "1d 1h 1m 1s")]
        public void Short(double seconds, int units, string expected) =>
            Assert.That(TimeFormatter.Short(seconds, units), Is.EqualTo(expected));

        [Test]
        public void Truncate()
        {
            Assert.That("Hello world".Truncate(8), Is.EqualTo("Hello w…"));
            Assert.That("Hello".Truncate(8), Is.EqualTo("Hello"));
            Assert.That("Hello world".Truncate(8, "..."), Is.EqualTo("Hello..."));
            Assert.That("Hello".Truncate(2, "..."), Is.EqualTo(".."));
            Assert.That("Hello".Truncate(0), Is.Empty);
        }

        [Test]
        public void Colors()
        {
            Assert.That(ColorExtensions.ParseHex("#FF0000"), Is.EqualTo(Color.red));
            Assert.That(ColorExtensions.ParseHex("00FF00"), Is.EqualTo(Color.green));
            Assert.That(ColorExtensions.TryParseHex("nope", out _), Is.False);
            Assert.That(ColorExtensions.ParseHex(null, Color.white), Is.EqualTo(Color.white));
            Assert.That(Color.red.ToHex(), Is.EqualTo("FF0000"));
            Assert.That(Color.red.WithAlpha(0f).ToHexRgba(), Is.EqualTo("FF000000"));
        }

        [Test]
        public void Math()
        {
            Assert.That(MathHelper.Remap(5, 0, 10, 0, 100), Is.EqualTo(50f));
            Assert.That(MathHelper.Remap(5, 3, 3, 7, 9), Is.EqualTo(7f));
            Assert.That(MathHelper.RemapClamped(20, 0, 10, 100, 0), Is.EqualTo(0f));
            Assert.That(MathHelper.Wrap(3, 3), Is.EqualTo(0));
            Assert.That(MathHelper.Wrap(-1, 3), Is.EqualTo(2));
            Assert.That(MathHelper.Wrap(5, 0), Is.EqualTo(0));
        }
    }

    public class UnityObjectTests
    {
        private readonly List<Object> created = new();

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created) if (o) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void FindDeep_DepthFirst_IgnoresCase_AndIncludesInactive()
        {
            GameObject root = Make("Root");
            GameObject a = Make("A", root.transform);
            GameObject deep = Make("Btn_Play", a.transform);
            deep.SetActive(false);
            Make("btn_play", root.transform);

            Assert.That(root.transform.FindDeep("BTN_PLAY"), Is.SameAs(deep.transform));
            Assert.That(root.transform.FindDeep("BTN_PLAY", ignoreCase: false), Is.Null);
            Assert.That(root.transform.FindDeepStartingWith("btn_"), Is.SameAs(deep.transform));
            Assert.That(root.transform.FindDeepContaining("PLAY"), Is.SameAs(deep.transform));
            Assert.That(root.transform.FindDeep("Missing"), Is.Null);
            Assert.That(root.transform.FindDeep("Root"), Is.Null, "the root itself is not a descendant");
        }

        [Test]
        public void Children_LayerActiveAndDestroy()
        {
            GameObject root = Make("Root");
            GameObject child = Make("C1", root.transform);
            GameObject grandChild = Make("C1a", child.transform);
            Make("C2", root.transform);

            root.SetLayerRecursively(5);
            Assert.That(grandChild.layer, Is.EqualTo(5));

            root.SetActiveChildren(false);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(root.transform.ActiveChildCount(), Is.Zero);
            Assert.That(grandChild.activeSelf, Is.True, "only direct children");
            root.transform.GetChild(1).gameObject.SetActive(true);
            Assert.That(root.transform.ActiveChildCount(), Is.EqualTo(1));

            root.transform.DestroyChildren();
            Assert.That(root.transform.childCount, Is.Zero);
            Assert.That(child == null, Is.True);
        }

        [Test]
        public void Components_AndOrNull()
        {
            GameObject go = Make("Go");
            var first = go.GetOrAddComponent<BoxCollider>();
            Assert.That(go.GetOrAddComponent<BoxCollider>(), Is.SameAs(first));
            Assert.That(first.GetOrAddComponent<BoxCollider>(), Is.SameAs(first));

            Assert.That(first.OrNull(), Is.SameAs(first));
            Object.DestroyImmediate(first);
            Assert.That(ReferenceEquals(first.OrNull(), null), Is.True);
        }

        [Test]
        public void TransformSetters()
        {
            Transform t = Make("T").transform;
            t.position = new Vector3(1, 2, 3);
            t.SetPositionY(9);
            Assert.That(t.position, Is.EqualTo(new Vector3(1, 9, 3)));
            t.SetLocalPositionX(4);
            t.SetLocalPositionZ(0);
            Assert.That(t.localPosition, Is.EqualTo(new Vector3(4, 9, 0)));
            t.SetLocalScale(2);
            Assert.That(t.localScale, Is.EqualTo(Vector3.one * 2));
            t.localRotation = Quaternion.Euler(10, 20, 30);
            t.ResetLocal();
            Assert.That(t.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(t.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(t.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void Ui()
        {
            GameObject go = Make("Ui");
            var group = go.AddComponent<CanvasGroup>();
            group.SetVisible(false);
            Assert.That((group.alpha, group.interactable, group.blocksRaycasts), Is.EqualTo((0f, false, false)));
            group.SetVisible(true);
            group.SetInteractable(false);
            Assert.That((group.alpha, group.interactable, group.blocksRaycasts), Is.EqualTo((1f, false, false)));

            var image = go.AddComponent<Image>();
            image.color = Color.red;
            image.SetAlpha(0.5f);
            Assert.That(image.color, Is.EqualTo(new Color(1, 0, 0, 0.5f)));

            var texture = new Texture2D(200, 100);
            created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 200, 100), Vector2.one * 0.5f);
            created.Add(sprite);
            image.sprite = sprite;
            image.SetSizeByWidth(50);
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(50, 25)));
            image.SetSizeByHeight(50);
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(100, 50)));
        }
    }

    public class VectorTests
    {
        [Test]
        public void WithAndConversions()
        {
            var v = new Vector3(1, 2, 3);
            Assert.That(v.WithY(0), Is.EqualTo(new Vector3(1, 0, 3)));
            Assert.That(v.Flat(), Is.EqualTo(new Vector3(1, 0, 3)));
            Assert.That(v.XY(), Is.EqualTo(new Vector2(1, 2)));
            Assert.That(v.XZ(), Is.EqualTo(new Vector2(1, 3)));
            Assert.That(new Vector2(1, 3).ToVector3XZ(5), Is.EqualTo(new Vector3(1, 5, 3)));
            Assert.That(new Vector2(1, 2).ToVector3XY(), Is.EqualTo(new Vector3(1, 2, 0)));
        }

        [Test]
        public void Angles_AreCounterClockwiseFromX()
        {
            Assert.That(Vector2.up.AngleDegrees(), Is.EqualTo(90f).Within(1e-4));
            Assert.That(Vector2.zero.AngleDegreesTo(new Vector2(-1, 0)), Is.EqualTo(180f).Within(1e-4));
            Vector2 turned = Vector2.right.Rotate(90);
            Assert.That(turned.x, Is.EqualTo(0f).Within(1e-5));
            Assert.That(turned.y, Is.EqualTo(1f).Within(1e-5));
            Assert.That(VectorExtensions.DirectionFromDegrees(180).x, Is.EqualTo(-1f).Within(1e-5));
        }

        [Test]
        public void FiniteAndClosestPoint()
        {
            Assert.That(new Vector3(float.NaN, 0, 0).IsFinite(), Is.False);
            Assert.That(new Vector2(1, float.PositiveInfinity).IsFinite(), Is.False);
            Assert.That(Quaternion.identity.IsFinite(), Is.True);
            Assert.That(new Vector3(3, 5, 0).ClosestPointOnLine(Vector3.zero, Vector3.right * 10), Is.EqualTo(new Vector3(3, 0, 0)));
        }
    }
}
