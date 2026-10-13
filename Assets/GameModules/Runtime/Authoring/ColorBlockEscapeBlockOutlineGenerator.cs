using System;
using System.Collections.Generic;
using PuzzleFramework.CoreBoard;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>Result of building CBE's presentation-only block edge geometry.</summary>
    public readonly struct ColorBlockEscapeBlockOutlineGenerationResult
    {
        private ColorBlockEscapeBlockOutlineGenerationResult(
            bool success,
            Mesh mesh,
            int perimeterSegmentCount,
            int verticalSegmentCount,
            string failureReason)
        {
            Success = success;
            Mesh = mesh;
            PerimeterSegmentCount = perimeterSegmentCount;
            VerticalSegmentCount = verticalSegmentCount;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public Mesh Mesh { get; }
        public int PerimeterSegmentCount { get; }
        public int VerticalSegmentCount { get; }
        public int TotalSegmentCount => PerimeterSegmentCount + VerticalSegmentCount;
        public string FailureReason { get; }

        public static ColorBlockEscapeBlockOutlineGenerationResult Successful(
            Mesh mesh,
            int perimeterSegmentCount,
            int verticalSegmentCount) =>
            new(true, mesh, perimeterSegmentCount, verticalSegmentCount, string.Empty);

        public static ColorBlockEscapeBlockOutlineGenerationResult Failed(string reason) =>
            new(false, null, 0, 0, reason);
    }

    /// <summary>
    /// Builds a dark structural cage around a generated CBE block. The framework remains the
    /// source of boundary topology; this class only converts those facts into CBE view geometry.
    /// </summary>
    public sealed class ColorBlockEscapeBlockOutlineGenerator
    {
        private static readonly int[] BoxTriangles =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            3, 7, 6, 3, 6, 2,
            0, 4, 7, 0, 7, 3,
            1, 2, 6, 1, 6, 5
        };

        /// <summary>
        /// Generates top and bottom perimeter bars plus vertical bars at every footprint turn.
        /// The returned mesh has no collider and must never be used for gameplay queries.
        /// </summary>
        public ColorBlockEscapeBlockOutlineGenerationResult Generate(
            IReadOnlyList<GridCoordinate> offsets,
            GridWorldLayout layout,
            float depth,
            float bevelWidth,
            float outlineWidth,
            float surfaceOffset)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (offsets.Count == 0)
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline requires at least one footprint cell.");
            if (!IsFinitePositive(layout.CellSize.x) ||
                !IsFinitePositive(layout.CellSize.y))
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline cell size must be finite and positive.");
            if (!IsFinitePositive(depth) || !IsFinitePositive(outlineWidth))
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline depth and width must be finite and positive.");
            if (!IsFinite(bevelWidth) || bevelWidth < 0f || bevelWidth * 2f >= depth)
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline bevel must be non-negative and below half the block depth.");
            if (bevelWidth * 2f >= Mathf.Min(layout.CellSize.x, layout.CellSize.y))
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline bevel must stay below half the smallest cell dimension.");
            if (!IsFinite(surfaceOffset) || surfaceOffset < 0f)
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline surface offset must be finite and non-negative.");

            WallGenerationResult boundary;
            try
            {
                boundary = new WallGenerationSystem().Generate(offsets);
            }
            catch (ArgumentException exception)
            {
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(exception.Message);
            }

            if (boundary.HasDiagonalTouch)
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Diagonal-touch footprints do not have an unambiguous unified outline.");

            int verticalCount = 0;
            foreach (BoardBoundaryVertex vertex in boundary.BoundaryVertices)
                if (vertex.Kind == BoardBoundaryVertexKind.Convex ||
                    vertex.Kind == BoardBoundaryVertexKind.Concave)
                    verticalCount++;

            int perimeterCount = boundary.ExposedEdges.Count * 2;
            int segmentCount = perimeterCount + verticalCount;
            List<Vector3> vertices = new(segmentCount * 8);
            List<int> triangles = new(segmentCount * 36);
            float halfDepth = depth * 0.5f;
            float anchorOffset = layout.CellAnchor == GridCellAnchor.Center ? -0.5f : 0f;
            float raisedWidth = outlineWidth + surfaceOffset * 2f;
            Dictionary<GridVertexCoordinate, Vector2> capPoints = new();
            foreach (BoardBoundaryVertex vertex in boundary.BoundaryVertices)
                capPoints.Add(vertex.Coordinate,
                    InsetVertexPosition(vertex, layout, anchorOffset, bevelWidth));

            foreach (BoardBoundaryEdge edge in boundary.ExposedEdges)
            {
                Vector2 start = capPoints[edge.StartVertex];
                Vector2 end = capPoints[edge.EndVertex];
                Vector2 center = (start + end) * 0.5f;
                Vector2 delta = end - start;
                Vector3 size = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? new Vector3(Mathf.Abs(delta.x) + outlineWidth, raisedWidth, raisedWidth)
                    : new Vector3(raisedWidth, Mathf.Abs(delta.y) + outlineWidth, raisedWidth);
                AppendBox(vertices, triangles,
                    new Vector3(center.x, center.y, -halfDepth - surfaceOffset), size);
                AppendBox(vertices, triangles,
                    new Vector3(center.x, center.y, halfDepth + surfaceOffset), size);
            }

            float verticalLength = Mathf.Max(outlineWidth,
                depth - bevelWidth * 2f + outlineWidth + surfaceOffset * 2f);
            foreach (BoardBoundaryVertex vertex in boundary.BoundaryVertices)
            {
                if (vertex.Kind != BoardBoundaryVertexKind.Convex &&
                    vertex.Kind != BoardBoundaryVertexKind.Concave)
                    continue;
                Vector2 point = VertexPosition(vertex.Coordinate, layout, anchorOffset);
                AppendBox(vertices, triangles, new Vector3(point.x, point.y, 0f),
                    new Vector3(raisedWidth, raisedWidth, verticalLength));
            }

            Mesh mesh = new() { name = "CBE generated block outline" };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return ColorBlockEscapeBlockOutlineGenerationResult.Successful(
                mesh, perimeterCount, verticalCount);
        }

        private static Vector2 VertexPosition(
            GridVertexCoordinate vertex,
            GridWorldLayout layout,
            float anchorOffset) => new(
            (vertex.X + anchorOffset) * layout.CellSize.x,
            (vertex.Y + anchorOffset) * layout.CellSize.y);

        private static Vector2 InsetVertexPosition(
            BoardBoundaryVertex vertex,
            GridWorldLayout layout,
            float anchorOffset,
            float bevelWidth)
        {
            Vector2 position = VertexPosition(vertex.Coordinate, layout, anchorOffset);
            if (bevelWidth <= 0f) return position;

            Vector2 direction = vertex.Kind switch
            {
                BoardBoundaryVertexKind.Convex =>
                    QuadrantDirection(vertex.ParticipatingQuadrants),
                BoardBoundaryVertexKind.Concave =>
                    -QuadrantDirection(MissingQuadrant(vertex.ParticipatingQuadrants)),
                _ => StraightInsetDirection(vertex.ParticipatingQuadrants)
            };
            return position + direction * bevelWidth;
        }

        private static Vector2 StraightInsetDirection(BoardVertexQuadrants quadrants)
        {
            if (quadrants == (BoardVertexQuadrants.SouthWest |
                              BoardVertexQuadrants.NorthWest)) return Vector2.left;
            if (quadrants == (BoardVertexQuadrants.SouthEast |
                              BoardVertexQuadrants.NorthEast)) return Vector2.right;
            if (quadrants == (BoardVertexQuadrants.SouthWest |
                              BoardVertexQuadrants.SouthEast)) return Vector2.down;
            return Vector2.up;
        }

        private static BoardVertexQuadrants MissingQuadrant(BoardVertexQuadrants quadrants)
        {
            BoardVertexQuadrants all = BoardVertexQuadrants.SouthWest |
                                       BoardVertexQuadrants.SouthEast |
                                       BoardVertexQuadrants.NorthWest |
                                       BoardVertexQuadrants.NorthEast;
            return all & ~quadrants;
        }

        private static Vector2 QuadrantDirection(BoardVertexQuadrants quadrant) => quadrant switch
        {
            BoardVertexQuadrants.SouthWest => new Vector2(-1f, -1f),
            BoardVertexQuadrants.SouthEast => new Vector2(1f, -1f),
            BoardVertexQuadrants.NorthWest => new Vector2(-1f, 1f),
            BoardVertexQuadrants.NorthEast => new Vector2(1f, 1f),
            _ => Vector2.zero
        };

        private static void AppendBox(
            ICollection<Vector3> vertices,
            ICollection<int> triangles,
            Vector3 center,
            Vector3 size)
        {
            int start = vertices.Count;
            Vector3 half = size * 0.5f;
            vertices.Add(center + new Vector3(-half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, half.z));

            for (int i = 0; i < BoxTriangles.Length; i++)
                triangles.Add(start + BoxTriangles[i]);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
    }
}
