using System;
using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>Game-owned block colors and exits over one authoritative framework authoring session.</summary>
    public sealed class ColorBlockEscapeAuthoringSession
    {
        private readonly Dictionary<string, ColorIdentity> _colors = new(StringComparer.Ordinal);
        private readonly List<ExitDefinition> _exits = new();
        private readonly ColorBlockEscapeRuntimeBuilder _builder = new();
        private readonly JsonLevelSaveLoadService _storage = new();

        public ColorBlockEscapeAuthoringSession(int width = 6, int height = 6)
        {
            Core = new LevelAuthoringCore(width, height);
            Core.SetMetadata("color-block-escape-level", "Color Block Escape", 1);
            Core.SetTimer(true, TimerMode.Countdown, 60f, 10f);
        }

        public LevelAuthoringCore Core { get; private set; }
        public IReadOnlyList<ExitDefinition> Exits => _exits;
        public string SelectedExitId { get; private set; }
        public int MaxNewFootprintDimension { get; set; } = 4;
        public void ClearExitSelection() => SelectedExitId = null;

        public bool TryGetBlockColor(string id, out ColorIdentity color) => _colors.TryGetValue(id, out color);

        public AuthoringEditResult Resize(int width, int height) =>
            Core.TryResize(width, height, (w, h) => AffectedExits(CandidateResize(w, h)));

        public AuthoringEditResult SetCell(GridCoordinate cell, AuthoredCellState state) =>
            Core.TrySetCellState(cell, state,
                (coordinate, next) => AffectedExits(CandidateCellChange(coordinate, next)));

        public AuthoringEditResult PlaceBlock(string id, GridCoordinate origin,
            IReadOnlyList<GridCoordinate> offsets, ColorIdentity color)
        {
            if (!ValidColor(color)) return Reject("Choose a playable block color.");
            if (string.IsNullOrWhiteSpace(id) || _colors.ContainsKey(id) || ExitIdExists(id))
                return Reject($"Block ID '{id}' is missing or already used.");
            AuthoringEditResult shape = ValidateNewFootprint(offsets);
            if (!shape.Success) return shape;
            AuthoredFootprint item;
            try { item = new AuthoredFootprint(id, origin, offsets); }
            catch (ArgumentException exception) { return Reject(exception.Message); }
            AuthoringEditResult result = Core.TryPlaceOrMove(item);
            if (!result.Success) return result;
            _colors.Add(id, color);
            Core.SelectItem(id);
            SelectedExitId = null;
            return result;
        }

        public AuthoringEditResult PreviewBlock(GridCoordinate origin,
            IReadOnlyList<GridCoordinate> offsets)
        {
            AuthoringEditResult shape = ValidateNewFootprint(offsets);
            string previewId = "candidate-block-preview";
            while (Core.TryGetItem(previewId, out _)) previewId += "_";
            return shape.Success
                ? Core.EvaluatePlacement(new AuthoredFootprint(previewId, origin, offsets))
                : shape;
        }

        public bool SelectBlockAt(GridCoordinate cell)
        {
            if (!Core.SelectItemAtCell(cell)) return false;
            SelectedExitId = null;
            return true;
        }

        public AuthoringEditResult MoveSelectedBlock(GridCoordinate origin) =>
            Core.TryMoveItem(Core.SelectedItemId, origin);

        public AuthoringEditResult RotateSelectedBlock()
        {
            if (Core.SelectedItemId == null) return Reject("Select a block to rotate.");
            return Core.TryRotateItemClockwise(Core.SelectedItemId);
        }

        public AuthoringEditResult RecolorSelectedBlock(ColorIdentity color)
        {
            if (Core.SelectedItemId == null || !_colors.ContainsKey(Core.SelectedItemId))
                return Reject("Select a block to recolor.");
            if (!ValidColor(color)) return Reject("Choose a playable block color.");
            _colors[Core.SelectedItemId] = color;
            return AuthoringEditResult.Accepted;
        }

        public bool EraseBlockAt(GridCoordinate cell)
        {
            if (!Core.TryFindItemAtCell(cell, out AuthoredFootprint item)) return false;
            _colors.Remove(item.Id);
            return Core.Erase(item.Id);
        }

        public bool EraseSelectedBlock()
        {
            string id = Core.SelectedItemId;
            if (id == null) return false;
            _colors.Remove(id);
            return Core.Erase(id);
        }

        public bool SelectExitAt(GridCoordinate edgeCell, ExitSide side)
        {
            foreach (ExitDefinition exit in _exits)
                if (exit.Side == side)
                    for (int index = 0; index < exit.Width; index++)
                        if (ExitBoundaryValidator.EdgeCellAt(exit, index) == edgeCell)
                        {
                            SelectedExitId = exit.Id;
                            Core.SelectCell(edgeCell);
                            return true;
                        }
            return false;
        }

        public bool TryGetSelectedExit(out ExitDefinition exit)
        {
            exit = _exits.Find(candidate => candidate.Id == SelectedExitId);
            return exit != null;
        }

        public AuthoringEditResult PreviewExit(string id, ExitSide side, GridCoordinate start,
            int width, ColorIdentity color) => ValidateExit(id, side, start, width, color);

        public AuthoringEditResult PutExit(string id, ExitSide side, GridCoordinate start,
            int width, ColorIdentity color)
        {
            AuthoringEditResult result = ValidateExit(id, side, start, width, color);
            if (!result.Success) return result;
            ExitDefinition replacement = NewExit(id, side, start, width, color);
            int index = _exits.FindIndex(candidate => candidate.Id == id);
            if (index >= 0) _exits[index] = replacement;
            else _exits.Add(replacement);
            SelectedExitId = id;
            Core.SelectCell(start);
            return result;
        }

        public bool EraseSelectedExit()
        {
            if (SelectedExitId == null) return false;
            int removed = _exits.RemoveAll(exit => exit.Id == SelectedExitId);
            SelectedExitId = null;
            return removed > 0;
        }

        public AuthoringEditResult SetTimer(float duration, float warning)
        {
            if (duration <= 0f || warning < 0f || warning > duration ||
                float.IsNaN(duration) || float.IsInfinity(duration) ||
                float.IsNaN(warning) || float.IsInfinity(warning))
                return Reject("Countdown duration must be positive and warning within its range.");
            Core.SetTimer(true, TimerMode.Countdown, duration, warning);
            return AuthoringEditResult.Accepted;
        }

        public bool TryCreateLevel(out LevelDefinition definition, out string failure)
        {
            definition = null;
            ColorBlockEscapeLevelData payload = CreatePayload();
            if (!ColorBlockEscapeLevelCodec.TryEncode(payload, out string json, out failure)) return false;
            definition = Core.CreateLevelSnapshot(ColorBlockEscapeLevelCodec.ContentTypeId, json);
            return _builder.TryBuild(definition, out _, out failure);
        }

        public SaveResult Save(string path)
        {
            if (!TryCreateLevel(out LevelDefinition definition, out string failure))
                return SaveResult.Failed(failure);
            return _storage.Save(definition, path);
        }

        /// <summary>Stages codec, structural session and runtime validation before replacing live state.</summary>
        public bool TryLoad(LevelDefinition definition, out string failure)
        {
            if (!ColorBlockEscapeLevelCodec.TryDecode(definition, out ColorBlockEscapeLevelData payload,
                    out failure)) return false;
            if (definition.FrameworkData?.Board == null || definition.Metadata == null ||
                definition.FrameworkData.Timer == null)
            { failure = "Level framework board, metadata and timer are required."; return false; }
            List<AuthoredFootprint> items = new(payload.Blocks.Count);
            foreach (BlockDefinition block in payload.Blocks)
            {
                List<GridCoordinate> offsets = new(block.FootprintOffsets.Count);
                foreach (CellCoordinateData offset in block.FootprintOffsets)
                    offsets.Add(new GridCoordinate(offset.X, offset.Y));
                items.Add(new AuthoredFootprint(block.Id,
                    new GridCoordinate(block.Origin.X, block.Origin.Y), offsets));
            }
            if (!LevelAuthoringCore.TryRestore(definition.FrameworkData.Board, items,
                    definition.Metadata, definition.FrameworkData.Timer, out LevelAuthoringCore candidate,
                    out failure)) return false;
            if (!_builder.TryBuild(definition, out _, out failure)) return false;
            Core = candidate;
            _colors.Clear();
            foreach (BlockDefinition block in payload.Blocks) _colors.Add(block.Id, block.Color);
            _exits.Clear();
            foreach (ExitDefinition exit in payload.Exits)
                _exits.Add(NewExit(exit.Id, exit.Side,
                    new GridCoordinate(exit.StartCell.X, exit.StartCell.Y), exit.Width, exit.Color));
            SelectedExitId = null;
            return true;
        }

        public bool TryLoad(string path, out string failure)
        {
            LoadResult<LevelDefinition> loaded = _storage.Load(path);
            if (!loaded.Success) { failure = loaded.FailureReason; return false; }
            return TryLoad(loaded.Data, out failure);
        }

        public bool TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel level, out string failure)
        {
            level = null;
            if (!TryCreateLevel(out LevelDefinition definition, out failure)) return false;
            return _builder.TryBuild(definition, out level, out failure);
        }

        private ColorBlockEscapeLevelData CreatePayload()
        {
            ColorBlockEscapeLevelData payload = new()
            {
                Version = ColorBlockEscapeLevelCodec.CurrentVersion,
                Blocks = new List<BlockDefinition>(), Exits = new List<ExitDefinition>()
            };
            foreach (AuthoredFootprint item in Core.Items)
            {
                BlockDefinition block = new()
                {
                    Id = item.Id, Color = _colors[item.Id],
                    Origin = Cell(item.Origin), FootprintOffsets = new List<CellCoordinateData>()
                };
                foreach (GridCoordinate offset in item.Offsets) block.FootprintOffsets.Add(Cell(offset));
                payload.Blocks.Add(block);
            }
            foreach (ExitDefinition exit in _exits)
                payload.Exits.Add(NewExit(exit.Id, exit.Side,
                    new GridCoordinate(exit.StartCell.X, exit.StartCell.Y), exit.Width, exit.Color));
            return payload;
        }

        private AuthoringEditResult ValidateExit(string id, ExitSide side,
            GridCoordinate start, int width, ColorIdentity color)
        {
            if (string.IsNullOrWhiteSpace(id) || _colors.ContainsKey(id) ||
                !ValidColor(color) || width <= 0 || !Enum.IsDefined(typeof(ExitSide), side))
                return Reject("Exit ID, side, width or color is invalid.");
            List<ExitDefinition> candidate = new(_exits.Count + 1);
            foreach (ExitDefinition exit in _exits)
                if (exit.Id != id) candidate.Add(exit);
            candidate.Add(NewExit(id, side, start, width, color));
            if (!ExitBoundaryValidator.TryValidate(BuildGrid(Core.CreateBoardSnapshot()),
                    candidate, out string failure)) return Reject(failure, new[] { id });
            return AuthoringEditResult.Accepted;
        }

        private IReadOnlyList<string> AffectedExits(BoardDefinitionData board)
        {
            List<string> affected = new();
            if (board == null) return affected;
            GridBoard grid = BuildGrid(board);
            foreach (ExitDefinition exit in _exits)
                if (!ExitBoundaryValidator.TryValidate(grid, new[] { exit }, out _)) affected.Add(exit.Id);
            return affected;
        }

        private BoardDefinitionData CandidateResize(int width, int height)
        {
            if (width <= 0 || height <= 0) return null;
            BoardDefinitionData board = new() { Width = width, Height = height };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    GridCoordinate coordinate = new(x, y);
                    board.Cells.Add(new CellDefinitionData
                    { Coordinate = Cell(coordinate), CellState =
                        x < Core.Width && y < Core.Height ? Core.GetCellState(coordinate) : AuthoredCellState.Active });
                }
            return board;
        }

        private BoardDefinitionData CandidateCellChange(GridCoordinate coordinate, AuthoredCellState state)
        {
            BoardDefinitionData board = Core.CreateBoardSnapshot();
            foreach (CellDefinitionData cell in board.Cells)
                if (cell.Coordinate.X == coordinate.X && cell.Coordinate.Y == coordinate.Y)
                { cell.CellState = state; break; }
            return board;
        }

        private static GridBoard BuildGrid(BoardDefinitionData board)
        {
            List<GridCoordinate> structural = new();
            List<GridCoordinate> blocked = new();
            foreach (CellDefinitionData cell in board.Cells)
            {
                GridCoordinate coordinate = new(cell.Coordinate.X, cell.Coordinate.Y);
                if (cell.CellState != AuthoredCellState.Inactive) structural.Add(coordinate);
                if (cell.CellState == AuthoredCellState.Blocked) blocked.Add(coordinate);
            }
            return new GridBoard(board.Width, board.Height, structural, blocked);
        }

        private bool ExitIdExists(string id) => _exits.Exists(exit => exit.Id == id);
        private AuthoringEditResult ValidateNewFootprint(IReadOnlyList<GridCoordinate> offsets)
        {
            if (offsets == null || offsets.Count == 0) return Reject("Choose a nonempty footprint.");
            HashSet<GridCoordinate> cells = new(offsets);
            if (cells.Count != offsets.Count) return Reject("Footprint offsets must be unique.");
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (GridCoordinate cell in cells)
            {
                minX = Math.Min(minX, cell.X); minY = Math.Min(minY, cell.Y);
                maxX = Math.Max(maxX, cell.X); maxY = Math.Max(maxY, cell.Y);
            }
            if (MaxNewFootprintDimension <= 0 || maxX - minX + 1 > MaxNewFootprintDimension ||
                maxY - minY + 1 > MaxNewFootprintDimension)
                return Reject($"New footprint exceeds the {MaxNewFootprintDimension}×{MaxNewFootprintDimension} authoring guard.");
            Queue<GridCoordinate> queue = new();
            HashSet<GridCoordinate> visited = new();
            queue.Enqueue(offsets[0]); visited.Add(offsets[0]);
            while (queue.Count > 0)
            {
                GridCoordinate current = queue.Dequeue();
                GridCoordinate[] neighbors =
                { current.Offset(1, 0), current.Offset(-1, 0), current.Offset(0, 1), current.Offset(0, -1) };
                foreach (GridCoordinate neighbor in neighbors)
                    if (cells.Contains(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
            return visited.Count == cells.Count ? AuthoringEditResult.Accepted :
                Reject("Footprint offsets must be edge-connected.");
        }
        private static bool ValidColor(ColorIdentity color) =>
            color >= ColorIdentity.Slot0 && color <= ColorIdentity.Slot9;
        private static CellCoordinateData Cell(GridCoordinate coordinate) =>
            new() { X = coordinate.X, Y = coordinate.Y };
        private static ExitDefinition NewExit(string id, ExitSide side, GridCoordinate start,
            int width, ColorIdentity color) => new()
            { Id = id, Side = side, StartCell = Cell(start), Width = width, Color = color };
        private static AuthoringEditResult Reject(string reason, IReadOnlyList<string> affected = null) =>
            new(false, reason, affected);
    }
}
