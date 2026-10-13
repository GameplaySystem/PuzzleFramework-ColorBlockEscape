using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Presentation
{
    /// <summary>One pooled standard-cube fragment and its resettable presentation state.</summary>
    public sealed class ColorBlockEscapeFragment
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly MaterialPropertyBlock _properties = new();
        private Color _baseColor;

        internal ColorBlockEscapeFragment(GameObject gameObject, MeshRenderer renderer)
        {
            GameObject = gameObject;
            Renderer = renderer;
        }

        public GameObject GameObject { get; }
        public Transform Transform => GameObject.transform;
        public MeshRenderer Renderer { get; }
        public bool IsActive { get; internal set; }
        internal Tween ActiveTween { get; set; }

        public void SetColor(Color color)
        {
            _baseColor = color;
            ApplyAlpha(color.a);
        }

        public void ApplyAlpha(float alpha)
        {
            Color color = _baseColor;
            color.a = Mathf.Clamp01(alpha);
            _properties.Clear();
            _properties.SetColor(BaseColorId, color);
            _properties.SetColor(ColorId, color);
            Renderer.SetPropertyBlock(_properties);
        }

        internal void ClearProperties()
        {
            _properties.Clear();
            Renderer.SetPropertyBlock(null);
            _baseColor = Color.white;
        }
    }

    /// <summary>Bounded pool for cube fragments. Exhaustion skips visuals instead of gameplay.</summary>
    public sealed class ColorBlockEscapeFragmentPool : IDisposable
    {
        private readonly Transform _root;
        private readonly Material _sharedMaterial;
        private readonly int _maximumCount;
        private readonly Queue<ColorBlockEscapeFragment> _available = new();
        private readonly HashSet<ColorBlockEscapeFragment> _active = new();
        private readonly List<ColorBlockEscapeFragment> _all = new();
        private bool _disposed;

        public ColorBlockEscapeFragmentPool(Transform root, Material sharedMaterial,
            int prewarmCount, int maximumCount)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _sharedMaterial = sharedMaterial ?? throw new ArgumentNullException(nameof(sharedMaterial));
            if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
            _maximumCount = maximumCount;
            int prewarm = Mathf.Clamp(prewarmCount, 0, maximumCount);
            for (int i = 0; i < prewarm; i++) _available.Enqueue(Create());
        }

        public int CreatedCount => _all.Count;
        public int ActiveCount => _active.Count;
        public int AvailableCount => _available.Count;

        public bool TryRent(Vector3 position, Quaternion rotation, Vector3 scale, Color color,
            out ColorBlockEscapeFragment fragment)
        {
            ThrowIfDisposed();
            if (_available.Count == 0 && _all.Count >= _maximumCount)
            {
                fragment = null;
                return false;
            }
            fragment = _available.Count > 0 ? _available.Dequeue() : Create();
            fragment.ActiveTween?.Kill(false);
            fragment.ActiveTween = null;
            Transform transform = fragment.Transform;
            transform.SetParent(_root, false);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = scale;
            fragment.SetColor(color);
            fragment.IsActive = true;
            fragment.GameObject.SetActive(true);
            _active.Add(fragment);
            return true;
        }

        public void Return(ColorBlockEscapeFragment fragment)
        {
            if (fragment == null || !_active.Remove(fragment)) return;
            fragment.ActiveTween?.Kill(false);
            fragment.ActiveTween = null;
            fragment.ClearProperties();
            fragment.Transform.SetParent(_root, false);
            fragment.Transform.localPosition = Vector3.zero;
            fragment.Transform.localRotation = Quaternion.identity;
            fragment.Transform.localScale = Vector3.one;
            fragment.IsActive = false;
            fragment.GameObject.SetActive(false);
            _available.Enqueue(fragment);
        }

        public void ReturnAll()
        {
            if (_disposed || _active.Count == 0) return;
            ColorBlockEscapeFragment[] active = new ColorBlockEscapeFragment[_active.Count];
            _active.CopyTo(active);
            for (int i = 0; i < active.Length; i++) Return(active[i]);
        }

        public void Dispose()
        {
            if (_disposed) return;
            ReturnAll();
            _disposed = true;
            for (int i = 0; i < _all.Count; i++)
            {
                GameObject instance = _all[i].GameObject;
                if (instance == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
                else UnityEngine.Object.DestroyImmediate(instance);
            }
            _all.Clear();
            _available.Clear();
            _active.Clear();
        }

        private ColorBlockEscapeFragment Create()
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = "Pooled chipper fragment";
            gameObject.transform.SetParent(_root, false);
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            gameObject.SetActive(false);
            ColorBlockEscapeFragment fragment = new(gameObject, renderer);
            _all.Add(fragment);
            return fragment;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ColorBlockEscapeFragmentPool));
        }
    }
}
