using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Interaction;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>Game-owned capture tuning in board-cell units, independent of presentation.</summary>
    public sealed class ExitCaptureSettings
    {
        public ExitCaptureSettings(float overlapFraction = 0.70f,
            float captureDistanceCells = 0.35f, float outwardSpeedCellsPerSecond = 3f)
        {
            if (float.IsNaN(overlapFraction) || overlapFraction <= 0f || overlapFraction > 1f ||
                float.IsNaN(captureDistanceCells) || float.IsInfinity(captureDistanceCells) ||
                captureDistanceCells < 0f || float.IsNaN(outwardSpeedCellsPerSecond) ||
                float.IsInfinity(outwardSpeedCellsPerSecond) || outwardSpeedCellsPerSecond <= 0f)
                throw new ArgumentException("Exit capture tuning values are invalid.");
            OverlapFraction = overlapFraction;
            CaptureDistanceCells = captureDistanceCells;
            OutwardSpeedCellsPerSecond = outwardSpeedCellsPerSecond;
        }

        public float OverlapFraction { get; }
        public float CaptureDistanceCells { get; }
        public float OutwardSpeedCellsPerSecond { get; }
    }

    /// <summary>One player-driven admission result. Failure leaves the block player-controlled.</summary>
    public readonly struct ExitCaptureResult
    {
        public ExitCaptureResult(bool captured, string exitId, Vector3 alignedWorldPosition,
            string failureReason)
        {
            Captured = captured;
            ExitId = exitId;
            AlignedWorldPosition = alignedWorldPosition;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Captured { get; }
        public string ExitId { get; }
        public Vector3 AlignedWorldPosition { get; }
        public string FailureReason { get; }
    }

    /// <summary>
    /// CBE-only exit admission and logical outward travel. Shared sweep/clearance/occupancy
    /// primitives own geometry and storage; this class owns aperture, color and lifecycle rules.
    /// </summary>
    public sealed class ColorBlockEscapeExitCapture
    {
        private const float GeometryEpsilon = 0.00001f;
        private readonly ColorBlockEscapeRuntimeLevel _level;
        private readonly GridWorldLayout _layout;
        private readonly ExitCaptureSettings _settings;
        private readonly SweptFootprintHelper _sweep = new();

        public ColorBlockEscapeExitCapture(ColorBlockEscapeRuntimeLevel level,
            GridWorldLayout layout, ExitCaptureSettings settings = null)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _layout = layout;
            _settings = settings ?? new ExitCaptureSettings();
        }

        public string LastAdvanceFailure { get; private set; } = string.Empty;

        /// <summary>Attempts capture only for an outward player movement sample.</summary>
        public ExitCaptureResult TryCapture(BlockRuntimeState block,
            Vector2 previousPose, Vector2 requestedPose)
        {
            if (block == null || block.Lifecycle != BlockLifecycle.OnBoard)
                return Rejected("Block is not available for exit capture.");

            Vector2 current = block.ContinuousOrigin;
            string lastFailure = "No matching exit is aligned with the outward movement.";
            foreach (ExitRuntimeState exit in _level.Exits)
            {
                Vector2 outward = Outward(exit.Side);
                if (Vector2.Dot(requestedPose - previousPose, outward) <= GeometryEpsilon) continue;
                if (exit.Color != block.Color) { lastFailure = "Exit color does not match the block."; continue; }
                if (exit.IsBusy) { lastFailure = $"Exit '{exit.Id}' is busy."; continue; }

                Bounds(block.Footprint.Offsets, exit.Side, out int lateralMin,
                    out int lateralMax, out int normalMin, out int normalMax);
                int requiredSpan = lateralMax - lateralMin + 1;
                if (requiredSpan > exit.Width)
                { lastFailure = $"Exit '{exit.Id}' is narrower than the block's projected span."; continue; }

                float plane = BoundaryPlane(exit);
                float leading = LeadingEdge(current, exit.Side, normalMin, normalMax);
                float distance = (exit.Side == ExitSide.Top || exit.Side == ExitSide.Right)
                    ? plane - leading : leading - plane;
                if (distance < -GeometryEpsilon || distance > _settings.CaptureDistanceCells + GeometryEpsilon)
                { lastFailure = $"Block is outside capture distance of exit '{exit.Id}'."; continue; }

                float lateral = IsHorizontalOpening(exit.Side) ? current.x : current.y;
                float apertureStart = IsHorizontalOpening(exit.Side) ? exit.StartCell.X : exit.StartCell.Y;
                float overlap = Mathf.Max(0f, Mathf.Min(lateral + lateralMax + 1f,
                    apertureStart + exit.Width) - Mathf.Max(lateral + lateralMin, apertureStart));
                if (overlap + GeometryEpsilon < requiredSpan * _settings.OverlapFraction)
                { lastFailure = $"Block has insufficient overlap with exit '{exit.Id}'."; continue; }

                int firstLateralOrigin = (int)apertureStart - lateralMin;
                int lastLateralOrigin = (int)apertureStart + exit.Width - 1 - lateralMax;
                int lateralOrigin = NearestInteger(lateral, firstLateralOrigin, lastLateralOrigin);
                int normalOrigin = (exit.Side == ExitSide.Top || exit.Side == ExitSide.Right)
                    ? (int)plane - 1 - normalMax : (int)plane - normalMin;
                GridCoordinate aligned = IsHorizontalOpening(exit.Side)
                    ? new GridCoordinate(lateralOrigin, normalOrigin)
                    : new GridCoordinate(normalOrigin, lateralOrigin);

                if (!OnBoardAlignmentClear(block, current, aligned, out lastFailure)) continue;
                int travelToOutside = normalMax - normalMin + 1;
                Vector2 alignedPose = new(aligned.X, aligned.Y);
                if (!ExitSweepClear(block, exit, alignedPose,
                        alignedPose + outward * travelToOutside, block.RetainedCells,
                        out lastFailure)) continue;

                // Capture/entry reserves the complete remaining in-board corridor at once.
                // Irregular shapes can cover a previously empty cell during translation;
                // reserving it before outward travel makes later occupancy release-only.
                HashSet<GridCoordinate> destination = RemainingCorridorCells(block, exit,
                    alignedPose, alignedPose + outward * travelToOutside);
                CellOccupancyOperationResult transfer = _level.FrameworkContext.CellOccupancySystem
                    .TransferFootprint(block.RetainedCells, destination);
                if (!transfer.Success)
                { lastFailure = transfer.FailureReason; continue; }

                block.RetainedCells.Clear();
                foreach (GridCoordinate cell in destination) block.RetainedCells.Add(cell);
                block.CommittedOrigin = aligned;
                block.ContinuousOrigin = alignedPose;
                block.OutwardTravelCells = 0f;
                block.AcceptedExitId = exit.Id;
                block.Lifecycle = BlockLifecycle.Exiting;
                exit.IsBusy = true;
                return new ExitCaptureResult(true, exit.Id,
                    _layout.GridToWorldPosition(aligned), string.Empty);
            }
            return Rejected(lastFailure);
        }

        /// <summary>Advances accepted blocks; the validated remaining corridor only shrinks.</summary>
        public void Advance(float deltaTimeSeconds)
        {
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) ||
                deltaTimeSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));
            LastAdvanceFailure = string.Empty;
            float step = deltaTimeSeconds * _settings.OutwardSpeedCellsPerSecond;
            if (step <= 0f) return;
            foreach (BlockRuntimeState block in _level.Blocks)
            {
                if (block.Lifecycle != BlockLifecycle.Exiting ||
                    !_level.TryGetExit(block.AcceptedExitId, out ExitRuntimeState exit)) continue;
                AdvanceBlock(block, exit, step);
            }
        }

        private void AdvanceBlock(BlockRuntimeState block, ExitRuntimeState exit, float step)
        {
            Bounds(block.Footprint.Offsets, exit.Side, out _, out _,
                out int normalMin, out int normalMax);
            float fullTravel = normalMax - normalMin + 1;
            float nextTravel = Mathf.Min(fullTravel, block.OutwardTravelCells + step);
            Vector2 outward = Outward(exit.Side);
            Vector2 previous = new Vector2(block.CommittedOrigin.X, block.CommittedOrigin.Y) +
                               outward * block.OutwardTravelCells;
            Vector2 next = new Vector2(block.CommittedOrigin.X, block.CommittedOrigin.Y) +
                           outward * nextTravel;
            if (!ExitSweepClear(block, exit, previous, next, block.RetainedCells,
                    out string failure))
            { LastAdvanceFailure = failure; return; }

            Vector2 finalPose = new Vector2(block.CommittedOrigin.X, block.CommittedOrigin.Y) +
                                outward * fullTravel;
            HashSet<GridCoordinate> nextOccupied = RemainingCorridorCells(block, exit,
                next, finalPose);
            if (!nextOccupied.IsSubsetOf(block.RetainedCells))
            {
                LastAdvanceFailure = "The validated outward corridor would acquire a new cell.";
                return;
            }
            CellOccupancySystem occupancy = _level.FrameworkContext.CellOccupancySystem;
            if (nextOccupied.Count > 0)
            {
                CellOccupancyOperationResult transfer = occupancy.TransferFootprint(
                    block.RetainedCells, nextOccupied);
                if (!transfer.Success) { LastAdvanceFailure = transfer.FailureReason; return; }
            }
            else
            {
                // The generic atomic transfer intentionally rejects an empty destination.
                // Every retained cell has been validated as ours; release the final slice.
                foreach (GridCoordinate cell in block.RetainedCells)
                {
                    CellOccupancyOperationResult release = occupancy.Release(cell);
                    if (!release.Success) throw new InvalidOperationException(release.FailureReason);
                }
            }

            block.RetainedCells.Clear();
            foreach (GridCoordinate cell in nextOccupied) block.RetainedCells.Add(cell);
            block.ContinuousOrigin = next;
            block.OutwardTravelCells = nextTravel;
            if (nextTravel < fullTravel - GeometryEpsilon) return;
            block.Lifecycle = BlockLifecycle.Removed;
            exit.IsBusy = false;
        }

        private bool OnBoardAlignmentClear(BlockRuntimeState block,
            Vector2 current, GridCoordinate aligned, out string failure)
        {
            FootprintClearanceQuery clearance = new(_level.FrameworkContext.GridBoard,
                _level.FrameworkContext.CellOccupancySystem, block.RetainedCells);
            ShapeAwareDragFootprint footprint = new(block.Footprint.Offsets, 0f);
            Vector3 from = _layout.BoardLocalToWorld(current);
            Vector3 to = _layout.GridToWorldPosition(aligned);
            foreach (SweptFootprintContactGroup group in _sweep.EnumerateContactGroups(
                         new SweptFootprintRequest(from, to, _layout, footprint.Rectangles)))
            {
                FootprintClearanceResult result = clearance.Evaluate(group.OverlappedCells);
                if (result.IsClear) continue;
                failure = $"Alignment reaches {result.Failure} at {result.BlockingCoordinate}.";
                return false;
            }
            FootprintClearanceResult final = clearance.Evaluate(
                block.Footprint.ResolveCoordinates(aligned));
            failure = final.IsClear ? string.Empty :
                $"Aligned pose reaches {final.Failure} at {final.BlockingCoordinate}.";
            return final.IsClear;
        }

        private bool ExitSweepClear(BlockRuntimeState block, ExitRuntimeState exit,
            Vector2 from, Vector2 to, IEnumerable<GridCoordinate> selfCells, out string failure)
        {
            HashSet<GridCoordinate> self = new(selfCells);
            ShapeAwareDragFootprint footprint = new(block.Footprint.Offsets, 0f);
            foreach (SweptFootprintContactGroup group in _sweep.EnumerateContactGroups(
                         new SweptFootprintRequest(_layout.BoardLocalToWorld(from),
                             _layout.BoardLocalToWorld(to), _layout, footprint.Rectangles)))
                if (!ExitContactsClear(group.OverlappedCells, exit, self, out failure)) return false;
            return ExitContactsClear(AllOverlaps(block, to), exit, self, out failure);
        }

        private bool ExitContactsClear(IEnumerable<GridCoordinate> contacts,
            ExitRuntimeState exit, HashSet<GridCoordinate> self, out string failure)
        {
            GridBoard board = _level.FrameworkContext.GridBoard;
            CellOccupancySystem occupancy = _level.FrameworkContext.CellOccupancySystem;
            foreach (GridCoordinate cell in contacts)
            {
                if (IsBeyondExit(cell, exit))
                {
                    if (!board.ContainsCell(cell)) continue;
                    failure = $"Exit corridor meets structural cell {cell}.";
                    return false;
                }
                if (!board.ContainsCell(cell) || board.IsBlocked(cell))
                { failure = $"Exit corridor meets non-Active cell {cell}."; return false; }
                if (!self.Contains(cell) && occupancy.IsInUse(cell))
                { failure = $"Exit corridor meets another occupant at {cell}."; return false; }
            }
            failure = string.Empty;
            return true;
        }

        private HashSet<GridCoordinate> BoardOverlaps(BlockRuntimeState block,
            ExitRuntimeState exit, Vector2 pose)
        {
            HashSet<GridCoordinate> result = new();
            foreach (GridCoordinate cell in AllOverlaps(block, pose))
                if (!IsBeyondExit(cell, exit)) result.Add(cell);
            return result;
        }

        private HashSet<GridCoordinate> RemainingCorridorCells(BlockRuntimeState block,
            ExitRuntimeState exit, Vector2 from, Vector2 fullyOutside)
        {
            HashSet<GridCoordinate> result = BoardOverlaps(block, exit, from);
            ShapeAwareDragFootprint footprint = new(block.Footprint.Offsets, 0f);
            foreach (SweptFootprintContactGroup group in _sweep.EnumerateContactGroups(
                         new SweptFootprintRequest(_layout.BoardLocalToWorld(from),
                             _layout.BoardLocalToWorld(fullyOutside), _layout, footprint.Rectangles)))
                foreach (GridCoordinate cell in group.OverlappedCells)
                    if (!IsBeyondExit(cell, exit)) result.Add(cell);
            return result;
        }

        private static HashSet<GridCoordinate> AllOverlaps(BlockRuntimeState block, Vector2 pose)
        {
            HashSet<GridCoordinate> result = new();
            foreach (GridCoordinate offset in block.Footprint.Offsets)
            {
                float minX = pose.x + offset.X, maxX = minX + 1f;
                float minY = pose.y + offset.Y, maxY = minY + 1f;
                for (int x = Mathf.FloorToInt(minX); x < Mathf.CeilToInt(maxX); x++)
                    for (int y = Mathf.FloorToInt(minY); y < Mathf.CeilToInt(maxY); y++)
                        if (Mathf.Min(maxX, x + 1f) - Mathf.Max(minX, x) > GeometryEpsilon &&
                            Mathf.Min(maxY, y + 1f) - Mathf.Max(minY, y) > GeometryEpsilon)
                            result.Add(new GridCoordinate(x, y));
            }
            return result;
        }

        private static void Bounds(IReadOnlyList<GridCoordinate> offsets, ExitSide side,
            out int lateralMin, out int lateralMax, out int normalMin, out int normalMax)
        {
            lateralMin = normalMin = int.MaxValue;
            lateralMax = normalMax = int.MinValue;
            foreach (GridCoordinate offset in offsets)
            {
                int lateral = IsHorizontalOpening(side) ? offset.X : offset.Y;
                int normal = IsHorizontalOpening(side) ? offset.Y : offset.X;
                lateralMin = Math.Min(lateralMin, lateral);
                lateralMax = Math.Max(lateralMax, lateral);
                normalMin = Math.Min(normalMin, normal);
                normalMax = Math.Max(normalMax, normal);
            }
        }

        private static float LeadingEdge(Vector2 pose, ExitSide side, int normalMin, int normalMax) =>
            side switch
            {
                ExitSide.Top => pose.y + normalMax + 1f,
                ExitSide.Bottom => pose.y + normalMin,
                ExitSide.Right => pose.x + normalMax + 1f,
                _ => pose.x + normalMin
            };

        private static float BoundaryPlane(ExitRuntimeState exit) => exit.Side switch
        {
            ExitSide.Top => exit.StartCell.Y + 1f,
            ExitSide.Bottom => exit.StartCell.Y,
            ExitSide.Right => exit.StartCell.X + 1f,
            _ => exit.StartCell.X
        };

        private static bool IsBeyondExit(GridCoordinate cell, ExitRuntimeState exit) => exit.Side switch
        {
            ExitSide.Top => cell.Y > exit.StartCell.Y,
            ExitSide.Bottom => cell.Y < exit.StartCell.Y,
            ExitSide.Right => cell.X > exit.StartCell.X,
            _ => cell.X < exit.StartCell.X
        };

        private static bool IsHorizontalOpening(ExitSide side) =>
            side == ExitSide.Top || side == ExitSide.Bottom;

        private static Vector2 Outward(ExitSide side) => side switch
        {
            ExitSide.Top => Vector2.up,
            ExitSide.Bottom => Vector2.down,
            ExitSide.Right => Vector2.right,
            _ => Vector2.left
        };

        private static int NearestInteger(float value, int first, int last)
        {
            int best = first;
            float bestDistance = Mathf.Abs(value - first);
            for (int candidate = first + 1; candidate <= last; candidate++)
            {
                float distance = Mathf.Abs(value - candidate);
                if (distance < bestDistance - GeometryEpsilon)
                { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        private static ExitCaptureResult Rejected(string reason) =>
            new(false, null, default, reason);
    }
}
