using System.Collections.Generic;
using ColorBlockEscape.Runtime;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;

namespace ColorBlockEscape.Tests
{
    public sealed class LevelConstructionTests
    {
        [Test]
        public void InteriorInactiveHoleIsNotAnExitButExteriorConnectedNotchIs()
        {
            LevelAuthoringCore core = new(5, 5);
            Assert.IsTrue(core.TrySetCellState(new GridCoordinate(2, 2), AuthoredCellState.Inactive).Success);
            ExitDefinition exit = MakeExit("exit", ExitSide.Top, 2, 1, 1);
            Assert.IsFalse(ExitBoundaryValidator.TryValidate(BuildGrid(core.CreateBoardSnapshot()),
                new[] { exit }, out string enclosedFailure));
            StringAssert.Contains("exterior", enclosedFailure);

            Assert.IsTrue(core.TrySetCellState(new GridCoordinate(2, 3), AuthoredCellState.Inactive).Success);
            Assert.IsTrue(core.TrySetCellState(new GridCoordinate(2, 4), AuthoredCellState.Inactive).Success);
            Assert.IsTrue(ExitBoundaryValidator.TryValidate(BuildGrid(core.CreateBoardSnapshot()),
                new[] { exit }, out string connectedFailure), connectedFailure);
        }

        [Test]
        public void BlockedNeighborAndOverlappingExitEdgesAreRejected()
        {
            LevelAuthoringCore core = new(4, 4);
            core.TrySetCellState(new GridCoordinate(1, 2), AuthoredCellState.Blocked);
            Assert.IsFalse(ExitBoundaryValidator.TryValidate(BuildGrid(core.CreateBoardSnapshot()),
                new[] { MakeExit("bad", ExitSide.Top, 1, 1, 1) }, out _));

            ExitDefinition first = MakeExit("one", ExitSide.Top, 1, 3, 2);
            ExitDefinition second = MakeExit("two", ExitSide.Top, 2, 3, 1);
            Assert.IsFalse(ExitBoundaryValidator.TryValidate(BuildGrid(core.CreateBoardSnapshot()),
                new[] { first, second }, out string overlap));
            StringAssert.Contains("overlaps", overlap);
        }

        [Test]
        public void CodecRejectsDuplicateIdsAndDisconnectedFootprints()
        {
            ColorBlockEscapeLevelData payload = MakePayload();
            payload.Exits[0].Id = payload.Blocks[0].Id;
            Assert.IsFalse(ColorBlockEscapeLevelCodec.TryValidateSchema(payload, out string duplicate));
            StringAssert.Contains("Duplicate", duplicate);

            payload.Exits[0].Id = "exit";
            payload.Blocks[0].FootprintOffsets.Add(Cell(2, 0));
            Assert.IsFalse(ColorBlockEscapeLevelCodec.TryValidateSchema(payload, out string disconnected));
            StringAssert.Contains("disconnected", disconnected);
        }

        [Test]
        public void BuilderPublishesOnlyValidBoardBlockAndExitState()
        {
            ColorBlockEscapeLevelData payload = MakePayload();
            LevelDefinition definition = MakeLevel(4, 4, payload);
            ColorBlockEscapeRuntimeBuilder builder = new();
            Assert.IsTrue(builder.TryBuild(definition, out ColorBlockEscapeRuntimeLevel level,
                out string failure), failure);
            Assert.AreEqual(1, level.Blocks.Count);
            Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(new GridCoordinate(1, 1)));
            Assert.AreEqual(1, level.Exits.Count);

            payload.Blocks.Add(MakeBlock("second", 1, 1));
            definition = MakeLevel(4, 4, payload);
            Assert.IsFalse(builder.TryBuild(definition, out ColorBlockEscapeRuntimeLevel rejected,
                out string overlap));
            Assert.IsNull(rejected);
            StringAssert.Contains("overlaps", overlap);
        }

        [Test]
        public void PayloadRoundTripPreservesIdentityColorAndExplicitOffsets()
        {
            ColorBlockEscapeLevelData payload = MakePayload();
            payload.Blocks[0].FootprintOffsets.Add(Cell(1, 0));
            payload.Exits[0].Width = 2;
            Assert.IsTrue(ColorBlockEscapeLevelCodec.TryEncode(payload,
                out string json, out string encodeFailure), encodeFailure);
            LevelDefinition envelope = MakeLevel(4, 4, payload);
            envelope.ContentPayload.PayloadJson = json;
            Assert.IsTrue(ColorBlockEscapeLevelCodec.TryDecode(envelope,
                out ColorBlockEscapeLevelData loaded, out string decodeFailure), decodeFailure);
            Assert.AreEqual("block", loaded.Blocks[0].Id);
            Assert.AreEqual(ColorIdentity.Slot0, loaded.Blocks[0].Color);
            Assert.AreEqual(2, loaded.Blocks[0].FootprintOffsets.Count);
            Assert.AreEqual(1, loaded.Blocks[0].FootprintOffsets[1].X);
            Assert.AreEqual(2, loaded.Exits[0].Width);
        }

        [Test]
        public void BuilderRejectsIncompleteExplicitBoardAndInvalidTimer()
        {
            LevelDefinition definition = MakeLevel(4, 4, MakePayload());
            definition.FrameworkData.Board.Cells.RemoveAt(0);
            ColorBlockEscapeRuntimeBuilder builder = new();
            Assert.IsFalse(builder.TryBuild(definition, out _, out string boardFailure));
            StringAssert.Contains("explicit", boardFailure);

            definition = MakeLevel(4, 4, MakePayload());
            definition.FrameworkData.Timer.DurationSeconds = float.NaN;
            Assert.IsFalse(builder.TryBuild(definition, out _, out string timerFailure));
            StringAssert.Contains("countdown", timerFailure);
        }

        private static LevelDefinition MakeLevel(int width, int height,
            ColorBlockEscapeLevelData payload)
        {
            Assert.IsTrue(ColorBlockEscapeLevelCodec.TryEncode(payload,
                out string json, out string error), error);
            LevelAuthoringCore core = new(width, height);
            core.SetMetadata("test-level", "Test", 1);
            core.SetTimer(true, TimerMode.Countdown, 60f, 10f);
            return core.CreateLevelSnapshot(ColorBlockEscapeLevelCodec.ContentTypeId, json);
        }

        private static ColorBlockEscapeLevelData MakePayload() => new()
        {
            Version = ColorBlockEscapeLevelCodec.CurrentVersion,
            Blocks = new List<BlockDefinition> { MakeBlock("block", 1, 1) },
            Exits = new List<ExitDefinition> { MakeExit("exit", ExitSide.Top, 1, 3, 1) }
        };

        private static BlockDefinition MakeBlock(string id, int x, int y) => new()
        {
            Id = id,
            Color = ColorIdentity.Slot0,
            Origin = Cell(x, y),
            FootprintOffsets = new List<CellCoordinateData> { Cell(0, 0) }
        };

        private static ExitDefinition MakeExit(string id, ExitSide side, int x, int y, int width) => new()
        {
            Id = id,
            Side = side,
            StartCell = Cell(x, y),
            Width = width,
            Color = ColorIdentity.Slot0
        };

        private static CellCoordinateData Cell(int x, int y) => new() { X = x, Y = y };

        private static GridBoard BuildGrid(BoardDefinitionData data)
        {
            List<GridCoordinate> structural = new();
            List<GridCoordinate> blocked = new();
            foreach (CellDefinitionData cell in data.Cells)
            {
                GridCoordinate coordinate = new(cell.Coordinate.X, cell.Coordinate.Y);
                if (cell.CellState != AuthoredCellState.Inactive) structural.Add(coordinate);
                if (cell.CellState == AuthoredCellState.Blocked) blocked.Add(coordinate);
            }
            return new GridBoard(data.Width, data.Height, structural, blocked);
        }
    }
}
