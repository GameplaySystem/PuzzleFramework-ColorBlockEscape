using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>CBE-owned visual tuning over the generic footprint mesh contract.</summary>
    [Serializable]
    public sealed class ColorBlockEscapeBlockVisualProfile
    {
        [SerializeField, Min(0.01f)] private float _depth = 0.24f;
        [SerializeField, Min(0f)] private float _bevelWidth = 0.04f;
        [SerializeField, Range(1, 8)] private int _bevelSegments = 2;

        public ColorBlockEscapeBlockVisualProfile()
        {
        }

        public ColorBlockEscapeBlockVisualProfile(
            float depth,
            float bevelWidth,
            int bevelSegments)
        {
            _depth = depth;
            _bevelWidth = bevelWidth;
            _bevelSegments = bevelSegments;
        }

        public float Depth => _depth;
        public float BevelWidth => _bevelWidth;
        public int BevelSegments => _bevelSegments;

        public FootprintMeshSettings CreateSettings(GridWorldLayout layout) =>
            new(layout.CellSize, _depth, _bevelWidth, _bevelSegments, layout.CellAnchor);
    }

    /// <summary>
    /// Scene-lifetime CBE adapter that selects mesh settings while the framework owns generation
    /// and equivalent-footprint caching. It contains no gameplay or color-match behavior.
    /// </summary>
    public sealed class ColorBlockEscapeBlockMeshPresentation : IDisposable
    {
        private readonly ColorBlockEscapeBlockVisualProfile _profile;
        private readonly FootprintMeshCache _cache = new();

        public ColorBlockEscapeBlockMeshPresentation(
            ColorBlockEscapeBlockVisualProfile profile = null)
        {
            _profile = profile ?? new ColorBlockEscapeBlockVisualProfile();
        }

        public int CachedMeshCount => _cache.CachedMeshCount;
        public float Depth => _profile.Depth;

        public FootprintMeshGenerationResult GetOrCreate(
            IReadOnlyList<GridCoordinate> offsets,
            GridWorldLayout layout)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            return _cache.GetOrCreate(new ShapeFootprint(offsets),
                _profile.CreateSettings(layout));
        }

        public void Dispose() => _cache.Dispose();
    }
}
