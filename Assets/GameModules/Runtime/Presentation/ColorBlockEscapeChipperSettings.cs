using System;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Presentation
{
    /// <summary>Game-owned, serialized tuning for the Color Block Escape chipper effect.</summary>
    [Serializable]
    public sealed class ColorBlockEscapeChipperSettings
    {
        [Header("Fragment layout")]
        [SerializeField, Range(1, 8)] private int _xySubdivision = 3;
        [SerializeField, Range(1, 8)] private int _depthLayers = 2;
        [SerializeField, Min(0f)] private float _fragmentGap = 0.035f;

        [Header("Timing")]
        [SerializeField, Min(0.01f)] private float _baseDuration = 1.1f;
        [SerializeField, Min(0f)] private float _durationVariation = 0.18f;
        [SerializeField, Min(0f)] private float _releaseStagger = 0.012f;
        [SerializeField, Min(0f)] private float _staggerVariation = 0.018f;

        [Header("Trajectory")]
        [SerializeField, Min(0f)] private float _forwardTravelDistance = 2.2f;
        [SerializeField, Min(0f)] private float _forwardVariation = 0.35f;
        [SerializeField, Min(0f)] private float _sidewaysScatter = 0.32f;
        [SerializeField, Min(0f)] private float _arcHeight = 0.65f;
        [SerializeField, Min(0f)] private float _arcVariation = 0.18f;

        [Header("Rotation")]
        [SerializeField, Min(0f)] private float _rotationAmount = 360f;
        [SerializeField, Min(0f)] private float _rotationVariation = 180f;

        [Header("Disappearance")]
        [SerializeField, Range(0f, 1f)] private float _shrinkStartNormalized = 0.68f;
        [SerializeField, Range(0f, 1f)] private float _finalScale = 0.04f;
        [SerializeField, Range(0f, 1f)] private float _fadeStartNormalized = 0.76f;

        [Header("Pool")]
        [SerializeField, Min(0)] private int _prewarmCount = 64;
        [SerializeField, Min(1)] private int _maximumFragmentCount = 384;

        [Header("Outcome presentation")]
        [SerializeField, Min(0f)] private float _postEffectDelay = 0.25f;
        [SerializeField, Min(0.01f)] private float _maximumWaitTimeout = 4f;
        [SerializeField] private int _deterministicSeed = 1337;

        public ColorBlockEscapeChipperSettings()
        {
        }

        public ColorBlockEscapeChipperSettings(
            int xySubdivision = 3,
            int depthLayers = 2,
            float fragmentGap = 0.035f,
            float baseDuration = 1.1f,
            float durationVariation = 0.18f,
            float releaseStagger = 0.012f,
            float staggerVariation = 0.018f,
            float forwardTravelDistance = 2.2f,
            float forwardVariation = 0.35f,
            float sidewaysScatter = 0.32f,
            float arcHeight = 0.65f,
            float arcVariation = 0.18f,
            float rotationAmount = 360f,
            float rotationVariation = 180f,
            float shrinkStartNormalized = 0.68f,
            float finalScale = 0.04f,
            float fadeStartNormalized = 0.76f,
            int prewarmCount = 64,
            int maximumFragmentCount = 384,
            float postEffectDelay = 0.25f,
            float maximumWaitTimeout = 4f,
            int deterministicSeed = 1337)
        {
            _xySubdivision = xySubdivision;
            _depthLayers = depthLayers;
            _fragmentGap = fragmentGap;
            _baseDuration = baseDuration;
            _durationVariation = durationVariation;
            _releaseStagger = releaseStagger;
            _staggerVariation = staggerVariation;
            _forwardTravelDistance = forwardTravelDistance;
            _forwardVariation = forwardVariation;
            _sidewaysScatter = sidewaysScatter;
            _arcHeight = arcHeight;
            _arcVariation = arcVariation;
            _rotationAmount = rotationAmount;
            _rotationVariation = rotationVariation;
            _shrinkStartNormalized = shrinkStartNormalized;
            _finalScale = finalScale;
            _fadeStartNormalized = fadeStartNormalized;
            _prewarmCount = prewarmCount;
            _maximumFragmentCount = maximumFragmentCount;
            _postEffectDelay = postEffectDelay;
            _maximumWaitTimeout = maximumWaitTimeout;
            _deterministicSeed = deterministicSeed;
        }

        public int XYSubdivision => Mathf.Max(1, _xySubdivision);
        public int DepthLayers => Mathf.Max(1, _depthLayers);
        public float FragmentGap => Mathf.Max(0f, _fragmentGap);
        public float BaseDuration => Mathf.Max(0.01f, _baseDuration);
        public float DurationVariation => Mathf.Max(0f, _durationVariation);
        public float ReleaseStagger => Mathf.Max(0f, _releaseStagger);
        public float StaggerVariation => Mathf.Max(0f, _staggerVariation);
        public float ForwardTravelDistance => Mathf.Max(0f, _forwardTravelDistance);
        public float ForwardVariation => Mathf.Max(0f, _forwardVariation);
        public float SidewaysScatter => Mathf.Max(0f, _sidewaysScatter);
        public float ArcHeight => Mathf.Max(0f, _arcHeight);
        public float ArcVariation => Mathf.Max(0f, _arcVariation);
        public float RotationAmount => Mathf.Max(0f, _rotationAmount);
        public float RotationVariation => Mathf.Max(0f, _rotationVariation);
        public float ShrinkStartNormalized => Mathf.Clamp01(_shrinkStartNormalized);
        public float FinalScale => Mathf.Clamp01(_finalScale);
        public float FadeStartNormalized => Mathf.Clamp01(_fadeStartNormalized);
        public int MaximumFragmentCount => Mathf.Max(1, _maximumFragmentCount);
        public int PrewarmCount => Mathf.Clamp(_prewarmCount, 0, MaximumFragmentCount);
        public float PostEffectDelay => Mathf.Max(0f, _postEffectDelay);
        public float MaximumWaitTimeout => Mathf.Max(0.01f, _maximumWaitTimeout);
        public int DeterministicSeed => _deterministicSeed;
    }
}
