using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>Disposable CBE preview geometry composed from shared board and boundary facts.</summary>
    public static class ColorBlockEscapeAuthoringView
    {
        private static readonly Dictionary<Color, Material> Materials = new();
        private static readonly Color[] Palette =
        {
            new(0.25f, 0.55f, 0.98f), new(0.97f, 0.38f, 0.30f),
            new(0.31f, 0.78f, 0.43f), new(0.95f, 0.79f, 0.24f),
            new(0.71f, 0.42f, 0.89f), new(0.98f, 0.56f, 0.20f),
            new(0.24f, 0.78f, 0.78f), new(0.91f, 0.38f, 0.65f),
            new(0.61f, 0.65f, 0.72f), new(0.67f, 0.85f, 0.33f)
        };

        public static Dictionary<string, Transform> Build(ColorBlockEscapeAuthoringSession model,
            ColorBlockEscapeRuntimeLevel runtime, GridWorldLayout layout, Transform root,
            ModularBoardCellView boardCellPrefab = null)
        {
            LevelAuthoringCore core = model.Core;
            bool modularBoardBuilt = TryBuildModularBoard(model, layout, root, boardCellPrefab);
            foreach (CellDefinitionData cell in core.CreateBoardSnapshot().Cells)
            {
                if (cell.CellState == AuthoredCellState.Inactive) continue;
                GridCoordinate coordinate = new(cell.Coordinate.X, cell.Coordinate.Y);
                if (modularBoardBuilt && cell.CellState == AuthoredCellState.Active) continue;
                Color color = cell.CellState == AuthoredCellState.Blocked
                    ? new Color(0.20f, 0.23f, 0.29f) : new Color(0.74f, 0.77f, 0.82f);
                Cube($"Cell {coordinate}", layout.CellCenterToWorld(coordinate) + Vector3.forward * 0.16f,
                    new Vector3(0.96f, 0.96f, 0.12f), color, root);
            }

            if (!modularBoardBuilt)
            {
                WallGenerationResult boundary = core.CreateBoundary(
                    state => state == AuthoredCellState.Active);
                foreach (BoardBoundaryEdge edge in boundary.ExposedEdges)
                {
                    if (IsOpening(model.Exits, edge)) continue;
                    Vector3 center = layout.CellCenterToWorld(edge.CellCoordinate);
                    bool horizontal = edge.Direction == BoardEdgeDirection.North ||
                                      edge.Direction == BoardEdgeDirection.South;
                    Vector3 position = center + (horizontal
                        ? Vector3.up * (edge.Direction == BoardEdgeDirection.North ? 0.5f : -0.5f)
                        : Vector3.right * (edge.Direction == BoardEdgeDirection.East ? 0.5f : -0.5f));
                    Cube("Boundary", position + Vector3.back * 0.03f,
                        horizontal ? new Vector3(1f, 0.09f, 0.2f) : new Vector3(0.09f, 1f, 0.2f),
                        new Color(0.37f, 0.43f, 0.53f), root);
                }
            }

            foreach (ExitDefinition exit in model.Exits)
                for (int index = 0; index < exit.Width; index++)
                {
                    GridCoordinate cell = ExitBoundaryValidator.EdgeCellAt(exit, index);
                    Vector3 center = layout.CellCenterToWorld(cell);
                    bool horizontal = exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom;
                    center += horizontal
                        ? Vector3.up * (exit.Side == ExitSide.Top ? 0.57f : -0.57f)
                        : Vector3.right * (exit.Side == ExitSide.Right ? 0.57f : -0.57f);
                    Cube($"Exit {exit.Id}", center + Vector3.back * 0.10f,
                        horizontal ? new Vector3(0.92f, 0.18f, 0.22f) : new Vector3(0.18f, 0.92f, 0.22f),
                        Tint(exit.Color), root);
                }

            Dictionary<string, Transform> views = new();
            if (runtime != null)
            {
                foreach (BlockRuntimeState block in runtime.Blocks)
                    views.Add(block.Id, BlockView(block.Id, block.CommittedOrigin,
                        block.Footprint.Offsets, block.Color, layout, root));
            }
            else
            {
                foreach (AuthoredFootprint block in core.Items)
                {
                    model.TryGetBlockColor(block.Id, out ColorIdentity color);
                    views.Add(block.Id, BlockView(block.Id, block.Origin,
                        block.Offsets, color, layout, root));
                }
            }
            return views;
        }

        private static bool TryBuildModularBoard(ColorBlockEscapeAuthoringSession model,
            GridWorldLayout layout, Transform root, ModularBoardCellView boardCellPrefab)
        {
            if (boardCellPrefab == null) return false;
            WallGenerationResult boundary = model.Core.CreateBoundary(
                state => state == AuthoredCellState.Active);
            ModularBoardVisualPlan plan = new ModularBoardVisualPlanner().CreatePlan(boundary);
            GameObject boardRoot = new("Modular board visuals");
            boardRoot.transform.SetParent(root, false);
            ModularBoardVisualBuilder builder = new();
            if (!builder.TryRebuild(plan, boardCellPrefab, boardRoot.transform, layout, 0f,
                    out IReadOnlyList<ModularBoardCellView> cells, out string failure))
            {
                if (Application.isPlaying) Object.Destroy(boardRoot);
                else Object.DestroyImmediate(boardRoot);
                Debug.LogWarning($"CBE modular board visual fallback: {failure}");
                return false;
            }

            Dictionary<GridCoordinate, ModularBoardCellView> byCoordinate = new();
            foreach (ModularBoardCellView cell in cells) byCoordinate[cell.Coordinate] = cell;
            foreach (ExitDefinition exit in model.Exits)
                for (int index = 0; index < exit.Width; index++)
                {
                    GridCoordinate coordinate = ExitBoundaryValidator.EdgeCellAt(exit, index);
                    if (byCoordinate.TryGetValue(coordinate, out ModularBoardCellView cell))
                        cell.SetBoundaryEdgeVisible(Direction(exit.Side), false);
                }
            return true;
        }

        private static BoardEdgeDirection Direction(ExitSide side) => side switch
        {
            ExitSide.Top => BoardEdgeDirection.North,
            ExitSide.Bottom => BoardEdgeDirection.South,
            ExitSide.Left => BoardEdgeDirection.West,
            _ => BoardEdgeDirection.East
        };

        public static Color Tint(ColorIdentity color) =>
            Palette[Mathf.Clamp((int)color - (int)ColorIdentity.Slot0, 0, 9)];

        /// <summary>Shows every proposed opening segment, including invalid width overflow.</summary>
        public static void BuildExitPreview(ExitSide side, GridCoordinate start, int width,
            ColorIdentity color, bool valid, GridWorldLayout layout, Transform root)
        {
            Color tint = valid ? Tint(color) : new Color(0.96f, 0.18f, 0.17f);
            for (int index = 0; index < width; index++)
            {
                GridCoordinate cell = side == ExitSide.Top || side == ExitSide.Bottom
                    ? start.Offset(index, 0) : start.Offset(0, index);
                Vector3 center = layout.CellCenterToWorld(cell);
                bool horizontal = side == ExitSide.Top || side == ExitSide.Bottom;
                center += horizontal
                    ? Vector3.up * (side == ExitSide.Top ? 0.5f : -0.5f)
                    : Vector3.right * (side == ExitSide.Right ? 0.5f : -0.5f);
                Cube("Exit candidate", center + Vector3.back * 0.35f,
                    horizontal ? new Vector3(0.94f, 0.13f, 0.12f) : new Vector3(0.13f, 0.94f, 0.12f),
                    tint, root);
            }
        }

        private static Transform BlockView(string id, GridCoordinate origin,
            IReadOnlyList<GridCoordinate> offsets, ColorIdentity color,
            GridWorldLayout layout, Transform parent)
        {
            GameObject block = new(id);
            block.transform.SetParent(parent, false);
            block.transform.position = layout.GridToWorldPosition(origin) + Vector3.back * 0.2f;
            foreach (GridCoordinate offset in offsets)
                Cube("Block cell", new Vector3(offset.X + 0.5f, offset.Y + 0.5f, 0f),
                    new Vector3(0.94f, 0.94f, 0.24f), Tint(color), block.transform, true);
            return block.transform;
        }

        private static bool IsOpening(IReadOnlyList<ExitDefinition> exits, BoardBoundaryEdge edge)
        {
            ExitSide side = edge.Direction switch
            {
                BoardEdgeDirection.North => ExitSide.Top,
                BoardEdgeDirection.South => ExitSide.Bottom,
                BoardEdgeDirection.West => ExitSide.Left,
                _ => ExitSide.Right
            };
            foreach (ExitDefinition exit in exits)
                if (exit.Side == side)
                    for (int index = 0; index < exit.Width; index++)
                        if (ExitBoundaryValidator.EdgeCellAt(exit, index) == edge.CellCoordinate)
                            return true;
            return false;
        }

        private static void Cube(string name, Vector3 position, Vector3 scale,
            Color color, Transform parent, bool local = false)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Collider collider = cube.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
            cube.transform.SetParent(parent, false);
            if (local) cube.transform.localPosition = position;
            else cube.transform.position = position;
            cube.transform.localScale = scale;
            if (!Materials.TryGetValue(color, out Material material) || material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                material = new Material(shader) { color = color };
                Materials[color] = material;
            }
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
