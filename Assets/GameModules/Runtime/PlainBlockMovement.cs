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
        public PlainBlockMoveResult(Vector3 worldPosition, bool wasBlocked, string failureReason,
            bool wasCaptured = false)
        {
            WorldPosition = worldPosition;
            WasBlocked = wasBlocked;
            FailureReason = failureReason ?? string.Empty;
            WasCaptured = wasCaptured;
        }

        public Vector3 WorldPosition { get; }
        public bool WasBlocked { get; }
        public string FailureReason { get; }
        public bool WasCaptured { get; }
    }

    /// <summary>
    /// CBE-owned on-board movement policy. The shared sweep and clearance helpers validate
    /// continuous travel; occupancy changes only when a release reaches a valid snapped pose.
    /// An optional game-owned exit capture service may admit a block after the normal swept
    /// on-board movement. The plain movement fixture leaves that service absent.
    /// </summary>
    public sealed class PlainBlockMovement
    {
        /// <summary>
        /// Initial exposed-edge tolerance for the active drag query. Authored and committed
        /// footprints remain exact; callers may pass zero to disable the tolerance.
        /// </summary>
        public const float DefaultDragClearanceInsetCells = 0.08f;

        private readonly ColorBlockEscapeRuntimeLevel _level;
        private readonly GridWorldLayout _layout;
        private readonly float _dragClearanceInsetCells;
        private readonly SweptFootprintHelper _sweep = new();
        private readonly GridSnapSystem _snap = new();
        private readonly ColorBlockEscapeExitCapture _exitCapture;
        private BlockRuntimeState _activeBlock;
        private ShapeAwareDragFootprint _activeQueryFootprint;
        private ShapeAwareDragFootprint _activeExactFootprint;

        public PlainBlockMovement(ColorBlockEscapeRuntimeLevel level, GridWorldLayout layout,
            ColorBlockEscapeExitCapture exitCapture = null,
            float dragClearanceInsetCells = DefaultDragClearanceInsetCells)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            if (dragClearanceInsetCells < 0f || dragClearanceInsetCells > 0.45f)
                throw new ArgumentOutOfRangeException(nameof(dragClearanceInsetCells),
                    "Drag clearance inset must stay between 0 and 0.45 cells.");
            _layout = layout;
            _exitCapture = exitCapture;
            _dragClearanceInsetCells = dragClearanceInsetCells;
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
            // Only the active drag query receives feel tolerance. Committed occupancy, release
            // snap, exit fit, and authored data continue to use the exact footprint.
            _activeQueryFootprint = new ShapeAwareDragFootprint(
                block.Footprint.Offsets, _dragClearanceInsetCells);
            _activeExactFootprint = new ShapeAwareDragFootprint(block.Footprint.Offsets, 0f);
            worldPosition = CurrentWorldPosition();
            return true;
        }

        public PlainBlockMoveResult Move(Vector3 candidateWorldPosition)
        {
            EnsureActive();
            Vector3 previous = CurrentWorldPosition();
            Vector2 previousLocal = _activeBlock.ContinuousOrigin;
            Vector2 local = _layout.WorldToBoardLocal(candidateWorldPosition);
            GridBoard board = _level.FrameworkContext.GridBoard;
            Vector2 bounded = new(
                Mathf.Clamp(local.x, -_activeExactFootprint.MinX,
                    board.Width - _activeExactFootprint.MaxX),
                Mathf.Clamp(local.y, -_activeExactFootprint.MinY,
                    board.Height - _activeExactFootprint.MaxY));
            Vector3 target = _layout.BoardLocalToWorld(bounded);
            bool clamped = (bounded - local).sqrMagnitude > 0.00000001f;

            if (IsPathClear(previous, target, _activeQueryFootprint, out string failure))
            {
                _activeBlock.ContinuousOrigin = bounded;
                return FinishMove(new PlainBlockMoveResult(target, clamped,
                    clamped ? "Board boundary." : string.Empty), previousLocal, local);
            }

            Vector3 accepted = ResolveBlockedMovement(previous, target);
            _activeBlock.ContinuousOrigin = _layout.WorldToBoardLocal(accepted);
            return FinishMove(new PlainBlockMoveResult(accepted, true, failure), previousLocal, local);
        }

        private PlainBlockMoveResult FinishMove(PlainBlockMoveResult onBoard,
            Vector2 previousPose, Vector2 requestedPose)
        {
            if (_exitCapture == null) return onBoard;
            ExitCaptureResult capture = _exitCapture.TryCapture(_activeBlock,
                previousPose, requestedPose);
            if (!capture.Captured) return onBoard;
            _activeBlock = null;
            _activeQueryFootprint = null;
            _activeExactFootprint = null;
            return new PlainBlockMoveResult(capture.AlignedWorldPosition, false,
                string.Empty, wasCaptured: true);
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
                IsPathClear(current, snap.SnappedWorldPosition, _activeExactFootprint, out failure);
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
            _activeQueryFootprint = null;
            _activeExactFootprint = null;
            return new PlainBlockMoveResult(_layout.GridToWorldPosition(block.CommittedOrigin),
                !valid, valid ? string.Empty : failure);
        }

        private Vector3 CurrentWorldPosition() =>
            _layout.BoardLocalToWorld(_activeBlock.ContinuousOrigin);

        private Vector3 ResolveBlockedMovement(Vector3 previous, Vector3 target)
        {
            Vector2 previousLocal = _layout.WorldToBoardLocal(previous);
            Vector2 targetLocal = _layout.WorldToBoardLocal(target);
            Vector3 best = FindLastClearPositionOnBlockedPath(previous, target);
            float bestProgressSquared = (best - previous).sqrMagnitude;

            // Preserve the free axis when a diagonal pointer sample presses the block into an
            // edge. Every fallback is still swept from the last accepted pose, so sliding cannot
            // tunnel through a corner or another footprint.
            TrySelectBetterCandidate(previous,
                _layout.BoardLocalToWorld(new Vector2(targetLocal.x, previousLocal.y)),
                ref best, ref bestProgressSquared);
            TrySelectBetterCandidate(previous,
                _layout.BoardLocalToWorld(new Vector2(previousLocal.x, targetLocal.y)),
                ref best, ref bestProgressSquared);
            return best;
        }

        private void TrySelectBetterCandidate(Vector3 previous, Vector3 candidate,
            ref Vector3 best, ref float bestProgressSquared)
        {
            if (!IsPathClear(previous, candidate, _activeQueryFootprint, out _)) return;
            float progressSquared = (candidate - previous).sqrMagnitude;
            if (progressSquared <= bestProgressSquared) return;
            best = candidate;
            bestProgressSquared = progressSquared;
        }

        private Vector3 FindLastClearPositionOnBlockedPath(Vector3 previous, Vector3 target)
        {
            // A blocked prefix cannot become traversable later on the same straight segment.
            // Binary search keeps the visual at the last collision-safe continuous pose.
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (low + high) * 0.5f;
                if (IsPathClear(previous, Vector3.Lerp(previous, target, mid),
                        _activeQueryFootprint, out _)) low = mid;
                else high = mid;
            }

            return Vector3.Lerp(previous, target, low);
        }

        private bool IsPathClear(Vector3 from, Vector3 to,
            ShapeAwareDragFootprint footprint, out string failure)
        {
            FootprintClearanceQuery clearance = new(_level.FrameworkContext.GridBoard,
                _level.FrameworkContext.CellOccupancySystem, _activeBlock.RetainedCells);
            IReadOnlyList<SweptFootprintContactGroup> groups = _sweep.EnumerateContactGroups(
                new SweptFootprintRequest(from, to, _layout, footprint.Rectangles));
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
