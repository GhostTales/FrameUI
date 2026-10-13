using FrameUI;
using NUnit.Framework;
using UnityEngine;

namespace FrameUI.Tests
{
    public class LayoutTests
    {
        static readonly Rect Screen = new Rect(0f, 0f, 400f, 300f);

        // A leaf with a fixed size: Layout.None reports its Bounds as content size.
        static FigmaFrame Box(float w, float h) => new FigmaFrame(new Rect(0f, 0f, w, h));

        static FigmaFrame Stack(FigmaLayout layout, float w, float h) =>
            new FigmaFrame(new Rect(0f, 0f, w, h)) { Layout = layout };

        static void AssertRect(Rect expected, Rect actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), "x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), "y");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(0.001f), "width");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(0.001f), "height");
        }

        [Test]
        public void NoLayoutPlacesChildrenAtTheirBounds()
        {
            var root = Stack(FigmaLayout.None, 200f, 100f);
            var child = root.Add(new FigmaFrame(new Rect(10f, 20f, 30f, 40f)));
            root.Arrange(new Rect(5f, 5f, 0f, 0f));

            AssertRect(new Rect(5f, 5f, 200f, 100f), root.LayoutRect);
            AssertRect(new Rect(15f, 25f, 30f, 40f), child.LayoutRect);
        }

        [Test]
        public void HorizontalStackAppliesPaddingAndGap()
        {
            var root = Stack(FigmaLayout.Horizontal, 200f, 100f);
            root.Padding = new Vector4(10f, 5f, 0f, 0f);
            root.Gap = 4f;
            var a = root.Add(Box(20f, 10f));
            var b = root.Add(Box(30f, 10f));
            root.Arrange(Screen);

            AssertRect(new Rect(10f, 5f, 20f, 10f), a.LayoutRect);
            AssertRect(new Rect(34f, 5f, 30f, 10f), b.LayoutRect);
        }

        [Test]
        public void FillChildrenShareLeftoverSpaceByWeight()
        {
            var root = Stack(FigmaLayout.Horizontal, 200f, 50f);
            root.Gap = 10f;
            var fixedChild = root.Add(Box(40f, 10f));
            var small = root.Add(Box(0f, 10f).Size(FigmaSize.Fill, FigmaSize.Fixed));
            var big = root.Add(Box(0f, 10f).Size(FigmaSize.Fill, FigmaSize.Fixed));
            big.FillWeight = 3f;
            root.Arrange(Screen);

            // 200 - 40 - 2 gaps of 10 = 140 shared 1:3
            Assert.That(small.LayoutRect.width, Is.EqualTo(35f).Within(0.001f));
            Assert.That(big.LayoutRect.width, Is.EqualTo(105f).Within(0.001f));
            Assert.That(big.LayoutRect.x, Is.EqualTo(fixedChild.LayoutRect.xMax + 10f + 35f + 10f).Within(0.001f));
        }

        [Test]
        public void SpaceBetweenPushesFirstAndLastToTheEdges()
        {
            var root = Stack(FigmaLayout.Horizontal, 200f, 40f);
            root.MainAlign = FigmaAlign.SpaceBetween;
            root.CrossAlign = FigmaAlign.Center;
            var a = root.Add(Box(30f, 10f));
            var b = root.Add(Box(30f, 20f));
            root.Arrange(Screen);

            Assert.That(a.LayoutRect.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(b.LayoutRect.xMax, Is.EqualTo(200f).Within(0.001f));
            Assert.That(a.LayoutRect.y, Is.EqualTo(15f).Within(0.001f));
            Assert.That(b.LayoutRect.y, Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void VerticalStackEndAlignsAndCrossFills()
        {
            var root = Stack(FigmaLayout.Vertical, 100f, 100f);
            root.MainAlign = FigmaAlign.End;
            var child = root.Add(Box(0f, 30f).Size(FigmaSize.Fill, FigmaSize.Fixed));
            root.Arrange(Screen);

            AssertRect(new Rect(0f, 70f, 100f, 30f), child.LayoutRect);
        }

        [Test]
        public void HugSizesToContentIncludingNestedFrames()
        {
            var root = Stack(FigmaLayout.Vertical, 500f, 500f);
            root.Gap = 5f;
            var card = root.Add(Stack(FigmaLayout.Horizontal, 0f, 0f).Size(FigmaSize.Hug, FigmaSize.Hug));
            card.Padding = new Vector4(4f, 4f, 4f, 4f);
            card.Gap = 6f;
            card.Add(Box(20f, 10f));
            card.Add(Box(30f, 16f));
            var below = root.Add(Box(10f, 10f));
            root.Arrange(Screen);

            // content: 20 + 6 + 30 + 8 padding = 64 wide, 16 + 8 = 24 high
            AssertRect(new Rect(0f, 0f, 64f, 24f), card.LayoutRect);
            Assert.That(below.LayoutRect.y, Is.EqualTo(29f).Within(0.001f));
        }

        [Test]
        public void ContentSizeMatchesWhatHugUses()
        {
            var root = Stack(FigmaLayout.Vertical, 500f, 500f);
            root.Padding = new Vector4(0f, 10f, 0f, 10f);
            root.Gap = 2f;
            root.Add(Box(50f, 20f));
            root.Add(Box(80f, 30f));

            Assert.That(root.ContentSize(), Is.EqualTo(new Vector2(80f, 72f)));
        }

        [Test]
        public void DeepHugNestingIsMeasuredOnce()
        {
            var root = Stack(FigmaLayout.Vertical, 1000f, 1000f);
            var node = root;
            for (int i = 0; i < 40; i++)
                node = node.Add(Stack(FigmaLayout.Vertical, 0f, 0f).Size(FigmaSize.Hug, FigmaSize.Hug));
            node.Add(Box(7f, 9f));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            root.Arrange(Screen);
            watch.Stop();

            Assert.That(node.LayoutRect.size, Is.EqualTo(new Vector2(7f, 9f)));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200));
        }

        [Test]
        public void GridSharesFractionsAndHonoursSpans()
        {
            var root = Stack(FigmaLayout.Grid, 220f, 100f);
            root.ColumnTracks = new[] { FigmaTrack.Px(60f), FigmaTrack.Fr(1f), FigmaTrack.Fr(3f) };
            root.RowTracks = new[] { FigmaTrack.Px(20f), FigmaTrack.Fr(1f) };
            root.ColumnGap = 10f;
            root.RowGap = 10f;
            var a = root.Add(Box(0f, 0f).Size(FigmaSize.Fill, FigmaSize.Fill));
            var wide = root.Add(Box(0f, 0f).Size(FigmaSize.Fill, FigmaSize.Fill));
            wide.ColumnSpan = 2;
            var b = root.Add(Box(0f, 0f).Size(FigmaSize.Fill, FigmaSize.Fill));
            root.Arrange(Screen);

            // columns: 60, then (220 - 60 - 20) = 140 shared 1:3 -> 35 and 105
            AssertRect(new Rect(0f, 0f, 60f, 20f), a.LayoutRect);
            AssertRect(new Rect(70f, 0f, 150f, 20f), wide.LayoutRect);
            AssertRect(new Rect(0f, 30f, 60f, 70f), b.LayoutRect);
        }

        [Test]
        public void GridCellAlignmentPlacesNonFillChildren()
        {
            var root = Stack(FigmaLayout.Grid, 100f, 100f);
            root.Columns = 1;
            var child = root.Add(Box(20f, 20f));
            child.CellAlignX = FigmaAlign.End;
            child.CellAlignY = FigmaAlign.Center;
            root.Arrange(Screen);

            AssertRect(new Rect(80f, 40f, 20f, 20f), child.LayoutRect);
        }

        [Test]
        public void EmptyGridMeasuresToPaddingOnly()
        {
            var root = Stack(FigmaLayout.Grid, 100f, 100f);
            root.Padding = new Vector4(3f, 4f, 5f, 6f);
            root.Columns = 2;
            root.RowGap = 9f;

            Assert.That(root.ContentSize(), Is.EqualTo(new Vector2(8f, 10f)));
        }

        [Test]
        public void BorderWidthAndCornerRadiusShorthandsSetEverySide()
        {
            var frame = Box(10f, 10f);
            frame.BorderWidth = 2f;
            frame.CornerRadius = 5f;

            Assert.That(frame.BorderWidths, Is.EqualTo(new Vector4(2f, 2f, 2f, 2f)));
            Assert.That(frame.CornerRadii, Is.EqualTo(new Vector4(5f, 5f, 5f, 5f)));
        }
    }
}
