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
            ColorBlockEscapeBlockMeshPresentation blockMeshPresentation,
            ModularBoardCellView boardCellPrefab = null)
        {
            if (model == null) throw new System.ArgumentNullException(nameof(model));
            if (blockMeshPresentation == null)
                throw new System.ArgumentNullException(nameof(blockMeshPresentation));
            LevelAuthoringCore core = model.Core;
            List<ExitViewData> exits = new(model.Exits.Count);
            foreach (ExitDefinition exit in model.Exits) exits.Add(new ExitViewData(exit));
            BuildEnvironment(core.CreateBoardSnapshot(), exits, layout, root, boardCellPrefab);

            Dictionary<string, Transform> views = new();
            if (runtime != null)
            {
                foreach (BlockRuntimeState block in runtime.Blocks)
                    views.Add(block.Id, BlockView(block.Id, block.CommittedOrigin,
                        block.Footprint.Offsets, block.Color, layout, root,
                        blockMeshPresentation));
            }
            else
            {
                foreach (AuthoredFootprint block in core.Items)
                {
                    model.TryGetBlockColor(block.Id, out ColorIdentity color);
                    views.Add(block.Id, BlockView(block.Id, block.Origin,
                        block.Offsets, color, layout, root, blockMeshPresentation));
                }
            }
            return views;
        }

        /// <summary>Builds gameplay views directly from validated level/runtime data.</summary>
        public static Dictionary<string, Transform> BuildRuntime(LevelDefinition definition,
            ColorBlockEscapeRuntimeLevel runtime, GridWorldLayout layout, Transform root,
            ColorBlockEscapeBlockMeshPresentation blockMeshPresentation,
            ModularBoardCellView boardCellPrefab = null)
        {
            if (definition?.FrameworkData?.Board == null)
                throw new System.ArgumentNullException(nameof(definition));
            if (runtime == null) throw new System.ArgumentNullException(nameof(runtime));
            if (blockMeshPresentation == null)
                throw new System.ArgumentNullException(nameof(blockMeshPresentation));

            List<ExitViewData> exits = new(runtime.Exits.Count);
            foreach (ExitRuntimeState exit in runtime.Exits) exits.Add(new ExitViewData(exit));
            BuildEnvironment(definition.FrameworkData.Board, exits, layout, root, boardCellPrefab);
            Dictionary<string, Transform> views = new();
            foreach (BlockRuntimeState block in runtime.Blocks)
                views.Add(block.Id, BlockView(block.Id, block.CommittedOrigin,
                    block.Footprint.Offsets, block.Color, layout, root,
                    blockMeshPresentation));
            return views;
        }

        private static void BuildEnvironment(BoardDefinitionData board,
            IReadOnlyList<ExitViewData> exits, GridWorldLayout layout, Transform root,
            ModularBoardCellView boardCellPrefab)
        {
            WallGenerationResult boundary = CreateActiveBoundary(board);
            bool modularBoardBuilt = TryBuildModularBoard(
                boundary, exits, layout, root, boardCellPrefab);
            foreach (CellDefinitionData cell in board.Cells)
            {
                if (cell.CellState == AuthoredCellState.Inactive) continue;
                GridCoordinate coordinate = new(cell.Coordinate.X, cell.Coordinate.Y);
                if (modularBoardBuilt && cell.CellState == AuthoredCellState.Active) continue;
                Color color = cell.CellState == AuthoredCellState.Blocked
                    ? new Color(0.20f, 0.23f, 0.29f) : new Color(0.74f, 0.77f, 0.82f);
                Cube($"Cell {coordinate}", layout.CellCenterToWorld(coordinate) +
                    Vector3.forward * 0.16f, new Vector3(0.96f, 0.96f, 0.12f), color, root);
            }

            if (!modularBoardBuilt)
                foreach (BoardBoundaryEdge edge in boundary.ExposedEdges)
                {
                    if (IsOpening(exits, edge)) continue;
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

            foreach (ExitViewData exit in exits)
                for (int index = 0; index < exit.Width; index++)
                {
                    GridCoordinate cell = ExitCellAt(exit, index);
                    Vector3 center = layout.CellCenterToWorld(cell);
                    bool horizontal = exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom;
                    center += horizontal
                        ? Vector3.up * (exit.Side == ExitSide.Top ? 0.57f : -0.57f)
                        : Vector3.right * (exit.Side == ExitSide.Right ? 0.57f : -0.57f);
                    Cube($"Exit {exit.Id}", center + Vector3.back * 0.10f,
                        horizontal ? new Vector3(0.92f, 0.18f, 0.22f) :
                            new Vector3(0.18f, 0.92f, 0.22f), Tint(exit.Color), root);
                }
        }

        private static bool TryBuildModularBoard(WallGenerationResult boundary,
            IReadOnlyList<ExitViewData> exits, GridWorldLayout layout, Transform root,
            ModularBoardCellView boardCellPrefab)
        {
            if (boardCellPrefab == null) return false;
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
            foreach (ExitViewData exit in exits)
                for (int index = 0; index < exit.Width; index++)
                {
                    GridCoordinate coordinate = ExitCellAt(exit, index);
                    if (byCoordinate.TryGetValue(coordinate, out ModularBoardCellView cell))
                        cell.SetBoundaryEdgeVisible(Direction(exit.Side), false);
                }
            return true;
        }

        private static WallGenerationResult CreateActiveBoundary(BoardDefinitionData board)
        {
            List<GridCoordinate> active = new();
            foreach (CellDefinitionData cell in board.Cells)
                if (cell.CellState == AuthoredCellState.Active)
                    active.Add(new GridCoordinate(cell.Coordinate.X, cell.Coordinate.Y));
            return new WallGenerationSystem().Generate(active);
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
            GridWorldLayout layout, Transform parent,
            ColorBlockEscapeBlockMeshPresentation blockMeshPresentation)
        {
            GameObject block = new(id);
            block.transform.SetParent(parent, false);
            block.transform.position = layout.GridToWorldPosition(origin) + Vector3.back * 0.2f;
            FootprintMeshGenerationResult generated =
                blockMeshPresentation.GetOrCreate(offsets, layout);
            if (!generated.Success)
            {
                Debug.LogWarning(
                    $"Block '{id}' uses fallback cells because unified mesh generation failed: " +
                    generated.FailureReason);
                Color fallbackTint = Tint(color);
                float anchorOffset = layout.CellAnchor == GridCellAnchor.Corner ? 0.5f : 0f;
                for (int i = 0; i < offsets.Count; i++)
                {
                    GridCoordinate offset = offsets[i];
                    Cube("Block fallback cell",
                        new Vector3((offset.X + anchorOffset) * layout.CellSize.x,
                            (offset.Y + anchorOffset) * layout.CellSize.y, 0f),
                        new Vector3(layout.CellSize.x * 0.94f,
                            layout.CellSize.y * 0.94f, blockMeshPresentation.Depth),
                        fallbackTint, block.transform, true);
                }
                return block.transform;
            }

            MeshFilter filter = block.AddComponent<MeshFilter>();
            filter.sharedMesh = generated.Mesh;
            MeshRenderer renderer = block.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(Tint(color));
            return block.transform;
        }

        private static bool IsOpening(IReadOnlyList<ExitViewData> exits, BoardBoundaryEdge edge)
        {
            ExitSide side = edge.Direction switch
            {
                BoardEdgeDirection.North => ExitSide.Top,
                BoardEdgeDirection.South => ExitSide.Bottom,
                BoardEdgeDirection.West => ExitSide.Left,
                _ => ExitSide.Right
            };
            foreach (ExitViewData exit in exits)
                if (exit.Side == side)
                    for (int index = 0; index < exit.Width; index++)
                        if (ExitCellAt(exit, index) == edge.CellCoordinate)
                            return true;
            return false;
        }

        private static GridCoordinate ExitCellAt(ExitViewData exit, int index) =>
            exit.Side == ExitSide.Top || exit.Side == ExitSide.Bottom
                ? exit.StartCell.Offset(index, 0)
                : exit.StartCell.Offset(0, index);

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
            cube.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
        }

        private static Material MaterialFor(Color color)
        {
            if (Materials.TryGetValue(color, out Material material) && material != null)
                return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { color = color };
            Materials[color] = material;
            return material;
        }

        private readonly struct ExitViewData
        {
            public ExitViewData(ExitDefinition exit)
            {
                Id = exit.Id;
                Side = exit.Side;
                StartCell = new GridCoordinate(exit.StartCell.X, exit.StartCell.Y);
                Width = exit.Width;
                Color = exit.Color;
            }

            public ExitViewData(ExitRuntimeState exit)
            {
                Id = exit.Id;
                Side = exit.Side;
                StartCell = exit.StartCell;
                Width = exit.Width;
                Color = exit.Color;
            }

            public string Id { get; }
            public ExitSide Side { get; }
            public GridCoordinate StartCell { get; }
            public int Width { get; }
            public ColorIdentity Color { get; }
        }
    }
}
