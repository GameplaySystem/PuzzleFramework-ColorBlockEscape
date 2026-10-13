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
    /// Builds a dark structural surface outline for a generated CBE block. The framework remains the
    /// source of boundary topology; this class only converts those facts into CBE view geometry.
    /// </summary>
    public sealed class ColorBlockEscapeBlockOutlineGenerator
    {
        private static readonly int[] DoubleSidedQuadTriangles =
        {
            0, 1, 2, 0, 2, 3,
            2, 1, 0, 3, 2, 0
        };

        /// <summary>
        /// Generates flush top/bottom perimeter ribbons and wrapped side ribbons at every turn.
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
            if ((bevelWidth + outlineWidth) * 2f >=
                Mathf.Min(layout.CellSize.x, layout.CellSize.y))
                return ColorBlockEscapeBlockOutlineGenerationResult.Failed(
                    "Block outline width collapses the smallest cap corridor.");
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
            int renderedQuadCount = perimeterCount + verticalCount * 2;
            List<Vector3> vertices = new(renderedQuadCount * 4);
            List<int> triangles = new(renderedQuadCount * 12);
            float halfDepth = depth * 0.5f;
            float anchorOffset = layout.CellAnchor == GridCellAnchor.Center ? -0.5f : 0f;
            Dictionary<GridVertexCoordinate, Vector2> outerCapPoints = new();
            Dictionary<GridVertexCoordinate, Vector2> innerCapPoints = new();
            HashSet<GridVertexCoordinate> turningVertices = new();
            foreach (BoardBoundaryVertex vertex in boundary.BoundaryVertices)
            {
                outerCapPoints.Add(vertex.Coordinate,
                    InsetVertexPosition(vertex, layout, anchorOffset, bevelWidth));
                innerCapPoints.Add(vertex.Coordinate,
                    InsetVertexPosition(vertex, layout, anchorOffset,
                        bevelWidth + outlineWidth));
                if (vertex.Kind == BoardBoundaryVertexKind.Convex ||
                    vertex.Kind == BoardBoundaryVertexKind.Concave)
                    turningVertices.Add(vertex.Coordinate);
            }

            foreach (BoardBoundaryEdge edge in boundary.ExposedEdges)
            {
                Vector2 outerStart = outerCapPoints[edge.StartVertex];
                Vector2 outerEnd = outerCapPoints[edge.EndVertex];
                Vector2 innerStart = innerCapPoints[edge.StartVertex];
                Vector2 innerEnd = innerCapPoints[edge.EndVertex];
                AppendDoubleSidedQuad(vertices, triangles,
                    AtDepth(outerStart, -halfDepth - surfaceOffset),
                    AtDepth(outerEnd, -halfDepth - surfaceOffset),
                    AtDepth(innerEnd, -halfDepth - surfaceOffset),
                    AtDepth(innerStart, -halfDepth - surfaceOffset));
                AppendDoubleSidedQuad(vertices, triangles,
                    AtDepth(outerStart, halfDepth + surfaceOffset),
                    AtDepth(innerStart, halfDepth + surfaceOffset),
                    AtDepth(innerEnd, halfDepth + surfaceOffset),
                    AtDepth(outerEnd, halfDepth + surfaceOffset));

                if (turningVertices.Contains(edge.StartVertex))
                    AppendSideRibbon(vertices, triangles, edge, edge.StartVertex,
                        edge.EndVertex, layout, anchorOffset, halfDepth, bevelWidth,
                        outlineWidth, surfaceOffset);
                if (turningVertices.Contains(edge.EndVertex))
                    AppendSideRibbon(vertices, triangles, edge, edge.EndVertex,
                        edge.StartVertex, layout, anchorOffset, halfDepth, bevelWidth,
                        outlineWidth, surfaceOffset);
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

        private static void AppendSideRibbon(
            ICollection<Vector3> vertices,
            ICollection<int> triangles,
            BoardBoundaryEdge edge,
            GridVertexCoordinate cornerVertex,
            GridVertexCoordinate interiorVertex,
            GridWorldLayout layout,
            float anchorOffset,
            float halfDepth,
            float bevelWidth,
            float outlineWidth,
            float surfaceOffset)
        {
            Vector2 corner = VertexPosition(cornerVertex, layout, anchorOffset);
            Vector2 towardInterior =
                (VertexPosition(interiorVertex, layout, anchorOffset) - corner).normalized;
            Vector2 outward = edge.Direction switch
            {
                BoardEdgeDirection.North => Vector2.up,
                BoardEdgeDirection.East => Vector2.right,
                BoardEdgeDirection.South => Vector2.down,
                _ => Vector2.left
            };
            corner += outward * surfaceOffset;
            Vector2 inner = corner + towardInterior * outlineWidth;
            float top = -halfDepth + bevelWidth;
            float bottom = halfDepth - bevelWidth;
            AppendDoubleSidedQuad(vertices, triangles,
                AtDepth(corner, top), AtDepth(inner, top),
                AtDepth(inner, bottom), AtDepth(corner, bottom));
        }

        private static Vector3 AtDepth(Vector2 point, float depth) =>
            new(point.x, point.y, depth);

        private static void AppendDoubleSidedQuad(
            ICollection<Vector3> vertices,
            ICollection<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth)
        {
            int start = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            for (int i = 0; i < DoubleSidedQuadTriangles.Length; i++)
                triangles.Add(start + DoubleSidedQuadTriangles[i]);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
    }
}
