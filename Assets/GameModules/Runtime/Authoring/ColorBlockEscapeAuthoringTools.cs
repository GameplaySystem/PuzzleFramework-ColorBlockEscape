using System;
using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;

namespace ColorBlockEscape.Runtime.Authoring
{
    public enum BlockShapePreset { Single, Bar2, Bar3, Square2, Rectangle2x3, Square3, L, T, S, Z }

    /// <summary>Editor-only palette names; saved levels retain explicit offsets.</summary>
    public static class BlockShapePresets
    {
        public static IReadOnlyList<GridCoordinate> Get(BlockShapePreset preset) => preset switch
        {
            BlockShapePreset.Single => Cells((0, 0)),
            BlockShapePreset.Bar2 => Cells((0, 0), (1, 0)),
            BlockShapePreset.Bar3 => Cells((0, 0), (1, 0), (2, 0)),
            BlockShapePreset.Square2 => Cells((0, 0), (1, 0), (0, 1), (1, 1)),
            BlockShapePreset.Rectangle2x3 => Cells((0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2)),
            BlockShapePreset.Square3 => Cells((0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2)),
            BlockShapePreset.L => Cells((0, 0), (0, 1), (0, 2), (1, 0)),
            BlockShapePreset.T => Cells((0, 0), (1, 0), (2, 0), (1, 1)),
            BlockShapePreset.S => Cells((0, 0), (1, 0), (-1, 1), (0, 1)),
            BlockShapePreset.Z => Cells((0, 0), (1, 0), (1, 1), (2, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

        private static IReadOnlyList<GridCoordinate> Cells(params (int x, int y)[] coordinates)
        {
            List<GridCoordinate> cells = new(coordinates.Length);
            foreach ((int x, int y) in coordinates) cells.Add(new GridCoordinate(x, y));
            return cells;
        }
    }

    /// <summary>CBE tool payload and explicit registration over framework target dispatch.</summary>
    public sealed class ColorBlockEscapeAuthoringTools
    {
        private readonly ColorBlockEscapeAuthoringSession _model;
        private int _nextId = 1;

        public ColorBlockEscapeAuthoringTools(ColorBlockEscapeAuthoringSession model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Host = new AuthoringToolHost(model.Core);
            Register("Place block", AuthoringTargetKind.Cell, PlacePreview, PlaceApply);
            Register("Select / move", AuthoringTargetKind.Cell, SelectPreview, SelectApply);
            Register("Erase block", AuthoringTargetKind.Cell, _ => AuthoringEditResult.Accepted, EraseBlock);
            Register("Active cell", AuthoringTargetKind.Cell, _ => AuthoringEditResult.Accepted,
                target => _model.SetCell(target.Cell, AuthoredCellState.Active));
            Register("Inactive cell", AuthoringTargetKind.Cell, _ => AuthoringEditResult.Accepted,
                target => _model.SetCell(target.Cell, AuthoredCellState.Inactive));
            Register("Blocked cell", AuthoringTargetKind.Cell, _ => AuthoringEditResult.Accepted,
                target => _model.SetCell(target.Cell, AuthoredCellState.Blocked));
            Register("Place / edit exit", AuthoringTargetKind.BoundaryEdge, ExitPreview, ExitApply);
            Register("Select exit", AuthoringTargetKind.BoundaryEdge, _ => AuthoringEditResult.Accepted, SelectExit);
            Register("Erase exit", AuthoringTargetKind.BoundaryEdge, _ => AuthoringEditResult.Accepted, EraseExit);
            Host.SelectTool("Place block");
        }

        public AuthoringToolHost Host { get; }
        public BlockShapePreset Shape { get; set; } = BlockShapePreset.Single;
        public ColorIdentity Color { get; set; } = ColorIdentity.Slot0;
        public int ExitWidth { get; set; } = 1;
        public IReadOnlyList<GridCoordinate> CustomOffsets { get; set; }

        public bool SelectTool(string id) => Host.SelectTool(id);

        /// <summary>Rebinds the tool host after a staged load swaps the authoritative live session.</summary>
        public ColorBlockEscapeAuthoringTools RebindAfterLoad() => new(_model)
        { Shape = Shape, Color = Color, ExitWidth = ExitWidth, CustomOffsets = CustomOffsets };

        private AuthoringEditResult PlacePreview(AuthoringTarget target)
        {
            IReadOnlyList<GridCoordinate> offsets = CustomOffsets ?? BlockShapePresets.Get(Shape);
            return _model.PreviewBlock(target.Cell, offsets);
        }

        private AuthoringEditResult PlaceApply(AuthoringTarget target)
        {
            string id;
            do { id = $"block-{_nextId++}"; }
            while (_model.Core.TryGetItem(id, out _) || HasExitId(id));
            return _model.PlaceBlock(id, target.Cell, CustomOffsets ?? BlockShapePresets.Get(Shape), Color);
        }

        private AuthoringEditResult SelectPreview(AuthoringTarget target) =>
            _model.Core.TryFindItemAtCell(target.Cell, out _) || _model.Core.SelectedItemId != null
                ? AuthoringEditResult.Accepted : Reject("Select a block first.");

        private AuthoringEditResult SelectApply(AuthoringTarget target)
        {
            if (_model.SelectBlockAt(target.Cell)) return AuthoringEditResult.Accepted;
            if (_model.Core.SelectedItemId == null) return Reject("Select a block first.");
            return _model.MoveSelectedBlock(target.Cell);
        }

        private AuthoringEditResult EraseBlock(AuthoringTarget target) =>
            _model.EraseBlockAt(target.Cell) ? AuthoringEditResult.Accepted : Reject("No block at this cell.");

        private AuthoringEditResult ExitPreview(AuthoringTarget target)
        {
            ExitSide side = Side(target.Edge.Direction);
            string id = _model.SelectedExitId ?? "preview-exit";
            return _model.PreviewExit(id, side, target.Cell, ExitWidth, Color);
        }

        private AuthoringEditResult ExitApply(AuthoringTarget target)
        {
            ExitSide side = Side(target.Edge.Direction);
            string id = _model.SelectedExitId;
            if (id == null)
            {
                do { id = $"exit-{_nextId++}"; }
                while (_model.Core.TryGetItem(id, out _) || HasExitId(id));
            }
            return _model.PutExit(id, side, target.Cell, ExitWidth, Color);
        }

        private AuthoringEditResult SelectExit(AuthoringTarget target) =>
            _model.SelectExitAt(target.Cell, Side(target.Edge.Direction))
                ? AuthoringEditResult.Accepted : Reject("No exit on this edge.");

        private AuthoringEditResult EraseExit(AuthoringTarget target) =>
            _model.SelectExitAt(target.Cell, Side(target.Edge.Direction)) && _model.EraseSelectedExit()
                ? AuthoringEditResult.Accepted : Reject("No exit on this edge.");

        private bool HasExitId(string id)
        {
            foreach (ExitDefinition exit in _model.Exits) if (exit.Id == id) return true;
            return false;
        }

        private void Register(string id, AuthoringTargetKind kind,
            Func<AuthoringTarget, AuthoringEditResult> preview,
            Func<AuthoringTarget, AuthoringEditResult> apply) =>
            Host.Register(new Tool(id, kind, preview, apply));

        private static ExitSide Side(BoardEdgeDirection direction) => direction switch
        {
            BoardEdgeDirection.North => ExitSide.Top,
            BoardEdgeDirection.South => ExitSide.Bottom,
            BoardEdgeDirection.West => ExitSide.Left,
            BoardEdgeDirection.East => ExitSide.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        private static AuthoringEditResult Reject(string reason) => new(false, reason, null);

        private sealed class Tool : IAuthoringTool
        {
            private readonly AuthoringTargetKind _kind;
            private readonly Func<AuthoringTarget, AuthoringEditResult> _preview;
            private readonly Func<AuthoringTarget, AuthoringEditResult> _apply;

            public Tool(string id, AuthoringTargetKind kind,
                Func<AuthoringTarget, AuthoringEditResult> preview,
                Func<AuthoringTarget, AuthoringEditResult> apply)
            { Id = id; _kind = kind; _preview = preview; _apply = apply; }

            public string Id { get; }
            public AuthoringEditResult Preview(LevelAuthoringCore session, AuthoringTarget target) =>
                target.Kind == _kind ? _preview(target) : Reject("This tool needs a different board target.");
            public AuthoringEditResult Apply(LevelAuthoringCore session, AuthoringTarget target) =>
                target.Kind == _kind ? _apply(target) : Reject("This tool needs a different board target.");
        }
    }
}
