using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Validates authored openings against genuine exterior-facing playable edges.
    /// Inactive regions count as exterior only when connected to outside the board bounds.
    /// Blocked cells never create an exterior opening by themselves.
    /// </summary>
    public static class ExitBoundaryValidator
    {
        public static bool TryValidate(GridBoard board, IReadOnlyList<ExitDefinition> exits,
            out string failure)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (exits == null) throw new ArgumentNullException(nameof(exits));

            HashSet<GridCoordinate> exteriorInactive = FindExteriorInactive(board);
            HashSet<(GridCoordinate, ExitSide)> occupiedEdges = new();
            foreach (ExitDefinition exit in exits)
            {
                if (exit == null || exit.StartCell == null || exit.Width <= 0 ||
                    !Enum.IsDefined(typeof(ExitSide), exit.Side))
                {
                    failure = "An exit has invalid structural fields.";
                    return false;
                }
                GridCoordinate start = new(exit.StartCell.X, exit.StartCell.Y);
                for (int index = 0; index < exit.Width; index++)
                {
                    GridCoordinate edgeCell = exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom
                        ? start.Offset(index, 0)
                        : start.Offset(0, index);
                    if (!board.IsWithinBounds(edgeCell) || !board.ContainsCell(edgeCell) ||
                        board.IsBlocked(edgeCell))
                    {
                        failure = $"Exit '{exit.Id}' edge cell {edgeCell} is not Active.";
                        return false;
                    }

                    GridCoordinate beyond = edgeCell.Offset(OutwardX(exit.Side), OutwardY(exit.Side));
                    bool exterior = !board.IsWithinBounds(beyond) || exteriorInactive.Contains(beyond);
                    if (!exterior)
                    {
                        failure = $"Exit '{exit.Id}' edge {edgeCell}/{exit.Side} does not face the exterior.";
                        return false;
                    }
                    if (!occupiedEdges.Add((edgeCell, exit.Side)))
                    {
                        failure = $"Exit '{exit.Id}' overlaps another opening at {edgeCell}/{exit.Side}.";
                        return false;
                    }
                }
            }
            failure = string.Empty;
            return true;
        }

        public static GridCoordinate EdgeCellAt(ExitDefinition exit, int index)
        {
            GridCoordinate start = new(exit.StartCell.X, exit.StartCell.Y);
            return exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom
                ? start.Offset(index, 0)
                : start.Offset(0, index);
        }

        public static int OutwardX(ExitSide side) => side switch
        {
            ExitSide.Left => -1,
            ExitSide.Right => 1,
            _ => 0
        };

        public static int OutwardY(ExitSide side) => side switch
        {
            ExitSide.Bottom => -1,
            ExitSide.Top => 1,
            _ => 0
        };

        private static HashSet<GridCoordinate> FindExteriorInactive(GridBoard board)
        {
            HashSet<GridCoordinate> exterior = new();
            Queue<GridCoordinate> queue = new();
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    if (x != 0 && x != board.Width - 1 && y != 0 && y != board.Height - 1)
                        continue;
                    GridCoordinate cell = new(x, y);
                    if (!board.ContainsCell(cell) && exterior.Add(cell)) queue.Enqueue(cell);
                }

            while (queue.Count > 0)
            {
                GridCoordinate cell = queue.Dequeue();
                GridCoordinate[] neighbors =
                {
                    cell.Offset(1, 0), cell.Offset(-1, 0),
                    cell.Offset(0, 1), cell.Offset(0, -1)
                };
                foreach (GridCoordinate neighbor in neighbors)
                    if (board.IsWithinBounds(neighbor) && !board.ContainsCell(neighbor) &&
                        exterior.Add(neighbor)) queue.Enqueue(neighbor);
            }
            return exterior;
        }
    }
}
