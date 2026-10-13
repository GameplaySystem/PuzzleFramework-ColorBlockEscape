using System;
using PuzzleFramework.RuntimeFlow;

namespace ColorBlockEscape.Runtime.Presentation
{
    /// <summary>
    /// Delays only terminal UI visibility. It cannot mutate the committed gameplay outcome.
    /// </summary>
    public sealed class ColorBlockEscapePresentationGate
    {
        private readonly float _postEffectDelay;
        private readonly float _maximumWaitTimeout;
        private float _terminalElapsed;
        private float _inactiveElapsed;

        public ColorBlockEscapePresentationGate(float postEffectDelay, float maximumWaitTimeout)
        {
            if (float.IsNaN(postEffectDelay) || float.IsInfinity(postEffectDelay) ||
                postEffectDelay < 0f) throw new ArgumentOutOfRangeException(nameof(postEffectDelay));
            if (float.IsNaN(maximumWaitTimeout) || float.IsInfinity(maximumWaitTimeout) ||
                maximumWaitTimeout <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumWaitTimeout));
            _postEffectDelay = postEffectDelay;
            _maximumWaitTimeout = maximumWaitTimeout;
        }

        public bool IsResultVisible { get; private set; }

        public void Advance(GameState committedState, bool presentationActive, float deltaTimeSeconds)
        {
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) ||
                deltaTimeSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));
            if (committedState == GameState.Playing)
            {
                Reset();
                return;
            }
            if (IsResultVisible) return;
            _terminalElapsed += deltaTimeSeconds;
            if (_terminalElapsed >= _maximumWaitTimeout)
            {
                IsResultVisible = true;
                return;
            }
            if (presentationActive)
            {
                _inactiveElapsed = 0f;
                return;
            }
            _inactiveElapsed += deltaTimeSeconds;
            if (_inactiveElapsed >= _postEffectDelay) IsResultVisible = true;
        }

        public void Reset()
        {
            _terminalElapsed = 0f;
            _inactiveElapsed = 0f;
            IsResultVisible = false;
        }
    }
}
