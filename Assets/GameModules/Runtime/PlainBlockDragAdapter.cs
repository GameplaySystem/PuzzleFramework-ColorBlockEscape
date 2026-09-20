using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Interaction;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Scene-side pointer sampler and block view bridge. Framework InputSystem owns the
    /// single-pointer drag lifecycle; CBE movement owns collision and release policy.
    /// </summary>
    public sealed class PlainBlockDragAdapter : MonoBehaviour
    {
        private const float BlockViewDepth = -0.2f;
        private ColorBlockEscapeRuntimeLevel _level;
        private GridWorldLayout _layout;
        private Camera _camera;
        private PlainBlockMovement _movement;
        private ColorBlockEscapeExitCapture _exitCapture;
        private ColorBlockEscapeOutcomeSession _outcome;
        private Dictionary<string, Transform> _views;
        private PuzzleFramework.Interaction.InputSystem _input;
        private int _activePointerId = -1;

        public void Initialize(ColorBlockEscapeRuntimeLevel level, GridWorldLayout layout,
            Camera sceneCamera, IReadOnlyDictionary<string, Transform> blockViews,
            ExitCaptureSettings exitSettings = null,
            ColorBlockEscapeOutcomeSession outcome = null)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _camera = sceneCamera ?? throw new ArgumentNullException(nameof(sceneCamera));
            _layout = layout;
            _exitCapture = exitSettings == null ? null :
                new ColorBlockEscapeExitCapture(level, layout, exitSettings);
            _outcome = outcome;
            _movement = new PlainBlockMovement(level, layout, _exitCapture);
            _views = new Dictionary<string, Transform>(blockViews);
            _input = new PuzzleFramework.Interaction.InputSystem();
        }

        private void Update()
        {
            if (_movement == null) return;
            _outcome?.AdvanceTime(Time.deltaTime);
            if (_outcome != null && _outcome.State != GameState.Playing)
                FinishActiveDragForTerminalState();
            if (_exitCapture != null)
            {
                _exitCapture.Advance(Time.deltaTime);
                foreach (BlockRuntimeState block in _level.Blocks)
                {
                    if (block.Lifecycle == BlockLifecycle.Exiting)
                        PositionView(block.Id, _layout.BoardLocalToWorld(block.ContinuousOrigin));
                    else if (block.Lifecycle == BlockLifecycle.Removed)
                        _views[block.Id].gameObject.SetActive(false);
                }
            }
            if ((_outcome == null || _outcome.State == GameState.Playing) &&
                TryReadPointer(out int id, out Vector2 screen,
                    out bool pressed, out bool held, out bool released))
                ProcessPointerSample(id, screen, pressed, held, released);
            _outcome?.ResolveBoundary();
        }

        /// <summary>Applies one screen-space pointer sample through the shared drag lifecycle.</summary>
        public void ProcessPointerSample(int id, Vector2 screen,
            bool pressed, bool held, bool released)
        {
            if (_movement == null) throw new InvalidOperationException("Adapter is not initialized.");
            if (_outcome != null && _outcome.State != GameState.Playing) return;
            if (_activePointerId >= 0 && _activePointerId != id) return;
            if (!BoardPointerProjection.TryProject(_camera.ScreenPointToRay(screen),
                    _layout.BoardOrigin, Vector3.Cross(_layout.BoardXAxis, _layout.BoardYAxis),
                    out Vector3 pointerWorld)) return;

            InteractionPointerContext context = new(id, screen, pointerWorld);
            if (pressed && _activePointerId < 0)
            {
                BlockRuntimeState block = HitTest(pointerWorld);
                if (block == null) return;
                _activePointerId = id;
                _input.BeginPointerPress(context,
                    new InteractionTarget(draggable: new BlockDrag(this, block.Id)));
            }
            else if (_activePointerId == id && held)
            {
                _input.UpdatePointer(context);
            }

            if (_activePointerId == id && released)
            {
                _input.UpdatePointer(context);
                _input.EndPointerPress(context);
                _activePointerId = -1;
            }
        }

        private BlockRuntimeState HitTest(Vector3 pointerWorld)
        {
            Vector2 point = _layout.WorldToBoardLocal(pointerWorld);
            foreach (BlockRuntimeState block in _level.Blocks)
            {
                if (block.Lifecycle != BlockLifecycle.OnBoard) continue;
                foreach (GridCoordinate offset in block.Footprint.Offsets)
                {
                    float x = block.ContinuousOrigin.x + offset.X;
                    float y = block.ContinuousOrigin.y + offset.Y;
                    if (point.x >= x && point.x < x + 1f &&
                        point.y >= y && point.y < y + 1f) return block;
                }
            }
            return null;
        }

        private void FinishActiveDragForTerminalState()
        {
            if (_activePointerId < 0) return;
            string blockId = _movement.ActiveBlockId;
            if (blockId != null) PositionView(blockId, _movement.End().WorldPosition);
            _activePointerId = -1;
            _input = new PuzzleFramework.Interaction.InputSystem();
        }

        private void PositionView(string id, Vector3 logicalWorldPosition)
        {
            _views[id].position = logicalWorldPosition + Vector3.forward * BlockViewDepth;
        }

        private static bool TryReadPointer(out int id, out Vector2 screen,
            out bool pressed, out bool held, out bool released)
        {
            if (Touchscreen.current != null &&
                (Touchscreen.current.primaryTouch.press.isPressed ||
                 Touchscreen.current.primaryTouch.press.wasReleasedThisFrame))
            {
                var touch = Touchscreen.current.primaryTouch;
                id = touch.touchId.ReadValue();
                screen = touch.position.ReadValue();
                pressed = touch.press.wasPressedThisFrame;
                held = touch.press.isPressed;
                released = touch.press.wasReleasedThisFrame;
                return true;
            }

            if (Mouse.current != null)
            {
                id = 0;
                screen = Mouse.current.position.ReadValue();
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                released = Mouse.current.leftButton.wasReleasedThisFrame;
                return pressed || held || released;
            }

            id = -1;
            screen = default;
            pressed = held = released = false;
            return false;
        }

        private sealed class BlockDrag : IDraggable
        {
            private readonly PlainBlockDragAdapter _owner;
            private readonly string _id;
            private Vector3 _pointerOffset;

            public BlockDrag(PlainBlockDragAdapter owner, string id)
            {
                _owner = owner;
                _id = id;
            }

            public void OnDragStart(InteractionPointerContext context)
            {
                if (_owner._movement.TryBegin(_id, out Vector3 worldPosition))
                    _pointerOffset = BoardPointerProjection.CaptureOffset(
                        worldPosition, context.WorldPosition);
            }

            public void OnDrag(InteractionPointerContext context)
            {
                if (_owner._movement.ActiveBlockId != _id) return;
                Vector3 requested = BoardPointerProjection.ApplyOffset(
                    context.WorldPosition, _pointerOffset);
                PlainBlockMoveResult move = _owner._movement.Move(requested);
                _owner.PositionView(_id, move.WorldPosition);
                if (!move.WasCaptured) return;
                _owner._input.EndPointerPress(context);
                _owner._activePointerId = -1;
            }

            public void OnDragEnd(InteractionPointerContext context)
            {
                if (_owner._movement.ActiveBlockId != _id) return;
                _owner.PositionView(_id, _owner._movement.End().WorldPosition);
            }
        }
    }
}
