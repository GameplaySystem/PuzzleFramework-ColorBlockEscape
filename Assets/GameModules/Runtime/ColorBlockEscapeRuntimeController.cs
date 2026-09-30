using System;
using System.Collections.Generic;
using ColorBlockEscape.Runtime.Authoring;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Scene composition and view wiring shared by standalone gameplay and editor play-test.
    /// Gameplay rules remain in the runtime level, movement, exit capture, and outcome services.
    /// </summary>
    public sealed class ColorBlockEscapeRuntimeController : MonoBehaviour
    {
        public const string RuntimeRootName = "Color Block Escape Runtime";

        [SerializeField] private TextAsset[] _levelAssets = Array.Empty<TextAsset>();
        [SerializeField, Min(0)] private int _startingLevelIndex;
        [SerializeField] private ModularBoardCellView _boardCellPrefab;
        [SerializeField] private ColorBlockEscapeBlockVisualProfile _blockVisualProfile = new();
        [SerializeField, Range(0.01f, 1f)] private float _captureOverlapFraction = 0.70f;
        [SerializeField, Min(0f)] private float _captureDistanceCells = 0.35f;
        [SerializeField, Min(0.01f)] private float _exitSpeedCellsPerSecond = 3f;
        [SerializeField] private bool _showHud = true;
        [Header("Camera")]
        [SerializeField, Range(1f, 89f)] private float _cameraPitchDegrees =
            ColorBlockEscapeBoardCamera.DefaultPitchDegrees;
        [SerializeField, Range(1f, 179f)] private float _cameraFieldOfView =
            ColorBlockEscapeBoardCamera.DefaultFieldOfView;
        [SerializeField, Min(1f)] private float _cameraFramingPadding =
            ColorBlockEscapeBoardCamera.DefaultFramingPadding;
        [SerializeField, Min(0f)] private float _cameraMinimumDistance =
            ColorBlockEscapeBoardCamera.DefaultMinimumDistance;

        private readonly GridWorldLayout _layout =
            ColorBlockEscapeBoardSpace.CreateLayout(Vector3.zero);
        private Camera _camera;
        private LevelDefinition _sourceDefinition;
        private ColorBlockEscapeRuntimeSession _session;
        private ColorBlockEscapeBlockMeshPresentation _blockMeshPresentation;
        private bool _ownsBlockMeshPresentation;
        private GameObject _runtimeRoot;
        private Dictionary<string, Transform> _blockViews;
        private PlainBlockDragAdapter _dragAdapter;
        private ExitCaptureSettings _activeExitSettings;
        private string _statusMessage = "Waiting for level data.";
        private int _selectedLevelIndex = -1;

        public bool IsRunning => _session != null;
        public int LevelCount => _levelAssets?.Length ?? 0;
        public int SelectedLevelIndex => _selectedLevelIndex;
        public string StatusMessage => _statusMessage;
        public ColorBlockEscapeRuntimeSession Session => _session;
        public ColorBlockEscapeRuntimeLevel Level => _session?.Level;
        public ColorBlockEscapeOutcomeSession Outcome => _session?.Outcome;
        public PlainBlockDragAdapter DragAdapter => _dragAdapter;
        public IReadOnlyDictionary<string, Transform> BlockViews => _blockViews;

        private void Awake()
        {
            _camera = Camera.main;
            EnsureOwnedMeshPresentation();
        }

        private void Start()
        {
            if (IsRunning) return;
            if (_levelAssets == null || _levelAssets.Length == 0)
            {
                _statusMessage = "No authored gameplay level is assigned.";
                Debug.LogError(_statusMessage, this);
                return;
            }
            TrySelectLevel(Mathf.Clamp(_startingLevelIndex, 0, _levelAssets.Length - 1));
        }

        private void OnDestroy()
        {
            if (_ownsBlockMeshPresentation) _blockMeshPresentation?.Dispose();
            _blockMeshPresentation = null;
        }

        /// <summary>Loads and starts one authored JSON asset selected by scene configuration.</summary>
        public bool TrySelectLevel(int index)
        {
            if (_levelAssets == null || index < 0 || index >= _levelAssets.Length ||
                _levelAssets[index] == null)
            {
                _statusMessage = $"Gameplay level index {index} is not assigned.";
                return false;
            }
            if (!TryReadDefinition(_levelAssets[index], out LevelDefinition definition,
                    out string failure))
            {
                _statusMessage = failure;
                return false;
            }
            if (!TryStart(definition)) return false;
            _selectedLevelIndex = index;
            return true;
        }

        /// <summary>
        /// Starts the same detached runtime composition from an in-memory authored definition.
        /// Editor play-test uses this entry point; standalone gameplay uses TrySelectLevel.
        /// </summary>
        public bool TryStart(LevelDefinition definition, Camera sceneCamera = null,
            ModularBoardCellView boardCellPrefab = null,
            ColorBlockEscapeBlockMeshPresentation blockMeshPresentation = null,
            ExitCaptureSettings exitSettings = null, bool showHud = true)
        {
            if (definition == null)
            {
                _statusMessage = "A level definition is required.";
                return false;
            }
            if (sceneCamera != null) _camera = sceneCamera;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null)
            {
                _statusMessage = "Color Block Escape runtime needs a scene camera.";
                return false;
            }
            if (boardCellPrefab != null) _boardCellPrefab = boardCellPrefab;
            if (blockMeshPresentation != null)
            {
                if (_ownsBlockMeshPresentation) _blockMeshPresentation?.Dispose();
                _blockMeshPresentation = blockMeshPresentation;
                _ownsBlockMeshPresentation = false;
            }
            else EnsureOwnedMeshPresentation();

            _showHud = showHud;
            ExitCaptureSettings requestedSettings = exitSettings ?? CreateExitSettings();
            if (!BuildFresh(definition, requestedSettings)) return false;
            _sourceDefinition = definition;
            _activeExitSettings = requestedSettings;
            return true;
        }

        /// <summary>Discards all mutable runtime state and reconstructs the selected level.</summary>
        public bool RestartCurrentLevel()
        {
            if (_sourceDefinition == null)
            {
                _statusMessage = "No gameplay level is available to restart.";
                return false;
            }
            return BuildFresh(_sourceDefinition, _activeExitSettings ?? CreateExitSettings());
        }

        private bool BuildFresh(LevelDefinition definition, ExitCaptureSettings exitSettings)
        {
            if (!ColorBlockEscapeRuntimeSession.TryCreate(definition,
                    out ColorBlockEscapeRuntimeSession session, out string failure))
            {
                _statusMessage = failure;
                Debug.LogError(failure, this);
                return false;
            }

            ClearRuntimeObjects();
            _session = session;
            _runtimeRoot = new GameObject(RuntimeRootName);
            _runtimeRoot.transform.SetParent(transform, false);
            _blockViews = ColorBlockEscapeAuthoringView.BuildRuntime(
                definition, session.Level, _layout, _runtimeRoot.transform,
                _blockMeshPresentation, _boardCellPrefab);
            _dragAdapter = _runtimeRoot.AddComponent<PlainBlockDragAdapter>();
            _dragAdapter.Initialize(session.Level, _layout, _camera, _blockViews,
                exitSettings, session.Outcome);
            FrameCamera(definition.FrameworkData.Board);
            _statusMessage = $"Playing {definition.Metadata.DisplayName}.";
            return true;
        }

        private void ClearRuntimeObjects()
        {
            _session = null;
            _dragAdapter = null;
            _blockViews = null;
            if (_runtimeRoot == null) return;
            _runtimeRoot.SetActive(false);
            if (Application.isPlaying) Destroy(_runtimeRoot);
            else DestroyImmediate(_runtimeRoot);
            _runtimeRoot = null;
        }

        private void FrameCamera(BoardDefinitionData board)
        {
            _camera.rect = new Rect(0f, 0f, 1f, 1f);
            if (!ColorBlockEscapeBoardCamera.TryFrame(
                    _camera,
                    _layout,
                    board.Width,
                    board.Height,
                    _cameraPitchDegrees,
                    _cameraFieldOfView,
                    _cameraFramingPadding,
                    _cameraMinimumDistance,
                    out string failure))
                Debug.LogError($"CBE camera framing failed: {failure}", this);
        }

        private void EnsureOwnedMeshPresentation()
        {
            if (_blockMeshPresentation != null) return;
            _blockMeshPresentation = new ColorBlockEscapeBlockMeshPresentation(
                _blockVisualProfile ?? new ColorBlockEscapeBlockVisualProfile());
            _ownsBlockMeshPresentation = true;
        }

        private ExitCaptureSettings CreateExitSettings() => new(
            _captureOverlapFraction > 0f ? _captureOverlapFraction : 0.70f,
            Mathf.Max(0f, _captureDistanceCells),
            _exitSpeedCellsPerSecond > 0f ? _exitSpeedCellsPerSecond : 3f);

        private static bool TryReadDefinition(TextAsset asset,
            out LevelDefinition definition, out string failure)
        {
            definition = null;
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                failure = "The selected gameplay level asset is empty.";
                return false;
            }
            try
            {
                definition = JsonUtility.FromJson<LevelDefinition>(asset.text);
            }
            catch (Exception exception)
            {
                failure = $"The selected gameplay level JSON cannot be parsed: {exception.Message}";
                return false;
            }
            if (definition == null)
            {
                failure = "The selected gameplay level JSON produced no level definition.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private void OnGUI()
        {
            if (!_showHud) return;
            GUILayout.BeginArea(new Rect(12f, 12f, 280f, 150f), GUI.skin.box);
            GUILayout.Label("COLOR BLOCK ESCAPE");
            if (_session == null)
            {
                GUILayout.Label(_statusMessage);
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label($"Time: {_session.Outcome.RemainingSeconds:0.0}s");
            GameState state = _session.Outcome.State;
            GUILayout.Label(state == GameState.Playing ? "Clear every block." :
                state == GameState.Won ? "LEVEL COMPLETE" : "TIME UP");
            if (GUILayout.Button("Restart")) RestartCurrentLevel();
            if (LevelCount > 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous"))
                    TrySelectLevel((_selectedLevelIndex - 1 + LevelCount) % LevelCount);
                if (GUILayout.Button("Next"))
                    TrySelectLevel((_selectedLevelIndex + 1) % LevelCount);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
    }
}
