using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        [SerializeField, Min(0.001f)] private float _outlineWidth = 0.018f;
        [SerializeField, Min(0f)] private float _outlineSurfaceOffset = 0.001f;
        [SerializeField] private Color _outlineColor = new(0.035f, 0.045f, 0.065f, 1f);

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
        public float OutlineWidth => _outlineWidth > 0f ? _outlineWidth : 0.018f;
        public float OutlineSurfaceOffset =>
            _outlineSurfaceOffset > 0f ? _outlineSurfaceOffset : 0.001f;
        public Color OutlineColor => _outlineColor.a > 0f
            ? _outlineColor : new Color(0.035f, 0.045f, 0.065f, 1f);

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
        private readonly ColorBlockEscapeBlockOutlineGenerator _outlineGenerator = new();
        private readonly Dictionary<string, ColorBlockEscapeBlockOutlineGenerationResult>
            _outlineCache = new();

        public ColorBlockEscapeBlockMeshPresentation(
            ColorBlockEscapeBlockVisualProfile profile = null)
        {
            _profile = profile ?? new ColorBlockEscapeBlockVisualProfile();
        }

        public int CachedMeshCount => _cache.CachedMeshCount;
        public int OutlineCachedMeshCount => _outlineCache.Count;
        public float Depth => _profile.Depth;
        public Color OutlineColor => _profile.OutlineColor;

        public FootprintMeshGenerationResult GetOrCreate(
            IReadOnlyList<GridCoordinate> offsets,
            GridWorldLayout layout)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            return _cache.GetOrCreate(new ShapeFootprint(offsets),
                _profile.CreateSettings(layout));
        }

        public ColorBlockEscapeBlockOutlineGenerationResult GetOrCreateOutline(
            IReadOnlyList<GridCoordinate> offsets,
            GridWorldLayout layout)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            string key = OutlineKey(offsets, layout);
            if (_outlineCache.TryGetValue(key, out ColorBlockEscapeBlockOutlineGenerationResult cached))
                return cached;

            ColorBlockEscapeBlockOutlineGenerationResult generated = _outlineGenerator.Generate(
                offsets, layout, _profile.Depth, _profile.BevelWidth, _profile.OutlineWidth,
                _profile.OutlineSurfaceOffset);
            if (generated.Success) _outlineCache.Add(key, generated);
            return generated;
        }

        public void Dispose()
        {
            _cache.Dispose();
            foreach (ColorBlockEscapeBlockOutlineGenerationResult generated in _outlineCache.Values)
            {
                if (generated.Mesh == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(generated.Mesh);
                else UnityEngine.Object.DestroyImmediate(generated.Mesh);
            }
            _outlineCache.Clear();
        }

        private string OutlineKey(IReadOnlyList<GridCoordinate> offsets, GridWorldLayout layout)
        {
            StringBuilder key = new();
            key.Append((int)layout.CellAnchor).Append('|')
                .Append(layout.CellSize.x.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(layout.CellSize.y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(_profile.Depth.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(_profile.BevelWidth.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(_profile.OutlineWidth.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(_profile.OutlineSurfaceOffset.ToString("R", CultureInfo.InvariantCulture));
            foreach (GridCoordinate offset in offsets.OrderBy(value => value.Y).ThenBy(value => value.X))
                key.Append('|').Append(offset.X).Append(',').Append(offset.Y);
            return key.ToString();
        }
    }
}
