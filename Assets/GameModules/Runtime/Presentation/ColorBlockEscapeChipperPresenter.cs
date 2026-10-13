using System;
using System.Collections.Generic;
using DG.Tweening;
using PuzzleFramework.CoreBoard;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Presentation
{
    /// <summary>
    /// CBE-owned visual observer for accepted blocks. Gameplay state is read but never mutated.
    /// </summary>
    public sealed class ColorBlockEscapeChipperPresenter : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");
        private static readonly int ClipEnabledId = Shader.PropertyToID("_ClipEnabled");

        private readonly Dictionary<string, ActiveEffect> _effects = new(StringComparer.Ordinal);
        private readonly List<ColorBlockEscapeFragmentSample> _ready = new();
        private ColorBlockEscapeRuntimeLevel _level;
        private GridWorldLayout _layout;
        private IReadOnlyDictionary<string, Transform> _blockViews;
        private ColorBlockEscapeChipperSettings _settings;
        private float _blockDepth;
        private Material _presentationMaterial;
        private ColorBlockEscapeFragmentPool _pool;
        private bool _initialized;
        private bool _poolWarningReported;
        private int _skippedFragments;

        public bool IsBusy => _effects.Count > 0;
        public int ActiveEffectCount => _effects.Count;
        public int ActiveFragmentCount => _pool?.ActiveCount ?? 0;
        public int CreatedFragmentCount => _pool?.CreatedCount ?? 0;
        public int SkippedFragmentCount => _skippedFragments;

        public void Initialize(ColorBlockEscapeRuntimeLevel level, GridWorldLayout layout,
            IReadOnlyDictionary<string, Transform> blockViews, float blockDepth,
            ColorBlockEscapeChipperSettings settings)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (blockViews == null) throw new ArgumentNullException(nameof(blockViews));
            if (blockDepth <= 0f) throw new ArgumentOutOfRangeException(nameof(blockDepth));
            ResetPresentation();
            DisposeResources();

            _level = level;
            _layout = layout;
            _blockViews = blockViews;
            _blockDepth = blockDepth;
            _settings = settings ?? new ColorBlockEscapeChipperSettings();
            Shader shader = Shader.Find("ColorBlockEscape/ChipperClippedUnlit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                Debug.LogError("CBE chipper clipping shader was not found; fragments will continue " +
                               "but the intact block cannot be clipped.", this);
            }
            _presentationMaterial = new Material(shader)
            {
                name = "CBE chipper shared presentation material"
            };
            GameObject poolRoot = new("Chipper fragment pool");
            poolRoot.transform.SetParent(transform, false);
            _pool = new ColorBlockEscapeFragmentPool(poolRoot.transform, _presentationMaterial,
                _settings.PrewarmCount, _settings.MaximumFragmentCount);
            _initialized = true;
        }

        /// <summary>Advances presentation after gameplay movement for the current frame.</summary>
        public void Advance(float deltaTimeSeconds)
        {
            if (!_initialized) return;
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) ||
                deltaTimeSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));

            foreach (BlockRuntimeState block in _level.Blocks)
            {
                if (block.Lifecycle == BlockLifecycle.Exiting && !_effects.ContainsKey(block.Id))
                    TryBegin(block);
                if (!_effects.TryGetValue(block.Id, out ActiveEffect effect)) continue;
                EmitReady(effect);
            }

            if (_effects.Count == 0) return;
            List<string> completed = null;
            foreach (KeyValuePair<string, ActiveEffect> pair in _effects)
            {
                ActiveEffect effect = pair.Value;
                if (effect.Block.Lifecycle != BlockLifecycle.Removed ||
                    !effect.Emission.IsComplete || effect.LiveFragments > 0) continue;
                completed ??= new List<string>();
                completed.Add(pair.Key);
            }
            if (completed == null) return;
            for (int i = 0; i < completed.Count; i++) _effects.Remove(completed[i]);
        }

        public void ResetPresentation()
        {
            foreach (ActiveEffect effect in _effects.Values) RestoreRenderer(effect);
            _effects.Clear();
            _ready.Clear();
            _pool?.ReturnAll();
            _poolWarningReported = false;
            _skippedFragments = 0;
        }

        private void OnDestroy()
        {
            ResetPresentation();
            DisposeResources();
        }

        private void TryBegin(BlockRuntimeState block)
        {
            if (!_level.TryGetExit(block.AcceptedExitId, out ExitRuntimeState exit) ||
                !_blockViews.TryGetValue(block.Id, out Transform view) || view == null)
            {
                Debug.LogWarning($"CBE chipper could not find the accepted exit or view for " +
                                 $"block '{block.Id}'.", this);
                return;
            }
            MeshRenderer renderer = view.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"CBE chipper block '{block.Id}' has no mesh renderer.", this);
                return;
            }

            float plane = ColorBlockEscapeFragmentLayout.BoundaryPlane(exit);
            IReadOnlyList<ColorBlockEscapeFragmentSample> samples =
                ColorBlockEscapeFragmentLayout.Build(block.Footprint, _layout.CellSize,
                    _blockDepth, new Vector2(block.CommittedOrigin.X, block.CommittedOrigin.Y),
                    plane, exit.Side, _settings);
            Material[] originalMaterials = renderer.sharedMaterials;
            Color[] colors = new Color[Mathf.Max(1, originalMaterials.Length)];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = MaterialColor(i < originalMaterials.Length ? originalMaterials[i] : null);

            Vector2 boardOutward = ColorBlockEscapeFragmentLayout.Outward(exit.Side);
            Vector3 worldOutward = (_layout.BoardXAxis * boardOutward.x +
                                    _layout.BoardYAxis * boardOutward.y).normalized;
            Vector2 planeBoardPoint = exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom
                ? new Vector2(0f, plane) : new Vector2(plane, 0f);
            Vector3 worldPlanePoint = _layout.BoardLocalToWorld(planeBoardPoint);
            Vector4 clipPlane = new(worldOutward.x, worldOutward.y, worldOutward.z,
                -Vector3.Dot(worldOutward, worldPlanePoint));
            Material[] clipped = new Material[colors.Length];
            for (int i = 0; i < clipped.Length; i++) clipped[i] = _presentationMaterial;
            renderer.sharedMaterials = clipped;
            for (int i = 0; i < colors.Length; i++)
            {
                MaterialPropertyBlock properties = new();
                properties.SetColor(BaseColorId, colors[i]);
                properties.SetColor(ColorId, colors[i]);
                properties.SetVector(ClipPlaneId, clipPlane);
                properties.SetFloat(ClipEnabledId, 1f);
                renderer.SetPropertyBlock(properties, i);
            }

            ActiveEffect effect = new(block, view, renderer, originalMaterials, colors[0],
                worldOutward, new ColorBlockEscapeFragmentEmissionTracker(samples));
            _effects.Add(block.Id, effect);
            EmitReady(effect);
        }

        private void EmitReady(ActiveEffect effect)
        {
            _ready.Clear();
            effect.Emission.CollectReady(effect.Block.OutwardTravelCells, _ready);
            for (int i = 0; i < _ready.Count; i++) Emit(effect, _ready[i], i);
        }

        private void Emit(ActiveEffect effect, ColorBlockEscapeFragmentSample sample, int batchIndex)
        {
            Vector3 position = effect.View.TransformPoint(sample.LocalPosition);
            Vector3 scale = sample.LocalScale;
            if (!_pool.TryRent(position, effect.View.rotation, scale, effect.BodyColor,
                    out ColorBlockEscapeFragment fragment))
            {
                _skippedFragments++;
                if (!_poolWarningReported)
                {
                    _poolWarningReported = true;
                    Debug.LogWarning("CBE chipper fragment pool reached its configured maximum; " +
                                     "remaining samples will be skipped without affecting gameplay.", this);
                }
                return;
            }

            effect.LiveFragments++;
            System.Random random = new(StableSeed(effect.Block.Id, sample.StableIndex,
                _settings.DeterministicSeed));
            float duration = Mathf.Max(0.05f, _settings.BaseDuration +
                Symmetric(random, _settings.DurationVariation));
            float delay = batchIndex * _settings.ReleaseStagger +
                          Range(random, 0f, _settings.StaggerVariation);
            float forward = Mathf.Max(0f, _settings.ForwardTravelDistance +
                Symmetric(random, _settings.ForwardVariation));
            float sideAmount = Symmetric(random, _settings.SidewaysScatter);
            float arc = Mathf.Max(0f, _settings.ArcHeight +
                Symmetric(random, _settings.ArcVariation));
            Vector3 up = ColorBlockEscapeBoardSpace.Up(_layout);
            Vector3 side = Vector3.Cross(up, effect.WorldOutward).normalized;
            Vector3 start = position;
            Vector3 initialScale = scale;
            float progress = 0f;
            Tween movement = DOTween.To(() => progress, value =>
                {
                    progress = value;
                    float parabola = 4f * arc * value * (1f - value);
                    float fall = arc * 0.65f * value * value;
                    fragment.Transform.position = start + effect.WorldOutward * (forward * value) +
                                                  side * (sideAmount * value) +
                                                  up * (parabola - fall);
                }, 1f, duration)
                .SetEase(Ease.Linear);
            Vector3 axis = new(Symmetric(random, 1f), Symmetric(random, 1f), Symmetric(random, 1f));
            if (axis.sqrMagnitude < 0.001f) axis = Vector3.up;
            float rotation = Mathf.Max(0f, _settings.RotationAmount +
                Symmetric(random, _settings.RotationVariation));
            Tween rotate = fragment.Transform.DORotate(
                fragment.Transform.eulerAngles + axis.normalized * rotation,
                duration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic);
            float shrinkStart = duration * _settings.ShrinkStartNormalized;
            float fadeStart = duration * _settings.FadeStartNormalized;
            Sequence sequence = DOTween.Sequence().SetDelay(delay).SetTarget(fragment.GameObject);
            sequence.Join(movement);
            sequence.Join(rotate);
            sequence.Insert(shrinkStart, fragment.Transform.DOScale(
                initialScale * _settings.FinalScale,
                Mathf.Max(0.01f, duration - shrinkStart)).SetEase(Ease.InQuad));
            sequence.Insert(fadeStart, DOTween.To(() => 1f, fragment.ApplyAlpha, 0f,
                Mathf.Max(0.01f, duration - fadeStart)).SetEase(Ease.InQuad));
            sequence.OnComplete(() => CompleteFragment(effect, fragment));
            fragment.ActiveTween = sequence;
        }

        private void CompleteFragment(ActiveEffect effect, ColorBlockEscapeFragment fragment)
        {
            fragment.ActiveTween = null;
            _pool.Return(fragment);
            effect.LiveFragments = Mathf.Max(0, effect.LiveFragments - 1);
        }

        private static Color MaterialColor(Material material)
        {
            if (material == null) return Color.white;
            if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
            if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
            return Color.white;
        }

        private static int StableSeed(string blockId, int sampleIndex, int baseSeed)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < blockId.Length; i++) hash = (hash ^ blockId[i]) * 16777619;
                hash = (hash ^ (uint)sampleIndex) * 16777619;
                hash = (hash ^ (uint)baseSeed) * 16777619;
                return (int)hash;
            }
        }

        private static float Symmetric(System.Random random, float magnitude) =>
            ((float)random.NextDouble() * 2f - 1f) * magnitude;

        private static float Range(System.Random random, float minimum, float maximum) =>
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble());

        private static void RestoreRenderer(ActiveEffect effect)
        {
            if (effect.Renderer == null) return;
            effect.Renderer.sharedMaterials = effect.OriginalMaterials;
            for (int i = 0; i < effect.OriginalMaterials.Length; i++)
                effect.Renderer.SetPropertyBlock(null, i);
        }

        private void DisposeResources()
        {
            _pool?.Dispose();
            _pool = null;
            if (_presentationMaterial != null)
            {
                if (Application.isPlaying) Destroy(_presentationMaterial);
                else DestroyImmediate(_presentationMaterial);
            }
            _presentationMaterial = null;
            _initialized = false;
        }

        private sealed class ActiveEffect
        {
            public ActiveEffect(BlockRuntimeState block, Transform view, MeshRenderer renderer,
                Material[] originalMaterials, Color bodyColor, Vector3 worldOutward,
                ColorBlockEscapeFragmentEmissionTracker emission)
            {
                Block = block;
                View = view;
                Renderer = renderer;
                OriginalMaterials = originalMaterials;
                BodyColor = bodyColor;
                WorldOutward = worldOutward;
                Emission = emission;
            }

            public BlockRuntimeState Block { get; }
            public Transform View { get; }
            public MeshRenderer Renderer { get; }
            public Material[] OriginalMaterials { get; }
            public Color BodyColor { get; }
            public Vector3 WorldOutward { get; }
            public ColorBlockEscapeFragmentEmissionTracker Emission { get; }
            public int LiveFragments { get; set; }
        }
    }
}
