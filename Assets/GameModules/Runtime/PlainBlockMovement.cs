using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Interaction;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>One on-board drag result. World position is authoritative for the block view.</summary>
    public readonly struct PlainBlockMoveResult
    {
        public PlainBlockMoveResult(Vector3 worldPosition, bool wasBlocked, string failureReason)
        {
            WorldPosition = worldPosition;
            WasBlocked = wasBlocked;
            FailureReason = failureReason ?? string.Empty;
        }

        public Vector3 WorldPosition { get; }
        public bool WasBlocked { get; }
        public string FailureReason { get; }
    }

    /// <summary>
    /// CBE-owned on-board movement policy. The shared sweep and clearance helpers validate
    /// continuous travel; occupancy changes only when a release reaches a valid snapped pose.
    /// Exits, outcomes and presentation are deliberately outside this checkpoint.
    /// </summary>
    public sealed class PlainBlockMovement
    {
        private readonly ColorBlockEscapeRuntimeLevel _level;
        private readonly GridWorldLayout _layout;
        private readonly SweptFootprintHelper _sweep = new();
        private readonly GridSnapSystem _snap = new();
        private BlockRuntimeState _activeBlock;
        private ShapeAwareDragFootprint _activeFootprint;

        public PlainBlockMovement(ColorBlockEscapeRuntimeLevel level, GridWorldLayout layout)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _layout = layout;
        }

        public string ActiveBlockId => _activeBlock?.Id;

        public bool TryBegin(string blockId, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (_activeBlock != null || string.IsNullOrWhiteSpace(blockId) ||
                !_level.TryGetBlock(blockId, out BlockRuntimeState block) ||
                block.Lifecycle != BlockLifecycle.OnBoard)
                return false;

            _activeBlock = block;
            // Gameplay uses the authored full footprint. A drag inset could allow visual
            // penetration into inactive cells or another block, so it is zero here.
            _activeFootprint = new ShapeAwareDragFootprint(block.Footprint.Offsets, 0f);
            worldPosition = CurrentWorldPosition();
            return true;
        }

        public PlainBlockMoveResult Move(Vector3 candidateWorldPosition)
        {
            EnsureActive();
            Vector3 previous = CurrentWorldPosition();
            Vector2 local = _layout.WorldToBoardLocal(candidateWorldPosition);
            GridBoard board = _level.FrameworkContext.GridBoard;
            Vector2 bounded = new(
                Mathf.Clamp(local.x, -_activeFootprint.MinX, board.Width - _activeFootprint.MaxX),
                Mathf.Clamp(local.y, -_activeFootprint.MinY, board.Height - _activeFootprint.MaxY));
            Vector3 target = _layout.BoardLocalToWorld(bounded);
            bool clamped = (bounded - local).sqrMagnitude > 0.00000001f;

            if (IsPathClear(previous, target, out string failure))
            {
                _activeBlock.ContinuousOrigin = bounded;
                return new PlainBlockMoveResult(target, clamped, clamped ? "Board boundary." : string.Empty);
            }

            // The sweep is monotonic along this segment: a blocked prefix cannot become
            // traversable later. Find the last clear continuous pose instead of freezing the
            // entire frame when a fast pointer sample crosses a blocker.
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (low + high) * 0.5f;
                if (IsPathClear(previous, Vector3.Lerp(previous, target, mid), out _)) low = mid;
                else high = mid;
            }

            Vector3 accepted = Vector3.Lerp(previous, target, low);
            _activeBlock.ContinuousOrigin = _layout.WorldToBoardLocal(accepted);
            return new PlainBlockMoveResult(accepted, true, failure);
        }

        public PlainBlockMoveResult End()
        {
            EnsureActive();
            BlockRuntimeState block = _activeBlock;
            Vector3 current = CurrentWorldPosition();
            GridSnapResult snap = _snap.Evaluate(new GridSnapRequest(current,
                block.CommittedOrigin, block.Footprint.Offsets, _layout,
                _level.FrameworkContext.GridBoard,
                _level.FrameworkContext.CellOccupancySystem));

            string failure = snap.FailureReason;
            bool valid = snap.IsValid && snap.ResolvedOriginCell.HasValue &&
                IsPathClear(current, snap.SnappedWorldPosition, out failure);
            if (valid)
            {
                GridCoordinate origin = snap.ResolvedOriginCell.Value;
                IReadOnlyList<GridCoordinate> destination = block.Footprint.ResolveCoordinates(origin);
                FootprintClearanceQuery clearance = new(_level.FrameworkContext.GridBoard,
                    _level.FrameworkContext.CellOccupancySystem, block.RetainedCells);
                FootprintClearanceResult destinationClearance = clearance.Evaluate(destination);
                if (!destinationClearance.IsClear)
                {
                    valid = false;
                    failure = $"Snap footprint reaches {destinationClearance.Failure} at {destinationClearance.BlockingCoordinate}.";
                }
                else
                {
                    CellOccupancyOperationResult transfer =
                        _level.FrameworkContext.CellOccupancySystem.TransferFootprint(block.RetainedCells, destination);
                    valid = transfer.Success;
                    failure = transfer.FailureReason;
                    if (valid)
                    {
                        block.RetainedCells.Clear();
                        foreach (GridCoordinate cell in destination) block.RetainedCells.Add(cell);
                        block.CommittedOrigin = origin;
                    }
                }
            }

            block.ContinuousOrigin = new Vector2(block.CommittedOrigin.X, block.CommittedOrigin.Y);
            _activeBlock = null;
            _activeFootprint = null;
            return new PlainBlockMoveResult(_layout.GridToWorldPosition(block.CommittedOrigin),
                !valid, valid ? string.Empty : failure);
        }

        private Vector3 CurrentWorldPosition() =>
            _layout.BoardLocalToWorld(_activeBlock.ContinuousOrigin);

        private bool IsPathClear(Vector3 from, Vector3 to, out string failure)
        {
            FootprintClearanceQuery clearance = new(_level.FrameworkContext.GridBoard,
                _level.FrameworkContext.CellOccupancySystem, _activeBlock.RetainedCells);
            IReadOnlyList<SweptFootprintContactGroup> groups = _sweep.EnumerateContactGroups(
                new SweptFootprintRequest(from, to, _layout, _activeFootprint.Rectangles));
            foreach (SweptFootprintContactGroup group in groups)
            {
                FootprintClearanceResult result = clearance.Evaluate(group.OverlappedCells);
                if (result.IsClear) continue;
                failure = $"Movement reaches {result.Failure} at {result.BlockingCoordinate}.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private void EnsureActive()
        {
            if (_activeBlock == null) throw new InvalidOperationException("No block drag is active.");
        }
    }
}
