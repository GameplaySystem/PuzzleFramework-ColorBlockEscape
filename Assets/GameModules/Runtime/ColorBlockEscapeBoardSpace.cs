using PuzzleFramework.CoreBoard;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Defines Color Block Escape's world-space board convention. Logical grid X maps to world X,
    /// logical grid Y maps to world Z, and world Y remains available for visual height.
    /// </summary>
    public static class ColorBlockEscapeBoardSpace
    {
        public static GridWorldLayout CreateLayout(Vector3 boardOrigin) => new(
            boardOrigin,
            Vector2.one,
            Vector3.right,
            Vector3.forward,
            GridCellAnchor.Corner);

        /// <summary>Returns the upward-facing normal for the configured board axes.</summary>
        public static Vector3 Up(GridWorldLayout layout) =>
            Vector3.Cross(layout.BoardYAxis, layout.BoardXAxis).normalized;

        /// <summary>Maps a conventional cube's XZ ground plane onto the board plane.</summary>
        public static Quaternion BoardRotation(GridWorldLayout layout) =>
            Quaternion.LookRotation(layout.BoardYAxis, Up(layout));

        /// <summary>
        /// Maps the footprint generator's local XY plane onto the board plane and its centered
        /// local extrusion axis downward, allowing the mesh root to sit half a depth above it.
        /// </summary>
        public static Quaternion FootprintRotation(GridWorldLayout layout) =>
            Quaternion.LookRotation(-Up(layout), layout.BoardYAxis);
    }
}
