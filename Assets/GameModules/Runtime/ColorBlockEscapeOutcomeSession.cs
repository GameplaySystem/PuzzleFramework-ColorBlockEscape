using System;
using PuzzleFramework.RuntimeFlow;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// CBE's outcome rule owner. Framework services track countdown time and lifecycle state;
    /// this session decides when accepted exits satisfy the level or expiry causes a loss.
    /// One instance belongs to one freshly constructed runtime level.
    /// </summary>
    public sealed class ColorBlockEscapeOutcomeSession
    {
        private readonly ColorBlockEscapeRuntimeLevel _level;
        private readonly TimerSystem _timer;
        private readonly GameStateSystem _gameState = new();
        private bool _expiryPendingAtBoundary;

        public ColorBlockEscapeOutcomeSession(ColorBlockEscapeRuntimeLevel level)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _timer = new TimerSystem(level.TimerDurationSeconds,
                level.TimerWarningSeconds > 0f ? level.TimerWarningSeconds : null);
            Require(_gameState.TryTransitionTo(GameState.Playing).IsSuccess);
            Require(_timer.Start().IsSuccess);
        }

        public GameState State => _gameState.CurrentState;
        public TimerStatus TimerStatus => _timer.Status;
        public float RemainingSeconds => _timer.RemainingSeconds;

        /// <summary>
        /// Advances authored countdown time before this frame's input. Exact expiry is deferred
        /// until ResolveBoundary so a final exit accepted at that boundary wins. An overshoot
        /// means the input sample came after expiry and locks a loss before input is processed.
        /// </summary>
        public void AdvanceTime(float deltaTimeSeconds)
        {
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) ||
                deltaTimeSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));
            if (State != GameState.Playing) return;

            // An acceptance already observed in this session predates this timer step.
            if (AllBlocksAccepted()) { Complete(); return; }
            if (_expiryPendingAtBoundary) { ResolveBoundary(); return; }

            float remainingBefore = _timer.RemainingSeconds;
            TimerAdvanceResult advance = _timer.Advance(deltaTimeSeconds);
            if (!advance.IsSuccess) throw new InvalidOperationException(advance.FailureReason);
            if (!advance.ExpiredRaised) return;
            if (deltaTimeSeconds > remainingBefore)
            {
                Lose();
                return;
            }
            _expiryPendingAtBoundary = true;
        }

        /// <summary>
        /// Resolves accepted exits before a pending exact-boundary expiry. Call after the
        /// frame's player input, including frames with no pointer sample.
        /// </summary>
        public void ResolveBoundary()
        {
            if (State != GameState.Playing) return;
            if (AllBlocksAccepted()) { Complete(); return; }
            if (_expiryPendingAtBoundary) Lose();
        }

        private bool AllBlocksAccepted()
        {
            if (_level.Blocks.Count == 0) return false;
            foreach (BlockRuntimeState block in _level.Blocks)
                if (block.AcceptedExitId == null || block.Lifecycle == BlockLifecycle.OnBoard)
                    return false;
            return true;
        }

        private void Complete()
        {
            Require(_gameState.TryTransitionTo(GameState.Won).IsSuccess);
            Require(_timer.Stop().IsSuccess);
            _expiryPendingAtBoundary = false;
        }

        private void Lose()
        {
            Require(_gameState.TryTransitionTo(GameState.Lost).IsSuccess);
            _expiryPendingAtBoundary = false;
        }

        private static void Require(bool success)
        {
            if (!success) throw new InvalidOperationException("CBE runtime flow transition failed.");
        }
    }
}
