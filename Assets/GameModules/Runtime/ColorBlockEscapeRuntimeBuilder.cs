using System;
using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.RuntimeConstruction;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Builds a CBE session from the shared level envelope. Game payload interpretation,
    /// placement and opening validation stay here rather than entering framework services.
    /// </summary>
    public sealed class ColorBlockEscapeRuntimeBuilder
    {
        private readonly LevelRuntimeBuilder _frameworkBuilder =
            new(new LevelRuntimeConstructionValidator());

        public bool TryBuild(LevelDefinition definition,
            out ColorBlockEscapeRuntimeLevel level, out string failure)
        {
            level = null;
            RuntimeLevelBuildResult shared = _frameworkBuilder.Build(definition);
            if (!shared.Success)
            {
                failure = shared.FailureReason;
                return false;
            }
            if (!TryValidateExplicitBoard(definition.FrameworkData.Board, out failure)) return false;
            if (!ColorBlockEscapeLevelCodec.TryDecode(definition,
                    out ColorBlockEscapeLevelData payload, out failure)) return false;

            TimerDefinitionData timer = definition.FrameworkData.Timer;
            if (timer == null || !timer.IsEnabled || timer.Mode != TimerMode.Countdown ||
                timer.DurationSeconds <= 0f || float.IsNaN(timer.DurationSeconds) ||
                float.IsInfinity(timer.DurationSeconds) ||
                float.IsNaN(timer.WarningThresholdSeconds) ||
                float.IsInfinity(timer.WarningThresholdSeconds) ||
                timer.WarningThresholdSeconds < 0f ||
                timer.WarningThresholdSeconds > timer.DurationSeconds)
            {
                failure = "Color Block Escape requires a valid enabled countdown timer.";
                return false;
            }

            if (!ExitBoundaryValidator.TryValidate(shared.Context.GridBoard, payload.Exits, out failure))
                return false;

            List<BlockRuntimeState> blocks = new(payload.Blocks.Count);
            HashSet<GridCoordinate> claimed = new();
            foreach (BlockDefinition source in payload.Blocks)
            {
                List<GridCoordinate> offsets = new(source.FootprintOffsets.Count);
                foreach (CellCoordinateData offset in source.FootprintOffsets)
                    offsets.Add(new GridCoordinate(offset.X, offset.Y));
                ShapeFootprint footprint = new(offsets);
                GridCoordinate origin = new(source.Origin.X, source.Origin.Y);
                foreach (GridCoordinate cell in footprint.ResolveCoordinates(origin))
                {
                    if (!shared.Context.GridBoard.IsWithinBounds(cell) ||
                        !shared.Context.GridBoard.ContainsCell(cell) ||
                        shared.Context.GridBoard.IsBlocked(cell))
                    {
                        failure = $"Block '{source.Id}' occupies non-Active cell {cell}.";
                        return false;
                    }
                    if (!claimed.Add(cell))
                    {
                        failure = $"Block '{source.Id}' overlaps another block at {cell}.";
                        return false;
                    }
                }
                blocks.Add(new BlockRuntimeState(source.Id, source.Color, origin, footprint));
            }

            foreach (GridCoordinate cell in claimed)
            {
                CellOccupancyOperationResult occupied =
                    shared.Context.CellOccupancySystem.Occupy(cell);
                if (!occupied.Success)
                {
                    failure = $"Could not initialize block occupancy at {cell}: {occupied.FailureReason}";
                    return false;
                }
            }

            List<ExitRuntimeState> exits = new(payload.Exits.Count);
            foreach (ExitDefinition source in payload.Exits)
                exits.Add(new ExitRuntimeState(source));
            level = new ColorBlockEscapeRuntimeLevel(shared.Context, blocks, exits,
                timer.DurationSeconds, timer.WarningThresholdSeconds);
            failure = string.Empty;
            return true;
        }

        private static bool TryValidateExplicitBoard(BoardDefinitionData board, out string failure)
        {
            if (board.Cells.Count != (long)board.Width * board.Height)
            {
                failure = "Color Block Escape requires one explicit state for every board cell.";
                return false;
            }
            HashSet<GridCoordinate> seen = new();
            foreach (CellDefinitionData cell in board.Cells)
            {
                if (cell?.Coordinate == null ||
                    !Enum.IsDefined(typeof(AuthoredCellState), cell.CellState))
                {
                    failure = "Board has a missing cell coordinate or invalid state.";
                    return false;
                }
                GridCoordinate coordinate = new(cell.Coordinate.X, cell.Coordinate.Y);
                if (coordinate.X < 0 || coordinate.X >= board.Width ||
                    coordinate.Y < 0 || coordinate.Y >= board.Height || !seen.Add(coordinate))
                {
                    failure = $"Board cell {coordinate} is out of bounds or duplicated.";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }
    }
}
