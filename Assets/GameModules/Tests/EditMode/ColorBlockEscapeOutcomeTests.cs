using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeOutcomeTests
    {
        private static readonly GridWorldLayout Layout = new(Vector3.zero, Vector2.one,
            Vector3.right, Vector3.up, GridCellAnchor.Corner);

        [Test]
        public void CountdownStartsFromAuthoredDurationAndAdvancesNormally()
        {
            ColorBlockEscapeOutcomeSession outcome = new(Build(12f));
            Assert.AreEqual(GameState.Playing, outcome.State);
            Assert.AreEqual(TimerStatus.Running, outcome.TimerStatus);
            Assert.AreEqual(12f, outcome.RemainingSeconds);
            outcome.AdvanceTime(2.5f);
            outcome.ResolveBoundary();
            Assert.AreEqual(9.5f, outcome.RemainingSeconds, 0.0001f);
            Assert.AreEqual(GameState.Playing, outcome.State);
        }

        [Test]
        public void ExpiryLosesWhenAnyBlockHasNotBeenAccepted()
        {
            ColorBlockEscapeRuntimeLevel level = Build(5f, twoBlocks: true);
            ColorBlockEscapeOutcomeSession outcome = new(level);
            Capture(level, "first");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Playing, outcome.State);
            outcome.AdvanceTime(5f);
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Lost, outcome.State);
            Assert.AreEqual(0f, outcome.RemainingSeconds);
            Capture(level, "second");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Lost, outcome.State);
        }

        [Test]
        public void AllAcceptedBlocksCompleteBeforeOutwardPresentationFinishes()
        {
            ColorBlockEscapeRuntimeLevel level = Build(5f, twoBlocks: true);
            ColorBlockEscapeOutcomeSession outcome = new(level);
            Capture(level, "first");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Playing, outcome.State);
            Capture(level, "second");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Won, outcome.State);
            Assert.AreEqual(TimerStatus.Stopped, outcome.TimerStatus);
            Assert.AreEqual(BlockLifecycle.Exiting, level.Blocks[0].Lifecycle);
            Assert.AreEqual(BlockLifecycle.Exiting, level.Blocks[1].Lifecycle);
        }

        [Test]
        public void FinalAcceptanceBeforeExpiryFreezesWinAndRemainingTime()
        {
            ColorBlockEscapeRuntimeLevel level = Build(5f);
            ColorBlockEscapeOutcomeSession outcome = new(level);
            outcome.AdvanceTime(4.5f);
            outcome.ResolveBoundary();
            Capture(level, "first");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Won, outcome.State);
            Assert.AreEqual(0.5f, outcome.RemainingSeconds, 0.0001f);
            outcome.AdvanceTime(10f);
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Won, outcome.State);
            Assert.AreEqual(0.5f, outcome.RemainingSeconds, 0.0001f);
        }

        [Test]
        public void ExactExpiryBoundaryResolvesFinalAcceptanceBeforeLoss()
        {
            ColorBlockEscapeRuntimeLevel level = Build(5f);
            ColorBlockEscapeOutcomeSession outcome = new(level);
            outcome.AdvanceTime(5f);
            Assert.AreEqual(TimerStatus.Expired, outcome.TimerStatus);
            Assert.AreEqual(GameState.Playing, outcome.State);
            Capture(level, "first");
            outcome.ResolveBoundary();
            Assert.AreEqual(GameState.Won, outcome.State);
            Assert.AreEqual(TimerStatus.Stopped, outcome.TimerStatus);
        }

        [Test]
        public void OvershootExpiresBeforeAPlayerCanSubmitSameFrameInput()
        {
            ColorBlockEscapeOutcomeSession outcome = new(Build(5f));
            outcome.AdvanceTime(5.1f);
            Assert.AreEqual(GameState.Lost, outcome.State);
        }

        [Test]
        public void FreshLevelSessionRestoresFullTimerAndUnacceptedBlocks()
        {
            ColorBlockEscapeAuthoringSession authored = Author(5f);
            ColorBlockEscapeRuntimeLevel first = Build(authored);
            ColorBlockEscapeOutcomeSession oldSession = new(first);
            oldSession.AdvanceTime(5f);
            oldSession.ResolveBoundary();
            Assert.AreEqual(GameState.Lost, oldSession.State);

            ColorBlockEscapeRuntimeLevel restarted = Build(authored);
            ColorBlockEscapeOutcomeSession freshSession = new(restarted);
            Assert.AreNotSame(first, restarted);
            Assert.AreEqual(GameState.Playing, freshSession.State);
            Assert.AreEqual(TimerStatus.Running, freshSession.TimerStatus);
            Assert.AreEqual(5f, freshSession.RemainingSeconds);
            Assert.AreEqual(BlockLifecycle.OnBoard, restarted.Blocks[0].Lifecycle);
            Assert.IsFalse(restarted.Exits[0].IsBusy);
        }

        private static ColorBlockEscapeRuntimeLevel Build(float duration, bool twoBlocks = false) =>
            Build(Author(duration, twoBlocks));

        private static ColorBlockEscapeAuthoringSession Author(float duration, bool twoBlocks = false)
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.SetTimer(duration, 0f).Success);
            Assert.IsTrue(authored.PlaceBlock("first", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("first-exit", ExitSide.Top,
                new GridCoordinate(2, 5), 1, ColorIdentity.Slot0).Success);
            if (!twoBlocks) return authored;
            Assert.IsTrue(authored.PlaceBlock("second", new GridCoordinate(4, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot1).Success);
            Assert.IsTrue(authored.PutExit("second-exit", ExitSide.Top,
                new GridCoordinate(4, 5), 1, ColorIdentity.Slot1).Success);
            return authored;
        }

        private static ColorBlockEscapeRuntimeLevel Build(ColorBlockEscapeAuthoringSession authored)
        {
            Assert.IsTrue(authored.TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel level,
                out string failure), failure);
            return level;
        }

        private static void Capture(ColorBlockEscapeRuntimeLevel level, string blockId)
        {
            Assert.IsTrue(level.TryGetBlock(blockId, out BlockRuntimeState block));
            ColorBlockEscapeExitCapture capture = new(level, Layout);
            Assert.IsTrue(capture.TryCapture(block, new Vector2(block.CommittedOrigin.X,
                block.CommittedOrigin.Y), new Vector2(block.CommittedOrigin.X,
                block.CommittedOrigin.Y + 1f)).Captured);
        }
    }
}
