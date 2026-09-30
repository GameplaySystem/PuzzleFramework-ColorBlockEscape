using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>CBE-owned camera look that delegates aspect-aware board fitting to the framework.</summary>
    public static class ColorBlockEscapeBoardCamera
    {
        public const float DefaultPitchDegrees = 60f;
        public const float DefaultFieldOfView = 60f;
        public const float DefaultFramingPadding = 1.12f;
        public const float DefaultMinimumDistance = 2f;

        public static bool TryFrame(
            Camera targetCamera,
            GridWorldLayout layout,
            int boardWidth,
            int boardHeight,
            float pitchDegrees,
            float fieldOfView,
            float framingPadding,
            float minimumDistance,
            out string failureReason)
        {
            if (targetCamera == null)
            {
                failureReason = "A CBE scene camera is required.";
                return false;
            }

            targetCamera.orthographic = false;
            targetCamera.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
            targetCamera.transform.rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            return PerspectiveBoardCameraPositioner.TryPosition(
                targetCamera,
                layout,
                boardWidth,
                boardHeight,
                framingPadding,
                minimumDistance,
                out failureReason);
        }
    }
}
