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
        private static readonly string[] ToolNames =
        {
            "Place block", "Select / move", "Erase block", "Active cell", "Inactive cell",
            "Blocked cell", "Place / edit exit", "Select exit", "Erase exit"
        };

        private ColorBlockEscapeAuthoringSession _model;
        private ColorBlockEscapeAuthoringTools _tools;
        private GridWorldLayout _layout;
        private Camera _camera;
        private GameObject _authoringRoot;
        private GameObject _playRoot;
        private GameObject _previewRoot;
        private string _previewKey;
        private int _toolIndex;
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

        public ColorBlockEscapeAuthoringSession Model => _model;
        public bool IsPlayTesting => _playRoot != null;

        private void Awake()
        {
            _model = new ColorBlockEscapeAuthoringSession();
            _tools = new ColorBlockEscapeAuthoringTools(_model);
            _layout = new GridWorldLayout(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            _camera = Camera.main;
            if (_camera == null) { Debug.LogError("CBE editor needs a Main Camera.", this); return; }
            _path = System.IO.Path.Combine(Application.persistentDataPath, "color-block-escape-level.json");
            RefreshEditorView();
        }

        private void Update()
        {
            if (_camera == null || IsPlayTesting || Mouse.current == null) return;
            UpdateExitPreview();
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;
            Vector2 screen = Mouse.current.position.ReadValue();
            if (screen.x < 330f) return;
            Ray ray = _camera.ScreenPointToRay(screen);
            bool edgeTool = ToolNames[_toolIndex].Contains("exit");
            if (_toolIndex == 6 && !TryParsePositive(_exitWidth, out _))
            { _message = "Exit width must be a positive integer."; return; }
            AuthoringTarget target;
            if (edgeTool)
            {
                WallGenerationResult boundary = _model.Core.CreateBoundary(
                    state => state == AuthoredCellState.Active);
                if (!BoardAuthoringPicker.TryPickBoundaryEdge(ray, _layout, _layout.BoardOrigin,
                        boundary, 0.24f, out BoardBoundaryEdge edge)) return;
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
                    cell.X >= _model.Core.Width || cell.Y >= _model.Core.Height) return;
                target = AuthoringTarget.ForCell(cell);
            }
            AuthoringEditResult preview = _tools.Host.Preview(target);
            AuthoringEditResult result = preview.Success ? _tools.Host.Apply(target) : preview;
            Show(result);
            if (result.Success) { SyncExitFields(); RefreshEditorView(); }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(8, 8, 314, Screen.height - 16), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(IsPlayTesting ? "CBE PLAY-TEST" : "CBE LEVEL EDITOR");
            GUILayout.Label(_message);
            if (IsPlayTesting)
            {
                GUILayout.Label("Plain block dragging only. Exit capture and outcomes are later checkpoints.");
                if (GUILayout.Button("Return to editor")) ReturnFromPlayTest();
                GUILayout.EndScrollView(); GUILayout.EndArea();
                return;
            }

            int nextTool = GUILayout.SelectionGrid(_toolIndex, ToolNames, 2);
            if (nextTool != _toolIndex)
            {
                _toolIndex = nextTool;
                _tools.SelectTool(ToolNames[_toolIndex]);
                ClearPreview();
            }

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
            _tools.Color = (ColorIdentity)_colorIndex;
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
                    _tools.SelectTool(ToolNames[_toolIndex]);
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
            _authoringRoot.SetActive(false);
            ClearPreview();
            _playRoot = new GameObject("CBE isolated play-test");
            _playRoot.transform.SetParent(transform, false);
            Dictionary<string, Transform> views = ColorBlockEscapeAuthoringView.Build(
                _model, level, _layout, _playRoot.transform);
            _playRoot.AddComponent<PlainBlockDragAdapter>().Initialize(level, _layout, _camera, views);
            _message = "Play-test running; editor data remains unchanged.";
            return true;
        }

        public void ReturnFromPlayTest()
        {
            if (_playRoot == null) return;
            Destroy(_playRoot);
            _playRoot = null;
            _authoringRoot.SetActive(true);
            _message = "Returned to the unchanged authoring session.";
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
            ColorBlockEscapeAuthoringView.Build(_model, null, _layout, _authoringRoot.transform);
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
            _colorIndex = (int)exit.Color;
            _tools.Color = exit.Color;
        }

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
            if (_toolIndex != 6 || _authoringRoot == null)
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
