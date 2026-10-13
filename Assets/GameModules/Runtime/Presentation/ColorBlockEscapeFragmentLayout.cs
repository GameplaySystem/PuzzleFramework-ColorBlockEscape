using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Presentation
{
    /// <summary>One deterministic cube sample inside an authored block footprint.</summary>
    public readonly struct ColorBlockEscapeFragmentSample
    {
        public ColorBlockEscapeFragmentSample(Vector3 localPosition, Vector3 localScale,
            float releaseTravelCells, int stableIndex)
        {
            LocalPosition = localPosition;
            LocalScale = localScale;
            ReleaseTravelCells = releaseTravelCells;
            StableIndex = stableIndex;
        }

        public Vector3 LocalPosition { get; }
        public Vector3 LocalScale { get; }
        public float ReleaseTravelCells { get; }
        public int StableIndex { get; }
    }

    /// <summary>
    /// Converts a logical footprint into ordered presentation samples. It has no gameplay state.
    /// </summary>
    public static class ColorBlockEscapeFragmentLayout
    {
        public static IReadOnlyList<ColorBlockEscapeFragmentSample> Build(
            ShapeFootprint footprint,
            Vector2 cellSize,
            float extrusionDepth,
            Vector2 alignedOrigin,
            float boundaryPlane,
            ExitSide exitSide,
            ColorBlockEscapeChipperSettings settings)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (cellSize.x <= 0f || cellSize.y <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (extrusionDepth <= 0f || float.IsNaN(extrusionDepth) ||
                float.IsInfinity(extrusionDepth))
                throw new ArgumentOutOfRangeException(nameof(extrusionDepth));

            int subdivision = settings.XYSubdivision;
            int depthLayers = settings.DepthLayers;
            float fragmentX = cellSize.x / subdivision;
            float fragmentY = cellSize.y / subdivision;
            float fragmentZ = extrusionDepth / depthLayers;
            float maximumGap = Mathf.Min(fragmentX, Mathf.Min(fragmentY, fragmentZ)) * 0.9f;
            float gap = Mathf.Min(settings.FragmentGap, maximumGap);
            Vector3 scale = new(
                Mathf.Max(0.001f, fragmentX - gap),
                Mathf.Max(0.001f, fragmentY - gap),
                Mathf.Max(0.001f, fragmentZ - gap));

            List<ColorBlockEscapeFragmentSample> samples = new(
                footprint.Offsets.Count * subdivision * subdivision * depthLayers);
            int stableIndex = 0;
            foreach (GridCoordinate offset in footprint.Offsets)
            {
                for (int depth = 0; depth < depthLayers; depth++)
                for (int y = 0; y < subdivision; y++)
                for (int x = 0; x < subdivision; x++)
                {
                    float normalizedX = (x + 0.5f) / subdivision;
                    float normalizedY = (y + 0.5f) / subdivision;
                    Vector2 logicalOffset = new(offset.X + normalizedX, offset.Y + normalizedY);
                    Vector3 local = new(
                        logicalOffset.x * cellSize.x,
                        logicalOffset.y * cellSize.y,
                        -extrusionDepth * 0.5f + (depth + 0.5f) * fragmentZ);
                    float release = ReleaseTravel(
                        alignedOrigin + logicalOffset, boundaryPlane, exitSide);
                    samples.Add(new ColorBlockEscapeFragmentSample(
                        local, scale, Mathf.Max(0f, release), stableIndex++));
                }
            }

            samples.Sort((left, right) =>
            {
                int threshold = left.ReleaseTravelCells.CompareTo(right.ReleaseTravelCells);
                return threshold != 0 ? threshold : left.StableIndex.CompareTo(right.StableIndex);
            });
            return samples;
        }

        public static Vector2 Outward(ExitSide side) => side switch
        {
            ExitSide.Top => Vector2.up,
            ExitSide.Bottom => Vector2.down,
            ExitSide.Right => Vector2.right,
            _ => Vector2.left
        };

        public static float BoundaryPlane(ExitRuntimeState exit)
        {
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            return exit.Side switch
            {
                ExitSide.Top => exit.StartCell.Y + 1f,
                ExitSide.Bottom => exit.StartCell.Y,
                ExitSide.Right => exit.StartCell.X + 1f,
                _ => exit.StartCell.X
            };
        }

        private static float ReleaseTravel(Vector2 sample, float plane, ExitSide side) => side switch
        {
            ExitSide.Top => plane - sample.y,
            ExitSide.Bottom => sample.y - plane,
            ExitSide.Right => plane - sample.x,
            _ => sample.x - plane
        };
    }

    /// <summary>Monotonic, allocation-free-after-construction emission cursor.</summary>
    public sealed class ColorBlockEscapeFragmentEmissionTracker
    {
        private readonly IReadOnlyList<ColorBlockEscapeFragmentSample> _samples;
        private int _next;

        public ColorBlockEscapeFragmentEmissionTracker(
            IReadOnlyList<ColorBlockEscapeFragmentSample> orderedSamples)
        {
            _samples = orderedSamples ?? throw new ArgumentNullException(nameof(orderedSamples));
        }

        public int TotalCount => _samples.Count;
        public int EmittedCount => _next;
        public bool IsComplete => _next >= _samples.Count;

        public int CollectReady(float outwardTravelCells,
            ICollection<ColorBlockEscapeFragmentSample> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (float.IsNaN(outwardTravelCells) || float.IsInfinity(outwardTravelCells) ||
                outwardTravelCells < 0f)
                throw new ArgumentOutOfRangeException(nameof(outwardTravelCells));
            int before = _next;
            while (_next < _samples.Count &&
                   _samples[_next].ReleaseTravelCells <= outwardTravelCells + 0.0001f)
                destination.Add(_samples[_next++]);
            return _next - before;
        }

        public void Reset() => _next = 0;
    }
}
