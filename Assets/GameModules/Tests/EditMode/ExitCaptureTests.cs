using System.Collections.Generic;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class ExitCaptureTests
    {
        private static readonly GridWorldLayout Layout = new(Vector3.zero, Vector2.one,
            Vector3.right, Vector3.up, GridCellAnchor.Corner);

        [Test]
        public void MatchingTwoByThreeBlockFitsTwoWideTopExitOnlyAfterOutwardDrag()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("block", new GridCoordinate(2, 3),
                BlockShapePresets.Get(BlockShapePreset.Rectangle2x3), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                2, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            ColorBlockEscapeExitCapture capture = new(level, Layout);
            PlainBlockMovement movement = new(level, Layout, capture);
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.Exits[0].IsBusy);
            Assert.IsTrue(movement.TryBegin("block", out _));
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle,
                "Starting against an exit must not auto-capture.");
            PlainBlockMoveResult result = movement.Move(new Vector3(2f, 4f));
            Assert.IsTrue(result.WasCaptured);
            Assert.AreEqual(BlockLifecycle.Exiting, level.Blocks[0].Lifecycle);
            Assert.IsTrue(level.Exits[0].IsBusy);
            Assert.IsNull(movement.ActiveBlockId);
            Assert.IsFalse(movement.TryBegin("block", out _));
        }

        [Test]
        public void ThreeCellProjectedSpanCannotUseTwoWideSideExit()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("block", new GridCoordinate(4, 1),
                BlockShapePresets.Get(BlockShapePreset.Rectangle2x3), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Right, new GridCoordinate(5, 1),
                2, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            PlainBlockMovement movement = new(level, Layout,
                new ColorBlockEscapeExitCapture(level, Layout));
            Assert.IsTrue(movement.TryBegin("block", out _));
            Assert.IsFalse(movement.Move(new Vector3(5f, 1f)).WasCaptured);
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.Exits[0].IsBusy);
        }

        [Test]
        public void WrongColorAndInwardSamplesCannotCapture()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("block", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                1, ColorIdentity.Slot1).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            ColorBlockEscapeExitCapture capture = new(level, Layout);
            Assert.IsFalse(capture.TryCapture(level.Blocks[0], new Vector2(2f, 6f),
                new Vector2(2f, 5f)).Captured);
            PlainBlockMovement movement = new(level, Layout, capture);
            Assert.IsTrue(movement.TryBegin("block", out _));
            Assert.IsFalse(movement.Move(new Vector3(2f, 6f)).WasCaptured);
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.Exits[0].IsBusy);
        }

        [Test]
        public void OverlapThresholdIsTunableAndDoesNotReplaceFullFit()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("block", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Bar2), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(3, 5),
                2, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            PlainBlockMovement movement = new(level, Layout,
                new ColorBlockEscapeExitCapture(level, Layout, new ExitCaptureSettings(0.70f)));
            Assert.IsTrue(movement.TryBegin("block", out _));
            Assert.IsFalse(movement.Move(new Vector3(2.2f, 6f)).WasCaptured);
            Assert.IsTrue(movement.Move(new Vector3(2.5f, 6f)).WasCaptured);
            Assert.AreEqual(new GridCoordinate(3, 5), level.Blocks[0].CommittedOrigin);

            ColorBlockEscapeRuntimeLevel strictLevel = Build(authored);
            PlainBlockMovement strict = new(strictLevel, Layout,
                new ColorBlockEscapeExitCapture(strictLevel, Layout, new ExitCaptureSettings(0.80f)));
            Assert.IsTrue(strict.TryBegin("block", out _));
            Assert.IsFalse(strict.Move(new Vector3(2.5f, 6f)).WasCaptured);
        }

        [Test]
        public void BlockedFullAlignmentPathRejectsCaptureWithoutChangingOccupancy()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.SetCell(new GridCoordinate(2, 4), AuthoredCellState.Blocked).Success);
            Assert.IsTrue(authored.PlaceBlock("block", new GridCoordinate(2, 3),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                1, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            PlainBlockMovement movement = new(level, Layout,
                new ColorBlockEscapeExitCapture(level, Layout,
                    new ExitCaptureSettings(captureDistanceCells: 2.1f)));
            Assert.IsTrue(movement.TryBegin("block", out _));
            Assert.IsFalse(movement.Move(new Vector3(2f, 7f)).WasCaptured);
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 3)));
            Assert.IsFalse(level.Exits[0].IsBusy);
        }

        [Test]
        public void BusyExitRejectsSecondBlockUntilFirstFullyLeaves()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("first", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PlaceBlock("second", new GridCoordinate(2, 3),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                1, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            ColorBlockEscapeExitCapture capture = new(level, Layout,
                new ExitCaptureSettings(captureDistanceCells: 2.1f,
                    outwardSpeedCellsPerSecond: 1f));
            PlainBlockMovement movement = new(level, Layout, capture);
            Assert.IsTrue(movement.TryBegin("first", out _));
            Assert.IsTrue(movement.Move(new Vector3(2f, 6f)).WasCaptured);
            Assert.IsTrue(movement.TryBegin("second", out _));
            Assert.IsFalse(movement.Move(new Vector3(2f, 7f)).WasCaptured);
            Assert.IsTrue(level.Exits[0].IsBusy);
            capture.Advance(0.5f);
            Assert.IsTrue(level.Exits[0].IsBusy);
            capture.Advance(0.5f);
            Assert.AreEqual(BlockLifecycle.Removed, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.Exits[0].IsBusy);
            Assert.IsTrue(movement.Move(new Vector3(2f, 7f)).WasCaptured);
        }

        [Test]
        public void IrregularExitTravelReservesRemainingCorridorThenOnlyReleasesCells()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("leaving", new GridCoordinate(2, 4),
                new[] { new GridCoordinate(0, 0), new GridCoordinate(1, 0),
                    new GridCoordinate(0, 1) }, ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PlaceBlock("waiting", new GridCoordinate(1, 4),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot1).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                2, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            ColorBlockEscapeExitCapture capture = new(level, Layout,
                new ExitCaptureSettings(outwardSpeedCellsPerSecond: 1f));
            PlainBlockMovement movement = new(level, Layout, capture);
            Assert.IsTrue(movement.TryBegin("leaving", out _));
            Assert.IsTrue(movement.Move(new Vector3(2f, 5f)).WasCaptured);
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(3, 5)),
                "The irregular trailing arm's future cell must be reserved at acceptance.");
            HashSet<GridCoordinate> atAcceptance = new(
                level.FrameworkContext.CellOccupancySystem.OccupiedCoordinates);
            capture.Advance(0.5f);
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(3, 5)),
                "The trailing irregular slice now physically reaches its reserved cell.");
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 4)));
            Assert.IsTrue(new HashSet<GridCoordinate>(level.FrameworkContext.CellOccupancySystem
                .OccupiedCoordinates).IsSubsetOf(atAcceptance));
            capture.Advance(0.5f);
            Assert.IsFalse(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 4)));
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 5)));
            Assert.IsTrue(new HashSet<GridCoordinate>(level.FrameworkContext.CellOccupancySystem
                .OccupiedCoordinates).IsSubsetOf(atAcceptance));
            Assert.IsTrue(level.Exits[0].IsBusy);
            Assert.IsTrue(movement.TryBegin("waiting", out _));
            Assert.IsFalse(movement.Move(new Vector3(2f, 4f)).WasCaptured);
            Assert.AreEqual(new Vector3(2f, 4f), movement.End().WorldPosition);
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 4)));
            capture.Advance(1f);
            Assert.AreEqual(BlockLifecycle.Removed, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(2, 5)));
            Assert.IsFalse(level.Exits[0].IsBusy);
        }

        [Test]
        public void ObstacleInOutboundCorridorRejectsIrregularCapture()
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.PlaceBlock("leaving", new GridCoordinate(2, 4),
                new[] { new GridCoordinate(0, 0), new GridCoordinate(1, 0),
                    new GridCoordinate(0, 1) }, ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PlaceBlock("obstacle", new GridCoordinate(3, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot1).Success);
            Assert.IsTrue(authored.PutExit("exit", ExitSide.Top, new GridCoordinate(2, 5),
                2, ColorIdentity.Slot0).Success);
            ColorBlockEscapeRuntimeLevel level = Build(authored);
            PlainBlockMovement movement = new(level, Layout,
                new ColorBlockEscapeExitCapture(level, Layout));
            Assert.IsTrue(movement.TryBegin("leaving", out _));
            Assert.IsFalse(movement.Move(new Vector3(2f, 5f)).WasCaptured);
            Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
            Assert.IsFalse(level.Exits[0].IsBusy);
        }

        private static ColorBlockEscapeRuntimeLevel Build(ColorBlockEscapeAuthoringSession authored)
        {
            Assert.IsTrue(authored.TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel level,
                out string failure), failure);
            return level;
        }
    }
}
