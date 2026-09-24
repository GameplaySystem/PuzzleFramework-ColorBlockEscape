using System;
using System.Collections.Generic;
using System.Globalization;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>Dedicated CBE Play-mode scene adapter; framework session and tools own the edits.</summary>
    public sealed class ColorBlockEscapeEditorController : MonoBehaviour
    {
        [SerializeField, Range(0.01f, 1f)] private float _captureOverlapFraction = 0.70f;
        [SerializeField, Min(0f)] private float _captureDistanceCells = 0.35f;
        [SerializeField, Min(0.01f)] private float _exitSpeedCellsPerSecond = 3f;
        [SerializeField] private ModularBoardCellView _boardCellPrefab;
        [SerializeField] private ColorBlockEscapeBlockVisualProfile _blockVisualProfile = new();
        private ColorBlockEscapeAuthoringSession _model;
        private ColorBlockEscapeAuthoringTools _tools;
        private GridWorldLayout _layout;
        private Camera _camera;
        private GameObject _authoringRoot;
        private GameObject _playRoot;
        private ColorBlockEscapeRuntimeLevel _playTestLevel;
        private ColorBlockEscapeOutcomeSession _playTestOutcome;
        private GameObject _previewRoot;
        private string _previewKey;
        private ColorBlockEscapeAuthoringMode _mode;
        private int _colorIndex;
        private int _shapeIndex;
        private string _width = "6";
        private string _height = "6";
        private string _exitWidth = "1";
        private string _customOffsets = "0,0;1,0";
        private string _shapeGuard = "4";
        private string _exitX = "0";
        private string _exitY = "0";
        private int _exitSide;
        private string _duration = "60";
        private string _warning = "10";
        private string _path;
        private string _message = "Choose a tool and click the board.";
        private Vector2 _scroll;
        private ColorBlockEscapeBlockMeshPresentation _blockMeshPresentation;

        public ColorBlockEscapeAuthoringSession Model => _model;
        public bool IsPlayTesting => _playRoot != null;
        public ColorBlockEscapeRuntimeLevel PlayTestLevel => _playTestLevel;
        public ColorBlockEscapeOutcomeSession PlayTestOutcome => _playTestOutcome;
        public ColorBlockEscapeAuthoringMode CurrentMode => _mode;

        private void Awake()
        {
            _model = new ColorBlockEscapeAuthoringSession();
            _tools = new ColorBlockEscapeAuthoringTools(_model);
            _layout = new GridWorldLayout(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            _blockMeshPresentation = new ColorBlockEscapeBlockMeshPresentation(_blockVisualProfile);
            _camera = Camera.main;
            if (_camera == null) { Debug.LogError("CBE editor needs a Main Camera.", this); return; }
            _path = System.IO.Path.Combine(Application.persistentDataPath, "color-block-escape-level.json");
            RefreshEditorView();
        }

        private void OnDestroy()
        {
            _blockMeshPresentation?.Dispose();
            _blockMeshPresentation = null;
        }

        private void Update()
        {
            if (_camera == null || IsPlayTesting || Mouse.current == null) return;
            Vector2 screen = Mouse.current.position.ReadValue();
            bool pointerOverBoard = screen.x >= 330f;
            if (GUIUtility.keyboardControl == 0)
                ProcessKeyboardShortcuts();
            if (pointerOverBoard) ProcessShapeScroll(Mouse.current.scroll.ReadValue().y);
            UpdateExitPreview();
            bool place = Mouse.current.leftButton.wasPressedThisFrame;
            bool erase = Mouse.current.rightButton.wasPressedThisFrame;
            if (!pointerOverBoard || (!place && !erase)) return;
            ProcessAuthoringClick(_camera.ScreenPointToRay(screen), erase);
        }

        /// <summary>Routes one board click through the active CBE mode.</summary>
        public bool ProcessAuthoringClick(Ray ray, bool erase)
        {
            bool edgeTool = _mode == ColorBlockEscapeAuthoringMode.PlaceEditExit ||
                            _mode == ColorBlockEscapeAuthoringMode.SelectExit;
            if (!erase && _mode == ColorBlockEscapeAuthoringMode.PlaceEditExit &&
                !TryParsePositive(_exitWidth, out _))
            { _message = "Exit width must be a positive integer."; return false; }
            AuthoringTarget target;
            if (edgeTool)
            {
                WallGenerationResult boundary = _model.Core.CreateBoundary(
                    state => state == AuthoredCellState.Active);
                if (!BoardAuthoringPicker.TryPickBoundaryEdge(ray, _layout, _layout.BoardOrigin,
                        boundary, 0.24f, out BoardBoundaryEdge edge)) return false;
                target = AuthoringTarget.ForBoundaryEdge(edge);
                _exitSide = edge.Direction switch
                {
                    BoardEdgeDirection.North => (int)ExitSide.Top,
                    BoardEdgeDirection.South => (int)ExitSide.Bottom,
                    BoardEdgeDirection.West => (int)ExitSide.Left,
                    _ => (int)ExitSide.Right
                };
                _exitX = edge.CellCoordinate.X.ToString();
                _exitY = edge.CellCoordinate.Y.ToString();
            }
            else
            {
                if (!BoardAuthoringPicker.TryPickCell(ray, _layout, _layout.BoardOrigin,
                        out GridCoordinate cell) || cell.X < 0 || cell.Y < 0 ||
                    cell.X >= _model.Core.Width || cell.Y >= _model.Core.Height) return false;
                target = AuthoringTarget.ForCell(cell);
            }

            if (erase)
            {
                AuthoringEditResult erased = _mode switch
                {
                    ColorBlockEscapeAuthoringMode.PlaceBlock =>
                        _model.EraseBlockAt(target.Cell) ? AuthoringEditResult.Accepted :
                            new AuthoringEditResult(false, "No block at this cell.", null),
                    ColorBlockEscapeAuthoringMode.PlaceEditExit =>
                        _model.EraseExitAt(target.Cell, Side(target.Edge.Direction))
                            ? AuthoringEditResult.Accepted
                            : new AuthoringEditResult(false, "No exit on this edge.", null),
                    _ => new AuthoringEditResult(false,
                        "Right-click erase is available in block or exit placement mode.", null)
                };
                Show(erased);
                if (erased.Success) { SyncExitFields(); RefreshEditorView(); }
                return erased.Success;
            }

            AuthoringEditResult preview = _tools.Host.Preview(target);
            AuthoringEditResult result = preview.Success ? _tools.Host.Apply(target) : preview;
            Show(result);
            if (result.Success) { SyncExitFields(); RefreshEditorView(); }
            return result.Success;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(8, 8, 314, Screen.height - 16), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(IsPlayTesting ? "CBE PLAY-TEST" : "CBE LEVEL EDITOR");
            GUILayout.Label(_message);
            if (IsPlayTesting)
            {
                GUILayout.Label($"Time: {_playTestOutcome.RemainingSeconds:0.0}s | {_playTestOutcome.State}");
                GUILayout.Label("Exit capture and outcomes are active; chipper presentation is a later checkpoint.");
                if (GUILayout.Button("Restart play-test")) RestartPlayTest();
                if (GUILayout.Button("Return to editor")) ReturnFromPlayTest();
                GUILayout.EndScrollView(); GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Mode: {ModeLabel(_mode)}");
            GUILayout.Label("B Block   M Move/select   O Obstacle toggle");
            GUILayout.Label("E Exit place/edit   S Exit select");
            GUILayout.Label("Left click applies. Right click erases in B/E.");
            GUILayout.Label("0-9 choose color. Mouse wheel changes block shape.");

            GUILayout.Space(8);
            GUILayout.Label($"Board {_model.Core.Width} × {_model.Core.Height}");
            GUILayout.BeginHorizontal();
            _width = GUILayout.TextField(_width, GUILayout.Width(45));
            _height = GUILayout.TextField(_height, GUILayout.Width(45));
            if (GUILayout.Button("Resize"))
            {
                if (int.TryParse(_width, out int width) && int.TryParse(_height, out int height))
                    Apply(_model.Resize(width, height));
                else _message = "Enter integer board dimensions.";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Block shape");
            _shapeIndex = GUILayout.SelectionGrid(_shapeIndex, Enum.GetNames(typeof(BlockShapePreset)), 3);
            if (_tools.Shape != (BlockShapePreset)_shapeIndex)
            {
                _tools.Shape = (BlockShapePreset)_shapeIndex;
                _tools.CustomOffsets = null;
            }
            GUILayout.Label("Optional custom offsets: x,y;x,y");
            _customOffsets = GUILayout.TextField(_customOffsets);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use custom shape"))
            {
                if (TryParseOffsets(_customOffsets, out IReadOnlyList<GridCoordinate> offsets))
                { _tools.CustomOffsets = offsets; _message = "Custom footprint selected."; }
                else _message = "Use semicolon-separated integer x,y offsets.";
            }
            if (GUILayout.Button("Use preset")) _tools.CustomOffsets = null;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("New shape guard", GUILayout.Width(112));
            _shapeGuard = GUILayout.TextField(_shapeGuard, GUILayout.Width(42));
            if (GUILayout.Button("Set"))
            {
                if (TryParsePositive(_shapeGuard, out int guard))
                    _model.MaxNewFootprintDimension = guard;
                else _message = "Shape guard must be positive.";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Color slot");
            _colorIndex = GUILayout.SelectionGrid(_colorIndex,
                new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, 5);
            _tools.Color = (ColorIdentity)((int)ColorIdentity.Slot0 + _colorIndex);
            if (_model.Core.SelectedItemId != null)
            {
                GUILayout.Label($"Selected block: {_model.Core.SelectedItemId}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rotate")) Apply(_model.RotateSelectedBlock());
                if (GUILayout.Button("Recolor")) Apply(_model.RecolorSelectedBlock(_tools.Color));
                if (GUILayout.Button("Delete"))
                    Apply(_model.EraseSelectedBlock() ? AuthoringEditResult.Accepted :
                        new AuthoringEditResult(false, "No selected block.", null));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("Exit: click an exposed edge; side/start come from the pick");
            _exitSide = GUILayout.SelectionGrid(_exitSide, Enum.GetNames(typeof(ExitSide)), 4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Start X/Y", GUILayout.Width(76));
            _exitX = GUILayout.TextField(_exitX, GUILayout.Width(42));
            _exitY = GUILayout.TextField(_exitY, GUILayout.Width(42));
            GUILayout.Label("Width", GUILayout.Width(42));
            _exitWidth = GUILayout.TextField(_exitWidth, GUILayout.Width(34));
            GUILayout.EndHorizontal();
            if (_model.SelectedExitId != null)
            {
                GUILayout.Label($"Selected exit: {_model.SelectedExitId}");
                if (GUILayout.Button("Apply side/start/width/color to selected exit"))
                {
                    if (TryExitFields(out GridCoordinate start, out int width))
                        Apply(_model.PutExit(_model.SelectedExitId, (ExitSide)_exitSide,
                            start, width, _tools.Color));
                }
                if (GUILayout.Button("Delete selected exit"))
                    Apply(_model.EraseSelectedExit() ? AuthoringEditResult.Accepted :
                        new AuthoringEditResult(false, "No selected exit.", null));
                if (GUILayout.Button("Create another exit")) _model.ClearExitSelection();
            }
            if (TryParsePositive(_exitWidth, out int exitWidth)) _tools.ExitWidth = exitWidth;

            GUILayout.Space(8);
            GUILayout.Label("Countdown duration / warning seconds");
            GUILayout.BeginHorizontal();
            _duration = GUILayout.TextField(_duration, GUILayout.Width(75));
            _warning = GUILayout.TextField(_warning, GUILayout.Width(75));
            if (GUILayout.Button("Set timer"))
            {
                if (TryFloat(_duration, out float duration) && TryFloat(_warning, out float warning))
                    Apply(_model.SetTimer(duration, warning));
                else _message = "Enter numeric timer values.";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Level JSON path");
            _path = GUILayout.TextField(_path);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                SaveResult result = _model.Save(_path);
                _message = result.Success ? $"Saved {_path}" : result.FailureReason;
            }
            if (GUILayout.Button("Load"))
            {
                if (_model.TryLoad(_path, out string failure))
                {
                    _tools = _tools.RebindAfterLoad();
                    SelectMode(_mode);
                    _width = _model.Core.Width.ToString();
                    _height = _model.Core.Height.ToString();
                    _duration = _model.Core.Timer.DurationSeconds.ToString(CultureInfo.InvariantCulture);
                    _warning = _model.Core.Timer.WarningThresholdSeconds.ToString(CultureInfo.InvariantCulture);
                    _message = $"Loaded {_path}";
                    RefreshEditorView();
                }
                else _message = failure;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Play-test authored level")) BeginPlayTest();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public bool BeginPlayTest()
        {
            if (IsPlayTesting) return true;
            if (!_model.TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel level, out string failure))
            { _message = failure; return false; }
            _playTestLevel = level;
            _playTestOutcome = new ColorBlockEscapeOutcomeSession(level);
            _authoringRoot.SetActive(false);
            ClearPreview();
            _playRoot = new GameObject("CBE isolated play-test");
            _playRoot.transform.SetParent(transform, false);
            Dictionary<string, Transform> views = ColorBlockEscapeAuthoringView.Build(
                _model, level, _layout, _playRoot.transform, _blockMeshPresentation,
                _boardCellPrefab);
            ExitCaptureSettings settings = new(
                _captureOverlapFraction > 0f ? _captureOverlapFraction : 0.70f,
                Mathf.Max(0f, _captureDistanceCells),
                _exitSpeedCellsPerSecond > 0f ? _exitSpeedCellsPerSecond : 3f);
            _playRoot.AddComponent<PlainBlockDragAdapter>().Initialize(level, _layout, _camera,
                views, settings, _playTestOutcome);
            _message = "Play-test running; editor data remains unchanged.";
            return true;
        }

        public void ReturnFromPlayTest()
        {
            if (_playRoot == null) return;
            _playRoot.SetActive(false);
            Destroy(_playRoot);
            _playRoot = null;
            _playTestLevel = null;
            _playTestOutcome = null;
            _authoringRoot.SetActive(true);
            _message = "Returned to the unchanged authoring session.";
        }

        /// <summary>Builds a fresh runtime level and timer from the unchanged authoring data.</summary>
        public bool RestartPlayTest()
        {
            ReturnFromPlayTest();
            return BeginPlayTest();
        }

        private void Apply(AuthoringEditResult result)
        {
            Show(result);
            if (result.Success) RefreshEditorView();
        }

        private void Show(AuthoringEditResult result)
        {
            _message = result.Success ? "Edit applied." : result.Reason;
            if (result.AffectedItemIds.Count > 0)
                _message += " Affected: " + string.Join(", ", result.AffectedItemIds);
            if (result.AffectedCells.Count > 0)
                _message += " Cells: " + string.Join(", ", result.AffectedCells);
        }

        private void RefreshEditorView()
        {
            _previewRoot = null;
            _previewKey = null;
            if (_authoringRoot != null) Destroy(_authoringRoot);
            _authoringRoot = new GameObject("CBE authoring preview");
            _authoringRoot.transform.SetParent(transform, false);
            ColorBlockEscapeAuthoringView.Build(_model, null, _layout, _authoringRoot.transform,
                _blockMeshPresentation, _boardCellPrefab);
            if (_camera != null)
            {
                _camera.orthographic = true;
                _camera.orthographicSize = Mathf.Max(4f, _model.Core.Height * 0.6f,
                    _model.Core.Width * Screen.height / Mathf.Max(1f, Screen.width - 330f) * 0.6f);
                float panelWorldOffset = 165f * 2f * _camera.orthographicSize /
                                         Mathf.Max(1f, Screen.height);
                _camera.transform.position = new Vector3(
                    _model.Core.Width * 0.5f - panelWorldOffset,
                    _model.Core.Height * 0.5f, -10f);
            }
        }

        private void SyncExitFields()
        {
            if (!_model.TryGetSelectedExit(out ExitDefinition exit)) return;
            _exitSide = (int)exit.Side;
            _exitX = exit.StartCell.X.ToString();
            _exitY = exit.StartCell.Y.ToString();
            _exitWidth = exit.Width.ToString();
            _colorIndex = Mathf.Clamp((int)exit.Color - (int)ColorIdentity.Slot0, 0, 9);
            _tools.Color = exit.Color;
        }

        /// <summary>Selects one authoring mode without routing through clickable HUD controls.</summary>
        public bool SelectMode(ColorBlockEscapeAuthoringMode mode)
        {
            if (!_tools.SelectTool(ToolId(mode))) return false;
            _mode = mode;
            ClearPreview();
            _message = $"Mode: {ModeLabel(mode)}";
            return true;
        }

        /// <summary>Processes one mnemonic mode shortcut; useful to scene input and focused tests.</summary>
        public bool ProcessModeShortcut(Key key)
        {
            return key switch
            {
                Key.B => SelectMode(ColorBlockEscapeAuthoringMode.PlaceBlock),
                Key.M => SelectMode(ColorBlockEscapeAuthoringMode.SelectMoveBlock),
                Key.O => SelectMode(ColorBlockEscapeAuthoringMode.ToggleObstacle),
                Key.E => SelectMode(ColorBlockEscapeAuthoringMode.PlaceEditExit),
                Key.S => SelectMode(ColorBlockEscapeAuthoringMode.SelectExit),
                _ => false
            };
        }

        /// <summary>Maps top-row or numpad digits to the shared ten color identities.</summary>
        public bool ProcessColorShortcut(Key key)
        {
            int slot = key switch
            {
                Key.Digit0 or Key.Numpad0 => 0,
                Key.Digit1 or Key.Numpad1 => 1,
                Key.Digit2 or Key.Numpad2 => 2,
                Key.Digit3 or Key.Numpad3 => 3,
                Key.Digit4 or Key.Numpad4 => 4,
                Key.Digit5 or Key.Numpad5 => 5,
                Key.Digit6 or Key.Numpad6 => 6,
                Key.Digit7 or Key.Numpad7 => 7,
                Key.Digit8 or Key.Numpad8 => 8,
                Key.Digit9 or Key.Numpad9 => 9,
                _ => -1
            };
            if (slot < 0) return false;
            _colorIndex = slot;
            _tools.Color = (ColorIdentity)((int)ColorIdentity.Slot0 + slot);
            _message = $"Color slot {slot}.";
            ClearPreview();
            return true;
        }

        /// <summary>Cycles preset block shapes by one mouse-wheel step.</summary>
        public bool ProcessShapeScroll(float scrollY)
        {
            if (Mathf.Approximately(scrollY, 0f)) return false;
            int count = Enum.GetValues(typeof(BlockShapePreset)).Length;
            _shapeIndex = (_shapeIndex + (scrollY > 0f ? -1 : 1) + count) % count;
            _tools.Shape = (BlockShapePreset)_shapeIndex;
            _tools.CustomOffsets = null;
            _message = $"Block shape: {_tools.Shape}.";
            return true;
        }

        private void ProcessKeyboardShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            foreach (Key key in new[] { Key.B, Key.M, Key.O, Key.E, Key.S })
                if (keyboard[key].wasPressedThisFrame) { ProcessModeShortcut(key); return; }
            foreach (Key key in new[]
            {
                Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
                Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
                Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
            })
            {
                if (keyboard[key].wasPressedThisFrame) { ProcessColorShortcut(key); return; }
            }
        }

        private static string ToolId(ColorBlockEscapeAuthoringMode mode) => mode switch
        {
            ColorBlockEscapeAuthoringMode.PlaceBlock => ColorBlockEscapeAuthoringTools.PlaceBlockToolId,
            ColorBlockEscapeAuthoringMode.SelectMoveBlock => ColorBlockEscapeAuthoringTools.SelectMoveToolId,
            ColorBlockEscapeAuthoringMode.ToggleObstacle => ColorBlockEscapeAuthoringTools.ToggleObstacleToolId,
            ColorBlockEscapeAuthoringMode.PlaceEditExit => ColorBlockEscapeAuthoringTools.PlaceEditExitToolId,
            ColorBlockEscapeAuthoringMode.SelectExit => ColorBlockEscapeAuthoringTools.SelectExitToolId,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        private static string ModeLabel(ColorBlockEscapeAuthoringMode mode) => mode switch
        {
            ColorBlockEscapeAuthoringMode.PlaceBlock => "B - Place block",
            ColorBlockEscapeAuthoringMode.SelectMoveBlock => "M - Select / move block",
            ColorBlockEscapeAuthoringMode.ToggleObstacle => "O - Toggle obstacle",
            ColorBlockEscapeAuthoringMode.PlaceEditExit => "E - Place / edit exit",
            ColorBlockEscapeAuthoringMode.SelectExit => "S - Select exit",
            _ => mode.ToString()
        };

        private static ExitSide Side(BoardEdgeDirection direction) => direction switch
        {
            BoardEdgeDirection.North => ExitSide.Top,
            BoardEdgeDirection.South => ExitSide.Bottom,
            BoardEdgeDirection.West => ExitSide.Left,
            BoardEdgeDirection.East => ExitSide.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

        private bool TryExitFields(out GridCoordinate start, out int width)
        {
            start = default;
            width = 0;
            if (!int.TryParse(_exitX, out int x) || !int.TryParse(_exitY, out int y) ||
                !TryParsePositive(_exitWidth, out width))
            { _message = "Enter integer exit start and positive width."; return false; }
            start = new GridCoordinate(x, y);
            return true;
        }

        private static bool TryParsePositive(string value, out int result) =>
            int.TryParse(value, out result) && result > 0;
        private static bool TryFloat(string value, out float result) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        private static bool TryParseOffsets(string text, out IReadOnlyList<GridCoordinate> offsets)
        {
            List<GridCoordinate> parsed = new();
            foreach (string pair in text.Split(';'))
            {
                string[] components = pair.Trim().Split(',');
                if (components.Length != 2 || !int.TryParse(components[0], out int x) ||
                    !int.TryParse(components[1], out int y))
                { offsets = null; return false; }
                parsed.Add(new GridCoordinate(x, y));
            }
            offsets = parsed;
            return parsed.Count > 0;
        }

        private void UpdateExitPreview()
        {
            if (_mode != ColorBlockEscapeAuthoringMode.PlaceEditExit || _authoringRoot == null)
            { ClearPreview(); return; }
            if (!TryParsePositive(_exitWidth, out _))
            { ClearPreview(); _message = "Exit width must be a positive integer."; return; }
            Vector2 screen = Mouse.current.position.ReadValue();
            if (screen.x < 330f) { ClearPreview(); return; }
            WallGenerationResult boundary = _model.Core.CreateBoundary(
                state => state == AuthoredCellState.Active);
            if (!BoardAuthoringPicker.TryPickBoundaryEdge(_camera.ScreenPointToRay(screen),
                    _layout, _layout.BoardOrigin, boundary, 0.24f, out BoardBoundaryEdge edge))
            { ClearPreview(); return; }
            ExitSide side = edge.Direction switch
            {
                BoardEdgeDirection.North => ExitSide.Top,
                BoardEdgeDirection.South => ExitSide.Bottom,
                BoardEdgeDirection.West => ExitSide.Left,
                _ => ExitSide.Right
            };
            int width = _tools.ExitWidth;
            string key = $"{edge.CellCoordinate.X},{edge.CellCoordinate.Y}/{side}/{width}/{_tools.Color}/{_model.SelectedExitId}";
            if (key == _previewKey) return;
            ClearPreview();
            AuthoringEditResult result = _tools.Host.Preview(AuthoringTarget.ForBoundaryEdge(edge));
            _previewRoot = new GameObject("Exit candidate preview");
            _previewRoot.transform.SetParent(_authoringRoot.transform, false);
            ColorBlockEscapeAuthoringView.BuildExitPreview(side, edge.CellCoordinate, width,
                _tools.Color, result.Success, _layout, _previewRoot.transform);
            _previewKey = key;
            _message = result.Success ? "Opening fits this exterior edge." : result.Reason;
        }

        private void ClearPreview()
        {
            if (_previewRoot != null) Destroy(_previewRoot);
            _previewRoot = null;
            _previewKey = null;
        }
    }
}
