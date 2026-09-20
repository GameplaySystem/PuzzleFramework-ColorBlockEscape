using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// Small playable fixture for the on-board movement checkpoint. It is intentionally
    /// independent of exit, timer and chipper behavior, and can be replaced by the level
    /// editor/runtime scene once those later checkpoints exist.
    /// </summary>
    public sealed class PlainMovementCheckpointBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            LevelAuthoringCore authoring = new(7, 6);
            authoring.SetMetadata("plain-movement-checkpoint", "Plain movement", 1);
            authoring.SetTimer(true, TimerMode.Countdown, 60f, 10f);
            authoring.TrySetCellState(new GridCoordinate(3, 2), AuthoredCellState.Blocked);
            authoring.TrySetCellState(new GridCoordinate(0, 4), AuthoredCellState.Inactive);
            authoring.TrySetCellState(new GridCoordinate(1, 4), AuthoredCellState.Inactive);
            authoring.TrySetCellState(new GridCoordinate(0, 5), AuthoredCellState.Inactive);

            ColorBlockEscapeLevelData payload = new()
            {
                Version = ColorBlockEscapeLevelCodec.CurrentVersion,
                Blocks = new List<BlockDefinition>
                {
                    Block("L block", ColorIdentity.Slot0, 1, 1, (0, 0), (1, 0), (0, 1)),
                    Block("bar block", ColorIdentity.Slot1, 4, 2, (0, 0), (1, 0))
                },
                Exits = new List<ExitDefinition>
                {
                    new() { Id = "future-exit", Side = ExitSide.Top,
                        StartCell = Cell(6, 5), Width = 1, Color = ColorIdentity.Slot0 }
                }
            };
            if (!ColorBlockEscapeLevelCodec.TryEncode(payload, out string json, out string failure) ||
                !new ColorBlockEscapeRuntimeBuilder().TryBuild(
                    authoring.CreateLevelSnapshot(ColorBlockEscapeLevelCodec.ContentTypeId, json),
                    out ColorBlockEscapeRuntimeLevel level, out failure))
            {
                Debug.LogError($"Movement checkpoint could not build: {failure}", this);
                return;
            }

            GridWorldLayout layout = new(Vector3.zero, Vector2.one);
            BuildBoard(authoring.CreateBoardSnapshot(), layout);
            Dictionary<string, Transform> views = BuildBlocks(level, layout);
            Camera sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                Debug.LogError("Movement checkpoint scene needs a Main Camera.", this);
                return;
            }
            gameObject.AddComponent<PlainBlockDragAdapter>()
                .Initialize(level, layout, sceneCamera, views);
        }

        private void BuildBoard(BoardDefinitionData board, GridWorldLayout layout)
        {
            foreach (CellDefinitionData cell in board.Cells)
            {
                if (cell.CellState == AuthoredCellState.Inactive) continue;
                Vector3 center = layout.BoardLocalToWorld(new Vector2(
                    cell.Coordinate.X + 0.5f, cell.Coordinate.Y + 0.5f));
                Color color = cell.CellState == AuthoredCellState.Blocked
                    ? new Color(0.2f, 0.22f, 0.28f)
                    : new Color(0.73f, 0.76f, 0.82f);
                CreateCube($"Cell {cell.Coordinate.X},{cell.Coordinate.Y}",
                    center + Vector3.forward * 0.15f,
                    new Vector3(0.96f, 0.96f, 0.12f), color, transform);
            }
        }

        private Dictionary<string, Transform> BuildBlocks(
            ColorBlockEscapeRuntimeLevel level, GridWorldLayout layout)
        {
            Dictionary<string, Transform> views = new();
            foreach (BlockRuntimeState block in level.Blocks)
            {
                GameObject root = new(block.Id);
                root.transform.SetParent(transform, false);
                root.transform.position = layout.GridToWorldPosition(block.CommittedOrigin) +
                                          Vector3.back * 0.2f;
                Color color = block.Color == ColorIdentity.Slot0
                    ? new Color(0.22f, 0.5f, 0.95f)
                    : new Color(0.94f, 0.38f, 0.28f);
                foreach (GridCoordinate offset in block.Footprint.Offsets)
                    CreateCube("Footprint cell", new Vector3(offset.X + 0.5f, offset.Y + 0.5f),
                        new Vector3(0.94f, 0.94f, 0.23f), color, root.transform, true);
                views.Add(block.Id, root.transform);
            }
            return views;
        }

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale,
            Color color, Transform parent, bool localPosition = false)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            if (localPosition) cube.transform.localPosition = position;
            else cube.transform.position = position;
            cube.transform.localScale = scale;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new(shader);
            material.color = color;
            cube.GetComponent<Renderer>().material = material;
            return cube;
        }

        private static BlockDefinition Block(string id, ColorIdentity color,
            int x, int y, params (int x, int y)[] offsets)
        {
            List<CellCoordinateData> cells = new();
            foreach ((int dx, int dy) in offsets) cells.Add(Cell(dx, dy));
            return new BlockDefinition { Id = id, Color = color,
                Origin = Cell(x, y), FootprintOffsets = cells };
        }

        private static CellCoordinateData Cell(int x, int y) => new() { X = x, Y = y };
    }
}
