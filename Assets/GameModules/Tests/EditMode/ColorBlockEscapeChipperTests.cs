using System.Collections.Generic;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Presentation;
using NUnit.Framework;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeChipperTests
    {
        [TestCase(2, 2, 24)]
        [TestCase(3, 2, 54)]
        [TestCase(3, 3, 81)]
        [TestCase(4, 2, 96)]
        [TestCase(4, 3, 144)]
        public void FragmentLayoutCountTracksFootprintAndConfiguredDensity(
            int subdivision, int depthLayers, int expected)
        {
            ShapeFootprint footprint = new(new[]
            {
                new GridCoordinate(0, 0),
                new GridCoordinate(1, 0),
                new GridCoordinate(0, 1)
            });
            ColorBlockEscapeChipperSettings settings = new(
                xySubdivision: subdivision, depthLayers: depthLayers, fragmentGap: 0.02f);

            IReadOnlyList<ColorBlockEscapeFragmentSample> samples =
                ColorBlockEscapeFragmentLayout.Build(footprint, Vector2.one, 0.3f,
                    Vector2.zero, 2f, ExitSide.Top, settings);

            Assert.AreEqual(expected, samples.Count);
            Assert.That(samples[0].LocalScale.x,
                Is.EqualTo(1f / subdivision - 0.02f).Within(0.0001f));
            Assert.That(samples[0].LocalScale.z,
                Is.EqualTo(0.3f / depthLayers - 0.02f).Within(0.0001f));
        }

        [Test]
        public void EveryExitSideProcessesTheLeadingVolumeFirst()
        {
            ShapeFootprint footprint = new(new[] { new GridCoordinate(0, 0) });
            ColorBlockEscapeChipperSettings settings = new(
                xySubdivision: 2, depthLayers: 1);
            var cases = new[]
            {
                (ExitSide.Top, new Vector2(0f, 0f), 1f),
                (ExitSide.Bottom, new Vector2(0f, 0f), 0f),
                (ExitSide.Right, new Vector2(0f, 0f), 1f),
                (ExitSide.Left, new Vector2(0f, 0f), 0f)
            };

            foreach (var item in cases)
            {
                IReadOnlyList<ColorBlockEscapeFragmentSample> samples =
                    ColorBlockEscapeFragmentLayout.Build(footprint, Vector2.one, 0.2f,
                        item.Item2, item.Item3, item.Item1, settings);
                Assert.AreEqual(4, samples.Count, item.Item1.ToString());
                Assert.That(samples[0].ReleaseTravelCells,
                    Is.EqualTo(0.25f).Within(0.0001f), item.Item1.ToString());
                Assert.That(samples[3].ReleaseTravelCells,
                    Is.EqualTo(0.75f).Within(0.0001f), item.Item1.ToString());
            }
        }

        [Test]
        public void ProgressiveEmissionNeverReturnsTheSameSampleTwice()
        {
            ShapeFootprint footprint = new(new[] { new GridCoordinate(0, 0) });
            IReadOnlyList<ColorBlockEscapeFragmentSample> samples =
                ColorBlockEscapeFragmentLayout.Build(footprint, Vector2.one, 0.2f,
                    Vector2.zero, 1f, ExitSide.Top,
                    new ColorBlockEscapeChipperSettings(xySubdivision: 2, depthLayers: 1));
            ColorBlockEscapeFragmentEmissionTracker tracker = new(samples);
            List<ColorBlockEscapeFragmentSample> emitted = new();

            Assert.AreEqual(2, tracker.CollectReady(0.25f, emitted));
            Assert.AreEqual(0, tracker.CollectReady(0.25f, emitted));
            Assert.AreEqual(2, tracker.CollectReady(0.75f, emitted));
            Assert.AreEqual(0, tracker.CollectReady(10f, emitted));
            Assert.AreEqual(4, emitted.Count);
            Assert.AreEqual(4, new HashSet<int>(System.Array.ConvertAll(
                emitted.ToArray(), sample => sample.StableIndex)).Count);
            Assert.IsTrue(tracker.IsComplete);
        }

        [Test]
        public void PresentationGateWaitsForEffectsThenDelayWithoutChangingOutcome()
        {
            ColorBlockEscapePresentationGate gate = new(0.2f, 2f);
            gate.Advance(GameState.Won, true, 0.5f);
            Assert.IsFalse(gate.IsResultVisible);
            gate.Advance(GameState.Won, false, 0.1f);
            Assert.IsFalse(gate.IsResultVisible);
            gate.Advance(GameState.Won, false, 0.1f);
            Assert.IsTrue(gate.IsResultVisible);
        }

        [Test]
        public void PresentationGateTimeoutCannotBeHeldByBrokenPresentation()
        {
            ColorBlockEscapePresentationGate gate = new(0.5f, 1f);
            gate.Advance(GameState.Lost, true, 0.6f);
            Assert.IsFalse(gate.IsResultVisible);
            gate.Advance(GameState.Lost, true, 0.4f);
            Assert.IsTrue(gate.IsResultVisible);
            gate.Reset();
            Assert.IsFalse(gate.IsResultVisible);
        }

        [Test]
        public void FragmentPoolReusesAndFullyResetsCubeInstances()
        {
            GameObject root = new("Fragment pool test");
            Shader shader = Shader.Find("ColorBlockEscape/ChipperClippedUnlit") ??
                            Shader.Find("Universal Render Pipeline/Unlit");
            Assert.IsNotNull(shader);
            Material material = new(shader);
            ColorBlockEscapeFragmentPool pool = new(root.transform, material, 1, 1);
            try
            {
                Assert.IsTrue(pool.TryRent(Vector3.one, Quaternion.Euler(10f, 20f, 30f),
                    new Vector3(0.2f, 0.3f, 0.4f), Color.red,
                    out ColorBlockEscapeFragment first));
                first.ApplyAlpha(0.2f);
                pool.Return(first);
                Assert.AreEqual(0, pool.ActiveCount);
                Assert.IsTrue(pool.TryRent(Vector3.zero, Quaternion.identity,
                    new Vector3(0.5f, 0.5f, 0.5f), Color.green,
                    out ColorBlockEscapeFragment second));
                Assert.AreSame(first, second);
                Assert.AreEqual(Vector3.zero, second.Transform.position);
                Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), second.Transform.localScale);
                MaterialPropertyBlock properties = new();
                second.Renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(properties.GetColor("_BaseColor").g, Is.EqualTo(1f).Within(0.0001f));
                Assert.IsFalse(pool.TryRent(Vector3.zero, Quaternion.identity, Vector3.one,
                    Color.white, out _), "A bounded active pool must reject exhaustion.");
            }
            finally
            {
                pool.Dispose();
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(root);
            }
        }
    }
}
