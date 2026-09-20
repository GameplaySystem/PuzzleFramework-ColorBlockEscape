using System.Collections.Generic;
using ColorBlockEscape.Runtime;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class PlainBlockMovementTests
    {
        private static readonly GridWorldLayout Layout = new(Vector3.zero, Vector2.one);

        [Test]
        public void DragUsesSubcellPositionsAndReleaseTransfersWholeFootprint()
        {
            ColorBlockEscapeRuntimeLevel level = Build(new[] { Block("moving", 1, 1, (0, 0), (1, 0)) });
            PlainBlockMovement movement = new(level, Layout);
            Assert.IsTrue(movement.TryBegin("moving", out Vector3 start));
            Assert.AreEqual(1f, start.x);

            PlainBlockMoveResult preview = movement.Move(new Vector3(1.35f, 1f));
            Assert.IsFalse(preview.WasBlocked, preview.FailureReason);
            Assert.That(preview.WorldPosition.x, Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(level.Blocks[0].ContinuousOrigin.x, Is.EqualTo(1.35f).Within(0.0001f));
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(1, 1)));

            movement.Move(new Vector3(1.8f, 1f));
            PlainBlockMoveResult released = movement.End();
            Assert.IsFalse(released.WasBlocked, released.FailureReason);
            Assert.AreEqual(new GridCoordinate(2, 1), level.Blocks[0].CommittedOrigin);
            Assert.AreEqual(new Vector3(2f, 1f), released.WorldPosition);
            Assert.IsFalse(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(1, 1)));
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 1)));
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(3, 1)));
        }

        [Test]
        public void FullFootprintStaysInsideBoardBounds()
        {
            ColorBlockEscapeRuntimeLevel level = Build(new[] { Block("moving", 1, 1,
                (0, 0), (1, 0), (0, 1), (1, 1)) });
            PlainBlockMovement movement = new(level, Layout);
            movement.TryBegin("moving", out _);
            PlainBlockMoveResult left = movement.Move(new Vector3(-100f, 1f));
            Assert.IsTrue(left.WasBlocked);
            Assert.That(left.WorldPosition.x, Is.EqualTo(0f).Within(0.0001f));
            PlainBlockMoveResult right = movement.Move(new Vector3(100f, 1f));
            Assert.IsTrue(right.WasBlocked);
            Assert.That(right.WorldPosition.x, Is.EqualTo(5f).Within(0.0001f));
            Assert.IsFalse(movement.End().WasBlocked);
        }

        [Test]
        public void FastDragStopsAtBlockedAndInactiveCellsRatherThanTunneling()
        {
            LevelAuthoringCore core = Authoring();
            core.TrySetCellState(new GridCoordinate(4, 1), AuthoredCellState.Blocked);
            core.TrySetCellState(new GridCoordinate(2, 3), AuthoredCellState.Inactive);
            ColorBlockEscapeRuntimeLevel level = Build(new[] { Block("moving", 1, 1, (0, 0), (1, 0)) }, core);
            PlainBlockMovement movement = new(level, Layout);
            movement.TryBegin("moving", out _);

            PlainBlockMoveResult horizontal = movement.Move(new Vector3(6f, 1f));
            Assert.IsTrue(horizontal.WasBlocked);
            Assert.That(horizontal.WorldPosition.x, Is.GreaterThan(1.9f));
            Assert.That(horizontal.WorldPosition.x, Is.LessThanOrEqualTo(2.001f));
            PlainBlockMoveResult vertical = movement.Move(new Vector3(horizontal.WorldPosition.x, 5f));
            Assert.IsTrue(vertical.WasBlocked);
            Assert.That(vertical.WorldPosition.y, Is.LessThan(3f));
            PlainBlockMoveResult release = movement.End();
            Assert.AreEqual(Layout.GridToWorldPosition(level.Blocks[0].CommittedOrigin), release.WorldPosition);
        }

        [Test]
        public void AnotherBlockStopsTheSweepAndKeepsItsOccupancy()
        {
            ColorBlockEscapeRuntimeLevel level = Build(new[]
            {
                Block("moving", 1, 1, (0, 0), (1, 0)),
                Block("stationary", 4, 1, (0, 0))
            });
            PlainBlockMovement movement = new(level, Layout);
            movement.TryBegin("moving", out _);
            PlainBlockMoveResult result = movement.Move(new Vector3(5f, 1f));
            Assert.IsTrue(result.WasBlocked);
            Assert.That(result.WorldPosition.x, Is.LessThanOrEqualTo(2.001f));
            movement.End();
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(4, 1)));
        }

        [Test]
        public void IrregularFootprintGapRemainsOpenButItsActualCellsCollide()
        {
            LevelAuthoringCore core = Authoring();
            core.TrySetCellState(new GridCoordinate(2, 2), AuthoredCellState.Blocked);
            ColorBlockEscapeRuntimeLevel level = Build(new[]
            {
                Block("l", 1, 1, (0, 0), (1, 0), (0, 1))
            }, core);
            PlainBlockMovement movement = new(level, Layout);
            Assert.IsTrue(movement.TryBegin("l", out _));
            PlainBlockMoveResult left = movement.Move(new Vector3(0.7f, 1f));
            Assert.IsFalse(left.WasBlocked, left.FailureReason);
            PlainBlockMoveResult right = movement.Move(new Vector3(1.6f, 1f));
            Assert.IsTrue(right.WasBlocked);
            Assert.That(right.WorldPosition.x, Is.LessThanOrEqualTo(1.001f));
            Assert.AreEqual(new Vector3(1f, 1f), movement.End().WorldPosition);
        }

        [Test]
        public void PointerAdapterPreservesGrabOffsetAndShowsContinuousViewMotion()
        {
            ColorBlockEscapeRuntimeLevel level = Build(new[] { Block("moving", 1, 1, (0, 0)) });
            GameObject cameraObject = new("Test camera");
            GameObject adapterObject = new("Test adapter");
            GameObject viewObject = new("Test block view");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 4f;
                camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                cameraObject.transform.position = new Vector3(3f, 3f, -10f);
                viewObject.transform.position = new Vector3(1f, 1f, -0.2f);
                PlainBlockDragAdapter adapter = adapterObject.AddComponent<PlainBlockDragAdapter>();
                adapter.Initialize(level, Layout, camera,
                    new Dictionary<string, Transform> { ["moving"] = viewObject.transform });

                Vector2 grab = camera.WorldToScreenPoint(new Vector3(1.4f, 1.5f));
                adapter.ProcessPointerSample(0, grab, true, true, false);
                Vector2 moved = camera.WorldToScreenPoint(new Vector3(1.75f, 1.5f));
                adapter.ProcessPointerSample(0, moved, false, true, false);
                Assert.That(viewObject.transform.position.x, Is.EqualTo(1.35f).Within(0.001f));
                Vector2 released = camera.WorldToScreenPoint(new Vector3(2.2f, 1.5f));
                adapter.ProcessPointerSample(0, released, false, false, true);
                Assert.That(viewObject.transform.position.x, Is.EqualTo(2f).Within(0.001f));
                Assert.AreEqual(new GridCoordinate(2, 1), level.Blocks[0].CommittedOrigin);
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
                Object.DestroyImmediate(adapterObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static LevelAuthoringCore Authoring()
        {
            LevelAuthoringCore core = new(7, 6);
            core.SetMetadata("movement-test", "Movement test", 1);
            core.SetTimer(true, TimerMode.Countdown, 60f, 10f);
            return core;
        }

        private static ColorBlockEscapeRuntimeLevel Build(IReadOnlyList<BlockDefinition> blocks,
            LevelAuthoringCore core = null)
        {
            core ??= Authoring();
            ColorBlockEscapeLevelData payload = new()
            {
                Version = ColorBlockEscapeLevelCodec.CurrentVersion,
                Blocks = new List<BlockDefinition>(blocks),
                Exits = new List<ExitDefinition>
                {
                    new() { Id = "unused-exit", Side = ExitSide.Top,
                        StartCell = Cell(0, 5), Width = 1, Color = ColorIdentity.Slot0 }
                }
            };
            Assert.IsTrue(ColorBlockEscapeLevelCodec.TryEncode(payload, out string json,
                out string encodeFailure), encodeFailure);
            LevelDefinition definition = core.CreateLevelSnapshot(ColorBlockEscapeLevelCodec.ContentTypeId, json);
            Assert.IsTrue(new ColorBlockEscapeRuntimeBuilder().TryBuild(definition,
                out ColorBlockEscapeRuntimeLevel level, out string failure), failure);
            return level;
        }

        private static BlockDefinition Block(string id, int x, int y, params (int x, int y)[] offsets)
        {
            List<CellCoordinateData> cells = new();
            foreach ((int dx, int dy) in offsets) cells.Add(Cell(dx, dy));
            return new BlockDefinition { Id = id, Origin = Cell(x, y),
                Color = ColorIdentity.Slot0, FootprintOffsets = cells };
        }

        private static CellCoordinateData Cell(int x, int y) => new() { X = x, Y = y };
    }
}
